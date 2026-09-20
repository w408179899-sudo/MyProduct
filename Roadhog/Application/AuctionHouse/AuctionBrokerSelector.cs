using Roadhog.Application.Trading;
using Roadhog.Core.Api;
using Roadhog.Core.Input;

namespace Roadhog.Application.AuctionHouse;

/// <summary>Resolves the path's NPC by name while retaining the broker identity requirement.</summary>
public sealed class AuctionBrokerSelector(IKeyboardInput input, IRoadhogSnapshotReader snapshots,
    string? configuredName, CancellationToken token, Func<int, CancellationToken, Task>? delay = null)
{
    private readonly string npcName = configuredName?.Trim() ?? string.Empty;
    private readonly TradingActions actions = new(input, snapshots, token, delay);

    public async Task<bool> IsSelectedAsync()
    {
        var target = (await snapshots.ReadLockedTargetAsync().WaitAsync(token)).Value;
        if (!target.HasTarget || target.ServerObjectId == 0 ||
            (npcName.Length > 0 && !string.Equals(target.Name.Trim(), npcName, StringComparison.OrdinalIgnoreCase))) return false;
        return target.Name == "摩比隆" ||
            (await snapshots.ReadAuctionHouseAsync().WaitAsync(token)).Value.BrokerTargetServerObjectId == target.ServerObjectId;
    }

    public async Task SelectAsync()
    {
        await actions.Alive();
        if (await IsSelectedAsync()) return;
        // Like merchant selection, retry F8; each attempt polls actual target data instead of a fixed wait.
        for (var attempt = 0; attempt < 30; attempt++)
        {
            await actions.Key("F8");
            for (var poll = 0; poll < 10; poll++)
            {
                await actions.Alive();
                if (await IsSelectedAsync()) return;
                await actions.Pause(100);
            }
        }
        throw new InvalidOperationException(npcName.Length == 0
            ? "多次选择后仍未确认交易中介。"
            : "未选中配置的拍卖 NPC，或其交易中介身份未确认：" + npcName);
    }
}
