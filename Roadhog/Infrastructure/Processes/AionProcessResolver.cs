using System.Diagnostics;
using Roadhog.Core.Common;
using Roadhog.Core.Processes;

namespace Roadhog.Infrastructure.Processes;

public sealed class AionProcessResolver : ITargetProcessResolver
{
    private readonly AionProcessResolverOptions _options;

    public AionProcessResolver(AionProcessResolverOptions options)
    {
        _options = options;
    }

    public string ResolveTargetProcessName(string? overrideProcessName = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideProcessName))
        {
            return overrideProcessName.Trim();
        }

        var envProcessName = Environment.GetEnvironmentVariable(_options.ProcessNameEnvironmentVariable);
        return string.IsNullOrWhiteSpace(envProcessName)
            ? _options.DefaultProcessName
            : envProcessName.Trim();
    }

    public IReadOnlyList<TargetProcessInfo> ListTargets(string? overrideProcessName = null)
    {
        var targetName = ResolveTargetProcessName(overrideProcessName);
        return Process.GetProcesses()
            .Where(process => MatchesTargetName(process, targetName))
            .Select(ToProcessInfo)
            .OrderBy(process => process.ProcessId)
            .ToArray();
    }

    public OperationResult<ProcessBinding> BindByPid(string accountName, int processId, string? overrideProcessName = null)
    {
        if (string.IsNullOrWhiteSpace(accountName))
        {
            return OperationResult<ProcessBinding>.Fail("Account name cannot be empty.");
        }

        if (processId <= 0)
        {
            return OperationResult<ProcessBinding>.Fail("ProcessId must be greater than zero before binding.");
        }

        var targetName = ResolveTargetProcessName(overrideProcessName);
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return OperationResult<ProcessBinding>.Fail("Process not found: " + processId);
        }

        using (process)
        {
            if (!MatchesTargetName(process, targetName))
            {
                return OperationResult<ProcessBinding>.Fail(
                    $"Process {processId} does not match target process '{targetName}'.");
            }

            return OperationResult<ProcessBinding>.Ok(new ProcessBinding(
                accountName,
                process.Id,
                GetDisplayProcessName(process, targetName),
                DateTimeOffset.Now));
        }
    }

    public OperationResult<ProcessBinding> TryAutoBind(string accountName, string? overrideProcessName = null)
    {
        if (string.IsNullOrWhiteSpace(accountName))
        {
            return OperationResult<ProcessBinding>.Fail("Account name cannot be empty.");
        }

        var targetName = ResolveTargetProcessName(overrideProcessName);
        var targets = ListTargets(targetName);
        if (targets.Count == 0)
        {
            return OperationResult<ProcessBinding>.Fail("Target process not found: " + targetName);
        }

        if (targets.Count > 1)
        {
            var pids = string.Join(", ", targets.Select(target => target.ProcessId));
            return OperationResult<ProcessBinding>.Fail(
                $"Multiple '{targetName}' processes found. Set ProcessId for account '{accountName}'. PIDs: {pids}");
        }

        var target = targets[0];
        return OperationResult<ProcessBinding>.Ok(new ProcessBinding(
            accountName,
            target.ProcessId,
            target.ProcessName,
            DateTimeOffset.Now));
    }

    private static TargetProcessInfo ToProcessInfo(Process process)
    {
        return new TargetProcessInfo(
            process.Id,
            GetSafeProcessName(process),
            GetSafeMainWindowTitle(process),
            GetSafeFileName(process),
            GetSafeStartTime(process));
    }

    private static bool MatchesTargetName(Process process, string targetName)
    {
        var expectedFileName = Path.GetFileName(targetName);
        var expectedBaseName = Path.GetFileNameWithoutExtension(targetName);
        var processName = GetSafeProcessName(process);

        if (EqualsName(processName, targetName) || EqualsName(processName, expectedFileName) || EqualsName(processName, expectedBaseName))
        {
            return true;
        }

        var fileName = GetSafeFileName(process);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var actualFileName = Path.GetFileName(fileName);
        var actualBaseName = Path.GetFileNameWithoutExtension(fileName);
        return EqualsName(actualFileName, expectedFileName)
            || EqualsName(actualFileName, targetName)
            || EqualsName(actualBaseName, expectedBaseName)
            || EqualsName(actualBaseName, targetName);
    }

    private static string GetDisplayProcessName(Process process, string fallbackName)
    {
        var fileName = GetSafeFileName(process);
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            return Path.GetFileName(fileName);
        }

        var processName = GetSafeProcessName(process);
        return string.IsNullOrWhiteSpace(processName) ? fallbackName : processName;
    }

    private static bool EqualsName(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left)
            && !string.IsNullOrWhiteSpace(right)
            && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string GetSafeProcessName(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string? GetSafeMainWindowTitle(Process process)
    {
        try
        {
            return process.MainWindowTitle;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetSafeFileName(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset? GetSafeStartTime(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch
        {
            return null;
        }
    }
}
