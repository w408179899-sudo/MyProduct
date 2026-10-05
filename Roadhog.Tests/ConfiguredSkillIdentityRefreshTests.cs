using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using Roadhog;
using Roadhog.Application;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;

internal static class ConfiguredSkillIdentityRefreshTests
{
    private static SkillSnapshot Rank(uint id, int tier, string name = "Identity Strike") =>
        new(id, name + " " + tier, tier, 1, name, tier, false, 0, 0, XmlChainTime: "1550");
    private static string Json(object? value) => JsonSerializer.Serialize(value);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static object? Call(object? target, string name, params object?[] args) => typeof(AccountSettingsForm)
        .GetMethods(BindingFlags.NonPublic | (target is null ? BindingFlags.Static : BindingFlags.Instance))
        .Single(method => method.Name == name && method.GetParameters().Length == args.Length).Invoke(target, args);
    private static List<SkillConfigNode> Capture(TreeView tree) => (List<SkillConfigNode>)Call(null, "CaptureSkillTree", tree.Nodes)!;

    public static Task IdOnlyRootRefreshAsync() => OnSta(() =>
    {
        foreach (var legacy in new[] { false, true })
        {
            var root = new SkillConfigNode
            {
                SkillId = 101,
                Children = new()
                {
                    new() { SkillId = 102, Name = "Kept Chain II", BaseName = "Kept Chain", Type = "连续技", ChainTimeMs = 713,
                        Children = new() { new() { SkillId = 103, Name = "Kept Final I", BaseName = "Kept Final", Type = "连续技", ChainTimeMs = 1213 } } }
                }
            };
            var source = new ScriptSettings
            {
                ProfileName = "id-only-profile",
                SkillTreeReleaseMode = legacy ? SkillTreeReleaseMode.Legacy : SkillTreeReleaseMode.QuickbarAvailability,
                QuickbarSkills = new() { ExecutionTree = legacy ? new() : new() { root.Clone() } },
                Skills = new() { ExecutionTree = new() { root.Clone() } }
            };
            var sourceBefore = Json(source);
            var accounts = new InMemoryAccountConfigStore(new AccountConfig { AccountName = "id-only-account", ScriptSettings = source });
            var logger = new InMemoryRoadhogLogger();
            var runtime = DispatchProxy.Create<IRoadhogRuntime, ConfiguredSkillQuickbarRefreshTests.PreviewRuntime>();
            var preview = (ConfiguredSkillQuickbarRefreshTests.PreviewRuntime)runtime;
            preview.Inner = new RoadhogRuntime(new FakeGameApi(), logger, new AccountRuntimeManager(logger), null!);
            preview.LearnedSkills = new[] { Rank(104, 4), Rank(1024, 4, "Kept Chain"), Rank(1034, 4, "Kept Final") };
            preview.ExactSkills = new[] { Rank(101, 1) };
            preview.Bar = new(0, new[] { new QuickbarSlotSnapshot(SkillQuickbar.Main, 0, 21, 101) });
            using var form = new AccountSettingsForm("id-only-account", runtime, accounts, new InMemorySharedPathStore(),
                new InMemoryScriptProfileStore(), new RecordingFolderLauncher(), "test-paths");
            var before = (ScriptSettings)Call(form, "CaptureScriptSettings")!;
            var task = (Task<(int UpdatedCount, int DeletedCount, bool Saved, string Error)>)Call(form, "RefreshConfiguredSkillsCoreAsync")!;
            var result = task.GetAwaiter().GetResult();
            var saved = accounts.LoadAllAsync().GetAwaiter().GetResult().Value!.Single().ScriptSettings!;
            var after = saved.QuickbarSkills.ExecutionTree.Single();
            Check(result.Saved && result.UpdatedCount == 1 && result.DeletedCount == 0 &&
                after.SkillId == 101 && after.Name == "Identity Strike 1" && after.BaseName == "Identity Strike" && after.ChainTimeMs == 1550,
                "an explicit or migrated ID-only root keeps its real bar identity and gains genuine metadata: " + result.Error);
            Check(Json(after.Children) == Json(before.QuickbarSkills.ExecutionTree.Single().Children),
                "off-bar chain identities, order and windows remain unchanged instead of learned-rank replacement");
            Check(Json(saved.Skills.ExecutionTree) == Json(source.Skills.ExecutionTree) && Json(source) == sourceBefore,
                "ID-based refresh preserves the compatibility archive and caller source object");
            Call(form, "ApplyScriptSettings", saved);
            Check((bool)Call(form, "SaveCurrentSettings", new object?[] { null })!, "refreshed ID-only tree reloads and saves");
            var reloaded = accounts.LoadAllAsync().GetAwaiter().GetResult().Value!.Single().ScriptSettings!;
            Check(Json(reloaded.QuickbarSkills) == Json(saved.QuickbarSkills), "subsequent reload/save does not delete or replace the recovered identity");
        }
    });

    public static Task IdOnlyMultiRankAndMissingIdentityAsync() => OnSta(() =>
    {
        using var tree = new TreeView();
        var child = new SkillConfigNode { SkillId = 102, Name = "Off-bar Child", Type = "连续技", ChainTimeMs = 839 };
        Call(null, "PopulateSelectedSkillTreeFromConfig", tree, new List<SkillConfigNode>
        {
            new() { SkillId = 101, Children = new() { child.Clone() } },
            new() { SkillId = 204, Name = "Other Strike IV", BaseName = "Other Strike" },
            new() { SkillId = 0, Name = "Unknown identity" },
            new() { SkillId = 999, Children = new() { child.Clone() } }
        });
        var result = ((int UpdatedCount, int DeletedCount))Call(null, "RefreshConfiguredQuickbarSkillTree", tree,
            new SkillSnapshot[] { Rank(101, 1), Rank(103, 3), Rank(202, 2, "Other Strike") })!;
        var after = Capture(tree);
        Check(result.DeletedCount == 3 && after.Select(node => node.SkillId).SequenceEqual(new uint[] { 103, 202 }),
            "ID-only recovery keeps the highest actual bar rank and normal named-rank replacement while removing truly missing roots");
        Check(Json(after[0].Children.Single()) == Json(child),
            "an off-bar child retains its exact configured metadata during ID-only root recovery");
        Check(after.All(node => node.SkillId != 0 && node.SkillId != 999),
            "an absent or zero identity is never guessed from another skill on the bar");
    });

    private static Task OnSta(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception exception) { completion.SetException(new InvalidOperationException(exception.ToString(), exception)); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
