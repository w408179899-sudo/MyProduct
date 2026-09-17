using Hardware.KmBox;
using Smart.Adapters.Dma;
using Smart.Adapters.KmBox;
using Smart.Contracts;
using Smart.Data;
using Smart.Runtime;
namespace Smart.Hosting.Windows;

public sealed class HardwareSessionFactory(InputLeaseRegistry leases,
    Action<SessionComposition, DmaDispatcher, ProcessBinding>? configure = null, IEventSink? events = null,
    ISnapshotObserver? observer = null) : IRuntimeSessionFactory
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Func<ValueTask>> _pendingCleanup = new();
    private readonly VmmConnectionPool _connections = new(leases);
    public async ValueTask<IRuntimeSession> OpenAsync(AccountProfile profile, CancellationToken token)
    {
        profile.Validate();
        if (profile.Mode != RuntimeMode.Hardware) throw new ArgumentException("Hardware factory requires a hardware profile.");
        if (_pendingCleanup.TryGetValue(profile.Id, out var pending))
        {
            await pending().ConfigureAwait(false);
            _pendingCleanup.TryRemove(profile.Id, out _);
        }
        var settings = profile.Dma!;
        VmmConnectionPool.ConnectionLease? connection = null;
        SnapshotSession? snapshots = null;
        ConnectionSnapshotLifetime? connectionLifetime = null;
        ActionExecutor? executor = null;
        try
        {
            connection = await _connections.AcquireAsync(settings, token).ConfigureAwait(false);
            var transport = connection.Transport;
            token.ThrowIfCancellationRequested();
            var binding = await Task.Run(() =>
            {
                var candidate = Select(transport.ListProcesses(), settings, requireModule: false);
                var resolved = transport.GetProcess(candidate.ProcessId, settings.ModuleName);
                if (resolved.ModuleBase == 0) throw new IOException("Required process module is not loaded.");
                return resolved;
            }, token).ConfigureAwait(false);
            var dispatcher = connection.Dispatcher;
            var composition = new SessionComposition(profile);
            try { configure?.Invoke(composition, dispatcher, binding); composition.Seal(); }
            catch (Exception ex) { throw new SessionConfigurationException("Session registration failed: " + ex.Message, ex); }
            var catalog = composition.Channels;
            var runId = Guid.NewGuid();
            var recorder = observer ?? (profile.RecordSnapshots && events is not null ? new SnapshotTraceRecorder(events) : null);
            snapshots = new SnapshotProvider(catalog, events: events, observer: recorder).OpenSession(new(transport.DeviceId, transport.ConnectionId,
                profile.Id, runId.ToString("N"), binding.ProcessId, binding.ProcessIdentity, binding.ModuleIdentity));
            connectionLifetime = new(transport, connection, snapshots);
            if (!connection.IsCurrent) throw new MemoryConnectionLostException("DMA worker exited while creating the account session.");
            if (recorder is SnapshotTraceRecorder trace) trace.RecordConfiguration(profile, runId.ToString("N"));
            var entries = composition.Activate(snapshots.Reader, runId);
            var channels = catalog.Channels.Select(x => x.Id).ToArray();
            ModuleCatalog.Validate(entries, channels);
            var input = profile.Input!;
            var device = new KmBoxInputDevice(new KmBoxOptions { IpAddress = input.Address, Port = input.Port, Mac = input.Mac });
            try { executor = new(device, leases); }
            catch { await device.DisposeAsync().ConfigureAwait(false); throw; }
            await device.InitializeAsync(token).ConfigureAwait(false);
            var worker = new AccountWorker(profile.Id, snapshots.Reader, executor, entries, events: events, registeredChannels: channels, runId: runId);
            var failures = 0;
            async ValueTask<bool> Probe(CancellationToken ct)
            {
                if (!connection.IsCurrent) return false;
                try
                {
                    var current = await Task.Run(() => transport.GetProcess(binding.ProcessId, settings.ModuleName), ct).ConfigureAwait(false);
                    failures = 0;
                    return current.ProcessIdentity == binding.ProcessIdentity && current.ModuleIdentity == binding.ModuleIdentity;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (TargetProcessUnavailableException) { return false; }
                catch
                {
                    if (++failures >= 3) connection.Retire();
                    throw;
                }
            }
            return new RuntimeSession(worker, snapshots, Probe, connectionLifetime);
        }
        catch
        {
            async ValueTask Cleanup()
            {
                snapshots?.Dispose();
                if (executor is not null) await executor.DisposeAsync().ConfigureAwait(false);
                if (snapshots is not null) await snapshots.DisposeAsync().ConfigureAwait(false);
                if (connectionLifetime is not null) await connectionLifetime.DisposeAsync().ConfigureAwait(false);
                else if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false);
            }
            _pendingCleanup[profile.Id] = Cleanup;
            await Cleanup().ConfigureAwait(false);
            _pendingCleanup.TryRemove(profile.Id, out _);
            throw;
        }
    }
    public static ProcessBinding Select(IReadOnlyList<ProcessBinding> processes, DmaSettings settings, bool requireModule = true)
    {
        var matches = processes.Where(x => settings.ProcessId is { } pid ? x.ProcessId == pid :
            string.Equals(x.Name, settings.ProcessName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) throw new IOException("Select exactly one target process; specify PID when names are ambiguous.");
        if (requireModule && matches[0].ModuleBase == 0) throw new IOException("Required process module is not loaded.");
        return matches[0];
    }
    public async ValueTask DisposeAsync()
    {
        List<Exception>? errors = null;
        foreach (var entry in _pendingCleanup)
        {
            try { await entry.Value().ConfigureAwait(false); _pendingCleanup.TryRemove(entry.Key, out _); }
            catch (Exception ex) { (errors ??= []).Add(ex); }
        }
        if (errors is not null) throw new AggregateException("Hardware cleanup remains pending.", errors);
        await _connections.DisposeAsync().ConfigureAwait(false);
    }
}
