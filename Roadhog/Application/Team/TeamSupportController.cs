using Roadhog.Application.Workers;
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
        TeamSupportState state)
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
        var action = await SelectActionAsync(context, support, snapshot).ConfigureAwait(false);
        if (action is not null)
        {
            await ExecuteActionAsync(context, state, action).ConfigureAwait(false);
            return TeamSupportTickResult.SkipNormalWork(TeamSupportTickDelay);
        }

        var leaderDeadStop =
            support.StopWhenLeaderDead &&
            snapshot.LeaderMember?.PartyMember.IsDead == true;
        return !support.JoinCombat || leaderDeadStop
            ? TeamSupportTickResult.SkipNormalWork(TeamSupportTickDelay)
            : TeamSupportTickResult.Continue(TeamSupportTickDelay);
    }

    private async Task<TeamSupportAction?> SelectActionAsync(
        AccountWorkerContext context,
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
        if (healRules.Length == 0)
        {
            return null;
        }

        var timing = await ResolveCurrentRunTimingAsync(context, snapshot).ConfigureAwait(false);
        var allowedRules = healRules
            .Where(rule => IsRuleAllowed(rule, timing))
            .ToArray();
        if (allowedRules.Length == 0)
        {
            return null;
        }

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

        if (healCandidate?.Rule is null)
        {
            return null;
        }

        return healCandidate.Rule.TargetType == TeamHealSkillTargetType.Group
            ? TeamSupportAction.GroupHeal(healCandidate.Rule, healCandidate.Member)
            : TeamSupportAction.TargetedHeal(healCandidate.Member, healCandidate.Rule);
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
        return true;
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

    private static bool IsRuleAllowed(
        TeamHealSkillRuleConfig rule,
        MaintenanceRuleRunTiming timing)
    {
        return rule.RunTiming == MaintenanceRuleRunTiming.Always ||
               rule.RunTiming == timing;
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
            ["skillId"] = action.HealRule?.SkillId,
            ["skillName"] = action.HealRule?.SkillName
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
        TeamMemberSnapshot? TriggerMember = null)
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
        GroupHeal
    }
}
