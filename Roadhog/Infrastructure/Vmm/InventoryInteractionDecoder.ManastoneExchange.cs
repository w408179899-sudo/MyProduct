using System.Net;
using System.Text.RegularExpressions;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class InventoryInteractionDecoder
{
    private static readonly Regex ManastoneHtmlButton = new(
        @"<button\b(?<attributes>[^>]*)>(?<text>.*?)</button\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));
    private static readonly Regex ManastoneHtmlAttribute = new(
        "(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*=\\s*(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)')",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    private static readonly Regex ManastoneHtmlTag = new(@"<[^>]*>", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));

    // DlgDialog, Game.dll 2026-09-09: 15DE40/15EF90/168630. Account 5 verified 2026-11-04.
    // Read HTML and match a unique rendered label; never inspect href offsets on arbitrary widget types.
    public ManastoneExchangeSnapshot ReadManastoneExchange(ulong gameBase)
    {
        _guards.Clear();
        var viewport = Guard(gameBase + 0xDACF00, 16);
        _width = checked((int)Number(BitConverter.ToDouble(viewport, 0)));
        _height = checked((int)Number(BitConverter.ToDouble(viewport, 8)));
        Require(_width is >= 320 and <= 16384 && _height is >= 200 and <= 16384, "Invalid manastone viewport.");
        ulong Root(int id) => GU(gameBase + 0xD63990 + (ulong)id * 8);
        bool Open(ulong address) => address != 0 && Visible(address);
        var dialog = Root(132);
        var open = Open(dialog);
        var modalOpen = false;
        for (var id = 336; id <= 370; id++) modalOpen |= Open(Root(id));
        if (!open)
        {
            VerifyGuards();
            return new(false, 0, 0, 0, 0, "", "", Array.Empty<ManastoneDialogEntry>(), null, null, modalOpen);
        }

        Require(Name(dialog) == "dlg_dialog", "Unexpected manastone dialog.");
        Guard(dialog + 8, 32);
        var npc = BitConverter.ToUInt32(Guard(dialog + 0x4E8, 4));
        var state = Guard(dialog + 0x518, 12);
        var type = BitConverter.ToInt32(state, 0);
        var pageId = BitConverter.ToInt32(state, 4);
        var quest = BitConverter.ToUInt32(state, 8);
        Require(npc != 0 && type is >= 0 and <= 9 && pageId >= 0 && quest <= 99999, "Invalid manastone dialog identity.");
        var pageName = AuctionWide(dialog + 0x528);
        var html = AuctionWide(dialog + 0x548);
        Require(pageName.Length is > 0 and <= 128 && html.Length > 0, "Incomplete manastone dialog page.");
        var nodes = Nodes(dialog);
        var htmlViews = nodes.Where(n => n.Name == "html_view").ToArray();
        Require(htmlViews.Length == 1 && htmlViews[0].Address == GU(dialog + 0x4E0), "Mismatched manastone HTML view.");
        var htmlView = htmlViews[0];
        Require(AuctionText(htmlView.Address) == html, "Manastone HTML changed during layout.");

        var bag = Root(27);
        Node? bagBounds = null;
        if (Open(bag))
        {
            Require(Name(bag) == "inventory_dialog", "Unexpected inventory covering manastone dialog.");
            var bounds = Guard(bag + 0x58, 32);
            double D(int offset) => Number(BitConverter.ToDouble(bounds, offset));
            Require(D(16) > 0 && D(24) > 0, "Invalid inventory covering geometry.");
            bagBounds = new(bag, "inventory_dialog", D(0), D(8), D(16), D(24), true);
        }
        static bool Inside(Node bounds, GameUiPoint point) =>
            point.X >= bounds.X && point.X < bounds.X + bounds.W && point.Y >= bounds.Y && point.Y < bounds.Y + bounds.H;
        GameUiPoint? ClickPoint(Node node, bool htmlEntry = false)
        {
            var point = node.Point(this);
            if (modalOpen || !nodes[0].Enabled || point == null || !Inside(nodes[0], point) ||
                (htmlEntry && (!htmlView.Enabled || !Inside(htmlView, point))) ||
                (bagBounds != null && Inside(bagBounds, point))) return null;
            return point;
        }
        var labels = nodes.Where(n => n.Address != dialog && n.Address != htmlView.Address)
            .Select(n => (Node: n, Text: ManastonePlainText(AuctionText(n.Address)))).Where(n => n.Text.Length > 0).ToArray();
        var matches = ManastoneHtmlButton.Matches(html);
        Require(matches.Count <= 64 && Regex.Matches(html, @"<button\b", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250)).Count == matches.Count,
            "Malformed manastone HTML buttons.");
        var entries = new List<ManastoneDialogEntry>();
        foreach (Match match in matches)
        {
            var attributes = ManastoneHtmlAttribute.Matches(match.Groups["attributes"].Value)
                .Select(a => (Name: a.Groups["name"].Value, Value: WebUtility.HtmlDecode(a.Groups["value"].Value))).ToArray();
            var hrefs = attributes.Where(a => a.Name.Equals("href", StringComparison.OrdinalIgnoreCase)).ToArray();
            // Reward headings use a button skin without an action.
            if (hrefs.Length == 0) continue;
            Require(hrefs.Length == 1 && hrefs[0].Value.Length is > 0 and <= 256, "Invalid manastone HTML action.");
            var text = ManastonePlainText(match.Groups["text"].Value);
            Require(text.Length is > 0 and <= 512, "Invalid manastone HTML label.");
            var matching = labels.Where(n => n.Text == text).ToArray();
            var disabled = attributes.Any(a => a.Name.Equals("disabled", StringComparison.OrdinalIgnoreCase) &&
                !a.Value.Equals("false", StringComparison.OrdinalIgnoreCase) && a.Value != "0");
            entries.Add(new(text, hrefs[0].Value, !disabled && matching.Length == 1 ? ClickPoint(matching[0].Node, true) : null));
        }
        Require(entries.Select(e => e.Href).Distinct(StringComparer.Ordinal).Count() == entries.Count, "Duplicate manastone HTML action.");
        GameUiPoint? Button(string name)
        {
            var found = nodes.Where(n => n.Name == name).ToArray();
            Require(found.Length <= 1, "Duplicate manastone dialog button.");
            return found.Length == 1 ? ClickPoint(found[0]) : null;
        }
        var accept = nodes.Where(n => n.Name == "accept").ToArray();
        Require(accept.Length <= 1, "Duplicate manastone accept button.");
        if (accept.Length == 1)
            entries.Add(new(ManastonePlainText(AuctionText(accept[0].Address)), "accept", ClickPoint(accept[0])));
        var confirm = Button("ok");
        GameUiPoint? ClosePoint(string name)
        {
            var found = nodes.Where(n => n.Name == name).ToArray();
            // DlgDialog has both a bottom Cancel and an empty title-bar Cancel.
            // Prefer the uniquely labelled control; ambiguous controls are non-actionable.
            var labelled = found.Where(n => labels.Any(l => l.Node.Address == n.Address && l.Text is "取消" or "关闭")).ToArray();
            var selected = labelled.Length == 1 ? labelled[0] : found.Length == 1 ? found[0] : null;
            return selected == null ? null : ClickPoint(selected);
        }
        var close = ClosePoint("close") ?? ClosePoint("cancel");
        VerifyGuards();
        return new(true, npc, quest, type, pageId, pageName, html, entries.AsReadOnly(), confirm, close, modalOpen);
    }

    private static string ManastonePlainText(string html) => WebUtility.HtmlDecode(ManastoneHtmlTag.Replace(html, "")).Trim();
}
