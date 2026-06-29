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
                ExtractTokenValue(line, "XmlCostDp"));

            result.Add(skill);
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

            latest = new PlayerSnapshot(
                ParseUShort(match.Groups["entity"].Value),
                ParseUShort(match.Groups["target"].Value),
                string.Empty,
                ParseUInt(match.Groups["hp"].Value),
                ParseUInt(match.Groups["maxHp"].Value),
                ParseUInt(match.Groups["mp"].Value),
                ParseUInt(match.Groups["maxMp"].Value),
                ParseUShort(match.Groups["dp"].Value),
                TryParsePosition(match),
                DateTimeOffset.Now);
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
                IsTargetingLocalPlayer: IsYes(match.Groups["targetingMe"])));
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

    [GeneratedRegex(@"#\d+\s+Id=(?<id>\d+).*?HighestLevel=(?<highest>\d+).*?ItemLevel=(?<itemlevel>\d+).*?Name=""(?<name>[^""]*)""(?: Base=""(?<base>[^""]*)"" Tier=(?<tier>\d+))?.*?Toggle=(?<toggle>\d+).*?Cooldown=(?<cooldown>\d+)/(?<cooldownEnd>\d+)", RegexOptions.Compiled)]
    private static partial Regex SkillLineRegex();

    [GeneratedRegex(@"EntityId=(?<entity>\d+)\s+TargetId=(?<target>\d+).*?HP=(?<hp>\d+)/(?<maxHp>\d+).*?MP=(?<mp>\d+)/(?<maxMp>\d+).*?DP=(?<dp>\d+).*?Pos=(?:n/a|X=(?<x>-?\d+(?:\.\d+)?)\s+Y=(?<y>-?\d+(?:\.\d+)?)\s+Z=(?<z>-?\d+(?:\.\d+)?))", RegexOptions.Compiled)]
    private static partial Regex PlayerLineRegex();

    [GeneratedRegex(@"#\d+.*?Dist=(?<dist>-?\d+(?:\.\d+)?)\s+EntityId=(?<entity>\d+)\s+ServerId=(?<server>\d+)(?:\s+TargetServerId=(?<targetServer>\d+|n/a)\s+TargetingMe=(?<targetingMe>yes|no|n/a))?.*?IsMonster=(?<isMonster>yes|no|n/a).*?\sName=""(?<name>[^""]*)"".*?Pos=X=(?<x>-?\d+(?:\.\d+)?)\s+Y=(?<y>-?\d+(?:\.\d+)?)\s+Z=(?<z>-?\d+(?:\.\d+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex WorldObjectLineRegex();

    [GeneratedRegex(@"#\d+.*?Dist=(?<dist>-?\d+(?:\.\d+)?)\s+EntityId=(?<entity>\d+)\s+ServerId=(?<server>\d+).*?CEntityType=(?<entityType>\d+).*?ObjType=(?<objectType>\d+)\s+TemplateId=(?<template>\d+)\s+Level=(?<level>\d+)\s+Name=""(?<name>[^""]*)""\s+Corpse=(?<corpse>yes|no)\s+Lootable=(?<lootable>yes|no)\s+LootableRaw=0x(?<lootableRaw>[0-9A-Fa-f]+)\s+InteractionState=0x(?<interactionState>[0-9A-Fa-f]+)\s+HP=(?<hp>\d+)/(?<maxHp>\d+)\s+HpPercent=(?<hpPercent>\d+).*?Pos=X=(?<x>-?\d+(?:\.\d+)?)\s+Y=(?<y>-?\d+(?:\.\d+)?)\s+Z=(?<z>-?\d+(?:\.\d+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex LootCorpseLineRegex();
}
