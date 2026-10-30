using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Infrastructure.Login;
using Roadhog.Infrastructure.Vmm;

namespace Roadhog.Infrastructure.GameSessions;

// A process/window/control scene is atomic: geometry must not merge across sessions.
internal sealed class GameExitSnapshotPublisher(GameApiReadContext context, IRoadhogLogger? logger = null) : IDisposable
{
    private readonly DmaStableSnapshotStore _store = new(AionVmmSnapshotChannels.Registry);
    private readonly DmaSnapshotReadCoordinator _coordinator = new();
    private readonly object _commit = new();
    private bool _disposed;
    private string? _session;
    private string? _lastFailure;
    private DateTimeOffset _nextFailureLog;

    internal GameExitObservation? Capture(Func<Action<string>, GameExitObservation> capture,
        Action<GameExitObservation>? bindInput = null, CancellationToken token = default) =>
        _coordinator.Execute(context.AccountName, AionVmmSnapshotChannels.GameExit, () =>
        {
            try
            {
                lock (_commit) Check(token);
                var value = capture(key =>
                {
                    lock (_commit)
                    {
                        Check(token);
                        if (key == _session) return;
                        if (_session != null) _store.ClearSession(_session);
                        _session = key;
                    }
                });
                lock (_commit)
                {
                    Check(token);
                    if (_session == null || value.Snapshot.Session != _session)
                        throw new InvalidOperationException("Exit capture did not establish the matching process lifecycle.");
                    _store.Resolve(_session, AionVmmSnapshotChannels.GameExit, context,
                        OperationResult<GameExitObservation>.Ok(value), DateTimeOffset.UtcNow);
                    _lastFailure = null;
                    // Held publications never bind input. Only this validated capture can do so.
                    bindInput?.Invoke(value);
                    return value;
                }
            }
            catch (Exception error) when (error is InvalidDataException || LoginReadErrors.IsRetryable(error))
            {
                lock (_commit)
                {
                    Check(token);
                    var failure = error.GetType().Name + ":" + error.Message;
                    var now = DateTimeOffset.UtcNow;
                    if (_lastFailure != failure || now >= _nextFailureLog)
                    {
                        _lastFailure = failure; _nextFailureLog = now.AddSeconds(10);
                        logger?.Warn("game_exit.read_retry", new Dictionary<string, object?>
                        { ["account"] = context.AccountName, ["device"] = context.VmmDeviceName, ["error"] = failure });
                    }
                    if (_session == null) return null;
                    var held = _store.Resolve(_session, AionVmmSnapshotChannels.GameExit, context,
                        OperationResult<GameExitObservation>.Fail(failure), now);
                    return held.Result.Success ? held.Result.Value : null;
                }
            }
        });

    private void Check(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        token.ThrowIfCancellationRequested();
    }

    // A timed-out raw capture may still hold the capture gate. The committed store
    // remains available independently; this method can never authorize input.
    internal GameExitObservation? ReadPublished(CancellationToken token = default)
    {
        lock (_commit)
        {
            Check(token);
            if (_session == null) return null;
            var held = _store.Resolve(_session, AionVmmSnapshotChannels.GameExit, context,
                OperationResult<GameExitObservation>.Fail("Exit capture is draining."), DateTimeOffset.UtcNow);
            return held.Result.Success ? held.Result.Value : null;
        }
    }

    public void Dispose()
    {
        lock (_commit)
        {
            _disposed = true;
            if (_session != null) _store.ClearSession(_session);
            _session = null;
        }
    }
}
