using System.Diagnostics;
using System.Text;

namespace Smart.Adapters.Dma;

// Short diagnostics use private inherited pipes. Native work begins only after job assignment
// and receipt of the request. Cancellation closes our own job, never another host's worker.
public static class NativeWorkerCommand
{
    public static async Task<string> RunInventoryAsync(string request, int timeoutMs = 10000,
        string? workerPath = null, CancellationToken token = default)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) throw new PlatformNotSupportedException();
        if (request.Length > 16384 || timeoutMs is < 100 or > 120000) throw new ArgumentException("Invalid inventory request budget.");
        var executable = workerPath ?? Path.Combine(AppContext.BaseDirectory, "native-worker", "Smart.Dma.Worker.exe");
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable)) throw new FileNotFoundException("Native worker is missing.", executable);
        using var job = new WorkerJob();
        using var process = new Process { StartInfo = new(executable, "--inventory")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false),
            WorkingDirectory = Path.GetDirectoryName(executable)! } };
        var host = Environment.ProcessPath;
        if (host is not null && Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            process.StartInfo.Environment["DOTNET_ROOT_X64"] = Path.GetDirectoryName(host)!;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeoutMs);
        token.ThrowIfCancellationRequested();
        if (!process.Start()) throw new IOException("Could not start inventory worker.");
        Task<string>? output = null, error = null;
        try
        {
            job.Assign(process);
            output = ReadBoundedAsync(process.StandardOutput, 131072, deadline.Token);
            error = ReadBoundedAsync(process.StandardError, 8192, deadline.Token);
            await process.StandardInput.WriteLineAsync(request.AsMemory(), deadline.Token).ConfigureAwait(false);
            process.StandardInput.Close();
            var result = await output.ConfigureAwait(false);
            var detail = await error.ConfigureAwait(false);
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            if (process.ExitCode != 0) throw new IOException("设备枚举失败：" + detail.Trim());
            return result;
        }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
        { throw new TimeoutException("设备枚举超时，已终止独立枚举进程。", ex); }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            await deadline.CancelAsync().ConfigureAwait(false);
            if (output is not null) { try { await output.ConfigureAwait(false); } catch { } }
            if (error is not null) { try { await error.ConfigureAwait(false); } catch { } }
        }
    }
    private static async Task<string> ReadBoundedAsync(StreamReader reader, int maximum, CancellationToken token)
    {
        var result = new StringBuilder(); var buffer = new char[2048];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (count == 0) return result.ToString();
            if (result.Length + count > maximum) throw new InvalidDataException("Inventory response exceeded its size budget.");
            result.Append(buffer, 0, count);
        }
    }
}
