using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;

internal static class SkillTreeMaintenanceIntegrationTests
{
    public static async Task EmptyTreeMaintenanceAsync()
    {
        const uint maintenanceSkillId = 900;
        var settings = new ScriptSettings
        {
            SkillTreeReleaseMode = SkillTreeReleaseMode.QuickbarAvailability
        };
        settings.Skills.SpiritmasterAutoSkillLogicEnabled = true;
        settings.SemiAuto.AttackKeyLoopEnabled = false;
        settings.Maintenance.SitMaintenanceEnabled = false;
        settings.Maintenance.StatusMaintenanceRules.Add(new()
        {
            SkillId = maintenanceSkillId,
            SkillName = "Status Buff",
            Key = "D3",
            RunTiming = MaintenanceRuleRunTiming.InCombat
        });
        var api = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 100, "Fake Spiritmaster", 100, 100, 100, 100, 0,
                new(0, 0, 0), DateTimeOffset.Now, CharacterClassId: AionClassId.Spiritmaster),
            PlayerAbnormalStatuses = PlayerAbnormalStatusSnapshot.Empty(1),
            TargetEntityId = 100,
            TargetServerObjectId = 1000,
            TargetCurrentHp = 1000,
            TargetMaxHp = 1000,
            Quickbar = new(0, new[] { new QuickbarSlotSnapshot(SkillQuickbar.Main, 2, 21, maintenanceSkillId) }),
            Skills = new[] { new SkillSnapshot(maintenanceSkillId, "Status Buff", 1, 1, "Status Buff", 1, false, 5000, 0) },
            SkillAvailabilityRead = () => throw new InvalidOperationException("An empty attack tree must not read attack availability.")
        };
        var keyboard = new RecordingKeyboardInput();
        keyboard.AfterPress = key =>
        {
            if (key == "D3")
                api.PlayerAbnormalStatuses = new(1, DateTimeOffset.Now, 1,
                    new[] { new AbnormalStatusEntrySnapshot(0, maintenanceSkillId, PlayerAbnormalStatusSnapshot.BuffCategory, 5000, 1, 0) });
        };
        var logger = new InMemoryRoadhogLogger();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var context = new AccountWorkerContext(new AccountConfig { AccountName = "empty-new-tree", ScriptSettings = settings },
            new RoadhogSnapshotReaderFactory(api), logger, new AccountRuntimeManager(logger), new(), stop.Token);
        var maintenancePlan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        Check(maintenancePlan.UsesSpiritmasterAutoLogic && !maintenancePlan.HasCombatActions,
            "the old spiritmaster flag skips front maintenance while both attack trees are empty");
        var state = new SemiAutoCombatState();
        var controller = new SemiAutoCombatController(keyboard);

        await controller.TickAsync(context, maintenancePlan, state).ConfigureAwait(false);

        Check(keyboard.Keys.Count == 3 && keyboard.Keys.All(key => key == "D3"),
            "empty new attack tree still executes the existing status maintenance burst and no attack keys");
        Check(logger.Entries.Any(entry => entry.EventName == "semi_auto.maintenance.status_key_pressed"),
            "status maintenance reaches the existing confirmation boundary");
        Check(api.SkillAvailabilityReadCount == 0,
            "empty new attack tree never enters the availability executor");
        Check(context.Config.ScriptSettings!.QuickbarSkills.ExecutionTree.Count == 0 &&
            context.Config.ScriptSettings.Skills.ExecutionTree.Count == 0,
            "maintenance does not add an attack skill to either configuration");

        await controller.TickAsync(context, maintenancePlan, state).ConfigureAwait(false);

        Check(keyboard.Keys.Count == 3 && api.SkillAvailabilityReadCount == 0,
            "the active confirmed status prevents repeated maintenance while the empty attack tree remains idle");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
