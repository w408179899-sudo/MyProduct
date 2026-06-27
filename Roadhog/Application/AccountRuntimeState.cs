namespace Roadhog.Application;

public sealed class AccountRuntimeState
{
    public AccountRuntimeState(string accountName, string characterName = "")
    {
        AccountName = string.IsNullOrWhiteSpace(accountName)
            ? throw new ArgumentException("Account name cannot be empty.", nameof(accountName))
            : accountName;
        CharacterName = characterName;
    }

    public string AccountName { get; }

    public string CharacterName { get; private set; }

    public int ProcessId { get; private set; }

    public string TargetProcessName { get; private set; } = string.Empty;

    public string HardwareKey { get; private set; } = string.Empty;

    public string HardwareBindingKind { get; private set; } = string.Empty;

    public string HardwareBindingConfidence { get; private set; } = string.Empty;

    public string HardwareDeviceInstanceId { get; private set; } = string.Empty;

    public string HardwareLocationKey { get; private set; } = string.Empty;

    public string HardwareDisplayName { get; private set; } = string.Empty;

    public string VmmDeviceName { get; private set; } = string.Empty;

    public string Status { get; private set; } = "idle";

    public int? ThreadId { get; private set; }

    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.Now;

    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.Now;

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? StoppedAt { get; private set; }

    public DateTimeOffset? LastHeartbeatAt { get; private set; }

    public string? LastError { get; private set; }

    public int KillCount { get; private set; }

    public DateTimeOffset? LastKillAt { get; private set; }

    public bool StopRequested { get; private set; }

    public void ApplyAccountInfo(string characterName, AccountConfigSnapshot config)
    {
        CharacterName = characterName;
        ProcessId = config.ProcessId;
        TargetProcessName = config.TargetProcessName;
        HardwareKey = config.HardwareKey;
        HardwareBindingKind = config.HardwareBindingKind;
        HardwareBindingConfidence = config.HardwareBindingConfidence;
        HardwareDeviceInstanceId = config.HardwareDeviceInstanceId;
        HardwareLocationKey = config.HardwareLocationKey;
        HardwareDisplayName = config.HardwareDisplayName;
        VmmDeviceName = config.VmmDeviceName;
        Touch();
    }

    public void MarkStarting(AccountConfigSnapshot config)
    {
        CharacterName = config.CharacterName;
        ProcessId = config.ProcessId;
        TargetProcessName = config.TargetProcessName;
        HardwareKey = config.HardwareKey;
        HardwareBindingKind = config.HardwareBindingKind;
        HardwareBindingConfidence = config.HardwareBindingConfidence;
        HardwareDeviceInstanceId = config.HardwareDeviceInstanceId;
        HardwareLocationKey = config.HardwareLocationKey;
        HardwareDisplayName = config.HardwareDisplayName;
        VmmDeviceName = config.VmmDeviceName;
        Status = "starting";
        ThreadId = null;
        StopRequested = false;
        LastError = null;
        StartedAt = null;
        StoppedAt = null;
        LastHeartbeatAt = null;
        KillCount = 0;
        LastKillAt = null;
        Touch();
    }

    public void MarkRunning(int threadId)
    {
        Status = "running";
        ThreadId = threadId;
        StopRequested = false;
        StartedAt ??= DateTimeOffset.Now;
        LastHeartbeatAt = DateTimeOffset.Now;
        Touch();
    }

    public void MarkHeartbeat()
    {
        LastHeartbeatAt = DateTimeOffset.Now;
        Touch();
    }

    public void RequestStop()
    {
        StopRequested = true;
        Status = "stopping";
        Touch();
    }

    public void MarkStopped()
    {
        Status = "idle";
        ProcessId = 0;
        ThreadId = null;
        StopRequested = false;
        StoppedAt = DateTimeOffset.Now;
        Touch();
    }

    public void MarkFailed(string error)
    {
        Status = "failed";
        LastError = error;
        ThreadId = null;
        StopRequested = false;
        StoppedAt = DateTimeOffset.Now;
        Touch();
    }

    public void MarkKill()
    {
        KillCount++;
        LastKillAt = DateTimeOffset.Now;
        Touch();
    }

    private void Touch()
    {
        UpdatedAt = DateTimeOffset.Now;
    }

    public sealed record AccountConfigSnapshot(
        string CharacterName,
        int ProcessId,
        string TargetProcessName,
        string HardwareKey,
        string HardwareBindingKind,
        string HardwareBindingConfidence,
        string HardwareDeviceInstanceId,
        string HardwareLocationKey,
        string HardwareDisplayName,
        string VmmDeviceName);
}
