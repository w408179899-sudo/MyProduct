using System.Diagnostics;
using Roadhog.Infrastructure.WorkerProcesses;

internal static class WorkerProcessExitReconcilerTests
{
    private static readonly string PowerShellPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");

    public static Task MatchingIdentityTerminatesAndConfirmsAbsenceAsync() => WithDummyAsync(async dummy =>
    {
        var descriptor = DescriptorFor(dummy);
        await new WindowsWorkerProcessExitReconciler().ReconcileAsync(descriptor, PowerShellPath)
            .WaitAsync(TimeSpan.FromSeconds(15));
        Require(dummy.HasExited, "matching identity cleanup terminates the dedicated child");
        await RequireCimAbsentAsync(descriptor.ProcessId);
    });

    public static Task IdentityMismatchNeverTerminatesProcessAsync() => WithDummyAsync(async dummy =>
    {
        var descriptor = DescriptorFor(dummy);
        var reconciler = new WindowsWorkerProcessExitReconciler();
        await RequireIdentityRejectedAsync(() => reconciler.ReconcileAsync(
            descriptor with { ProcessStartedAtUtc = descriptor.ProcessStartedAtUtc.AddMinutes(-1) }, PowerShellPath));
        Require(!dummy.HasExited, "a reused PID with another start time cannot receive termination");

        var wrongExecutable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        await RequireIdentityRejectedAsync(() => reconciler.ReconcileAsync(descriptor, wrongExecutable));
        Require(!dummy.HasExited, "a process with another full executable path cannot receive termination");

        await reconciler.ReconcileAsync(descriptor, PowerShellPath).WaitAsync(TimeSpan.FromSeconds(15));
        Require(dummy.HasExited, "identity rejection does not prevent later cleanup with the verified identity");
        await RequireCimAbsentAsync(descriptor.ProcessId);
    });

    public static Task AlreadyExitedProcessSucceedsAsync() => WithDummyAsync(async dummy =>
    {
        var descriptor = DescriptorFor(dummy);
        dummy.Kill(entireProcessTree: false);
        await dummy.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await RequireCimAbsentAsync(descriptor.ProcessId);

        var reconciler = new WindowsWorkerProcessExitReconciler();
        await reconciler.ReconcileAsync(descriptor, PowerShellPath).WaitAsync(TimeSpan.FromSeconds(15));
        await reconciler.ReconcileAsync(descriptor, PowerShellPath).WaitAsync(TimeSpan.FromSeconds(15));
        Require(dummy.HasExited, "an absent process can be reconciled repeatedly without an error");
    });

    public static Task HelperDeadlineAllowsRetryWithSameIdentityAsync() => WithDummyAsync(async dummy =>
    {
        var descriptor = DescriptorFor(dummy);
        var elapsed = Stopwatch.StartNew();
        var deadlineExpired = false;
        try
        {
            await new WindowsWorkerProcessExitReconciler(TimeSpan.FromMilliseconds(50))
                .ReconcileAsync(descriptor, PowerShellPath).WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            deadlineExpired = true;
        }
        Require(deadlineExpired, "the helper reports its expired deadline instead of claiming successful cleanup");
        Require(elapsed.Elapsed < TimeSpan.FromSeconds(5), "the 50 ms helper deadline is bounded independently of the sleeping target");
        // Timeout can occur after termination but before its reply; either target state must remain retryable.
        await new WindowsWorkerProcessExitReconciler().ReconcileAsync(descriptor, PowerShellPath)
            .WaitAsync(TimeSpan.FromSeconds(15));
        Require(dummy.HasExited, "cleanup can be retried after the helper deadline expires");
        await RequireCimAbsentAsync(descriptor.ProcessId);
    });

    private static async Task WithDummyAsync(Func<Process, Task> test)
    {
        using var dummy = new Process { StartInfo = PowerShellStart("Start-Sleep -Seconds 30") };
        Require(dummy.Start(), "start a dedicated PowerShell process for exit reconciliation");
        try
        {
            _ = dummy.SafeHandle;
            Require(!dummy.HasExited, "dedicated child starts alive");
            await test(dummy);
        }
        finally
        {
            // This handle belongs only to the process created by this test.
            if (!dummy.HasExited) dummy.Kill(entireProcessTree: false);
            await dummy.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static ProcessStartInfo PowerShellStart(string command)
    {
        var start = new ProcessStartInfo(PowerShellPath)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(command);
        return start;
    }

    private static WorkerDescriptor DescriptorFor(Process dummy) => new()
    {
        InstanceId = Guid.NewGuid().ToString("N"), AccountName = "exit-reconciler-test",
        PipeName = "exit-reconciler-test", Token = new string('A', 64),
        ProcessId = dummy.Id, ProcessStartedAtUtc = dummy.StartTime.ToUniversalTime()
    };

    private static async Task RequireIdentityRejectedAsync(Func<Task> reconcile)
    {
        var rejected = false;
        try { await reconcile().WaitAsync(TimeSpan.FromSeconds(15)); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected, "a mismatched process identity must produce InvalidOperationException");
    }

    private static async Task RequireCimAbsentAsync(int pid)
    {
        var start = PowerShellStart(FormattableString.Invariant(
            $"$ErrorActionPreference = 'Stop'; if ($null -eq (Get-CimInstance -ClassName Win32_Process -Filter 'ProcessId = {pid}' -ErrorAction Stop)) {{ exit 0 }}; exit 1"));
        start.RedirectStandardError = true;
        start.RedirectStandardOutput = true;
        using var probe = new Process { StartInfo = start };
        Require(probe.Start(), "start independent CIM absence check");
        try
        {
            _ = probe.SafeHandle;
            var error = probe.StandardError.ReadToEndAsync();
            var output = probe.StandardOutput.ReadToEndAsync();
            await probe.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Require(probe.ExitCode == 0, "cleanup completes only after the PID disappears from CIM: " + await error + await output);
        }
        finally
        {
            if (!probe.HasExited) probe.Kill(entireProcessTree: false);
            await probe.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
