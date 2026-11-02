using Roadhog.Core.Api;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using static Roadhog.Application.Trading.TradingActions;

namespace Roadhog.Application.Trading;

/// <summary>Cancels UI state only. Never submits, confirms, or replays a trade.</summary>
internal sealed class TradingWindowRecovery(IKeyboardInput input, IRoadhogSnapshotReader snapshots,
    Func<int, CancellationToken, Task>? delay = null)
{
    public async Task<bool> TryCloseAsync(Action<string> report, CancellationToken token, bool includeNpc = true,
        TimeSpan? timeout = null, bool includeShop = true)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(12));
        using var reads = SnapshotReadBudget.EnterCancellation(deadline.Token);
        var actions = new TradingActions(input, snapshots, deadline.Token, delay);
        try
        {
            await actions.Reset();
            if (includeShop) await CloseShopAsync(actions, deadline.Token).WaitAsync(deadline.Token);
            if (includeNpc) await CloseNpcAsync(actions, deadline.Token).WaitAsync(deadline.Token);
            await CloseBagAsync(actions, deadline.Token).WaitAsync(deadline.Token);
            return true;
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            report("交易界面收尾未完成，放弃本次交易并交回主流程：" + ex.Message);
            return false;
        }
        finally { deadline.Cancel(); }
    }

    private async Task CloseShopAsync(TradingActions actions, CancellationToken token)
    {
        async Task<PersonalShopSnapshot> Ui() => (await snapshots.ReadPersonalShopAsync().WaitAsync(token)).Value;
        var ui = await Ui();
        if (ui.Editor != null || ui.OtherModalOpen)
        {
            await actions.Key("Escape");
            ui = await actions.Wait(Ui, s => s.Editor == null && !s.OtherModalOpen, 2000);
        }
        if (ui.IsSelling)
        {
            try
            {
                await actions.Click(Ui, s => s.StopButton, s => s.IsSelling && s.Editor == null && !s.OtherModalOpen);
            }
            catch (InvalidOperationException) when (!token.IsCancellationRequested)
            {
                // The last buyer can stop the stall between observation and click.
                if ((await Ui()).IsSelling) throw;
            }
            await actions.Wait(Ui, s => !s.IsSelling, 2000);
        }
        if ((await Ui()).IsOpen)
        {
            await actions.Key("Y");
            await actions.Wait(Ui, s => !s.IsOpen && !s.IsSelling && s.Editor == null && !s.OtherModalOpen, 2000);
        }
    }

    private async Task CloseNpcAsync(TradingActions actions, CancellationToken token)
    {
        async Task<NpcTradeSnapshot> Ui() => (await snapshots.ReadNpcTradeAsync().WaitAsync(token)).Value;
        static bool Closed(NpcTradeSnapshot s) => !s.IsOpen && !s.DialogOpen && !s.OtherModalOpen;
        if (Closed(await Ui())) return;
        await actions.Key("Space");
        try { await actions.Wait(Ui, Closed, 2000); }
        catch (TradingActions.ConfirmationTimeoutException)
        {
            await actions.Key("Escape");
            await actions.Key("Space");
            await actions.Wait(Ui, Closed, 2000);
        }
    }

    private async Task CloseBagAsync(TradingActions actions, CancellationToken token)
    {
        async Task<InventoryInteractionSnapshot> Bag() =>
            (await snapshots.ReadInventoryInteractionAsync().WaitAsync(token)).Value;
        var bag = await Bag();
        Require(!bag.ShopIsOpen && !bag.IsSelling, "仍有交易占用背包，不能切换背包窗口。");
        if (bag.DiscardDialog != null || bag.OtherModalOpen || bag.PendingDiscardInstanceId != 0)
        {
            await actions.Key("Escape");
            bag = await actions.Wait(Bag, s => s.DiscardDialog == null && !s.OtherModalOpen && s.PendingDiscardInstanceId == 0, 2000);
        }
        if (bag.IsOpen)
        {
            await actions.Key("I");
            await actions.Wait(Bag, s => !s.IsOpen, 2000);
        }
    }
}
