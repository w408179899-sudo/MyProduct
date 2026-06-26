using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public sealed class SemiAutoCombatController
{
    private static readonly TimeSpan WarningLogInterval = TimeSpan.FromSeconds(3);

    private readonly IKeyboardInput _keyboard;

    public SemiAutoCombatController(IKeyboardInput keyboard)
    {
        _keyboard = keyboard;
    }

    public async Task<TimeSpan> TickAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState state)
    {
        var settings = context.Config.ScriptSettings?.SemiAuto ?? new SemiAutoScriptSettings();
        var now = DateTimeOffset.Now;

        if (!plan.HasExecutableSkills)
        {
            if (ShouldLog(state.LastPlanWarningAt, now))
            {
                state.LastPlanWarningAt = now;
                context.Logger.Warn(
                    "semi_auto.plan.empty",
                    new Dictionary<string, object?> { ["account"] = context.Config.AccountName });
            }

            return Ms(settings.TargetIdleDelayMs, 200);
        }

        var targetResult = await ReadLockedTargetAsync(context).ConfigureAwait(false);
        if (!targetResult.Success || targetResult.Value is null)
        {
            if (ShouldLog(state.LastTargetWarningAt, now))
            {
                state.LastTargetWarningAt = now;
                context.Logger.Warn(
                    "semi_auto.target.read_failed",
                    new Dictionary<string, object?>
                    {
                        ["account"] = context.Config.AccountName,
                        ["error"] = targetResult.Error
                    });
            }

            return Ms(settings.TargetIdleDelayMs, 200);
        }

        if (!targetResult.Value.IsMonsterAlive)
        {
            if (ShouldLog(state.LastTargetStateLogAt, now))
            {
                state.LastTargetStateLogAt = now;
                context.Logger.Info(
                    "semi_auto.target.not_attackable",
                    new Dictionary<string, object?>
                    {
                        ["account"] = context.Config.AccountName,
                        ["hasTarget"] = targetResult.Value.HasTarget,
                        ["targetEntityId"] = targetResult.Value.TargetEntityId,
                        ["targetName"] = targetResult.Value.Name,
                        ["objectType"] = targetResult.Value.ObjectType,
                        ["currentHp"] = targetResult.Value.CurrentHp,
                        ["maxHp"] = targetResult.Value.MaxHp,
                        ["isMonster"] = targetResult.Value.IsMonster,
                        ["isAlive"] = targetResult.Value.IsAlive
                    });
            }

            return Ms(settings.TargetIdleDelayMs, 200);
        }

        var skillsResult = await ReadSkillsAsync(context, plan).ConfigureAwait(false);
        if (!skillsResult.Success || skillsResult.Value is null)
        {
            if (ShouldLog(state.LastSkillWarningAt, now))
            {
                state.LastSkillWarningAt = now;
                context.Logger.Warn(
                    "semi_auto.skills.read_failed",
                    new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["error"] = skillsResult.Error
                });
            }

            return Ms(settings.TickIntervalMs, 50);
        }

        if (skillsResult.Value.Count == 0)
        {
            if (ShouldLog(state.LastNoSkillLogAt, now))
            {
                state.LastNoSkillLogAt = now;
                context.Logger.Warn(
                    "semi_auto.skills.empty",
                    new Dictionary<string, object?>
                    {
                        ["account"] = context.Config.AccountName,
                        ["targetEntityId"] = targetResult.Value.TargetEntityId,
                        ["targetName"] = targetResult.Value.Name
                    });
            }

            return Ms(settings.TickIntervalMs, 50);
        }

        var configuredSkills = ResolveConfiguredSkills(plan, skillsResult.Value);
        var osTick = CurrentOsTick();
        if (state.TryUpdateCooldownTickCalibration(
                configuredSkills,
                osTick,
                DateTimeOffset.Now,
                out var calibration))
        {
            context.Logger.Info("semi_auto.cooldown.calibrated", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["skill"] = calibration.SkillName,
                ["skillId"] = calibration.SkillId,
                ["durationMs"] = calibration.CooldownDuration,
                ["endTick"] = calibration.CooldownEndTime,
                ["startTick"] = calibration.CooldownStartTick,
                ["osTick"] = calibration.OsTick,
                ["offsetMs"] = calibration.OffsetMs
            });
        }

        var decision = SemiAutoSkillReleasePriority.SelectNext(plan, state, configuredSkills, settings, DateTimeOffset.Now);
        if (decision.Kind != SemiAutoSkillReleaseDecisionKind.None)
        {
            await ExecuteReleaseDecisionAsync(context, plan, state, decision, settings).ConfigureAwait(false);
            return state.HasChainWork
                ? Ms(settings.ChainTickIntervalMs, 40)
                : Ms(settings.TickIntervalMs, 40);
        }

        if (ShouldLog(state.LastNoSkillLogAt, now))
        {
            state.LastNoSkillLogAt = now;
            context.Logger.Info(
                "semi_auto.skill.none_ready",
                new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["targetEntityId"] = targetResult.Value.TargetEntityId,
                    ["targetName"] = targetResult.Value.Name,
                    ["skillCount"] = configuredSkills.Count,
                    ["rawSkillCount"] = skillsResult.Value.Count,
                    ["topLevelSkillCount"] = plan.Roots.Count,
                    ["chainRootCount"] = plan.Roots.Count(root => root.Children.Count > 0),
                    ["triggerPrefixCount"] = plan.TriggerPrefixRoots.Count,
                    ["topLevelSkills"] = string.Join(" > ", plan.Roots.Select(root => root.Name + "[" + root.Type + "]@" + root.Key)),
                    ["chainRoots"] = string.Join(" > ", plan.Roots.Where(root => root.Children.Count > 0).Select(root => root.Name + "@" + root.Key)),
                    ["configuredSkills"] = FormatConfiguredSkills(configuredSkills),
                    ["reasons"] = SemiAutoSkillReleasePriority.BuildNoReadyReasons(plan, state, configuredSkills, settings)
                });
        }

        return Ms(settings.TickIntervalMs, 40);
    }

    private async Task ExecuteReleaseDecisionAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState state,
        SemiAutoSkillReleaseDecision decision,
        SemiAutoScriptSettings settings)
    {
        var node = decision.Node;
        if (node is null)
        {
            return;
        }

        if (decision.Kind == SemiAutoSkillReleaseDecisionKind.ClearPendingChain)
        {
            LogChainEnded(context, node, decision.Reason, decision.Skill);
            state.ClearPendingChainAdvance();
            return;
        }

        if (!decision.ShouldPress || decision.Skill is null)
        {
            return;
        }

        var pressed = await PressSkillAsync(
                context,
                plan,
                state,
                node,
                settings,
                includeTriggerPrefix: decision.Kind != SemiAutoSkillReleaseDecisionKind.PressChain)
            .ConfigureAwait(false);
        if (decision.Kind == SemiAutoSkillReleaseDecisionKind.PressChain)
        {
            state.ClearPendingChainAdvance();
            if (pressed)
            {
                state.MarkSkillPressed(
                    decision.Skill,
                    DateTimeOffset.Now + Ms(settings.ConfirmTimeoutMs, 1500));
                StartPendingChainAdvance(context, state, node, decision.Skill, settings);
            }
            else
            {
                LogChainEnded(context, node, "press_failed", decision.Skill);
                state.ClearChain();
            }

            return;
        }

        if (pressed)
        {
            state.MarkSkillPressed(
                decision.Skill,
                DateTimeOffset.Now + Ms(settings.ConfirmTimeoutMs, 1500));
            if (node.Children.Count > 0)
            {
                StartPendingChainAdvance(context, state, node, decision.Skill, settings);
            }
        }
    }

    private async Task<bool> PressSkillAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState state,
        SemiAutoSkillNode node,
        SemiAutoScriptSettings settings,
        bool includeTriggerPrefix)
    {
        return await PressSkillKeysAsync(
                context,
                plan,
                node,
                settings,
                includeTriggerPrefix)
            .ConfigureAwait(false);
    }

    private async Task<bool> PressSkillKeysAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoSkillNode node,
        SemiAutoScriptSettings settings,
        bool includeTriggerPrefix)
    {
        if (includeTriggerPrefix && !node.IsTrigger)
        {
            await PressTriggerPrefixAsync(context, plan, node, settings).ConfigureAwait(false);
        }

        return await PressNodeKeyAsync(
                context,
                node,
                settings,
                phase: "skill")
            .ConfigureAwait(false);
    }

    private async Task PressTriggerPrefixAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoSkillNode currentNode,
        SemiAutoScriptSettings settings)
    {
        foreach (var trigger in plan.TriggerPrefixRoots)
        {
            if (ReferenceEquals(trigger, currentNode))
            {
                continue;
            }

            await PressNodeKeyAsync(
                    context,
                    trigger,
                    settings,
                    phase: "trigger_prefix")
                .ConfigureAwait(false);
        }
    }

    private async Task<bool> PressNodeKeyAsync(
        AccountWorkerContext context,
        SemiAutoSkillNode node,
        SemiAutoScriptSettings settings,
        string phase = "skill")
    {
        var result = await _keyboard
            .PressKeyAsync(node.Key, Ms(settings.KeyHoldMs, 25), context.StopToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            context.Logger.Warn("semi_auto.key.failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["skill"] = node.Name,
                ["key"] = node.Key,
                ["phase"] = phase,
                ["error"] = result.Error
            });
            return false;
        }

        context.Logger.Info("semi_auto.key.pressed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["skill"] = node.Name,
            ["key"] = node.Key,
            ["type"] = node.Type,
            ["phase"] = phase
        });
        return true;
    }

    private static IReadOnlyList<SkillSnapshot> ResolveConfiguredSkills(
        SemiAutoSkillPlan plan,
        IReadOnlyList<SkillSnapshot> learnedSkills)
    {
        var configuredSkills = new List<SkillSnapshot>();
        var seenSkillIds = new HashSet<uint>();
        foreach (var node in FlattenNodes(plan.Roots))
        {
            var skill = node.ResolveSkill(learnedSkills);
            if (skill is null || !seenSkillIds.Add(skill.SkillId))
            {
                continue;
            }

            configuredSkills.Add(skill);
        }

        return configuredSkills;
    }

    private static IEnumerable<SemiAutoSkillNode> FlattenNodes(IEnumerable<SemiAutoSkillNode> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in FlattenNodes(root.Children))
            {
                yield return child;
            }
        }
    }

    private static string FormatConfiguredSkills(IReadOnlyList<SkillSnapshot> configuredSkills)
    {
        return string.Join(
            " | ",
            configuredSkills.Select(skill =>
                skill.Name +
                "#" + skill.SkillId +
                ":cooldown=" + skill.CooldownDuration +
                "/" + skill.CooldownEndTime));
    }

    private static void StartPendingChainAdvance(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoSkillNode sourceNode,
        SkillSnapshot sourceSkill,
        SemiAutoScriptSettings settings)
    {
        if (sourceNode.Children.Count == 0)
        {
            state.ClearChain();
            return;
        }

        StartPendingChainAdvance(context, state, sourceNode, sourceSkill, sourceNode.Children[0], settings);
    }

    private static void StartPendingChainAdvance(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoSkillNode sourceNode,
        SkillSnapshot sourceSkill,
        SemiAutoSkillNode nextNode,
        SemiAutoScriptSettings settings)
    {
        var windowMs = nextNode.ChainTimeMs ??
                       sourceNode.ChainTimeMs ??
                       settings.DefaultChainTimeMs;
        state.StartPendingChainAdvance(
            sourceNode,
            nextNode,
            DateTimeOffset.Now + Ms(windowMs, 5000),
            sourceSkill.CooldownEndTime);
        context.Logger.Info("semi_auto.chain.pending", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["sourceSkill"] = sourceNode.Name,
            ["sourceKey"] = sourceNode.Key,
            ["sourceCooldownEndTime"] = sourceSkill.CooldownEndTime,
            ["nextSkill"] = nextNode.Name,
            ["nextKey"] = nextNode.Key,
            ["configuredChildCount"] = sourceNode.Children.Count,
            ["expiresInMs"] = windowMs
        });
    }

    private static void LogChainEnded(
        AccountWorkerContext context,
        SemiAutoSkillNode node,
        string reason,
        SkillSnapshot? skill = null)
    {
        context.Logger.Info("semi_auto.chain.ended", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["skill"] = node.Name,
            ["key"] = node.Key,
            ["type"] = node.Type,
            ["reason"] = reason,
            ["cooldownDuration"] = skill?.CooldownDuration,
            ["cooldownEndTime"] = skill?.CooldownEndTime
        });
    }

    private static Task<OperationResult<LockedTargetSnapshot>> ReadLockedTargetAsync(AccountWorkerContext context)
    {
        if (context.GameApi is IRoadhogScopedGameApi scopedApi)
        {
            return scopedApi.ReadLockedTargetAsync(CreateReadContext(context), context.StopToken);
        }

        return context.GameApi.ReadLockedTargetAsync(context.StopToken);
    }

    private static Task<OperationResult<IReadOnlyList<SkillSnapshot>>> ReadSkillsAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan)
    {
        if (context.GameApi is IRoadhogScopedGameApi scopedApi)
        {
            var readContext = CreateReadContext(context);
            if (!plan.RequiresFullSkillRead && plan.SkillReadIds.Count > 0)
            {
                return scopedApi.ReadSkillsAsync(readContext, plan.SkillReadIds, context.StopToken);
            }

            return scopedApi.ReadSkillsAsync(readContext, context.StopToken);
        }

        return context.GameApi.ReadSkillsAsync(context.StopToken);
    }

    private static GameApiReadContext CreateReadContext(AccountWorkerContext context)
    {
        return new GameApiReadContext(
            context.Config.AccountName,
            context.Config.ProcessId,
            context.Config.TargetProcessName,
            context.Config.VmmDeviceName);
    }

    private static TimeSpan Ms(int configuredMs, int fallbackMs)
    {
        var value = configuredMs > 0 ? configuredMs : fallbackMs;
        return TimeSpan.FromMilliseconds(Math.Max(1, value));
    }

    private static uint CurrentOsTick()
    {
        return unchecked((uint)Environment.TickCount64);
    }

    private static bool ShouldLog(DateTimeOffset lastLogAt, DateTimeOffset now)
    {
        return now - lastLogAt >= WarningLogInterval;
    }
}
