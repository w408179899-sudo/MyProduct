using Smart.Contracts;
using UpgradeFixture.Domain;
namespace UpgradeFixture.Application;

public sealed class FixtureModule(SnapshotChannel<CounterValue, NoPartition> channel) : IAccountModule
{
    private bool _submitted;
    public string Id => "upgrade-fixture";
    public int Priority => 1;
    public IReadOnlyList<string> RequiredChannels => [channel.Id];
    public int Initialized { get; private set; }
    public int Stopped { get; private set; }
    public long? Observed { get; private set; }
    public TaskCompletionSource ActionCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ValueTask InitializeAsync(ModuleContext context, CancellationToken cancellationToken)
    { Initialized++; return ValueTask.CompletedTask; }
    public ValueTask StopAsync(ModuleStopReason reason, CancellationToken cancellationToken)
    { Stopped++; return ValueTask.CompletedTask; }
    public async ValueTask<ModuleResult> TickAsync(TickContext context, CancellationToken cancellationToken)
    {
        var official = await context.Snapshots.ReadAsync(channel, NoPartition.Value, cancellationToken);
        Observed = official.Value.Value;
        if (context.LastAction?.State == ActionState.Succeeded) ActionCompleted.TrySetResult();
        if (_submitted) return new(TimeSpan.FromMilliseconds(10));
        _submitted = true;
        return new(TimeSpan.FromMilliseconds(10), new("fixture-action", InputResource.Keyboard,
            [InputCommand.PressDown(4)], TimeSpan.FromSeconds(1), InputRetention.UntilOwnerChanges, Id));
    }
}
