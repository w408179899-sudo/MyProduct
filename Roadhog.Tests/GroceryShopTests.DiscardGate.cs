using Roadhog.Application;
using Roadhog.Application.BagCleanup;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;

internal static partial class GroceryShopTests
{
    public static async Task BackpackDiscardGateAsync()
    {
        foreach (var scenario in new[] { "recover", "still_full", "empty", "late", "no_goods", "overlap", "missing_scroll", "already_recovered", "disabled" })
        {
            using var trip = new Trip { AllowPreDiscard = true };
            var settings = trip.Config.ScriptSettings!;
            settings.Maintenance.BagCleanupEnabled = scenario != "disabled";
            trip.Api.InventoryCapacity = scenario is "recover" or "late" or "no_goods" or "already_recovered" ? 4 : 3;
            if (scenario == "recover") trip.AddDiscardCandidates(2);
            if (scenario is "still_full" or "late" or "no_goods" or "missing_scroll" or "disabled") trip.AddDiscardCandidates();
            if (scenario is "empty" or "overlap") trip.Api.InventoryCapacity = 2;
            if (scenario == "no_goods") settings.Maintenance.BagCleanupStallItems.Clear();
            if (scenario == "missing_scroll") trip.Api.InventoryItems = trip.Api.InventoryItems.Skip(1).ToArray();
            if (scenario == "overlap")
            {
                settings.Maintenance.BagCleanupDiscardItemNameKeywords.Add("goods");
                settings.Maintenance.BagCleanupDiscardItemNameKeywords.Add("卷轴");
            }
            if (scenario == "late") trip.AfterPreDiscardClose = () =>
            {
                trip.AfterPreDiscardClose = null;
                trip.Api.InventoryItems = trip.Api.InventoryItems.Append(new InventoryItemSnapshot(200, 200, "junk-late", 1, 2, false)).ToArray();
            };
            var context = trip.Context;
            Check(context.CleanupRequests.Request(settings, false, groceryTrigger: GroceryShopTrigger.Backpack).Success, "queue backpack request");
            await trip.Runner().RunAsync(context, context.CleanupRequests.Current!);
            var depart = scenario is "still_full" or "empty";
            Check((trip.ScrollClicks == 1 && trip.Follows == 1 && trip.ShopStarts == 1) == depart, "only exhausted discard plus insufficient slots can depart: " + scenario);
            Check(context.CleanupRequests.Current!.RequestsRestart == depart && trip.Success.Saves == (depart ? 1 : 0), "local cleanup cannot record sale or request restart: " + scenario);
            if (scenario is "recover" or "late") Check(trip.Discards == 2, "finish original and late discard work before rechecking capacity");
            if (scenario is "no_goods" or "overlap") Check(trip.Discards == 1, "local discard takes priority even if no sale plan remains");
            if (scenario is "missing_scroll" or "already_recovered" or "disabled") Check(trip.Discards == 0 && !trip.Input.Keys.Any(), "stale request or missing scroll cannot consume items");
            if (scenario == "still_full") Check(trip.Events.IndexOf("pre_discard") < trip.Events.IndexOf("scroll"), "confirmed local discard must precede scroll cast");
            if (scenario != "missing_scroll") Check(trip.Api.InventoryItems.Any(i => i.Name.Contains("卷轴")), "selected return scroll is protected during pre-discard");
            Check(!trip.BagOpen, "backpack gate closes inventory on completion");
        }
    }

