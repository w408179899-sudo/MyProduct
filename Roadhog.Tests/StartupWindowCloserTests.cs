using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

internal static class StartupWindowCloserTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static async Task WindowCombinationsAsync()
    {
        foreach (var (shop, bag, selling, expected) in new[]
        {
            (false, false, false, ""), (true, false, false, "Y"),
            (false, true, false, "I"), (true, true, false, "Y,I"),
            (true, true, true, "stop,Y,I"), (true, false, true, "stop,Y")
        })
        {
            using var h = new Harness(shop, bag, selling);
            await new StartupWindowCloser(h.Input).RunAsync(h.Context);
            Check(string.Join(",", h.Actions) == expected, $"window combination expected {expected}");
            Check(!h.ShopOpen && !h.BagOpen && !h.Selling && h.Succeeded, "all windows confirmed closed");
            Check(h.Listings.Count == 1, "closing does not submit or edit existing listings");
        }
    }

    public static async Task DelayedConfirmationAsync()
    {
        using var h = new Harness(true, true, true) { DelayReads = 3 };
        await new StartupWindowCloser(h.Input).RunAsync(h.Context);
        Check(h.Actions.SequenceEqual(new[] { "stop", "Y", "I" }) && h.Succeeded,
            "delayed observations never repeat clicks or toggle before preceding closure");

        using var coupled = new Harness(true, true) { ShopAlsoClosesBag = true };
        await new StartupWindowCloser(coupled.Input).RunAsync(coupled.Context);
        Check(coupled.Actions.SequenceEqual(new[] { "Y" }) && coupled.Succeeded,
            "fresh bag observation prevents reopening a bag closed with the shop");
    }

    public static async Task FailureAndTimeoutAsync()
    {
        using var failed = new Harness(true, true);
        failed.Input.PressResult = _ => OperationResult.Fail("input unavailable");
        await new StartupWindowCloser(failed.Input).RunAsync(failed.Context);
        Check(failed.Input.Keys.SequenceEqual(new[] { "Y" }) && failed.Failed && !failed.Succeeded,
            "failed shop toggle is not retried and cannot advance to bag");

        using var stuck = new Harness(true, true) { IgnoreActions = true };
        await new StartupWindowCloser(stuck.Input, 250).RunAsync(stuck.Context);
        Check(stuck.Actions.SequenceEqual(new[] { "Y" }) && stuck.Failed && !stuck.Succeeded,
            "unconfirmed close has a bounded deadline and no repeated toggle");

        using var unreadable = new Harness(false, false);
        unreadable.Api.InventoryInteractionRead = () => throw new IOException("unavailable UI");
        await new StartupWindowCloser(unreadable.Input, 250).RunAsync(unreadable.Context);
        Check(unreadable.Input.Keys.IsEmpty && unreadable.Failed,
            "unreadable initial state times out without blind input");
        unreadable.Api.InventoryInteractionRead = unreadable.Inventory;
        await new StartupWindowCloser(unreadable.Input).RunAsync(unreadable.Context);
        Check(unreadable.Succeeded, "deadline cancellation does not leak into later snapshot reads");
    }

    public static async Task CancellationAsync()
    {
        using var h = new Harness(true, true) { IgnoreActions = true };
        h.Input.AfterPress = _ => h.Stop.Cancel();
        try
        {
            await new StartupWindowCloser(h.Input).RunAsync(h.Context);
            throw new Exception("user stop must propagate");
        }
        catch (OperationCanceledException) when (h.Stop.IsCancellationRequested) { }
        Check(h.Input.Keys.SequenceEqual(new[] { "Y" }) && !h.Failed,
            "user stop prevents later bag input and is not swallowed as a cleanup failure");
    }

    public static async Task WorkerIntegrationAsync()
    {
        foreach (var fail in new[] { false, true })
        {
            using var h = new Harness(true, true);
            if (fail) h.Input.PressResult = key => key == "Y" ? OperationResult.Fail("failed close") : OperationResult.Ok();
            var semi = new SemiAutoCombatController(h.Input);
            var worker = new DefaultAccountWorkerLoop(h.Input, semi, new StationaryCombatController(h.Input, semi));
            var run = worker.RunAsync(h.Context);
            try
            {
                while (!h.Logger.Entries.Any(e => e.EventName == "skills.bindings.loaded"))
                {
                    if (run.IsCompleted) await run;
                    await Task.Delay(10, h.Stop.Token);
                }
                var entries = h.Logger.Entries.Select(e => e.EventName).ToArray();
                var close = Array.FindIndex(entries, e => e == (fail ? "worker.startup.windows_close_failed" : "worker.startup.windows_closed"));
                Check(close > Array.IndexOf(entries, "worker.startup.key_up") && close < Array.IndexOf(entries, "skills.bindings.loaded"),
                    "window pass follows existing startup input and precedes bindings on success or failure");
                Check(h.Input.Keys.SequenceEqual(fail ? new[] { "F1", "Y" } : new[] { "F1", "Y", "I" }),
                    "real worker startup preserves F1 and closes windows in order once");
                Check(h.Input.MouseCommands.Count(c => c == "wheel:-1") == 10, "existing camera initialization is preserved");
            }
            finally
            {
                h.Stop.Cancel();
                try { await run; } catch (OperationCanceledException) { }
            }
        }
    }

    private sealed class Harness : IDisposable
    {
        public readonly FakeGameApi Api = new();
        public readonly RecordingKeyboardInput Input = new();
        public readonly InMemoryRoadhogLogger Logger = new();
        public readonly CancellationTokenSource Stop = new(TimeSpan.FromSeconds(8));
        public readonly AccountWorkerContext Context;
        public readonly List<string> Actions = new();
        public readonly List<PersonalShopListing> Listings = new() { new(100, 200, 1, 10) };
        public bool ShopOpen, BagOpen, Selling, ShopAlsoClosesBag, IgnoreActions;
        public int DelayReads;
        private Action? pending;
        private int remaining;
        public bool Succeeded => Logger.Entries.Any(e => e.EventName == "worker.startup.windows_closed");
        public bool Failed => Logger.Entries.Any(e => e.EventName == "worker.startup.windows_close_failed");

        public Harness(bool shop, bool bag, bool selling = false)
        {
            ShopOpen = shop; BagOpen = bag; Selling = selling;
            Context = new(new() { AccountName = "startup", ScriptSettings = new() { MainMode = AccountMainMode.Gather } },
                Api, Logger, new AccountRuntimeManager(Logger), new() { PollPlayerSnapshot = false }, Stop.Token);
            Api.InventoryInteractionRead = Inventory;
            Api.PersonalShopRead = () =>
            {
                Advance();
                return new(ShopOpen, Selling, BagOpen, Array.Empty<InventoryUiItem>(), 0, Listings, null, null)
                { StopButton = new(500, 300) };
            };
            Input.AfterMove = (x, y) => Api.InventoryUiCursor = new(Api.InventoryUiCursor.X + x, Api.InventoryUiCursor.Y + y);
            Input.AfterMouseDown = button =>
            {
                Check(button == RoadhogMouseButton.Left && Selling, "only click stop while selling");
                Actions.Add("stop"); Schedule(() => Selling = false);
            };
            Input.AfterPress = key =>
            {
                if (key is not ("Y" or "I")) return;
                Actions.Add(key);
                if (key == "Y")
                {
                    Check(ShopOpen && !Selling, "stop selling must be confirmed before Y");
                    Schedule(() => { ShopOpen = false; if (ShopAlsoClosesBag) BagOpen = false; });
                }
                else
                {
                    Check(BagOpen && !ShopOpen && !Selling, "shop closure must be confirmed before I");
                    Schedule(() => BagOpen = false);
                }
            };
        }

        public InventoryInteractionSnapshot Inventory()
        {
            Advance();
            return new(BagOpen, ShopOpen, Selling, Array.Empty<InventoryUiItem>(), 0, 0, null, null, false);
        }
        private void Schedule(Action action)
        {
            if (IgnoreActions) return;
            pending = action; remaining = DelayReads;
        }
        private void Advance()
        {
            if (pending == null || remaining-- > 0) return;
            pending(); pending = null;
        }
        public void Dispose() { Stop.Cancel(); Stop.Dispose(); }
    }
}
