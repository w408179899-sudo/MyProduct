using Roadhog.Core.Model;

internal static partial class LootTouchTests
{
    public static async Task SessionStatisticsAsync()
    {
        using var f = new Fixture();
        f.Context.RuntimeStates.MarkStarting(f.Context.Config);
        f.Context.RuntimeStates.MarkRunning(f.Context.Config.AccountName, 1);
        int? Count() => f.Context.RuntimeStates.Snapshot().Single().LootCount;

        await f.Tick();
        Check(f.Presses == 1 && Count() == 0, "pressing a key alone never counts as loot");
        Volatile.Write(ref f.Reply, new LootInteractionSnapshot(9002, true, false));
        await f.State.LootAfterKill.Observation!.CaptureAsync();
        Check(Count() == 0, "another corpse's reply cannot count");
        Volatile.Write(ref f.Reply, LootInteractionSnapshot.Closed);
        f.Input.AfterPress = key => { if (key == "NumPadDecimal") f.Accept(window: true); };
        await Task.Delay(650);
        await f.Tick();
        await f.State.LootAfterKill.Observation!.CaptureAsync();
        await f.Tick();
        Check(f.Presses == 2 && Count() == 1, "two press attempts produce one confirmed pickup");
        await f.Tick();
        Check(Count() == 1, "repeated close-window ticks cannot double count");
        Volatile.Write(ref f.Reply, LootInteractionSnapshot.Closed);
        await f.Finish();
        f.Start(); await f.Finish();
        Check(f.Presses == 2 && Count() == 1, "revisiting a touched corpse does not count again");

        using var failed = new Fixture();
        failed.Context.RuntimeStates.MarkStarting(failed.Context.Config);
        await failed.Tick();
        failed.Api.TargetOwnServerObjectId = 9002;
        await Task.Delay(650); await failed.Finish();
        Check(failed.Context.RuntimeStates.Snapshot().Single().LootCount == 0,
            "unconfirmed attempts abandoned after target change do not count");
    }
}