    public static async Task BackpackDiscardInterruptionAsync()
    {
        foreach (var scenario in new[] { "failure", "attack", "death", "cancel" })
        {
            using var trip = new Trip { AllowPreDiscard = true, FailDiscard = scenario == "failure" };
            trip.Config.ScriptSettings!.Maintenance.BagCleanupEnabled = true;
            trip.Api.InventoryCapacity = 3;
            trip.AddDiscardCandidates();
            var afterPress = trip.Input.AfterPress;
            trip.Input.AfterPress = key =>
            {
                afterPress!(key);
                if (key != "I" || !trip.BagOpen) return;
                if (scenario == "attack") { trip.Api.TargetEntityId = 3; trip.Api.TargetIsTargetingLocalPlayer = true; }
                if (scenario == "death") trip.Api.Player = trip.Api.Player with { CurrentHp = 0 };
                if (scenario == "cancel") trip.Stop.Cancel();
            };
            var context = trip.Context;
            context.CleanupRequests.Request(trip.Config.ScriptSettings!, false, groceryTrigger: GroceryShopTrigger.Backpack);
            try { await trip.Runner().RunAsync(context, context.CleanupRequests.Current!); throw new Exception("discard interruption ignored"); }
            catch (CleanupCombatInterruptionException) when (scenario == "attack") { }
            catch (CleanupDeathInterruptionException) when (scenario == "death") { }
            catch (OperationCanceledException) when (scenario == "cancel") { }
            catch (InvalidOperationException) when (scenario == "failure") { }
            Check(trip.ScrollClicks == 0 && trip.Follows == 0 && trip.ShopStarts == 0 && trip.Success.Saves == 0 && trip.Discards == 0,
                "failed or interrupted discard is not exhaustion and cannot authorize travel: " + scenario);
            Check(trip.Api.InventoryItems.Any(i => i.Name == "junk0") && !context.CleanupRequests.Current!.RequestsRestart, "unconfirmed discard preserves item and blocks restart");
            if (scenario == "failure")
            {
                trip.FailDiscard = false;
                await trip.Runner().RunAsync(context, context.CleanupRequests.Current!);
                Check(trip.Discards == 1 && trip.ScrollClicks == 1 && trip.Success.Saves == 1, "same request retries remaining discard before departing");
            }
        }
    }

    public static async Task BackpackDiscardWorkerRetryAsync()
    {
        using var trip = new Trip { AllowPreDiscard = true, FailDiscard = true };
        trip.AddDiscardCandidates();
        trip.Api.InventoryCapacity = 4;
        var settings = trip.Config.ScriptSettings!;
        settings.MainMode = trip.Config.MainMode = AccountMainMode.SemiAuto;
        settings.Maintenance.BagCleanupEnabled = true;
        var paths = new InMemorySharedPathStore(trip.Route);
        var semi = new SemiAutoCombatController(trip.Input);
        var loop = new DefaultAccountWorkerLoop(trip.Input, semi, new StationaryCombatController(trip.Input, semi, paths), cleanupWorkflow: trip.Runner());
        var host = new AccountWorkerHost(trip.Api, trip.Logger, new AccountRuntimeManager(trip.Logger), loop, new() { TickInterval = TimeSpan.FromMilliseconds(5) });
        async Task Until(Func<bool> ready)
        {
            while (!ready()) { trip.Stop.Token.ThrowIfCancellationRequested(); Check(host.IsRunning, "worker remains running during local retry"); await Task.Delay(10, trip.Stop.Token); }
        }
        try
        {
            Check(host.Start(trip.Config).Success, "start backpack trigger worker");
            await Until(() => trip.Logger.Entries.Any(e => e.EventName == "cleanup_workflow.preparation_pending"));
            Check(trip.ScrollClicks == 0 && !trip.Logger.Entries.Any(e => e.EventName == "grocery_shop.failed"), "discard failure stays pending and cannot pause as a failed stall");
            trip.FailDiscard = false;
            await Until(() => trip.Discards == 1 && trip.Input.Keys.Contains("F1"));
            Check(trip.ScrollClicks == 0 && trip.Success.Saves == 0 && host.StandaloneShopRestartRequestId == null, "recovered slots resume ordinary combat without shop or restart");
            Check((await host.StopAsync()).Success, "pending-discard recovery remains stoppable");
        }
        finally { await host.StopAsync(); }
    }
}
