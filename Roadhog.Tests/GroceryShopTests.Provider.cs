using System.Text.Json;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;

internal static partial class GroceryShopTests
{
    public static Task ReturnUseGroupProviderAsync()
    {
        const uint template = 164000089;
        var bytes = new byte[528];
        BitConverter.GetBytes(template).CopyTo(bytes, 0);
        BitConverter.GetBytes(36u).CopyTo(bytes, 328);
        Check(AionVmmGameApi.TryDecodeItemUseGroup(template, bytes, out var group) && group == 36, "decode verified return group");
        BitConverter.GetBytes(31u).CopyTo(bytes, 328);
        Check(AionVmmGameApi.TryDecodeItemUseGroup(template, bytes, out group) && group == 31, "speed and flight scrolls have a distinct use group");
        BitConverter.GetBytes(0u).CopyTo(bytes, 328);
        Check(AionVmmGameApi.TryDecodeItemUseGroup(template, bytes, out group) && group == 0, "zero is a valid decoded group");
        Check(!AionVmmGameApi.TryDecodeItemUseGroup(template + 1, bytes, out _) &&
            !AionVmmGameApi.TryDecodeItemUseGroup(template, bytes.AsSpan(0, 331), out _), "reject wrong template identity and truncated metadata");
        var old = new InventoryItemSnapshot(template, 1, "伏魔殿返程咒语书", 38, 31, false, 18, UseGroup: 36);
        var fields = new InventoryItemFieldValidity(true, true, true, true, true, true, true, true, UseGroup: false);
        var partial = new InventoryReadResult(InventoryReadCompleteness.Partial,
            new[] { new InventoryItemObservation(old with { Count = 37, UseGroup = null }, fields) }, "metadata fault");
        var merged = AionVmmGameApi.MergeInventoryRead(partial, new[] { old });
        Check(merged.Single() is { Count: 37, UseGroup: 36 }, "valid quantity updates while failed use-group metadata holds trusted value");
        Check(AionVmmGameApi.MergeInventoryRead(partial, Array.Empty<InventoryItemSnapshot>()).Count == 0, "cold start cannot publish an item with unread use-group metadata");
        var complete = partial with
        {
            Completeness = InventoryReadCompleteness.Complete,
            Observations = new[] { new InventoryItemObservation(old with { UseGroup = 31 }, fields with { UseGroup = true }) }
        };
        Check(AionVmmGameApi.MergeInventoryRead(complete, new[] { old }).Single().UseGroup == 31, "valid refreshed group replaces previous metadata");
        complete = complete with { Observations = new[] { new InventoryItemObservation(old with { ItemType = 17, UseGroup = null }, fields with { UseGroup = true }) } };
        Check(AionVmmGameApi.MergeInventoryRead(complete, new[] { old }).Single().UseGroup == null, "valid non-scroll clears irrelevant group metadata");
        var replaced = old with { TemplateId = 164002011, Name = "[活动]高级疾走咒语书", UseGroup = null };
        partial = partial with { Observations = new[] { new InventoryItemObservation(replaced, fields) } };
        Check(AionVmmGameApi.MergeInventoryRead(partial, new[] { old }).Single() == old, "a different template cannot inherit trusted return metadata");
        Check(AionVmmGameApi.MergeInventoryRead(partial with { Completeness = InventoryReadCompleteness.Complete }, new[] { old }).Single() == old,
            "complete traversal with unread replacement metadata still holds the previous whole trusted item");
        var restored = JsonSerializer.Deserialize<InventoryItemSnapshot>(JsonSerializer.Serialize(old))!;
        Check(restored.UseGroup == 36, "use-group metadata survives worker IPC serialization");
        return Task.CompletedTask;
    }
}
