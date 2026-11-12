using Roadhog.Application.EquipmentUpgrade;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

internal static partial class EquipmentUpgradeTests
{
    public static async Task DelayedActionConfirmationAsync()
    {
        foreach (var kind in Enum.GetValues<EquipmentUpgradeKind>())
        {
            var game = new Simulation(kind);
            var readUi = game.Api.EquipmentUiRead!;
            var lastHover = 0u;
            var hoverReads = 0;
            int? lastDialog = null;
            var dialogReads = 0;
            EquipmentUpgradeUi? visible = null;
            game.Api.EquipmentUiRead = () =>
            {
                var ui = readUi();
                if (ui.HoveredInstanceId != lastHover) { lastHover = ui.HoveredInstanceId; hoverReads = 0; }
                if (lastHover != 0 && hoverReads++ < 4) ui = ui with { HoveredInstanceId = 0 };
                if (ui.Dialog?.DialogId != lastDialog) { lastDialog = ui.Dialog?.DialogId; dialogReads = 0; }
                if (ui.Dialog != null)
                {
                    dialogReads++;
                    if (dialogReads <= 3) ui = ui with { Dialog = null };
                    else if (dialogReads <= 6) ui = ui with { Dialog = ui.Dialog with { ConfirmButton = null } };
                }
                visible = ui;
                return ui;
            };
            var press = game.Input.AfterMouseDown;
            game.Input.AfterMouseDown = button =>
            {
                Require(visible != null, "a click needs a confirmed UI state");
                if (visible!.Dialog != null)
                    Require(visible.Dialog.ConfirmButton == game.Api.InventoryUiCursor, "confirmation button must be ready before pressing");
                else Require(visible.HoveredInstanceId != 0, "item hover must be acknowledged before pressing");
                press?.Invoke(button);
            };
            var result = await game.Run();
            Require(result.Success && game.Confirmed == 4 && game.Used.Count == 2,
                kind + " must wait for delayed hover, dialogs and buttons: " + result.Error);
            Require(game.Input.MouseCommands.Count(c => c.StartsWith("down:")) == 8,
                "waiting must not duplicate material, equipment or confirmation clicks");
        }
    }

    public static async Task ActionConfirmationGuardsAsync()
    {
        foreach (var scenario in new[] { "hover_timeout", "dialog_timeout", "wrong_identity", "item_changed", "dialog_changed", "stop" })
        {
            var game = new Simulation(EquipmentUpgradeKind.Enchant);
            var readUi = game.Api.EquipmentUiRead!;
            using var stop = new CancellationTokenSource();
            var hoverReads = 0;
            var dialogReads = 0;
            game.Api.EquipmentUiRead = () =>
            {
                var ui = readUi();
                if (scenario is "hover_timeout" or "stop")
                {
                    if (ui.HoveredInstanceId != 0 && scenario == "stop" && ++hoverReads == 3) stop.Cancel();
                    return ui with { HoveredInstanceId = 0 };
                }
                if (scenario == "item_changed" && ui.HoveredInstanceId != 0)
                {
                    if (++hoverReads < 3) return ui with { HoveredInstanceId = 0 };
                    return ui with { Items = ui.Items.Select(i => i.InstanceId == ui.HoveredInstanceId
                        ? i with { Point = new(i.Point.X + 20, i.Point.Y) } : i).ToArray() };
                }
                if (ui.Dialog != null)
                {
                    if (scenario == "dialog_changed")
                    {
                        if (++dialogReads == 1) return ui;
                        return ui with { Dialog = ui.Dialog with { ConfirmButton = new(350, 350) } };
                    }
                    return ui with { Dialog = scenario == "dialog_timeout" ? null : ui.Dialog with { EquipmentId = 999 } };
                }
                return ui;
            };
            var logger = new InMemoryRoadhogLogger();
            var result = await new EquipmentUpgradeSequence(game.Input, logger,
                (ms, ct) => Task.Delay(ms == 6000 ? 1 : ms, ct), TimeSpan.FromMilliseconds(100))
                .RunAsync(game.Api.Create(new AccountConfig(), logger, stop.Token), "test", game.Settings,
                    EquipmentUpgradeKind.Enchant, null, stop.Token);
            Require(!result.Success && game.Confirmed == 0, scenario + " must stop before a confirmation click");
            var expectedClicks = scenario is "hover_timeout" or "item_changed" or "stop" ? 0 : 2;
            Require(game.Input.MouseCommands.Count(c => c.StartsWith("down:")) == expectedClicks,
                scenario + " must never replay submitted clicks");
            Require(game.Input.MouseCommands.TakeLast(2).SequenceEqual(new[] { "up:Left", "up:Right" }),
                "ending an unconfirmed action must release buttons");
            if (scenario.EndsWith("timeout")) Require(result.Error!.Contains("超时"), "timeout identifies the failed phase");
        }
    }
}
