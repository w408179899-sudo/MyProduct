using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Roadhog.Infrastructure.WorkerProcesses;

/// <summary>Confirms that an already-owned worker has disappeared from the operating system.</summary>
public interface IWorkerProcessExitReconciler
{
    Task ReconcileAsync(WorkerDescriptor descriptor, string executablePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Windows can report HasExited while the same process remains in Win32_Process with driver handles.
/// Run CIM in a bounded helper so a stalled WMI provider cannot consume manager threads indefinitely.
/// This is only called for an identity previously owned or authenticated by the manager.
/// </summary>
public sealed class WindowsWorkerProcessExitReconciler : IWorkerProcessExitReconciler
{
    private readonly TimeSpan _timeout;

    public WindowsWorkerProcessExitReconciler(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
        if (_timeout <= TimeSpan.Zero || _timeout.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public async Task ReconcileAsync(WorkerDescriptor descriptor, string executablePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.ProcessId <= 0 || descriptor.ProcessStartedAtUtc == default)
            throw new InvalidOperationException("后台缺少已验证的进程身份，拒绝清理残留进程。");
        cancellationToken.ThrowIfCancellationRequested();
        var request = JsonSerializer.Serialize(new
        {
            descriptor.ProcessId,
            // Win32_Process.CreationDate has microsecond precision; StartTime can contain 100 ns ticks.
            StartedAtUtc = descriptor.ProcessStartedAtUtc.UtcDateTime.ToString("yyyyMMddHHmmss.ffffff'Z'", CultureInfo.InvariantCulture),
            ExecutablePath = Path.GetFullPath(executablePath),
            TimeoutMilliseconds = (int)_timeout.TotalMilliseconds
        });
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand",
                     Convert.ToBase64String(Encoding.Unicode.GetBytes(CleanupScript)) })
            start.ArgumentList.Add(argument);
        using var helper = new Process { StartInfo = start };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        if (!helper.Start()) throw new InvalidOperationException("无法启动后台进程清理助手。");
        var output = helper.StandardOutput.ReadToEndAsync();
        var error = helper.StandardError.ReadToEndAsync();
        try
        {
            await helper.StandardInput.WriteAsync(request.AsMemory(), deadline.Token).ConfigureAwait(false);
            helper.StandardInput.Close();
            await helper.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            var responseText = await output.WaitAsync(deadline.Token).ConfigureAwait(false);
            _ = await error.WaitAsync(deadline.Token).ConfigureAwait(false);
            CleanupResponse? response;
            try { response = JsonSerializer.Deserialize<CleanupResponse>(responseText); }
            catch (JsonException exception)
            {
                throw new InvalidOperationException("后台进程清理助手未返回有效结果，仍保留该进程身份。", exception);
            }
            if (helper.ExitCode != 0 || response?.Success != true)
                throw new InvalidOperationException(response?.Error ?? "无法确认后台进程已完全退出，仍保留该进程身份。");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"后台进程 {descriptor.ProcessId} 的残留清理超时，仍保留该进程身份。");
        }
        finally
        {
            // The helper is ours. Never kill a process tree or use its PID to rediscover another process.
            try
            {
                if (!helper.HasExited) helper.Kill(entireProcessTree: false);
                using var exitDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await helper.WaitForExitAsync(exitDeadline.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException) { }
        }
    }

    private sealed record CleanupResponse(bool Success, string? Error);

    // Account data is JSON on stdin, never interpolated into executable script text.
    private const string CleanupScript = """
        $ErrorActionPreference = 'Stop'
        $ProgressPreference = 'SilentlyContinue'
        [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
        function Write-Result([bool]$success, [string]$errorText) {
            [Console]::Out.Write((@{ Success = $success; Error = $errorText } | ConvertTo-Json -Compress))
        }
        try {
            $request = [Console]::In.ReadToEnd() | ConvertFrom-Json
            if ([int]$request.ProcessId -le 0) { throw 'Invalid worker process identity.' }
            $filter = 'ProcessId = {0}' -f [int]$request.ProcessId
            $timer = [Diagnostics.Stopwatch]::StartNew()
            function Find-Worker {
                Get-CimInstance -ClassName Win32_Process -Filter $filter -OperationTimeoutSec 1
            }
            function Assert-Identity($worker) {
                $created = $worker.CreationDate.ToUniversalTime().ToString("yyyyMMddHHmmss.ffffff'Z'", [Globalization.CultureInfo]::InvariantCulture)
                if ($created -cne [string]$request.StartedAtUtc -or
                    -not [StringComparer]::OrdinalIgnoreCase.Equals([string]$worker.ExecutablePath, [string]$request.ExecutablePath)) {
                    throw 'Worker process identity changed; refusing to terminate the process.'
                }
            }
            $worker = Find-Worker
            if ($null -eq $worker) { Write-Result $true ''; exit 0 }
            Assert-Identity $worker
            $result = Invoke-CimMethod -InputObject $worker -MethodName Terminate -Arguments @{ Reason = [uint32]0 } -OperationTimeoutSec 1
            if ([uint32]$result.ReturnValue -ne 0) { throw ('Worker CIM termination failed with result {0}.' -f $result.ReturnValue) }
            do {
                $worker = Find-Worker
                if ($null -eq $worker) { Write-Result $true ''; exit 0 }
                Assert-Identity $worker
                Start-Sleep -Milliseconds 100
            } while ($timer.ElapsedMilliseconds -lt [int]$request.TimeoutMilliseconds)
            throw 'Worker process still exists after CIM termination.'
        }
        catch {
            Write-Result $false $_.Exception.Message
            exit 1
        }
        """;
}
