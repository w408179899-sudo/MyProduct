using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Roadhog.Core.Model;

namespace Roadhog.Application.AbnormalStatuses;

public sealed class AbnormalStatusCatalog
{
    public const string AbnormalKindNegative = "Negative";
    public const string AbnormalKindPositive = "Positive";
    public const string AbnormalKindUnknown = "Unknown";
    public const uint DeathWeaknessAbnormalId = 8299;

    private static readonly Lazy<AbnormalStatusCatalog> DefaultCatalog = new(Load);
    private readonly IReadOnlyDictionary<uint, AbnormalStatusStaticInfo> _byId;
    private readonly IReadOnlyDictionary<uint, IReadOnlyList<uint>> _chantAuraEffectIdsBySourceSkillId;

    private AbnormalStatusCatalog(
        string sourcePath,
        string error,
        IReadOnlyDictionary<uint, AbnormalStatusStaticInfo> byId,
        IReadOnlyDictionary<uint, IReadOnlyList<uint>> chantAuraEffectIdsBySourceSkillId)
    {
        SourcePath = sourcePath;
        Error = error;
        _byId = byId;
        _chantAuraEffectIdsBySourceSkillId = chantAuraEffectIdsBySourceSkillId;
    }

    public string SourcePath { get; }

    public string Error { get; }

    public bool Loaded => string.IsNullOrWhiteSpace(Error);

    public int Count => _byId.Count;

    public static AbnormalStatusCatalog Default => DefaultCatalog.Value;

