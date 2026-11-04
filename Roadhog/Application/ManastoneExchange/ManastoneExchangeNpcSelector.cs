using Roadhog.Application.Trading;
using Roadhog.Core.Api;
using Roadhog.Core.Model;

namespace Roadhog.Application.ManastoneExchange;

internal static class ManastoneExchangeNpcSelector
{
    public const string NpcName = "比洛加";

    public static async Task<LockedTargetSnapshot> SelectAsync(IRoadhogSnapshotReader snapshots,
        TradingActions actions, IProgress<string>? progress, CancellationToken token)
    {
        await actions.Alive();
        var dialog = (await snapshots.ReadManastoneExchangeAsync().WaitAsync(token)).Value;
        TradingActions.Require(!dialog.OtherModalOpen, "请先关闭其他确认窗口。");
        if (dialog.IsOpen)
        {
            // Preserve an accepted task or already-paid reward when resuming.
            var current = (await snapshots.ReadLockedTargetAsync().WaitAsync(token)).Value;
            TradingActions.Require(IsSelected(current) && dialog.NpcServerObjectId == current.ServerObjectId,
                "请先关闭其他 NPC 的对话，再开始魔石兑换。");
            return current;
        }

        progress?.Report($"先按 F1，再按 F8 寻找 {NpcName}……");
        await actions.Key("F1");
        await actions.Pause(100);
        for (var attempt = 0; attempt < 30; attempt++)
        {
            await actions.Key("F8");
            for (var poll = 0; poll < 10; poll++)
            {
                await actions.Alive();
                var target = (await snapshots.ReadLockedTargetAsync().WaitAsync(token)).Value;
                if (IsSelected(target)) return target;
                await actions.Pause(100);
            }
        }
        throw new InvalidOperationException($"未找到 {NpcName}，请靠近该 NPC 后重新点击兑换魔石。");
    }

    private static bool IsSelected(LockedTargetSnapshot target) =>
        target.HasTarget && target.ServerObjectId != 0 && target.Name == NpcName;
}
