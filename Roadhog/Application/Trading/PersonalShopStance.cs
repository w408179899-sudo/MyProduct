using Roadhog.Core.Api;
using Roadhog.Core.Model;

namespace Roadhog.Application.Trading;

/// <summary>Confirm sheathing through the official player snapshot before opening a stall.</summary>
public static class PersonalShopStance
{
    public static async Task EnsureNormalAsync(TradingActions actions, IRoadhogSnapshotReader snapshots,
        Action<string> report, CancellationToken token)
    {
        async Task<PlayerSnapshot> Player() => (await snapshots.ReadPlayerAsync().WaitAsync(token)).Value;
        await actions.Alive();
        if (!(await Player()).IsCombatStance) return;
        report("角色处于战斗姿态，按 X 切换普通姿态");
        await actions.Key("X");
        await actions.Wait(Player, p => !p.IsCombatStance);
        report("已确认普通姿态，继续摆摊");
    }
}
