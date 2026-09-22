using System.Security.Cryptography;
using System.Text;
using Roadhog.Application;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Infrastructure.Config;
using Roadhog.Infrastructure.Profiles;

internal static class SharedCleanupMigrationAudit
{
    // Operates exclusively on a fresh copy; never starts a worker or touches a device.
    public static async Task RunAsync(string sourceConfig, string destination)
    {
        sourceConfig = Path.GetFullPath(sourceConfig); destination = Path.GetFullPath(destination);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new InvalidOperationException("审计输出目录必须不存在。");
        if (destination.StartsWith(sourceConfig + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("审计输出不能放在源配置目录内。");
        var sourceAccounts = Path.Combine(sourceConfig, "accounts.json");
        var loaded = await new JsonAccountConfigStore(sourceAccounts).LoadAllAsync();
        if (!loaded.Success || loaded.Value?.Count != 6) throw new InvalidDataException("审计要求完整的六账号配置。");
        var accounts = loaded.Value.Select(a => a.Clone()).ToArray();
        var hashes = new Dictionary<string, byte[]>();
        var configRoot = Path.Combine(destination, "config"); Directory.CreateDirectory(configRoot);
        void Copy(string source, string target)
        {
            if (!File.Exists(source)) return;
            var bytes = File.ReadAllBytes(source); hashes[source] = SHA256.HashData(bytes);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllBytes(target, bytes);
        }
        Copy(sourceAccounts, Path.Combine(destination, "source-accounts.json"));
        var profiles = Path.Combine(configRoot, "profiles");
        var originalProfiles = Path.Combine(sourceConfig, "profiles");
        if (Directory.Exists(originalProfiles))
            foreach (var path in Directory.EnumerateFiles(originalProfiles, "*.json")) Copy(path, Path.Combine(profiles, Path.GetFileName(path)));
        for (var i = 0; i < accounts.Length; i++)
        {
            var account = accounts[i]; account.Region = SharedCleanupMigration.InitialRegion(account);
            var source = string.IsNullOrWhiteSpace(account.BagCleanupNameListPath)
                ? Path.Combine(sourceConfig, JsonBagCleanupNameListStore.DefaultFileName) : Path.GetFullPath(account.BagCleanupNameListPath, sourceConfig);
            var relative = $"sources/{i + 1}/{JsonBagCleanupNameListStore.DefaultFileName}";
            var target = Path.Combine(configRoot, relative); Copy(source, target);
            Copy(Path.Combine(Path.GetDirectoryName(source)!, JsonBagCleanupNameListStore.LegacyFileName),
                Path.Combine(Path.GetDirectoryName(target)!, JsonBagCleanupNameListStore.LegacyFileName));
            account.BagCleanupNameListPath = relative;
        }
        var accountsPath = Path.Combine(configRoot, "accounts.json");
        var saved = await new JsonAccountConfigStore(accountsPath).SaveAllAsync(accounts);
        if (!saved.Success) throw new InvalidDataException(saved.Error);
        await SharedCleanupMigration.MigrateAsync(accountsPath, accounts, profiles);
        await new JsonAccountConfigStore(accountsPath).SaveAllAsync(accounts);
        var report = new StringBuilder("# 六账号共享配置迁移副本验证\n\n原始客户端未修改、未启动账号、未调用硬件。\n\n");
        var common = (await new SharedAccountConfigurationStore(SharedAccountConfigurationStore.PathFor(accountsPath), "一区").LoadAsync()).Value!.Document!;
        report.AppendLine($"公共名单：白名单 {common.Whitelist.Count}；黑名单 {common.Blacklist.Count}；出售 {common.Sell.Count}；摆摊 {common.Stall.Count}。");
        foreach (var region in accounts.GroupBy(a => a.Region))
        {
            var shared = new SharedAccountConfigurationStore(SharedAccountConfigurationStore.PathFor(accountsPath), region.Key);
            var lists = (await shared.LoadAsync()).Value!.Document!;
            report.AppendLine($"\n{region.Key}：{string.Join("、", region.Select(a => a.AccountName))}；拍卖行 {lists.AuctionHouse.Count} 项；自动查价 {lists.AuctionHouse.Count(i => i.PriceLookupMethod != AuctionPriceLookupMethod.Manual)} 项。");
            foreach (var account in region)
            {
                var effective = await new AccountStartConfigBuilder(new JsonScriptProfileStore(profiles), shared, NoOpRoadhogLogger.Instance).BuildAsync(account);
                if (!effective.Success || effective.Value?.ScriptSettings is null) throw new InvalidDataException("运行配置生成失败：" + effective.Error);
                if (!effective.Value.ScriptSettings.Maintenance.BagCleanupExcludedItemNames.SequenceEqual(common.Whitelist)) throw new InvalidDataException("公共名单不一致。");
                if (effective.Value.ScriptSettings.Maintenance.BagCleanupAuctionHouseItems.Count != lists.AuctionHouse.Count) throw new InvalidDataException("区服名单不一致。");
            }
        }
        if (accounts.Count(a => a.Region == "一区") != 4 || accounts.Count(a => a.Region == "六区") != 2) throw new InvalidDataException("区服分组不是 4 + 2。");
        var filters = await new SharedAccountConfigurationStore(SharedAccountConfigurationStore.PathFor(accountsPath), "一区").LoadMonsterFiltersAsync();
        report.AppendLine($"\n共享怪物过滤：{filters.Value!.Count} 项。\n\n合并规则：拍卖行自动查价优先；同为手动时取最高价；摆摊取最高价。\n");
        var before = await File.ReadAllBytesAsync(SharedAccountConfigurationStore.PathFor(accountsPath));
        await SharedCleanupMigration.MigrateAsync(accountsPath, accounts, profiles);
        if (!before.SequenceEqual(await File.ReadAllBytesAsync(SharedAccountConfigurationStore.PathFor(accountsPath)))) throw new InvalidDataException("重复迁移改变了结果。");
        foreach (var (path, hash) in hashes)
            if (!hash.SequenceEqual(SHA256.HashData(await File.ReadAllBytesAsync(path)))) throw new InvalidDataException("审计过程中源配置发生变化，请重试。");
        report.AppendLine($"验证通过：6 个账号均能生成共享配置；重复迁移不改变结果；{hashes.Count} 个源文件哈希保持一致。");
        await File.WriteAllTextAsync(Path.Combine(destination, "report.md"), report.ToString());
        Console.WriteLine(report.ToString());
    }
}
