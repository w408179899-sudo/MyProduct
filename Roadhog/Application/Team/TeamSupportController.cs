using Roadhog.Application.Workers;
using Roadhog.Application.StationaryCombat;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.Team;

public sealed class TeamSupportController
{
    private static readonly TimeSpan TeamSupportTickDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan WarningLogInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SelectConfirmDelay = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan TeamBuffRetryInterval = TimeSpan.FromSeconds(2);
    private const string LeaderAssistKey = "C";
    private const string AssistTargetKey = "Oem3";
    private const string LifeBlessingName = "\u751F\u547D\u7684\u795D\u798F";
    private const string ProtectionBlessingName = "\u4FDD\u62A4\u795D\u798F";
    private const string ProtectionBlessingNameWithParticle = "\u4FDD\u62A4\u7684\u795D\u798F";

    private readonly IKeyboardInput _keyboard;
    private TeamAbnormalStatusCatalog? _abnormalStatusCatalog;

    public TeamSupportController(
        IKeyboardInput keyboard,
        TeamAbnormalStatusCatalog? abnormalStatusCatalog = null)
    {
        _keyboard = keyboard;
        _abnormalStatusCatalog = abnormalStatusCatalog;
    }

    public async Task<TeamSupportTickResult> TickAsync(
        AccountWorkerContext context,
        TeamSupportState state,
        StationaryCombatState? combatState = null)
    {
        var team = context.Config.ScriptSettings?.Team ?? new TeamScriptSettings();
        var support = team.Support ?? new TeamSupportScriptSettings();
        if (team.Role != TeamRole.Support || !support.Enabled)
        {
            return TeamSupportTickResult.Continue(context.Options.TickInterval);
        }

        LogCatalogWarningIfNeeded(context.Logger, state, support);

        var readContext = CreateReadContext(context);
        var monitor = new TeamMonitor(context.GameApi, context.Logger);
        var snapshotResult = await monitor.ReadSnapshotAsync(readContext, context.StopToken).ConfigureAwait(false);
        if (!snapshotResult.Success || snapshotResult.Value is null)
        {
            if (ShouldLog(state.LastSnapshotWarningAt))
            {
                state.LastSnapshotWarningAt = DateTimeOffset.Now;
                context.Logger.Warn("team_support.snapshot.failed", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["error"] = snapshotResult.Error
                });
            }

            return TeamSupportTickResult.SkipNormalWork(TeamSupportTickDelay);
        }

        var snapshot = snapshotResult.Value;
        var action = await SelectActionAsync(context, state, support, snapshot).ConfigureAwait(false);
        if (action is not null)
        {
            await ExecuteActionAsync(context, state, action).ConfigureAwait(false);
            return TeamSupportTickResult.SkipNormalWork(TeamSupportTickDelay);
        }

        var leader = snapshot.LeaderMember;
        var leaderDeadStop =
            support.StopWhenLeaderDead &&
            leader?.PartyMember.IsDead == true;
        if (leaderDeadStop)
        {
            await SelectLeaderAndAssistAsync(context, state, snapshot).ConfigureAwait(false);
            return TeamSupportTickResult.SkipNormalWork(TeamSupportTickDelay);
        }

        var groupDistanceMeters = team.GroupDistanceMeters;
        var leaderInGroupRange = TeamLeaderRuntimePolicy.IsLeaderInGroupRange(
            leader,
            groupDistanceMeters);
        if (!leaderInGroupRange)
        {
            LogFollowDecision(
                context,
                state,
                "leader_out_of_range",
                leader,
                groupDistanceMeters,
                support.JoinCombat,
                combatState);
            return support.JoinCombat
                ? TeamSupportTickResult.Continue(TeamSupportTickDelay)
                : TeamSupportTickResult.SkipNormalWork(TeamSupportTickDelay);
        }

        if (TeamLeaderRuntimePolicy.HasActiveCombatTarget(combatState))
        {
            LogFollowDecision(
                context,
                state,
                "active_combat_target",
                leader,
                groupDistanceMeters,
                support.JoinCombat,
                combatState);
            return TeamSupportTickResult.Continue(TeamSupportTickDelay);
        }

        if (!support.JoinCombat)
        {
            await SelectLeaderAndAssistAsync(context, state, snapshot).ConfigureAwait(false);
            return TeamSupportTickResult.SkipNormalWork(TeamSupportTickDelay);
        }

