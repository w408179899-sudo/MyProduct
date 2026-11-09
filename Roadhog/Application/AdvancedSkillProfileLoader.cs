using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Profiles;

namespace Roadhog.Application;

public static class AdvancedSkillProfileLoader
{
    // Resolve once per script start. Running workers own their cloned snapshot.
    public static async Task ApplyAsync(AccountConfig account, IAdvancedSkillProfileStore? store,
        IRoadhogLogger logger, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(account.AdvancedSkillProfileName)) return;
        account.ScriptSettings ??= new();
        account.ScriptSettings.AdvancedSkills = new();
        if (!account.AdvancedSkillProfileEnabled) return;
        var result = store is null ? null : await store.LoadAllAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var profile = result?.Success == true ? result.Value?.FirstOrDefault(p =>
            string.Equals(p.Name, account.AdvancedSkillProfileName.Trim(), StringComparison.OrdinalIgnoreCase)) : null;
        if (profile is null)
        {
            logger.Warn("advanced_skill.profile.unavailable", new Dictionary<string, object?>
            { ["account"] = account.AccountName, ["profile"] = account.AdvancedSkillProfileName, ["error"] = result?.Error ?? "方案不存在" });
            return; // Original opening/ordinary flow; never run a stale copied shared plan.
        }
        account.ScriptSettings.AdvancedSkills = profile.Settings.Clone();
        account.ScriptSettings.AdvancedSkills.Enabled = true;
    }
}
