using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
[assembly: InternalsVisibleTo("Smart.Data.Tests")]
namespace Smart.Adapters.Dma;

// Infrastructure only. These byte blocks must be decoded, validated and published by Smart.Data.
public sealed record MemoryReadRequest(ulong Address, int Length);
public sealed record MemoryBlock(ulong Address, ImmutableArray<byte> Bytes, bool Complete)
{
    internal static MemoryBlock FromNativeOwnedBuffer(ulong address, byte[] buffer, bool success, uint bytesRead)
    {
        // Both MemReadEx and scatter report total valid bytes, potentially separated by holes.
        // The count cannot identify a valid prefix. Independent fields need separate read requests.
        // On success ownership transfers here; callers must not mutate the buffer afterwards.
        var complete = success && bytesRead == buffer.Length;
        return new(address, complete ? ImmutableCollectionsMarshal.AsImmutableArray(buffer) : [], complete);
    }
}
public interface IMemoryTransport : IDisposable
{
    string DeviceId { get; }
    string ConnectionId { get; }
    ImmutableArray<MemoryBlock> ReadBatch(int processId, IReadOnlyList<MemoryReadRequest> requests);
}
public interface IProcessMemoryTransport : IMemoryTransport
{
    IReadOnlyList<ProcessBinding> ListProcesses(string? requiredModule = null);
    ProcessBinding GetProcess(int pid, string module);
}
