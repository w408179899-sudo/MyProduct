using Roadhog.Core.Common;

namespace Roadhog.Application.Channels;

public sealed record FixedChannelSwitchRequest(
    string AccountName,
    int TargetChannelNumber,
    uint MapId,
    int AttemptNumber,
    IReadOnlyList<FixedChannelClickPoint> ClickPoints);

public sealed record FixedChannelClickPoint(
    FixedChannelClickStep Step,
    int X,
    int Y)
{
    public bool IsConfigured => X > 0 && Y > 0 && X <= short.MaxValue && Y <= short.MaxValue;
}

public enum FixedChannelClickStep
{
    Menu,
    Service,
    SwitchChannel,
    ChannelMove,
    SelectChannel,
    Move
}

public interface IFixedChannelSwitchExecutor
{
    Task<OperationResult> ExecuteAsync(
        FixedChannelSwitchRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class PendingFixedChannelSwitchExecutor : IFixedChannelSwitchExecutor
{
    public Task<OperationResult> ExecuteAsync(
        FixedChannelSwitchRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult.Fail(
            "Fixed-channel mouse switching is not implemented."));
    }
}
