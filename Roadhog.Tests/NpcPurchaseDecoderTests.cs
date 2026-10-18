using System.Text;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;

internal static partial class PersonalShopDecoderTests
{
    public static Task NpcPurchaseAsync()
    {
        var m = new Fixture();
        const ulong trade = 0x30000000, container = 0x31000000, buy = 0x32000000, record = 0x33000000,
            basket = 0x34000000, inventory = 0x35000000, modal = 0x36000000, all = 0x37000000, confirm = 0x38000000;
        m.U(Fixture.Game + 0xD63990 + 134 * 8, trade);
        m.Widget(trade, "trade_dialog", 20, 30, 320, 450, 0, 0);
        m.Widget(container, "buy_item_container", 0, 0, 280, 320, 0, 0);
        m.Widget(Fixture.List, "item_list", 10, 40, 250, 200, 0, 0);
        m.Widget(basket, "item_basket", 10, 260, 250, 80, 0, 0);
        m.Widget(buy, "ok", 200, 350, 50, 20, 0, 0);
        m.Children(trade, container, buy); m.Children(container, Fixture.List, basket);
        m.I(trade + 1252, 0); m.I(trade + 1240, 42);
        m.U(trade + 1264, Fixture.List); m.U(trade + 1320, basket);
        m.I(Fixture.List + 0x2E0, 1); m.I(Fixture.List + 0x2E8, 5); m.I(Fixture.List + 0x3F0, 0);
        m.U(Fixture.Game + 0xD4B010, inventory); m.U(inventory + 2368, Fixture.Head);
        m.U(Fixture.Node + 40, record); m.I(record + 8, 123); m.I(record + 12, 163000003);
        m.I(Fixture.Item + 168, 163000003); m.U(record + 128, 10); m.I(record + 96, 5);
        m.Put(record + 24, Encoding.Unicode.GetBytes("弓箭")); m.U(record + 40, 2); m.U(record + 48, 7);
        m.I(Fixture.Game + 0xD6F238, 100);
        var state = m.Decoder().ReadNpcTrade(Fixture.Game);
        Require(state.IsBuying && state.Purchase!.Items.Single() is { TemplateId: 163000003, Name: "弓箭", ItemType: 5, UnitPrice: 10 } &&
            state.Purchase.HoveredItemId == 123 && state.Purchase.BuyButton == new GameUiPoint(245, 390), "NPC stock name identity hover effective price and purchase button");
        m.I(Fixture.Game + 0xD71C1C, 1); m.I(Fixture.Game + 0xD6F23C, 90); m.I(Fixture.Game + 0xD6F240, 110);
        Require(m.Decoder().ReadNpcTrade(Fixture.Game).Purchase!.Items.Single().UnitPrice == 9, "NPC price applies integer rounding at every tax step");
        m.Widget(modal, "item_price_count_dialog", 300, 200, 250, 220, 0, 0);
        m.Widget(all, "all", 10, 120, 50, 20, 0, 0); m.Widget(confirm, "ok", 100, 160, 50, 20, 0, 0);
        m.Children(modal, all, confirm); m.U(trade + 2248, modal); m.I(trade + 2236, 123);
        m.U(modal + 1504, 1); m.U(modal + 1520, 10000); m.U(Fixture.Game + 0xD63990 + 336 * 8, modal);
        var edit = m.Decoder().ReadNpcTrade(Fixture.Game);
        Require(!edit.OtherModalOpen && edit.Purchase!.BuyButton == null && edit.Purchase.QuantityDialog is
            { ItemId: 123, Quantity: 1, Maximum: 10000, AllButton: not null, ConfirmButton: not null }, "bound quantity modal exposes ALL and confirm, not final purchase");
        foreach (var address in new[] { record + 24, record + 128, Fixture.Item + 160, modal + 1520 })
        {
            m.ShortAddress = address;
            Reject(() => m.Decoder().ReadNpcTrade(Fixture.Game), "short purchase data cannot publish");
        }
        m.ShortAddress = null;
        m.I(trade + 2236, 999); Reject(() => m.Decoder().ReadNpcTrade(Fixture.Game), "quantity dialog belongs to another item"); m.I(trade + 2236, 123);
        m.U(modal + 1504, 10000);
        Require(m.Decoder().ReadNpcTrade(Fixture.Game).Purchase!.QuantityDialog!.Quantity == 10000, "ALL quantity publishes immediately");
        var reads = 0;
        var changing = new InventoryInteractionDecoder((a, n) => a == record + 12 && ++reads > 1 ? BitConverter.GetBytes(123u) : m.Read(a, n));
        Reject(() => changing.ReadNpcTrade(Fixture.Game), "changed item identity rejects mixed UI publication");
        m.U(trade + 0x28, 0); m.U(modal + 0x28, 0);
        Require(m.Decoder().ReadNpcTrade(Fixture.Game) is { IsBuying: false, Purchase: null }, "closed NPC purchase clears prior stock");
        return Task.CompletedTask;
    }
}
