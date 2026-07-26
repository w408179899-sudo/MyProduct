using System.Globalization;
using System.Text.RegularExpressions;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.ToolBridge;

internal static partial class ToolOutputParsers
{
    public static IReadOnlyList<SkillSnapshot> ParseSkills(IEnumerable<string> lines)
    {
        var result = new List<SkillSnapshot>();

        foreach (var line in lines)
        {
            var match = SkillLineRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var skill = new SkillSnapshot(
                ParseUInt(match.Groups["id"].Value),
                match.Groups["name"].Value,
                ParseInt(match.Groups["highest"].Value),
                ParseInt(match.Groups["itemlevel"].Value),
                EmptyToNull(match.Groups["base"].Value),
                TryParseNullableInt(match.Groups["tier"].Value),
                ParseUInt(match.Groups["toggle"].Value) != 0,
                ParseUInt(match.Groups["cooldown"].Value),
                ParseUInt(match.Groups["cooldownEnd"].Value),
                ExtractQuotedValue(line, "XmlActivation"),
                ExtractTokenValue(line, "XmlTags"),
                ExtractQuotedValue(line, "XmlTargetSlot"),
                ExtractQuotedValue(line, "XmlChainCategory"),
                ExtractQuotedValue(line, "XmlPrechainCategory"),
                ExtractQuotedValue(line, "XmlChainTime"),
                ExtractQuotedValue(line, "XmlCounterSkill"),
                ExtractTokenValue(line, "XmlCostDp"),
                ExtractQuotedValue(line, "XmlSkillCategory"),
                ExtractQuotedValue(line, "XmlType"),
                ExtractQuotedValue(line, "XmlSubType"),
                ExtractQuotedValue(line, "XmlDispelCategory"),
                ExtractQuotedValue(line, "XmlFirstTarget"),
                ExtractQuotedValue(line, "XmlTargetRelation"),
                ExtractQuotedValue(line, "XmlTargetRange"),
                ExtractListValue(line, "XmlEffects"),
                ExtractOptionalInt(line, "XmlEffectRemainMs"),
                ExtractOptionalInt(line, "XmlEffectCheckTimeMs"));

            result.Add(skill);
        }

        return result;
    }

    public static IReadOnlyList<InventoryItemSnapshot> ParseInventory(IEnumerable<string> lines)
    {
        var result = new List<InventoryItemSnapshot>();

        foreach (var line in lines)
        {
            var match = InventoryLineRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            result.Add(new InventoryItemSnapshot(
                ParseUInt(match.Groups["template"].Value),
                ParseULong(match.Groups["instance"].Value),
                match.Groups["name"].Value,
                ParseUInt(match.Groups["count"].Value),
                ParseSlot(match.Groups["slot"].Value),
                IsYes(match.Groups["equipped"].Value),
                ParseOptionalUInt(match.Groups["type"])));
        }

        return result;
    }

    public static PlayerSnapshot? ParseLastPlayerSnapshot(IEnumerable<string> lines)
    {
        PlayerSnapshot? latest = null;

        foreach (var line in lines)
        {
            var match = PlayerLineRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var characterName = ExtractQuotedValue(line, "Name") ?? string.Empty;
            latest = new PlayerSnapshot(
                ParseUShort(match.Groups["entity"].Value),
                ParseUShort(match.Groups["target"].Value),
                characterName,
                ParseUInt(match.Groups["hp"].Value),
                ParseUInt(match.Groups["maxHp"].Value),
                ParseUInt(match.Groups["mp"].Value),
                ParseUInt(match.Groups["maxMp"].Value),
                ParseUShort(match.Groups["dp"].Value),
                TryParsePosition(match),
                DateTimeOffset.Now,
                Level: ExtractOptionalUShort(line, "Level"),
                CharacterClass: ExtractQuotedValue(line, "Class") ?? ExtractTokenValue(line, "Class") ?? string.Empty,
                CharacterClassId: ExtractOptionalClassId(line, "ClassId"));
        }

        return latest;
    }

