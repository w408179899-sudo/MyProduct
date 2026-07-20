namespace Roadhog.Application;

public sealed class AccountRuntimeState
{
    private static readonly TimeSpan DuplicateEntityKillSuppressWindow = TimeSpan.FromSeconds(10);

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

    public string? LastWarning { get; private set; }

    public DateTimeOffset? LastWarningAt { get; private set; }

    public int KillCount { get; private set; }

    public DateTimeOffset? FirstKillAt { get; private set; }

    public DateTimeOffset? LastKillAt { get; private set; }

    private ushort LastKillTargetEntityId { get; set; }

    private uint LastKillTargetServerObjectId { get; set; }

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
        LastWarning = null;
        LastWarningAt = null;
        StartedAt = null;
        StoppedAt = null;
        LastHeartbeatAt = null;
        KillCount = 0;
        FirstKillAt = null;
        LastKillAt = null;
        LastKillTargetEntityId = 0;
        LastKillTargetServerObjectId = 0;
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

    public bool MarkWarning(string? warning)
    {
        var normalized = warning?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return ClearWarning();
        }

        var changed = !string.Equals(LastWarning, normalized, StringComparison.Ordinal);
        LastWarning = normalized;
        LastWarningAt = DateTimeOffset.Now;
        Touch();
        return changed;
    }

    public bool ClearWarning()
    {
        if (LastWarning is null && LastWarningAt is null)
        {
            return false;
        }

        LastWarning = null;
        LastWarningAt = null;
        Touch();
        return true;
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
        LastWarning = null;
        LastWarningAt = null;
        Touch();
    }

    public void MarkFailed(string error)
    {
        Status = "failed";
        LastError = error;
        ThreadId = null;
        StopRequested = false;
        StoppedAt = DateTimeOffset.Now;
        LastWarning = null;
        LastWarningAt = null;
        Touch();
    }

    public bool MarkKill(
        ushort targetEntityId = 0,
        uint targetServerObjectId = 0,
        DateTimeOffset? killedAt = null)
    {
        var now = killedAt ?? DateTimeOffset.Now;
        if (IsDuplicateKill(targetEntityId, targetServerObjectId, now))
        {
            return false;
        }

        KillCount++;
        FirstKillAt ??= now;
        LastKillAt = now;
        LastKillTargetEntityId = targetEntityId;
        LastKillTargetServerObjectId = targetServerObjectId;
        Touch();
        return true;
    }

    private bool IsDuplicateKill(ushort targetEntityId, uint targetServerObjectId, DateTimeOffset now)
    {
        if (KillCount <= 0 || LastKillAt is not { } lastKillAt)
        {
            return false;
        }

        if (targetServerObjectId != 0 && targetServerObjectId == LastKillTargetServerObjectId)
        {
            return true;
        }

        return targetServerObjectId == 0 &&
               targetEntityId != 0 &&
               targetEntityId == LastKillTargetEntityId &&
               now - lastKillAt <= DuplicateEntityKillSuppressWindow;
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
