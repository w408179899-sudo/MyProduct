using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.Input;

/// <summary>Leave the previous bag hover before approaching each inventory item.</summary>
public sealed class InventoryItemMouseMover(IKeyboardInput input, IRoadhogSnapshotReader snapshots,
    Func<int, CancellationToken, Task>? delay = null)
{
    public static GameUiPoint ResetPoint { get; } = new(680, 468);

    public async Task MoveAsync(GameUiPoint point, CancellationToken token, Func<Task>? guard = null)
    {
        var mover = new FeedbackMouseMover(input, snapshots, delay);
        await mover.MoveAsync(ResetPoint, token);
        if (guard != null) await guard();
        // The configured reset point may itself overlap this particular bag cell.
        if (Math.Abs(point.X - ResetPoint.X) <= 32 && Math.Abs(point.Y - ResetPoint.Y) <= 32)
        {
            var cursor = (await snapshots.ReadUiCursorAsync().WaitAsync(token)).Value;
            await mover.MoveAsync(new(point.X + (point.X + 64 < cursor.Width ? 64 : -64), point.Y), token);
            if (guard != null) await guard();
        }
        await mover.MoveAsync(point, token);
    }

    public static async Task<OperationResult> MoveScreenPointAsync(IKeyboardInput input, int x, int y,
        int resetCount, TimeSpan stepDelay, CancellationToken token)
    {
        if (x < 0 || y < 0 || x > short.MaxValue || y > short.MaxValue)
            return OperationResult.Fail("Absolute mouse target must be between 0 and 32767.");
        var reset = await ScreenPointMouseMover.MoveToAsync(input, ResetPoint.X, ResetPoint.Y,
            resetCount, stepDelay, token);
        if (!reset.Success) return reset;
        token.ThrowIfCancellationRequested();
        var result = await input.MoveMouseRelativeAsync(x - ResetPoint.X, y - ResetPoint.Y, token);
        if (result.Success && stepDelay > TimeSpan.Zero) await Task.Delay(stepDelay, token);
        return result;
    }
}
