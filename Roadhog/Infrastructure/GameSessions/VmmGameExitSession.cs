using Roadhog.Application.GameSessions;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Infrastructure.Input;
using Roadhog.Infrastructure.Login;
using Roadhog.Infrastructure.Login.Native;
using Roadhog.Infrastructure.Vmm;

namespace Roadhog.Infrastructure.GameSessions;

internal sealed class VmmGameExitSession(AionVmmGameApi api, KmBoxNetKeyboardInput input,
    AccountConfig account, IRoadhogLogger? logger = null) : IGameExitSession
{
    private readonly GameExitSnapshotPublisher _publication = new(new(account.AccountName, 0, "", account.VmmDeviceName, true), logger);
    private readonly GameExitOperationExecutor _reads = new();
    private readonly GameExitOperationExecutor _inputs = new(TimeSpan.FromSeconds(5), () => ReleaseDeviceAsync(input, logger));
    private readonly SemaphoreSlim _actions = new(1, 1);
    private bool _inputUsed;
    private bool _disposed;
    private DateTimeOffset _nextReadTimeoutLog;

    public async Task<GameExitSnapshot> ReadAsync(CancellationToken token)
    {
        while (true)
        {
            var observed = await CaptureAsync(token).ConfigureAwait(false);
            if (observed != null) return observed.Snapshot;
            await Task.Delay(250, token).ConfigureAwait(false);
        }
    }

    private async Task<GameExitObservation?> CaptureAsync(CancellationToken token, Action<GameExitObservation>? bind = null)
    {
        try
        {
            return await _reads.RunAsync(readToken => _publication.Capture(lifecycle => api.WithLoginConnection(account.VmmDeviceName,
                vmm => GameExitNativeReader.Capture(vmm, account, lifecycle)), bind, readToken), "退出页面读取", token).ConfigureAwait(false);
        }
        catch (TimeoutException error)
        {
            token.ThrowIfCancellationRequested();
            var now = DateTimeOffset.UtcNow;
            if (now >= _nextReadTimeoutLog)
            {
                _nextReadTimeoutLog = now.AddSeconds(10);
                logger?.Warn("game_exit.read_draining", new Dictionary<string, object?>
                { ["account"] = account.AccountName, ["error"] = error.Message });
            }
            return _publication.ReadPublished(token);
        }
    }

    private async Task<GameExitObservation?> BindAsync(CancellationToken token)
    {
        GameExitObservation? bound = null;
        await CaptureAsync(token, value => bound = value).ConfigureAwait(false);
        return bound;
    }

    public async Task<bool> ExecuteAsync(GameExitSnapshot expected, GameExitAction action, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _actions.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var current = await BindAsync(token).ConfigureAwait(false);
            if (current == null || current.Snapshot.Session != expected.Session
                || current.Snapshot.ProcessId != expected.ProcessId || !Permits(current, action)
                || action != GameExitAction.Cancel && current.Snapshot != expected) return false;
            await LoginWindowFocus.EnsureAsync(checked((uint)current.Snapshot.ProcessId),
                () => ForegroundAsync(current.SessionPid, token), async () =>
                {
                    var next = await BindAsync(token).ConfigureAwait(false);
                    if (!SameTarget(current, next, action)) throw new InvalidOperationException("退出窗口已改变，取消本次输入。");
                }, HotkeyAsync, t => Task.Delay(250, t), null, token).ConfigureAwait(false);
            current = await BindAsync(token).ConfigureAwait(false);
            if (current == null || !Permits(current, action) || current.Snapshot.Session != expected.Session
                || action != GameExitAction.Cancel && current.Snapshot.CurrentHp < expected.CurrentHp) return false;
            if (action == GameExitAction.OpenMenu)
            {
                if (await ForegroundAsync(current.SessionPid, token).ConfigureAwait(false) != current.Snapshot.ProcessId) return false;
                Check(await InputAsync(t => input.PressKeyAsync("O", TimeSpan.FromMilliseconds(60), t), token).ConfigureAwait(false));
            }
            else
            {
                var point = Target(current, action);
                await MoveAsync(current.SessionPid, point, token).ConfigureAwait(false);
                var verified = await BindAsync(token).ConfigureAwait(false);
                if (!SameTarget(current, verified, action)
                    || await ForegroundAsync(current.SessionPid, token).ConfigureAwait(false) != current.Snapshot.ProcessId) return false;
                Check(await InputAsync(t => input.MouseDownAsync(RoadhogMouseButton.Left, t), token).ConfigureAwait(false));
                try { await Task.Delay(70, token).ConfigureAwait(false); }
                finally
                {
                    using var release = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    var executor = new LoginOperationExecutor(TimeSpan.FromSeconds(3));
                    Check(await executor.RunTaskAsync(t => input.MouseUpAsync(RoadhogMouseButton.Left, t), "退出点击释放", release.Token).ConfigureAwait(false));
                }
            }
            logger?.Info("game_exit.input", new Dictionary<string, object?>
            { ["account"] = account.AccountName, ["action"] = action.ToString(), ["gamePid"] = current.Snapshot.ProcessId,
                ["menuName"] = current.Ui.MenuName, ["countdownName"] = current.Ui.CountdownName });
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or TimeoutException || LoginReadErrors.IsRetryable(error))
        {
            await ReleaseAsync().ConfigureAwait(false);
            logger?.Warn("game_exit.input_retry", new Dictionary<string, object?>
            { ["account"] = account.AccountName, ["action"] = action.ToString(), ["error"] = error.Message });
            return false;
        }
        finally { _actions.Release(); }
    }

    internal static bool Permits(GameExitObservation value, GameExitAction action) => value.Snapshot.ProcessPresent
        && !value.Ui.OtherModalOpen && (action switch
        {
            GameExitAction.OpenMenu => !value.Ui.MenuOpen && !value.Ui.CountdownOpen && !value.Snapshot.IsDead,
            GameExitAction.Quit => value.Ui.MenuOpen && !value.Ui.CountdownOpen && value.Ui.Quit != null && !value.Snapshot.IsDead,
            GameExitAction.Cancel => value.Ui.CountdownOpen && value.Ui.Cancel != null,
            _ => false
        });

    internal static bool SameTarget(GameExitObservation expected, GameExitObservation? current, GameExitAction action) =>
        current != null && Permits(current, action) && current.Snapshot.Session == expected.Snapshot.Session
        && current.Snapshot.ProcessId == expected.Snapshot.ProcessId && current.SessionPid == expected.SessionPid
        && current.Window == expected.Window && current.Bounds == expected.Bounds
        && (action == GameExitAction.Cancel || current.Snapshot.CurrentHp >= expected.Snapshot.CurrentHp)
        && current.Ui.MenuAddress == expected.Ui.MenuAddress && current.Ui.CountdownAddress == expected.Ui.CountdownAddress
        && (action == GameExitAction.OpenMenu || Target(current, action) == Target(expected, action));

    private static Point Target(GameExitObservation observation, GameExitAction action)
    {
        var relative = (action == GameExitAction.Quit ? observation.Ui.Quit : observation.Ui.Cancel)
            ?? throw new InvalidOperationException("退出控件尚未准备好。");
        var point = new Point(observation.Bounds.Left + relative.X, observation.Bounds.Top + relative.Y);
        if (!observation.Bounds.Contains(point)) throw new InvalidOperationException("退出按钮不在目标窗口内。");
        return point;
    }

    private Task<uint> ForegroundAsync(uint sessionPid, CancellationToken token) =>
        ReadNativeAsync(vmm => SessionInputProbe.Foreground(vmm, sessionPid).GetProperty("Pid").GetUInt32(), token);

    private Task<T> ReadNativeAsync<T>(Func<Vmmsharp.Vmm, T> read, CancellationToken token) =>
        _reads.RunAsync(_ => api.WithLoginConnection(account.VmmDeviceName, read), "退出输入验证", token);

    private Task<OperationResult> InputAsync(Func<CancellationToken, Task<OperationResult>> action, CancellationToken token)
    {
        _inputUsed = true;
        return _inputs.RunTaskAsync(action, "退出设备输入", token);
    }

    private async Task HotkeyAsync(Keys[] keys, CancellationToken token) =>
        Check(await InputAsync(t => input.LoginHotkeyAsync(keys, t), token).ConfigureAwait(false));

    private async Task MoveAsync(uint sessionPid, Point target, CancellationToken token)
    {
        if (target.X < 0 || target.Y < 0 || target.X > 16384 || target.Y > 16384)
            throw new InvalidOperationException("退出按钮超出桌面坐标范围。");
        for (var i = 0; i < 16; i++)
        {
            var cursor = await ReadNativeAsync(vmm =>
            {
                var kernel = SessionInputProbe.SessionProcess(vmm, sessionPid);
                var bytes = LoginNativeReader.Exact(kernel, SessionSymbolResolver.Resolve(vmm, sessionPid).Cursor, 8);
                return new Point(BitConverter.ToInt32(bytes), BitConverter.ToInt32(bytes, 4));
            }, token).ConfigureAwait(false);
            var dx = target.X - cursor.X; var dy = target.Y - cursor.Y;
            if (Math.Abs(dx) <= 2 && Math.Abs(dy) <= 2) return;
            Check(await InputAsync(t => input.MoveMouseRelativeAsync(dx, dy, t), token).ConfigureAwait(false));
            await Task.Delay(120, token).ConfigureAwait(false);
        }
        throw new InvalidOperationException("鼠标未到达退出按钮。");
    }

    private static void Check(OperationResult result)
    {
        if (!result.Success) throw new InvalidOperationException(result.Error);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _publication.Dispose();
        _reads.Close(() => { });
        _inputs.Close(() => { });
        await ReleaseAsync().ConfigureAwait(false);
    }

    private async Task ReleaseAsync()
    {
        if (_inputUsed) await ReleaseDeviceAsync(input, logger).ConfigureAwait(false);
    }

    private static async Task ReleaseDeviceAsync(KmBoxNetKeyboardInput input, IRoadhogLogger? logger)
    {
        var release = new LoginOperationExecutor(TimeSpan.FromSeconds(3));
        try { await release.RunTaskAsync(input.ReleaseAllAsync, "退出模块释放输入", CancellationToken.None).ConfigureAwait(false); }
        catch (Exception error) { logger?.Warn("game_exit.release_failed", new Dictionary<string, object?> { ["error"] = error.Message }); }
    }
}
