using System.Security.Cryptography;
using System.Text;
namespace Smart.Runtime;

// One physical input endpoint belongs to one live account scope.
// Keep the registry shared at composition root; never construct one per account.
public sealed class InputLeaseRegistry(string? leaseDirectory = null)
{
    public string? LeaseDirectory => leaseDirectory;
    private readonly object _sync = new();
    private readonly HashSet<string> _held = new(StringComparer.OrdinalIgnoreCase);
    public IDisposable Acquire(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        deviceId = deviceId.Trim();
        lock (_sync)
        {
            if (!_held.Add(deviceId)) throw new InvalidOperationException("Input endpoint already leased: " + deviceId);
        }
        try
        {
            FileStream? file = null;
            if (leaseDirectory is not null)
            {
                Directory.CreateDirectory(leaseDirectory);
                var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(deviceId.Trim().ToUpperInvariant())));
                file = new FileStream(Path.Combine(leaseDirectory, name + ".lease"), FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.Read);
                try
                {
                    file.SetLength(0);
                    var metadata = Encoding.UTF8.GetBytes($"pid={Environment.ProcessId};device={deviceId};at={DateTimeOffset.UtcNow:O}");
                    file.Write(metadata); file.Flush();
                }
                catch { file.Dispose(); throw; }
            }
            return new Lease(this, deviceId, file);
        }
        catch (Exception ex)
        {
            lock (_sync) _held.Remove(deviceId);
            throw new InvalidOperationException("Device endpoint unavailable or leased by another process: " + deviceId, ex);
        }
    }
    public static string SharedDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Smart", "device-leases");
    private sealed class Lease(InputLeaseRegistry owner, string id, FileStream? file) : IDisposable
    {
        private readonly object _sync = new();
        private bool _disposed;
        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                file?.Dispose();
                lock (owner._sync) owner._held.Remove(id);
                _disposed = true;
            }
        }
    }
}
