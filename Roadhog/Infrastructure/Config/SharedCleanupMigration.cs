using System.Text.RegularExpressions;
using Roadhog.Core.Accounts;
using Roadhog.Infrastructure.Profiles;

namespace Roadhog.Infrastructure.Config;

public static class SharedCleanupMigration
{
    public static string InitialRegion(AccountConfig account)
    {
        if (!string.IsNullOrWhiteSpace(account.Region)) return account.Region.Trim();
        var match = Regex.Match(account.BagCleanupNameListPath.Replace('\\', '/'), @"(?:^|/)preserved/([1-6])/", RegexOptions.IgnoreCase);
        if (!match.Success) match = Regex.Match(account.AccountName.Trim(), @"^(?:脚本|账号)?([1-6])$");
        return match.Success ? int.Parse(match.Groups[1].Value) <= 4 ? "一区" : "六区" : "未分区";
    }

    public static async Task MigrateAsync(string accountsPath, IReadOnlyList<AccountConfig> accounts, string profilesDirectory,
        string? defaultListPath = null, CancellationToken token = default)
    {
        var root = Path.GetDirectoryName(Path.GetFullPath(accountsPath))!;
        var store = new SharedAccountConfigurationStore(SharedAccountConfigurationStore.PathFor(accountsPath), "未分区");
        var marker = Path.Combine(root, "shared-cleanup-migrated.json");
        var profiles = new JsonScriptProfileStore(profilesDirectory);
        foreach (var account in accounts) account.Region = InitialRegion(account);
        await store.UpdateAsync(async data =>
        {
            if (File.Exists(marker) && !AtomicJsonFile.Exists(store.FilePath))
                throw new InvalidDataException("共享清包配置已丢失，请从 shared-cleanup-backups 恢复；不会重新导入旧名单。");
            var pending = accounts.Where(a => !data.MigratedAccounts.Contains(Key(a), StringComparer.OrdinalIgnoreCase)).ToArray();
            if (pending.Length == 0 && File.Exists(store.FilePath)) return false;
            var backup = Path.Combine(root, "shared-cleanup-backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));
            Directory.CreateDirectory(backup);
            void Backup(string path)
            {
                if (!File.Exists(path)) return;
                var name = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path))))[..12];
                File.Copy(path, Path.Combine(backup, name + "-" + Path.GetFileName(path)), overwrite: true);
            }
            Backup(accountsPath); Backup(store.FilePath);
            var firstMigration = !File.Exists(store.FilePath);
            foreach (var account in pending)
            {
                token.ThrowIfCancellationRequested();
                var settings = account.ScriptSettings?.Clone() ?? new();
                var profileName = string.IsNullOrWhiteSpace(account.ScriptSettings?.ProfileName) ? account.ProfileName : account.ScriptSettings.ProfileName;
                profileName = string.IsNullOrWhiteSpace(profileName) ? "default_profile" : profileName;
                var safeProfileName = new string(profileName.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
                Backup(Path.Combine(profilesDirectory, (safeProfileName.Length == 0 ? "profile" : safeProfileName) + ".json"));
                var profile = await profiles.LoadAsync(profileName, token).ConfigureAwait(false);
                var monsters = settings.Combat.ActiveMonsterNameFilters.AsEnumerable();
                if (profile.Success && profile.Value is not null)
                {
                    monsters = monsters.Concat(profile.Value.Settings.Combat.ActiveMonsterNameFilters);
                    settings = profile.Value.Settings.Clone();
                }
                var path = string.IsNullOrWhiteSpace(account.BagCleanupNameListPath)
                    ? defaultListPath ?? Path.Combine(root, JsonBagCleanupNameListStore.DefaultFileName)
                    : Path.GetFullPath(account.BagCleanupNameListPath, root);
                Backup(path);
                Backup(Path.Combine(Path.GetDirectoryName(path)!, JsonBagCleanupNameListStore.LegacyFileName));
                var lists = BagCleanupNameListsDocument.FromSettings(settings.Maintenance);
                if (firstMigration || !string.IsNullOrWhiteSpace(account.BagCleanupNameListPath))
                {
                    var loaded = await new JsonBagCleanupNameListStore(path).LoadAsync(token).ConfigureAwait(false);
                    if (!loaded.Success) throw new InvalidDataException(account.AccountName + " 原名单读取失败，未迁移：" + loaded.Error);
                    lists = loaded.Value?.Document ?? lists;
                }
                data.Merge(account.Region, lists, monsters);
                data.MigratedAccounts.Add(Key(account));
            }
            await File.WriteAllTextAsync(Path.Combine(backup, "migration.txt"),
                "公共名单合并；拍卖行按区合并。同名物品优先自动查价（弹窗最低价优先），全部手动时取最高价。\n" +
                string.Join("\n", pending.Select(a => a.AccountName + " -> " + a.Region)), token).ConfigureAwait(false);
            return true;
        }, token).ConfigureAwait(false);
        if (!File.Exists(marker))
        {
            try { await AtomicJsonFile.WriteAsync(marker, new { version = 1 }, SharedAccountConfigurationStore.Json, token).ConfigureAwait(false); }
            catch (IOException) when (File.Exists(marker)) { /* Another initializing console committed the same marker. */ }
        }
    }

    private static string Key(AccountConfig account) => string.IsNullOrWhiteSpace(account.InstanceId) ? account.AccountName : account.InstanceId;
}
