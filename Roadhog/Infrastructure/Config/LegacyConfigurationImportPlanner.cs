using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Roadhog.Core.Accounts;
using Roadhog.Infrastructure.Composition;

namespace Roadhog.Infrastructure.Config;

internal sealed record LegacyImportFile(string Target, byte[] Content);

/// <summary>Plans new files and rewrites only imported references; never overwrites a shared library.</summary>
internal sealed class LegacyConfigurationImportPlanner(RoadhogServiceOptions options)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private static readonly JsonDocumentOptions DocumentOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
    private readonly List<LegacyImportFile> _files = [];
    private sealed record LibraryDocument(string Source, string FileName, byte[] Bytes, JsonObject Document);

    public async Task<IReadOnlyList<LegacyImportFile>> PlanAsync(string source, string sourceName,
        List<AccountConfig> accounts, CancellationToken token)
    {
        var configRoot = Path.GetDirectoryName(Path.GetFullPath(options.AccountConfigPath))!;
        var privateRoot = Path.Combine(configRoot, "imported", Guid.NewGuid().ToString("N"));
        var paths = await ReadLibraryAsync(Path.Combine(source, "paths"), privateRoot, token).ConfigureAwait(false);
        var profiles = await ReadLibraryAsync(Path.Combine(source, "profiles"), privateRoot, token).ConfigureAwait(false);
        var accountNodes = accounts.Select(a => JsonSerializer.SerializeToNode(a, Json)!.AsObject()).ToArray();
        var pathReferences = profiles.Select(p => (JsonNode)p.Document).Concat(accountNodes)
            .SelectMany(n => References(n, "PathName")).ToArray();
        var pathNames = await PlanNamedLibraryAsync(paths, options.PathLibraryDirectory, sourceName,
            pathReferences, null, token).ConfigureAwait(false);
        var profileReferences = accountNodes.SelectMany(n => References(n, "ProfileName")).ToArray();
        var profileNames = await PlanNamedLibraryAsync(profiles, options.ProfileLibraryDirectory, sourceName,
            profileReferences, pathNames, token).ConfigureAwait(false);

        // Lists and maps have fixed filenames/IDs: preserve their source scope instead of renaming IDs or merging rules.
        var sourceMaps = Path.Combine(source, "radar-maps");
        var targetMaps = Path.Combine(privateRoot, "radar-maps");
        foreach (var file in EnumerateFiles(sourceMaps, "*.json"))
            _files.Add(new(Path.Combine(targetMaps, Path.GetRelativePath(sourceMaps, file)), await File.ReadAllBytesAsync(file, token).ConfigureAwait(false)));
        foreach (var name in new[] { JsonBagCleanupNameListStore.DefaultFileName, JsonBagCleanupNameListStore.LegacyFileName })
        {
            var path = Path.Combine(source, name);
            if (File.Exists(path)) _files.Add(new(Path.Combine(privateRoot, name), await File.ReadAllBytesAsync(path, token).ConfigureAwait(false)));
        }

        for (var i = 0; i < accounts.Count; i++)
        {
            RewriteReferences(accountNodes[i], "PathName", pathNames);
            RewriteReferences(accountNodes[i], "ProfileName", profileNames);
            var account = accountNodes[i].Deserialize<AccountConfig>(Json)!;
            if (IsDefaultResource(account.RadarMapDirectory, sourceMaps))
                account.RadarMapDirectory = Path.GetRelativePath(configRoot, targetMaps);
            if (IsDefaultResource(account.BagCleanupNameListPath, Path.Combine(source, JsonBagCleanupNameListStore.DefaultFileName)))
                account.BagCleanupNameListPath = Path.GetRelativePath(configRoot, Path.Combine(privateRoot, JsonBagCleanupNameListStore.DefaultFileName));
            accounts[i] = account;
        }
        _files.Add(new(Path.Combine(privateRoot, "import-summary.json"), JsonSerializer.SerializeToUtf8Bytes(new
        {
            sourceDirectory = source, importedAt = DateTimeOffset.UtcNow,
            pathNames, profileNames,
            accounts = accounts.Select(a => new { a.AccountName, a.InstanceId })
        }, Json)));
        token.ThrowIfCancellationRequested();
        return _files;
    }

    private async Task<Dictionary<string, string>> PlanNamedLibraryAsync(IReadOnlyList<LibraryDocument> documents,
        string targetRoot, string sourceName, IReadOnlyList<string> references,
        IReadOnlyDictionary<string, string>? pathNames, CancellationToken token)
    {
        targetRoot = Path.GetFullPath(targetRoot);
        var reserved = documents.Select(d => d.FileName).Concat(references.Select(FileName)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in documents)
        {
            token.ThrowIfCancellationRequested();
            var document = (JsonObject)item.Document.DeepClone();
            var changed = pathNames is not null && RewriteReferences(document, "PathName", pathNames);
            var content = changed ? JsonSerializer.SerializeToUtf8Bytes(document, Json) : item.Bytes;
            var target = Path.Combine(targetRoot, item.FileName + ".json");
            // Resolve by the actual filename, matching the runtime stores, including names containing invalid filename characters.
            var name = ReadString(document, "Name");
            if (string.IsNullOrWhiteSpace(name) || !FileName(name).Equals(item.FileName, StringComparison.OrdinalIgnoreCase)) name = item.FileName;
            if (File.Exists(target))
            {
                var existing = await File.ReadAllBytesAsync(target, token).ConfigureAwait(false);
                if (existing.AsSpan().SequenceEqual(content)) { names.Add(item.FileName, name); continue; }
            }
            if (File.Exists(target) || Directory.Exists(target))
            {
                name = UniqueName(name, sourceName, targetRoot, reserved);
                target = Path.Combine(targetRoot, FileName(name) + ".json");
                Set(document, "Name", name);
                if (pathNames is not null && Find(document, "Settings") is JsonObject settings) Set(settings, "ProfileName", name);
                content = JsonSerializer.SerializeToUtf8Bytes(document, Json);
            }
            names.Add(item.FileName, name);
            _files.Add(new(target, content));
        }
        // A route/profile missing from the old source must not silently resolve to another client's same-named file.
        foreach (var reference in references)
        {
            var key = FileName(reference);
            if (names.ContainsKey(key)) continue;
            if (File.Exists(Path.Combine(targetRoot, key + ".json")) || Directory.Exists(Path.Combine(targetRoot, key + ".json")))
                names[key] = UniqueName(reference, sourceName, targetRoot, reserved);
        }
        return names;
    }

    private async Task<IReadOnlyList<LibraryDocument>> ReadLibraryAsync(string directory, string privateRoot, CancellationToken token)
    {
        var documents = new List<LibraryDocument>();
        foreach (var file in EnumerateFiles(directory, "*.json"))
        {
            token.ThrowIfCancellationRequested();
            var bytes = await File.ReadAllBytesAsync(file, token).ConfigureAwait(false);
            var relative = Path.GetRelativePath(directory, file);
            // Runtime path/profile stores read the top level only. Preserve nested archival files without introducing new references.
            if (Path.GetDirectoryName(relative) is { Length: > 0 })
            {
                _files.Add(new(Path.Combine(privateRoot, "library-backup", Path.GetFileName(directory), relative), bytes));
                continue;
            }
            JsonObject document;
            try
            {
                document = JsonNode.Parse(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'), documentOptions: DocumentOptions) as JsonObject
                    ?? throw new JsonException("配置必须是 JSON 对象。");
            }
            catch (JsonException ex) { throw new InvalidDataException("旧配置无法读取，未导入：" + file + "；" + ex.Message, ex); }
            documents.Add(new(file, Path.GetFileNameWithoutExtension(file), bytes, document));
        }
        return documents;
    }

    private static IEnumerable<string> EnumerateFiles(string root, string pattern) => Directory.Exists(root)
        ? Directory.EnumerateFiles(root, pattern, new EnumerationOptions
        { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false })
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
        : [];

    private string UniqueName(string name, string sourceName, string root, HashSet<string> reserved)
    {
        static string Short(string value, int max) => value.Length <= max ? value : value[..max];
        var stem = Short(name.Trim(), 64) + "（" + Short(sourceName.Trim(), 24) + "）";
        for (var index = 1; ; index++)
        {
            var candidate = stem + (index == 1 ? "" : "_" + index);
            var key = FileName(candidate);
            var target = Path.Combine(root, key + ".json");
            if (reserved.Contains(key) || File.Exists(target) || Directory.Exists(target) ||
                _files.Any(f => f.Target.Equals(target, StringComparison.OrdinalIgnoreCase))) continue;
            reserved.Add(key);
            return candidate;
        }
    }

    private static string FileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        return string.IsNullOrEmpty(safe) ? "path" : safe;
    }
    private static bool IsDefaultResource(string configured, string sibling) => string.IsNullOrWhiteSpace(configured)
        || Path.GetFullPath(configured).Equals(Path.GetFullPath(sibling), StringComparison.OrdinalIgnoreCase);
    private static JsonNode? Find(JsonObject node, string name) => node.FirstOrDefault(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
    private static string? ReadString(JsonObject node, string name) => Find(node, name) is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    private static void Set(JsonObject node, string name, string value)
    {
        var key = node.Select(p => p.Key).FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? name;
        node[key] = value;
    }
    private static IEnumerable<string> References(JsonNode? node, string suffix)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj)
            {
                if (pair.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && pair.Value is JsonValue value &&
                    value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)) yield return text;
                else foreach (var nested in References(pair.Value, suffix)) yield return nested;
            }
        }
        else if (node is JsonArray array)
            foreach (var item in array) foreach (var text in References(item, suffix)) yield return text;
    }
    private static bool RewriteReferences(JsonNode? node, string suffix, IReadOnlyDictionary<string, string> names)
    {
        var changed = false;
        if (node is JsonObject obj)
        {
            foreach (var pair in obj.ToArray())
            {
                if (pair.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && pair.Value is JsonValue value &&
                    value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) &&
                    names.TryGetValue(FileName(text), out var replacement) && text != replacement)
                { obj[pair.Key] = replacement; changed = true; }
                else changed |= RewriteReferences(pair.Value, suffix, names);
            }
        }
        else if (node is JsonArray array)
            foreach (var item in array) changed |= RewriteReferences(item, suffix, names);
        return changed;
    }
}
