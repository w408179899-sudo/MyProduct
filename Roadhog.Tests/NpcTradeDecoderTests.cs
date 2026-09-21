using Roadhog.Core.Model;

internal static partial class PersonalShopDecoderTests
{
    public static Task NpcTradeAsync()
    {
        var m = new Fixture();
        const ulong trade = 0x30000000, container = 0x31000000, button = 0x32000000;
        m.U(Fixture.Game + 0xD63990 + 134 * 8, trade);
        m.Widget(trade, "trade_dialog", 20, 30, 320, 450, 0, 0);
        m.Widget(container, "sell_item_container", 0, 0, 280, 320, 0, 0);
        m.Widget(button, "ok", 200, 350, 50, 20, 0, 0);
        m.Children(trade, container, button); m.I(trade + 1252, 1); m.I(trade + 1240, 42);
        m.U(trade + 1328, Fixture.List);
        var s = m.Decoder().ReadNpcTrade(Fixture.Game);
        Require(s.IsSelling && s.NpcServerObjectId == 42 && s.Basket.Single() == new NpcTradeItem(123, 567, 26) && s.SellButton == new GameUiPoint(245, 390), "NPC identity basket and translated button geometry");
        m.ShortAddress = Fixture.Item + 160; Reject(() => m.Decoder().ReadNpcTrade(Fixture.Game), "short basket read rejected"); m.ShortAddress = null;
        m.U(Fixture.Item + 176, 0); Reject(() => m.Decoder().ReadNpcTrade(Fixture.Game), "zero quantity rejected"); m.U(Fixture.Item + 176, 26);
        m.I(trade + 1252, 3); Require(!m.Decoder().ReadNpcTrade(Fixture.Game).IsSelling, "personal-shop purchase is never an NPC sale");
        m.I(trade + 1252, 1);
        const ulong modal = 0x33000000;
        m.U(Fixture.Game + 0xD63990 + 336 * 8, modal); m.Widget(modal, "msgbox", 0, 0, 100, 100, 0, 0);
        Require(m.Decoder().ReadNpcTrade(Fixture.Game) is { OtherModalOpen: true, SellButton: null }, "modal hides sell control");
        return Task.CompletedTask;
    }
}
