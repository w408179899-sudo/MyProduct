using Roadhog.Application.Input;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;

internal static class FeedbackMouseMoverTests
{
    public static async Task DelayedResponseAsync()
    {
        var api = new FakeGameApi { InventoryUiCursor = new(0, 0) };
        var input = new RecordingKeyboardInput();
        GameUiPoint? pending = null;
        var origin = api.InventoryUiCursor;
        var polls = 0;
        var commands = 0;
        input.AfterMove = (dx, dy) =>
        {
            Check(pending == null, "a delayed or partially applied move must not be followed by another command");
            origin = api.InventoryUiCursor;
            pending = new(origin.X + dx, origin.Y + dy);
            polls = 0;
            commands++;
        };
        Task Delay(int _, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (pending != null)
            {
                polls++;
                if (polls == 3) api.InventoryUiCursor = new((origin.X + pending.X) / 2, (origin.Y + pending.Y) / 2);
                if (polls == 6) { api.InventoryUiCursor = pending; pending = null; }
            }
            return Task.CompletedTask;
        }
        var snapshots = api.Create(new AccountConfig(), new InMemoryRoadhogLogger(), CancellationToken.None);
        await new FeedbackMouseMover(input, snapshots, Delay).MoveAsync(new(210, 140), CancellationToken.None);
        Check(commands == 3 && pending == null && api.InventoryUiCursor == new GameUiPoint(210, 140),
            "each step must be acknowledged before the next one, including the final step");
        Check(input.MouseCommands.All(c => !c.StartsWith("down:")), "movement confirmation must not click");
    }

    public static async Task NoResponseAndCancellationAsync()
    {
        foreach (var scenario in new[] { "no_response", "pending_read", "stop" })
        {
            var api = new FakeGameApi { InventoryUiCursor = new(0, 0) };
            var input = new RecordingKeyboardInput();
            var commands = 0;
            var reads = 0;
            using var stop = new CancellationTokenSource();
            input.AfterMove = (_, _) => commands++;
            api.UiCursorRead = () =>
            {
                reads++;
                if (scenario == "pending_read" && commands > 0) throw new InvalidOperationException("simulated pending capture");
                if (scenario == "stop" && commands > 0) stop.Cancel();
                return new(1024, 768, api.InventoryUiCursor);
            };
            var snapshots = api.Create(new AccountConfig(), new InMemoryRoadhogLogger(), stop.Token);
            try
            {
                await new FeedbackMouseMover(input, snapshots, confirmationTimeout: TimeSpan.FromMilliseconds(60))
                    .MoveAsync(new(200, 100), stop.Token);
                throw new Exception("unconfirmed movement succeeded: " + scenario);
            }
            catch (OperationCanceledException) when (scenario == "stop" && stop.IsCancellationRequested) { }
            catch (TimeoutException error) when (scenario != "stop")
            { Check(error.Message.Contains("响应超时"), "timeout must identify the unconfirmed action"); }
            Check(commands == 1, scenario + " must not replay the submitted movement");
            var finishedReads = reads;
            await Task.Delay(50);
            Check(reads == finishedReads, "action cancellation must retire pending provider retries");
        }
    }

    public static async Task GainAndBoundsAsync()
    {
        var api = new FakeGameApi { InventoryUiCursor = new(0, 0) };
        var input = new RecordingKeyboardInput();
        var commands = 0;
        input.AfterMove = (dx, dy) =>
        {
            commands++;
            api.InventoryUiCursor = new(api.InventoryUiCursor.X + (int)Math.Round(dx * 0.6),
                api.InventoryUiCursor.Y + (int)Math.Round(dy * 0.6));
        };
        var snapshots = api.Create(new AccountConfig(), new InMemoryRoadhogLogger(), CancellationToken.None);
        await new FeedbackMouseMover(input, snapshots).MoveAsync(new(200, 100), CancellationToken.None);
        Check(Math.Abs(api.InventoryUiCursor.X - 200) <= 1 && Math.Abs(api.InventoryUiCursor.Y - 100) <= 1,
            "pointer gain must allow correction from the observed endpoint");
        var before = commands;
        await new FeedbackMouseMover(input, snapshots).MoveAsync(new(200, 100), CancellationToken.None);
        Check(commands == before, "already reached targets must not send another move");
        try
        {
            await new FeedbackMouseMover(input, snapshots).MoveAsync(new(1024, 100), CancellationToken.None);
            throw new Exception("out-of-window target accepted");
        }
        catch (InvalidOperationException) { }
        Check(commands == before, "invalid targets must be rejected before input");
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
