using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Profiles;

namespace Roadhog.Application;

/// <summary>Resolves the same shared profile and cleanup lists for manual starts and process recovery.</summary>
public sealed class AccountStartConfigBuilder
{
    private readonly IScriptProfileStore _profiles;
    private readonly IBagCleanupNameListStore _nameLists;
    private readonly Func<string, IBagCleanupNameListStore>? _accountNameListFactory;
    private readonly IRoadhogLogger _logger;

    public AccountStartConfigBuilder(IScriptProfileStore profiles, IBagCleanupNameListStore nameLists, IRoadhogLogger logger,
        Func<string, IBagCleanupNameListStore>? accountNameListFactory = null)
    { _profiles = profiles; _nameLists = nameLists; _logger = logger; _accountNameListFactory = accountNameListFactory; }

    public async Task<OperationResult<AccountConfig>> BuildAsync(AccountConfig account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (!account.Validate(out var error)) return OperationResult<AccountConfig>.Fail(error);
        var config = account.Clone();
        var nameLists = _nameLists;
        if (!string.IsNullOrWhiteSpace(config.BagCleanupNameListPath))
        {
            if (_accountNameListFactory is not null) nameLists = _accountNameListFactory(config.BagCleanupNameListPath);
            else if (!string.Equals(config.BagCleanupNameListPath, nameLists.FilePath, StringComparison.OrdinalIgnoreCase))
                return OperationResult<AccountConfig>.Fail("账号物品名单需要对应的加载器，不能使用其他账号的共享名单。");
        }
        var profileName = string.IsNullOrWhiteSpace(config.ScriptSettings?.ProfileName)
            ? config.ProfileName : config.ScriptSettings.ProfileName;
        if (!string.IsNullOrWhiteSpace(profileName))
        {
            var profile = await _profiles.LoadAsync(profileName, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (profile.Success && profile.Value is not null) config.ScriptSettings = profile.Value.Settings.Clone();
            else _logger.Warn("account.profile.load_failed", new Dictionary<string, object?>
            { ["account"] = config.AccountName, ["profileName"] = profileName, ["error"] = profile.Error });
        }
        config.ScriptSettings ??= new ScriptSettings
        {
            ProfileName = string.IsNullOrWhiteSpace(config.ProfileName) ? "default_profile" : config.ProfileName,
            MainMode = config.MainMode,
            CombatMode = config.CombatMode
        };
        var lists = await nameLists.LoadAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!lists.Success)
        {
            config.ScriptSettings.Maintenance.BagCleanupEnabled = false;
            _logger.Warn("bag_cleanup.name_lists.apply_failed", new Dictionary<string, object?>
            {
                ["account"] = config.AccountName, ["path"] = nameLists.FilePath,
                ["error"] = lists.Error, ["bagCleanupDisabled"] = true
            });
        }
        else if (lists.Value is { Found: true, Document: { } document })
        {
            document.ApplyTo(config.ScriptSettings.Maintenance);
            _logger.Info("bag_cleanup.name_lists.applied", new Dictionary<string, object?>
            {
                ["account"] = config.AccountName, ["path"] = nameLists.FilePath,
                ["source"] = lists.Value.Source.ToString(),
                ["whitelistCount"] = document.Whitelist.Count, ["blacklistCount"] = document.Blacklist.Count
            });
        }
        config.ProfileName = config.ScriptSettings.ProfileName;
        config.MainMode = config.ScriptSettings.MainMode;
        config.CombatMode = config.ScriptSettings.CombatMode;
        config.RevivePathName = config.ScriptSettings.Paths.RevivePathName;
        config.CombatPathName = config.ScriptSettings.Paths.CombatPathName;
        config.MaintenancePathName = config.ScriptSettings.Paths.MaintenancePathName;
        return OperationResult<AccountConfig>.Ok(config);
    }
}
