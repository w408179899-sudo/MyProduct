using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Roadhog.Core.Accounts;
using Roadhog.Core.Profiles;
using Roadhog.Infrastructure.Config;
using Roadhog.Infrastructure.Profiles;

internal static class SkillReleaseMigrationAudit
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() }
    };

    // Audit copies only. Never point a writable store at the source runtime.
    public static async Task RunAsync(string accountsPath, string profilesPath, string outputDirectory)
    {
        var sourceFiles = Directory.GetFiles(profilesPath, "*.json").Append(accountsPath).ToArray();
        var hashes = sourceFiles.ToDictionary(path => path, Hash);
        var accountJson = await File.ReadAllTextAsync(accountsPath);
        var rawAccounts = JsonNode.Parse(accountJson)!["Accounts"]!.Deserialize<List<AccountConfig>>(Options)!;
        var auditRoot = Path.Combine(Path.GetFullPath(outputDirectory), Guid.NewGuid().ToString("N"));
        var profileCopies = Path.Combine(auditRoot, "profiles");
        Directory.CreateDirectory(profileCopies);
        var accountCopy = Path.Combine(auditRoot, "accounts.json");
        await File.WriteAllTextAsync(accountCopy, accountJson);
        foreach (var source in sourceFiles.Where(path => path != accountsPath))
            File.Copy(source, Path.Combine(profileCopies, Path.GetFileName(source)));

        var accounts = new JsonAccountConfigStore(accountCopy);
        var loaded = await accounts.LoadAllAsync();
        Check(loaded.Success && loaded.Value!.Count == rawAccounts.Count, "copied account store loads every account");
        var migrated = 0;
        foreach (var original in rawAccounts)
        {
            var actual = loaded.Value!.Single(account => account.AccountName == original.AccountName);
            Verify(original.ScriptSettings!, actual.ScriptSettings!);
            if (original.ScriptSettings!.SkillTreeReleaseMode != SkillTreeReleaseMode.QuickbarAvailability) migrated++;
        }
        Check((await accounts.SaveAllAsync(loaded.Value!)).Success, "copied account store saves normalized settings");
        var reload = await accounts.LoadAllAsync();
        Check(reload.Success && Json(loaded.Value) == Json(reload.Value), "account save/reload is stable");

        var profiles = new JsonScriptProfileStore(profileCopies);
        var count = 0;
        foreach (var source in Directory.GetFiles(profilesPath, "*.json"))
        {
            var original = JsonSerializer.Deserialize<ScriptProfileDocument>(await File.ReadAllTextAsync(source), Options)!;
            var name = Path.GetFileNameWithoutExtension(source);
            var profile = await profiles.LoadAsync(name);
            Check(profile.Success, "copied profile loads: " + name);
            // Existing profile-store normalization always derives this from the document name.
            original.Settings.ProfileName = string.IsNullOrWhiteSpace(original.Name) ? name : original.Name.Trim();
            Verify(original.Settings, profile.Value!.Settings);
            if (original.Settings.SkillTreeReleaseMode != SkillTreeReleaseMode.QuickbarAvailability) migrated++;
            var save = await profiles.SaveAsync(profile.Value);
            Check(save.Success, "copied profile saves: " + name + "; " + save.Error);
            var second = await profiles.LoadAsync(name);
            Check(second.Success && Json(profile.Value.Settings) == Json(second.Value!.Settings), "profile settings survive save/reload: " + name);
            count++;
        }
        foreach (var source in sourceFiles)
            Check(Hash(source) == hashes[source], "the source runtime configuration remains byte-identical");
        Console.WriteLine($"PASS skill release migration snapshot audit: accounts={rawAccounts.Count}, profiles={count}, legacyMarkers={migrated}; original files unchanged");
    }

    private static void Verify(ScriptSettings original, ScriptSettings actual)
    {
        Check(actual.SkillTreeReleaseMode == SkillTreeReleaseMode.QuickbarAvailability, "the only runtime mode is quickbar");
        Check(Json(original.Skills) == Json(actual.Skills), "all archived skill fields and shared opening/spiritmaster settings are preserved");
        var before = JsonSerializer.SerializeToNode(original, Options)!;
        var after = JsonSerializer.SerializeToNode(actual, Options)!;
        // Existing store loads already trim cleanup lists and merge default
        // cleanup rules. Compare against that unchanged pre-migration normalization.
        before["Maintenance"] = JsonSerializer.SerializeToNode(original.Maintenance.Clone(), Options);
        foreach (var property in new[] { "SkillTreeReleaseMode", "QuickbarSkills" })
        {
            before.AsObject().Remove(property);
            after.AsObject().Remove(property);
        }
        Check(JsonNode.DeepEquals(before, after), "all unrelated account settings are preserved; differing fields=" +
            string.Join(",", before.AsObject().Where(pair => !JsonNode.DeepEquals(pair.Value, after[pair.Key])).Select(pair => pair.Key)));
        if (original.SkillTreeReleaseMode == SkillTreeReleaseMode.QuickbarAvailability || original.QuickbarSkills.ExecutionTree.Count > 0)
            Check(Json(original.QuickbarSkills) == Json(actual.QuickbarSkills), "existing new trees including deliberate empty trees remain authoritative");
        else if (original.Skills.ExecutionTree.Any(node => !node.Type.Contains("DP", StringComparison.OrdinalIgnoreCase)) && original.Skills.Mode == SkillConfigurationMode.Auto)
            Check(actual.QuickbarSkills.ExecutionTree.Count > 0, "an old-only executable tree cannot disappear");
        Check(Json(actual) == Json(actual.Clone()), "normalized settings are idempotent");
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, Options);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
