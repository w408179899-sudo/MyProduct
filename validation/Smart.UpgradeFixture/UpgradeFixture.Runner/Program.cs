using System.Reflection;
using System.Text.Json;
using Smart.Contracts;
using Smart.Data;
using Smart.Hosting;
using Smart.Runtime;
using UpgradeFixture.Application;
using UpgradeFixture.Domain;

namespace UpgradeFixture.Runner;

public static class Program
{
    internal static readonly string[] FrameworkAssemblies = ["Smart.Contracts", "Smart.Data", "Smart.Runtime",
        "Smart.Hosting", "Smart.Hosting.Windows", "Smart.Adapters.Dma", "Smart.Adapters.KmBox"];
    private sealed class RawReader : IRawChannelReader<CounterValue, NoPartition>
    {
        public bool Fail;
        public long Value;
        public int Captures;
        public ValueTask<RawRead<CounterValue>> CaptureAsync(CaptureContext context, NoPartition partition, CancellationToken cancellationToken)
        {
            Captures++;
            return ValueTask.FromResult(Fail ? RawRead<CounterValue>.Failed("Controlled fixture failure.") :
                RawRead<CounterValue>.Complete(new CounterValue(Value)));
        }
    }
    private sealed class FakeInput : IInputDevice
    {
        public string DeviceId => "upgrade-fixture-input";
        public int Sends, Releases, Disposed;
        public bool Held;
        public ValueTask SendAsync(InputCommand command, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Sends++; Held = command.Operation == InputOperation.KeyDown; return ValueTask.CompletedTask; }
        public ValueTask ReleaseAllAsync(CancellationToken cancellationToken)
        { Releases++; Held = false; return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync() { Disposed++; return ValueTask.CompletedTask; }
    }
    internal static void Require(bool condition, string detail)
    { if (!condition) throw new InvalidOperationException(detail); }
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 4 && args[0] == "api-compare") return ApiSurface.Compare(args[1], args[2], args[3]);
            if (args.Length != 3 || args[0] is not ("baseline" or "upgrade"))
                throw new ArgumentException("Usage: UpgradeFixture.Runner baseline|upgrade <evidence directory> <persisted config path>");
            Directory.CreateDirectory(args[1]);
            var checks = new List<string>();
            var expected = new FixtureSettings(true, 25, 0);
            static void Validate(FixtureSettings value)
            { Require(value.Enabled && value.IntervalMilliseconds > 0 && value.Threshold >= 0, "Fixture configuration changed."); }
            var config = new JsonConfigStore<FixtureSettings>(Path.Combine(AppContext.BaseDirectory, "fixture.settings.json"), 1, Validate);
            Require(await config.LoadAsync() == expected, "The fixed baseline JSON was not read correctly.");
            var persisted = new JsonConfigStore<FixtureSettings>(args[2], 1, Validate);
            if (args[0] == "baseline") await persisted.SaveAsync(expected);
            Require(await persisted.LoadAsync() == expected, "The configuration saved by the baseline package was not preserved.");
            checks.Add("fixed-and-baseline-persisted-config");

            var domainReferences = typeof(CounterValue).Assembly.GetReferencedAssemblies();
            var applicationReferences = typeof(FixtureModule).Assembly.GetReferencedAssemblies();
            Require(!domainReferences.Any(x => x.Name!.StartsWith("Smart.", StringComparison.Ordinal)), "Domain depends on framework internals.");
            Require(applicationReferences.Where(x => x.Name!.StartsWith("Smart.", StringComparison.Ordinal)).All(x => x.Name == "Smart.Contracts"),
                "Application depends on raw data, runtime or hardware.");
            checks.Add("domain-application-reference-boundaries");

            var raw = new RawReader { Fail = true };
            var catalog = new SnapshotCatalog();
            var channel = catalog.Register<CounterValue, NoPartition, CounterValue>("fixture.counter", raw,
                new ReplaceMerger<CounterValue>(_ => true), SnapshotMergePolicy.Replace, TimeSpan.Zero);
            catalog.Seal();
            await using var session = new SnapshotProvider(catalog).OpenSession(new("fixture-dma", "fixture-connection", "fixture-account",
                "fixture-worker", 1, "fixture-process", "fixture-module"));
            using (var coldStop = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
            {
                try { await session.Reader.ReadAsync(channel, NoPartition.Value, coldStop.Token); throw new InvalidOperationException("Cold start returned a fabricated value."); }
                catch (OperationCanceledException) when (coldStop.IsCancellationRequested) { }
            }
            Require(raw.Captures > 0 && session.Metrics.Publications == 0, "Cold-start failure unexpectedly published.");
            checks.Add("cold-start-cancellation");
            raw.Fail = false; raw.Value = 0;
            var first = await session.Reader.ReadAsync(channel, NoPartition.Value).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            Require(first.Value.Value == 0 && first.Stamp.Version == 1, "A valid zero did not become the first publication.");
            raw.Fail = true;
            var captures = raw.Captures;
            var held = await session.Reader.ReadAsync(channel, NoPartition.Value);
            Require(raw.Captures > captures && ReferenceEquals(first, held) && session.Metrics.Publications == 1,
                "A failed capture changed the official value, stamp or capture timestamp.");
            checks.Add("valid-zero-and-failed-read-retains-exact-publication");
            raw.Fail = false; raw.Value = 7;
            var recovered = await session.Reader.WaitForChangeAsync(channel, NoPartition.Value, first.Stamp).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            Require(recovered.Value.Value == 7 && recovered.Stamp.Generation == first.Stamp.Generation && recovered.Stamp.Version == 2,
                "Successful recovery failed to publish version two.");
            checks.Add("successful-read-recovery");

            var module = new FixtureModule(channel); var input = new FakeInput(); var leases = new InputLeaseRegistry();
            await using (var runner = new AccountRunner(() => new AccountWorker("fixture-account", session.Reader,
                new ActionExecutor(input, leases), [module], registeredChannels: [channel.Id])))
            {
                runner.Start();
                await module.ActionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Require(module.Initialized == 1 && module.Observed == 7 && input.Sends == 1 && input.Held,
                    "The fixed module did not consume an official value and retain its fake input.");
                await runner.StopAsync(TimeSpan.FromSeconds(3));
                Require(runner.Status.State == AccountState.Stopped && module.Stopped == 1 && input.Releases > 0 && input.Disposed == 1 && !input.Held,
                    "Stopping the module failed to release and dispose its fake input.");
                using var reacquired = leases.Acquire(input.DeviceId);
            }
            checks.Add("module-run-stop-and-input-lease-release");
            var assemblies = FrameworkAssemblies.Select(name => Assembly.Load(new AssemblyName(name))).ToArray();
            await File.WriteAllTextAsync(Path.Combine(args[1], "public-api.json"), JsonSerializer.Serialize(ApiSurface.Capture(assemblies), new JsonSerializerOptions { WriteIndented = true }));
            var report = new { Passed = true, Mode = args[0], Checks = checks, LoadedFramework = assemblies.Select(assembly => new
            { Name = assembly.GetName().Name, Version = assembly.GetName().Version?.ToString(), assembly.Location }) };
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(args[1], "result.json"), json);
            Console.WriteLine(json); return 0;
        }
        catch (Exception error)
        { Console.Error.WriteLine(error); return 2; }
    }
}
