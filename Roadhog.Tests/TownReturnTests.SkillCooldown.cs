using Roadhog.Application.BagCleanup;
using Roadhog.Application.Travel;
using Roadhog.Core.Model;

internal static partial class TownReturnTests
{
    private const uint RecallSkillId = 1801;
    private const uint PreviousRecallEnd = 3_047_846_578;
    private const uint NewRecallEnd = 3_049_633_500;

    private static SkillSnapshot RecallSkill(uint endTime) =>
        new(RecallSkillId, "回程", 1, 1, null, null, false, 1_200_000, endTime);

    private static void BindRecallSkill(Harness h)
    {
        h.Context.Config.ScriptSettings!.Paths.TownReturnKey = "OemPlus";
        h.Api.Quickbar = new(0, [new(SkillQuickbar.Main, 11, 21, RecallSkillId)]);
        h.Api.Skills = [RecallSkill(PreviousRecallEnd)];
    }

    public static async Task StartupResourceSamePositionRecallAsync()
    {
        var clock = new ReturnClock();
        using var h = new Harness(clock);
        BindRecallSkill(h);
        var departure = ResourceScene(25, 25, x: 1.677f);
        h.Api.TransitionRead = () => departure;

        h.Input.AfterPress = key =>
        {
            if (key == "OemPlus")
                Check(h.Api.LastRequestedSkillIds?.SequenceEqual(new uint[] { RecallSkillId }) == true,
                    "the worker captures an exact skill baseline before pressing return");
        };
        await CheckStartupResources(h);
        Check(h.State.StartupTownReturnPending, "the worker waits for the submitted return");
        foreach (var endTime in new uint[] { PreviousRecallEnd, PreviousRecallEnd - 100, 0 })
        {
            h.Api.Skills = [RecallSkill(endTime), RecallSkill(NewRecallEnd) with { SkillId = 8900 }];
            await CheckStartupResources(h);
            Check(h.State.ReturnTransition?.Phase == TownReturnPhase.WaitingForDeparture &&
                !h.State.StartupRecoveryActive && !h.Input.KeyDowns.Contains("W"),
                "unchanged, older or zero cooldown and another skill's advance do not confirm");
        }
        h.Api.Skills = [];
        await CheckStartupResources(h);
        Check(h.State.StartupTownReturnPending, "missing skill samples leave the transition pending");

        clock.Advance(TimeSpan.FromMinutes(1));
        h.Api.Skills = [RecallSkill(NewRecallEnd)];
        await CheckStartupResources(h);
        Check(h.Input.Keys.Count(key => key == "OemPlus") == 1, "cooldown evidence wins over a due retry");
        Check(!h.State.StartupTownReturnPending && h.State.StartupRecoveryActive && h.State.StartupRecoveryPointIndex == 0,
            "same map and unchanged position at the entrance reconnect from the first revive point");
        Check(h.Logger.Entries.Count(entry => entry.EventName == "town_return.skill_cooldown_advanced") == 1,
            "this action is diagnosed as a skill confirmation");
    }

    public static async Task RecallSkillCooldownNeedsSafeLandingAsync()
    {
        using var h = new Harness();
        BindRecallSkill(h);
        var now = DateTimeOffset.UtcNow;
        var transition = await TownReturnTransition.StartRevivePathAsync(h.Context, Scene(1), h.Path, "OemPlus", now);
        h.Api.Skills = [RecallSkill(NewRecallEnd)];
        h.Api.TransitionRead = Loading;
        Check(await transition.TickAsync(h.Context) == TownReturnPhase.Loading,
            "cooldown advance does not release a loading scene");
        h.Api.TransitionRead = () => Scene(1, map: 2);
        Check(await transition.TickAsync(h.Context) == TownReturnPhase.WaitingForDestination,
            "matching coordinates on the wrong map are held");
        h.Api.TransitionRead = () => Scene(1, z: 50);
        Check(await transition.TickAsync(h.Context) == TownReturnPhase.WaitingForDestination,
            "matching XY on the wrong floor is held");
        h.Api.TransitionRead = () => Scene(50);
        Check(await transition.TickAsync(h.Context) == TownReturnPhase.WaitingForDestination,
            "a scene away from the entry is held");
        Check(!transition.TryReserveRetry(now + TimeSpan.FromMinutes(1)), "confirmed cast cannot be replayed while awaiting its destination");

        var arrival = Scene(1);
        h.Api.TransitionRead = () => arrival with { Player = arrival.Player! with { MaxHp = 0 } };
        Check(await transition.TickAsync(h.Context) == TownReturnPhase.WaitingForDestination,
            "unknown life state cannot complete a skill-confirmed return");
        h.Api.TransitionRead = () => arrival with { Player = arrival.Player! with { CharacterName = "other" } };
        Check(await transition.TickAsync(h.Context) == TownReturnPhase.WaitingForDestination,
            "a different character cannot complete the confirmed return");

        h.Api.TransitionRead = () => Scene(1);
        Check(await transition.TickAsync(h.Context) == TownReturnPhase.Arrived, "a later ready snapshot at the route entry is accepted");

        BindRecallSkill(h);
        var dying = await TownReturnTransition.StartRevivePathAsync(h.Context, Scene(1), h.Path, "OemPlus", now);
        h.Api.Skills = [RecallSkill(NewRecallEnd)];
        h.Api.TransitionRead = () => Scene(1, hp: 0);
        Check(await dying.TickAsync(h.Context) == TownReturnPhase.Dead, "death wins over otherwise valid cooldown and position");
    }

