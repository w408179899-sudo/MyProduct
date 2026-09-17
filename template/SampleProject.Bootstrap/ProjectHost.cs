using Smart.Contracts;
using Smart.Hosting;
using Smart.Hosting.Windows;
using Smart.Runtime;
namespace SampleProject.Bootstrap;

public sealed class ProjectHost : IRuntimeSessionFactory
{
    private readonly MockSessionFactory _mock;
    private readonly HardwareSessionFactory _hardware;
    public ProjectHost(IEventSink? events = null, string? leaseDirectory = null, bool recordSnapshots = false)
    {
        var leases = new InputLeaseRegistry(leaseDirectory ?? InputLeaseRegistry.SharedDirectory);
        var observer = recordSnapshots && events is not null ? new SnapshotTraceRecorder(events) : null;
        // Register project-specific typed channels here, before the catalog is sealed.
        _mock = new(leases, configure: ProjectComposition.ConfigureMock, events: events, observer: observer);
        _hardware = new(leases, configure: ProjectComposition.ConfigureHardware, events: events, observer: observer);
    }
    public ValueTask<IRuntimeSession> OpenAsync(AccountProfile profile, CancellationToken token) => profile.Mode switch
    {
        RuntimeMode.Mock => _mock.OpenAsync(profile, token),
        RuntimeMode.Hardware => _hardware.OpenAsync(profile, token),
        _ => throw new ArgumentOutOfRangeException(nameof(profile))
    };
    public ValueTask DisposeAsync() => _hardware.DisposeAsync();
}
