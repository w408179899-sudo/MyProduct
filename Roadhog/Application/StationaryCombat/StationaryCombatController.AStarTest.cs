using Roadhog.Application.NavigationTests;
using Roadhog.Application.Workers;
using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Application.StationaryCombat;

public sealed partial class StationaryCombatController
{
    public Task<OperationResult> ExecuteAStarTestAsync(AccountWorkerContext context,
        AStarWalkTestRequest request, IProgress<string>? progress) =>
        new AStarWalkTest().RunAsync(request, new AStarTestMovement(this, context), progress, context.StopToken);

    private sealed class AStarTestMovement(StationaryCombatController owner, AccountWorkerContext context) : IAStarTestMovement
    {
        private readonly StationaryCombatState _state = new();
        public async Task<ChannelTransitionSnapshot> ReadSceneAsync(CancellationToken token) =>
            (await context.Snapshots.ReadChannelTransitionAsync().WaitAsync(token).ConfigureAwait(false)).Value;

        public async Task MoveStepAsync(PlayerSnapshot player, Vector3Snapshot target, double reachDistance, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            // One-metre navigation nodes need bounded input. Never leave W held while the
            // next scene snapshot or a near-target turn is being awaited.
            const int pulseMilliseconds = 25;
            LogActionThrottled(context, _state, "astar_test.move.step", "pulse", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["x"] = player.Position?.X, ["y"] = player.Position?.Y, ["z"] = player.Position?.Z,
                ["targetX"] = target.X, ["targetY"] = target.Y, ["targetZ"] = target.Z,
                ["reachDistance"] = reachDistance, ["pulseMs"] = pulseMilliseconds
            }, TimeSpan.FromSeconds(1));
            try
            {
                await owner.PathFollowStepAsync(context, _state, player, target, reachDistance).ConfigureAwait(false);
                if (_state.IsMovingForward)
                    await Task.Delay(pulseMilliseconds, token).ConfigureAwait(false);
            }
            finally
            {
                await StopAsync().ConfigureAwait(false);
            }
        }

        public async Task StopAsync()
        {
            await owner.StopMovementBestEffortAsync(context, _state).ConfigureAwait(false);
            StopPathFollowPoller(_state);
        }
    }
}
