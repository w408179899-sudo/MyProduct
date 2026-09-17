using System.Diagnostics;
using System.Text.Json;
using Smart.ProbeProtocol;
namespace Smart.ProbeTarget;

public static class ProbeTargetRunner
{
    public static async Task<ProbeManifest> RunAsync(ProbeTargetOptions options, TextWriter output, TextWriter diagnostics,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = Guid.NewGuid();
        using var memory = new ProbeMemoryBlock(session);
        await memory.PublishAsync(0, TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
        using var process = Process.GetCurrentProcess();
        var manifest = new ProbeManifest(ProbeMemoryProtocol.Version, session, process.Id, process.ProcessName,
            process.StartTime.ToUniversalTime(), process.MainModule?.ModuleName ?? throw new InvalidOperationException("Cannot identify the target host module."),
            "0x" + memory.Address.ToString("X16"), ProbeMemoryProtocol.Size, IntPtr.Size,
            options.Mode, options.IntervalMs, options.TornWindowMs);
        manifest.Validate();
        var json = JsonSerializer.Serialize(manifest);
        if (options.ManifestPath is { } path) await SaveManifestAsync(path, json, cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync(json).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (options.DurationMs is { } milliseconds) lifetime.CancelAfter(milliseconds);
        long counter = 0; long tornWindows = 0;
        try
        {
            while (true)
            {
                await Task.Delay(options.IntervalMs, lifetime.Token).ConfigureAwait(false);
                var next = checked(counter + 1);
                var tear = options.ShouldTear(next);
                if (tear) tornWindows++;
                await memory.PublishAsync(next, tear ? TimeSpan.FromMilliseconds(options.TornWindowMs) : TimeSpan.Zero, lifetime.Token).ConfigureAwait(false);
                counter = next;
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally
        {
            await diagnostics.WriteLineAsync(JsonSerializer.Serialize(new
            { Event = "target.stopped", manifest.SessionId, LastPublishedCounter = counter, TornWindows = tornWindows })).ConfigureAwait(false);
            await diagnostics.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        return manifest;
    }

    private static async Task SaveManifestAsync(string path, string json, CancellationToken token)
    {
        var absolute = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        var temporary = absolute + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, json, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, absolute, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
