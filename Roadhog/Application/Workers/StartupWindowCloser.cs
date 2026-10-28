using Roadhog.Application.Trading;
using Roadhog.Core.Api;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.Workers;

/// <summary>One startup pass: stop selling, close the shop, then recheck and close the bag.</summary>
public sealed class StartupWindowCloser(IKeyboardInput input, int timeoutMs = 15000)
{
    public async Task RunAsync(AccountWorkerContext context)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.StopToken);
        deadline.CancelAfter(timeoutMs);
        using var reads = SnapshotReadBudget.EnterCancellation(deadline.Token);
        var token = deadline.Token;
        var actions = new TradingActions(input, context.Snapshots, token);
        var phase = "inspect";
        async Task<PersonalShopSnapshot> Shop() =>
            (await context.Snapshots.ReadPersonalShopAsync().WaitAsync(token).ConfigureAwait(false)).Value;
        async Task<InventoryInteractionSnapshot> Bag() =>
            (await context.Snapshots.ReadInventoryInteractionAsync().WaitAsync(token).ConfigureAwait(false)).Value;
        try
        {
            token.ThrowIfCancellationRequested();
            var inventory = await Bag().ConfigureAwait(false);
            if (inventory.ShopIsOpen || inventory.IsSelling)
            {
                var shop = await Shop().ConfigureAwait(false);
                if (shop.IsSelling)
                {
                    phase = "stop_selling";
                    await actions.Click(Shop, s => s.StopButton,
                        s => s.IsSelling && s.Editor == null && !s.OtherModalOpen).ConfigureAwait(false);
                    await actions.Wait(Shop, s => !s.IsSelling).ConfigureAwait(false);
                }

                phase = "close_shop";
                shop = await Shop().ConfigureAwait(false);
                TradingActions.Require(!shop.IsSelling && shop.Editor == null && !shop.OtherModalOpen,
                    "启动关闭摊位前界面状态改变。");
                if (shop.IsOpen)
                {
                    await actions.Key("Y").ConfigureAwait(false);
                    await actions.Wait(Shop, s => !s.IsOpen && !s.IsSelling).ConfigureAwait(false);
                }
                // Closing the shop may also change the bag. Never reuse its initial open state.
                inventory = await actions.Wait(Bag, s => !s.ShopIsOpen && !s.IsSelling).ConfigureAwait(false);
            }

            phase = "close_inventory";
            if (inventory.IsOpen)
            {
                TradingActions.Require(!inventory.ShopIsOpen && !inventory.IsSelling &&
                    inventory.DiscardDialog == null && !inventory.OtherModalOpen,
                    "启动关闭背包前界面不可操作。");
                await actions.Key("I").ConfigureAwait(false);
                await actions.Wait(Bag, s => !s.IsOpen && !s.ShopIsOpen && !s.IsSelling).ConfigureAwait(false);
            }
            context.Logger.Info("worker.startup.windows_closed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName
            });
        }
        catch (Exception ex) when (!context.StopToken.IsCancellationRequested)
        {
            // A failed startup cleanup must not stop the account or replay a toggle.
            context.Logger.Warn("worker.startup.windows_close_failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["phase"] = phase,
                ["error"] = ex.Message,
                ["timedOut"] = deadline.IsCancellationRequested || ex is TimeoutException
            });
        }
        finally { deadline.Cancel(); }
    }
}