    public static IReadOnlyList<WorldObjectSnapshot> ParseWorldObjects(IEnumerable<string> lines)
    {
        var result = new List<WorldObjectSnapshot>();

        foreach (var line in lines)
        {
            var match = WorldObjectLineRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var isMonster = match.Groups["isMonster"].Value;
            if (!string.Equals(isMonster, "yes", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var aggressive = ParseAggressive(line);
            result.Add(new WorldObjectSnapshot(
                ParseUShort(match.Groups["entity"].Value),
                ParseUInt(match.Groups["server"].Value),
                match.Groups["name"].Value,
                "monster",
                new Vector3Snapshot(
                    ParseFloat(match.Groups["x"].Value),
                    ParseFloat(match.Groups["y"].Value),
                    ParseFloat(match.Groups["z"].Value)),
                ParseDouble(match.Groups["dist"].Value),
                TargetServerObjectId: ParseOptionalUInt(match.Groups["targetServer"]),
                IsTargetingLocalPlayer: IsYes(match.Groups["targetingMe"]),
                AggressiveKnown: aggressive.Known,
                IsAggressiveToPlayer: aggressive.IsAggressiveToPlayer,
                AggressiveSource: aggressive.Source));
        }

        return result;
    }

    public static IReadOnlyList<LootCorpseSnapshot> ParseLootCorpses(IEnumerable<string> lines)
    {
        var result = new List<LootCorpseSnapshot>();

        foreach (var line in lines)
        {
            var match = LootCorpseLineRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            result.Add(new LootCorpseSnapshot(
                ParseUShort(match.Groups["entity"].Value),
                ParseUInt(match.Groups["server"].Value),
                ParseUShort(match.Groups["entityType"].Value),
                ParseUInt(match.Groups["objectType"].Value),
                ParseUInt(match.Groups["template"].Value),
                ParseUShort(match.Groups["level"].Value),
                match.Groups["name"].Value,
                new Vector3Snapshot(
                    ParseFloat(match.Groups["x"].Value),
                    ParseFloat(match.Groups["y"].Value),
                    ParseFloat(match.Groups["z"].Value)),
                ParseDouble(match.Groups["dist"].Value),
                ParseUInt(match.Groups["hp"].Value),
                ParseUInt(match.Groups["maxHp"].Value),
                byte.Parse(match.Groups["hpPercent"].Value, CultureInfo.InvariantCulture),
                ParseHexUInt(match.Groups["lootableRaw"].Value),
                ParseHexUInt(match.Groups["interactionState"].Value),
                DateTimeOffset.Now));
        }

        return result;
    }

    public static IReadOnlyList<GatherObjectSnapshot> ParseGatherObjects(IEnumerable<string> lines)
    {
        var latestByServerObjectId = new Dictionary<uint, GatherObjectSnapshot>();
        var capturedAt = DateTimeOffset.Now;

        foreach (var line in lines)
        {
            if (!line.StartsWith("#", StringComparison.Ordinal) ||
                !line.Contains(" Gather=", StringComparison.Ordinal) ||
                !TryExtractUInt(line, "ServerId", out var serverObjectId) ||
                serverObjectId == 0 ||
                !TryExtractUInt(line, "SourceId", out var gatherSourceId))
            {
                continue;
            }

            var distance = TryExtractDouble(line, "Dist");
            var position = ExtractPosition(line, "Pos");
            var spawnPosition = ExtractPosition(line, "Spawn");
            var snapshot = new GatherObjectSnapshot(
                TryExtractUShort(line, "EntityId"),
                serverObjectId,
                gatherSourceId,
                ExtractQuotedValue(line, "Name") ?? string.Empty,
                TryExtractUShort(line, "DisplayLevel"),
                TryExtractByte(line, "StateOrRemain"),
                TryExtractFloat(line, "Radius"),
                0,
                position,
                spawnPosition,
                distance,
                line.Contains("[TARGET]", StringComparison.Ordinal),
                null,
                capturedAt);
            latestByServerObjectId[serverObjectId] = snapshot;
        }

        return latestByServerObjectId.Values
            .OrderBy(item => item.DistanceToLocalPlayer ?? double.MaxValue)
            .ThenBy(item => item.ServerObjectId)
            .ToArray();
    }

    private static Vector3Snapshot? TryParsePosition(Match match)
    {
        if (!match.Groups["x"].Success || !match.Groups["y"].Success || !match.Groups["z"].Success)
        {
            return null;
        }

        return new Vector3Snapshot(
            ParseFloat(match.Groups["x"].Value),
            ParseFloat(match.Groups["y"].Value),
            ParseFloat(match.Groups["z"].Value));
    }

    private static string? EmptyToNull(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? ExtractQuotedValue(string line, string fieldName)
    {
        var match = Regex.Match(line, fieldName + "=\"(?<value>[^\"]*)\"", RegexOptions.CultureInvariant);
        return match.Success ? EmptyToNull(match.Groups["value"].Value) : null;
    }

    private static string? ExtractTokenValue(string line, string fieldName)
    {
        var match = Regex.Match(line, fieldName + "=(?<value>[^ ]+)", RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups["value"].Value;
        return string.Equals(value, "n/a", StringComparison.OrdinalIgnoreCase) ? null : EmptyToNull(value);
    }

    private static bool TryExtractUInt(string line, string fieldName, out uint value)
    {
        return uint.TryParse(
            ExtractTokenValue(line, fieldName),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static ushort TryExtractUShort(string line, string fieldName)
    {
        return ushort.TryParse(
            ExtractTokenValue(line, fieldName),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : (ushort)0;
    }

    private static byte TryExtractByte(string line, string fieldName)
    {
        return byte.TryParse(
            ExtractTokenValue(line, fieldName),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : (byte)0;
    }

    private static float TryExtractFloat(string line, string fieldName)
    {
        return float.TryParse(
            ExtractTokenValue(line, fieldName),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0;
    }

    private static double? TryExtractDouble(string line, string fieldName)
    {
        return double.TryParse(
            ExtractTokenValue(line, fieldName),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    private static Vector3Snapshot? ExtractPosition(string line, string fieldName)
    {
        var match = Regex.Match(
            line,
            @"\b" + Regex.Escape(fieldName) +
            @"=X=(?<x>-?\d+(?:\.\d+)?)\s+Y=(?<y>-?\d+(?:\.\d+)?)\s+Z=(?<z>-?\d+(?:\.\d+)?)",
            RegexOptions.CultureInvariant);
        return match.Success
            ? new Vector3Snapshot(
                ParseFloat(match.Groups["x"].Value),
                ParseFloat(match.Groups["y"].Value),
                ParseFloat(match.Groups["z"].Value))
            : null;
    }

    private static string? ExtractListValue(string line, string fieldName)
    {
        var match = Regex.Match(line, fieldName + @"=\[(?<value>[^\]]*)\]", RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups["value"].Value
            .Replace("n/a", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim(',', ' ');
        return EmptyToNull(value);
    }

    private static ushort ExtractOptionalUShort(string line, string fieldName)
    {
        var value = ExtractTokenValue(line, fieldName);
        return ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : (ushort)0;
    }

    private static int? ExtractOptionalInt(string line, string fieldName)
    {
        var value = ExtractTokenValue(line, fieldName);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static AionClassId? ExtractOptionalClassId(string line, string fieldName)
    {
        var value = ExtractTokenValue(line, fieldName);
        if (!uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ||
            !AionClassCatalog.TryFromRaw(parsed, out var classId))
        {
            return null;
        }

        return classId;
    }

    private static (bool Known, bool IsAggressiveToPlayer, string? Source) ParseAggressive(string line)
    {
        var match = Regex.Match(
            line,
            @"\bAggressive=(?<value>yes|no|n/a)(?:\((?<source>[^)]*)\))?",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return (false, false, null);
        }

        var value = match.Groups["value"].Value;
        if (string.Equals(value, "n/a", StringComparison.OrdinalIgnoreCase))
        {
            return (false, false, null);
        }

        var source = match.Groups["source"].Success
            ? EmptyToNull(match.Groups["source"].Value)
            : null;
        return (true, string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase), source);
    }

    private static int? TryParseNullableInt(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static int ParseInt(string value)
    {
        return int.Parse(value, CultureInfo.InvariantCulture);
    }

    private static uint ParseUInt(string value)
    {
        return uint.Parse(value, CultureInfo.InvariantCulture);
    }

    private static ulong ParseULong(string value)
    {
        return ulong.Parse(value, CultureInfo.InvariantCulture);
    }

    private static int ParseSlot(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : -1;
    }

    private static uint ParseHexUInt(string value)
    {
        return uint.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static uint ParseOptionalUInt(Group group)
    {
        return group.Success && uint.TryParse(group.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    private static ushort ParseUShort(string value)
    {
        return ushort.Parse(value, CultureInfo.InvariantCulture);
    }

    private static float ParseFloat(string value)
    {
        return float.Parse(value, CultureInfo.InvariantCulture);
    }

    private static double ParseDouble(string value)
    {
        return double.Parse(value, CultureInfo.InvariantCulture);
    }

    private static bool IsYes(Group group)
    {
        return group.Success && string.Equals(group.Value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsYes(string value)
    {
        return string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"#\d+\s+Id=(?<id>\d+).*?HighestLevel=(?<highest>\d+).*?ItemLevel=(?<itemlevel>\d+).*?Name=""(?<name>[^""]*)""(?: Base=""(?<base>[^""]*)"" Tier=(?<tier>\d+))?.*?Toggle=(?<toggle>\d+).*?Cooldown=(?<cooldown>\d+)/(?<cooldownEnd>\d+)", RegexOptions.Compiled)]
    private static partial Regex SkillLineRegex();

    [GeneratedRegex(@"#\d+\s+Slot=(?<slot>-?\d+|n/a).*?\sInstanceId=(?<instance>\d+)\s+TemplateId=(?<template>\d+)\s+Count=(?<count>\d+)\s+Name=""(?<name>[^""]*)""(?:.*?\sType=(?<type>\d+))?.*?\sEquipped=(?<equipped>yes|no)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex InventoryLineRegex();

    [GeneratedRegex(@"EntityId=(?<entity>\d+)\s+TargetId=(?<target>\d+).*?HP=(?<hp>\d+)/(?<maxHp>\d+).*?MP=(?<mp>\d+)/(?<maxMp>\d+).*?DP=(?<dp>\d+).*?Pos=(?:n/a|X=(?<x>-?\d+(?:\.\d+)?)\s+Y=(?<y>-?\d+(?:\.\d+)?)\s+Z=(?<z>-?\d+(?:\.\d+)?))", RegexOptions.Compiled)]
    private static partial Regex PlayerLineRegex();

    [GeneratedRegex(@"#\d+.*?Dist=(?<dist>-?\d+(?:\.\d+)?)\s+EntityId=(?<entity>\d+)\s+ServerId=(?<server>\d+)(?:\s+TargetServerId=(?<targetServer>\d+|n/a)\s+TargetingMe=(?<targetingMe>yes|no|n/a))?.*?IsMonster=(?<isMonster>yes|no|n/a).*?\sName=""(?<name>[^""]*)"".*?Pos=X=(?<x>-?\d+(?:\.\d+)?)\s+Y=(?<y>-?\d+(?:\.\d+)?)\s+Z=(?<z>-?\d+(?:\.\d+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex WorldObjectLineRegex();

    [GeneratedRegex(@"#\d+.*?Dist=(?<dist>-?\d+(?:\.\d+)?)\s+EntityId=(?<entity>\d+)\s+ServerId=(?<server>\d+).*?CEntityType=(?<entityType>\d+).*?ObjType=(?<objectType>\d+)\s+TemplateId=(?<template>\d+)\s+Level=(?<level>\d+)\s+Name=""(?<name>[^""]*)""\s+Corpse=(?<corpse>yes|no)\s+Lootable=(?<lootable>yes|no)\s+LootableRaw=0x(?<lootableRaw>[0-9A-Fa-f]+)\s+InteractionState=0x(?<interactionState>[0-9A-Fa-f]+)\s+HP=(?<hp>\d+)/(?<maxHp>\d+)\s+HpPercent=(?<hpPercent>\d+).*?Pos=X=(?<x>-?\d+(?:\.\d+)?)\s+Y=(?<y>-?\d+(?:\.\d+)?)\s+Z=(?<z>-?\d+(?:\.\d+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex LootCorpseLineRegex();
}
