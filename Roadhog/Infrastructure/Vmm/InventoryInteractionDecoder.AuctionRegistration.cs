using System.Text.RegularExpressions;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class InventoryInteractionDecoder
{
    // aion202609091125.json: 2E90F0 stores the pending registration; 2E1CC0 creates
    // the fee modal; 2E3E90 dispatches 2106/2107; 5F8E90 binds the displayed text.
    private AuctionRegistrationConfirmation ReadAuctionRegistration(ulong auction, ulong modal)
    {
        var pending = Guard(auction + 1696, 25);
        var instance = BitConverter.ToUInt32(pending, 0);
        var unitPrice = BitConverter.ToUInt64(pending, 8);
        var quantity = BitConverter.ToUInt64(pending, 16);
        Require(instance != 0 && unitPrice > 0 && quantity > 0 && pending[24] <= 1,
            "Unbound auction registration confirmation.");
        var amounts = AuctionRegistrationAmounts(AuctionText(GU(modal + 1376)));
        Require(amounts.Total == checked(unitPrice * quantity), "Auction confirmation total does not match pending registration.");
        var nodes = Nodes(modal);
        return new(instance, quantity, unitPrice, pending[24] == 1 || quantity == 1, amounts.Total, amounts.Fee,
            nodes.SingleOrDefault(n => n.Address == GU(modal + 0x578))?.Point(this),
            nodes.SingleOrDefault(n => n.Address == GU(modal + 0x580))?.Point(this));
    }

    internal static (ulong Total, ulong Fee) AuctionRegistrationAmounts(string text)
    {
        var amounts = Regex.Matches(text, "<font\\s+font_xml=\"v3_msgbox_money\">([^<]+)</font>")
            .Select(m => AuctionMoney(m.Groups[1].Value)).ToArray();
        Require(amounts.Length == 2 && amounts.All(a => a.HasValue), "Incomplete auction registration amounts.");
        return (amounts[0]!.Value, amounts[1]!.Value);
    }
}
