using Roadhog.Core.Api;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.Food;

/// <summary>Owns only inventory opened by one maintenance round, including failure cleanup.</summary>
public sealed class FoodInventorySession(IRoadhogSnapshotReader snapshots, IKeyboardInput input,
    CancellationToken stopToken) : IAsyncDisposable
{
    private ChannelTransitionSnapshot? openedIn;

    public void RecordOpened(ChannelTransitionSnapshot scene) => openedIn ??= scene;

    public async ValueTask DisposeAsync()
    {
        var initial = openedIn;
        openedIn = null;
        if (initial == null || stopToken.IsCancellationRequested) return;
        try
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var scene = (await snapshots.ReadChannelTransitionAsync().WaitAsync(cleanup.Token)).Value;
            var bag = (await snapshots.ReadInventoryInteractionAsync().WaitAsync(cleanup.Token)).Value;
            if (scene.IsReady && scene.Player!.CharacterName == initial.Player?.CharacterName &&
                scene.Channel!.MapId == initial.Channel!.MapId && scene.Channel.Index == initial.Channel.Index &&
                !scene.Player.IsDead && bag.IsOpen && !bag.OtherModalOpen && !bag.ShopIsOpen &&
                !bag.IsSelling && bag.DiscardDialog == null && bag.PendingDiscardInstanceId == 0)
                await input.PressKeyAsync("I", TimeSpan.FromMilliseconds(60), cleanup.Token);
        }
        catch (Exception) { /* Never mask the action result or block death recovery. */ }
    }
}
