using Roadhog.Infrastructure.GameSessions;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class InventoryInteractionDecoder
{
    // Game.dll 2026-09-09, SHA256 834300c2a85b6427de2d4a4be2931ff2ea59fd70f48f31754f5c2181753bd786.
    // The factory resource names are not assumed to be runtime widget names.
    internal GameExitUi ReadGameExit(ulong gameBase)
    {
        _guards.Clear();
        Viewport(gameBase);
        ulong Root(int slot) => GU(gameBase + 0xD63990 + (ulong)slot * 8);
        var menu = Root(30);
        var countdown = Root(215);
        var menuOpen = menu != 0 && Visible(menu);
        var countdownOpen = countdown != 0 && Visible(countdown);
        var blocked = false;
        for (var slot = 336; slot <= 370; slot++)
        {
            var modal = Root(slot);
            blocked |= modal != 0 && Visible(modal);
        }
        Roadhog.Core.Model.GameUiPoint? quit = null, cancel = null;
        var menuName = ""; var countdownName = "";
        if (menuOpen)
        {
            Require(GU(menu) == gameBase + 0x7FCE80, "Unexpected system menu class.");
            menuName = Name(menu);
            var nodes = Nodes(menu);
            var buttons = nodes.Where(node => node.Name == "quit_game").ToArray();
            Require(buttons.Length <= 1, "Ambiguous game exit control.");
            if (!blocked && !countdownOpen && (GU(menu + 0x28) & 2) != 0)
                quit = buttons.SingleOrDefault()?.Point(this);
        }
        if (countdownOpen)
        {
            Require(GU(countdown) == gameBase + 0x7E9748, "Unexpected exit countdown class.");
            countdownName = Name(countdown);
            var nodes = Nodes(countdown);
            // A title-bar close control and the body button may both be named cancel.
            // Both route through UIDialog::cancel and the same exit-state reset callback.
            if (!blocked && (GU(countdown + 0x28) & 2) != 0)
                cancel = nodes.Where(node => node.Name == "cancel" && node.Point(this) != null)
                    .OrderByDescending(node => node.W * node.H).ThenBy(node => node.Address)
                    .Select(node => node.Point(this)).FirstOrDefault();
        }
        VerifyGuards();
        return new(_width, _height, menuOpen, countdownOpen, blocked, menu, countdown,
            quit, cancel, menuName, countdownName);
    }
}
