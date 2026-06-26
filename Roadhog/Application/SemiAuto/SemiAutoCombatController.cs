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
            state.ClearChain();
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
            state.ClearChain();
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

        var skillsResult = await ReadSkillsAsync(context).ConfigureAwait(false);
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

        if (await TryExecuteActiveChainAsync(context, plan, state, skillsResult.Value, settings).ConfigureAwait(false))
        {
            return state.ActiveChainNode is null
                ? Ms(settings.TickIntervalMs, 50)
                : Ms(settings.ChainTickIntervalMs, 30);
        }

        var executed = await TryExecuteNextRootAsync(context, plan, state, skillsResult.Value, settings).ConfigureAwait(false);
        if (!executed && ShouldLog(state.LastNoSkillLogAt, now))
        {
            state.LastNoSkillLogAt = now;
            context.Logger.Info(
                "semi_auto.skill.none_ready",
                new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["targetEntityId"] = targetResult.Value.TargetEntityId,
                    ["targetName"] = targetResult.Value.Name,
                    ["skillCount"] = skillsResult.Value.Count,
                    ["rootCount"] = plan.Roots.Count,
                    ["triggerPrefixCount"] = plan.TriggerPrefixRoots.Count,
                    ["roots"] = string.Join(" > ", plan.Roots.Select(root => root.Name + "[" + root.Type + "]@" + root.Key))
                });
        }

        return Ms(settings.TickIntervalMs, 50);
    }

    private async Task<bool> TryExecuteActiveChainAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState state,
        IReadOnlyList<SkillSnapshot> skills,
        SemiAutoScriptSettings settings)
    {
        var node = state.ActiveChainNode;
        if (node is null)
        {
            return false;
        }

        var now = DateTimeOffset.Now;
        if (state.IsChainExpired(now))
        {
            state.ClearChain();
            return false;
        }

        var skill = node.ResolveSkill(skills);
        if (skill is null)
        {
            context.Logger.Warn("semi_auto.chain.skill_missing", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["skill"] = node.Name,
                ["skillId"] = node.SkillId,
                ["key"] = node.Key
            });
            state.ClearChain();
            return false;
        }

        if (skill.CooldownEndTime != 0)
        {
            AdvanceChain(state, node, settings);
            return true;
        }

        if (!state.CanPress(node, now, Ms(settings.RepeatGuardMs, 120)))
        {
            return true;
        }

        var confirmed = await PressSkillAsync(context, plan, state, node, skill, settings).ConfigureAwait(false);
        if (confirmed)
        {
            AdvanceChain(state, node, settings);
        }

        return true;
    }

    private async Task<bool> TryExecuteNextRootAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState state,
        IReadOnlyList<SkillSnapshot> skills,
        SemiAutoScriptSettings settings)
    {
        foreach (var root in plan.Roots)
        {
            if (root.IsTrigger)
            {
                continue;
            }

            var now = DateTimeOffset.Now;
            if (!state.CanPress(root, now, Ms(settings.RepeatGuardMs, 120)))
            {
                continue;
            }

            var skill = root.ResolveSkill(skills);
            if (skill is null || skill.CooldownEndTime != 0)
            {
                if (skill is null && ShouldLog(state.LastSkillWarningAt, now))
                {
                    state.LastSkillWarningAt = now;
                    context.Logger.Warn("semi_auto.root.skill_missing", new Dictionary<string, object?>
                    {
                        ["account"] = context.Config.AccountName,
                        ["skill"] = root.Name,
                        ["skillId"] = root.SkillId,
                        ["key"] = root.Key,
                        ["type"] = root.Type
                    });
                }

                continue;
            }

            var confirmed = await PressSkillAsync(context, plan, state, root, skill, settings).ConfigureAwait(false);
            if (confirmed && root.Children.Count > 0)
            {
                StartNextChain(state, root, root.Children[0], settings);
            }
            else if (!confirmed)
            {
                state.Suppress(root, DateTimeOffset.Now + TimeSpan.FromSeconds(1));
            }

            return true;
        }

        return false;
    }

    private async Task<bool> PressSkillAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState state,
        SemiAutoSkillNode node,
        SkillSnapshot skill,
        SemiAutoScriptSettings settings)
    {
        if (!node.IsTrigger)
        {
            await PressTriggerPrefixAsync(context, plan, node, settings).ConfigureAwait(false);
        }

        var pressResult = await PressNodeKeyAsync(context, state, node, settings).ConfigureAwait(false);
        if (!pressResult)
        {
            return false;
        }

        if (skill.CooldownDuration == 0)
        {
            return true;
        }

        return await WaitForCooldownStartAsync(context, node, settings).ConfigureAwait(false);
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

            await PressNodeKeyAsync(context, null, trigger, settings, validateRepeatGuard: false).ConfigureAwait(false);
            await Delay(settings.KeyGapMs, 30, context.StopToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> PressNodeKeyAsync(
        AccountWorkerContext context,
        SemiAutoCombatState? state,
        SemiAutoSkillNode node,
        SemiAutoScriptSettings settings,
        bool validateRepeatGuard = true)
    {
        var now = DateTimeOffset.Now;
        if (validateRepeatGuard &&
            state is not null &&
            !state.CanPress(node, now, Ms(settings.RepeatGuardMs, 120)))
        {
            return false;
        }

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
                ["error"] = result.Error
            });
            return false;
        }

        state?.MarkPressed(node, now);
        context.Logger.Info("semi_auto.key.pressed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["skill"] = node.Name,
            ["key"] = node.Key,
            ["type"] = node.Type
        });
        return true;
    }

    private async Task<bool> WaitForCooldownStartAsync(
        AccountWorkerContext context,
        SemiAutoSkillNode node,
        SemiAutoScriptSettings settings)
    {
        var deadline = DateTimeOffset.Now + Ms(settings.ConfirmTimeoutMs, 500);
        while (DateTimeOffset.Now < deadline)
        {
            await Delay(settings.ConfirmPollMs, 30, context.StopToken).ConfigureAwait(false);
            var skillsResult = await ReadSkillsAsync(context).ConfigureAwait(false);
            if (!skillsResult.Success || skillsResult.Value is null)
            {
                continue;
            }

            var updated = node.ResolveSkill(skillsResult.Value);
            if (updated?.CooldownEndTime != 0)
            {
                return true;
            }
        }

        context.Logger.Warn("semi_auto.skill.not_confirmed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["skill"] = node.Name,
            ["key"] = node.Key
        });
        return false;
    }

    private static void AdvanceChain(
        SemiAutoCombatState state,
        SemiAutoSkillNode completedNode,
        SemiAutoScriptSettings settings)
    {
        if (completedNode.Children.Count == 0)
        {
            state.ClearChain();
            return;
        }

        StartNextChain(state, completedNode, completedNode.Children[0], settings);
    }

    private static void StartNextChain(
        SemiAutoCombatState state,
        SemiAutoSkillNode sourceNode,
        SemiAutoSkillNode nextNode,
        SemiAutoScriptSettings settings)
    {
        var windowMs = nextNode.ChainTimeMs ??
                       sourceNode.ChainTimeMs ??
                       settings.DefaultChainTimeMs;
        state.StartChain(nextNode, DateTimeOffset.Now + Ms(windowMs, 5000));
    }

    private static Task<OperationResult<LockedTargetSnapshot>> ReadLockedTargetAsync(AccountWorkerContext context)
    {
        if (context.GameApi is IRoadhogScopedGameApi scopedApi)
        {
            return scopedApi.ReadLockedTargetAsync(CreateReadContext(context), context.StopToken);
        }

        return context.GameApi.ReadLockedTargetAsync(context.StopToken);
    }

    private static Task<OperationResult<IReadOnlyList<SkillSnapshot>>> ReadSkillsAsync(AccountWorkerContext context)
    {
        if (context.GameApi is IRoadhogScopedGameApi scopedApi)
        {
            return scopedApi.ReadSkillsAsync(CreateReadContext(context), context.StopToken);
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

    private static async Task Delay(int configuredMs, int fallbackMs, CancellationToken cancellationToken)
    {
        var delay = Ms(configuredMs, fallbackMs);
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static TimeSpan Ms(int configuredMs, int fallbackMs)
    {
        var value = configuredMs > 0 ? configuredMs : fallbackMs;
        return TimeSpan.FromMilliseconds(Math.Max(1, value));
    }

    private static bool ShouldLog(DateTimeOffset lastLogAt, DateTimeOffset now)
    {
        return now - lastLogAt >= WarningLogInterval;
    }
}
