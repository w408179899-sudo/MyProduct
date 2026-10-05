using System.Reflection;
using System.Windows.Forms;
using Roadhog;
using Roadhog.Application;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;

internal static class SkillCandidateTreeTests
{
    public static Task SharedChildrenAndPredecessorTokensAsync() => OnFormAsync(form =>
    {
        foreach (var separator in new[] { ",", ";", "|", " " })
        foreach (var reverse in new[] { false, true })
        {
            var skills = new[]
            {
                Skill(101, "Root A", "A"), Skill(102, "Root B", "B"),
                Skill(201, "Shared child", "C", " a" + separator + "B "),
                Skill(202, "Grandchild", null, "C"),
                Skill(203, "Orphan", null, "AB")
            };
            var category = Populate(form, reverse ? skills.Reverse().ToArray() : skills);
            var roots = category.Nodes.Cast<TreeNode>().Where(node => Id(node) is 101 or 102).ToArray();
            Check(roots.Length == 2 && roots.All(root => root.Nodes.Count == 1 && Id(root.Nodes[0]) == 201 &&
                root.Nodes[0].Nodes.Count == 1 && Id(root.Nodes[0].Nodes[0]) == 202),
                "every valid predecessor has its own complete child branch, independent of separator and input order");
            Check(!ReferenceEquals(roots[0].Nodes[0], roots[1].Nodes[0]) &&
                category.Nodes.Cast<TreeNode>().Single(node => Id(node) == 203).Nodes.Count == 0,
                "shared branches are independently editable; category tokens do not match partial names");
            var rootKeys = (HashSet<string>)typeof(AccountSettingsForm).GetMethod("GetChainRootSkillKeys",
                BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { skills })!;
            Check(rootKeys.Count == 2, "root classification uses the same token membership as the displayed tree");
        }
    });

    public static Task DuplicateMetadataAndCyclesAsync() => OnFormAsync(form =>
    {
        var first = Skill(201, "Cycle A", "A", "A|B");
        var second = Skill(202, "Cycle B", "B", "A");
        var skills = new[] { Skill(101, "Root A", "A"), Skill(102, "Root B", "B"), first, first, second };
        var category = Populate(form, skills);
        Check(category.Nodes.Count == 2, "both valid roots survive shared cyclic metadata");
        foreach (TreeNode root in category.Nodes)
        {
            Check(root.Nodes.Cast<TreeNode>().Select(Id).Distinct().Count() == root.Nodes.Count,
                "duplicate metadata cannot duplicate a sibling candidate");
            Walk(root, new HashSet<uint>());
        }
        Check(Count(category) <= 12, "cycle detection bounds the tree while preserving separate root paths");
    });

    public static Task SameNameDifferentIdentitiesCanBeAddedAsync() => OnFormAsync(form =>
    {
        var category = Populate(form, new[]
        {
            Skill(101, "Root", "A"), Skill(201, "Same child", null, "A"), Skill(202, "Same child", null, "A")
        });
        var target = new TreeNode("Configured");
        var add = typeof(AccountSettingsForm).GetMethod("AddSkillSubtreeIfMissing", BindingFlags.Static | BindingFlags.NonPublic)!;
        TreeNode Add(TreeNode source) => (TreeNode)add.Invoke(null, new object[] { target.Nodes, source })!;
        var first = category.Nodes[0].Nodes.Cast<TreeNode>().Single(node => Id(node) == 201);
        var second = category.Nodes[0].Nodes.Cast<TreeNode>().Single(node => Id(node) == 202);
        var addedFirst = Add(first);
        var addedSecond = Add(second);
        Check(target.Nodes.Count == 2 && !ReferenceEquals(addedFirst, addedSecond) &&
            target.Nodes.Cast<TreeNode>().Select(Id).Order().SequenceEqual(new uint[] { 201, 202 }),
            "same display name must not merge two explicit skill identities");
        var renamed = (TreeNode)first.Clone();
        renamed.Text = "Renamed same identity";
        Check(ReferenceEquals(Add(renamed), addedFirst) && target.Nodes.Count == 2,
            "repeated addition of an explicit ID remains idempotent even when display text differs");
        var legacy = new TreeNode("Name-only legacy");
        var addedLegacy = Add(legacy);
        Check(ReferenceEquals(Add(new TreeNode(legacy.Text)), addedLegacy) && target.Nodes.Count == 3,
            "name-only legacy candidates retain text deduplication");
    });

    private static void Walk(TreeNode node, HashSet<uint> path)
    {
        Check(path.Add(Id(node)), "a skill cannot recur on its own ancestor path");
        foreach (TreeNode child in node.Nodes) Walk(child, path);
        path.Remove(Id(node));
    }
    private static int Count(TreeNode node) => 1 + node.Nodes.Cast<TreeNode>().Sum(Count);
    private static uint Id(TreeNode node) => (uint)node.Tag!.GetType().GetProperty("SkillId")!.GetValue(node.Tag)!;
    private static SkillSnapshot Skill(uint id, string name, string? chain, string? prechain = null) =>
        new(id, name, 1, 1, name, 1, false, 0, 0, XmlActivation: "Active",
            XmlChainCategory: chain, XmlPrechainCategory: prechain);
    private static TreeNode Populate(AccountSettingsForm form, SkillSnapshot[] skills)
    {
        var category = new TreeNode("Chain");
        typeof(AccountSettingsForm).GetMethod("PopulateChainSkillTree", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, new object[] { category, skills });
        return category;
    }
    private static Task OnFormAsync(Action<AccountSettingsForm> run)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var logger = new InMemoryRoadhogLogger();
                var account = new AccountConfig { AccountName = "chain-candidates", ScriptSettings = new() };
                using var form = new AccountSettingsForm(account.AccountName,
                    new RoadhogRuntime(new FakeGameApi(), logger, new AccountRuntimeManager(logger), null!),
                    new InMemoryAccountConfigStore(account), new InMemorySharedPathStore(), new InMemoryScriptProfileStore(),
                    new RecordingFolderLauncher(), "test-paths");
                run(form);
                completion.SetResult();
            }
            catch (Exception error) { completion.SetException(error); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
