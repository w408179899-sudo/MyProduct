using System.IO.Compression;
using System.Text.Json;
namespace Smart.Hosting;

public static class DiagnosticBundle
{
    // Explicit files only. Never recursively sweep directories containing native libraries or user data.
    public static async Task ExportAsync(string destination, IEnumerable<string> logFiles,
        IReadOnlyList<(string Account, SessionStatus Status)> statuses, long maximumBytes = 32 * 1024 * 1024,
        CancellationToken token = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        var fullPath = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var manifest = archive.CreateEntry("manifest.json");
        await using (var writer = manifest.Open())
            await JsonSerializer.SerializeAsync(writer, new
            {
                CreatedAt = DateTimeOffset.UtcNow, Framework = typeof(ManagedAccount).Assembly.GetName().Version?.ToString(),
                Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                Accounts = statuses.Select(x => new { x.Account, x.Status })
            }, cancellationToken: token).ConfigureAwait(false);
        long copied = 0; var index = 0;
        foreach (var file in logFiles.OrderByDescending(File.GetLastWriteTimeUtc))
        {
            token.ThrowIfCancellationRequested();
            var length = new FileInfo(file).Length;
            if (length > maximumBytes - copied) continue;
            await using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            await using var target = archive.CreateEntry($"logs/{index++:D4}-{Path.GetFileName(file)}").Open();
            // Bound the copy even if a live writer appends while the bundle is being created.
            var buffer = new byte[16384]; long remaining = length;
            while (remaining > 0)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), token).ConfigureAwait(false);
                if (read == 0) break;
                await target.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                remaining -= read; copied += read;
            }
        }
    }
}
