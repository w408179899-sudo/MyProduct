using System.Reflection;
using Roadhog.Application.SemiAuto;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Model;

internal static class CombatFlowMaintenanceAdapterTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> Cases
    {
        get
        {
            foreach (var resource in new[] { "hp", "mp", "dp" })
            foreach (var scenario in new[] { "confirmed", "retry", "input_failed" })
                yield return ($"refactor maintenance adapter {resource} {scenario}", () => VerifyAsync(resource, scenario));
            foreach (var scenario in new[] { "dead", "lost", "changed" })
                yield return ($"refactor maintenance adapter drain {scenario}", () => VerifyAsync("hp", scenario));
        }
    }

    private static async Task VerifyAsync(string resource, string scenario)
    {
        using var f = new SharedSkillAuditFixture();
        f.Settings.Skills.SpiritmasterAutoSkillLogicEnabled = false;
        var id = resource == "mp" ? SharedSkillAuditFixture.LowMpId : SharedSkillAuditFixture.LowHpId;
        var key = resource == "mp" ? "D3" : "D2";
        var drain = scenario is "dead" or "lost" or "changed";
        if (drain) f.Api.Skills = f.Api.Skills.Select(s => s.SkillId == id ? s with
            { XmlFirstTarget = "target", XmlTargetRelationRestriction = "enemy", XmlEffects = "SkillATKDrain_Instant" } : s).ToArray();
        await f.PrepareAsync();
        var skill = f.Api.Skills.Single(s => s.SkillId == id);
        var presses = 0;
        f.Keyboard.PressResult = _ => scenario == "input_failed" ? OperationResult.Fail("fixture") : OperationResult.Ok();
        f.Keyboard.AfterPress = _ =>
        {
            presses++;
            switch (scenario)
            {
                case "dead": f.Api.TargetCurrentHp = 0; return;
                case "lost": f.Api.TargetEntityId = 0; return;
                case "changed": f.Api.TargetOwnServerObjectId++; return;
            }
            if (scenario != "retry" || presses == 2) f.Confirm(id);
        };
        var name = resource == "dp" ? "ExecuteDpMaintenanceKeyRuleAsync" : "ExecuteMaintenanceKeyRuleAsync";
        var method = typeof(SemiAutoCombatController).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
        object?[] arguments = resource == "dp"
            ? new object?[] { f.Context, f.State, f.Settings.SemiAuto,
                new DpMaintenanceRuleConfig { Key = key, SkillId = id, SkillName = skill.Name }, resource,
                f.Api.Player, 4000, null, skill }
            : new object?[] { f.Context, f.State, f.Settings.SemiAuto,
                new MaintenanceKeyRuleConfig { Key = key, SkillId = id, SkillName = skill.Name,
                    RunTiming = MaintenanceRuleRunTiming.InCombat }, resource, 50u, 100u, 50.0, 80,
                f.Api.Player, null, skill, null };
        var consumed = await (Task<bool>)method.Invoke(f.Controller, arguments)!;
        SharedSkillAuditFixture.Check(consumed == (scenario != "input_failed"), "adapter changed tick ownership");
        SharedSkillAuditFixture.Check(f.Keyboard.Keys.Count == (scenario == "retry" ? 2 : 1), "adapter changed retry count");
        SharedSkillAuditFixture.Check(f.Observer.Requests.All(ids => ids is not null && ids.SequenceEqual(new[] { id })),
            "confirmation must read the exact configured rank, never the highest editor rank or an unrelated cooldown");
        var logs = f.Logger.Entries.Where(e => e.EventName.StartsWith("semi_auto.maintenance.")).ToArray();
        var confirmed = logs.Where(e => e.Fields.TryGetValue("confirmedSkillId", out var skillId) && Equals(skillId, id)).ToArray();
        SharedSkillAuditFixture.Check(confirmed.Length == (!drain && scenario != "input_failed" ? 1 : 0),
            "confirmation is logged only when the configured skill advances");
        if (drain) SharedSkillAuditFixture.Check(logs.Any(e => e.EventName == "semi_auto.maintenance.target_guard_blocked") &&
            !logs.Any(e => e.EventName.EndsWith("unconfirmed")), "target invalidation must exit before timeout");
    }
}
