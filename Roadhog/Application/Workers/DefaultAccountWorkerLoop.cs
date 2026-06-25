namespace Roadhog.Application.Workers;

public sealed class DefaultAccountWorkerLoop : IAccountWorkerLoop
{
    public async Task RunAsync(AccountWorkerContext context)
    {
        context.Logger.Info("worker.loop.enter", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["mode"] = context.Config.MainMode.ToString(),
            ["hardwareKey"] = context.Config.HardwareKey,
            ["vmmDevice"] = context.Config.VmmDeviceName
        });

        while (!context.StopToken.IsCancellationRequested)
        {
            context.RuntimeStates.MarkHeartbeat(context.Config.AccountName);

            if (context.Options.PollPlayerSnapshot)
            {
                var result = await context.GameApi.ReadPlayerAsync(context.StopToken).ConfigureAwait(false);
                if (!result.Success)
                {
                    context.Logger.Warn("worker.player_poll.failed", new Dictionary<string, object?>
                    {
                        ["account"] = context.Config.AccountName,
                        ["error"] = result.Error
                    });
                }
            }

            await Task.Delay(context.Options.TickInterval, context.StopToken).ConfigureAwait(false);
        }
    }
}
