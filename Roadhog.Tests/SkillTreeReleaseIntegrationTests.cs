using Roadhog.Application.AbnormalStatuses;
using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;

internal static class SkillTreeReleaseIntegrationTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static SkillConfigNode Node(uint id) => new() { SkillId = id, Name = "skill" + id, Type = "主动技能" };
    private static SkillSnapshot Skill(uint id) => new(id, "skill" + id, 1, 1, "skill" + id, 1, false, 10000, 0);

    private static (FakeGameApi Api, ScriptSettings Settings) Fixture()
    {
        var api = new FakeGameApi
        {
            Quickbar = new(0, new QuickbarSlotSnapshot[]
            {
                new(SkillQuickbar.Main, 0, 21, 101), new(SkillQuickbar.Main, 1, 21, 201),
                new(SkillQuickbar.Alt, 0, 21, 8200)
            }),
            Skills = new[] { Skill(101), Skill(201), Skill(8200) with { Name = "疾风真言", XmlSkillCategory = "Chant" } },
            SkillAvailability = new(0, new SkillAvailabilitySlotSnapshot[]
            {
                new(SkillQuickbar.Main, 0, 21, 101, 101, false),
                new(SkillQuickbar.Main, 1, 21, 201, 201, true),
                new(SkillQuickbar.Alt, 0, 21, 8200, 8200, true)
            }, BindingSignature: "stable-test-layout")
        };
        var settings = new ScriptSettings();
        settings.Maintenance.SitMaintenanceEnabled = false;
        settings.SemiAuto.AttackKeyLoopEnabled = false;
        settings.SemiAuto.AttackWeaveEnabled = true; // The new mode must not enter legacy weave.
        settings.Skills.ExecutionTree.Add(Node(101));
        settings.QuickbarSkills.ExecutionTree.Add(Node(201));
        return (api, settings);
    }

    private static async Task<AccountWorkerContext> ContextAsync(FakeGameApi api, ScriptSettings settings,
        InMemoryRoadhogLogger logger, CancellationToken stop)
    {
        var context = new AccountWorkerContext(new AccountConfig { ScriptSettings = settings }, api, logger,
            new AccountRuntimeManager(logger), new(), stop);
        await context.PrepareSkillBindingsAsync();
        return context;
    }

    public static async Task LegacySettingUsesQuickbarAsync()
    {
        var (api, settings) = Fixture();
        settings.SemiAuto.AttackWeaveEnabled = false;
        var logger = new InMemoryRoadhogLogger();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var context = await ContextAsync(api, settings, logger, stop.Token);
        var keyboard = new RecordingKeyboardInput();
        var controller = new SemiAutoCombatController(keyboard);
        var plan = SemiAutoSkillPlan.FromSettings(context.Config.ScriptSettings!.Skills, context.SkillBindings);
        var state = new SemiAutoCombatState();
        await controller.TickAsync(context, plan, state);
        Check(keyboard.Keys.SequenceEqual(new[] { "D2" }), "an old mode marker uses the existing authoritative quickbar tree");
        Check(api.SkillAvailabilityReadCount > 0, "all main releases use the official availability provider");
        Check(state.QuickbarPlan is not null, "an old mode marker cannot enter the retired dispatcher");
    }

    public static async Task RoutingAndMaintenanceAsync()
    {
        var (api, settings) = Fixture();
        settings.SkillTreeReleaseMode = SkillTreeReleaseMode.QuickbarAvailability;
        settings.Maintenance.StatusMaintenanceRules.Add(new() { SkillId = 8200, AbnormalStatusId = 8232, SkillName = "疾风真言", Key = "NumPad1" });
        var logger = new InMemoryRoadhogLogger();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var context = await ContextAsync(api, settings, logger, stop.Token);
        var keyboard = new RecordingKeyboardInput();
        keyboard.AfterPress = key =>
        {
            if (key == "NumPad1")
                api.PlayerAbnormalStatuses = new(api.Player.EntityId, DateTimeOffset.Now, 0,
                    new[] { new AbnormalStatusEntrySnapshot(0, 8232, PlayerAbnormalStatusSnapshot.BuffCategory, 0, 1, 0) });
            if (key == "D2")
            {
                api.SkillAvailability = api.SkillAvailability with { LastReleasedSkillId = 201, LastReleasedSkillTime = 10 };
                api.Skills = api.Skills.Select(skill => skill.SkillId == 201
                    ? skill with { CooldownEndTime = unchecked((uint)Environment.TickCount64 + 10000) }
                    : skill).ToArray();
            }
        };
        var controller = new SemiAutoCombatController(keyboard);
        var plan = SemiAutoSkillPlan.FromSettings(context.Config.ScriptSettings!.Skills, context.SkillBindings);
        var state = new SemiAutoCombatState();
        await controller.TickAsync(context, plan, state);
        Check(keyboard.Keys.Count > 0 && keyboard.Keys.All(key => key == "NumPad1"), "missing chant runs existing maintenance before the new tree");
        Check(api.SkillAvailabilityReadCount == 0, "maintenance tick does not concurrently run the new selector");
        Check(logger.Entries.Any(entry => entry.EventName == "semi_auto.maintenance.status_key_pressed"), "existing chant effect confirmation preserved");
        keyboard.Keys.Clear();
        await controller.TickAsync(context, plan, state);
        Check(keyboard.Keys.SequenceEqual(new[] { "D2" }), "active chant is retained and only the independent new tree attacks");
        Check(api.SkillAvailabilityReadCount > 0, "new tree obtains its official availability channel");
        Check(!state.HasChainWork && !state.AttackWeave.HasAttempts, "legacy pending chain and weave remain unused");
        await controller.TickAsync(context, plan, state);
        Check(keyboard.Keys.Count == 1 && state.QuickbarSkills.PendingAction is null, "actual release confirmation and cooldown prevent another press");

        // Old serialized mode markers remain readable, but cannot revive the retired dispatcher.
        settings = context.Config.ScriptSettings.Clone();
        settings.SkillTreeReleaseMode = SkillTreeReleaseMode.Legacy;
        settings.SemiAuto.AttackWeaveEnabled = false;
        var readsBeforeLegacy = api.SkillAvailabilityReadCount;
        api.Skills = api.Skills.Select(skill => skill with { CooldownEndTime = 0 }).ToArray();
        api.SkillAvailability = api.SkillAvailability with { LastReleasedSkillId = 0, LastReleasedSkillTime = 0 };
        var legacyContext = await ContextAsync(api, settings, logger, stop.Token);
        keyboard.Keys.Clear();
        await controller.TickAsync(legacyContext, SemiAutoSkillPlan.FromSettings(legacyContext.Config.ScriptSettings!.Skills,
            legacyContext.SkillBindings), new());
        Check(keyboard.Keys.SequenceEqual(new[] { "D2" }), "an old marker preserves the new tree and active shared chant");
        Check(api.SkillAvailabilityReadCount > readsBeforeLegacy, "the only dispatcher continues reading official availability");
    }
}
