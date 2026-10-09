using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Roadhog.Application.Trading;
using Roadhog.Core.Api;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Common;
using Roadhog.Infrastructure.Config;
using Roadhog.Infrastructure.Hardware;
using Roadhog.Infrastructure.Input;
using Roadhog.Infrastructure.Vmm;

// Explicit, bounded live diagnostic. Uses the production snapshots and input sequence.
// It never saves account settings or starts combat after the test.
internal static class StandaloneShopLiveProbe
{
    public static async Task<int> RunAsync(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        string Option(string prefix, string? fallback = null) => args.SingleOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..]
            ?? fallback ?? throw new ArgumentException("Missing " + prefix);
        var root = Path.GetFullPath(Option("--root="));
        var character = Option("--character=");
        var execute = args.Contains("--execute");
        var textOnly = args.Contains("--text-only");
        var config = (await new JsonAccountConfigStore(Path.Combine(root, "config/accounts.json")).LoadAllAsync()).Value!
            .Single(a => a.CharacterName == character);
        var discount = int.Parse(Option("--discount=", config.ScriptSettings!.Maintenance.CleanupWorkflow.StandaloneShopDiscount.ToString()));
        var expected = PersonalShopAdvertisement.Text(discount);
        var maxItems = int.Parse(Option("--max-items=", "1"));
        if (maxItems is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(maxItems));
        var hardware = new WindowsHardwareDeviceResolverOptions();
        hardware.VmmDeviceByHardwareKey[config.HardwareKey] = config.VmmDeviceName;
        var binding = new WindowsHardwareDeviceResolver(hardware).BindByKey(config.AccountName, config.HardwareKey);
        if (!binding.Success || binding.Value!.VmmDeviceName != config.VmmDeviceName) throw new Exception("Hardware binding mismatch.");
        var self = Process.GetCurrentProcess();
        var leases = new DeviceLeaseStore();
        var lease = leases.TryAcquire(self.Id, self.StartTime.ToUniversalTime(), root, config.HardwareKey, config.VmmDeviceName);
        if (!lease.Success) throw new Exception("Account device is occupied: " + lease.Error);
        var logger = new InMemoryRoadhogLogger();
        var api = new AionVmmGameApi(new() { DefaultVmmDeviceName = config.VmmDeviceName, MemProcFsHome = root }, logger);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(110));
        var readContext = new GameApiReadContext("standalone-shop-live-probe", config.ProcessId, "Aion.bin", config.VmmDeviceName, true);
        try
        {
            var player = await api.ReadPlayerAsync(readContext, stop.Token);
            if (!player.Success || player.Value is null || player.Value.IsDead || player.Value.CharacterName != character)
                throw new Exception("Expected live character not verified: " + player.Error);
            Print("identity", new { config.AccountName, player.Value.CharacterName, config.VmmDeviceName, player.Value.Position });
            // Fail promptly in this raw diagnostic before a provider cold-start wait.
            var rawUi = await api.ReadPersonalShopAsync(readContext, stop.Token);
            if (!rawUi.Success) throw new Exception("Personal shop read: " + rawUi.Error);
            Print("baseline", rawUi.Value);
            if (args.Contains("--inspect-ad"))
            {
                var connections = (System.Collections.IDictionary)typeof(AionVmmGameApi).GetField("_connections", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(api)!;
                var connection = connections.Values.Cast<VmmConnection>().Single();
                var process = connection.Vmm.Process("Aion.bin");
                var gameBase = process.GetModuleBase("Game.dll");
                var memory = new PersonalShopLiveProbe.Memory((a, n) => process.MemRead(a, (uint)n, 1), gameBase);
                var ad = memory.Nodes(memory.U(gameBase + 0xD63ED0)).Single(n => n.Name == "ad_editbox");
                var getter = memory.U(memory.U(ad.Address) + 632);
                var code = memory.Bytes(getter, 24);
                var offset = BitConverter.ToInt32(code, 3);
                Print("ad_widget", new { widget = memory.Widget(ad.Address), getterRva = $"0x{getter - gameBase:X}", code = Convert.ToHexString(code),
                    data = offset is >= 0 and <= 8192 ? Convert.ToHexString(memory.Bytes(ad.Address + (ulong)offset, 32)) : null });
            }
            var shared = new SharedAccountConfigurationStore(Path.Combine(root, "config", SharedAccountConfigurationStore.DefaultFileName), config.Region);
            var names = await shared.LoadAsync(stop.Token);
            if (!names.Success) throw new Exception(names.Error);
            names.Value!.Document!.ApplyTo(config.ScriptSettings!.Maintenance);
            config.ScriptSettings!.Maintenance.CleanupWorkflow.StandaloneShopDiscount = discount;
            var snapshots = new RoadhogSnapshotReaderFactory(api).Create(config, logger, stop.Token);
            var inventory = (await snapshots.ReadInventoryAsync()).Value;
            var plan = DiscountedPersonalShopWorkflow.Plan(inventory, config.ScriptSettings!.Maintenance, s => Print("progress", s)).Take(maxItems).ToArray();
            Print("plan", plan.Select(p => new { p.Item.Name, p.Item.InstanceId, p.Item.TemplateId, p.Item.Count, p.Item.VendorSellUnitPrice, p.UnitPrice }));
            if (!execute) return 0;
            var kmBox = config.KmBox ?? throw new Exception("Account has no KMBox input configuration.");
            using var input = new KmBoxNetKeyboardInput(new() { IpAddress = kmBox.IpAddress, Port = kmBox.Port, Mac = kmBox.Mac });
            async Task Feedback()
            {
                var feedback = (await snapshots.ReadPersonalShopAsync()).Value;
                Print("input_feedback", new { feedback.AdvertisementText, feedback.IsOpen, feedback.IsSelling, feedback.Editor });
            }
            var tracedInput = new TracedInput(input, args.Contains("--input-feedback") ? Feedback : null);
            var actions = new TradingActions(tracedInput, snapshots, stop.Token);
            async Task<Roadhog.Core.Model.PersonalShopSnapshot> Ui() => (await snapshots.ReadPersonalShopAsync()).Value;
            try
            {
                await actions.Reset();
                if (textOnly)
                {
                    var ui = await Ui();
                    TradingActions.Require(!ui.IsSelling && ui.Listings.Count == 0 && ui.Editor == null && !ui.OtherModalOpen, "Text test requires idle empty shop.");
                    if (!ui.IsOpen)
                    {
                        await actions.Key("Y");
                        await Task.Delay(350, stop.Token);
                        var opened = await api.ReadPersonalShopAsync(readContext, stop.Token);
                        if (!opened.Success) throw new Exception("Opened shop read: " + opened.Error);
                        await actions.Wait(Ui, s => s.IsOpen);
                    }
                    await PersonalShopAdvertisement.EnsureAsync(actions, Ui, discount, s => Print("progress", s));
                    Print("text_verified", await Ui());
                    return 0;
                }
                if (plan.Length == 0) throw new Exception("No configured discounted stall candidates.");
                var verifiedSelling = false;
                var run = new ConfiguredPersonalShopSequence(tracedInput).RunAsync(snapshots, plan, s => Print("progress", s), stop.Token, discount);
                try
                {
                    while (!run.IsCompleted)
                    {
                        var ui = await Ui();
                        if (ui.IsSelling)
                        {
                            TradingActions.Require(ui.AdvertisementText == expected && ui.Listings.Count == plan.Length &&
                                plan.All(p => ui.Listings.Contains(new((uint)p.Item.InstanceId, p.Item.TemplateId, p.Item.Count, p.UnitPrice))),
                                "Selling UI differs from discount text or registered plan.");
                            Print("selling_verified", ui);
                            verifiedSelling = true;
                            // The actual stall remains selling. End only this diagnostic's wait.
                            stop.Cancel();
                            break;
                        }
                        await Task.Delay(100, stop.Token);
                    }
                    await run;
                    Print("sold_out_verified", new { discount, items = plan.Length });
                }
                catch (OperationCanceledException) when (verifiedSelling) { }
                finally { stop.Cancel(); try { await run; } catch (OperationCanceledException) { } }
                return 0;
            }
            finally { await input.ReleaseAllAsync(CancellationToken.None); }
        }
        catch (Exception ex) { Print("probe_failed", new { error = ex.ToString() }); return 1; }
        finally
        {
            foreach (var entry in logger.Entries.Where(e => e.EventName.StartsWith("snapshot.read"))) Print("provider_log", entry);
            try
            {
                var connections = (System.Collections.IDictionary)typeof(AionVmmGameApi).GetField("_connections", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(api)!;
                foreach (VmmConnection connection in connections.Values) connection.Dispose();
            }
            finally { leases.Release(self.Id, self.StartTime.ToUniversalTime()); }
        }
    }

    private static void Print(string e, object? value) => Console.WriteLine(JsonSerializer.Serialize(new
    { at = DateTimeOffset.UtcNow, e, value }, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));

    private sealed class TracedInput(KmBoxNetKeyboardInput input, Func<Task>? feedback) : IKeyboardInput, IInputStateReset
    {
        private async Task<OperationResult> Record(string operation, object? target, Task<OperationResult> task)
        {
            var result = await task; Print("input", new { operation, target, result.Success, result.Error });
            if (feedback != null && operation is "press" or "key_down" or "key_up") await feedback();
            return result;
        }
        public Task<OperationResult> PressKeyAsync(string key, TimeSpan holdDuration, CancellationToken cancellationToken = default) =>
            Record("press", key, input.PressKeyAsync(key, holdDuration, cancellationToken));
        public Task<OperationResult> KeyDownAsync(string key, CancellationToken cancellationToken = default) =>
            Record("key_down", key, input.KeyDownAsync(key, cancellationToken));
        public Task<OperationResult> KeyUpAsync(string key, CancellationToken cancellationToken = default) =>
            Record("key_up", key, input.KeyUpAsync(key, cancellationToken));
        public Task<OperationResult> MouseDownAsync(RoadhogMouseButton button, CancellationToken cancellationToken = default) =>
            Record("mouse_down", button.ToString(), input.MouseDownAsync(button, cancellationToken));
        public Task<OperationResult> MouseUpAsync(RoadhogMouseButton button, CancellationToken cancellationToken = default) =>
            Record("mouse_up", button.ToString(), input.MouseUpAsync(button, cancellationToken));
        public Task<OperationResult> MoveMouseRelativeAsync(int deltaX, int deltaY, CancellationToken cancellationToken = default) =>
            Record("move", new { deltaX, deltaY }, input.MoveMouseRelativeAsync(deltaX, deltaY, cancellationToken));
        public Task<OperationResult> ScrollMouseAsync(int wheelDelta, CancellationToken cancellationToken = default) =>
            Record("scroll", wheelDelta, input.ScrollMouseAsync(wheelDelta, cancellationToken));
        public Task<OperationResult> ReleaseAllAsync(CancellationToken cancellationToken = default) =>
            Record("release_all", null, input.ReleaseAllAsync(cancellationToken));
    }
}
