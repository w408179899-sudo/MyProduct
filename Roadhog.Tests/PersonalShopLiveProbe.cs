using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Diagnostics;
using Roadhog.Infrastructure.Config;
using Roadhog.Infrastructure.Hardware;
using Roadhog.Infrastructure.Input;
using Roadhog.Infrastructure.Vmm;
// Raw, bounded hardware diagnostic only. It is deliberately outside runtime business APIs.
internal static class PersonalShopLiveProbe
{

    public static async Task<int> RunAsync(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            return await RunCoreAsync(args);
        }
        catch (Exception ex)
        {
            Print("probe_failed", new
            {
                error = ex.Message
            });
            return 1;
        }
    }

    private static async Task<int> RunCoreAsync(string[] args)
    {
        var root = Path.GetFullPath(RequiredOption(args, "--root="));
        var execute = args.Contains("--execute");
        var expectedCharacter = execute ? RequiredOption(args, "--character=") : null;
        if (execute && RequiredOption(args, "--unit-price=") != "1") throw new ArgumentException("This probe implements the requested one-coin test only.");
        var config = (await new JsonAccountConfigStore(Path.Combine(root, "config/accounts.json")).LoadAllAsync()).Value!.Single();
        var hardwareOptions = new WindowsHardwareDeviceResolverOptions();
        hardwareOptions.VmmDeviceByHardwareKey[config.HardwareKey] = config.VmmDeviceName;
        var binding = new WindowsHardwareDeviceResolver(hardwareOptions).BindByKey(config.AccountName, config.HardwareKey);
        if (!binding.Success || binding.Value!.VmmDeviceName != config.VmmDeviceName) throw new Exception("Hardware binding mismatch: " + JsonSerializer.Serialize(binding));
        var self = Process.GetCurrentProcess();
        var leases = new DeviceLeaseStore();
        var lease = leases.TryAcquire(self.Id, self.StartTime.ToUniversalTime(), root, config.HardwareKey, config.VmmDeviceName);
        if (!lease.Success) throw new Exception("Device occupied: " + JsonSerializer.Serialize(lease));
        var log = new InMemoryRoadhogLogger();
        var api = new AionVmmGameApi(new AionVmmGameApiOptions
        {
            DefaultVmmDeviceName = config.VmmDeviceName,
            MemProcFsHome = root
        }, log);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(110));
        try
        {
            var context = new GameApiReadContext("personal-shop-probe", 0, "Aion.bin", config.VmmDeviceName, true);
            var player = await api.ReadPlayerAsync(context, stop.Token);
            if (!player.Success || player.Value is null || player.Value.IsDead) throw new Exception("Player not ready " + player.Error);
            Print("identity", new
            {
                root,
                config.VmmDeviceName,
                player.Value.CharacterName,
                player.Value.EntityId,
                player.Value.CurrentHp,
                player.Value.Position
            });
            var connection = Connections(api).Single();
            var process = connection.Vmm.Process("Aion.bin");
            var b = process.GetModuleBase("Game.dll");
            var mem = new Memory((address, count) => process.MemRead(address, (uint)count, 1), b);
            Print("baseline", mem.Shop());
            var inventory = await api.ReadInventoryAsync(context, stop.Token);
            if (!inventory.Success) throw new Exception(inventory.Error);
            Print("inventory", inventory.Value!.Where(i => !i.IsEquipped).ToArray());
            var names = await new JsonBagCleanupNameListStore(Path.Combine(root, "config", JsonBagCleanupNameListStore.DefaultFileName)).LoadAsync();
            if (!names.Success) throw new Exception(names.Error);
            names.Value?.Document?.ApplyTo(config.ScriptSettings!.Maintenance);
            var candidates = BagCleanupItemMatcher.SelectSellRegistrationItems(inventory.Value!, config.ScriptSettings!.Maintenance);
            Print("sell_candidates", candidates);
            if (execute)
            {
                if (player.Value.CharacterName != expectedCharacter) throw new Exception("Unexpected character");
                using var productionInput = new KmBoxNetKeyboardInput(JsonSerializer.Deserialize<KmBoxNetKeyboardInputOptions>(
                    File.ReadAllText(Path.Combine(root, "config/kmbox-net.json")))!);
                var runtime = new Roadhog.Application.RoadhogRuntime(new RoadhogSnapshotReaderFactory(api), log,
                    new Roadhog.Application.AccountRuntimeManager(log), null!,
                    accountConfigStore: new JsonAccountConfigStore(Path.Combine(root, "config/accounts.json")), keyboardInput: productionInput);
                var watch = Stopwatch.StartNew();
                var outcome = await runtime.TestPersonalShopAsync(config.AccountName, config.ScriptSettings!.Maintenance,
                    new Progress<string>(text => Print("progress", text)), stop.Token);
                Print("production_result", new { elapsedMs = watch.ElapsedMilliseconds, outcome });
                foreach (var entry in log.Entries.Where(e => e.EventName.StartsWith("personal_shop") || e.EventName.StartsWith("snapshot.read"))) Print("production_log", entry);
                if (!outcome.Success) throw new Exception(outcome.Error);
            }

            Print("final", new
            {
                shop = mem.Shop(),
                items = mem.Registered()
            });
            var finalPlayer = await api.ReadPlayerAsync(context, stop.Token);
            Print("final_player", new
            {
                finalPlayer.Value?.CharacterName,
                finalPlayer.Value?.StanceFlags,
                finalPlayer.Value?.MotionMode,
                finalPlayer.Value?.IsResting
            });
        }
        finally
        {
            try { foreach (var connection in Connections(api)) connection.Dispose(); }
            finally { leases.Release(self.Id, self.StartTime.ToUniversalTime()); }
        }
        return 0;
    }

    private static string RequiredOption(string[] args, string prefix) =>
        args.SingleOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..] is { Length: > 0 } value
            ? value : throw new ArgumentException("Missing " + prefix);

    private static IEnumerable<VmmConnection> Connections(AionVmmGameApi api) => ((System.Collections.IDictionary)typeof(AionVmmGameApi).GetField("_connections", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(api)!).Values.Cast<VmmConnection>();

    internal static void ValidateEditor(ulong pending, ulong expected, uint mode, ulong unit, ulong total, ulong quantity, uint expectedQuantity)
    {
        if (pending != expected || mode != 0 || unit != 1 || quantity != expectedQuantity || quantity == 0 || total != quantity) throw new InvalidDataException("Price/quantity/identity mismatch; no confirmation.");
    }

    internal static void ValidatePlan(IReadOnlyList<Roadhog.Core.Model.InventoryItemSnapshot> plan, IReadOnlyList<Listing> actual)
    {
        if (plan.Count is < 1 or > 10 || plan.Select(i => i.InstanceId).Distinct().Count() != plan.Count || actual.Count != plan.Count || actual.Select(i => i.InstanceId).Distinct().Count() != actual.Count || actual.Any(e => !plan.Any(i => i.InstanceId == e.InstanceId && i.TemplateId == e.TemplateId && i.Count == e.Quantity) || e.UnitPrice != 1)) throw new InvalidDataException("Listing differs from the authorized plan.");
    }

    static void Print(string e, object? value) => Console.WriteLine(JsonSerializer.Serialize(new
    {
        at = DateTimeOffset.Now,
        e,
        value
    }, new JsonSerializerOptions
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    }));

    internal sealed class Memory(Func<ulong, int, byte[]?> read, ulong b)
    {
        public byte[] Bytes(ulong a, int n)
        {
            if (a < 0x10000 || a >= 0x800000000000) throw new Exception($"Invalid pointer {a:X}");
            var x = read(a, n);
            if (x == null || x.Length != n) throw new Exception($"Short read {a:X}");
            return x;
        }
        public ulong U(ulong a) => BitConverter.ToUInt64(Bytes(a, 8));
        public uint I(ulong a) => BitConverter.ToUInt32(Bytes(a, 4));
        public double D(ulong a)
        {
            var value = BitConverter.ToDouble(Bytes(a, 8));
            if (!double.IsFinite(value) || Math.Abs(value) > 32768) throw new InvalidDataException("Invalid geometry");
            return value;
        }
        public string Name(ulong a)
        {
            var n = U(a + 0x18);
            var cap = U(a + 0x20);
            if (n > 256 || cap < n || cap > 4096) throw new Exception("Invalid name");
            return n == 0 ? "" : Encoding.UTF8.GetString(Bytes(cap < 16 ? a + 8 : U(a + 8), (int)n));
        }
        public double[] Rect(ulong a, ulong off) => Enumerable.Range(0, 4).Select(i => D(a + off + (ulong)i * 8)).ToArray();
        public object Widget(ulong a) => new
        {
            addr = $"0x{a:X}",
            name = Name(a),
            flags = U(a + 0x28),
            rect = Rect(a, 0x58),
            client = Rect(a, 0x78)
        };
        public List<(ulong Address, string Name, double X, double Y, double W, double H)> Nodes(ulong a, double x = 0, double y = 0, int depth = 0, HashSet<ulong>? seen = null)
        {
            seen ??= new();
            if (depth > 8 || seen.Count > 400 || !seen.Add(a)) throw new Exception("Invalid tree");
            var list = new List<(ulong, string, double, double, double, double)>();
            if ((U(a + 0x28) & 1) == 0) return list;
            var r = Rect(a, 0x58);
            var c = Rect(a, 0x78);
            x += r[0];
            y += r[1];
            list.Add((a, Name(a), x, y, r[2], r[3]));
            var h = U(a + 0x238);
            if (h == 0) return list;
            var n = U(h);
            for (var i = 0; n != h; i++)
            {
                if (n == 0 || i >= 100) throw new Exception("Invalid children");
                list.AddRange(Nodes(U(n + 0x10), x + c[0], y + c[1], depth + 1, seen));
                n = U(n);
            }
            return list;
        }
        public (ulong Grid, int X, int Y) ItemPoint(ulong id)
        {
            foreach (var n in Nodes(U(b + 0xD63990 + 27 * 8)).Where(n => n.Name.StartsWith("list")))
            {
                var start = U(n.Address + 0x368);
                var end = U(n.Address + 0x370);
                if (end < start || (end - start) % 8 != 0 || (end - start) / 8 > 135) throw new Exception("Invalid grid");
                for (ulong i = 0; i < (end - start) / 8; i++)
                {
                    var e = U(start + i * 8);
                    if (I(e + 0xA0) != id) continue;
                    var columns = I(n.Address + 0x2E8);
                    if (I(n.Address + 0x2E0) != 1 || columns == 0 || columns > 20) throw new Exception("Invalid grid mode");
                    var w = D(n.Address + 0x2F8);
                    var h = D(n.Address + 0x2F0);
                    return (n.Address, (int)Math.Round(n.X + D(n.Address + 0x328) + D(n.Address + 0x340) + (i % columns) * (w + D(n.Address + 0x308)) + w / 2), (int)Math.Round(n.Y + D(n.Address + 0x330) + D(n.Address + 0x338) + (i / columns) * (h + D(n.Address + 0x310)) + h / 2));
                }
            }
            throw new Exception("Item not visible " + id);
        }
        public uint Hover(ulong grid)
        {
            var i = BitConverter.ToInt16(Bytes(grid + 0x3F0, 2));
            var begin = U(grid + 0x368);
            var end = U(grid + 0x370);
            return i >= 0 && (ulong)i < (end - begin) / 8 ? I(U(begin + (ulong)i * 8) + 0xA0) : 0;
        }
        public List<Listing> Registered()
        {
            var shop = U(b + 0xD63ED0);
            var list = U(shop + 0x4F0);
            var begin = U(list + 0x368);
            var end = U(list + 0x370);
            if (end < begin || (end - begin) % 8 != 0 || (end - begin) / 8 > 10) throw new Exception("Invalid listing vector");
            var result = new List<Listing>();
            var h = U(shop + 0x4E0);
            for (ulong i = 0; i < (end - begin) / 8; i++)
            {
                var a = U(begin + i * 8);
                if (a == 0 || I(a + 0xA8) == 0) continue;
                var id = I(a + 0xA0);
                var n = U(h + 8);
                ulong price = 0;
                var found = false;
                for (var j = 0; n != h && Bytes(n + 0x19, 1)[0] == 0; j++)
                {
                    if (j > 64) throw new Exception("Invalid price tree");
                    var key = I(n + 0x20);
                    if (key == id)
                    {
                        price = U(n + 0x28);
                        found = true;
                        break;
                    }
                    n = U(n + (key > id ? 0UL : 0x10UL));
                }
                if (!found) throw new Exception("Missing price");
                result.Add(new((int)i, id, I(a + 0xA8), U(a + 0xB0), price));
            }
            return result;
        }
        public object Shop()
        {
            var a = U(b + 0xD63ED0);
            return a == 0 ? new
            {
                exists = false
            }
            : (object)new
            {
                exists = true,
                widget = Widget(a),
                active = I(a + 0x4D8),
                state = I(a + 0x528),
                pendingItem = I(a + 0x510),
                editor = $"0x{U(a + 0x520):X}",
                width = D(b + 0xDACF00),
                height = D(b + 0xDACF08),
                cursor = new[] { I(b + 0xDAD020), I(b + 0xDAD024) }
            };
        }
    }

    internal sealed record Listing(int Slot, uint InstanceId, uint TemplateId, ulong Quantity, ulong UnitPrice);
}
