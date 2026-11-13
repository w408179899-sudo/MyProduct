using Roadhog.Application;
using Roadhog.Application.BagCleanup;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;

internal static class LegacyCleanupRemovalTests
{
    public static async Task OpenAsync()
    {
        var api = new FakeGameApi();
        var input = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var item = new InventoryUiItem(11, 21, 1, new(730, 420));
        var open = false;
        api.InventoryInteractionRead = () => new(open, false, false, new[] { item },
            0, 0, null, new(512, 384), false);
        var context = new AccountWorkerContext(new() { ScriptSettings = new ScriptSettings() },
            api, logger, new AccountRuntimeManager(logger), new(), CancellationToken.None);
        var discarder = new BagCleanupDiscarder(input);

        Require(!(await discarder.EnsureInventoryWindowOpenAsync(context)).Success,
            "unresponsive toggle must stop preparation");
        input.AfterPress = key => { if (key == "I") open = !open; };
        Require((await discarder.EnsureInventoryWindowOpenAsync(context)).Success && open,
            "open the inventory using the current UI channel");
        var presses = input.Keys.Count;
        Require((await discarder.EnsureInventoryWindowOpenAsync(context)).Success && input.Keys.Count == presses,
            "an already open inventory must not be toggled closed");
        Require(api.InventoryInteractionRead().Items.Single().Point == item.Point && input.MouseCommands.Count == 0,
            "preparation preserves the current geometry without moving or dragging the window");
        Require((await discarder.CloseInventoryWindowIfOpenAsync(context)).Success && !open,
            "shared discard still closes the inventory after completion");
        input.PressResult = _ => OperationResult.Fail("input unavailable");
        Require(!(await discarder.EnsureInventoryWindowOpenAsync(context)).Success,
            "failed input must not fall back to the removed coordinate flow");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
}