        return await SelectLeaderTargetForCombatAsync(context, state, leader).ConfigureAwait(false);
    }

    private async Task<TeamSupportAction?> SelectActionAsync(
        AccountWorkerContext context,
        TeamSupportState state,
        TeamSupportScriptSettings support,
        TeamSnapshot snapshot)
    {
        var members = GetMaintenanceMembers(snapshot);
        var statusMembers = members
            .Select(member => new TeamSupportStatusCandidate(
                member,
                CountMentalCleanseCandidates(member),
                CountPhysicalCleanseCandidates(member)))
            .ToArray();

        if (!string.IsNullOrWhiteSpace(support.GroupCleanseKey) &&
            statusMembers.Count(member => member.NeedsAnyCleanse) >= 2)
        {
            return TeamSupportAction.GroupCleanse(support.GroupCleanseKey);
        }

        if (support.MentalCleanseEnabled && !string.IsNullOrWhiteSpace(support.MentalCleanseKey))
        {
            var mentalCandidate = statusMembers.FirstOrDefault(member => member.MentalCleanseCount > 0);
            if (mentalCandidate is not null)
            {
                return TeamSupportAction.Targeted(
                    TeamSupportActionKind.MentalCleanse,
                    mentalCandidate.Member,
                    support.MentalCleanseKey,
                    mentalCandidate.MentalCleanseCount);
            }
        }

        if (support.PhysicalCleanseEnabled && !string.IsNullOrWhiteSpace(support.PhysicalCleanseKey))
        {
            var physicalCandidate = statusMembers.FirstOrDefault(member => member.PhysicalCleanseCount > 0);
            if (physicalCandidate is not null)
            {
                return TeamSupportAction.Targeted(
                    TeamSupportActionKind.PhysicalCleanse,
                    physicalCandidate.Member,
                    support.PhysicalCleanseKey,
                    physicalCandidate.PhysicalCleanseCount);
            }
        }

        var healRules = (support.HealSkillRules ?? new List<TeamHealSkillRuleConfig>())
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Key))
            .ToArray();
        var teamBuffRules = GetTeamBuffRules(context.Config.ScriptSettings?.Maintenance?.StatusMaintenanceRules);
        if (healRules.Length == 0 && teamBuffRules.Count == 0)
        {
            return null;
        }

        var timing = await ResolveCurrentRunTimingAsync(context, snapshot).ConfigureAwait(false);
        var allowedRules = healRules
            .Where(rule => IsRuleAllowed(rule, timing))
            .ToArray();

        if (allowedRules.Length > 0)
        {
            var healCandidate = members
                .Where(member => member.PartyMember.HasKnownHealth && member.PartyMember.IsAlive)
                .Select(member => new
                {
                    Member = member,
                    HpPercent = member.PartyMember.HpPercent,
                    Rule = SelectHealRule(member.PartyMember.HpPercent, allowedRules)
                })
                .Where(candidate => candidate.Rule is not null)
                .OrderBy(candidate => candidate.HpPercent)
                .ThenBy(candidate => candidate.Member.FunctionKeyNumber)
                .FirstOrDefault();

            if (healCandidate?.Rule is not null)
            {
                return healCandidate.Rule.TargetType == TeamHealSkillTargetType.Group
                    ? TeamSupportAction.GroupHeal(healCandidate.Rule, healCandidate.Member)
                    : TeamSupportAction.TargetedHeal(healCandidate.Member, healCandidate.Rule);
            }
        }

        var allowedBuffRules = teamBuffRules
            .Where(rule => IsRuleAllowed(rule, timing))
            .ToArray();
        return SelectTeamBuffAction(members, allowedBuffRules, state);
    }

    private async Task<bool> ExecuteActionAsync(
        AccountWorkerContext context,
        TeamSupportState state,
        TeamSupportAction action)
    {
        if (action.Target is not null &&
            !await EnsureMemberBodySelectedAsync(context, state, action.Target).ConfigureAwait(false))
        {
            context.Logger.Warn("team_support.target_select.failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["member"] = action.Target.Name,
                ["memberServerObjectId"] = action.Target.ServerObjectId,
                ["functionKey"] = FormatFunctionKey(action.Target)
            });
            return false;
        }

        var key = NormalizeKey(action.Key);
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var result = await _keyboard
            .PressKeyAsync(key, ResolveKeyHold(context), context.StopToken)
            .ConfigureAwait(false);
        LogActionPress(context, action, key, result);
        if (!result.Success)
        {
            state.LastInputWarningAt = DateTimeOffset.Now;
            return false;
        }

        state.LastActionAt = DateTimeOffset.Now;
        if (action.Kind == TeamSupportActionKind.TeamBuff &&
            action.Target is not null &&
            action.StatusRule is not null)
        {
            state.RememberTeamBuffPress(
                action.Target.ServerObjectId,
                ResolveTeamBuffAbnormalStatusId(action.StatusRule),
                key,
                DateTimeOffset.Now);
        }

        return true;
    }

    private async Task<bool> SelectLeaderAndAssistAsync(
        AccountWorkerContext context,
        TeamSupportState state,
        TeamSnapshot snapshot)
    {
        var leader = snapshot.LeaderMember;
        return await SelectLeaderAndAssistAsync(context, state, leader).ConfigureAwait(false);
    }

    private async Task<bool> SelectLeaderAndAssistAsync(
        AccountWorkerContext context,
        TeamSupportState state,
        TeamMemberSnapshot? leader)
    {
        if (leader is null || leader.IsSelf)
        {
            return false;
        }

        if (!await EnsureMemberBodySelectedAsync(context, state, leader).ConfigureAwait(false))
        {
            return false;
        }

        var result = await _keyboard
            .PressKeyAsync(LeaderAssistKey, ResolveKeyHold(context), context.StopToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            state.LastInputWarningAt = DateTimeOffset.Now;
            context.Logger.Warn("team_support.leader_assist_key.failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["key"] = LeaderAssistKey,
                ["error"] = result.Error
            });
            return false;
        }

        state.LastActionAt = DateTimeOffset.Now;
        context.Logger.Info("team_support.leader_assist.pressed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["leader"] = leader.Name,
            ["leaderServerObjectId"] = leader.ServerObjectId,
            ["functionKey"] = FormatFunctionKey(leader),
            ["key"] = LeaderAssistKey
        });
        return true;
    }

    private async Task<TeamSupportTickResult> SelectLeaderTargetForCombatAsync(
        AccountWorkerContext context,
        TeamSupportState state,
        TeamMemberSnapshot? leader)
    {
        if (leader is null || leader.IsSelf)
        {
            return TeamSupportTickResult.SkipNormalWork(TeamSupportTickDelay);
        }

        if (!await SelectLeaderAndAssistAsync(context, state, leader).ConfigureAwait(false))
        {
            return TeamSupportTickResult.SkipNormalWork(TeamSupportTickDelay);
        }

        var leaderTargetServerObjectId = leader.PartyMember.LiveTargetServerObjectId;
        if (!leader.IsScreenVisible || leaderTargetServerObjectId == 0)
        {
            return TeamSupportTickResult.SkipNormalWork(TeamSupportTickDelay);
        }

        var assistResult = await _keyboard
            .PressKeyAsync(AssistTargetKey, ResolveKeyHold(context), context.StopToken)
            .ConfigureAwait(false);
        if (!assistResult.Success)
        {
            if (ShouldLog(state.LastInputWarningAt))
            {
                state.LastInputWarningAt = DateTimeOffset.Now;
                context.Logger.Warn("team_support.assist_target_key.failed", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["key"] = AssistTargetKey,
                    ["error"] = assistResult.Error
                });
            }

            return TeamSupportTickResult.SkipNormalWork(TeamSupportTickDelay);
        }

        state.LastActionAt = DateTimeOffset.Now;
        var lockedTargetResult = await ReadLockedTargetAsync(context).ConfigureAwait(false);
        if (!TeamLeaderTargetValidator.IsLeaderAttackTarget(
                lockedTargetResult,
                leader,
                leaderTargetServerObjectId,
                out var rejectReason))
        {
            LogTargetRejected(context, state, lockedTargetResult.Value, leader, leaderTargetServerObjectId, rejectReason);
            await SelectLeaderAndAssistAsync(context, state, leader).ConfigureAwait(false);
            return TeamSupportTickResult.SkipNormalWork(TeamSupportTickDelay);
        }

        context.Logger.Info("team_support.target.accepted", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["leader"] = leader.Name,
            ["leaderServerObjectId"] = leader.ServerObjectId,
            ["leaderTargetServerObjectId"] = leaderTargetServerObjectId,
            ["targetName"] = lockedTargetResult.Value?.Name,
            ["targetServerObjectId"] = lockedTargetResult.Value?.ServerObjectId,
            ["targetTargetServerObjectId"] = lockedTargetResult.Value?.TargetServerObjectId
        });
        return TeamSupportTickResult.Continue(TeamSupportTickDelay);
    }

    private TeamAbnormalStatusCatalog AbnormalStatusCatalog
    {
        get
        {
            _abnormalStatusCatalog ??= TeamAbnormalStatusCatalog.Load();
            return _abnormalStatusCatalog;
        }
    }

    private async Task<bool> EnsureMemberBodySelectedAsync(
        AccountWorkerContext context,
        TeamSupportState state,
        TeamMemberSnapshot member)
    {
        if (member.ServerObjectId == 0)
        {
            return false;
        }

        var current = await ReadLockedTargetAsync(context).ConfigureAwait(false);
        if (IsSelectedMemberBody(current, member))
        {
            context.Logger.Info("team_support.target_selected", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["member"] = member.Name,
                ["memberServerObjectId"] = member.ServerObjectId,
                ["functionKey"] = FormatFunctionKey(member),
                ["alreadySelected"] = true
            });
            return true;
        }

        var functionKey = FormatFunctionKey(member);
        if (string.IsNullOrWhiteSpace(functionKey))
        {
            return false;
        }

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var pressResult = await _keyboard
                .PressKeyAsync(functionKey, ResolveKeyHold(context), context.StopToken)
                .ConfigureAwait(false);
            if (!pressResult.Success)
            {
                if (ShouldLog(state.LastInputWarningAt))
                {
                    state.LastInputWarningAt = DateTimeOffset.Now;
                    context.Logger.Warn("team_support.select_key.failed", new Dictionary<string, object?>
                    {
                        ["account"] = context.Config.AccountName,
                        ["member"] = member.Name,
                        ["memberServerObjectId"] = member.ServerObjectId,
                        ["functionKey"] = functionKey,
                        ["error"] = pressResult.Error
                    });
                }

                return false;
            }

            await Task.Delay(SelectConfirmDelay, context.StopToken).ConfigureAwait(false);
            current = await ReadLockedTargetAsync(context).ConfigureAwait(false);
            if (IsSelectedMemberBody(current, member))
            {
                context.Logger.Info("team_support.target_selected", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["member"] = member.Name,
                    ["memberServerObjectId"] = member.ServerObjectId,
                    ["functionKey"] = functionKey,
                    ["alreadySelected"] = false,
                    ["attempt"] = attempt
                });
                return true;
            }
        }

        return false;
    }

    private async Task<MaintenanceRuleRunTiming> ResolveCurrentRunTimingAsync(
        AccountWorkerContext context,
        TeamSnapshot snapshot)
    {
        var ids = new HashSet<uint>();
        foreach (var member in snapshot.Members)
        {
            if (member.ServerObjectId != 0)
            {
                ids.Add(member.ServerObjectId);
            }

            var pet = member.SummonedPet?.Pet;
            if (pet?.IsSummoned == true && pet.ServerObjectId != 0)
            {
                ids.Add(pet.ServerObjectId);
            }
        }

        var worldResult = await ReadWorldObjectsAsync(context).ConfigureAwait(false);
        if (worldResult.Success && worldResult.Value is not null)
        {
            foreach (var worldObject in worldResult.Value)
            {
                if (worldObject.IsAlive &&
                    worldObject.TargetServerObjectId != 0 &&
                    ids.Contains(worldObject.TargetServerObjectId))
                {
                    return MaintenanceRuleRunTiming.InCombat;
                }
            }
        }

        var lockedResult = await ReadLockedTargetAsync(context).ConfigureAwait(false);
        if (lockedResult.Success && lockedResult.Value?.IsMonsterAlive == true)
        {
            return MaintenanceRuleRunTiming.InCombat;
        }

        return MaintenanceRuleRunTiming.AfterCombat;
    }

    private int CountMentalCleanseCandidates(TeamMemberSnapshot member)
    {
        var count = 0;
        foreach (var entry in member.PartyMember.AbnormalStatuses)
        {
            if (AbnormalStatusCatalog.IsMentalCleanseCandidate(entry))
            {
                count++;
            }
        }

        return count;
    }

    private int CountPhysicalCleanseCandidates(TeamMemberSnapshot member)
    {
        var count = 0;
        foreach (var entry in member.PartyMember.AbnormalStatuses)
        {
            if (AbnormalStatusCatalog.IsPhysicalCleanseCandidate(entry))
            {
                count++;
            }
        }

        return count;
    }

    private static IReadOnlyList<TeamMemberSnapshot> GetMaintenanceMembers(TeamSnapshot snapshot)
    {
        return snapshot.Members
            .Where(member => member.ServerObjectId != 0 && member.PartyMember.IsAlive)
            .OrderBy(member => member.FunctionKeyNumber)
            .ToArray();
    }

    private static TeamHealSkillRuleConfig? SelectHealRule(
        double hpPercent,
        IReadOnlyList<TeamHealSkillRuleConfig> rules)
    {
        return rules
            .Where(rule => hpPercent < Math.Clamp(rule.BelowPercent, 1, 100))
            .OrderBy(rule => Math.Clamp(rule.BelowPercent, 1, 100))
            .ThenBy(rule => rule.TargetType == TeamHealSkillTargetType.Single ? 0 : 1)
            .FirstOrDefault();
    }

    private static TeamSupportAction? SelectTeamBuffAction(
        IReadOnlyList<TeamMemberSnapshot> members,
        IReadOnlyList<StatusMaintenanceRuleConfig> rules,
        TeamSupportState state)
    {
        if (rules.Count == 0)
        {
            return null;
        }

        var now = DateTimeOffset.Now;
        foreach (var member in members
                     .OrderBy(member => member.IsSelf ? 1 : 0)
                     .ThenBy(member => member.FunctionKeyNumber))
        {
            foreach (var rule in rules)
            {
                var abnormalStatusId = ResolveTeamBuffAbnormalStatusId(rule);
                if (abnormalStatusId == 0 ||
                    HasTeamBuffStatus(member, abnormalStatusId) ||
                    !state.ShouldPressTeamBuff(
                        member.ServerObjectId,
                        abnormalStatusId,
                        rule.Key,
                        now,
                        TeamBuffRetryInterval))
                {
                    continue;
                }

                return TeamSupportAction.TargetedBuff(member, rule);
            }
        }

        return null;
    }

    private static bool IsRuleAllowed(
        TeamHealSkillRuleConfig rule,
        MaintenanceRuleRunTiming timing)
    {
        return rule.RunTiming == MaintenanceRuleRunTiming.Always ||
               rule.RunTiming == timing;
    }

    private static bool IsRuleAllowed(
        StatusMaintenanceRuleConfig rule,
        MaintenanceRuleRunTiming timing)
    {
        return rule.RunTiming == MaintenanceRuleRunTiming.Always ||
               rule.RunTiming == timing;
    }

    private static IReadOnlyList<StatusMaintenanceRuleConfig> GetTeamBuffRules(
        IEnumerable<StatusMaintenanceRuleConfig>? rules)
    {
        return (rules ?? Array.Empty<StatusMaintenanceRuleConfig>())
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Key) &&
                           ResolveTeamBuffAbnormalStatusId(rule) != 0 &&
                           IsWhitelistedTeamBuffRule(rule))
            .ToArray();
    }

    private static bool IsWhitelistedTeamBuffRule(StatusMaintenanceRuleConfig rule)
    {
        var skillName = NormalizeStatusRuleName(rule.SkillName);
        return skillName.Contains(LifeBlessingName, StringComparison.Ordinal) ||
               skillName.Contains(ProtectionBlessingName, StringComparison.Ordinal) ||
               skillName.Contains(ProtectionBlessingNameWithParticle, StringComparison.Ordinal);
    }

    private static string NormalizeStatusRuleName(string? name)
    {
        return string.IsNullOrWhiteSpace(name)
            ? string.Empty
            : name.Trim()
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("\u3000", string.Empty, StringComparison.Ordinal);
    }

    private static uint ResolveTeamBuffAbnormalStatusId(StatusMaintenanceRuleConfig rule)
    {
        return rule.AbnormalStatusId != 0
            ? rule.AbnormalStatusId
            : rule.SkillId;
    }

    private static bool HasTeamBuffStatus(TeamMemberSnapshot member, uint abnormalStatusId)
    {
        return abnormalStatusId != 0 &&
               member.PartyMember.AbnormalStatuses.Any(entry => entry.AbnormalId == abnormalStatusId);
    }

    private static bool IsSelectedMemberBody(
        OperationResult<LockedTargetSnapshot> result,
        TeamMemberSnapshot member)
    {
        return result.Success &&
               result.Value is not null &&
               result.Value.ServerObjectId != 0 &&
               result.Value.ServerObjectId == member.ServerObjectId;
    }

    private static void LogTargetRejected(
        AccountWorkerContext context,
        TeamSupportState state,
        LockedTargetSnapshot? target,
        TeamMemberSnapshot leader,
        uint leaderTargetServerObjectId,
        string rejectReason)
    {
        if (!ShouldLog(state.LastTargetRejectLogAt))
        {
            return;
        }

        state.LastTargetRejectLogAt = DateTimeOffset.Now;
        context.Logger.Info("team_support.target.rejected", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["reason"] = rejectReason,
            ["leader"] = leader.Name,
            ["leaderServerObjectId"] = leader.ServerObjectId,
            ["leaderTargetServerObjectId"] = leaderTargetServerObjectId,
            ["targetName"] = target?.Name,
            ["targetServerObjectId"] = target?.ServerObjectId,
            ["targetObjectType"] = target?.ObjectType,
            ["targetCurrentHp"] = target?.CurrentHp,
            ["targetMaxHp"] = target?.MaxHp,
            ["targetTargetServerObjectId"] = target?.TargetServerObjectId
        });
    }

    private static void LogFollowDecision(
        AccountWorkerContext context,
        TeamSupportState state,
        string reason,
        TeamMemberSnapshot? leader,
        double groupDistanceMeters,
        bool joinCombat,
        StationaryCombatState? combatState)
    {
        if (leader is null || !ShouldLog(state.LastFollowDecisionLogAt))
        {
            return;
        }

        state.LastFollowDecisionLogAt = DateTimeOffset.Now;
        context.Logger.Info("team_support.follow.deferred", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["reason"] = reason,
            ["leader"] = leader.Name,
            ["leaderServerObjectId"] = leader.ServerObjectId,
            ["leaderDistanceToLocal"] = leader.PartyMember.DistanceToLocalPlayer,
            ["groupDistanceMeters"] = groupDistanceMeters,
            ["joinCombat"] = joinCombat,
            ["leaderDead"] = leader.PartyMember.IsDead,
            ["leaderScreenVisible"] = leader.IsScreenVisible,
            ["leaderTargetServerObjectId"] = leader.PartyMember.LiveTargetServerObjectId,
            ["combatFighting"] = combatState?.Fighting,
            ["candidateEntityId"] = combatState?.CandidateEntityId,
            ["candidateServerObjectId"] = combatState?.CandidateServerObjectId,
            ["currentTargetEntityId"] = combatState?.CurrentTargetEntityId,
            ["currentTargetServerObjectId"] = combatState?.CurrentTargetServerObjectId
        });
    }

    private static void LogActionPress(
        AccountWorkerContext context,
        TeamSupportAction action,
        string key,
        OperationResult result)
    {
        var fields = new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["action"] = action.Kind.ToString(),
            ["key"] = key,
            ["member"] = action.Target?.Name ?? action.TriggerMember?.Name,
            ["memberServerObjectId"] = action.Target?.ServerObjectId ?? action.TriggerMember?.ServerObjectId,
            ["candidateCount"] = action.CandidateCount,
            ["skillId"] = action.HealRule?.SkillId ?? action.StatusRule?.SkillId,
            ["skillName"] = action.HealRule?.SkillName ?? action.StatusRule?.SkillName,
            ["abnormalStatusId"] = action.StatusRule is null
                ? null
                : ResolveTeamBuffAbnormalStatusId(action.StatusRule)
        };

        if (result.Success)
        {
            context.Logger.Info("team_support.action_pressed", fields);
            return;
        }

        fields["error"] = result.Error;
        context.Logger.Warn("team_support.action_press.failed", fields);
    }

    private void LogCatalogWarningIfNeeded(
        IRoadhogLogger logger,
        TeamSupportState state,
        TeamSupportScriptSettings support)
    {
        if ((!support.MentalCleanseEnabled && !support.PhysicalCleanseEnabled) ||
            !ShouldLog(state.LastCatalogWarningAt))
        {
            return;
        }

        var catalog = AbnormalStatusCatalog;
        if (catalog.Loaded)
        {
            return;
        }

        state.LastCatalogWarningAt = DateTimeOffset.Now;
        logger.Warn("team_support.abnormal_catalog.failed", new Dictionary<string, object?>
        {
            ["path"] = catalog.SourcePath,
            ["error"] = catalog.Error
        });
    }

    private static string FormatFunctionKey(TeamMemberSnapshot member)
    {
        return member.FunctionKeyNumber is >= 1 and <= 6
            ? "F" + member.FunctionKeyNumber.ToString()
            : string.Empty;
    }

    private static string NormalizeKey(string key)
    {
        return key.Trim();
    }

    private static TimeSpan ResolveKeyHold(AccountWorkerContext context)
    {
        var configured = context.Config.ScriptSettings?.SemiAuto?.KeyHoldMs ?? 25;
        return TimeSpan.FromMilliseconds(Math.Clamp(configured, 1, 250));
    }

    private static bool ShouldLog(DateTimeOffset lastLogAt)
    {
        return DateTimeOffset.Now - lastLogAt >= WarningLogInterval;
    }

    private static Task<OperationResult<LockedTargetSnapshot>> ReadLockedTargetAsync(AccountWorkerContext context)
    {
        return context.GameApi is IRoadhogScopedGameApi scopedApi
            ? scopedApi.ReadLockedTargetAsync(CreateReadContext(context), context.StopToken)
            : context.GameApi.ReadLockedTargetAsync(context.StopToken);
    }

    private static Task<OperationResult<IReadOnlyList<WorldObjectSnapshot>>> ReadWorldObjectsAsync(AccountWorkerContext context)
    {
        return context.GameApi is IRoadhogScopedGameApi scopedApi
            ? scopedApi.ReadWorldObjectsAsync(CreateReadContext(context), context.StopToken)
            : context.GameApi.ReadWorldObjectsAsync(context.StopToken);
    }

    private static GameApiReadContext CreateReadContext(AccountWorkerContext context)
    {
        return new GameApiReadContext(
            context.Config.AccountName,
            context.Config.ProcessId,
            context.Config.TargetProcessName,
            context.Config.VmmDeviceName);
    }

    private sealed record TeamSupportStatusCandidate(
        TeamMemberSnapshot Member,
        int MentalCleanseCount,
        int PhysicalCleanseCount)
    {
        public bool NeedsAnyCleanse => MentalCleanseCount > 0 || PhysicalCleanseCount > 0;
    }

    private sealed record TeamSupportAction(
        TeamSupportActionKind Kind,
        TeamMemberSnapshot? Target,
        string Key,
        int CandidateCount,
        TeamHealSkillRuleConfig? HealRule = null,
        TeamMemberSnapshot? TriggerMember = null,
        StatusMaintenanceRuleConfig? StatusRule = null)
    {
        public static TeamSupportAction Targeted(
            TeamSupportActionKind kind,
            TeamMemberSnapshot target,
            string key,
            int candidateCount)
        {
            return new TeamSupportAction(kind, target, key, candidateCount);
        }

        public static TeamSupportAction TargetedHeal(
            TeamMemberSnapshot target,
            TeamHealSkillRuleConfig rule)
        {
            return new TeamSupportAction(TeamSupportActionKind.Heal, target, rule.Key, 1, rule, target);
        }

        public static TeamSupportAction GroupHeal(
            TeamHealSkillRuleConfig rule,
            TeamMemberSnapshot triggerMember)
        {
            return new TeamSupportAction(TeamSupportActionKind.GroupHeal, null, rule.Key, 1, rule, triggerMember);
        }

        public static TeamSupportAction TargetedBuff(
            TeamMemberSnapshot target,
            StatusMaintenanceRuleConfig rule)
        {
            return new TeamSupportAction(TeamSupportActionKind.TeamBuff, target, rule.Key, 1, null, target, rule);
        }

        public static TeamSupportAction GroupCleanse(string key)
        {
            return new TeamSupportAction(TeamSupportActionKind.GroupCleanse, null, key, 0);
        }
    }

    private enum TeamSupportActionKind
    {
        MentalCleanse,
        PhysicalCleanse,
        GroupCleanse,
        Heal,
        GroupHeal,
        TeamBuff
    }
}
