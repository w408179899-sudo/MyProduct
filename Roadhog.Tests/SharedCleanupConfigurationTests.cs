using Roadhog.Application;
using Roadhog.Application.Workers;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Config;
using Roadhog.Infrastructure.Profiles;

internal static class SharedCleanupConfigurationTests
{
    public static async Task MigrationAsync()
    {
        using var fixture = new Fixture();
        var accounts = new List<AccountConfig>();
        for (var n = 1; n <= 6; n++)
        {
            var account = new AccountConfig { InstanceId = Guid.NewGuid().ToString(), AccountName = "脚本" + n,
                BagCleanupNameListPath = $"preserved/{n}/bag-cleanup-name-lists.json", ScriptSettings = new() };
            account.ScriptSettings.Combat.ActiveMonsterNameFilters = new() { "monster" + n, " common " };
            account.ScriptSettings.Maintenance.BagCleanupExcludedItemNames.Add("stale-embedded");
            accounts.Add(account);
            var saved = await new JsonBagCleanupNameListStore(Path.Combine(fixture.Root, account.BagCleanupNameListPath)).SaveAsync(new()
            {
                Whitelist = new() { "keep" + n, " common " }, Blacklist = new() { "discard" + n }, Sell = new() { "sell" + n },
                Stall = new() { new() { Name = " same ", UnitPrice = n * 10 } },
                AuctionHouse = new() { new() { Name = "same", UnitPrice = n * 100 },
                    new() { Name = "method", UnitPrice = 99999, PriceLookupMethod = n is 2 or 5 ? AuctionPriceLookupMethod.DialogMinimum : n == 3 ? AuctionPriceLookupMethod.SearchCalculation : AuctionPriceLookupMethod.Manual } }
            });
            Require(saved.Success, "legacy source saved");
        }
        await new JsonAccountConfigStore(fixture.AccountsPath).SaveAllAsync(accounts);
        await SharedCleanupMigration.MigrateAsync(fixture.AccountsPath, accounts, fixture.Profiles);
        Require(accounts.Take(4).All(a => a.Region == "一区") && accounts.Skip(4).All(a => a.Region == "六区"), "exact 1234/56 region assignment");
        var one = fixture.Store("一区"); var six = fixture.Store("六区");
        var first = await Lists(one); var second = await Lists(six);
        Require(first.Whitelist.Count == 7 && !first.Whitelist.Contains("stale-embedded") && first.Blacklist.Count == 6 && first.Sell.Count == 6, "authoritative legacy lists union and dedup, without stale embedded resurrection");
        Require(first.Stall.Single().UnitPrice == 60 && second.Stall.Single().UnitPrice == 60, "stall max price across all accounts");
        Require(first.AuctionHouse.Single(i => i.Name == "same").UnitPrice == 400 && second.AuctionHouse.Single(i => i.Name == "same").UnitPrice == 600, "auction max only inside each region");
        Require(first.AuctionHouse.Single(i => i.Name == "method").PriceLookupMethod == AuctionPriceLookupMethod.DialogMinimum &&
            second.AuctionHouse.Single(i => i.Name == "method").PriceLookupMethod == AuctionPriceLookupMethod.DialogMinimum, "automatic lookup beats manual, without cross-region method leakage");
        Require((await one.LoadMonsterFiltersAsync()).Value!.Count == 7, "all monster filters shared");
        Require(Directory.GetFiles(Path.Combine(fixture.Root, "shared-cleanup-backups"), "*", SearchOption.AllDirectories).Length >= 8, "source backups created before migration");
        var cleared = first.Clone(); cleared.Blacklist.Clear();
        Require((await one.SaveChangesAsync(first, cleared)).Success, "clear saved");
        await SharedCleanupMigration.MigrateAsync(fixture.AccountsPath, accounts, fixture.Profiles);
        Require((await Lists(six)).Blacklist.Count == 0, "restart never reimports deleted rules");
        var startup = await new AccountStartConfigBuilder(new JsonScriptProfileStore(fixture.Profiles), six, NoOpRoadhogLogger.Instance).BuildAsync(accounts[5]);
        Require(startup.Success && startup.Value!.Region == "六区" && startup.Value.ScriptSettings!.Maintenance.BagCleanupAuctionHouseItems.Single(i => i.Name == "same").UnitPrice == 600,
            "startup overrides legacy private path with correct shared region");
        Require(startup.Value!.ScriptSettings!.Combat.ActiveMonsterNameFilters.Count == 7, "startup applies shared monster filters");
        Require(accounts[0].Clone().Region == "一区", "account cloning preserves region");
        File.Delete(one.FilePath);
        bool missingRejected = false;
        try { await SharedCleanupMigration.MigrateAsync(fixture.AccountsPath, accounts, fixture.Profiles); }
        catch (InvalidDataException) { missingRejected = true; }
        Require(missingRejected && !File.Exists(one.FilePath), "missing migrated file cannot silently resurrect legacy rules");
    }

