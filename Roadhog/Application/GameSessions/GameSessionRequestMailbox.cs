using Roadhog.Core.Common;

namespace Roadhog.Application.GameSessions;

public enum GameSessionOperation { Exit, Relogin }
public sealed record GameSessionRequest(GameSessionOperation Operation)
{
    public Guid Id { get; } = Guid.NewGuid();
    public GameExitController Exit { get; } = new();
}

/// <summary>Transient account command, discarded by Stop; suitable for UI or a future scheduler.</summary>
public sealed class GameSessionRequestMailbox
{
    private readonly object _sync = new();
    private GameSessionRequest? _current;
    private GameSessionRequest? _completed;
    private Guid? _scriptRestartRequestId;
    public Guid? ScriptRestartRequestId { get { lock (_sync) return _scriptRestartRequestId; } }
    // Restart the worker only; do not send game exit input or manufacture a shop completion.
    public Guid RequestScriptRestart()
    {
        lock (_sync) return _scriptRestartRequestId ??= Guid.NewGuid();
    }
    public GameSessionRequest? Current { get { lock (_sync) return _current; } }
    public GameSessionRequest? Completed { get { lock (_sync) return _completed; } }
    public OperationResult Request(GameSessionOperation operation)
    {
        lock (_sync)
        {
            if (_current is not null || _completed is not null) return OperationResult.Fail("退出 / 重登任务已在等待或执行。");
            _current = new(operation);
            return OperationResult.Ok();
        }
    }
    public void Complete(GameSessionRequest request)
    {
        lock (_sync)
        {
            if (!ReferenceEquals(_current, request)) throw new InvalidOperationException("退出请求已改变。");
            _completed = request; _current = null;
        }
    }
}
