using Smart.Contracts;
using Smart.Data;
using Smart.Runtime;
namespace Smart.Hosting;

// Empty by default. A project supplies registration and module factories at its composition root.
public sealed class MockSessionFactory(InputLeaseRegistry leases, Action<SessionComposition>? configure = null, IEventSink? events = null,
    ISnapshotObserver? observer = null) : IRuntimeSessionFactory
{
    public ValueTask<IRuntimeSession> OpenAsync(AccountProfile profile, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (profile.Mode != RuntimeMode.Mock) throw new ArgumentException("Mock factory accepts only mock profiles.");
        profile.Validate();
        var composition = new SessionComposition(profile);
        try { configure?.Invoke(composition); composition.Seal(); }
        catch (Exception ex) { throw new SessionConfigurationException("Session registration failed: " + ex.Message, ex); }
        var catalog = composition.Channels;
        var runId = Guid.NewGuid();
        var recorder = observer ?? (profile.RecordSnapshots && events is not null ? new SnapshotTraceRecorder(events) : null);
        var snapshots = new SnapshotProvider(catalog, events: events, observer: recorder).OpenSession(
            new("mock:" + profile.Id, Guid.NewGuid().ToString("N"), profile.Id, runId.ToString("N"), 1, "mock-process", "mock-module"));
        if (recorder is SnapshotTraceRecorder trace) trace.RecordConfiguration(profile, runId.ToString("N"));
        ActionExecutor? executor = null;
        try
        {
            var entries = composition.Activate(snapshots.Reader, runId);
            ModuleCatalog.Validate(entries, catalog.Channels.Select(x => x.Id).ToArray());
            executor = new(new MockInputDevice("mock:" + profile.Id), leases);
            var worker = new AccountWorker(profile.Id, snapshots.Reader, executor, entries, events: events,
                registeredChannels: catalog.Channels.Select(x => x.Id).ToArray(), runId: runId);
            return ValueTask.FromResult<IRuntimeSession>(new RuntimeSession(worker, snapshots));
        }
        catch { snapshots.Dispose(); if (executor is not null) executor.DisposeAsync().AsTask().GetAwaiter().GetResult(); throw; }
    }
}
