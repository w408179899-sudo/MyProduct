using System.IO.Compression;
using Smart.Contracts;
using Smart.Hosting;
using Xunit;
namespace Smart.Runtime.Tests;

public sealed class DiagnosticTests
{
    [Fact] public async Task OversizedEventCannotBypassFileBudget()
    {
        var root = Path.Combine(Path.GetTempPath(), "smart-event-limit-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var logs = new JsonLineEventSink(root, maximumFileBytes: 512);
            logs.Write(new(DateTimeOffset.UtcNow, "huge", "a", new string('x', 1024)));
            logs.Write(new(DateTimeOffset.UtcNow, "small", "a", "ok"));
            await logs.DisposeAsync();
            Assert.Equal(1, logs.DroppedEvents);
            Assert.All(Directory.GetFiles(root), file => Assert.InRange(new FileInfo(file).Length, 1, 512));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact] public async Task DiagnosticBundleIncludesManifestAndHonorsLogByteLimit()
    {
        var root = Path.Combine(Path.GetTempPath(), "smart-bundle-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var small = Path.Combine(root, "small.jsonl"); var large = Path.Combine(root, "large.jsonl");
            await File.WriteAllTextAsync(small, "small"); await File.WriteAllTextAsync(large, new string('x', 1000));
            var output = Path.Combine(root, "diagnostics.zip");
            await DiagnosticBundle.ExportAsync(output, [large, small], [("account", new(SessionState.Stopped))], maximumBytes: 10);
            using var zip = ZipFile.OpenRead(output);
            Assert.NotNull(zip.GetEntry("manifest.json"));
            Assert.Single(zip.Entries, x => x.FullName.StartsWith("logs/", StringComparison.Ordinal));
            Assert.DoesNotContain(zip.Entries, x => x.FullName.Contains("large", StringComparison.Ordinal));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