    public static AbnormalStatusCatalog Load()
    {
        var path = ResolveClientSkillsXmlPath(out var resolveError);
        if (string.IsNullOrWhiteSpace(path) || !string.IsNullOrWhiteSpace(resolveError))
        {
            return Failed(path, resolveError);
        }

        try
        {
            var entries = new Dictionary<uint, AbnormalStatusStaticInfo>();
            var chantAuraEffectNamesBySkillId = new Dictionary<uint, List<string>>();
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                IgnoreWhitespace = true,
                IgnoreComments = true
            };

            using var reader = XmlReader.Create(path, settings);
            while (!reader.EOF)
            {
                if (reader.NodeType == XmlNodeType.Element &&
                    string.Equals(reader.Name, "skill_base_client", StringComparison.OrdinalIgnoreCase))
                {
                    var element = (XElement)XNode.ReadFrom(reader);
                    if (TryReadEntry(element, out var info))
                    {
                        entries[info.Id] = info;
                        var auraEffectNames = ReadAuraEffectNames(element);
                        if (auraEffectNames.Count > 0)
                        {
                            chantAuraEffectNamesBySkillId[info.Id] = auraEffectNames;
                        }
                    }

                    continue;
                }

                if (!reader.Read())
                {
                    break;
                }
            }

            return LoadedFrom(path, entries, BuildChantAuraEffectMappings(entries, chantAuraEffectNamesBySkillId));
        }
        catch (Exception ex)
        {
            return Failed(path, ex.GetType().Name + ":" + ex.Message);
        }
    }

    public static AbnormalStatusCatalog LoadedFrom(
        string sourcePath,
        IReadOnlyDictionary<uint, AbnormalStatusStaticInfo> byId)
    {
        return LoadedFrom(sourcePath, byId, new Dictionary<uint, IReadOnlyList<uint>>());
    }

    public static AbnormalStatusCatalog LoadedFrom(
        string sourcePath,
        IReadOnlyDictionary<uint, AbnormalStatusStaticInfo> byId,
        IReadOnlyDictionary<uint, IReadOnlyList<uint>> chantAuraEffectIdsBySourceSkillId)
    {
        return new AbnormalStatusCatalog(
            sourcePath,
            string.Empty,
            byId,
            chantAuraEffectIdsBySourceSkillId);
    }

    public static AbnormalStatusCatalog Failed(string sourcePath, string error)
    {
        return new AbnormalStatusCatalog(
            sourcePath,
            string.IsNullOrWhiteSpace(error) ? "client_skills.xml not found" : error,
            new Dictionary<uint, AbnormalStatusStaticInfo>(),
            new Dictionary<uint, IReadOnlyList<uint>>());
    }

    public bool TryGet(uint abnormalId, out AbnormalStatusStaticInfo info)
    {
        return _byId.TryGetValue(abnormalId, out info!);
    }

    public static bool IsIgnoredNegativeStatus(uint abnormalId)
    {
        return abnormalId == DeathWeaknessAbnormalId;
    }

    public IReadOnlyList<uint> GetChantAuraEffectAbnormalIds(uint sourceSkillId)
    {
        if (sourceSkillId == 0 ||
            !_chantAuraEffectIdsBySourceSkillId.TryGetValue(sourceSkillId, out var abnormalIds))
        {
            return Array.Empty<uint>();
        }

        return abnormalIds;
    }

    public bool IsHarmfulForRest(AbnormalStatusEntrySnapshot entry)
    {
        if (entry.AbnormalId == 0 || IsIgnoredNegativeStatus(entry.AbnormalId))
        {
            return false;
        }

        if (TryGet(entry.AbnormalId, out var info))
        {
            return string.Equals(info.StatusKind, AbnormalKindNegative, StringComparison.Ordinal);
        }

        return entry.IsPhysicalDebuffCategory;
    }

    public bool IsMentalCleanseCandidate(AbnormalStatusEntrySnapshot entry)
    {
        if (!IsNegative(entry, out var info))
        {
            return false;
        }

        var category = NormalizeSkillXmlToken(info.DispelCategory);
        return string.Equals(category, "debuffmen", StringComparison.Ordinal) ||
               string.Equals(category, "mentaldebuff", StringComparison.Ordinal) ||
               string.Equals(category, "mental", StringComparison.Ordinal);
    }

    public bool IsPhysicalCleanseCandidate(AbnormalStatusEntrySnapshot entry)
    {
        if (!IsNegative(entry, out var info))
        {
            return false;
        }

        var category = NormalizeSkillXmlToken(info.DispelCategory);
        return string.Equals(category, "debuffphy", StringComparison.Ordinal) ||
               string.Equals(category, "physicaldebuff", StringComparison.Ordinal) ||
               string.Equals(category, "physical", StringComparison.Ordinal) ||
               string.Equals(category, "2", StringComparison.Ordinal);
    }

    private bool IsNegative(AbnormalStatusEntrySnapshot entry, out AbnormalStatusStaticInfo info)
    {
        if (entry.AbnormalId == 0 ||
            IsIgnoredNegativeStatus(entry.AbnormalId) ||
            !TryGet(entry.AbnormalId, out info))
        {
            info = default!;
            return false;
        }

        return string.Equals(info.StatusKind, AbnormalKindNegative, StringComparison.Ordinal);
    }

    private static bool TryReadEntry(XElement element, out AbnormalStatusStaticInfo info)
    {
        info = default!;
        var idText = GetSkillXmlValue(element, "id", "skill_id", "skillid");
        if (!TryParseSkillXmlUInt(idText, out var id) || id == 0)
        {
            return false;
        }

        var raw = new AbnormalStatusStaticInfo(
            id,
            GetSkillXmlValue(element, "name", "skill_name", "skillname"),
            GetSkillXmlValue(element, "target_slot", "targetslot"),
            GetSkillXmlValue(element, "target_relation_restriction", "targetrelationrestriction"),
            GetSkillXmlValue(element, "dispel_category", "dispelcategory"),
            GetSkillXmlValue(element, "effect1_type", "effect_1_type", "effect1type"),
            GetSkillXmlValue(element, "effect2_type", "effect_2_type", "effect2type"),
            GetSkillXmlValue(element, "effect3_type", "effect_3_type", "effect3type"),
            GetSkillXmlValue(element, "effect4_type", "effect_4_type", "effect4type"),
            AbnormalKindUnknown);
        info = raw with { StatusKind = ClassifyStaticAbnormalStatus(raw) };
        return true;
    }

    private static string ClassifyStaticAbnormalStatus(AbnormalStatusStaticInfo info)
    {
        var targetSlot = NormalizeSkillXmlToken(info.TargetSlot);
        if (string.Equals(targetSlot, "debuff", StringComparison.Ordinal) ||
            string.Equals(targetSlot, "1", StringComparison.Ordinal))
        {
            return AbnormalKindNegative;
        }

        if (string.Equals(targetSlot, "buff", StringComparison.Ordinal) ||
            string.Equals(targetSlot, "chant", StringComparison.Ordinal) ||
            string.Equals(targetSlot, "boost", StringComparison.Ordinal) ||
            string.Equals(targetSlot, "0", StringComparison.Ordinal) ||
            string.Equals(targetSlot, "2", StringComparison.Ordinal) ||
            string.Equals(targetSlot, "5", StringComparison.Ordinal))
        {
            return AbnormalKindPositive;
        }

        var relation = NormalizeSkillXmlToken(info.TargetRelationRestriction);
        if (string.Equals(relation, "enemy", StringComparison.Ordinal))
        {
            return AbnormalKindNegative;
        }

        if (string.Equals(relation, "friend", StringComparison.Ordinal) && HasAnyEffectType(info))
        {
            return AbnormalKindPositive;
        }

        return AbnormalKindUnknown;
    }

    private static bool HasAnyEffectType(AbnormalStatusStaticInfo info)
    {
        return !string.IsNullOrWhiteSpace(info.Effect1Type) ||
               !string.IsNullOrWhiteSpace(info.Effect2Type) ||
               !string.IsNullOrWhiteSpace(info.Effect3Type) ||
               !string.IsNullOrWhiteSpace(info.Effect4Type);
    }

    private static List<string> ReadAuraEffectNames(XElement element)
    {
        var result = new List<string>();
        for (var index = 1; index <= 4; index++)
        {
            var effectType = GetSkillXmlValue(
                element,
                "effect" + index.ToString(CultureInfo.InvariantCulture) + "_type",
                "effect_" + index.ToString(CultureInfo.InvariantCulture) + "_type",
                "effect" + index.ToString(CultureInfo.InvariantCulture) + "type");
            if (!string.Equals(NormalizeSkillXmlToken(effectType), "aura", StringComparison.Ordinal))
            {
                continue;
            }

            var effectName = GetSkillXmlValue(
                element,
                "effect" + index.ToString(CultureInfo.InvariantCulture) + "_reserved1",
                "effect_" + index.ToString(CultureInfo.InvariantCulture) + "_reserved1",
                "effect" + index.ToString(CultureInfo.InvariantCulture) + "_reserved_1",
                "effect" + index.ToString(CultureInfo.InvariantCulture) + "reserved1");
            if (HasUsefulSkillXmlValue(effectName))
            {
                result.Add(effectName.Trim());
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<uint, IReadOnlyList<uint>> BuildChantAuraEffectMappings(
        IReadOnlyDictionary<uint, AbnormalStatusStaticInfo> entries,
        IReadOnlyDictionary<uint, List<string>> auraEffectNamesBySkillId)
    {
        if (entries.Count == 0 || auraEffectNamesBySkillId.Count == 0)
        {
            return new Dictionary<uint, IReadOnlyList<uint>>();
        }

        var entriesByName = new Dictionary<string, List<AbnormalStatusStaticInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in entries.Values)
        {
            if (!HasUsefulSkillXmlValue(info.XmlName))
            {
                continue;
            }

            var name = info.XmlName.Trim();
            if (!entriesByName.TryGetValue(name, out var matchingEntries))
            {
                matchingEntries = new List<AbnormalStatusStaticInfo>();
                entriesByName[name] = matchingEntries;
            }

            matchingEntries.Add(info);
        }

        var result = new Dictionary<uint, IReadOnlyList<uint>>();
        foreach (var pair in auraEffectNamesBySkillId)
        {
            var abnormalIds = new List<uint>();
            foreach (var effectName in pair.Value)
            {
                if (!entriesByName.TryGetValue(effectName.Trim(), out var matchingEntries))
                {
                    continue;
                }

                foreach (var match in matchingEntries)
                {
                    if (!string.Equals(NormalizeSkillXmlToken(match.TargetSlot), "chant", StringComparison.Ordinal) ||
                        abnormalIds.Contains(match.Id))
                    {
                        continue;
                    }

                    abnormalIds.Add(match.Id);
                }
            }

            if (abnormalIds.Count > 0)
            {
                result[pair.Key] = abnormalIds.ToArray();
            }
        }

        return result;
    }

    private static bool HasUsefulSkillXmlValue(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               !string.Equals(value.Trim(), "0", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveClientSkillsXmlPath(out string error)
    {
        error = string.Empty;
        foreach (var variableName in new[] { "AION_CLIENT_SKILLS_XML", "ROADHOG_CLIENT_SKILLS_XML", "AION_SKILL_XML" })
        {
            var explicitPath = Environment.GetEnvironmentVariable(variableName);
            if (string.IsNullOrWhiteSpace(explicitPath))
            {
                continue;
            }

            var expanded = Environment.ExpandEnvironmentVariables(explicitPath.Trim().Trim('"'));
            try
            {
                expanded = Path.GetFullPath(expanded);
            }
            catch
            {
                // Keep the original path in the error below.
            }

            if (File.Exists(expanded))
            {
                return expanded;
            }

            error = "client_skills.xml path not found: " + expanded;
            return expanded;
        }

        var baseDirectory = AppContext.BaseDirectory;
        var desktopPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "client_skills.xml");
        var candidates = new[]
        {
            Path.Combine("Source", "client_skills.xml"),
            Path.Combine("Roadhog", "Source", "client_skills.xml"),
            Path.Combine(baseDirectory, "Source", "client_skills.xml"),
            Path.Combine(baseDirectory, "client_skills.xml"),
            Path.Combine(baseDirectory, "TXT", "client_skills.xml"),
            Path.Combine(baseDirectory, "..", "..", "..", "Source", "client_skills.xml"),
            Path.Combine("TXT", "client_skills.xml"),
            "client_skills.xml",
            desktopPath
        };

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                return Path.GetFullPath(candidate);
            }
            catch
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    private static bool TryParseSkillXmlUInt(string text, out uint value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                value = Convert.ToUInt32(text[2..], 16);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static string GetSkillXmlValue(XElement element, params string[] names)
    {
        foreach (var name in names)
        {
            foreach (var attribute in element.Attributes())
            {
                if (string.Equals(attribute.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
                {
                    return attribute.Value.Trim();
                }
            }

            foreach (var child in element.Elements())
            {
                if (string.Equals(child.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
                {
                    return child.Value.Trim();
                }
            }
        }

        return string.Empty;
    }

    private static string NormalizeSkillXmlToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim()
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
    }
}

public sealed record AbnormalStatusStaticInfo(
    uint Id,
    string XmlName,
    string TargetSlot,
    string TargetRelationRestriction,
    string DispelCategory,
    string Effect1Type,
    string Effect2Type,
    string Effect3Type,
    string Effect4Type,
    string StatusKind);
