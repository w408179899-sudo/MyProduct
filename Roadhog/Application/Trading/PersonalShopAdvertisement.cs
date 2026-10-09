using Roadhog.Core.Model;
using static Roadhog.Application.Trading.TradingActions;

namespace Roadhog.Application.Trading;

public static class PersonalShopAdvertisement
{
    public static string Text(int discount)
    {
        if (discount is < 4 or > 9) throw new ArgumentOutOfRangeException(nameof(discount));
        return (discount * 10).ToString(System.Globalization.CultureInfo.InvariantCulture) + " %";
    }

    public static async Task EnsureAsync(TradingActions actions, Func<Task<PersonalShopSnapshot>> read,
        int discount, Action<string> report)
    {
        var expected = Text(discount);
        bool Idle(PersonalShopSnapshot s) => s.IsOpen && !s.IsSelling && s.Editor == null &&
            s.Listings.Count == 0 && !s.OtherModalOpen;
        var state = await read();
        Require(Idle(state), "修改摊位文字前界面不可操作。");
        if (state.AdvertisementText != expected)
        {
            if (state.InventoryOpen)
            {
                await actions.Key("I");
                await actions.Wait(read, s => Idle(s) && !s.InventoryOpen);
            }
            report("设置摊位文字：" + expected);
            await actions.Percentage(read, s => s.AdvertisementInput,
                s => Idle(s) && !s.InventoryOpen, (uint)(discount * 10));
            state = await actions.Wait(read, s => Idle(s) && s.AdvertisementText == expected);
        }
        if (!state.InventoryOpen)
        {
            // Clicking the first empty listing cell releases edit focus without opening
            // the bag or submitting a sale. Keyboard shortcuts work only afterward.
            await actions.Click(read, s => s.AdvertisementBlurPoint,
                s => Idle(s) && !s.InventoryOpen && s.AdvertisementText == expected);
            await actions.Pause(100);
            await actions.Wait(read, s => Idle(s) && s.AdvertisementText == expected);
        }
        report("摊位文字已确认：" + expected);
    }
}
