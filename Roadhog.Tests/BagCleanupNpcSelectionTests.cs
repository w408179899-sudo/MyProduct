using Roadhog.Application;
using Roadhog.Application.BagCleanup;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;

internal static class BagCleanupNpcSelectionTests
{
    private const string Merchant = "cleanup merchant";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    public static Task SelectsAfterThirtyAttemptsAsync() => WithNoSelectionDelayAsync(async () =>
    {
        using var stop = new CancellationTokenSource(TestTimeout);
        var api = new FakeGameApi { TargetEntityId = 77, TargetName = "nearby character" };
        var input = new RecordingKeyboardInput();
        input.AfterPress = _ =>
        {
            if (input.Keys.Count == 31)
                api.TargetName = Merchant;
        };

        var result = await new BagCleanupNpcInteractor(input).SelectConfiguredNpcAsync(CreateContext(api, stop.Token), Merchant);

        Require(result.Success, "NPC selection should find the configured merchant after 30 misses");
        Require(input.Keys.SequenceEqual(Enumerable.Repeat("F8", 31)), "NPC selection should stop pressing F8 as soon as the configured merchant is found");
    });

    public static Task StopsDuringTargetReadAsync() => WithNoSelectionDelayAsync(async () =>
    {
        using var stop = new CancellationTokenSource();
        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingRead = new TaskCompletionSource<OperationResult<LockedTargetSnapshot>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new FakeGameApi
        {
            LockedTargetReadOverride = _ =>
            {
                readStarted.TrySetResult();
                return pendingRead.Task;
            }
        };
        var input = new RecordingKeyboardInput();
        var context = CreateContext(new PendingReaderFactory(api), stop.Token);
        var npc = new BagCleanupNpcInteractor(input);
        var selection = npc.SelectConfiguredNpcAsync(context, Merchant);
        try
        {
            await readStarted.Task.WaitAsync(TestTimeout);
            stop.Cancel();
            await ExpectStoppedAsync(selection, stop.Token);
            await ExpectStoppedAsync(npc.SelectConfiguredNpcAsync(context, Merchant), stop.Token);
            Require(input.Keys.SequenceEqual(new[] { "F8" }), "a stop must prevent further F8 presses during or before selection");
        }
        finally
        {
            stop.Cancel();
            pendingRead.TrySetResult(OperationResult<LockedTargetSnapshot>.Ok(
                new(77, 7700, 0, 2, Merchant, 1, 1, null, null, DateTimeOffset.UtcNow)));
        }
    });

    private static async Task ExpectStoppedAsync(Task<OperationResult> selection, CancellationToken stopToken)
    {
        try
        {
            await selection.WaitAsync(TestTimeout);
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == stopToken)
        {
            return;
        }

        throw new InvalidOperationException("NPC selection should observe the account stop token");
    }

    private static async Task WithNoSelectionDelayAsync(Func<Task> test)
    {
        const string variable = "ROADHOG_BAG_CLEANUP_NPC_SELECT_DELAY_MS";
        var previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, "0");
            await test();
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    private static AccountWorkerContext CreateContext(IRoadhogSnapshotReaderFactory readerFactory, CancellationToken stopToken)
    {
        var logger = new InMemoryRoadhogLogger();
        return new(new AccountConfig { AccountName = "npc selection" }, readerFactory, logger,
            new AccountRuntimeManager(logger), new AccountWorkerOptions(), stopToken);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class PendingReaderFactory(FakeGameApi api) : IRoadhogSnapshotReaderFactory
    {
        // Let the consumer stop independently while the provider still owns its in-flight read.
        public IRoadhogSnapshotReader Create(AccountConfig config, IRoadhogLogger logger, CancellationToken cancellationToken = default) =>
            api.Create(config, logger);
    }
}
