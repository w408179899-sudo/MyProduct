using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Roadhog.Application.Trading;
using Roadhog.Core.Api;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Config;
using Roadhog.Infrastructure.Hardware;
using Roadhog.Infrastructure.Input;
using Roadhog.Infrastructure.Vmm;

// Explicitly invoked diagnostic; holds only the selected account's device lease.
internal static class ManastoneExchangeLiveProbe
{
    public static async Task<int> RunAsync(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        string? Option(string prefix) => args.SingleOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
        var root = Path.GetFullPath(Option("--root=") ?? throw new ArgumentException("--root required"));
        var character = Option("--character=") ?? throw new ArgumentException("--character required");
        var config = (await new JsonAccountConfigStore(Path.Combine(root, "config/accounts.json")).LoadAllAsync()).Value!
            .Single(a => a.CharacterName == character);
        var hardware = new WindowsHardwareDeviceResolverOptions();
        hardware.VmmDeviceByHardwareKey[config.HardwareKey] = config.VmmDeviceName;
        var binding = new WindowsHardwareDeviceResolver(hardware).BindByKey(config.AccountName, config.HardwareKey);
        if (!binding.Success || binding.Value!.VmmDeviceName != config.VmmDeviceName) throw new Exception("Hardware binding mismatch.");
        using var self = Process.GetCurrentProcess();
        var leases = new DeviceLeaseStore();
        var lease = leases.TryAcquire(self.Id, self.StartTime.ToUniversalTime(), root, config.HardwareKey, config.VmmDeviceName);
        if (!lease.Success) throw new Exception("Account device occupied: " + lease.Error);
        var logger = new InMemoryRoadhogLogger();
        var api = new AionVmmGameApi(new() { DefaultVmmDeviceName = config.VmmDeviceName, MemProcFsHome = root }, logger);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(args.Contains("--execute") ? 1200 : 60));
        try
        {
            var context = new GameApiReadContext("manastone-exchange-probe", config.ProcessId, "Aion.bin", config.VmmDeviceName, true);
            var player = await api.ReadPlayerAsync(context, stop.Token);
            if (!player.Success || player.Value is null || player.Value.IsDead || player.Value.CharacterName != character)
                throw new Exception("Expected character not verified: " + player.Error);
            Print("identity", new { config.AccountName, config.InstanceId, config.VmmDeviceName, player.Value.CharacterName, player.Value.TargetEntityId });
            var snapshots = new RoadhogSnapshotReaderFactory(api).Create(config, logger, stop.Token);
            Print("target", (await snapshots.ReadLockedTargetAsync()).Value);
            Print("inventory", (await snapshots.ReadInventoryAsync()).Value.Where(i => i.Name.Contains("魔石")).ToArray());
            var initial = await api.ReadNpcTradeAsync(context, stop.Token);
            if (!initial.Success) throw new Exception("NPC diagnostic: " + initial.Error);
            Print("before", initial.Value);
            if (args.Contains("--exchange-ui") || args.Contains("--execute"))
            {
                var official = await api.ReadManastoneExchangeAsync(context, stop.Token);
                Print("exchange_ui", official);
                if (!official.Success) throw new Exception(official.Error);
            }
            if (args.Contains("--execute"))
            {
                var box = config.KmBox ?? throw new Exception("Input configuration missing.");
                using var input = new KmBoxNetKeyboardInput(new() { IpAddress = box.IpAddress, Port = box.Port, Mac = box.Mac });
                var result = await new Roadhog.Application.ManastoneExchange.ManastoneExchangeSequence(input, logger)
                    .RunAsync(snapshots, config.AccountName, new Progress<string>(s => Print("progress", s)), stop.Token,
                        int.Parse(Option("--max-completions=") ?? "2147483647"));
                Print("result", result);
                Print("inventory_after", (await snapshots.ReadInventoryAsync()).Value.Where(i => i.Name.Contains("魔石")).ToArray());
                foreach (var entry in logger.Entries.Where(e => e.EventName.StartsWith("manastone_exchange"))) Print("event", entry);
                return result.Success ? 0 : 1;
            }
            if (args.Contains("--talk") || Option("--click-text=") != null)
            {
                var box = config.KmBox ?? throw new Exception("Input configuration missing.");
                using var input = new KmBoxNetKeyboardInput(new() { IpAddress = box.IpAddress, Port = box.Port, Mac = box.Mac });
                try
                {
                    var actions = new TradingActions(input, snapshots, stop.Token);
                    if (args.Contains("--talk")) await actions.Key("C");
                    if (Option("--click-text=") is { } text)
                    {
                        async Task<NpcTradeSnapshot> Ui() => (await snapshots.ReadNpcTradeAsync()).Value;
                        await actions.Click(Ui, s => s.DialogEntries.GetValueOrDefault(text), s => s.DialogOpen && !s.OtherModalOpen);
                    }
                    await Task.Delay(650, stop.Token);
                }
                finally { await input.ReleaseAllAsync(CancellationToken.None); }
            }
            Print("after", (await api.ReadNpcTradeAsync(context, stop.Token)).Value);
            var connections = (System.Collections.IDictionary)typeof(AionVmmGameApi).GetField("_connections", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(api)!;
            var connection = connections.Values.Cast<VmmConnection>().Single();
            var process = connection.Vmm.Process("Aion.bin");
            var gameBase = process.GetModuleBase("Game.dll");
            var mem = new PersonalShopLiveProbe.Memory((a, n) => process.MemRead(a, (uint)n, 1), gameBase);
            Print("inventory_after", (await snapshots.ReadInventoryAsync()).Value.Where(i => i.Name.Contains("魔石")).ToArray());
            for (var slot = 0; slot < 8; slot++)
            {
                var record = gameBase + 0x938B0C + (ulong)slot * 10080;
                var questId = mem.I(record);
                if (questId is 4113 or 4114)
                    Print("quest_cache", new { questId, slot, fields = Enumerable.Range(0, 10).Select(i => mem.I(record + 248 + (ulong)i * 4)).ToArray() });
            }
            var decoder = new InventoryInteractionDecoder(mem.Bytes);
            var textMethod = typeof(InventoryInteractionDecoder).GetMethod("AuctionText", BindingFlags.Instance | BindingFlags.NonPublic)!;
            foreach (var id in new[] { 132, 133 }.Concat(Enumerable.Range(336, 35)))
            {
                var address = mem.U(gameBase + 0xD63990 + (ulong)id * 8);
                if (address == 0 || (mem.U(address + 0x28) & 1) == 0) continue;
                Print("dialog", new { id, widget = mem.Widget(address) });
                if (id == 132)
                {
                    Print("dialog_state", new { npc = mem.I(address + 0x4E8), type = mem.I(address + 0x518), page = mem.I(address + 0x51C), quest = mem.I(address + 0x520) });
                    var wide = typeof(InventoryInteractionDecoder).GetMethod("AuctionWide", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    foreach (var offset in new ulong[] { 0x528, 0x548, 0x568, 0x588, 0xD68, 0xD88 })
                        Print("page_field", new { offset = $"0x{offset:X}", text = wide.Invoke(decoder, new object[] { address + offset }) });
                }
                foreach (var node in mem.Nodes(address))
                {
                    string text;
                    try { text = (string)textMethod.Invoke(decoder, new object[] { node.Address })!; }
                    catch (Exception ex) { text = "<" + (ex.InnerException ?? ex).Message + ">"; }
                    Print("node", new { node.Name, address = $"0x{node.Address:X}", node.X, node.Y, node.W, node.H, flags = mem.U(node.Address + 0x28), text });
                }
            }
            return 0;
        }
        catch (Exception ex) { Print("failed", ex.ToString()); return 1; }
        finally
        {
            var connections = (System.Collections.IDictionary)typeof(AionVmmGameApi).GetField("_connections", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(api)!;
            foreach (VmmConnection connection in connections.Values) connection.Dispose();
            leases.Release(self.Id, self.StartTime.ToUniversalTime());
        }
    }

    private static void Print(string e, object? value) => Console.WriteLine(JsonSerializer.Serialize(new { e, value },
        new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
}
