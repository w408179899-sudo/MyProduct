namespace Roadhog.Application.GameSessions;

public enum GameExitAction { OpenMenu, Quit, Cancel }

public sealed record GameExitSnapshot(string Session, int ProcessId, bool ProcessPresent,
    bool MenuOpen, bool CountdownOpen, bool CanQuit, bool CanCancel, uint CurrentHp, bool IsDead);

public interface IGameExitSession : IAsyncDisposable
{
    Task<GameExitSnapshot> ReadAsync(CancellationToken token);
    // Rebind process, foreground and control geometry before every physical action.
    Task<bool> ExecuteAsync(GameExitSnapshot expected, GameExitAction action, CancellationToken token);
}

public sealed record GameExitTick(bool Completed, bool YieldToCombat, string Message);

/// <summary>One normal game-exit attempt series. Login and scheduling are separate callers.</summary>
public sealed class GameExitController(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private DateTimeOffset? _submittedAt;
    private DateTimeOffset _nextAction;
    private uint? _lastHp;
    private bool _prepared;
    private bool _cancelling;

    public static async Task CancelAsync(IGameExitSession session, CancellationToken token)
    {
        while (true)
        {
            var scene = await session.ReadAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!scene.ProcessPresent || !scene.CountdownOpen) return;
            if (scene.CanCancel) await session.ExecuteAsync(scene, GameExitAction.Cancel, token).ConfigureAwait(false);
            await Task.Delay(150, token).ConfigureAwait(false);
        }
    }

    public async Task<GameExitTick> TickAsync(IGameExitSession session, Func<Task<bool>> prepare,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var scene = await session.ReadAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!scene.ProcessPresent) return new(true, false, "游戏已退出");
        var now = _time.GetUtcNow();
        var damaged = _lastHp.HasValue && scene.CurrentHp < _lastHp.Value;
        _lastHp = scene.CurrentHp;
        if (_prepared && (damaged || scene.IsDead)) _cancelling = true;
        if (_submittedAt.HasValue)
        {
            var elapsed = now - _submittedAt.Value;
            // A closed countdown can precede process teardown by several seconds. Keep
            // observing the process; do not reopen O or read combat during that interval.
            if (scene.CountdownOpen && elapsed >= TimeSpan.FromSeconds(12) || elapsed >= TimeSpan.FromSeconds(30))
                _cancelling = true;
        }
        // An existing countdown must be cancelled before yielding input to combat.
        if (_cancelling || !_prepared && scene.CountdownOpen)
        {
            if (scene.CountdownOpen)
            {
                _cancelling = true;
                if (scene.CanCancel && now >= _nextAction)
                {
                    await session.ExecuteAsync(scene, GameExitAction.Cancel, token).ConfigureAwait(false);
                    _nextAction = now.AddMilliseconds(500);
                }
                return new(false, false, "取消本次退出，等待处理战斗后重试");
            }
            _prepared = false; _cancelling = false; _submittedAt = null;
            return new(false, true, "退出尚未完成，先处理战斗 / 复活后重试");
        }
        if (!_prepared)
        {
            var ready = await prepare().ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!ready)
                return new(false, true, "重新登录已排队，先处理当前战斗 / 复活");
            _prepared = true;
            return new(false, false, "准备退出游戏");
        }
        if (scene.CountdownOpen || _submittedAt.HasValue)
        {
            _submittedAt ??= now;
            return new(false, false, "等待游戏退出（12 秒观察窗口）");
        }
        if (now < _nextAction) return new(false, false, "等待退出菜单响应");
        if (scene.MenuOpen)
        {
            if (scene.CanQuit && await session.ExecuteAsync(scene, GameExitAction.Quit, token).ConfigureAwait(false))
                _submittedAt = _time.GetUtcNow();
        }
        else await session.ExecuteAsync(scene, GameExitAction.OpenMenu, token).ConfigureAwait(false);
        _nextAction = now.AddMilliseconds(700);
        return new(false, false, "正在退出游戏");
    }
}
