using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Roadhog.Core.Radar;
using Roadhog.Infrastructure.Config;
using Roadhog.Infrastructure.Radar;

if (args.Length == 0 || args.Any(arg => arg.StartsWith("--") && arg != "--apply"))
{
    Console.Error.WriteLine("Usage: RadarMapCache <radar-map-directory> [--apply]");
    return 2;
}
var directory = Path.GetFullPath(args[0]);
var apply = args.Contains("--apply");
var options = new JsonSerializerOptions { WriteIndented = true };
var backupDirectory = Path.Combine(directory, ".navigation-cache-backups", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
var store = new JsonRadarMapStore(directory);
var files = Directory.GetFiles(directory, "*.json")
    .Where(path => uint.TryParse(Path.GetFileNameWithoutExtension(path), out _)).Order().ToArray();
foreach (var path in files)
{
    var before = File.ReadAllBytes(path);
    var json = JsonNode.Parse(before, documentOptions: new JsonDocumentOptions
    { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })!.AsObject();
    var watch = Stopwatch.StartNew();
    var loaded = await store.LoadAsync(uint.Parse(Path.GetFileNameWithoutExtension(path)));
    if (!loaded.Success || loaded.Value is not { Found: true } map)
        throw new InvalidDataException(path + ": " + loaded.Error);
    var cache = map.Document.NavigationCache!;
    if (!RadarNavigationGraphBuilder.Prepare(map.Document.Segments, cache).LoadedFromCache)
        throw new InvalidDataException("Generated cache failed validation: " + path);
    var existing = json["NavigationCache"]?.Deserialize<RadarNavigationCache>();
    var valid = existing is not null &&
                RadarNavigationGraphBuilder.Prepare(map.Document.Segments, existing).LoadedFromCache;
    json["NavigationCache"] = JsonSerializer.SerializeToNode(cache, options);
    var after = JsonSerializer.SerializeToUtf8Bytes(json, options);
    Console.WriteLine($"{Path.GetFileName(path)}: segments={map.Document.Segments.Count}, points={cache.Points.Count}, edges={cache.Edges.Count}, bytes={after.Length}, prepareMs={watch.Elapsed.TotalMilliseconds:F2}, cache={(valid ? "current" : "rebuild")}");
    if (!apply || valid) continue;

    // Only add/replace derived data; preserve all original map fields and segment records.
    if (!before.AsSpan().SequenceEqual(File.ReadAllBytes(path)))
        throw new IOException("Map changed during preparation; no replacement performed: " + path);
    Directory.CreateDirectory(backupDirectory);
    var backupPath = Path.Combine(backupDirectory, Path.GetFileName(path));
    var temporaryPath = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp");
    try
    {
        await File.WriteAllBytesAsync(temporaryPath, after);
        if (!before.AsSpan().SequenceEqual(File.ReadAllBytes(path)))
            throw new IOException("Map changed before replacement: " + path);
        File.Replace(temporaryPath, path, backupPath);
        Console.WriteLine("  updated; backup=" + backupPath);
    }
    finally
    {
        if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
    }
}
return 0;
