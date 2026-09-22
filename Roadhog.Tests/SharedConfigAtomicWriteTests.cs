using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Roadhog.Application;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Paths;
using Roadhog.Core.Profiles;
using Roadhog.Core.Radar;
using Roadhog.Infrastructure.Config;
using Roadhog.Infrastructure.Input;
using Roadhog.Infrastructure.Paths;
using Roadhog.Infrastructure.Profiles;
using Roadhog.Infrastructure.Radar;
using Roadhog.Infrastructure.WorkerProcesses;

internal static class SharedConfigAtomicWriteTests
{
    public static async Task StartupUsesLatestSharedConfigAsync()
    {
        using var files = new TestFiles();
        var profiles = new JsonScriptProfileStore(Path.Combine(files.Directory, "profiles"));
        var listsPath = Path.Combine(files.Directory, "lists.json");
        var lists = new JsonBagCleanupNameListStore(listsPath);
        var builder = new AccountStartConfigBuilder(profiles, lists, NoOpRoadhogLogger.Instance);
        var account = new AccountConfig
        {
            AccountName = "local", ProfileName = "shared",
            ScriptSettings = new() { ProfileName = "shared", Paths = new() { CombatPathName = "local-path" },
                Maintenance = new() { BagCleanupEnabled = true, BagCleanupExcludedItemNames = new() { "local-list" } } }
        };
        Require((await profiles.SaveAsync(new() { Name = "shared", Settings = new() { Paths = new() { CombatPathName = "first-path" },
            Maintenance = new() { BagCleanupEnabled = true } } })).Success, "initial shared profile saved");
        Require((await lists.SaveAsync(new() { Whitelist = new() { "first-list" } })).Success, "initial shared list saved");
        var first = await builder.BuildAsync(account);
        Require(first.Success && first.Value!.CombatPathName == "first-path" &&
            first.Value.ScriptSettings!.Maintenance.BagCleanupExcludedItemNames.SequenceEqual(new[] { "first-list" }), "startup resolves shared profile and lists");
        Require((await profiles.SaveAsync(new() { Name = "shared", Settings = new() { Paths = new() { CombatPathName = "next-path" },
            Maintenance = new() { BagCleanupEnabled = true } } })).Success, "updated shared profile saved");
        Require((await lists.SaveAsync(new() { Whitelist = new() { "next-list" } })).Success, "updated shared list saved");
        var next = await builder.BuildAsync(account);
        Require(next.Success && next.Value!.CombatPathName == "next-path" &&
            next.Value.ScriptSettings!.Maintenance.BagCleanupExcludedItemNames.SequenceEqual(new[] { "next-list" }), "recovery sees freshly saved shared configuration");
        Require(account.ScriptSettings.Paths.CombatPathName == "local-path" &&
            account.ScriptSettings.Maintenance.BagCleanupExcludedItemNames.SequenceEqual(new[] { "local-list" }), "startup does not mutate stored account configuration");
        await File.WriteAllTextAsync(listsPath, "{broken");
        var broken = await builder.BuildAsync(account);
        Require(broken.Success && !broken.Value!.ScriptSettings!.Maintenance.BagCleanupEnabled, "invalid shared lists preserve cleanup-disabled safety behavior");
        Require((await profiles.DeleteAsync("shared")).Success, "remove shared profile");
        File.Delete(listsPath);
        var missing = await builder.BuildAsync(account);
        Require(missing.Success && missing.Value!.CombatPathName == "local-path" && missing.Value.ScriptSettings!.Maintenance.BagCleanupEnabled,
            "missing profile falls back to local settings");

        var workerStore = new WorkerAccountConfigStore(account);
        var loaded = await workerStore.LoadAllAsync();
        Require(loaded.Value?.Count == 1 && loaded.Value[0].AccountName == account.AccountName, "worker account store exposes only its own launch account");
        loaded.Value![0].AccountName = "foreign-change";
        Require((await workerStore.LoadAllAsync()).Value![0].AccountName == account.AccountName, "worker snapshots cannot mutate stored configuration");
        Require(!(await workerStore.SaveAllAsync(new[] { account })).Success && !(await workerStore.UpsertAsync(account)).Success,
            "worker processes cannot write the manager's shared account configuration");
    }

