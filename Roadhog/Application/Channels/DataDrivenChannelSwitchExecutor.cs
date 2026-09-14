using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.Channels;

/// <summary>One bounded attempt shared by fixed-channel correction and the manual button.</summary>
public sealed class DataDrivenChannelSwitchExecutor(
    IKeyboardInput input, IRoadhogSnapshotReaderFactory readers, IRoadhogLogger logger) : IFixedChannelSwitchExecutor
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<OperationResult> ExecuteAsync(FixedChannelSwitchRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Config is null) return OperationResult.Fail("缺少账号读取配置。");
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return OperationResult.Fail("频道切换正在执行，请等待本次完成。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Cursor convergence and repeated combat checks can exceed 15 seconds before the final click.
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            var snapshots = readers.Create(request.Config, logger, timeout.Token);
            return await new ChannelSwitchSequence(input, logger).RunAsync(
                snapshots, request.AccountName, request.TargetChannelNumber, timeout.Token,
                request.CanUseMouseAsync, () => timeout.CancelAfter(Timeout.InfiniteTimeSpan)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return OperationResult.Fail("频道切换执行超时，本次已停止；请查看日志中的中断步骤和游戏提示。");
        }
        finally { timeout.Cancel(); _gate.Release(); }
    }
}

internal sealed class ChannelSwitchSequence(IKeyboardInput input, IRoadhogLogger logger, TimeSpan? settleDelay = null,
    ChannelTransitionWaiter? transitionWaiter = null)
{
    private readonly TimeSpan _settle = settleDelay ?? TimeSpan.FromMilliseconds(150);

    public async Task<OperationResult> RunAsync(IRoadhogSnapshotReader snapshots, string account, int target, CancellationToken token,
        Func<IRoadhogSnapshotReader, Task<bool>>? canUseMouseAsync = null, Action? submitted = null)
    {
        var stage = "读取当前频道";
        try
        {
            var channel = (await snapshots.ReadChannelAsync().ConfigureAwait(false)).Value;
            if (target < 1 || target > channel.Count) return OperationResult.Fail($"当前地图只有 {channel.Count} 个频道，请选择有效频道。");
            if (channel.Number == target) return OperationResult.Ok();
            var mapId = channel.MapId;
            await Guard();
            foreach (var key in new[] { "W", "A", "S", "D" })
                Check(await input.KeyUpAsync(key, token).ConfigureAwait(false));
            Check(await input.MouseUpAsync(RoadhogMouseButton.Right, token).ConfigureAwait(false));
            var ui = await Ui();
            if (!ui.DialogOpen)
            {
                if (ui.ServiceItem is null)
                {
                    stage = "打开菜单";
                    await Click(s => s.MenuButton);
                    ui = await Wait(s => s.ServiceItem != null);
                }
                stage = "展开服务菜单";
                await Move(s => s.ServiceItem);
                ui = await Wait(s => s.SwitchChannelItem != null);
                // Enter the submenu horizontally before moving down, so other main menu rows cannot steal hover.
                await Move(s => s.SwitchChannelItem is { } p ? new ChannelUiPoint(p.X, s.Cursor.Y) : null);
                stage = "打开频道移动窗口";
                await Click(s => s.SwitchChannelItem);
                ui = await Wait(s => s.DialogOpen);
            }
            stage = "选择目标频道";
            if (ui.SelectedChannelNumber != target || ui.DropdownOpen)
            {
                if (!ui.DropdownOpen)
                {
                    await Click(s => s.DialogOpen ? s.DropdownButton : null);
                    ui = await Wait(s => s.DialogOpen && s.DropdownOpen);
                }
                var option = ui.Options.SingleOrDefault(o => o.Number == target);
                if (option is null || !option.Enabled || option.Point is null)
                    return OperationResult.Fail("目标频道不可选，或频道列表没有完整显示；本次未提交移动。");
                await Click(s => s.DialogOpen && s.DropdownOpen ? s.Options.SingleOrDefault(o => o.Number == target && o.Enabled)?.Point : null);
                ui = await Wait(s => s.DialogOpen && !s.DropdownOpen && s.SelectedChannelNumber == target);
            }
            var beforeSubmit = (await snapshots.ReadChannelAsync().ConfigureAwait(false)).Value;
            if (beforeSubmit.MapId != mapId) return OperationResult.Fail("地图已变化，本次频道切换已停止。");
            if (beforeSubmit.Number == target) return OperationResult.Ok();
            stage = "提交频道移动";
            await Click(s => s.DialogOpen && !s.DropdownOpen && s.SelectedChannelNumber == target ? s.MoveButton : null,
                TimeSpan.Zero, submitted);
            stage = "等待过图和角色恢复";
            return await (transitionWaiter ?? new ChannelTransitionWaiter(logger))
                .WaitAsync(snapshots, account, target, mapId, token, Guard).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return Failed("未确认成功，请查看游戏提示（可能仍在冷却中）。本次不会重复提交移动。");
        }
        catch (OperationCanceledException)
        {
            logger.Info("channel_switch.interrupted", new Dictionary<string, object?>
            {
                ["account"] = account, ["target"] = target, ["stage"] = stage,
                ["reason"] = "操作取消或整段执行时限已到"
            });
            throw;
        }
        catch (InvalidOperationException ex) { return Failed(ex.Message); }

        OperationResult Failed(string error)
        {
            logger.Warn("channel_switch.failed", new Dictionary<string, object?> { ["account"] = account, ["target"] = target, ["stage"] = stage, ["error"] = error });
            return OperationResult.Fail(stage + "：" + error);
        }
        async Task Guard()
        {
            token.ThrowIfCancellationRequested();
            if (canUseMouseAsync is not null && !await canUseMouseAsync(snapshots).ConfigureAwait(false))
                throw new InvalidOperationException("脱战条件已不满足，本次停止切换并继续挂机。");
        }
        async Task<ChannelSwitchUiSnapshot> Ui()
        {
            await Guard();
            return (await snapshots.ReadChannelSwitchUiAsync().ConfigureAwait(false)).Value;
        }
        async Task<ChannelSwitchUiSnapshot> Wait(Func<ChannelSwitchUiSnapshot, bool> predicate)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            while (true)
            {
                await Guard();
                var value = (await snapshots.ReadChannelSwitchUiAsync().WaitAsync(deadline.Token).ConfigureAwait(false)).Value;
                if (predicate(value)) return value;
                await Task.Delay(_settle, deadline.Token).ConfigureAwait(false);
            }
        }
        async Task Move(Func<ChannelSwitchUiSnapshot, ChannelUiPoint?> locate)
        {
            for (var attempt = 0; attempt < 40; attempt++)
            {
                token.ThrowIfCancellationRequested();
                var state = await Ui();
                var point = locate(state) ?? throw new InvalidOperationException("目标控件未显示或不可点击。");
                if (point.X < 0 || point.Y < 0 || point.X >= state.Width || point.Y >= state.Height)
                    throw new InvalidOperationException("目标控件位于游戏窗口外。");
                var dx = point.X - state.Cursor.X;
                var dy = point.Y - state.Cursor.Y;
                if (Math.Abs(dx) <= 2 && Math.Abs(dy) <= 2) return;
                Check(await input.MoveMouseRelativeAsync(Math.Clamp(dx, -100, 100), Math.Clamp(dy, -100, 100), token).ConfigureAwait(false));
                await Task.Delay(_settle, token).ConfigureAwait(false);
            }
            throw new InvalidOperationException("鼠标未到达目标控件。");
        }
        async Task Click(Func<ChannelSwitchUiSnapshot, ChannelUiPoint?> locate, TimeSpan? afterClickDelay = null,
            Action? beforePress = null)
        {
            await Move(locate);
            // Re-check the semantic control at the final cursor position, immediately before pressing.
            var state = await Ui();
            var point = locate(state);
            if (point is null || Math.Abs(point.X - state.Cursor.X) > 2 || Math.Abs(point.Y - state.Cursor.Y) > 2)
                throw new InvalidOperationException("点击前控件位置发生变化。");
            // Once the final press begins, only user stop may cancel the recovery wait.
            // Retire the navigation timer before mouse-down so it cannot fire during the press.
            beforePress?.Invoke();
            token.ThrowIfCancellationRequested();
            Check(await input.MouseDownAsync(RoadhogMouseButton.Left, token).ConfigureAwait(false));
            try { await Task.Delay(TimeSpan.FromMilliseconds(35), token).ConfigureAwait(false); }
            finally { Check(await input.MouseUpAsync(RoadhogMouseButton.Left, CancellationToken.None).ConfigureAwait(false)); }
            logger.Info("channel_switch.clicked", new Dictionary<string, object?> { ["account"] = account, ["stage"] = stage, ["x"] = point.X, ["y"] = point.Y });
            await Task.Delay(afterClickDelay ?? _settle, token).ConfigureAwait(false);
        }
    }

    private static void Check(OperationResult result)
    {
        if (!result.Success) throw new InvalidOperationException(result.Error ?? "鼠标输入失败。");
    }
}
