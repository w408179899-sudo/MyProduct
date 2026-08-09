using Roadhog.Application.Input;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;

namespace Roadhog.Application.Channels;

public sealed class FixedChannelMouseSwitchExecutor : IFixedChannelSwitchExecutor
{
    public static readonly TimeSpan DefaultPointHoverDelay = TimeSpan.FromMilliseconds(50);

    public static readonly TimeSpan DefaultClickHoldDelay = TimeSpan.FromMilliseconds(35);

    public static readonly TimeSpan DefaultInterClickDelay = TimeSpan.FromMilliseconds(750);

    private readonly IKeyboardInput _input;
    private readonly IRoadhogLogger _logger;
    private readonly TimeSpan _pointHoverDelay;
    private readonly TimeSpan _clickHoldDelay;
    private readonly TimeSpan _interClickDelay;
    private readonly TimeSpan _mouseStepDelay;

    public FixedChannelMouseSwitchExecutor(
        IKeyboardInput input,
        IRoadhogLogger logger,
        TimeSpan? pointHoverDelay = null,
        TimeSpan? clickHoldDelay = null,
        TimeSpan? interClickDelay = null,
        TimeSpan? mouseStepDelay = null)
    {
        _input = input;
        _logger = logger;
        _pointHoverDelay = ClampDelay(pointHoverDelay ?? DefaultPointHoverDelay);
        _clickHoldDelay = ClampDelay(clickHoldDelay ?? DefaultClickHoldDelay);
        _interClickDelay = ClampDelay(interClickDelay ?? DefaultInterClickDelay);
        _mouseStepDelay = ClampDelay(mouseStepDelay ?? TimeSpan.FromMilliseconds(ScreenPointMouseMover.DefaultStepDelayMs));
    }

    public async Task<OperationResult> ExecuteAsync(
        FixedChannelSwitchRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(request.ClickPoints);
        if (!validation.Success)
        {
            return validation;
        }

        for (var index = 0; index < request.ClickPoints.Count; index++)
        {
            var point = request.ClickPoints[index];
            var previousPoint = index == 0 ? null : request.ClickPoints[index - 1];
            var click = await ClickAsync(point, previousPoint, cancellationToken).ConfigureAwait(false);
            if (!click.Success)
            {
                return click;
            }

            _logger.Info("fixed_channel.mouse.step_clicked", new Dictionary<string, object?>
            {
                ["account"] = request.AccountName,
                ["attemptNumber"] = request.AttemptNumber,
                ["stepNumber"] = index + 1,
                ["step"] = FixedChannelClickPlan.Name(point.Step),
                ["x"] = point.X,
                ["y"] = point.Y,
                ["moveMode"] = UsesPreviousPointMove(point, previousPoint)
                    ? "from_previous_point"
                    : "top_left_rebase"
            });

            if (index + 1 < request.ClickPoints.Count)
            {
                await DelayAsync(_interClickDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        _logger.Info("fixed_channel.mouse.sequence_completed", new Dictionary<string, object?>
        {
            ["account"] = request.AccountName,
            ["attemptNumber"] = request.AttemptNumber,
            ["targetChannelNumber"] = request.TargetChannelNumber,
            ["mapId"] = request.MapId,
            ["clickCount"] = request.ClickPoints.Count
        });
        return OperationResult.Ok();
    }

    private async Task<OperationResult> ClickAsync(
        FixedChannelClickPoint point,
        FixedChannelClickPoint? previousPoint,
        CancellationToken cancellationToken)
    {
        var name = FixedChannelClickPlan.Name(point.Step);
        var move = await MoveToPointAsync(point, previousPoint, cancellationToken).ConfigureAwait(false);
        if (!move.Success)
        {
            return OperationResult.Fail(name + " move failed: " + move.Error);
        }

        await DelayAsync(_pointHoverDelay, cancellationToken).ConfigureAwait(false);
        var down = await _input
            .MouseDownAsync(RoadhogMouseButton.Left, cancellationToken)
            .ConfigureAwait(false);
        if (!down.Success)
        {
            return OperationResult.Fail(name + " mouse down failed: " + down.Error);
        }

        try
        {
            await DelayAsync(_clickHoldDelay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await _input.MouseUpAsync(RoadhogMouseButton.Left, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        var up = await _input
            .MouseUpAsync(RoadhogMouseButton.Left, cancellationToken)
            .ConfigureAwait(false);
        return up.Success
            ? OperationResult.Ok()
            : OperationResult.Fail(name + " mouse up failed: " + up.Error);
    }

    private Task<OperationResult> MoveToPointAsync(
        FixedChannelClickPoint point,
        FixedChannelClickPoint? previousPoint,
        CancellationToken cancellationToken)
    {
        if (UsesPreviousPointMove(point, previousPoint))
        {
            return _input.MoveMouseRelativeAsync(
                point.X - previousPoint!.X,
                point.Y - previousPoint.Y,
                cancellationToken);
        }

        return ScreenPointMouseMover.MoveToAsync(
            _input,
            point.X,
            point.Y,
            stepDelay: _mouseStepDelay,
            cancellationToken: cancellationToken);
    }

    private static bool UsesPreviousPointMove(
        FixedChannelClickPoint point,
        FixedChannelClickPoint? previousPoint)
    {
        return point.Step == FixedChannelClickStep.SwitchChannel &&
               previousPoint?.Step == FixedChannelClickStep.Service;
    }

    private static OperationResult Validate(IReadOnlyList<FixedChannelClickPoint>? points)
    {
        if (points is null || points.Count != FixedChannelClickPlan.OrderedSteps.Count)
        {
            return OperationResult.Fail("Fixed-channel switching requires exactly six click points.");
        }

        for (var index = 0; index < FixedChannelClickPlan.OrderedSteps.Count; index++)
        {
            var point = points[index];
            var expected = FixedChannelClickPlan.OrderedSteps[index];
            if (point.Step != expected)
            {
                return OperationResult.Fail(
                    "Fixed-channel click order is invalid at step " + (index + 1) + ".");
            }

            if (!point.IsConfigured)
            {
                return OperationResult.Fail(
                    "Fixed-channel " + FixedChannelClickPlan.Name(point.Step) + " point is not configured.");
            }
        }

        return OperationResult.Ok();
    }

    private static TimeSpan ClampDelay(TimeSpan delay)
    {
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    private static Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return delay <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(delay, cancellationToken);
    }
}
