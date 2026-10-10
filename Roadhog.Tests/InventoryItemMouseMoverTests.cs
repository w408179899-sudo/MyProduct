using Roadhog.Application.Input;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;

internal static class InventoryItemMouseMoverTests
{
    public static async Task ResetAndGuardsAsync()
    {
        foreach (var target in new[] { new GameUiPoint(100, 100), new GameUiPoint(680, 468), new GameUiPoint(681, 468) })
        {
            var api = new FakeGameApi { InventoryUiCursor = target };
            var input = new RecordingKeyboardInput();
            var visits = new List<GameUiPoint>();
            input.AfterMove = (x, y) => { api.InventoryUiCursor = new(api.InventoryUiCursor.X + x, api.InventoryUiCursor.Y + y); visits.Add(api.InventoryUiCursor); };
            var snapshots = api.Create(new AccountConfig(), new InMemoryRoadhogLogger(), CancellationToken.None);
            var guards = 0;
            GameUiPoint? firstGuardPosition = null;
            await new InventoryItemMouseMover(input, snapshots, (_, ct) => { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; })
                .MoveAsync(target, CancellationToken.None, () => { firstGuardPosition ??= api.InventoryUiCursor; guards++; return Task.CompletedTask; });
            Check(api.InventoryUiCursor == target && guards > 0, "finish at item and recheck state after reset");
            Check(Math.Abs(firstGuardPosition!.X - 680) <= 1 && Math.Abs(firstGuardPosition.Y - 468) <= 1,
                "fixed point is reached within cursor tolerance before approaching the target");
            Check(visits.Any(p => Math.Abs(p.X - target.X) > 32 || Math.Abs(p.Y - target.Y) > 32), "even an item at the reset point gets a real hover exit");
            Check(input.MouseCommands.All(c => !c.StartsWith("down:")), "pre-move never clicks the fixed point");
        }
        foreach (var stop in new[] { false, true })
        {
            using var cancelled = new CancellationTokenSource();
            var api = new FakeGameApi(); var input = new RecordingKeyboardInput();
            input.AfterMove = (x, y) => api.InventoryUiCursor = new(api.InventoryUiCursor.X + x, api.InventoryUiCursor.Y + y);
            var snapshots = api.Create(new AccountConfig(), new InMemoryRoadhogLogger(), cancelled.Token);
            try
            {
                await new InventoryItemMouseMover(input, snapshots, (_, ct) => { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; })
                    .MoveAsync(new(100, 100), cancelled.Token, () =>
                    {
                        if (stop) { cancelled.Cancel(); return Task.CompletedTask; }
                        throw new InvalidOperationException("inventory changed");
                    });
                throw new Exception("failed guard reached item");
            }
            catch (OperationCanceledException) when (stop) { }
            catch (InvalidOperationException) when (!stop) { }
            Check(api.InventoryUiCursor == InventoryItemMouseMover.ResetPoint, "stop or changed state prevents approaching item after reset");
        }
        var legacyInput = new RecordingKeyboardInput(); var position = new GameUiPoint(100, 100); var legacyVisits = new List<GameUiPoint>();
        legacyInput.AfterMove = (x, y) =>
        { position = new(Math.Max(0, position.X + x), Math.Max(0, position.Y + y)); legacyVisits.Add(position); };
        Check((await InventoryItemMouseMover.MoveScreenPointAsync(legacyInput, 140, 240, 1, TimeSpan.Zero, CancellationToken.None)).Success,
            "legacy screen-point item entry succeeds");
        Check(legacyVisits.Contains(InventoryItemMouseMover.ResetPoint) && position == new GameUiPoint(140, 240), "legacy entry also visits fixed point first");
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
