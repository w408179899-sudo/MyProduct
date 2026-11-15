using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public sealed partial class SemiAutoCombatController
{
    private static bool RequiresLifeDrainTarget(string resource, SkillSnapshot? skill, uint configuredSkillId)
    {
        if (resource != "hp") return false;

        // Targeted physical, magical and periodic life drain share this guard.
        // Read static effects from the resolved official skill, never display names.
        if (string.Equals(skill?.XmlFirstTarget?.Trim(), "Target", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(skill?.XmlTargetRelationRestriction?.Trim(), "Enemy", StringComparison.OrdinalIgnoreCase) &&
            (skill?.XmlEffects ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Any(effect => effect.Equals("SkillATKDrain_Instant", StringComparison.OrdinalIgnoreCase) ||
                               effect.Equals("SpellATKDrain_Instant", StringComparison.OrdinalIgnoreCase) ||
                               effect.Equals("SpellATKDrain", StringComparison.OrdinalIgnoreCase)))
            return true;

        // Compatibility for EL_DrainEnergy_G1..G7 when XML detail is unavailable.
        return (skill?.SkillId ?? configuredSkillId) is 1755 or 1756 or 1757 or 1758 or 1779 or 1790 or 2229;
    }

    private static string? GetLifeDrainTargetBlockReason(
        LockedTargetSnapshot target, LockedTargetSnapshot? expected)
    {
        if (!target.HasTarget) return "target_lost";
        if (!target.IsMonster) return "target_not_monster";
        if (!target.IsMonsterAlive) return "target_dead";
        if (expected is not null &&
            (target.TargetEntityId != expected.TargetEntityId || target.ServerObjectId != expected.ServerObjectId))
            return "target_changed";
        return null;
    }

    private static void LogLifeDrainTargetBlocked(
        AccountWorkerContext context, MaintenanceKeyRuleConfig rule, SkillSnapshot? skill,
        LockedTargetSnapshot target, LockedTargetSnapshot? expected, string reason, int attempts,
        DateTimeOffset startedAt) =>
        context.Logger.Info("semi_auto.maintenance.target_guard_blocked", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["resource"] = "hp",
            ["key"] = rule.Key,
            ["maintenanceSkillId"] = skill?.SkillId ?? rule.SkillId,
            ["maintenanceSkillName"] = skill?.Name ?? rule.SkillName,
            ["reason"] = reason,
            ["attempts"] = attempts,
            ["confirmElapsedMs"] = (long)Math.Max(0, (DateTimeOffset.Now - startedAt).TotalMilliseconds),
            ["targetEntityId"] = target.TargetEntityId,
            ["targetServerObjectId"] = target.ServerObjectId,
            ["targetName"] = target.Name,
            ["targetHp"] = target.CurrentHp,
            ["targetMaxHp"] = target.MaxHp,
            ["isMonsterAlive"] = target.IsMonsterAlive,
            ["expectedEntityId"] = expected?.TargetEntityId,
            ["expectedServerObjectId"] = expected?.ServerObjectId
        });
}
