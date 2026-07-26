using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Gathering;

public sealed class GatherSourceCatalog
{
    private static readonly Lazy<GatherSourceCatalog> DefaultCatalog = new(Load);
    private readonly IReadOnlyDictionary<uint, GatherSourceDefinition> _byId;

    private GatherSourceCatalog(
        string sourcePath,
        string error,
        IReadOnlyDictionary<uint, GatherSourceDefinition> byId)
    {
        SourcePath = sourcePath;
        Error = error;
        _byId = byId;
    }

    public string SourcePath { get; }

    public string Error { get; }

    public bool Loaded => string.IsNullOrWhiteSpace(Error);

    public int Count => _byId.Count;

    public IReadOnlyCollection<GatherSourceDefinition> Sources => _byId.Values.ToArray();

    public static GatherSourceCatalog Default => DefaultCatalog.Value;

    public static GatherSourceCatalog Load()
    {
        var path = ResolvePath(out var error);
        return string.IsNullOrWhiteSpace(path) || !string.IsNullOrWhiteSpace(error)
            ? Failed(path, error)
            : Load(path);
    }

    public static GatherSourceCatalog Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Failed(string.Empty, "gather_src.xml path is empty");
        }

        try
        {
            path = Path.GetFullPath(path);
            if (!File.Exists(path))
            {
                return Failed(path, "gather_src.xml not found: " + path);
            }

            var entries = new Dictionary<uint, GatherSourceDefinition>();
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                IgnoreComments = true,
                IgnoreWhitespace = true
            };

            using var reader = XmlReader.Create(path, settings);
            while (!reader.EOF)
            {
                if (reader.NodeType == XmlNodeType.Element &&
                    string.Equals(reader.Name, "gather_src", StringComparison.OrdinalIgnoreCase))
                {
                    var element = (XElement)XNode.ReadFrom(reader);
                    if (TryReadDefinition(element, out var definition))
                    {
                        entries[definition.GatherSourceId] = definition;
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
            return Failed(path, ex.GetType().Name + ": " + ex.Message);
        }
    }

    public static GatherSourceCatalog LoadedFrom(
        string sourcePath,
        IReadOnlyDictionary<uint, GatherSourceDefinition> byId)
    {
        return new GatherSourceCatalog(sourcePath, string.Empty, byId);
    }

    public static GatherSourceCatalog Failed(string sourcePath, string error)
    {
        return new GatherSourceCatalog(
            sourcePath,
            string.IsNullOrWhiteSpace(error) ? "gather_src.xml not found" : error,
            new Dictionary<uint, GatherSourceDefinition>());
    }

    public bool TryGet(uint gatherSourceId, out GatherSourceDefinition definition)
    {
        return _byId.TryGetValue(gatherSourceId, out definition!);
    }

    private static bool TryReadDefinition(XElement element, out GatherSourceDefinition definition)
    {
        definition = default!;
        if (!TryReadUInt(element, "id", out var gatherSourceId) || gatherSourceId == 0)
        {
            return false;
        }

        var materials = new List<GatherMaterialDefinition>();
        AddMaterials(element, materials, "material", "normal_rate", isExtra: false);
        AddMaterials(element, materials, "extra_material", "extra_normal_rate", isExtra: true);

        definition = new GatherSourceDefinition(
            gatherSourceId,
            GetValue(element, "name"),
            GetValue(element, "desc"),
            GetValue(element, "category"),
            GetValue(element, "source_type"),
            GetValue(element, "mesh"),
            GetValue(element, "source_color"),
            ReadDouble(element, "source_upper"),
            GetValue(element, "source_fx"),
            GetValue(element, "motion_name"),
            GetValue(element, "harvestskill"),
            ReadInt(element, "skill_level"),
            ReadInt(element, "char_level_limit"),
            ReadUInt(element, "gather_delay_id"),
            ReadInt(element, "gather_delay"),
            GetValue(element, "required_item"),
            ReadInt(element, "check_type"),
            ReadInt(element, "erase_value"),
            ReadInt(element, "harvest_count"),
            ReadInt(element, "success_adj"),
            ReadInt(element, "failure_adj"),
            ReadInt(element, "aerial_adj"),
            ReadInt(element, "captcha_rate"),
            materials);
        return true;
    }

    private static void AddMaterials(
        XElement element,
        ICollection<GatherMaterialDefinition> materials,
        string materialPrefix,
        string ratePrefix,
        bool isExtra)
    {
        for (var index = 1; index <= 8; index++)
        {
            var name = GetValue(element, materialPrefix + index.ToString(CultureInfo.InvariantCulture));
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            materials.Add(
                new GatherMaterialDefinition(
                    name,
                    ReadInt(element, ratePrefix + index.ToString(CultureInfo.InvariantCulture)),
                    isExtra));
        }
    }

    private static string ResolvePath(out string error)
    {
        error = string.Empty;
        foreach (var variableName in new[] { "AION_GATHER_SRC_XML", "ROADHOG_GATHER_SRC_XML" })
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
            }

            if (File.Exists(expanded))
            {
                return expanded;
            }

            error = "gather_src.xml path not found: " + expanded;
            return expanded;
        }

        var baseDirectory = AppContext.BaseDirectory;
        var currentDirectory = Environment.CurrentDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "Source", "gather_src.xml"),
            Path.Combine(baseDirectory, "gather_src.xml"),
            Path.Combine(currentDirectory, "Roadhog", "Source", "gather_src.xml"),
            Path.Combine(currentDirectory, "Source", "gather_src.xml"),
            Path.Combine("Roadhog", "Source", "gather_src.xml"),
            Path.Combine("Source", "gather_src.xml"),
            "gather_src.xml"
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

    private static string GetValue(XElement element, string name)
    {
        return element.Elements()
            .FirstOrDefault(child => string.Equals(child.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
            ?.Value
            .Trim() ?? string.Empty;
    }

    private static bool TryReadUInt(XElement element, string name, out uint value)
    {
        return uint.TryParse(
            GetValue(element, name),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static uint ReadUInt(XElement element, string name)
    {
        return TryReadUInt(element, name, out var value) ? value : 0;
    }

    private static int ReadInt(XElement element, string name)
    {
        return int.TryParse(
            GetValue(element, name),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0;
    }

    private static double ReadDouble(XElement element, string name)
    {
        return double.TryParse(
            GetValue(element, name),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0;
    }
}
