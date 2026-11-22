namespace Roadhog.Infrastructure.Vmm;

/// <summary>Serializes retirement with reads, including readers that already obtained this connection.</summary>
internal sealed class VmmConnection(string deviceName, string remote, Vmmsharp.Vmm vmm) : IDisposable
{
    private const int ReadCacheRetryDelayMs = 5000;
    private bool _retired;
    private int _appliedReadCacheMs;
    private long _readCacheRetryAfter = long.MinValue;
    public string DeviceName { get; } = deviceName;
    public string Remote { get; } = remote;
    public object SyncRoot { get; } = new();
    public bool IsRetired => Volatile.Read(ref _retired);

    public bool NeedsReadCacheUpdate(int periodMs) =>
        periodMs != Volatile.Read(ref _appliedReadCacheMs) && Environment.TickCount64 >= Volatile.Read(ref _readCacheRetryAfter);

    public void RecordReadCacheAttempt(int periodMs, bool configured)
    {
        if (configured)
        {
            Volatile.Write(ref _appliedReadCacheMs, periodMs);
            Volatile.Write(ref _readCacheRetryAfter, long.MinValue);
        }
        else
        {
            Volatile.Write(ref _readCacheRetryAfter, Environment.TickCount64 + ReadCacheRetryDelayMs);
        }
    }

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
