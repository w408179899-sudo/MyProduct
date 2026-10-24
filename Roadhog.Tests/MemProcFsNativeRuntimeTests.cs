using Roadhog.Infrastructure.Vmm;

internal static class MemProcFsNativeRuntimeTests
{
    private sealed class Fixture : IDisposable
    {
        private readonly string _temp = Path.GetFullPath(Path.GetTempPath());
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "RoadhogNativeTest-" + Guid.NewGuid().ToString("N"));
        internal string Source => Path.Combine(Root, "多账号客户端");
        internal string Cache => Path.Combine(Root, "ascii-cache");
        internal Fixture()
        {
            Directory.CreateDirectory(Source);
            foreach (string name in new[] { "vmm.dll", "leechcore.dll", "dbghelp.dll", "symsrv.dll", "FTD3XX.dll" })
                File.WriteAllText(Path.Combine(Source, name), name + "-test-fixture");
        }
        public void Dispose()
        {
            string full = Path.GetFullPath(Root);
            if (!full.StartsWith(_temp.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(full).StartsWith("RoadhogNativeTest-", StringComparison.Ordinal))
                throw new InvalidOperationException("Test cleanup outside its temporary workspace.");
            Directory.Delete(full, true);
        }
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    internal static async Task ConcurrentStagingAsync()
    {
        using var fixture = new Fixture();
        var paths = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => MemProcFsNativeRuntime.Stage(fixture.Source, fixture.Cache))));
        Require(paths.Distinct().Count() == 1, "Workers must use the same complete native bundle.");
        Require(!MemProcFsNativeRuntime.NeedsAsciiPath(paths[0]), "Chinese installation must produce an ASCII symbol location.");
        foreach (var source in Directory.GetFiles(fixture.Source))
            Require(File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(Path.Combine(paths[0], Path.GetFileName(source)))), "Concurrent staging must preserve every native DLL.");
        Require(Directory.GetFiles(paths[0], "*.tmp").Length == 0, "No partial native file may be exposed.");
    }

    internal static Task RepairAndVersionIsolationAsync()
    {
        using var fixture = new Fixture();
        string first = MemProcFsNativeRuntime.Stage(fixture.Source, fixture.Cache);
        File.WriteAllText(Path.Combine(first, "dbghelp.dll"), "partial");
        Require(MemProcFsNativeRuntime.Stage(fixture.Source, fixture.Cache) == first, "Retry uses the same content identity.");
        Require(File.ReadAllText(Path.Combine(first, "dbghelp.dll")) == "dbghelp.dll-test-fixture", "Incomplete cache is repaired before use.");
        File.WriteAllText(Path.Combine(fixture.Source, "vmm.dll"), "new-native-version");
        string second = MemProcFsNativeRuntime.Stage(fixture.Source, fixture.Cache);
        Require(first != second, "A native update cannot overwrite a bundle used by another worker.");
        Require(File.ReadAllText(Path.Combine(first, "vmm.dll")) == "vmm.dll-test-fixture", "Old version stays intact.");
        return Task.CompletedTask;
    }

    internal static Task MissingDependencyAsync()
    {
        using var fixture = new Fixture();
        File.Delete(Path.Combine(fixture.Source, "symsrv.dll"));
        bool rejected = false;
        try { MemProcFsNativeRuntime.Stage(fixture.Source, fixture.Cache); }
        catch (FileNotFoundException) { rejected = true; }
        Require(rejected && !Directory.Exists(fixture.Cache), "Missing symbol dependency cannot publish an incomplete native bundle.");
        return Task.CompletedTask;
    }
}
