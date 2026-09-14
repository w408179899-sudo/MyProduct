namespace Roadhog.Infrastructure.Vmm;

/// <summary>Serializes retirement with reads, including readers that already obtained this connection.</summary>
internal sealed class VmmConnection(string deviceName, string remote, Vmmsharp.Vmm vmm) : IDisposable
{
    private bool _retired;
    public string DeviceName { get; } = deviceName;
    public string Remote { get; } = remote;
    public object SyncRoot { get; } = new();
    public Vmmsharp.Vmm Vmm
    {
        get
        {
            lock (SyncRoot)
            {
                ObjectDisposedException.ThrowIf(_retired, this);
                return vmm;
            }
        }
    }

    public void Dispose()
    {
        lock (SyncRoot)
        {
            if (_retired) return;
            _retired = true;
            vmm.Dispose();
        }
    }
}
