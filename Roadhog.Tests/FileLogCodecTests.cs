using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Roadhog.Infrastructure.Diagnostics;

internal static class FileLogCodecTests
{
    public static Task RoundTripAsync()
    {
        using var symbolsStream = typeof(FileRoadhogLogger).Assembly.GetManifestResourceStream(
            "Roadhog.Infrastructure.Diagnostics.FileLogSymbols.v1.json")!;
        using var symbols = JsonDocument.Parse(symbolsStream);
        var fields = symbols.RootElement.GetProperty("Fields").EnumerateArray().Take(349).Select(value => value.GetString());
        var events = symbols.RootElement.GetProperty("Events").EnumerateArray().Take(88).Select(value => value.GetString());
        var contract = string.Join("\n", fields) + "\n--events--\n" + string.Join("\n", events);
        Require(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contract))) == "532BCC4B87D9E66E4737B7C04BDA66A1B0EFBD812B2798F2F200679AF1C8B650",
            "all original R1 symbol IDs are permanent; new symbols may only be appended");
        // Fixed R1 bytes guard the persisted symbol IDs, not just encoder/decoder agreement.
        const string golden = "[\"R1\",639000000000000001,480,1,14,[0,\"test\",2,2200709866,14,null]]";
        var restored = FileLogCodec.Parse(golden);
        Require(restored.Timestamp.Ticks == 639000000000000001 && restored.Timestamp.Offset.TotalMinutes == 480,
            "preserve 100 ns precision and original timezone");
        Require(restored.Level == "warn" && restored.EventName == "stationary_combat.tab.verify" &&
            restored.Fields.Keys.SequenceEqual(new[] { "account", "targetServerObjectId", "error" }), "R1 symbols stay stable");
        Require(FileLogCodec.Encode(restored) == golden, "fixed persisted format roundtrips");
        var unknown = new FileLogEntry(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.FromHours(-4)).AddTicks(1234567),
            "custom-level", "new.event", new Dictionary<string, object?>
            {
                ["0"] = "literal numeric field name", ["account"] = "中文", ["new.field"] = null,
                ["bool"] = false, ["ulong"] = ulong.MaxValue, ["long"] = long.MinValue,
                ["decimal"] = 0.1234567890123456789012345678m, ["escape"] = "a\n\"b\\c",
                ["exceptionMessage"] = "完整异常", ["stackTrace"] = new string('x', 5000)
            }, "完整异常");
        var line = FileLogCodec.Encode(unknown);
        var decoded = FileLogCodec.Decode(line);
        Require(JsonNode.DeepEquals(JsonSerializer.SerializeToNode(unknown), JsonNode.Parse(decoded)),
            "unknown names, Unicode, null, bool, numeric types and full exception payload survive");
        Require(JsonNode.DeepEquals(JsonNode.Parse(decoded), JsonNode.Parse(FileLogCodec.Decode(decoded))),
            "legacy JSON and compact records can coexist in one file");
        foreach (var invalid in new[] { "[\"R2\",0,0,0,0,[]]", "[\"R1\",0,0,0,999999,[]]", "[\"R1\",0,0,0,0,[0]]" })
        {
            try { FileLogCodec.Decode(invalid); throw new Exception("invalid format was accepted"); }
            catch (InvalidDataException) { }
        }
        return Task.CompletedTask;
    }

    public static Task CompressionAsync()
    {
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "file-log-compact-v1.jsonl"));
        var result = Replay(lines);
        Require(result.After * 2 <= result.Before, "representative existing output must shrink by at least another 50%");
        return Task.CompletedTask;
    }

    public static Task DecodeFilesAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoadhogLogCodec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "source.log");
            var output = Path.Combine(root, "decoded.jsonl");
            const string old = "{\"Timestamp\":\"2026-11-09T12:00:00+08:00\",\"Level\":\"info\",\"EventName\":\"account.stop_requested\",\"Fields\":{\"reason\":\"manual\"},\"Error\":null}";
            File.WriteAllLines(input, new[] { old, FileLogCodec.Encode(FileLogCodec.Parse(old)) });
            var sourceBytes = File.ReadAllBytes(input);
            FileLogCodec.DecodeFile(input, output);
            Require(File.ReadAllLines(output).All(line => JsonNode.DeepEquals(JsonNode.Parse(line), JsonNode.Parse(old))),
                "decode mixed old/new logs without changing the event meaning");
            try { FileLogCodec.DecodeFile(input, input); throw new Exception("source overwrite was allowed"); }
            catch (IOException) { }
            Require(sourceBytes.SequenceEqual(File.ReadAllBytes(input)), "decoder never overwrites source evidence");
            File.WriteAllText(input, old + "\n[\"R1\"");
            try { FileLogCodec.DecodeFile(input, Path.Combine(root, "partial.jsonl")); throw new Exception("truncated input accepted"); }
            catch (InvalidDataException ex) { Require(ex.Message.Contains("line 2"), "corrupt/incomplete line is identified explicitly"); }
        }
        finally { Directory.Delete(root, true); }
        return Task.CompletedTask;
    }

    // Explicit opt-in audit of local, already-compacted real logs. No account or live file writes.
    public static void ReplayDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        long before = 0, after = 0;
        var count = 0;
        foreach (var path in Directory.GetFiles(source, "*.jsonl"))
        {
            var result = Replay(File.ReadAllLines(path));
            before += result.Before; after += result.After; count += result.Lines.Length;
            File.WriteAllLines(Path.Combine(destination, Path.GetFileName(path)), result.Lines, new UTF8Encoding(false));
            Console.WriteLine($"{Path.GetFileName(path)}: {result.Lines.Length} records, {result.Before} -> {result.After} bytes, {100D * (1D - (double)result.After / result.Before):F2}% smaller");
            Require(result.After * 2 <= result.Before, "each captured account log must shrink by at least 50%");
        }
        Require(count > 0, "replay requires captured records");
        Console.WriteLine($"TOTAL: {count} exact roundtrips; {before} -> {after} bytes; {100D * (1D - (double)after / before):F2}% smaller");
    }

    private static (long Before, long After, string[] Lines) Replay(string[] lines)
    {
        long before = 0, after = 0;
        var compact = new List<string>();
        foreach (var line in lines)
        {
            var encoded = FileLogCodec.Encode(FileLogCodec.Parse(line));
            Require(JsonNode.DeepEquals(JsonNode.Parse(line), JsonNode.Parse(FileLogCodec.Decode(encoded))),
                "replay must preserve every timestamp, level, event, field and error");
            compact.Add(encoded);
            before += Encoding.UTF8.GetByteCount(line + Environment.NewLine);
            after += Encoding.UTF8.GetByteCount(encoded + Environment.NewLine);
        }
        return (before, after, compact.ToArray());
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
