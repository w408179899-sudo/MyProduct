using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;

namespace Roadhog.Application.Channels;

internal sealed class ChannelTransitionWaiter(IRoadhogLogger logger, TimeProvider? clock = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    internal static readonly TimeSpan EntryWindow = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan SlowLoadWarning = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(200);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public async Task<OperationResult> WaitAsync(IRoadhogSnapshotReader snapshots, string account, int target,
        uint mapId, CancellationToken token, Func<Task>? guard = null)
    {
        var started = _clock.GetUtcNow();
        DateTimeOffset? loadingAt = null;
        var warned = false;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            // Keep a single provider request alive even during a cold wait/reconnect.
            var pending = snapshots.ReadChannelTransitionAsync();
            while (!pending.IsCompleted)
            {
                WarnIfSlow();
                await _delay(Poll, token).ConfigureAwait(false);
            }
            var scene = (await pending.ConfigureAwait(false)).Value;
            if (!scene.IsReady)
            {
                if (loadingAt is null)
                {
                    loadingAt = _clock.GetUtcNow();
                    logger.Info("channel_switch.loading", Fields());
                }
                WarnIfSlow();
            }
            else if (loadingAt is not null)
            {
                var actual = scene.Channel!;
                var success = actual.MapId == mapId && actual.Number == target;
                logger.Info("channel_switch.world_ready", new Dictionary<string, object?>
                {
                    ["account"] = account, ["channel"] = actual.Number, ["mapId"] = actual.MapId,
                    ["entityId"] = scene.Player!.EntityId, ["success"] = success
                });
                return success ? OperationResult.Ok() : OperationResult.Fail("角色已恢复，但未到达目标频道；稍后重试。");
            }
            else
            {
                if (guard is not null) await guard().ConfigureAwait(false);
                if (_clock.GetUtcNow() - started >= EntryWindow)
                {
                    logger.Info("channel_switch.no_loading", Fields());
                    return OperationResult.Fail("点击移动后 5 秒内未观察到过图；继续挂机，稍后重试。");
                }
            }
            await _delay(Poll, token).ConfigureAwait(false);
        }

        Dictionary<string, object?> Fields() => new()
        {
            ["account"] = account, ["target"] = target, ["elapsedMs"] = (_clock.GetUtcNow() - started).TotalMilliseconds
        };
        void WarnIfSlow()
        {
            if (warned || _clock.GetUtcNow() - (loadingAt ?? started) < SlowLoadWarning) return;
            warned = true;
            logger.Warn("channel_switch.loading_slow", Fields());
        }
    }
}
