using System.Collections.Immutable;
using Smart.Adapters.Dma;
using Smart.Contracts;
using Smart.Data;
using Smart.ProbeProtocol;

namespace Smart.HardwareProbe;

internal sealed class ProbeTargetChangedException(string message) : IOException(message);

// All inspection and byte reads run on the dispatcher's one native-I/O thread.
internal sealed class BoundProbeTransport(IProcessMemoryTransport inner, ProbeManifest manifest, ProcessBinding expected) : IMemoryTransport
{
    public string DeviceId => inner.DeviceId;
    public string ConnectionId => inner.ConnectionId;
    public ImmutableArray<MemoryBlock> ReadBatch(int processId, IReadOnlyList<MemoryReadRequest> requests)
    {
        if (processId != manifest.ProcessId || requests.Count != 1 || requests[0].Address != manifest.GetAddress() ||
            requests[0].Length != ProbeMemoryProtocol.Size) throw new InvalidOperationException("Probe reads must match the explicit fixture manifest.");
        Verify();
        var result = inner.ReadBatch(processId, requests);
        Verify();
        return result;
    }
    private void Verify()
    {
        ProcessBinding current;
        try { current = inner.GetProcess(manifest.ProcessId, manifest.MainModuleName); }
        catch (TargetProcessUnavailableException ex) { throw new ProbeTargetChangedException(ex.Message); }
        if (current != expected) throw new ProbeTargetChangedException("The selected process or module identity changed; create a new fixture manifest.");
    }
    public void Dispose() => inner.Dispose();
}

// Raw errors belong to this provider reader; the observation loop receives only ProbeSample publications.
internal sealed class ProbeReader(DmaDispatcher dispatcher, ProbeManifest manifest, Action<string> invalidate) : IRawChannelReader<ProbeSample, NoPartition>
{
    private readonly MemoryReadRequest[] _request = [new(manifest.GetAddress(), ProbeMemoryProtocol.Size)];
    private long _torn, _validation, _readErrors, _highestSequence;
    public long TornReads => Interlocked.Read(ref _torn);
    public long ValidationRejected => Interlocked.Read(ref _validation);
    public long ReadErrors => Interlocked.Read(ref _readErrors);
    public async ValueTask<RawRead<ProbeSample>> CaptureAsync(CaptureContext context, NoPartition partition, CancellationToken token)
    {
        ImmutableArray<MemoryBlock> blocks;
        try { blocks = await dispatcher.ReadAsync(context, _request, token).ConfigureAwait(false); }
        catch (ProbeTargetChangedException ex)
        { invalidate(ex.Message); return RawRead<ProbeSample>.Failed(ex.Message); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        { Interlocked.Increment(ref _readErrors); return RawRead<ProbeSample>.Failed(ex.Message); }
        if (blocks.Length != 1 || blocks[0].Address != manifest.GetAddress() || !blocks[0].Complete || blocks[0].Bytes.Length != ProbeMemoryProtocol.Size)
        { Interlocked.Increment(ref _readErrors); return RawRead<ProbeSample>.Failed("Fixture read was incomplete."); }
        if (!ProbeMemoryProtocol.TryDecode(blocks[0].Bytes.AsSpan(), manifest.SessionId, out var sample, out var error))
        {
            if (error == ProbeReadError.WrongSession) invalidate("The fixture session changed; old publications were invalidated.");
            if (error is ProbeReadError.Updating or ProbeReadError.SequenceMismatch)
                Interlocked.Increment(ref _torn);
            else Interlocked.Increment(ref _validation);
            return RawRead<ProbeSample>.Failed(error.ToString());
        }
        if (sample!.Sequence < _highestSequence)
        { Interlocked.Increment(ref _validation); return RawRead<ProbeSample>.Failed("Fixture sequence moved backwards."); }
        _highestSequence = sample.Sequence;
        return RawRead<ProbeSample>.Complete(sample) with { ObservationId = sample.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture) };
    }
}
