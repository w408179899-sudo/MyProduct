using System.Text.Json;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Infrastructure.Config;

internal static partial class GroceryShopTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static DateTimeOffset At(int hour, int minute = 0) => new(2026, 10, 9, hour, minute, 0, TimeSpan.FromHours(8));

    public static Task ConfigurationAndScheduleAsync()
    {
        var settings = new ScriptSettings();
        Check(settings.Maintenance.CleanupWorkflow.Mode == CleanupMode.Normal, "old configurations retain normal cleanup");
        settings.Maintenance.CleanupWorkflow.Mode = CleanupMode.GroceryShop;
        settings.Maintenance.CleanupWorkflow.GroceryScheduleEnabled = true;
        settings.Maintenance.CleanupWorkflow.GroceryScheduleTimes = new() { "19:00", "12:00", "12:00" };
        settings.Paths.GroceryStallPathName = "grocery route";
        settings.Paths.GroceryReturnItemName = "伏魔殿回程卷轴";
        var copy = settings.Clone();
        copy.Maintenance.CleanupWorkflow.GroceryScheduleTimes.Clear();
        Check(settings.Maintenance.CleanupWorkflow.GroceryScheduleTimes.Count == 3, "schedule lists are independently cloned");
        var restored = JsonSerializer.Deserialize<ScriptSettings>(JsonSerializer.Serialize(settings))!;
        Check(restored.Paths.GroceryStallPathName == "grocery route" && restored.Paths.GroceryReturnItemName == "伏魔殿回程卷轴", "path and return item persist");
        var flow = settings.Maintenance.CleanupWorkflow;
        Check(GroceryShopSchedule.LatestDue(flow, At(12), At(8)) == At(12), "due at exact minute");
        Check(GroceryShopSchedule.LatestDue(flow, At(12, 10), At(8)) == At(12), "interrupted departure remains due");
        Check(GroceryShopSchedule.LatestDue(flow, At(19, 30), At(8)) == At(19), "later due time coalesces without a queue");
        Check(GroceryShopSchedule.LatestDue(flow, At(20), At(20)) == null, "20h actual sellout covers 12h and 19h slots");
        Check(GroceryShopSchedule.LatestDue(flow, At(12).AddDays(1), At(20)) == At(12).AddDays(1), "schedule repeats the next day");
        Check(GroceryShopSchedule.LatestDue(flow, At(0).AddDays(1), At(12), At(19)) == At(19), "observed unfinished time remains due across midnight");
        flow.GroceryScheduleEnabled = false;
        Check(GroceryShopSchedule.LatestDue(flow, At(19), null) == null, "disabled scheduling is inactive");
        flow.GroceryScheduleEnabled = true; flow.Mode = CleanupMode.Normal;
        Check(GroceryShopSchedule.LatestDue(flow, At(19), null) == null, "normal cleanup does not schedule grocery trips");
        flow.Mode = CleanupMode.GroceryShop;
        var mailbox = new CleanupRequestMailbox();
        Check(mailbox.Request(settings, false, groceryTrigger: GroceryShopTrigger.Scheduled).Success && mailbox.Current!.GroceryTrigger == GroceryShopTrigger.Scheduled, "scheduled request keeps its trigger");
        Check(!mailbox.Request(settings, true).Success, "busy account cannot enqueue a duplicate task");
        mailbox.Complete();
        Check(mailbox.Request(settings, true).Success && mailbox.Current!.GroceryTrigger == GroceryShopTrigger.Manual && !mailbox.Current.RequestsRestart, "manual request forces route but cannot restart before sellout");
        mailbox.Current!.SoldOutConfirmed = true;
        mailbox.CompleteStandaloneShopForRestart(mailbox.Current);
        Check(mailbox.StandaloneShopRestartRequestId != null, "confirmed grocery sale uses existing stop/start handoff");
        Check(!mailbox.Request(settings, true).Success, "restart handoff blocks new tasks");
        var standalone = new CleanupRequestMailbox();
        Check(standalone.Request(settings, true, standaloneShop: true).Success && standalone.Current!.StandaloneShop && !standalone.Current.GroceryShop,
            "original standalone button retains behavior even with grocery mode saved");
        standalone.Complete();
        Check(!standalone.Request(settings, true, standaloneShop: true, groceryTrigger: GroceryShopTrigger.Manual).Success && standalone.Current == null,
            "conflicting request types cannot falsely complete an unsold grocery trip");
        try { GroceryShopSchedule.Normalize(new[] { "25:00" }); throw new Exception("invalid time accepted"); }
        catch (FormatException) { }
        return Task.CompletedTask;
    }

    public static Task ScheduleNeverEarlyAsync()
    {
        var flow = new CleanupWorkflowSettings { Mode = CleanupMode.GroceryShop, GroceryScheduleEnabled = true,
            GroceryScheduleTimes = new() { "10:10" } };
        foreach (var success in new DateTimeOffset?[] { null, At(8).AddDays(-1), At(20).AddDays(-1), At(9) })
        {
            Check(GroceryShopSchedule.LatestDue(flow, At(10, 1), success) == null, "10:01 must not invent yesterday's 10:10, with or without a success record");
            Check(GroceryShopSchedule.LatestDue(flow, At(10, 10).AddTicks(-1), success) == null, "do not trigger one tick before configured time");
            Check(GroceryShopSchedule.LatestDue(flow, At(10, 10), success) == At(10, 10), "trigger at configured time");
            Check(GroceryShopSchedule.LatestDue(flow, At(10, 11), success) == At(10, 10), "today's due task remains eligible after failure or a late start");
        }
        Check(GroceryShopSchedule.LatestDue(flow, At(10, 10).ToOffset(TimeSpan.Zero), null) == At(10, 10), "due time uses China time even with UTC input");
        Check(GroceryShopSchedule.LatestDue(flow, At(10, 11), At(10, 10)) == null, "exact sellout covers the slot");
        Check(GroceryShopSchedule.LatestDue(flow, At(0), null) == null, "first startup after midnight cannot generate yesterday's trip");
        Check(GroceryShopSchedule.LatestDue(flow, At(10, 1), null, At(10, 10)) == null, "future pending time cannot authorize departure");
        Check(GroceryShopSchedule.LatestDue(flow, At(0).AddDays(1), null, At(10, 10)) == At(10, 10), "only an actually observed pending slot carries across midnight");
        Check(GroceryShopSchedule.LatestDue(flow, At(0).AddDays(1), At(20), At(10, 10)) == null, "a later actual sellout clears pending slots across midnight");
        flow.GroceryScheduleTimes = new() { "12:00", "19:00" };
        Check(GroceryShopSchedule.LatestDue(flow, At(8), null, At(10, 10).AddDays(-1)) == null, "removed pending time cannot trigger a newly configured future time");
        Check(GroceryShopSchedule.LatestDue(flow, At(18), null) == At(12), "an earlier due slot does not make a later future slot due");
        Check(GroceryShopSchedule.LatestDue(flow, At(20), At(20), At(12)) == null, "20h sellout covers both 12h and 19h");
        flow.GroceryScheduleTimes.Clear();
        Check(GroceryShopSchedule.LatestDue(flow, At(20), null, At(12)) == null, "empty schedule clears pending eligibility");
        return Task.CompletedTask;
    }

    public static async Task SuccessPersistenceAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "roadhog-grocery-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonGroceryShopSuccessStore(directory);
            Check(await store.LoadAsync("one") == null, "no success before first sellout");
            await store.RecordAsync("one", At(20));
            Check(await new JsonGroceryShopSuccessStore(directory).LoadAsync("one") == At(20), "restart reloads durable actual success time");
            Check(await store.LoadAsync("two") == null, "accounts are isolated");
            await store.RecordAsync("one", At(12));
            Check(await store.LoadAsync("one") == At(20), "older completion cannot regress timestamp");
            using var stop = new CancellationTokenSource(); stop.Cancel();
            try { await store.RecordAsync("one", At(21), stop.Token); throw new Exception("cancelled save accepted"); }
            catch (OperationCanceledException) { }
            Check(await store.LoadAsync("one") == At(20), "cancel does not overwrite successful record");
            await File.WriteAllTextAsync(Directory.GetFiles(directory, "*.json").Single(), "invalid json");
            try { await store.LoadAsync("one"); throw new Exception("corrupt success record accepted"); }
            catch (JsonException) { }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
