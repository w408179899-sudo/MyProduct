using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Roadhog.Core.Accounts;
using Roadhog.Core.Profiles;
using Roadhog.Infrastructure.Config;
using Roadhog.Infrastructure.Profiles;

internal static class SkillTreeReleaseMigrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, Converters = { new JsonStringEnumConverter() }
    };

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static SkillConfigNode Node(uint id, string name, string type = "Active") => new()
    {
        SkillId = id, Name = name, BaseName = name, Type = type, ChainTimeMs = 875
    };

    private static ScriptSettings Settings()
    {
        var settings = new ScriptSettings { ProfileName = "legacy-migration" };
        settings.Skills.ExecutionTree = new() { Node(101, "first IV"), Node(104, "second II") };
        settings.Skills.ExecutionTree[0].Children = new() { Node(102, "chain III", "Chain") };
        settings.Skills.ExecutionTree[0].Children[0].Children = new() { Node(103, "final I", "Trigger") };
        settings.Skills.SystemExecutionTree = new() { Node(201, "system I") };
        settings.Skills.ManualMappings = new() { new() { SkillName = "manual II", SkillType = "Active", Key = "F4" } };
        settings.Skills.OpeningSkill = new() { Enabled = true, SkillId = 301, SkillName = "opener", Key = "F1" };
        settings.Skills.SpiritmasterAutoSkillLogicEnabled = true;
        settings.Skills.Spiritmaster.DotSkills.Add(new() { SkillId = 401, SkillName = "dot" });
        settings.Maintenance.DpMaintenanceRules.Add(new() { SkillId = 501, SkillName = "DP", Key = "F5", RequiredDp = 2000 });
        settings.Maintenance.HpMaintenanceRules.Add(new() { SkillId = 601, SkillName = "heal", Key = "F6", BelowPercent = 57 });
        settings.Team.Support.HealSkillRules.Add(new() { SkillId = 701, SkillName = "team", Key = "F7", BelowPercent = 63 });
        settings.SemiAuto.AttackWeaveEnabled = true;
        settings.SemiAuto.AttackWeaveDelayMs = 530;
        settings.Combat.SmartPreAimEnabled = true;
        settings.Paths.CombatPathName = "kept-path";
        return settings;
    }

    private static string Json(object value) => JsonSerializer.Serialize(value, JsonOptions);

    private static string SharedAndArchive(ScriptSettings settings)
    {
        var json = JsonNode.Parse(Json(settings))!.AsObject();
        json.Remove(nameof(ScriptSettings.SkillTreeReleaseMode));
        json.Remove(nameof(ScriptSettings.QuickbarSkills));
        return json.ToJsonString();
    }

    public static Task AutoAndCloneAsync()
    {
        var source = Settings();
        source.Skills.ExecutionTree.Insert(1, Node(900, "DP attack", "DP技能"));
        var sourceJson = Json(source);
        var archived = SharedAndArchive(source);
        var migrated = source.Clone();
        Check(migrated.SkillTreeReleaseMode == SkillTreeReleaseMode.QuickbarAvailability &&
            migrated.QuickbarSkills.ExecutionTree.Select(node => node.SkillId).SequenceEqual(new uint[] { 101, 104 }),
            "legacy auto migration preserves executable root priority and excludes DP maintenance roots");
        var first = migrated.QuickbarSkills.ExecutionTree[0];
        Check(first.ChainTimeMs == 875 && first.Children.Single().SkillId == 102 &&
            first.Children[0].ChainTimeMs == 875 && first.Children[0].Children.Single().SkillId == 103 &&
            first.Children[0].Children[0].Type == "Trigger", "migration preserves every level, identity, type and chain window");
        Check(SharedAndArchive(migrated) == archived && Json(source) == sourceJson,
            "clone migration leaves source, old attack archives and every shared setting unchanged");
        var migratedJson = Json(migrated);
        Check(!SkillTreeReleaseMigration.Migrate(migrated) && Json(migrated) == migratedJson,
            "migration is idempotent after switching to the sole engine");
        first.Children[0].Children[0].Name = "edited";
        Check(source.Skills.ExecutionTree[0].Children[0].Children[0].Name == "final I" &&
            migrated.Skills.ExecutionTree[0].Children[0].Children[0].Name == "final I",
            "new editor mutations cannot affect source or compatibility archive children");
        return Task.CompletedTask;
    }

    public static Task ExistingAndExplicitEmptyAsync()
    {
        var settings = Settings();
        settings.QuickbarSkills.ExecutionTree.Add(Node(801, "new priority"));
        var newTree = Json(settings.QuickbarSkills);
        Check(SkillTreeReleaseMigration.Migrate(settings) && Json(settings.QuickbarSkills) == newTree,
            "existing new priorities win over a stale legacy marker");
        settings = Settings();
        settings.SkillTreeReleaseMode = SkillTreeReleaseMode.QuickbarAvailability;
        var archived = SharedAndArchive(settings);
        Check(!SkillTreeReleaseMigration.Migrate(settings) && settings.QuickbarSkills.ExecutionTree.Count == 0 &&
            SharedAndArchive(settings) == archived, "explicit quickbar empty remains intentional and never copies the archive");
        settings.QuickbarSkills = null!;
        var normalized = settings.Clone();
        Check(normalized.QuickbarSkills.ExecutionTree.Count == 0 && normalized.SkillTreeReleaseMode == SkillTreeReleaseMode.QuickbarAvailability,
            "explicit quickbar null configuration normalizes to empty without importing old skills");
        var raw = JsonNode.Parse(Json(Settings()))!.AsObject();
        raw.Remove(nameof(ScriptSettings.SkillTreeReleaseMode));
        raw.Remove(nameof(ScriptSettings.QuickbarSkills));
        var missing = JsonSerializer.Deserialize<ScriptSettings>(raw.ToJsonString(), JsonOptions)!;
        Check(missing.SkillTreeReleaseMode == SkillTreeReleaseMode.Legacy && missing.Clone().QuickbarSkills.ExecutionTree.Count == 2,
            "missing old JSON fields remain identifiable and load into the new engine");
        foreach (var marker in new JsonNode[] { JsonValue.Create(0)!, JsonValue.Create("Legacy")! })
        {
            raw[nameof(ScriptSettings.SkillTreeReleaseMode)] = marker;
            var legacy = JsonSerializer.Deserialize<ScriptSettings>(raw.ToJsonString(), JsonOptions)!;
            Check(legacy.Clone().QuickbarSkills.ExecutionTree[0].SkillId == 101,
                "both numeric and string legacy enum values remain readable");
        }
        return Task.CompletedTask;
    }

    public static Task AlternateLegacyModesAsync()
    {
        var settings = Settings();
        settings.Skills.Mode = SkillConfigurationMode.SystemClassification;
        settings.Skills.SystemExecutionTree[0].Children.Add(Node(202, "system chain", "Chain"));
        settings.Skills.SystemExecutionTree.Add(Node(900, "DP", "DP技能"));
        var archived = SharedAndArchive(settings);
        var normalized = settings.Clone();
        Check(normalized.QuickbarSkills.ExecutionTree.Single().SkillId == 201 &&
            normalized.QuickbarSkills.ExecutionTree[0].Children.Single().SkillId == 202 &&
            normalized.Skills.Mode == SkillConfigurationMode.SystemClassification && SharedAndArchive(normalized) == archived,
            "system migration selects its actual tree without overwriting archived mode or shared state");
        settings = Settings();
        settings.Skills.Mode = SkillConfigurationMode.ManualMapping;
        settings.Skills.ManualMappings.AddRange(new[]
        {
            new ManualSkillMappingConfig { SkillName = "no key", SkillType = "Active", Key = " " },
            new ManualSkillMappingConfig { SkillName = "", SkillType = "Active", Key = "F2" },
            new ManualSkillMappingConfig { SkillName = "DP manual", SkillType = "DP技能", Key = "F3" },
            new ManualSkillMappingConfig { SkillName = "second manual", SkillType = "Trigger", Key = "NumPad1" }
        });
        archived = SharedAndArchive(settings);
        normalized = settings.Clone();
        Check(normalized.QuickbarSkills.ExecutionTree.Select(node => node.Name).SequenceEqual(new[] { "manual II", "second manual" }) &&
            normalized.QuickbarSkills.ExecutionTree.All(node => node.SkillId == 0 && node.Name == node.BaseName) &&
            normalized.QuickbarSkills.ExecutionTree[1].Type == "Trigger" && SharedAndArchive(normalized) == archived,
            "manual migration retains valid ordered skill identities and excludes inactive keyless, nameless and DP rows");
        return Task.CompletedTask;
    }

    public static Task NullAndMalformedNodesAsync()
    {
        var settings = Settings();
        settings.Skills.ExecutionTree = new()
        {
            null!, new(), new() { BaseName = "base-only rank unknown", Children = null! },
            new() { SkillId = 105, Name = "malformed tier ???", BaseName = null!, Type = null!,
                Children = new() { null!, new(), new() { SkillId = 106, Name = "id child", Children = null! } } }
        };
        settings.Skills.SystemExecutionTree = null!;
        settings.Skills.ManualMappings = null!;
        settings.QuickbarSkills = null!;
        Check(SkillTreeReleaseMigration.Migrate(settings) && settings.QuickbarSkills.ExecutionTree.Count == 2 &&
            settings.QuickbarSkills.ExecutionTree[0].Name == "base-only rank unknown" &&
            settings.QuickbarSkills.ExecutionTree[1].Children.Single().SkillId == 106,
            "malformed ranks need no parsing and nullable/identityless nodes cannot break migration");
        var normalized = settings.Clone();
        Check(normalized.Skills.SystemExecutionTree.Count == 0 && normalized.Skills.ManualMappings.Count == 0 &&
            normalized.QuickbarSkills.ExecutionTree[0].Children.Count == 0, "nested null lists normalize safely through clone");
        settings = new() { Skills = null!, QuickbarSkills = new() { ExecutionTree = null! } };
        Check(SkillTreeReleaseMigration.Migrate(settings) && settings.QuickbarSkills.ExecutionTree.Count == 0 &&
            settings.Clone().Skills is not null, "null legacy settings and null new trees normalize safely");
        return Task.CompletedTask;
    }

    public static async Task StoreLoadAndReloadAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RoadhogSkillMigration-" + Guid.NewGuid().ToString("N"));
        var profileDirectory = Path.Combine(directory, "profiles");
        Directory.CreateDirectory(profileDirectory);
        try
        {
            var source = Settings();
            var rawAccount = JsonNode.Parse(Json(new AccountConfig { AccountName = "migration", ScriptSettings = source }))!.AsObject();
            rawAccount[nameof(AccountConfig.ScriptSettings)]!.AsObject().Remove(nameof(ScriptSettings.SkillTreeReleaseMode));
            rawAccount[nameof(AccountConfig.ScriptSettings)]!.AsObject().Remove(nameof(ScriptSettings.QuickbarSkills));
            var accountPath = Path.Combine(directory, "accounts.json");
            var accountJson = new JsonObject { ["Version"] = 1, ["Accounts"] = new JsonArray(rawAccount) }.ToJsonString();
            await File.WriteAllTextAsync(accountPath, accountJson);
            var profile = new ScriptProfileDocument { Name = source.ProfileName, Settings = source };
            var rawProfile = JsonNode.Parse(Json(profile))!.AsObject();
            rawProfile[nameof(ScriptProfileDocument.Settings)]!.AsObject().Remove(nameof(ScriptSettings.SkillTreeReleaseMode));
            rawProfile[nameof(ScriptProfileDocument.Settings)]!.AsObject().Remove(nameof(ScriptSettings.QuickbarSkills));
            var profilePath = Path.Combine(profileDirectory, source.ProfileName + ".json");
            var profileJson = rawProfile.ToJsonString();
            await File.WriteAllTextAsync(profilePath, profileJson);
            var accounts = new JsonAccountConfigStore(accountPath);
            var profiles = new JsonScriptProfileStore(profileDirectory);
            var accountResult = await accounts.LoadAllAsync();
            var profileResult = await profiles.LoadAsync(source.ProfileName);
            Check(accountResult.Success && profileResult.Success, "old account and profile files load successfully");
            var loadedAccount = accountResult.Value!.Single();
            var loadedProfile = profileResult.Value!;
            var expected = source.Clone();
            Check(Json(loadedAccount.ScriptSettings!) == Json(expected) && Json(loadedProfile.Settings) == Json(expected),
                "both real load paths migrate all chain levels and preserve shared configuration and archives");
            Check(await File.ReadAllTextAsync(accountPath) == accountJson && await File.ReadAllTextAsync(profilePath) == profileJson,
                "read-only migration does not rewrite any source file");
            for (var i = 0; i < 3; i++)
            {
                Check((await accounts.UpsertAsync(loadedAccount)).Success && (await profiles.SaveAsync(loadedProfile)).Success,
                    "normalized account and profile saves succeed");
                loadedAccount = (await accounts.LoadAllAsync()).Value!.Single();
                loadedProfile = (await profiles.LoadAsync(source.ProfileName)).Value!;
                Check(Json(loadedAccount.ScriptSettings!) == Json(expected) && Json(loadedProfile.Settings) == Json(expected),
                    "repeated save/load is idempotent and does not duplicate, drop or reorder skills");
            }
            loadedAccount.ScriptSettings!.QuickbarSkills.ExecutionTree.Clear();
            loadedProfile.Settings.QuickbarSkills.ExecutionTree.Clear();
            Check((await accounts.UpsertAsync(loadedAccount)).Success && (await profiles.SaveAsync(loadedProfile)).Success,
                "intentional empty active trees save");
            loadedAccount = (await accounts.LoadAllAsync()).Value!.Single();
            loadedProfile = (await profiles.LoadAsync(source.ProfileName)).Value!;
            Check(loadedAccount.ScriptSettings!.QuickbarSkills.ExecutionTree.Count == 0 && loadedProfile.Settings.QuickbarSkills.ExecutionTree.Count == 0 &&
                SharedAndArchive(loadedAccount.ScriptSettings) == SharedAndArchive(source) &&
                SharedAndArchive(loadedProfile.Settings) == SharedAndArchive(source),
                "clear-save-reload never revives archived old attacks or loses shared settings");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
