using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Trading;
using Roadhog.Core.Common;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;

internal static partial class ArrowRestockTests
{
    public static async Task DirectReturnWorkflowAsync()
    {
        await RunDirectReturnAsync(legacyPath: true, cancelDuringLoading: false);
        await RunDirectReturnAsync(legacyPath: false, cancelDuringLoading: false);
    }

    public static Task DirectReturnCancellationAsync() =>
        RunDirectReturnAsync(legacyPath: false, cancelDuringLoading: true);

    private static async Task RunDirectReturnAsync(bool legacyPath, bool cancelDuringLoading)
    {
        using var f = new Fixture(legacyPath ? 9992U : 49999U);
        var merchant = SetupRoute(f);
        var revive = new SharedPathDocument
        {
            Name = "grinding", MapId = legacyPath ? null : 1,
            Points = new() { new() { X = 1000 }, new() { X = 1100 } }
        };
        var settings = f.Context.Config.ScriptSettings!;
        settings.Paths.RevivePathName = revive.Name;
        settings.Paths.TownReturnKey = "OemPlus";
        settings.Paths.BagCleanupReturnByReversePath = false;
        var paths = new InMemorySharedPathStore(merchant, revive);
        var traveled = new List<string>();
        var returned = false;
        var recallPressed = false;
        var recallObservations = 0;
        var afterPress = f.Input.AfterPress;
        f.Input.AfterPress = key =>
        {
            afterPress!(key);
            if (key != "OemPlus") return;
            Check(!recallPressed && f.BuyClicks == 1 && !f.Ui.IsOpen && !f.Ui.DialogOpen,
                "recall once, only after purchase is verified and merchant UI is closed");
            Check(f.Logger.Entries.Any(e => e.EventName == "arrow_restock.purchase_verified"),
                "purchase confirmation precedes recall");
            recallPressed = true;
        };
        f.Api.TransitionRead = () =>
        {
            if (!recallPressed) return new(true, f.Api.Player, f.Api.Channel, DateTimeOffset.UtcNow);
            recallObservations++;
            Check(!returned, "combat return must wait for confirmed recall landing");
            if (!legacyPath)
            {
                if (recallObservations == 1)
                    return new(true, f.Api.Player, f.Api.Channel, DateTimeOffset.UtcNow);
                if (recallObservations == 2)
                {
                    if (cancelDuringLoading) f.Stop.Cancel();
                    return new(false, null, null, DateTimeOffset.UtcNow);
                }
                if (recallObservations == 3)
                    return new(true, f.Api.Player with { Position = new(1000, 0, 0) },
                        f.Api.Channel with { MapId = 3 }, DateTimeOffset.UtcNow);
            }
            f.Api.Player = f.Api.Player with { Position = new(1000, 0, 0) };
            f.Api.Channel = f.Api.Channel with { MapId = 1 };
            return new(true, f.Api.Player, f.Api.Channel, DateTimeOffset.UtcNow);
        };
        var runner = new CleanupWorkflowRunner(f.Input, paths, (_, name, points) =>
        {
            Check(name == merchant.Name && !recallPressed && traveled.Count == 0,
                "only the outbound merchant path runs; never reverse it after buying arrows");
            traveled.Add(name);
            f.Api.Player = f.Api.Player with { Position = points[^1] };
            return Task.FromResult(OperationResult.Ok());
        }, new UnusedJournal());
        Check(f.Context.CleanupRequests.Request(settings, false, arrowRestock: true).Success, "queue arrow trip");
        var request = f.Context.CleanupRequests.Current!;
        var semi = new SemiAutoCombatController(f.Input);
        var combat = new StationaryCombatController(f.Input, semi, paths);
        var state = new StationaryCombatState();
        try
        {
            await runner.RunAsync(f.Context, request, async context =>
            {
                Check(recallPressed && f.Api.Channel.MapId == 1 && f.Api.Player.Position == new Vector3Snapshot(1000, 0, 0) &&
                    f.Logger.Entries.Any(e => e.EventName == "bag_cleanup.return_to_revive.verify.ok"),
                    "existing combat return starts only after recall landing is verified");
                f.Api.TargetEntityId = 0;
                f.Api.TransitionRead = null;
                // The purchase fixture's mouse callbacks model shop clicks, not navigation.
                f.Input.AfterMouseDown = null;
                f.Input.AfterMouseUp = null;
                f.Input.AfterMove = null;
                f.Input.AfterKeyDown = key =>
                {
                    if (key == "W") f.Api.Player = f.Api.Player with { Position = new(1100, 0, 0) };
                };
                await combat.ReturnAfterCleanupAsync(context, SemiAutoSkillPlan.FromSettings(settings.Skills), new(), state);
                Check(f.Api.Player.Position == new Vector3Snapshot(1100, 0, 0),
                    "revive-path navigation reaches the grinding point after recall");
                returned = true;
            });
            Check(!cancelDuringLoading && returned && !state.CleanupReturnToCombatActive &&
                f.Logger.Entries.Any(e => e.EventName == "cleanup_workflow.complete"),
                "confirmed recall completes through the existing combat return controller");
        }
        catch (OperationCanceledException) when (cancelDuringLoading && f.Stop.IsCancellationRequested)
        {
            Check(recallPressed && recallObservations == 2 && !returned &&
                !f.Logger.Entries.Any(e => e.EventName == "cleanup_workflow.complete"),
                "stop during loading cannot walk a route or report successful cleanup");
        }
        Check(traveled.SequenceEqual(new[] { merchant.Name }) && f.Input.Keys.Count(k => k == "OemPlus") == 1 &&
            !f.Logger.Entries.Any(e => e.EventName == "bag_cleanup.return_path.complete"),
            "direct return never replays purchase, recall or reverse-path travel");
        Check(!settings.Paths.BagCleanupReturnByReversePath && !request.Settings.Paths.BagCleanupReturnByReversePath,
            "runtime execution preserves saved and queued return preferences");
    }
}
