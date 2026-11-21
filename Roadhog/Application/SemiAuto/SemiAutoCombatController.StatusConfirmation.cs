using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;
using SkillScheduling;

namespace Roadhog.Application.SemiAuto;

public sealed partial class SemiAutoCombatController
{
    internal sealed record StatusMaintenanceAttempt(
        StatusMaintenanceRuleConfig Rule, string Resource, uint SkillId, string SkillName, ushort PlayerId,
        HashSet<uint> BeforeIds, DateTimeOffset StartedAt, SkillConfirmationWindow Confirmation,
        bool IsChant, int PressCount, int IntervalMs)
    {
        public bool Awaiting { get; set; } = true;
        public long LastPoll { get; set; }
        public int Polls { get; set; }
        public HashSet<uint> RejectedIds { get; } = [];
    }

    private async Task<bool> ContinueCombatStatusMaintenanceAsync(AccountWorkerContext context,
        SemiAutoCombatState state, PlayerSnapshot? player, IReadOnlyCollection<StatusMaintenanceRuleConfig> rules)
    {
        if (state.CombatStatusAttempts.Count == 0) return false;
        foreach (var pending in state.CombatStatusAttempts.Values.ToArray())
        {
            if (player is null || player.IsDead || player.EntityId != pending.PlayerId || !rules.Contains(pending.Rule))
                state.CombatStatusAttempts.Remove(pending.Rule);
        }
        var attempt = state.CombatStatusAttempts.Values.FirstOrDefault(pending => pending.Awaiting);
        if (attempt is null) return false;
        if (attempt.Confirmation.Evaluate(confirmed: false, DateTimeOffset.Now) == SkillConfirmation.Pending &&
            attempt.Polls != 0 &&
            _timeProvider.GetElapsedTime(attempt.LastPoll) < MaintenanceConfirmPollInterval)
            return true;
        var result = await PollStatusMaintenanceAsync(context, state, attempt).ConfigureAwait(false);
        if (result == SkillConfirmation.TimedOut) ExpireStatusMaintenance(context, attempt);
        return result == SkillConfirmation.Pending;
    }

    private async Task<StatusMaintenanceCandidate?> RecheckStatusMaintenanceCandidateAsync(AccountWorkerContext context,
        SemiAutoCombatState state, StatusMaintenanceCandidate candidate, bool requireCooldownCalibration)
    {
        var rule = candidate.Rule;
        var skill = candidate.Skill;
        var skillId = ResolveStatusMaintenanceSkillId(rule, skill);
        var current = await ReadPlayerAbnormalStatusesAsync(context).ConfigureAwait(false);
        if (ResolveKnownStatusMaintenanceAbnormalId(rule, skillId, state, current.Entries).Found ||
            !state.ShouldPressMaintenanceKey(rule.Key, DateTimeOffset.Now, MaintenanceKeyRetryInterval, MaintenanceGlobalKeyInterval))
            return null;

        if (skill is not null)
        {
            var skills = await ReadMaintenanceSkillsAsync(context, new[] { (skill.SkillId, rule.SkillName) }).ConfigureAwait(false);
            skill = skills.FirstOrDefault(item => item.SkillId == skillId);
            if (skill is null ||
                (requireCooldownCalibration && !state.HasCooldownTickCalibration && skill.CooldownEndTime != 0) ||
                GetMaintenanceCooldownReadiness(skill, state) == SemiAutoSkillCooldownReadiness.CoolingDown)
                return null;
        }
        return candidate with { Skill = skill, BeforeStatuses = current };
    }

    private async Task<SkillConfirmation> PollStatusMaintenanceAsync(AccountWorkerContext context, SemiAutoCombatState state,
        StatusMaintenanceAttempt attempt)
    {
        var abnormal = await ReadPlayerAbnormalStatusesAsync(context).ConfigureAwait(false);
        context.StopToken.ThrowIfCancellationRequested();
        var confirmed = ObserveStatusMaintenance(context, state, attempt, abnormal);
        return attempt.Confirmation.Evaluate(confirmed, DateTimeOffset.Now);
    }

    private bool ObserveStatusMaintenance(AccountWorkerContext context, SemiAutoCombatState state,
        StatusMaintenanceAttempt attempt, PlayerAbnormalStatusSnapshot abnormal)
    {
        attempt.LastPoll = _timeProvider.GetTimestamp();
        attempt.Polls++;
        if (abnormal.EntityId != 0 && attempt.PlayerId != 0 && abnormal.EntityId != attempt.PlayerId) return false;
        var confirmed = ResolveConfirmedStatusMaintenanceAbnormalId(attempt.Rule, attempt.SkillId,
            state, attempt.BeforeIds, abnormal.Entries, attempt.RejectedIds);
        if (!confirmed.Found) return false;

        if (attempt.SkillId != 0)
        {
            state.RememberStatusMaintenanceAbnormalId(attempt.SkillId, confirmed.AbnormalId);
            if (attempt.IsChant) state.MarkChantStatusMaintenanceObserved(attempt.SkillId, DateTimeOffset.Now);
        }
        if (state.CombatStatusAttempts.TryGetValue(attempt.Rule, out var pending) && ReferenceEquals(pending, attempt))
            state.CombatStatusAttempts.Remove(attempt.Rule);
        var fields = StatusAttemptFields(context, attempt);
        fields["abnormalStatusId"] = confirmed.AbnormalId;
        fields["abnormalStatusSource"] = confirmed.Source;
        context.Logger.Info("semi_auto.maintenance.status_key_pressed", fields);
        if (attempt.Confirmation.Clock is not null)
            LogQueuedStatusSkill(context, "quickbar_skill.release.confirmed", attempt.SkillId, attempt.SkillName, attempt.Rule.Key);
        return true;
    }

    private static void LogQueuedStatusSkill(AccountWorkerContext context, string eventName, uint skillId, string skillName, string key) =>
        context.Logger.Info(eventName, new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["skillId"] = skillId,
            ["skillName"] = skillName,
            ["key"] = key,
            ["source"] = "status_maintenance"
        });

    private static void ExpireStatusMaintenance(AccountWorkerContext context, StatusMaintenanceAttempt attempt)
    {
        attempt.Awaiting = false;
        var fields = StatusAttemptFields(context, attempt);
        fields["configuredAbnormalStatusId"] = attempt.Rule.AbnormalStatusId;
        fields["rejectedCandidateAbnormalIds"] = FormatAbnormalIdList(attempt.RejectedIds);
        fields["rejectedCandidateCount"] = attempt.RejectedIds.Count;
        context.Logger.Warn("semi_auto.maintenance.status_unconfirmed", fields);
    }

    private static Dictionary<string, object?> StatusAttemptFields(AccountWorkerContext context,
        StatusMaintenanceAttempt attempt) => new()
    {
        ["account"] = context.Config.AccountName,
        ["resource"] = attempt.Resource,
        ["key"] = attempt.Rule.Key,
        ["skillId"] = attempt.SkillId,
        ["skillName"] = attempt.SkillName,
        ["oneShot"] = attempt.IsChant,
        ["chant"] = attempt.IsChant,
        ["pressCount"] = attempt.PressCount,
        ["pressIntervalMs"] = (long)attempt.IntervalMs,
        ["polls"] = attempt.Polls,
        ["confirmWindowMs"] = (long)attempt.Confirmation.Duration.TotalMilliseconds,
        ["confirmElapsedMs"] = (long)Math.Max(0, (DateTimeOffset.Now - attempt.StartedAt).TotalMilliseconds)
    };
}
