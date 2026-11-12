using System.IO.Compression;
using System.Text.Json;
using System.Text;
using Roadhog.Core.Model;
using Roadhog.Core.NavigationTests;

namespace Roadhog.Infrastructure.NavigationTests;

public static class AStarTestMapFile
{
    private const int MaximumBytes = 64 * 1024 * 1024;
    public static string SamplePath => Path.Combine(AppContext.BaseDirectory, "Source", "NavigationTests", "300100000.sample.astar.json.gz");

    public static async Task<AStarTestMap> LoadAsync(string path, CancellationToken token)
    {
        await using var file = File.OpenRead(path);
        var binary = path.EndsWith(".astar.bin", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".astar.bin.gz", StringComparison.OrdinalIgnoreCase);
        if (file.Length > (binary ? 1024L * 1024 * 1024 : MaximumBytes)) throw new InvalidDataException("导航文件超过大小限制。");
        using var gzip = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? new GZipStream(file, CompressionMode.Decompress, true) : null;
        Stream input = gzip ?? (Stream)file;
        if (binary) return await Task.Run(() => ReadBinary(input, token), token).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[65536];
        int size;
        while ((size = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            if (memory.Length + size > MaximumBytes) throw new InvalidDataException("解压后的导航数据超过 64 MB 测试限制。");
            memory.Write(buffer, 0, size);
        }
        token.ThrowIfCancellationRequested();
        var document = JsonSerializer.Deserialize<AStarTestMapDocument>(memory.GetBuffer().AsSpan(0, (int)memory.Length),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, MaxDepth = 16 })
            ?? throw new InvalidDataException("导航文件为空。");
        return new AStarTestMap(document, token);
    }

    private static AStarTestMap ReadBinary(Stream input, CancellationToken token)
    {
        using var buffered = new BufferedStream(input, 65536);
        using var reader = new BinaryReader(buffered, Encoding.UTF8, true);
        if (!reader.ReadBytes(8).AsSpan().SequenceEqual("RHASTAR1"u8) || reader.ReadInt32() != 1)
            throw new InvalidDataException("不支持的二进制导航版本。");
        var mapId = reader.ReadUInt32();
        var count = reader.ReadInt32();
        var edgeCount = reader.ReadInt32();
        if (mapId == 0 || count is < 1 or > AStarTestMap.MaximumNodes || edgeCount is < 0 or > AStarTestMap.MaximumEdges || (long)edgeCount > (long)count * 64)
            throw new InvalidDataException("二进制导航地图 ID 或数量无效。");
        string ReadText()
        {
            var length = reader.Read7BitEncodedInt();
            if (length is < 0 or > 8192) throw new InvalidDataException("导航说明过长。");
            var bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException("导航说明不完整。");
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        var name = ReadText(); var description = ReadText();
        token.ThrowIfCancellationRequested();
        var positions = new Vector3Snapshot[count];
        for (var i = 0; i < count; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            positions[i] = new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }
        var offsets = new int[count + 1];
        for (var i = 0; i < offsets.Length; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            offsets[i] = reader.ReadInt32();
        }
        var links = new int[edgeCount];
        for (var i = 0; i < links.Length; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            links[i] = reader.ReadInt32();
        }
        // Read to the end so trailing data and a bad gzip checksum cannot be silently accepted.
        if (reader.BaseStream.ReadByte() != -1) throw new InvalidDataException("导航文件有多余数据。");
        return new AStarTestMap(mapId, name, description, positions, offsets, links, token);
    }
}
