using Roadhog.Application.SemiAuto;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

internal static class SkillKeyBindingChainIdentityTests
{
    private static SkillSnapshot Skill(uint id, string name, string? prechain = null) =>
        new(id, name, 1, 1, name, 1, false, 0, 0,
            XmlChainCategory: id == 101 ? "source-chain" : null,
            XmlPrechainCategory: prechain);

    private static SemiAutoSkillNode Parent() => new(101, "Source", "Source", "", null, "D1");
    private static SkillConfigNode Node(uint id, string name) => new()
        { SkillId = id, Name = name, BaseName = name };
    private static QuickbarSkillScriptSettings Tree(uint childId, string name) => new()
    {
        ExecutionTree = new() { new() { SkillId = 101, Name = "Source", Children = new() { Node(childId, name) } } }
    };
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    public static Task AmbiguousNamesAsync()
    {
        var learned = new[]
        {
            Skill(101, "Source"), Skill(201, "Same Child", "source-chain"),
            Skill(202, "Same Child", "source-chain")
        };
        foreach (var ownSlots in new[] { false, true })
        foreach (var snapshots in new[] { learned, learned.Reverse().ToArray() })
        {
            var slots = new List<QuickbarSlotSnapshot> { new(SkillQuickbar.Main, 0, 21, 101) };
            if (ownSlots)
            {
                slots.Add(new(SkillQuickbar.Main, 1, 21, 201));
                slots.Add(new(SkillQuickbar.Alt, 2, 21, 202));
            }
            var bindings = new SkillKeyBindings(new(0, slots), snapshots);
            Check(bindings.Resolve(0, "Same Child") is null, "ambiguous direct name has no arbitrary key");
            Check(bindings.ResolveChain(0, "Same Child", Parent()) is null,
                "ambiguous child cannot bypass name protection by inheriting a parent's key");
            var missing = new List<string>();
            var plan = QuickbarSkillPlan.FromSettings(Tree(0, "Same Child"), bindings, missing.Add);
            Check(plan.Roots.Count == 1 && plan.Roots[0].Children.Count == 0 &&
                plan.SkillReadIds.SequenceEqual(new uint[] { 101 }) && missing.Count == 1,
                "plan retains the source and reports an ambiguous child without inventing an identity");
        }
        var baseNames = learned.Select(skill => skill.SkillId == 101 ? skill :
            skill with { Name = skill.SkillId == 201 ? "Child I" : "Child II", DisplayBaseName = "Child" }).ToArray();
        var baseBindings = new SkillKeyBindings(new(0, new[] { new QuickbarSlotSnapshot(SkillQuickbar.Main, 0, 21, 101) }), baseNames);
        Check(baseBindings.ResolveChain(0, "Child", Parent()) is null,
            "multiple ranks sharing a base name also require an exact configured identity");
        return Task.CompletedTask;
    }

    public static Task UniqueAndExplicitIdentitiesAsync()
    {
        var learned = new[]
        {
            Skill(101, "Source"), Skill(201, "Same Child", "source-chain"),
            Skill(202, "Same Child", "source-chain"),
            Skill(301, "Unique Child II", "other-chain, source-chain") with { DisplayBaseName = "Unique Child" }
        };
        var sourceBar = new QuickbarSnapshot(0, new[] { new QuickbarSlotSnapshot(SkillQuickbar.Main, 0, 21, 101) });
        var bindings = new SkillKeyBindings(sourceBar, learned);
        Check(bindings.ResolveChain(202, "Same Child", Parent()) is { SkillId: 202, Key: "D1" },
            "an exact child ID remains usable despite another identity sharing its name");
        Check(bindings.ResolveChain(999, "Same Child", Parent()) is null,
            "an unknown explicit ID never substitutes a same-named learned child");
        foreach (var name in new[] { "Unique Child II", "Unique Child" })
        {
            var bound = bindings.ResolveChain(0, name, Parent());
            var plan = QuickbarSkillPlan.FromSettings(Tree(0, name), bindings);
            Check(bound is { SkillId: 301, Key: "D1" } &&
                plan.Roots.Single().Children.Single() is { SkillId: 301, Key: "D1", BaseSkillId: 101 },
                "a unique exact or base name inherits only the confirmed source slot");
        }
        var repeated = new SkillKeyBindings(sourceBar with
        {
            Slots = sourceBar.Slots.Concat(new[]
            {
                new QuickbarSlotSnapshot(SkillQuickbar.Alt, 0, 21, 202),
                new QuickbarSlotSnapshot(SkillQuickbar.Main, 11, 21, 202),
                new QuickbarSlotSnapshot(SkillQuickbar.Main, 5, 21, 202)
            }).ToArray()
        }, learned.Concat(new[] { learned[2] }).ToArray());
        var ownPlan = QuickbarSkillPlan.FromSettings(Tree(202, "Same Child"), repeated);
        Check(repeated.ResolveChain(202, "Same Child", Parent()) is { SkillId: 202, Key: "D6" } &&
            ownPlan.Roots.Single().Children.Single() is { SkillId: 202, Key: "D6", BaseSkillId: 202 },
            "repeated slots and metadata retain main-bar leftmost preference and a child's own anchor");
        var duplicateMetadata = new SkillKeyBindings(sourceBar, learned.Concat(new[] { learned[3] }).ToArray());
        Check(duplicateMetadata.ResolveChain(0, "Unique Child", Parent()) is { SkillId: 301, Key: "D1" },
            "duplicate metadata for one identity does not create false name ambiguity");
        Check(repeated.SkillIds.SequenceEqual(new uint[] { 101, 202 }) &&
            ((ICollection<uint>)repeated.SkillIds).IsReadOnly,
            "actual skill IDs are distinct and exposed as an immutable snapshot");
        return Task.CompletedTask;
    }
}