    public static async Task ConcurrentReadersAsync()
    {
        using var files = new TestFiles();
        foreach (var store in CreateStores(files.Directory))
        {
            Require((await store.Save(0)).Success, store.Name + " initial save");
            using var start = new ManualResetEventSlim();
            var writer = Task.Run(async () =>
            {
                start.Wait();
                for (var revision = 1; revision <= 30; revision++)
                {
                    var result = await store.Save(revision);
                    Require(result.Success, store.Name + " save failed: " + result.Error);
                }
            });
            var readers = Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
            {
                start.Wait();
                var reads = 0;
                do
                {
                    Require(await store.Load(), store.Name + " reader observed an invalid or unavailable document");
                    await using var stream = AtomicJsonFile.OpenRead(store.Path);
                    using var document = await JsonDocument.ParseAsync(stream);
                    Require(document.RootElement.ValueKind == JsonValueKind.Object, store.Name + " complete JSON root");
                    reads++;
                } while (!writer.IsCompleted || reads < 10);
            })).ToArray();
            start.Set();
            await Task.WhenAll(readers.Append(writer)).WaitAsync(TimeSpan.FromSeconds(20));
        }
        files.RequireNoTemporaryFiles();
    }

    public static async Task OpenReadersAndEncodingAsync()
    {
        using var files = new TestFiles();
        foreach (var store in CreateStores(files.Directory))
        {
            Require((await store.Save(1)).Success, store.Name + " initial save");
            var original = await File.ReadAllTextAsync(store.Path);
            await using (var oldReader = AtomicJsonFile.OpenRead(store.Path))
            {
                var result = await store.Save(2);
                Require(result.Success, store.Name + " replacement must succeed while an account is reading: " + result.Error);
                using var textReader = new StreamReader(oldReader, leaveOpen: true);
                Require(await textReader.ReadToEndAsync() == original, store.Name + " existing reader retains complete old version");
            }
            var current = await File.ReadAllBytesAsync(store.Path);
            Require(current.Length > 3 && !(current[0] == 0xef && current[1] == 0xbb && current[2] == 0xbf), store.Name + " retains UTF-8 no-BOM writes");
            Require(Encoding.UTF8.GetString(current).Contains('\n'), store.Name + " retains indented JSON");
            await File.WriteAllBytesAsync(store.Path, Encoding.UTF8.GetPreamble().Concat(current).ToArray());
            Require(await store.Load(), store.Name + " accepts an existing UTF-8 BOM file");
        }
    }

    public static async Task FailedWritesKeepPreviousAsync()
    {
        using var files = new TestFiles();
        var path = Path.Combine(files.Directory, "fault.json");
        var options = new JsonSerializerOptions();
        await AtomicJsonFile.WriteAsync(path, new { Revision = 1 }, options);
        var before = await File.ReadAllBytesAsync(path);
        var failing = new JsonSerializerOptions();
        failing.Converters.Add(new CallbackConverter(() => throw new IOException("simulated serialization failure")));
        await ExpectFailureAsync<IOException>(() => AtomicJsonFile.WriteAsync(path, new Payload(), failing));
        Require(before.SequenceEqual(await File.ReadAllBytesAsync(path)), "serialization failure preserves old bytes");

        using var cancelled = new CancellationTokenSource();
        var cancelling = new JsonSerializerOptions();
        cancelling.Converters.Add(new CallbackConverter(cancelled.Cancel));
        await ExpectFailureAsync<OperationCanceledException>(() => AtomicJsonFile.WriteAsync(path, new Payload(), cancelling, cancelled.Token));
        Require(before.SequenceEqual(await File.ReadAllBytesAsync(path)), "cancel before commit preserves old bytes");

        // An external reader that does not allow deletion must cause a failed save,
        // never a truncate-then-write fallback that damages its document.
        using (var blockingReader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failed = false;
            try { await AtomicJsonFile.WriteAsync(path, new { Revision = 2 }, options); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { failed = true; }
            Require(failed, "replacement is rejected when a reader does not permit delete");
        }
        Require(before.SequenceEqual(await File.ReadAllBytesAsync(path)), "failed atomic replacement preserves old bytes");
        files.RequireNoTemporaryFiles();
    }

    public static async Task IncompleteWritesAndPathIsolationAsync()
    {
        using var files = new TestFiles();
        var first = Path.Combine(files.Directory, "first.json");
        var second = Path.Combine(files.Directory, "second.json");
        var options = new JsonSerializerOptions();
        await AtomicJsonFile.WriteAsync(first, new { Revision = 1 }, options);
        using var writing = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var paused = new JsonSerializerOptions();
        paused.Converters.Add(new CallbackConverter(() =>
        {
            writing.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("test writer was not released");
        }));
        var firstWrite = Task.Run(() => AtomicJsonFile.WriteAsync(first, new Payload(), paused));
        try
        {
            Require(writing.Wait(TimeSpan.FromSeconds(5)), "first document entered serialization");
            for (var index = 0; index < 10; index++)
            {
                await using var reader = AtomicJsonFile.OpenRead(first);
                using var previous = await JsonDocument.ParseAsync(reader);
                Require(previous.RootElement.GetProperty("Revision").GetInt32() == 1, "incomplete replacement remains invisible");
            }
            await AtomicJsonFile.WriteAsync(second, new { Revision = 7 }, options).WaitAsync(TimeSpan.FromSeconds(2));
            Require(File.Exists(second), "another path is independently writable while first serialization waits");
        }
        finally
        {
            release.Set();
            await firstWrite.WaitAsync(TimeSpan.FromSeconds(5));
        }
        await using var finalReader = AtomicJsonFile.OpenRead(first);
        using var final = await JsonDocument.ParseAsync(finalReader);
        Require(final.RootElement.GetProperty("Revision").GetInt32() == 2, "complete replacement is published");
        files.RequireNoTemporaryFiles();
    }

    private static IReadOnlyList<StoreCase> CreateStores(string root)
    {
        var accountPath = Path.Combine(root, "accounts.json");
        var accountWriter = new JsonAccountConfigStore(accountPath);
        var accountReader = new JsonAccountConfigStore(accountPath);
        var kmPath = Path.Combine(root, "kmbox.json");
        var kmWriter = new JsonKmBoxNetDeviceConfigStore(kmPath);
        var kmReader = new JsonKmBoxNetDeviceConfigStore(kmPath);
        var pathDirectory = Path.Combine(root, "paths");
        var pathWriter = new JsonSharedPathStore(pathDirectory);
        var pathReader = new JsonSharedPathStore(pathDirectory);
        var profileDirectory = Path.Combine(root, "profiles");
        var profileWriter = new JsonScriptProfileStore(profileDirectory);
        var profileReader = new JsonScriptProfileStore(profileDirectory);
        var radarDirectory = Path.Combine(root, "radar");
        var radarWriter = new JsonRadarMapStore(radarDirectory);
        var radarReader = new JsonRadarMapStore(radarDirectory);
        var listsPath = Path.Combine(root, "lists.json");
        var listsWriter = new JsonBagCleanupNameListStore(listsPath);
        var listsReader = new JsonBagCleanupNameListStore(listsPath);
        return new[]
        {
            new StoreCase("accounts", accountPath,
                revision => accountWriter.SaveAllAsync(new[] { new AccountConfig { AccountName = "shared", CharacterName = Marker(revision) } }),
                async () => LoadedAccount(await accountReader.LoadAllAsync())),
            new StoreCase("account upsert", accountPath,
                revision => accountWriter.UpsertAsync(new AccountConfig { AccountName = "shared", CharacterName = Marker(revision) }),
                async () => LoadedAccount(await accountReader.LoadAllAsync())),
            new StoreCase("KMBox", kmPath,
                revision => kmWriter.SaveAsync(new KmBoxNetDeviceConfig { IpAddress = "127.0.0.1", Port = 12345, Mac = Marker(revision) }),
                async () => Loaded(kmReader.Load()) && Loaded(await kmReader.LoadAsync())),
            new StoreCase("paths", Path.Combine(pathDirectory, "shared.json"),
                revision => pathWriter.SaveAsync(new SharedPathDocument { Name = "shared", CleanupNpcName = Marker(revision) }),
                async () => Loaded(await pathReader.LoadAsync("shared")) && Loaded(await pathReader.LoadSummariesAsync())),
            new StoreCase("profiles", Path.Combine(profileDirectory, "shared.json"),
                revision => profileWriter.SaveAsync(new ScriptProfileDocument { Name = "shared", Settings = new ScriptSettings { Paths = new PathScriptSettings { CombatPathName = Marker(revision) } } }),
                async () => Loaded(await profileReader.LoadAsync("shared")) && Loaded(await profileReader.LoadSummariesAsync())),
            new StoreCase("radar", Path.Combine(radarDirectory, "47.json"),
                revision => radarWriter.SaveAsync(new RadarMapDocument { MapId = 47, MapCode = Marker(revision) }),
                async () => Loaded(await radarReader.LoadAsync(47))),
            new StoreCase("name lists", listsPath,
                revision => listsWriter.SaveAsync(new BagCleanupNameListsDocument { Whitelist = new() { Marker(revision) } }),
                async () => Loaded(await listsReader.LoadAsync()))
        };
    }

    private static string Marker(int revision) => revision + new string('界', 4096);

    private static bool Loaded<T>(OperationResult<T> result)
    {
        Require(result.Success, "configuration read failed: " + result.Error);
        return true;
    }

    private static bool LoadedAccount(OperationResult<IReadOnlyList<AccountConfig>> result)
    {
        Loaded(result);
        Require(result.Value?.Count == 1 && result.Value[0].AccountName == "shared", "replacement must never expose an empty account configuration");
        return true;
    }

    private static async Task ExpectFailureAsync<TException>(Func<Task> action) where TException : Exception
    {
        try { await action(); }
        catch (TException) { return; }
        throw new InvalidOperationException("Expected " + typeof(TException).Name);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record StoreCase(string Name, string Path, Func<int, Task<OperationResult>> Save, Func<Task<bool>> Load);
    private sealed class Payload { }

    private sealed class CallbackConverter(Action callback) : JsonConverter<Payload>
    {
        public override Payload? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, Payload value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("Partial", new string('x', 32768));
            callback();
            writer.WriteNumber("Revision", 2);
            writer.WriteEndObject();
        }
    }

    private sealed class TestFiles : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "RoadhogAtomicTests", Guid.NewGuid().ToString("N"));

        public TestFiles() => System.IO.Directory.CreateDirectory(Directory);

        public void RequireNoTemporaryFiles() => Require(
            !System.IO.Directory.EnumerateFiles(Directory, "*.tmp", SearchOption.AllDirectories).Any(), "temporary files are cleaned up");

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
