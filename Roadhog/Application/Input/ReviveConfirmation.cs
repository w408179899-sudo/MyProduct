using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.Input;

/// <summary>One UI-confirmed click. The death-recovery state machine owns retries and survival confirmation.</summary>
public sealed class ReviveConfirmation(IKeyboardInput input, IRoadhogSnapshotReader snapshots,
    Func<int, CancellationToken, Task>? delay = null)
{
    public async Task<GameUiPoint?> TryClickAsync(PlayerSnapshot expectedPlayer, int holdMs, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var dialog = (await snapshots.ReadReviveUiAsync().WaitAsync(token).ConfigureAwait(false)).Value;
        if (!dialog.IsOpen || dialog.ConfirmButton is not { } point) return null;
        await new FeedbackMouseMover(input, snapshots, delay).MoveAsync(point, token).ConfigureAwait(false);
        await Pause(200, token).ConfigureAwait(false);
        var player = (await snapshots.ReadPlayerAsync().WaitAsync(token).ConfigureAwait(false)).Value;
        if (!player.IsDead || player.EntityId != expectedPlayer.EntityId || player.CharacterName != expectedPlayer.CharacterName)
            return null;
        var actual = (await snapshots.ReadReviveUiAsync().WaitAsync(token).ConfigureAwait(false)).Value;
        if (actual != dialog) return null;
        var cursor = (await snapshots.ReadUiCursorAsync().WaitAsync(token).ConfigureAwait(false)).Value.Position;
        if (Math.Abs(cursor.X - point.X) > 1 || Math.Abs(cursor.Y - point.Y) > 1) return null;
        token.ThrowIfCancellationRequested();
        try
        {
            Check(await input.MouseDownAsync(RoadhogMouseButton.Left, token).ConfigureAwait(false));
            await Pause(holdMs, token).ConfigureAwait(false);
        }
        finally
        {
            Check(await input.MouseUpAsync(RoadhogMouseButton.Left, CancellationToken.None).ConfigureAwait(false));
        }
        return point;
    }

    private Task Pause(int ms, CancellationToken token) => delay?.Invoke(ms, token) ?? Task.Delay(ms, token);
    private static void Check(OperationResult result)
    {
        if (!result.Success) throw new InvalidOperationException(result.Error ?? "Revive mouse input failed.");
    }
}
