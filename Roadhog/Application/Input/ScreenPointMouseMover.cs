using Roadhog.Core.Common;
using Roadhog.Core.Input;

namespace Roadhog.Application.Input;

public static class ScreenPointMouseMover
{
    public const int AbsoluteMouseResetDelta = -2000;
    public const int DefaultResetCount = 2;
    public const int DefaultStepDelayMs = 10;

    public static async Task<OperationResult> MoveToAsync(
        IKeyboardInput input,
        int x,
        int y,
        int resetCount = DefaultResetCount,
        TimeSpan? stepDelay = null,
        CancellationToken cancellationToken = default)
    {
        if (x < 0 || y < 0 || x > short.MaxValue || y > short.MaxValue)
        {
            return OperationResult.Fail("Absolute mouse target must be between 0 and 32767.");
        }

        var effectiveResetCount = Math.Clamp(resetCount, 1, 10);
        var effectiveStepDelay = stepDelay.GetValueOrDefault(TimeSpan.FromMilliseconds(DefaultStepDelayMs));
        if (effectiveStepDelay < TimeSpan.Zero)
        {
            effectiveStepDelay = TimeSpan.Zero;
        }

        for (var i = 0; i < effectiveResetCount; i++)
        {
            var reset = await input
                .MoveMouseRelativeAsync(AbsoluteMouseResetDelta, AbsoluteMouseResetDelta, cancellationToken)
                .ConfigureAwait(false);
            if (!reset.Success)
            {
                return OperationResult.Fail("Absolute mouse reset failed. " + reset.Error);
            }

            await DelayAsync(effectiveStepDelay, cancellationToken).ConfigureAwait(false);
        }

        var move = await input.MoveMouseRelativeAsync(x, y, cancellationToken).ConfigureAwait(false);
        if (!move.Success)
        {
            return OperationResult.Fail("Absolute mouse target move failed. " + move.Error);
        }

        await DelayAsync(effectiveStepDelay, cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok();
    }

    private static Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return delay <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(delay, cancellationToken);
    }
}
