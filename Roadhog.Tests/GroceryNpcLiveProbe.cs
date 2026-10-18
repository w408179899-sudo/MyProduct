using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Roadhog.Application;
using Roadhog.Application.BagCleanup;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Trading;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Infrastructure.Config;
using Roadhog.Infrastructure.Hardware;
using Roadhog.Infrastructure.Input;
using Roadhog.Infrastructure.Paths;
using Roadhog.Infrastructure.Vmm;
using Roadhog.Infrastructure.WorkerProcesses;

// Bounded, explicitly invoked test of the production NPC leg. No config saves or combat restart.
internal static class GroceryNpcLiveProbe
{
    public static async Task<int> RunAsync(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        string? Option(string prefix) => args.SingleOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
        var root = Path.GetFullPath(Option("--root=") ?? throw new ArgumentException("--root required"));
        var character = Option("--character=") ?? throw new ArgumentException("--character required");
        var config = (await new JsonAccountConfigStore(Path.Combine(root, "config/accounts.json")).LoadAllAsync()).Value!
            .Single(a => string.Equals(a.CharacterName, character, StringComparison.OrdinalIgnoreCase)).Clone();
        if (Option("--release-worker=") is { } specPath)
        {
            var spec = JsonSerializer.Deserialize<WorkerLaunchSpec>(await File.ReadAllTextAsync(specPath))!;
            if (spec.Account.InstanceId != config.InstanceId) throw new Exception("Worker identity differs from selected account.");
            var rpc = new WorkerRpcClient(spec.PipeName, spec.Token);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var status = await rpc.CallAsync<WorkerStatus>(WorkerCommands.Status, [], timeout.Token);
            Print("worker_status", new { config.AccountName, status.ProcessId, status.IsRunning });
            TradingActions.Require((await rpc.CallAsync<OperationResult>(WorkerCommands.Shutdown, [], timeout.Token)).Success, "Worker shutdown failed.");
            try { await Process.GetProcessById(status.ProcessId).WaitForExitAsync(timeout.Token); }
            catch (ArgumentException) { }
            Print("worker_released", new { status.ProcessId });
            return 0;
        }
        var self = Process.GetCurrentProcess();
        var leases = new DeviceLeaseStore();
        var lease = leases.TryAcquire(self.Id, self.StartTime.ToUniversalTime(), root, config.HardwareKey, config.VmmDeviceName);
        if (!lease.Success) throw new Exception("Selected account device is occupied: " + lease.Error);
        var logger = new InMemoryRoadhogLogger();
        var api = new AionVmmGameApi(new() { DefaultVmmDeviceName = config.VmmDeviceName, MemProcFsHome = root }, logger);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        try
        {
            var read = new GameApiReadContext("grocery-npc-live-probe", config.ProcessId, "Aion.bin", config.VmmDeviceName, true);
            var identity = await api.ReadPlayerAsync(read, stop.Token);
            TradingActions.Require(identity.Success && identity.Value is { IsDead: false } &&
                string.Equals(identity.Value.CharacterName, character, StringComparison.OrdinalIgnoreCase), "Live character identity unavailable or different.");
            Print("identity", new { config.AccountName, identity.Value!.CharacterName, identity.Value.Position });
            var shared = await new SharedAccountConfigurationStore(Path.Combine(root, "config", SharedAccountConfigurationStore.DefaultFileName), config.Region).LoadAsync(stop.Token);
            TradingActions.Require(shared.Success, "Shared cleanup rules could not be loaded.");
            shared.Value!.Document!.ApplyTo(config.ScriptSettings!.Maintenance);
            var settings = config.ScriptSettings!;
            settings.Paths.BagCleanupReturnItemName = Option("--return-item=") ?? settings.Paths.BagCleanupReturnItemName;
            settings.Maintenance.CleanupWorkflow.GroceryPostSaleAction = GroceryPostSaleAction.NpcSell;
            foreach (var name in new[] { settings.Paths.BagCleanupReturnItemName, settings.Paths.GroceryReturnItemName })
                if (!string.IsNullOrWhiteSpace(name)) settings.Maintenance.BagCleanupExcludedItemNames.Add(name);
            var paths = new JsonSharedPathStore(Path.Combine(root, "config/paths"));
            var loaded = await paths.LoadAsync(settings.Paths.MaintenancePathName, stop.Token);
            TradingActions.Require(loaded.Success && loaded.Value is { PointCount: > 0 }, "Cleanup route unavailable.");
            var route = loaded.Value!.Clone();
            if (Option("--map=") is { } map) route.MapId = uint.Parse(map);
            var testPaths = new InMemorySharedPathStore(route);
            var context = new AccountWorkerContext(config, new RoadhogSnapshotReaderFactory(api), logger, new AccountRuntimeManager(logger), new(), stop.Token)
                { WorkflowOwnsCleanup = true };
            var before = (await context.Snapshots.ReadInventoryAsync()).Value;
            var money = (await context.Snapshots.ReadInventoryMoneyAsync()).Value;
            var candidates = GroceryNpcSalePolicy.SelectCandidates(before, settings.Maintenance);
            Print("excluded_from_npc", BagCleanupItemMatcher.SelectDiscardItems(before, settings.Maintenance)
                .Where(i => !i.CanSellToNpc)
                .Select(i => new { i.Name, i.InstanceId, i.ItemType, i.CanSellToNpc }));
            Print("scene", (await context.Snapshots.ReadChannelTransitionAsync()).Value);
            Print("return_items", before.Where(i => i.Name == settings.Paths.BagCleanupReturnItemName)
                .Select(i => new { i.Name, i.InstanceId, i.Count, i.Slot }));
            Print("npc_ui", (await context.Snapshots.ReadNpcTradeAsync()).Value);
            Print("plan", new { route.Name, route.MapId, route.CleanupNpcName, settings.Paths.BagCleanupReturnItemName,
                money, items = candidates.Select(i => new { i.Name, i.InstanceId, i.TemplateId, i.Count, i.Slot, i.ItemType, i.CanSellToNpc, i.VendorSellUnitPrice }) });
            if (!args.Contains("--execute")) return 0;
            var box = config.KmBox ?? throw new Exception("Missing account input configuration.");
            using var input = new KmBoxNetKeyboardInput(new() { IpAddress = box.IpAddress, Port = box.Port, Mac = box.Mac });
            try
            {
                var movement = new StationaryCombatController(input, new SemiAutoCombatController(input), testPaths);
                if (args.Contains("--close-empty-npc"))
                {
                    var ui = (await context.Snapshots.ReadNpcTradeAsync()).Value;
                    TradingActions.Require(!ui.OtherModalOpen && ui.Basket.Count == 0, "Only an empty NPC window may be closed for this diagnostic.");
                    if (ui.IsOpen || ui.DialogOpen)
                    {
                        var actions = new TradingActions(input, context.Snapshots, stop.Token);
                        await actions.Key("Space");
                        await actions.Wait(async () => (await context.Snapshots.ReadNpcTradeAsync()).Value, s => !s.IsOpen && !s.DialogOpen);
                    }
                }
                if (args.Contains("--stage-from-route-end"))
                {
                    Print("staging", "Follow the known cleanup route before testing return from its end.");
                    TradingActions.Check(await movement.ExecutePathOnceAsync(context, route.Name, route.Points.Select(p => p.ToVector3()).ToArray()));
                }
                async Task NpcOnly()
                {
                    var npc = new BagCleanupNpcInteractor(input);
                    TradingActions.Check(await npc.SelectConfiguredNpcAsync(context, route.CleanupNpcName));
                    TradingActions.Check(await npc.OpenDialogAsync(context));
                    var state = new BagCleanupState(); state.Start(0, 0);
                    var outcome = await new NpcSaleSequence(input).RunAsync(context, route.CleanupNpcName, state,
                        bag => GroceryNpcSalePolicy.SelectCandidates(bag, settings.Maintenance), 3, registerAllJunkFirst: true);
                    TradingActions.Require(outcome == NpcSaleOutcome.AllConfiguredItemsSold, "NPC did not complete the sale.");
                    var actions = new TradingActions(input, context.Snapshots, stop.Token);
                    await actions.Key("Space");
                    await actions.Wait(async () => (await context.Snapshots.ReadNpcTradeAsync()).Value, s => !s.IsOpen && !s.DialogOpen);
                }
                var run = args.Contains("--npc-only") ? NpcOnly() :
                    new GroceryNpcSaleSequence(input, testPaths, movement.ExecutePathOnceAsync).RunAsync(context, s => Print("progress", s));
                try
                {
                    while (!run.IsCompleted)
                    {
                        await Task.WhenAny(run, Task.Delay(1000, stop.Token));
                        if (run.IsCompleted) break;
                        var scene = (await context.Snapshots.ReadChannelTransitionAsync()).Value;
                        Print("observation", new { scene.IsReady, map = scene.Channel?.MapId, scene.Player?.Position,
                            scrollCount = (await context.Snapshots.ReadInventoryAsync()).Value.Where(i => i.Name == settings.Paths.BagCleanupReturnItemName).Sum(i => (long)i.Count) });
                    }
                    await run;
                }
                finally { if (!run.IsCompleted) { stop.Cancel(); try { await run; } catch (OperationCanceledException) { } } }
                var after = (await context.Snapshots.ReadInventoryAsync()).Value;
                var remaining = GroceryNpcSalePolicy.SelectCandidates(after, settings.Maintenance);
                TradingActions.Require(remaining.Count == 0, "Discard candidates remain after NPC sale.");
                var sold = logger.Entries.Where(e => e.EventName == "bag_cleanup.sell.batch_verified")
                    .SelectMany(e => (ulong[])e.Fields!["instanceIds"]!).ToHashSet();
                TradingActions.Require(candidates.All(i => sold.Contains(i.InstanceId)), "Configured sale was not verified.");
                var preserved = before.Where(i => !sold.Contains(i.InstanceId) && i.Name != settings.Paths.BagCleanupReturnItemName);
                TradingActions.Require(preserved.All(i => after.Any(a => a.InstanceId == i.InstanceId && a.Count == i.Count)), "Unselected inventory changed.");
                Print("verified", new { soldEntries = sold.Count, beforeMoney = money, afterMoney = (await context.Snapshots.ReadInventoryMoneyAsync()).Value,
                    position = (await context.Snapshots.ReadPlayerAsync()).Value.Position, protectedEntries = preserved.Count(), remaining = remaining.Count });
                return 0;
            }
            finally { await input.ReleaseAllAsync(CancellationToken.None); }
        }
        catch (Exception ex) { Print("failed", ex.ToString()); return 1; }
        finally
        {
            foreach (var entry in logger.Entries.Where(e => e.EventName.StartsWith("bag_cleanup") || e.EventName.StartsWith("grocery_shop") || e.EventName.StartsWith("snapshot.read"))) Print("log", entry);
            try
            {
                var connections = (System.Collections.IDictionary)typeof(AionVmmGameApi).GetField("_connections", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(api)!;
                foreach (VmmConnection connection in connections.Values) connection.Dispose();
            }
            finally { leases.Release(self.Id, self.StartTime.ToUniversalTime()); }
        }
    }
    private static void Print(string e, object? value) => Console.WriteLine(JsonSerializer.Serialize(new { at = DateTimeOffset.UtcNow, e, value },
        new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
}
