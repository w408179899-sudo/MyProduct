using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class InventoryInteractionDecoder
{
    /// <summary>Game.dll 2026-09-09; ordinary revive geometry verified on account 2 on 2026-09-20.</summary>
    public ReviveUiSnapshot ReadRevive(ulong gameBase)
    {
        _guards.Clear();
        Viewport(gameBase);
        ulong Root(int id) => GU(gameBase + 0xD63990 + (ulong)id * 8);
        var dialog = Root(209);
        var result = ReviveUiSnapshot.Closed;
        if (dialog != 0 && Visible(dialog))
        {
            Require(Name(dialog) == "resurrect_dialog", "Unexpected revive dialog.");
            // An incoming resurrection or another modal must not be clicked through.
            var other = Root(210);
            var blocked = other != 0 && Visible(other);
            for (var id = 336; id <= 365; id++)
            {
                var modal = Root(id);
                blocked |= modal != 0 && Visible(modal);
            }
            GameUiPoint? point = null;
            if (!blocked)
            {
                var button = GU(dialog + 0x4E0);
                if (button != 0)
                {
                    Require(Name(button) == "resurrect_ok", "Unexpected revive confirmation button.");
                    var flags = GU(button + 0x28);
                    var node = Nodes(dialog).SingleOrDefault(n => n.Address == button);
                    if ((flags & 3) == 3 && (GU(dialog + 0x28) & 2) != 0)
                        point = node?.Point(this);
                }
            }
            result = new(true, dialog, point);
        }
        VerifyGuards();
        return result;
    }
}
