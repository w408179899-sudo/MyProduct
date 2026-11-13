using Roadhog.Core.Api;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.Input;

/// <summary>Leave the previous bag hover before approaching each inventory item.</summary>
public sealed class InventoryItemMouseMover(IKeyboardInput input, IRoadhogSnapshotReader snapshots,
    Func<int, CancellationToken, Task>? delay = null)
{
    private const int ResetTolerance = 3;
    public static GameUiPoint ResetPoint { get; } = new(680, 468);

    public async Task MoveAsync(GameUiPoint point, CancellationToken token, Func<Task>? guard = null)
    {
        var mover = new FeedbackMouseMover(input, snapshots, delay);
        // This is a hover-exit waypoint, never a click target. Keep the final
        // item movement and TradingActions click validation at one pixel.
        await mover.MoveAsync(ResetPoint, token, arrivalTolerance: ResetTolerance);
        if (guard != null) await guard();
        // The configured reset point may itself overlap this particular bag cell.
        if (Math.Abs(point.X - ResetPoint.X) <= 32 + ResetTolerance && Math.Abs(point.Y - ResetPoint.Y) <= 32 + ResetTolerance)
        {
            var cursor = (await snapshots.ReadUiCursorAsync().WaitAsync(token)).Value;
            await mover.MoveAsync(new(point.X + (point.X + 64 < cursor.Width ? 64 : -64), point.Y), token);
            if (guard != null) await guard();
        }
        await mover.MoveAsync(point, token);
    }
}
