using Roadhog.Application.GameSessions;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Infrastructure.Login;
using Roadhog.Infrastructure.Vmm;

namespace Roadhog.Infrastructure.GameSessions;

internal sealed class GameProcessSnapshotPublisher(GameApiReadContext context, IRoadhogLogger? logger = null) : IDisposable
{
    private readonly DmaStableSnapshotStore _store = new(AionVmmSnapshotChannels.Registry);
    private readonly DmaSnapshotReadCoordinator _coordinator = new();
    private readonly object _commit = new();
    private string? _session;
    private bool _disposed;
    private DateTimeOffset _nextDiagnostic;

    internal GameProcessSnapshot? Capture(Func<Action<string>, GameProcessSnapshot> capture, CancellationToken token) =>
        _coordinator.Execute(context.AccountName, AionVmmSnapshotChannels.GameProcesses, () =>
        {
            try
            {
                lock (_commit) Check(token);
                var value = capture(session =>
                {
                    lock (_commit)
                    {
                        Check(token);
                        if (_session == session) return;
                        if (_session != null) _store.ClearSession(_session);
                        _session = session;
                    }
                });
                lock (_commit)
                {
                    Check(token);
                    if (_session == null || value.Session != _session || value.Processes.Any(p => p.ProcessId <= 0 || string.IsNullOrWhiteSpace(p.InstanceKey))
                        || value.Processes.Select(p => p.ProcessId).Distinct().Count() != value.Processes.Count)
                        throw new InvalidDataException("Invalid game process inventory identity.");
                    // Copy at publication so callers cannot mutate a committed process inventory.
                    var published = value with { Processes = Array.AsReadOnly(value.Processes.ToArray()) };
                    _store.Resolve(_session, AionVmmSnapshotChannels.GameProcesses, context,
                        OperationResult<GameProcessSnapshot>.Ok(published), DateTimeOffset.UtcNow);
                    return published;
                }
            }
            catch (Exception error) when (error is InvalidDataException || LoginReadErrors.IsRetryable(error))
            {
                lock (_commit)
                {
                    Check(token);
                    if (DateTimeOffset.UtcNow >= _nextDiagnostic)
                    {
                        _nextDiagnostic = DateTimeOffset.UtcNow.AddSeconds(10);
                        logger?.Warn("vmm.game_processes.read_retry", new Dictionary<string, object?>
                        { ["account"] = context.AccountName, ["device"] = context.VmmDeviceName, ["error"] = error.Message });
                    }
                    return ReadPublished(token);
                }
            }
        });

    internal GameProcessSnapshot? ReadPublished(CancellationToken token)
    {
        lock (_commit)
        {
            Check(token);
            if (_session == null) return null;
            var held = _store.Resolve(_session, AionVmmSnapshotChannels.GameProcesses, context,
                OperationResult<GameProcessSnapshot>.Fail("Process capture pending."), DateTimeOffset.UtcNow);
            return held.Result.Success ? held.Result.Value : null;
        }
    }

    private void Check(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        token.ThrowIfCancellationRequested();
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
