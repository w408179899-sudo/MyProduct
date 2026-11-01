using Roadhog.Application.Workers;
using Roadhog.Core.Common;
using Roadhog.Core.Model;

internal static partial class TownReturnTests
{
    public static async Task SpiritmasterSummonsAfterLandingAsync()
    {
        foreach (var showLoading in new[] { false, true })
        {
            using var h = new Harness();
            EnableMissingSpiritmasterPet(h);
            var recalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var summoned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowLanding = false;
            var pendingReads = 0;
            h.Input.AfterPress = key =>
            {
                if (key == "F8") recalled.TrySetResult();
                if (key == "F7") summoned.TrySetResult();
            };
            h.Api.TransitionRead = () =>
            {
                if (!recalled.Task.IsCompleted) return Scene(1000);
                if (Volatile.Read(ref allowLanding)) return Scene(0);
                if (++pendingReads >= 3) held.TrySetResult();
                return showLoading ? Loading() : Scene(0, map: 9);
            };
            var work = new DefaultAccountWorkerLoop(h.Input, h.SemiAuto, h.Controller).RunAsync(h.Context);
            try
            {
                await held.Task.WaitAsync(TimeSpan.FromSeconds(8));
                Check(!summoned.Task.IsCompleted, "missing pet cannot summon during loading or at an unconfirmed destination");
                Check(!h.Input.KeyDowns.Contains("W"), "return owns movement until landing");
                h.Api.Player = Scene(0).Player! with { CharacterClassId = AionClassId.Spiritmaster };
                Volatile.Write(ref allowLanding, true);
                await summoned.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Check(h.Logger.Entries.Any(e => e.EventName == "stationary_combat.startup_recovery.return.verify.ok"),
                    "arrival must be confirmed before the summon key");
                Check(h.Input.Keys.SequenceEqual(new[] { "F1", "F8", "F7" }),
                    "startup recall must precede summon exactly once: " + string.Join(",", h.Input.Keys));
                Check(!h.Input.KeyDowns.Contains("W"), "summon at landing before walking the return route");
            }
            finally
            {
                h.Stop.Cancel();
                try { await work.WaitAsync(TimeSpan.FromSeconds(3)); } catch (OperationCanceledException) { }
            }
        }
    }

    public static async Task SpiritmasterWithoutRecallSummonsImmediatelyAsync()
    {
        using var h = new Harness();
        EnableMissingSpiritmasterPet(h);
        h.Api.Player = Scene(10).Player! with { CharacterClassId = AionClassId.Spiritmaster };
        h.Api.TransitionRead = () => Scene(10);
        await WaitForWorkerSummonAsync(h);
        Check(h.Input.Keys.SequenceEqual(new[] { "F1", "F7" }), "nearby route needs no recall before summon");
        Check(!h.Input.KeyDowns.Contains("W"), "nearby route must not delay summon until the combat point");
    }

    public static async Task SpiritmasterRecallFailureAllowsSummonAsync()
    {
        foreach (var missingKey in new[] { false, true })
        {
            using var h = new Harness();
            EnableMissingSpiritmasterPet(h);
            if (missingKey) h.Context.Config.ScriptSettings!.Paths.TownReturnKey = string.Empty;
            else h.Input.PressResult = key => key == "F8"
                ? OperationResult.Fail("test recall failure") : OperationResult.Ok();
            await WaitForWorkerSummonAsync(h);
            Check(h.Logger.Entries.Any(e => e.EventName == "town_return.branch_abandoned"),
                "failed or unconfigured recall must be handled before summon");
            Check(h.Input.Keys.Count(k => k == "F8") == (missingKey ? 0 : 1), "failed recall is not replayed");
        }
    }

    private static void EnableMissingSpiritmasterPet(Harness h)
    {
        var skills = h.Context.Config.ScriptSettings!.Skills;
        skills.SpiritmasterAutoSkillLogicEnabled = true;
        skills.Spiritmaster.SummonSkills.Add(new() { Key = "F7" });
        h.Api.Player = h.Api.Player with { CharacterClassId = AionClassId.Spiritmaster };
        h.Api.SummonedPetRoster = SummonedPetRosterSnapshot.Empty(0, DateTimeOffset.UtcNow);
    }

    private static async Task WaitForWorkerSummonAsync(Harness h)
    {
        var summoned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Input.AfterPress = key => { if (key == "F7") summoned.TrySetResult(); };
        var work = new DefaultAccountWorkerLoop(h.Input, h.SemiAuto, h.Controller).RunAsync(h.Context);
        try { await summoned.Task.WaitAsync(TimeSpan.FromSeconds(8)); }
        finally
        {
            h.Stop.Cancel();
            try { await work.WaitAsync(TimeSpan.FromSeconds(3)); } catch (OperationCanceledException) { }
        }
    }
}
