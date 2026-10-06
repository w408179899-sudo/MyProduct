using System.Xml.Linq;
using Roadhog.Application.AbnormalStatuses;
using Roadhog.Core.Model;

namespace Roadhog.Application.Food;

public enum FoodKind { Drink = 21, Food = 22 }

public sealed class FoodCatalog
{
    private readonly Dictionary<string, (uint Id, FoodKind Kind)> byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, FoodKind> byId = new();
    public static FoodCatalog Default => Shared.Value;
    private static readonly Lazy<FoodCatalog> Shared = new(() =>
        new(XDocument.Load(AbnormalStatusCatalog.Default.SourcePath)));

    public FoodCatalog(XDocument document)
    {
        foreach (var e in document.Descendants("skill_base_client"))
        {
            if (!uint.TryParse(e.Element("id")?.Value, out var id) ||
                !int.TryParse(e.Element("conflict_id")?.Value, out var group) || group is not (21 or 22)) continue;
            var name = e.Element("name")?.Value;
            if (string.IsNullOrWhiteSpace(name)) continue;
            byName[name] = (id, (FoodKind)group);
            byId[id] = (FoodKind)group;
        }
    }

    public bool HasStatus(PlayerAbnormalStatusSnapshot statuses, FoodKind kind) =>
        statuses.Entries.Any(e => byId.TryGetValue(e.AbnormalId, out var group) && group == kind);

    public InventoryItemSnapshot? Select(IEnumerable<InventoryItemSnapshot> items, FoodKind kind, int playerLevel) =>
        items.Where(i => i is { IsEquipped: false, Count: > 0, Slot: >= 0, Food: { Level: >= 3 } } &&
            i.Food.RequiredLevel <= playerLevel &&
            byName.TryGetValue(i.Food.UseSkillName, out var skill) && skill.Kind == kind)
        .OrderBy(i => i.Food!.Level).ThenBy(i => i.Food!.RequiredLevel)
        .ThenBy(i => i.TemplateId).ThenBy(i => i.Slot).ThenBy(i => i.InstanceId).FirstOrDefault();
}
