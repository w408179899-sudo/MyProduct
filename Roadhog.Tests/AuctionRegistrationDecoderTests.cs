using System.Text;
using Roadhog.Core.Model;

internal static partial class PersonalShopDecoderTests
{
    public static Task AuctionRegistrationDecodeAsync()
    {
        var m = new Fixture();
        const ulong auction = 0x40000000, tab = 0x41000000, list = 0x42000000, modal = 0x43000000,
            ok = 0x44000000, cancel = 0x45000000, body = 0x46000000, vtable = 0x47000000, getter = 0x48000000, textBuffer = 0x49000000;
        m.U(Fixture.Game + 0xD63990 + 152 * 8, auction); m.Widget(auction, "vendor_dialog", 0, 0, 700, 500, 0, 0);
        m.U(auction + 1256, tab); m.Widget(tab, "tab", 0, 0, 300, 30, 0, 0); m.I(tab + 0x2F8, 1);
        m.U(auction + 1384, list); m.Widget(list, "own_list", 0, 30, 600, 400, 0, 0); m.Children(auction, tab, list);
        m.I(auction + 1432, 1);
        m.U(Fixture.Game + 0xD63990 + 337 * 8, modal); m.Widget(modal, "msgbox", 350, 300, 310, 140, 0, 0);
        m.Widget(ok, "ok", 180, 110, 60, 20, 0, 0); m.Widget(cancel, "cancel", 240, 110, 60, 20, 0, 0); m.Children(modal, ok, cancel);
        m.U(modal + 0x578, ok); m.U(modal + 0x580, cancel);
        m.I(modal + 0x4D8, 257); m.I(modal + 0x4DC, 2106); m.I(modal + 0x4E0, 2107);
        m.I(modal + 0x4E8, 999); // Reused message-box scratch field is NOT the registration identity.
        m.I(auction + 1696, 123); m.U(auction + 1704, 608888); m.U(auction + 1712, 1); m.I(auction + 1720, 0);
        m.U(modal + 1376, body); m.U(body, vtable); m.U(vtable + 632, getter); m.Put(getter, new byte[] { 0x48, 0x8D, 0x81, 0, 3, 0, 0 });
        void Text(string value)
        {
            m.U(body + 0x300, textBuffer); m.Put(textBuffer, Encoding.Unicode.GetBytes(value));
            m.U(body + 0x310, (ulong)value.Length); m.U(body + 0x318, (ulong)value.Length);
        }
        const string message = "出售价格<font font_xml=\"v3_msgbox_money\">608,888 \uE000 </font>手续费<font font_xml=\"v3_msgbox_money\">14,063 \uE000 </font>";
        Text(message);
        var ui = m.Decoder().ReadAuction(Fixture.Game);
        Require(ui.RegistrationConfirmation is { InstanceId: 123, Quantity: 1, UnitPrice: 608888, UnitPriceMode: true, TotalPrice: 608888, Fee: 14063 }
            && ui.RegistrationConfirmation.ConfirmButton == new GameUiPoint(560, 420) && ui.RegistrationConfirmation.CancelButton != null && !ui.OtherModalOpen,
            "fee confirmation binds actual pending registration and money text, ignores stale discard ID");
        Require(ui.Buttons.Count == 0 && ui.ListingsScrollPoint == null, "fee modal blocks underlying controls");
        m.I(modal + 0x4D8, 265); Require(m.Decoder().ReadAuction(Fixture.Game).RegistrationConfirmation != null, "modal parent flag accepted");
        m.U(auction + 1704, 123); Reject(() => m.Decoder().ReadAuction(Fixture.Game), "price differs from displayed total"); m.U(auction + 1704, 608888);
        m.I(auction + 1696, 0); Reject(() => m.Decoder().ReadAuction(Fixture.Game), "zero pending identity rejected"); m.I(auction + 1696, 123);
        m.ShortAddress = auction + 1696; Reject(() => m.Decoder().ReadAuction(Fixture.Game), "short pending read rejected"); m.ShortAddress = null;
        Text("missing fee"); Reject(() => m.Decoder().ReadAuction(Fixture.Game), "missing amounts cannot publish zero fee"); Text(message);
        m.I(modal + 0x4DC, 2105); Require(m.Decoder().ReadAuction(Fixture.Game) is { OtherModalOpen: true, RegistrationConfirmation: null }, "foreign callback not accepted"); m.I(modal + 0x4DC, 2106);
        const ulong other = 0x4A000000;
        m.U(Fixture.Game + 0xD63990 + 338 * 8, other); m.Widget(other, "other", 0, 0, 200, 100, 0, 0);
        Require(m.Decoder().ReadAuction(Fixture.Game) is { OtherModalOpen: true, RegistrationConfirmation: null }, "second modal masks confirmation");
        return Task.CompletedTask;
    }
}
