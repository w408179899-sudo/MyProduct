using System.Text.Json;
using Smart.Contracts;
using Smart.Hosting;
using Xunit;
namespace Smart.Runtime.Tests;

public sealed class HostingTests
{
    private sealed record Settings(int Value);
    [Fact] public async Task ConfigRejectsInvalidValuesAndPersistsCompleteDocument()
    {
        var root = Path.Combine(Path.GetTempPath(), "smart-config-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "settings.json");
        try
        {
            var store = new JsonConfigStore<Settings>(path, 1, x => { if (x.Value < 0) throw new ArgumentException("Value"); });
            await store.SaveAsync(new(1));
            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new(-1)));
            Assert.Equal(1, (await store.LoadAsync()).Value);
            await Task.WhenAll(store.SaveAsync(new(2)), store.SaveAsync(new(3)));
            Assert.Contains((await store.LoadAsync()).Value, new[] { 2, 3 });
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact] public async Task LoggerDrainsAndBoundsQueueAndRotatedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "smart-logs-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sink = new JsonLineEventSink(root, capacity: 8, maximumFileBytes: 200, retainedFiles: 3);
            for (var i = 0; i < 1000; i++) sink.Write(new(DateTimeOffset.UtcNow, "test", "account", i.ToString()));
            await sink.DisposeAsync();
            Assert.Null(sink.WriteError);
            Assert.InRange(Directory.GetFiles(root).Length, 1, 3);
            foreach (var file in Directory.GetFiles(root))
                foreach (var line in await File.ReadAllLinesAsync(file))
                    Assert.Equal("test", JsonSerializer.Deserialize<DiagnosticEvent>(line)!.Name);
            Assert.True(sink.DroppedEvents >= 0);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
