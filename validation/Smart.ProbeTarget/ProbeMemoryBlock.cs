using System.Runtime.InteropServices;
using Smart.ProbeProtocol;
namespace Smart.ProbeTarget;

public sealed class ProbeMemoryBlock : IDisposable
{
    private readonly object _memoryGate = new();
    private readonly SemaphoreSlim _writer = new(1, 1);
    private readonly Guid _session;
    private nint _memory;
    private long _sequence;
    public ProbeMemoryBlock(Guid session)
    {
        if (IntPtr.Size != 8 || !BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("Run the probe target in a little-endian x64 process.");
        if (session == Guid.Empty) throw new ArgumentException("A session identity is required.", nameof(session));
        _session = session;
        _memory = Marshal.AllocHGlobal(ProbeMemoryProtocol.Size);
        Clear();
    }
    public ulong Address { get { lock (_memoryGate) { RequireMemory(); return (ulong)_memory; } } }
    private unsafe void Clear() => new Span<byte>((void*)_memory, ProbeMemoryProtocol.Size).Clear();
    private void RequireMemory() => ObjectDisposedException.ThrowIf(_memory == 0, this);

    public async Task PublishAsync(long counter, TimeSpan tornWindow, CancellationToken cancellationToken = default)
    {
        if (tornWindow < TimeSpan.Zero || tornWindow > TimeSpan.FromMinutes(1)) throw new ArgumentOutOfRangeException(nameof(tornWindow));
        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sequence = checked(_sequence + 2);
            var frame = new byte[ProbeMemoryProtocol.Size];
            ProbeMemoryProtocol.Encode(frame, _session, sequence, counter, DateTimeOffset.UtcNow);
            _sequence = sequence;
            BeginWrite(frame, sequence);
            if (tornWindow > TimeSpan.Zero) await Task.Delay(tornWindow, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            CompleteWrite(frame, sequence);
        }
        finally { _writer.Release(); }
    }
    private unsafe void BeginWrite(byte[] frame, long sequence)
    {
        lock (_memoryGate)
        {
            RequireMemory();
            var bytes = new Span<byte>((void*)_memory, ProbeMemoryProtocol.Size);
            Volatile.Write(ref *(long*)((byte*)_memory + ProbeMemoryProtocol.BeginSequenceOffset), sequence - 1);
            Thread.MemoryBarrier();
            Volatile.Write(ref *(long*)((byte*)_memory + ProbeMemoryProtocol.EndSequenceOffset), sequence - 1);
            frame.AsSpan(0, ProbeMemoryProtocol.BeginSequenceOffset).CopyTo(bytes);
            // Half of the payload is deliberately copied before an optional async tear window.
            frame.AsSpan(ProbeMemoryProtocol.PayloadOffset, 32).CopyTo(bytes[ProbeMemoryProtocol.PayloadOffset..]);
            Thread.MemoryBarrier();
        }
    }
    private unsafe void CompleteWrite(byte[] frame, long sequence)
    {
        lock (_memoryGate)
        {
            RequireMemory();
            var bytes = new Span<byte>((void*)_memory, ProbeMemoryProtocol.Size);
            frame.AsSpan(56, ProbeMemoryProtocol.EndSequenceOffset - 56).CopyTo(bytes[56..]);
            Thread.MemoryBarrier();
            Volatile.Write(ref *(long*)((byte*)_memory + ProbeMemoryProtocol.EndSequenceOffset), sequence);
            Thread.MemoryBarrier();
            Volatile.Write(ref *(long*)((byte*)_memory + ProbeMemoryProtocol.BeginSequenceOffset), sequence);
        }
    }
    // In-process inspection is used only by protocol tests; external DMA readers use the manifest address.
    public unsafe byte[] CopySnapshot()
    {
        lock (_memoryGate) { RequireMemory(); return new ReadOnlySpan<byte>((void*)_memory, ProbeMemoryProtocol.Size).ToArray(); }
    }
    public void Dispose()
    {
        lock (_memoryGate)
        {
            if (_memory == 0) return;
            Marshal.FreeHGlobal(_memory); _memory = 0;
        }
    }
}
