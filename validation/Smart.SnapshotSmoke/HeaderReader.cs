using System.Collections.Immutable;
using System.Security.Cryptography;
using Smart.Adapters.Dma;
using Smart.Contracts;
using Smart.Data;
using Smart.NativeSmoke;
namespace Smart.SnapshotSmoke;

public sealed record HeaderSnapshot(string Sha256, ushort Machine, ushort Sections, ushort OptionalMagic);
internal sealed class TargetChangedException(string message) : IOException(message);

// Identity checks and byte reads share the dispatcher's native-I/O thread.
internal sealed class BoundTransport(IProcessMemoryTransport inner, ProcessBinding expected, string module) : IMemoryTransport
{
    public string DeviceId => inner.DeviceId;
    public string ConnectionId => inner.ConnectionId;
    public ImmutableArray<MemoryBlock> ReadBatch(int processId, IReadOnlyList<MemoryReadRequest> requests)
    {
        if (processId != expected.ProcessId || requests.Count != 1 || requests[0].Length != 4096 ||
            requests[0].Address != 0 && requests[0].Address != expected.ModuleBase)
            throw new InvalidOperationException("Snapshot smoke reads are limited to the selected PE header and address zero.");
        Verify();
        var blocks = inner.ReadBatch(processId, requests);
        Verify();
        return blocks;
    }
    private void Verify()
    {
        ProcessBinding current;
        try { current = inner.GetProcess(expected.ProcessId, module); }
        catch (TargetProcessUnavailableException ex) { throw new TargetChangedException(ex.Message); }
        if (current != expected) throw new TargetChangedException("The selected process/module identity changed.");
    }
    public void Dispose() => inner.Dispose();
}

internal sealed class HeaderReader(DmaDispatcher dispatcher, ProcessBinding binding, Action<string> invalidate)
    : IRawChannelReader<HeaderSnapshot, NoPartition>
{
    private int _validAddress;
    private long _invalidFailures, _validSuccesses, _unexpectedZero, _errors;
    public bool UseValidAddress { set => Volatile.Write(ref _validAddress, value ? 1 : 0); }
    public long InvalidFailures => Interlocked.Read(ref _invalidFailures);
    public long ValidSuccesses => Interlocked.Read(ref _validSuccesses);
    public long UnexpectedZero => Interlocked.Read(ref _unexpectedZero);
    public long OtherErrors => Interlocked.Read(ref _errors);

    public async ValueTask<RawRead<HeaderSnapshot>> CaptureAsync(CaptureContext context, NoPartition partition, CancellationToken token)
    {
        var address = Volatile.Read(ref _validAddress) == 0 ? 0 : binding.ModuleBase;
        try
        {
            var blocks = await dispatcher.ReadAsync(context, [new(address, 4096)], token).ConfigureAwait(false);
            if (blocks.Length != 1 || blocks[0].Address != address)
                throw new IOException("The requested header block was not returned.");
            var block = blocks[0];
            if (!block.Complete || block.Bytes.Length != 4096)
            {
                if (address == 0) Interlocked.Increment(ref _invalidFailures);
                else Interlocked.Increment(ref _errors);
                return RawRead<HeaderSnapshot>.Failed("The bounded header read was incomplete.");
            }
            if (address == 0)
            {
                Interlocked.Increment(ref _unexpectedZero);
                return RawRead<HeaderSnapshot>.Failed("The invalid-address control unexpectedly returned a complete block.");
            }
            var bytes = block.Bytes.AsSpan();
            var offset = PeHeaderValidation.ReadDosOffset(bytes[..64]);
            if (offset > bytes.Length - PeHeaderValidation.PePrefixBytes)
                throw new InvalidDataException("The selected PE prefix lies outside the bounded 4096-byte header.");
            var pe = PeHeaderValidation.ReadPePrefix(bytes.Slice(offset, PeHeaderValidation.PePrefixBytes), offset);
            if (pe.RequiredPeBytes > bytes.Length - offset)
                throw new InvalidDataException("The declared PE header lies outside the bounded 4096-byte header.");
            PeHeaderValidation.ValidateCompletePe(bytes.Slice(offset, pe.RequiredPeBytes), pe);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            if (hash != binding.ModuleFingerprint) throw new TargetChangedException("The PE fingerprint changed during capture.");
            Interlocked.Increment(ref _validSuccesses);
            return RawRead<HeaderSnapshot>.Complete(new(hash, pe.Machine, pe.Sections, pe.OptionalMagic));
        }
        catch (TargetChangedException ex) { invalidate(ex.Message); return RawRead<HeaderSnapshot>.Failed(ex.Message); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _errors);
            return RawRead<HeaderSnapshot>.Failed(ex.GetType().Name + ": " + ex.Message);
        }
    }
}
