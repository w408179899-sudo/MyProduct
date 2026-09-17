using System.Text.Json;
using Smart.ProbeProtocol;
using Smart.ProbeTarget;
using Xunit;
namespace Smart.Probe.Tests;

public sealed class TargetTests
{
    [Fact] public async Task TargetExposesAnUpdatingWindowThenPublishesTheWholeNextFrame()
    {
        var session = Guid.NewGuid(); using var memory = new ProbeMemoryBlock(session);
        await memory.PublishAsync(0, TimeSpan.Zero);
        Assert.True(ProbeMemoryProtocol.TryDecode(memory.CopySnapshot(), session, out var initial, out _));
        Assert.Equal(0, initial!.Value);
        var writing = memory.PublishAsync(1, TimeSpan.FromMilliseconds(100));
        Assert.False(ProbeMemoryProtocol.TryDecode(memory.CopySnapshot(), session, out var torn, out var error));
        Assert.Null(torn); Assert.Equal(ProbeReadError.Updating, error);
        await writing.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(ProbeMemoryProtocol.TryDecode(memory.CopySnapshot(), session, out var complete, out _));
        Assert.Equal(1, complete!.Counter); Assert.True(complete.Sequence > initial.Sequence);
    }
    [Fact] public async Task CancelledPartialWriteCanBeFollowedByAValidPublication()
    {
        var session = Guid.NewGuid(); using var memory = new ProbeMemoryBlock(session);
        await memory.PublishAsync(1, TimeSpan.Zero);
        using var stop = new CancellationTokenSource();
        var writing = memory.PublishAsync(2, TimeSpan.FromSeconds(30), stop.Token);
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writing);
        Assert.False(ProbeMemoryProtocol.TryDecode(memory.CopySnapshot(), session, out _, out _));
        await memory.PublishAsync(8, TimeSpan.Zero);
        Assert.True(ProbeMemoryProtocol.TryDecode(memory.CopySnapshot(), session, out var recovered, out _));
        Assert.Equal(8, recovered!.Counter); Assert.Equal(0, recovered.Value);
    }
    [Fact] public async Task DisposalCannotAllowALateWriterToTouchFreedMemory()
    {
        using var memory = new ProbeMemoryBlock(Guid.NewGuid());
        var writing = memory.PublishAsync(1, TimeSpan.FromMilliseconds(50));
        memory.Dispose(); memory.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => writing);
        Assert.Throws<ObjectDisposedException>(() => memory.CopySnapshot());
    }
    [Fact] public void MixedFaultScheduleIsDeterministicAndStableModeHasNoInjectedWindow()
    {
        var mixed = ProbeTargetOptions.Parse([]);
        Assert.Equal(new long[] { 4, 12, 20 }, Enumerable.Range(1, 24).Select(x => (long)x).Where(mixed.ShouldTear));
        Assert.All(Enumerable.Range(1, 24), value => Assert.False((mixed with { Mode = "stable" }).ShouldTear(value)));
        Assert.All(Enumerable.Range(1, 24), value => Assert.True((mixed with { Mode = "torn" }).ShouldTear(value)));
    }
    [Theory]
    [InlineData("--interval-ms", "0")]
    [InlineData("--torn-ms", "60001")]
    [InlineData("--mode", "unknown")]
    [InlineData("--duration-ms", "99")]
    [InlineData("--manifest")]
    [InlineData("--mode", "mixed", "--mode", "stable")]
    public void InvalidTargetOptionsFailBeforeAllocatingMemory(params string[] arguments) =>
        Assert.Throws<ArgumentException>(() => ProbeTargetOptions.Parse(arguments));

    [Fact] public async Task FiniteTargetRunEmitsOneManifestAndAtomicallyReplacesItsFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "smart-probe-target-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory); var path = Path.Combine(directory, "target.json");
            await File.WriteAllTextAsync(path, "previous");
            using var output = new StringWriter(); using var diagnostics = new StringWriter();
            var manifest = await ProbeTargetRunner.RunAsync(new("mixed", 10, 10, 150, path), output, diagnostics).WaitAsync(TimeSpan.FromSeconds(5));
            manifest.Validate();
            Assert.Equal(manifest, JsonSerializer.Deserialize<ProbeManifest>(Assert.Single(output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))));
            Assert.Equal(manifest, JsonSerializer.Deserialize<ProbeManifest>(await File.ReadAllTextAsync(path)));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            Assert.Contains("target.stopped", diagnostics.ToString());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