    public static async Task RecallSkillCooldownScopeAsync()
    {
        foreach (var (slot, skill, key) in new (QuickbarSlotSnapshot, SkillSnapshot, string)[]
        {
            (new(SkillQuickbar.Main, 11, 29, RecallSkillId), RecallSkill(PreviousRecallEnd), "OemPlus"),
            (new(SkillQuickbar.Main, 11, 21, RecallSkillId), RecallSkill(PreviousRecallEnd) with { Name = "其他技能" }, "OemPlus"),
            (new(SkillQuickbar.Main, 11, 21, RecallSkillId), RecallSkill(PreviousRecallEnd) with { CooldownDuration = 0 }, "OemPlus"),
            (new(SkillQuickbar.Main, 11, 21, RecallSkillId), RecallSkill(PreviousRecallEnd), "F8")
        })
        {
            using var h = new Harness();
            h.Api.Quickbar = new(0, [slot]);
            h.Api.Skills = [skill];
            h.Api.TransitionRead = () => Scene(1);
            var operation = await TownReturnTransition.StartRevivePathAsync(h.Context, Scene(1), h.Path, key, DateTimeOffset.UtcNow);
            h.Api.Skills = [RecallSkill(NewRecallEnd)];
            Check(await operation.TickAsync(h.Context) == TownReturnPhase.WaitingForDeparture,
                "only a matching return skill with a cooldown enables cooldown confirmation");
            h.Api.TransitionRead = Loading;
            await operation.TickAsync(h.Context);
            h.Api.TransitionRead = () => Scene(1);
            Check(await operation.TickAsync(h.Context) == TownReturnPhase.Arrived,
                "scene evidence still confirms the return for macros and other bindings");
        }
    }

    public static async Task RecallSkillCooldownForLegacyRouteAsync()
    {
        using var h = new Harness();
        BindRecallSkill(h);
        var legacy = Route(map: null);
        var transition = await TownReturnTransition.StartRevivePathAsync(
            h.Context, Scene(1, z: 50), legacy, "OemPlus", DateTimeOffset.UtcNow);
        h.Api.Skills = [RecallSkill(NewRecallEnd)];
        h.Api.TransitionRead = () => Scene(1, z: 50);
        Check(await transition.TickAsync(h.Context) == TownReturnPhase.WaitingForDestination,
            "legacy paths still check the entrance height");
        h.Api.TransitionRead = () => Scene(1);
        Check(await transition.TickAsync(h.Context) == TownReturnPhase.Arrived,
            "legacy paths can use a confirmed cast and the existing coordinate check without adding map data");
    }

    public static async Task CleanupReviveReturnUsesRecallSkillAsync()
    {
        foreach (var returnToRevive in new[] { true, false })
        {
            using var h = new Harness();
            BindRecallSkill(h);
            h.Context.Config.ScriptSettings!.Paths.BagCleanupTownReturnKey = "OemPlus";
            h.Api.TransitionRead = () => Scene(1000);
            var controller = new BagCleanupController(h.Input, h.Paths, (_, _, _) => throw new Exception("unexpected movement"));
            var state = new BagCleanupState();
            state.Start(0, 0);
            state.Advance(returnToRevive ? BagCleanupStep.PressReturnToRevive : BagCleanupStep.PressTownReturn);
            await controller.TickAfterLootAsync(h.Context, state);
            Check(state.ReturnTransition is not null, "the cleanup press enters transition confirmation");
            h.Api.Skills = [RecallSkill(NewRecallEnd)];
            await controller.TickAfterLootAsync(h.Context, state);
            Check(state.ReturnTransition!.Phase == (returnToRevive ? TownReturnPhase.WaitingForDestination : TownReturnPhase.WaitingForDeparture),
                "the revive-route return recognizes its cooldown, while the merchant trip uses scene evidence");

            h.Api.TransitionRead = () => Scene(0);
            var result = await controller.TickAfterLootAsync(h.Context, state);
            Check(returnToRevive ? result.Status == BagCleanupTickStatus.Completed : state.Step == BagCleanupStep.LoadCleanupPath,
                "ready scene at the entrance completes either corresponding transition");
        }
    }
}
