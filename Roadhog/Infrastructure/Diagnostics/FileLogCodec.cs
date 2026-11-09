using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace Roadhog.Infrastructure.Diagnostics;

/// <summary>Stateless, lossless file encoding. Every line can be decoded independently.</summary>
internal static class FileLogCodec
{
    private sealed record Symbols(string Version, string[] Fields, string[] Events);
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };
    private static readonly Symbols Table = LoadSymbols();
    private static readonly Dictionary<string, int> FieldIds = Index(Table.Fields);
    private static readonly Dictionary<string, int> EventIds = Index(Table.Events);
    private static readonly string[] Levels = ["info", "warn", "error"];

    // Symbol positions are the on-disk contract: append only, never reorder/reuse.
    // Unknown names stay literal, so adding a diagnostic never requires a table update.
    private static Symbols LoadSymbols()
    {
        using var stream = typeof(FileLogCodec).Assembly.GetManifestResourceStream(
            "Roadhog.Infrastructure.Diagnostics.FileLogSymbols.v1.json")
            ?? throw new InvalidDataException("Missing R1 log symbols.");
        return JsonSerializer.Deserialize<Symbols>(stream) ?? throw new InvalidDataException("Invalid R1 log symbols.");
    }

    private static Dictionary<string, int> Index(string[] names) => names.Select((name, id) => (name, id))
        .ToDictionary(pair => pair.name, pair => pair.id, StringComparer.Ordinal);

    internal static string Encode(FileLogEntry entry)
    {
        var fields = new object?[entry.Fields.Count * 2];
        var index = 0;
        foreach (var pair in entry.Fields)
        {
            fields[index++] = FieldIds.TryGetValue(pair.Key, out var id) ? id : pair.Key;
            fields[index++] = pair.Value;
        }
        var level = Array.IndexOf(Levels, entry.Level);
        var row = new List<object?>
        {
            Table.Version, entry.Timestamp.Ticks, (int)entry.Timestamp.Offset.TotalMinutes,
            level >= 0 ? level : entry.Level,
            EventIds.TryGetValue(entry.EventName, out var eventId) ? eventId : entry.EventName,
            fields
        };
        if (entry.Error is not null) row.Add(entry.Error);
        return JsonSerializer.Serialize(row, JsonOptions);
    }

    internal static FileLogEntry Parse(string line)
    {
        using var document = JsonDocument.Parse(line);
        var row = document.RootElement;
        if (row.ValueKind == JsonValueKind.Object)
            return JsonSerializer.Deserialize<FileLogEntry>(line) ?? throw new InvalidDataException("Invalid legacy log entry.");
        if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() is < 6 or > 7 || row[0].GetString() != Table.Version)
            throw new InvalidDataException("Unsupported log format.");
        var timestamp = new DateTimeOffset(row[1].GetInt64(), TimeSpan.FromMinutes(row[2].GetInt32()));
        var values = row[5];
        if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() % 2 != 0)
            throw new InvalidDataException("Invalid log fields.");
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var i = 0; i < values.GetArrayLength(); i += 2)
            fields.Add(Name(values[i], Table.Fields), values[i + 1].Clone());
        return new(timestamp, Name(row[3], Levels), Name(row[4], Table.Events), fields,
            row.GetArrayLength() == 7 ? row[6].GetString() : null);
    }

    private static string Name(JsonElement value, string[] symbols)
    {
        if (value.ValueKind == JsonValueKind.String) return value.GetString()!;
        if (value.TryGetInt32(out var id) && id >= 0 && id < symbols.Length) return symbols[id];
        throw new InvalidDataException("Unknown log symbol.");
    }

    internal static string Decode(string line) => JsonSerializer.Serialize(Parse(line), JsonOptions);

    internal static void DecodeFile(string inputPath, string outputPath)
    {
        using var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        // Never overwrite a source log or an existing decoded file.
        using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var reader = new StreamReader(input, Encoding.UTF8);
        using var writer = new StreamWriter(output, new UTF8Encoding(false));
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            try { writer.WriteLine(Decode(line)); }
            catch (Exception ex) { throw new InvalidDataException($"Invalid log entry at line {lineNumber}.", ex); }
        }
    }
}
