using Roadhog.Core.Accounts;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;

internal static class NpcSellEligibilityTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static Task DecoderAndMergeAsync()
    {
        const ulong address = 0x1000;
        const uint instance = 42, weapon = 100000152;
        var memory = new byte[1236];
        void Set(int offset, uint value) => BitConverter.GetBytes(value).CopyTo(memory, offset);
        Set(8, instance); Set(12, weapon); memory[120] = 4;
        byte[] Read(ulong at, int length) => memory.AsSpan(checked((int)(at - address)), length).ToArray();
        bool Allowed() => InventoryNpcSellEligibilityDecoder.Read(Read, address, instance, BitConverter.ToUInt32(memory, 12));
        Check(Allowed(), "ordinary sellable item");
        memory[120] = 0; Check(!Allowed(), "missing vendor sale flag disables sale"); memory[120] = 4;
        Set(56, 1); Check(!Allowed(), "equipped or active state blocks sale"); Set(56, 0);
        Set(170, weapon + 1); memory[207] = 3; Check(!Allowed(), "associated-template restriction blocks sale");
        Set(170, weapon); Check(Allowed(), "matching associated template is allowed"); Set(170, 0);
        memory[203] = 1; Set(212, 9); Check(!Allowed(), "active item restriction blocks sale"); Set(212, 0); Check(Allowed(), "valid zero clears restriction");
        Set(1232, 1); Check(!Allowed(), "weapon-specific state blocks sale");
        Set(12, 182209037); Check(Allowed(), "nonweapon ignores weapon-specific bytes; item type is not a sale veto");
        void Invalid(Func<ulong, int, byte[]> reader, string reason)
        {
            try { InventoryNpcSellEligibilityDecoder.Read(reader, address, instance, 182209037); throw new Exception(reason); }
            catch (InvalidDataException) { }
        }
        Invalid((_, _) => Array.Empty<byte>(), "short read accepted");
        Invalid((at, n) => { var result = Read(at, n); if (at == address) result[8]++; return result; }, "wrong identity accepted");
        var captures = 0;
        Invalid((at, n) => { var result = Read(at, n); if (at == address && ++captures == 2) result[120] ^= 4; return result; }, "torn predicate accepted");
        var old = new InventoryItemSnapshot(182209037, instance, "junk", 2, 1, false, 11, CanSellToNpc: false);
        var invalid = new InventoryItemFieldValidity(true, true, true, true, true, true, true, true, CanSellToNpc: false);
        var partial = new InventoryReadResult(InventoryReadCompleteness.Partial,
            new[] { new InventoryItemObservation(old with { Count = 1, CanSellToNpc = true }, invalid) }, "eligibility read failure");
        Check(AionVmmGameApi.MergeInventoryRead(partial, new[] { old }).Single() is { Count: 1, CanSellToNpc: false },
            "partial capture updates quantity while retaining official eligibility");
        Check(AionVmmGameApi.MergeInventoryRead(partial, Array.Empty<InventoryItemSnapshot>()).Count == 0,
            "cold read cannot fabricate a sellable item");
        var valid = partial with { Observations = new[] { new InventoryItemObservation(old with { CanSellToNpc = true }, invalid with { CanSellToNpc = true }) } };
        Check(AionVmmGameApi.MergeInventoryRead(valid, new[] { old }).Single().CanSellToNpc, "valid true immediately replaces false");
        valid = valid with { Observations = new[] { new InventoryItemObservation(old, invalid with { CanSellToNpc = true }) } };
        Check(!AionVmmGameApi.MergeInventoryRead(valid, new[] { old with { CanSellToNpc = true } }).Single().CanSellToNpc,
            "valid false immediately replaces true");
        partial = partial with { Observations = new[] { new InventoryItemObservation(old with { TemplateId = weapon }, invalid) } };
        Check(AionVmmGameApi.MergeInventoryRead(partial, new[] { old }).Single() == old, "unread replacement cannot inherit old eligibility");
        return Task.CompletedTask;
    }

    public static Task CandidatePolicyAsync()
    {
        var settings = new MaintenanceScriptSettings { BagCleanupDiscardItemNameKeywords = new() { "junk" }, BagCleanupExcludedItemNames = new() { "protected" } };
        var items = new[]
        {
            new InventoryItemSnapshot(1, 1, "junk quest-type but sellable", 1, 1, false, 11, CanSellToNpc: true),
            new InventoryItemSnapshot(2, 2, "junk blocked equipment", 1, 2, false, 7, CanSellToNpc: false),
            new InventoryItemSnapshot(3, 3, "junk protected", 1, 3, false, CanSellToNpc: true),
            new InventoryItemSnapshot(4, 4, "ordinary", 1, 4, false, CanSellToNpc: true)
        };
        Check(GroceryNpcSalePolicy.SelectCandidates(items, settings).Select(i => i.InstanceId).SequenceEqual(new ulong[] { 1 }),
            "only original discard candidates with official sell eligibility; no type exclusion");
        Check(BagCleanupItemMatcher.SelectDiscardItems(items, settings).Count == 2, "local discard policy remains unchanged");
        return Task.CompletedTask;
    }
}