    public static async Task ConcurrentEditsAsync()
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        var one = fixture.Store("一区"); var six = fixture.Store("六区");
        var before = await Lists(one);
        var a = before.Clone(); a.Sell.Add("a"); a.AuctionHouse.Add(new() { Name = "gem", UnitPrice = 10 });
        var b = before.Clone(); b.Sell.Add("b"); b.AuctionHouse.Add(new() { Name = "gem", UnitPrice = 20 });
        var results = await Task.WhenAll(one.SaveChangesAsync(before, a), six.SaveChangesAsync(before, b));
        Require(results.All(r => r.Success), "concurrent writes succeed");
        Require((await Lists(one)).Sell.Order().SequenceEqual(new[] { "a", "b" }), "concurrent additions to same public list survive");
        Require((await Lists(one)).AuctionHouse.Single().UnitPrice == 10 && (await Lists(six)).AuctionHouse.Single().UnitPrice == 20, "concurrent region edits remain isolated");
        var stale = await Lists(six); var edited = stale.Clone(); edited.Blacklist.Add("new-discard");
        var current = await Lists(one); var remove = current.Clone(); remove.Sell.Remove("a");
        Require((await one.SaveChangesAsync(current, remove)).Success && (await six.SaveChangesAsync(stale, edited)).Success, "independent edits save");
        Require((await Lists(one)).Sell.SequenceEqual(new[] { "b" }), "stale unrelated edit cannot resurrect removed entry");
        var lower = await Lists(one); var lowered = lower.Clone(); lowered.AuctionHouse.Single().UnitPrice = 3;
        Require((await one.SaveChangesAsync(lower, lowered)).Success && (await Lists(one)).AuctionHouse.Single().UnitPrice == 3, "max price is migration-only, normal edits can lower price");
        var writes = Enumerable.Range(0, 12).Select(n => Task.Run(async () =>
        {
            var store = fixture.Store(n % 2 == 0 ? "一区" : "六区");
            var original = await Lists(store); var changed = original.Clone(); changed.Whitelist.Add("writer" + n);
            Require((await store.SaveChangesAsync(original, changed)).Success, "parallel writer saved");
            Require((await store.LoadAsync()).Success, "atomic reader never sees partial JSON");
        }));
        await Task.WhenAll(writes);
        Require((await Lists(one)).Whitelist.Count == 12, "all concurrent additions survive");
        await File.WriteAllTextAsync(one.FilePath, "{broken");
        Require(!(await one.LoadAsync()).Success && !(await one.SaveChangesAsync(before, a)).Success, "corrupt shared config fails without fallback or overwrite");
        Require(await File.ReadAllTextAsync(one.FilePath) == "{broken", "failed edit preserves corrupt file for diagnosis");
        await File.WriteAllTextAsync(one.FilePath, "{\"version\":1}");
        Require(!(await one.LoadAsync()).Success, "missing required sections cannot silently become empty lists");
    }

    public static async Task RefreshAndFreezeAsync()
    {
        using var fixture = new Fixture(); await fixture.Initialize(); var store = fixture.Store("一区");
        var settings = new ScriptSettings(); var request = new CleanupRequest(settings.Clone(), true);
        await SharedConfigurationRefresh.CaptureCleanupAsync(store, request);
        var before = await Lists(store); var after = before.Clone(); after.Sell.Add("next-round");
        await store.SaveChangesAsync(before, after);
        await store.SaveMonsterFiltersAsync(Array.Empty<string>(), new[] { "shared-monster" });
        await SharedConfigurationRefresh.RefreshAsync(store, settings, includeCleanup: false);
        Require(settings.Combat.ActiveMonsterNameFilters.Single() == "shared-monster", "running account refreshes monster filter");
        await SharedConfigurationRefresh.CaptureCleanupAsync(store, request);
        Require(request.Settings.Maintenance.BagCleanupSellItemNameKeywords.Count == 0, "retry of same cleanup keeps frozen rules");
        var next = new CleanupRequest(settings.Clone(), true);
        await SharedConfigurationRefresh.CaptureCleanupAsync(store, next);
        Require(next.Settings.Maintenance.BagCleanupSellItemNameKeywords.Single() == "next-round", "next cleanup gets latest list");
        await SharedConfigurationRefresh.RefreshAsync(store, settings, includeCleanup: true);
        Require(settings.Maintenance.BagCleanupSellItemNameKeywords.Single() == "next-round", "automatic candidate check refreshes lists too");
        var six = fixture.Store("六区");
        await six.SaveMonsterFiltersAsync(new[] { "shared-monster" }, Array.Empty<string>());
        await SharedConfigurationRefresh.RefreshAsync(store, settings, false);
        Require(settings.Combat.ActiveMonsterNameFilters.Count == 0, "shared clear propagates across regions");
    }

    public static Task PriorityAsync()
    {
        var settings = new MaintenanceScriptSettings
        {
            BagCleanupRules = new() { new() { Key = BagCleanupRuleCatalog.WhiteEquipment, Enabled = true, Action = BagCleanupAction.Discard } },
            BagCleanupExcludedItemNames = new() { "safe" }, BagCleanupDiscardItemNameKeywords = new() { "trash" },
            BagCleanupSellItemNameKeywords = new() { "sale" },
            BagCleanupAuctionHouseItems = new() { new() { Name = "sale", UnitPrice = 10 } },
            BagCleanupStallItems = new() { new() { Name = "sale", UnitPrice = 1 } }
        };
        var items = new[]
        {
            new InventoryItemSnapshot(1, 1, "trash sale", 1, 0, false, 0, 0),
            new InventoryItemSnapshot(2, 2, "sale white sword", 1, 1, false, 1, 1),
            new InventoryItemSnapshot(3, 3, "safe trash sale", 1, 2, false, 0, 0),
            new InventoryItemSnapshot(4, 4, "SALE misc", 1, 3, false, 0, 0),
            new InventoryItemSnapshot(5, 5, "sale equipped", 1, 4, true, 1, 1)
        };
        var discard = BagCleanupItemMatcher.SelectDiscardItems(items, settings);
        Require(discard.Select(i => i.InstanceId).SequenceEqual(new ulong[] { 1, 2 }), "discard wins over sale, auction and stall");
        var sell = BagCleanupItemMatcher.SelectSellRegistrationItems(items, settings);
        Require(sell.Select(i => i.InstanceId).SequenceEqual(new ulong[] { 3, 4 }), "name-only sale works, whitelist only protects from discard");
        var remainingAfterCap = items.Except(discard).Where(i => !i.IsEquipped).ToArray();
        Require(remainingAfterCap.All(i => CleanupTradePolicy.Rule(i, settings, true) != null), "unsold items can proceed to auction");
        Require(remainingAfterCap.All(i => CleanupTradePolicy.Rule(i, settings, false) != null), "remaining auction matches can proceed to stall");
        Require(settings.Clone().BagCleanupSellItemNameKeywords.Single() == "sale", "sale list clones independently");
        return Task.CompletedTask;
    }

    public static async Task FailedMigrationAsync()
    {
        using var fixture = new Fixture();
        var account = new AccountConfig { AccountName = "脚本1", BagCleanupNameListPath = "bad.json" };
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "bad.json"), "{broken");
        bool failed = false;
        try { await SharedCleanupMigration.MigrateAsync(fixture.AccountsPath, new[] { account }, fixture.Profiles); }
        catch (InvalidDataException) { failed = true; }
        Require(failed && !File.Exists(fixture.Store("一区").FilePath), "failed migration never publishes a partial shared document");
    }

    public static async Task WorkerRefreshAsync()
    {
        using var fixture = new Fixture(); await fixture.Initialize(); var store = fixture.Store("一区");
        await store.SaveMonsterFiltersAsync(Array.Empty<string>(), new[] { "first-monster" });
        var api = new FakeGameApi { TargetEntityId = 0 };
        var input = new RecordingKeyboardInput(); var logger = new InMemoryRoadhogLogger();
        var config = new AccountConfig { AccountName = "shared-worker", Region = "一区", MainMode = AccountMainMode.SemiAuto,
            ScriptSettings = new() { MainMode = AccountMainMode.SemiAuto } };
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var context = new AccountWorkerContext(config, api, logger, new AccountRuntimeManager(logger), new() { TickInterval = TimeSpan.FromMilliseconds(5) }, stop.Token);
        var semi = new SemiAutoCombatController(input);
        var loop = new DefaultAccountWorkerLoop(input, semi, new StationaryCombatController(input, semi), sharedConfigurationFactory: _ => store);
        var work = loop.RunAsync(context);
        async Task WaitFor(string expected)
        {
            while (!context.Config.ScriptSettings!.Combat.ActiveMonsterNameFilters.Contains(expected))
            { if (work.IsCompleted) await work; await Task.Delay(10, stop.Token); }
        }
        try
        {
            await WaitFor("first-monster");
            await store.SaveMonsterFiltersAsync(new[] { "first-monster" }, new[] { "next-monster" });
            await WaitFor("next-monster");
            Require(!work.IsCompleted && !context.Config.ScriptSettings!.Combat.ActiveMonsterNameFilters.Contains("first-monster"), "live worker adopts changed filters without restart");
        }
        finally { stop.Cancel(); try { await work; } catch (OperationCanceledException) when (stop.IsCancellationRequested) { } }
    }

    private static async Task<BagCleanupNameListsDocument> Lists(IBagCleanupNameListStore store)
    {
        var result = await store.LoadAsync(); Require(result.Success && result.Value?.Document != null, "load lists: " + result.Error);
        return result.Value!.Document!;
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "Roadhog-shared-cleanup-" + Guid.NewGuid().ToString("N"));
        public string AccountsPath => Path.Combine(Root, "accounts.json");
        public string Profiles => Path.Combine(Root, "profiles");
        public Fixture() => Directory.CreateDirectory(Root);
        public SharedAccountConfigurationStore Store(string region) => new(SharedAccountConfigurationStore.PathFor(AccountsPath), region);
        public Task Initialize() => SharedCleanupMigration.MigrateAsync(AccountsPath, Array.Empty<AccountConfig>(), Profiles);
        public void Dispose() => Directory.Delete(Root, true);
    }
}
