using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Roadhog.Infrastructure.Hardware;

public enum ProcessIdentityPresence
{
    Alive,
    Gone,
    Unknown
}

/// <summary>Checks whether the original process still exists, including after Process.HasExited becomes true.</summary>
public interface IProcessIdentityPresenceProbe
{
    ProcessIdentityPresence Check(int processId, DateTimeOffset processStartedAtUtc);
}

/// <summary>A read-only, bounded Win32_Process identity check. It never terminates the target process.</summary>
public sealed class WindowsProcessIdentityPresenceProbe : IProcessIdentityPresenceProbe
{
    private readonly TimeSpan _timeout;
    private readonly Func<int, DateTimeOffset, ProcessIdentityPresence?> _processApiProbe;

    public WindowsProcessIdentityPresenceProbe(TimeSpan? timeout = null,
        Func<int, DateTimeOffset, ProcessIdentityPresence?>? processApiProbe = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(3);
        if (_timeout <= TimeSpan.Zero || _timeout.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        _processApiProbe = processApiProbe ?? CheckProcessApi;
    }

    public ProcessIdentityPresence Check(int processId, DateTimeOffset processStartedAtUtc)
    {
        if (processId <= 0 || processStartedAtUtc == default) return ProcessIdentityPresence.Unknown;
        try
        {
            if (_processApiProbe(processId, processStartedAtUtc) is { } result) return result;
        }
        catch
        {
            // An unreliable Process API result must be confirmed by the independent CIM source.
        }

        try { return CheckCimAsync(processId, processStartedAtUtc).GetAwaiter().GetResult(); }
        catch { return ProcessIdentityPresence.Unknown; }
    }

    private static ProcessIdentityPresence? CheckProcessApi(int processId, DateTimeOffset processStartedAtUtc)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.HasExited)
                return process.StartTime.ToUniversalTime() == processStartedAtUtc.UtcDateTime
                    ? ProcessIdentityPresence.Alive : ProcessIdentityPresence.Gone;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                                      System.ComponentModel.Win32Exception)
        {
            // A missing PID or an inaccessible Process API result still needs CIM confirmation.
        }
        return null;
    }

    private async Task<ProcessIdentityPresence> CheckCimAsync(int processId, DateTimeOffset processStartedAtUtc)
    {
        var request = JsonSerializer.Serialize(new
        {
            ProcessId = processId,
            StartedAtUtc = processStartedAtUtc.UtcDateTime.ToString("yyyyMMddHHmmss.ffffff'Z'", CultureInfo.InvariantCulture)
        });
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand",
                     Convert.ToBase64String(Encoding.Unicode.GetBytes(ReadOnlyScript)) })
            start.ArgumentList.Add(argument);

        using var helper = new Process { StartInfo = start };
        using var deadline = new CancellationTokenSource(_timeout);
        if (!helper.Start()) return ProcessIdentityPresence.Unknown;
        var output = helper.StandardOutput.ReadToEndAsync();
        var error = helper.StandardError.ReadToEndAsync();
        try
        {
            await helper.StandardInput.WriteAsync(request.AsMemory(), deadline.Token).ConfigureAwait(false);
            helper.StandardInput.Close();
            await helper.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            var responseText = await output.WaitAsync(deadline.Token).ConfigureAwait(false);
            _ = await error.WaitAsync(deadline.Token).ConfigureAwait(false);
            if (helper.ExitCode != 0) return ProcessIdentityPresence.Unknown;
            var response = JsonSerializer.Deserialize<ProbeResponse>(responseText);
            return response?.Status switch
            {
                "Alive" => ProcessIdentityPresence.Alive,
                "Gone" => ProcessIdentityPresence.Gone,
                _ => ProcessIdentityPresence.Unknown
            };
        }
        finally
        {
            try
            {
                if (!helper.HasExited) helper.Kill(entireProcessTree: false);
                using var exitDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await helper.WaitForExitAsync(exitDeadline.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException) { }
        }
    }

    private sealed record ProbeResponse(string? Status);

    // The request is JSON on stdin. No PID or timestamp is inserted into script text.
    private const string ReadOnlyScript = """
        $ErrorActionPreference = 'Stop'
        $ProgressPreference = 'SilentlyContinue'
        [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
        try {
            $request = [Console]::In.ReadToEnd() | ConvertFrom-Json
            if ([int]$request.ProcessId -le 0) { throw 'Invalid process identity.' }
            $filter = 'ProcessId = {0}' -f [int]$request.ProcessId
            $process = Get-CimInstance -ClassName Win32_Process -Filter $filter -OperationTimeoutSec 1
            if ($null -eq $process) {
                [Console]::Out.Write('{"Status":"Gone"}')
                exit 0
            }
            $created = $process.CreationDate.ToUniversalTime().ToString("yyyyMMddHHmmss.ffffff'Z'", [Globalization.CultureInfo]::InvariantCulture)
            if ($created -ceq [string]$request.StartedAtUtc) {
                [Console]::Out.Write('{"Status":"Alive"}')
            } else {
                [Console]::Out.Write('{"Status":"Gone"}')
            }
            exit 0
        }
        catch {
            [Console]::Out.Write('{"Status":"Unknown"}')
            exit 1
        }
        """;
}
