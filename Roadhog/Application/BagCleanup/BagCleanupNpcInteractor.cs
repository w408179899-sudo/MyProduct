using Roadhog.Application.Workers;
using Roadhog.Core.Common;
using Roadhog.Core.Input;

namespace Roadhog.Application.BagCleanup;

public sealed class BagCleanupNpcInteractor
{
    private static readonly TimeSpan F8HoldDuration = TimeSpan.FromMilliseconds(35);
    private static readonly TimeSpan InteractHoldDuration = TimeSpan.FromMilliseconds(35);
    private static readonly TimeSpan DialogOpenDelay = TimeSpan.FromSeconds(1);

    private readonly IKeyboardInput _input;

    public BagCleanupNpcInteractor(IKeyboardInput input)
    {
        _input = input;
    }

    public async Task<OperationResult> SelectConfiguredNpcAsync(
        AccountWorkerContext context,
        string npcName)
    {
        if (string.IsNullOrWhiteSpace(npcName))
        {
            return OperationResult.Fail("Cleanup NPC name is empty.");
        }

        var maxAttempts = Math.Max(1, ReadIntFromEnv("ROADHOG_BAG_CLEANUP_NPC_SELECT_ATTEMPTS", 30));
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var press = await _input
                .PressKeyAsync("F8", F8HoldDuration, context.StopToken)
                .ConfigureAwait(false);
            if (!press.Success)
            {
                return OperationResult.Fail("F8 press failed: " + press.Error);
            }

            var selectDelayMs = ReadNpcSelectDelayMs();
            var selectDelay = TimeSpan.FromMilliseconds(selectDelayMs);
            await DelayAsync(selectDelay, context.StopToken).ConfigureAwait(false);
            var locked = await BagCleanupGameApi.ReadLockedTargetAsync(context).ConfigureAwait(false);
            var lockedName = locked.Success ? locked.Value?.Name ?? string.Empty : string.Empty;
            context.Logger.Info("bag_cleanup.npc.select.try", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["attempt"] = attempt,
                ["npcName"] = npcName,
                ["lockedName"] = lockedName,
                ["lockedEntityId"] = locked.Value?.TargetEntityId ?? 0,
                ["lockedServerObjectId"] = locked.Value?.ServerObjectId ?? 0,
                ["selectDelayMs"] = selectDelayMs
            });

            if (locked.Success &&
                locked.Value is { HasTarget: true } target &&
                string.Equals(target.Name.Trim(), npcName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                context.Logger.Info("bag_cleanup.npc.select.ok", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["npcName"] = npcName,
                    ["attempt"] = attempt,
                    ["targetEntityId"] = target.TargetEntityId,
                    ["targetServerObjectId"] = target.ServerObjectId
                });
                return OperationResult.Ok();
            }
        }

        return OperationResult.Fail("Cleanup NPC was not selected after F8 attempts: " + npcName);
    }

    public async Task<OperationResult> OpenDialogAsync(AccountWorkerContext context)
    {
        var press = await _input
            .PressKeyAsync("C", InteractHoldDuration, context.StopToken)
            .ConfigureAwait(false);
        if (!press.Success)
        {
            return OperationResult.Fail("NPC interact key press failed: " + press.Error);
        }

        await DelayAsync(DialogOpenDelay, context.StopToken).ConfigureAwait(false);
        context.Logger.Info("bag_cleanup.npc.dialog.open", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName
        });
        return OperationResult.Ok();
    }

    private static Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, cancellationToken);
    }

    private static int ReadIntFromEnv(string name, int fallback)
    {
        return int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;
    }

    private static int ReadNpcSelectDelayMs()
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_DELAY_MS"), out var fixedDelay))
        {
            return Math.Max(0, fixedDelay);
        }

        var min = Math.Max(0, ReadIntFromEnv("ROADHOG_BAG_CLEANUP_NPC_SELECT_DELAY_MIN_MS", 1000));
        var max = Math.Max(0, ReadIntFromEnv("ROADHOG_BAG_CLEANUP_NPC_SELECT_DELAY_MAX_MS", 2000));
        if (max < min)
        {
            max = min;
        }

        return min == max ? min : Random.Shared.Next(min, max + 1);
    }
}
