using Roadhog.Core.Model;

internal static partial class PersonalShopDecoderTests
{
    public static Task NpcTradeAsync()
    {
        var m = new Fixture();
        const ulong trade = 0x30000000, container = 0x31000000, button = 0x32000000, junk = 0x35000000;
        m.U(Fixture.Game + 0xD63990 + 134 * 8, trade);
        m.Widget(trade, "trade_dialog", 20, 30, 320, 450, 0, 0);
        m.Widget(container, "sell_item_container", 0, 0, 280, 320, 0, 0);
        m.Widget(button, "ok", 200, 350, 50, 20, 0, 0);
        m.Widget(junk, "sell_junk", 60, 350, 100, 20, 0, 0);
        m.Children(trade, container, button, junk); m.I(trade + 1252, 1); m.I(trade + 1240, 42);
        m.U(trade + 1328, Fixture.List);
        var s = m.Decoder().ReadNpcTrade(Fixture.Game);
        Require(s.IsSelling && s.NpcServerObjectId == 42 && s.Basket.Single() == new NpcTradeItem(123, 567, 26) && s.SellButton == new GameUiPoint(245, 390), "NPC identity basket and translated button geometry");
        Require(s.RegisterAllJunkButton == new GameUiPoint(130, 390), "junk registration control uses window geometry");
        m.U(junk + 0x28, 1);
        Require(m.Decoder().ReadNpcTrade(Fixture.Game).RegisterAllJunkButton == null, "disabled junk registration cannot be clicked");
        m.U(junk + 0x28, 3);
        m.ShortAddress = junk + 0x58; Reject(() => m.Decoder().ReadNpcTrade(Fixture.Game), "short junk control geometry rejected"); m.ShortAddress = null;
        m.ShortAddress = Fixture.Item + 160; Reject(() => m.Decoder().ReadNpcTrade(Fixture.Game), "short basket read rejected"); m.ShortAddress = null;
        m.U(Fixture.Item + 176, 0); Reject(() => m.Decoder().ReadNpcTrade(Fixture.Game), "zero quantity rejected"); m.U(Fixture.Item + 176, 26);
        m.I(trade + 1252, 3); Require(!m.Decoder().ReadNpcTrade(Fixture.Game).IsSelling, "personal-shop purchase is never an NPC sale");
        Require(m.Decoder().ReadNpcTrade(Fixture.Game).RegisterAllJunkButton == null, "other trade modes expose no junk action");
        m.I(trade + 1252, 1);
        const ulong bag = 0x34000000;
        m.U(Fixture.Game + 0xD63990 + 27 * 8, bag);
        m.Widget(bag, "inventory_dialog", 600, 20, 300, 600, 0, 0);
        Require(m.Decoder().ReadNpcTrade(Fixture.Game) is { InventoryOpen: true, SellButton: not null }, "open inventory outside sell button does not block NPC sale");
        m.D(bag + 0x58, 100);
        Require(m.Decoder().ReadNpcTrade(Fixture.Game).SellButton == null, "inventory covering sell button blocks click");
        Require(m.Decoder().ReadNpcTrade(Fixture.Game).RegisterAllJunkButton == null, "inventory covering junk button blocks click");
        m.D(bag + 0x58, 600);
        m.ShortAddress = bag + 0x58;
        Reject(() => m.Decoder().ReadNpcTrade(Fixture.Game), "incomplete inventory geometry cannot expose a sell button");
        m.ShortAddress = null;
        const ulong modal = 0x33000000;
        m.U(Fixture.Game + 0xD63990 + 336 * 8, modal); m.Widget(modal, "msgbox", 0, 0, 100, 100, 0, 0);
        Require(m.Decoder().ReadNpcTrade(Fixture.Game) is { OtherModalOpen: true, SellButton: null, RegisterAllJunkButton: null }, "modal hides sell and junk controls");
        return Task.CompletedTask;
    }
}
