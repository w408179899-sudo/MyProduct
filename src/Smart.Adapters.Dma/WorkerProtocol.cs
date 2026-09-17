using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace Smart.Adapters.Dma;

// Shared source between the adapter and the dependency-free native worker. No business payloads.
internal enum WorkerOperation : byte { Initialize = 1, ListProcesses = 2, GetProcess = 3, ReadBatch = 4, Close = 5 }
internal enum WorkerStatus : byte { Success, ReadError, ProcessExited, ConfigurationError }
internal sealed record WorkerStartup(string LibraryPath, string DeviceId, string[] Arguments,
    string LeaseDirectory, string? ProfileJson);

internal static class WorkerProtocol
{
    internal const int Version = 1;
    internal const int MaximumFrame = 4 * 1024 * 1024;
    internal const int MaximumBatchBytes = 1024 * 1024;
    internal const int MaximumProcesses = 65536;
    internal static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    internal static MemoryStream Message(int sequence, WorkerOperation operation, Action<BinaryWriter>? body = null)
    {
        var stream = new MemoryStream();
        try
        {
            using var writer = new BinaryWriter(stream, Utf8, leaveOpen: true);
            writer.Write(Version); writer.Write(sequence); writer.Write((byte)operation); body?.Invoke(writer);
            if (stream.Length > MaximumFrame) throw new InvalidDataException("Worker message exceeds its byte budget.");
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }
    internal static async ValueTask WriteAsync(Stream pipe, MemoryStream message, CancellationToken token)
    {
        if (message.Length is < 9 or > MaximumFrame) throw new InvalidDataException("Invalid worker frame length.");
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, checked((int)message.Length));
        await pipe.WriteAsync(header, token).ConfigureAwait(false);
        await pipe.WriteAsync(message.GetBuffer().AsMemory(0, checked((int)message.Length)), token).ConfigureAwait(false);
        await pipe.FlushAsync(token).ConfigureAwait(false);
    }
    internal static async ValueTask<MemoryStream> ReadAsync(Stream pipe, CancellationToken token)
    {
        var header = new byte[4]; await pipe.ReadExactlyAsync(header, token).ConfigureAwait(false);
        var count = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (count is < 9 or > MaximumFrame) throw new InvalidDataException("Invalid worker frame length.");
        var bytes = new byte[count]; await pipe.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        return new MemoryStream(bytes, writable: false);
    }
    internal static void WriteString(BinaryWriter writer, string value, int maximumBytes = 16384)
    {
        var length = Utf8.GetByteCount(value);
        if (length > maximumBytes || value.Contains('\0')) throw new InvalidDataException("Worker string exceeds its budget or contains NUL.");
        writer.Write(length); writer.Write(Utf8.GetBytes(value));
    }
    internal static string ReadString(BinaryReader reader, int maximumBytes = 16384)
    {
        var length = reader.ReadInt32();
        if (length < 0 || length > maximumBytes || length > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Invalid worker string length.");
        var value = Utf8.GetString(reader.ReadBytes(length));
        if (value.Contains('\0')) throw new InvalidDataException("Worker string contains NUL.");
        return value;
    }
    internal static void End(BinaryReader reader)
    {
        if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Unexpected worker message suffix.");
    }
    internal static void WriteBinding(BinaryWriter writer, ProcessBinding value)
    {
        writer.Write(value.ProcessId); WriteString(writer, value.Name); WriteString(writer, value.ProcessIdentity);
        writer.Write(value.ModuleBase); WriteString(writer, value.ModuleFingerprint);
        if (writer.BaseStream.Length > MaximumFrame) throw new InvalidDataException("Process list exceeds its byte budget.");
    }
    internal static ProcessBinding ReadBinding(BinaryReader reader) => new(reader.ReadInt32(), ReadString(reader),
        ReadString(reader), reader.ReadUInt64(), ReadString(reader));
    internal static void ValidateReads(int processId, IReadOnlyList<MemoryReadRequest> reads)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
        if (reads.Count is < 1 or > 256 || reads.Any(x => x is null || x.Length is <= 0 or > MaximumBatchBytes || x.Address > ulong.MaxValue - (ulong)x.Length) ||
            reads.Sum(x => (long)x.Length) > MaximumBatchBytes) throw new ArgumentException("Read batch exceeds address/count/byte budget.");
    }
    internal static ImmutableArray<MemoryBlock> ReadBlocks(BinaryReader reader, IReadOnlyList<MemoryReadRequest> requested)
    {
        if (reader.ReadInt32() != requested.Count) throw new InvalidDataException("Worker returned an unexpected block count.");
        var blocks = ImmutableArray.CreateBuilder<MemoryBlock>(requested.Count);
        foreach (var request in requested)
        {
            var address = reader.ReadUInt64(); var complete = reader.ReadBoolean(); var count = reader.ReadInt32();
            if (address != request.Address || count != (complete ? request.Length : 0))
                throw new InvalidDataException("Worker returned an unvalidated block shape.");
            var bytes = reader.ReadBytes(count);
            if (bytes.Length != count) throw new EndOfStreamException();
            blocks.Add(new(address, System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(bytes), complete));
        }
        return blocks.MoveToImmutable();
    }
}
