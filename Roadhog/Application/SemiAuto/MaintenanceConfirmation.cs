using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

internal enum MaintenanceActionOutcome { Confirmed, Unconfirmed, TargetBlocked, InputFailed, PosturePending }

// Consuming a tick is separate from proving that the skill was released.
internal sealed record MaintenanceActionResult(MaintenanceActionOutcome Outcome, bool ConsumesTick,
    int Attempts, DateTimeOffset StartedAt, DateTimeOffset CompletedAt, SkillSnapshot? ConfirmedSkill = null);

internal readonly record struct MaintenanceAttemptPermission(bool Allowed, bool ConsumesTick)
{
    public static MaintenanceAttemptPermission Allow => new(true, false);
}

internal interface IMaintenanceConfirmationActions
{
    CancellationToken StopToken { get; }
    DateTimeOffset Now { get; }
    Task<MaintenanceAttemptPermission> BeforeAttemptAsync(int attempts, DateTimeOffset startedAt);
    Task<bool> PressAsync();
    Task<SkillSnapshot?> ConfirmAsync();
    Task DelayAsync(TimeSpan delay);
}

/// <summary>Only HP/MP and DP share this confirmation cadence. Status bursts, pet and trade protocols do not.</summary>
internal static class MaintenanceConfirmation
{
    public static async Task<MaintenanceActionResult> RunAsync(IMaintenanceConfirmationActions actions,
        TimeSpan window, TimeSpan pollInterval)
    {
        var startedAt = actions.Now;
        var deadline = startedAt + window;
        var attempts = 0;
        MaintenanceActionResult Result(MaintenanceActionOutcome outcome, bool consumes,
            SkillSnapshot? skill = null) => new(outcome, consumes, attempts, startedAt, actions.Now, skill);

        while (actions.Now <= deadline)
        {
            actions.StopToken.ThrowIfCancellationRequested();
            var permission = await actions.BeforeAttemptAsync(attempts, startedAt).ConfigureAwait(false);
            actions.StopToken.ThrowIfCancellationRequested();
            if (!permission.Allowed) return Result(MaintenanceActionOutcome.TargetBlocked, permission.ConsumesTick);

            attempts++;
            var pressed = await actions.PressAsync().ConfigureAwait(false);
            actions.StopToken.ThrowIfCancellationRequested();
            if (!pressed) return Result(MaintenanceActionOutcome.InputFailed, false);

            var confirmed = await actions.ConfirmAsync().ConfigureAwait(false);
            actions.StopToken.ThrowIfCancellationRequested();
            if (confirmed is not null) return Result(MaintenanceActionOutcome.Confirmed, true, confirmed);

            var remaining = deadline - actions.Now;
            if (remaining <= TimeSpan.Zero) break;
            await actions.DelayAsync(remaining < pollInterval ? remaining : pollInterval).ConfigureAwait(false);
        }
        actions.StopToken.ThrowIfCancellationRequested();
        return Result(MaintenanceActionOutcome.Unconfirmed, true);
    }
}
