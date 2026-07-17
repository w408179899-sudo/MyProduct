using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Roadhog.Core.Model;

namespace Roadhog.Application.Team;

public sealed class TeamAbnormalStatusCatalog
{
    public const string AbnormalKindNegative = "Negative";
    public const string AbnormalKindPositive = "Positive";
    public const string AbnormalKindUnknown = "Unknown";

    private readonly IReadOnlyDictionary<uint, TeamAbnormalStatusStaticInfo> _byId;

    private TeamAbnormalStatusCatalog(
        string sourcePath,
        string error,
        IReadOnlyDictionary<uint, TeamAbnormalStatusStaticInfo> byId)
    {
        SourcePath = sourcePath;
        Error = error;
        _byId = byId;
    }

    public string SourcePath { get; }

    public string Error { get; }

    public bool Loaded => string.IsNullOrWhiteSpace(Error);

    public int Count => _byId.Count;

    public static TeamAbnormalStatusCatalog Load()
    {
        var path = ResolveClientSkillsXmlPath(out var resolveError);
        if (string.IsNullOrWhiteSpace(path) || !string.IsNullOrWhiteSpace(resolveError))
        {
            return Failed(path, resolveError);
        }

        try
        {
            var entries = new Dictionary<uint, TeamAbnormalStatusStaticInfo>();
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
                    }

                    continue;
                }

                if (!reader.Read())
                {
                    break;
                }
            }

            return LoadedFrom(path, entries);
        }
        catch (Exception ex)
        {
            return Failed(path, ex.GetType().Name + ":" + ex.Message);
        }
    }

    public static TeamAbnormalStatusCatalog LoadedFrom(
        string sourcePath,
        IReadOnlyDictionary<uint, TeamAbnormalStatusStaticInfo> byId)
    {
        return new TeamAbnormalStatusCatalog(sourcePath, string.Empty, byId);
    }

    public static TeamAbnormalStatusCatalog Failed(string sourcePath, string error)
    {
        return new TeamAbnormalStatusCatalog(sourcePath, string.IsNullOrWhiteSpace(error) ? "client_skills.xml not found" : error, new Dictionary<uint, TeamAbnormalStatusStaticInfo>());
    }

    public bool TryGet(uint abnormalId, out TeamAbnormalStatusStaticInfo info)
    {
        return _byId.TryGetValue(abnormalId, out info!);
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

    private bool IsNegative(AbnormalStatusEntrySnapshot entry, out TeamAbnormalStatusStaticInfo info)
    {
        if (entry.AbnormalId == 0 || !TryGet(entry.AbnormalId, out info))
        {
            info = default!;
            return false;
        }

        return string.Equals(info.StatusKind, AbnormalKindNegative, StringComparison.Ordinal);
    }

    private static bool TryReadEntry(XElement element, out TeamAbnormalStatusStaticInfo info)
    {
        info = default!;
        var idText = GetSkillXmlValue(element, "id", "skill_id", "skillid");
        if (!TryParseSkillXmlUInt(idText, out var id) || id == 0)
        {
            return false;
        }

        var raw = new TeamAbnormalStatusStaticInfo(
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

    private static string ClassifyStaticAbnormalStatus(TeamAbnormalStatusStaticInfo info)
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

    private static bool HasAnyEffectType(TeamAbnormalStatusStaticInfo info)
    {
        return !string.IsNullOrWhiteSpace(info.Effect1Type) ||
               !string.IsNullOrWhiteSpace(info.Effect2Type) ||
               !string.IsNullOrWhiteSpace(info.Effect3Type) ||
               !string.IsNullOrWhiteSpace(info.Effect4Type);
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

public sealed record TeamAbnormalStatusStaticInfo(
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
