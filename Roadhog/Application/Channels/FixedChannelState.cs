using Roadhog.Core.Model;

namespace Roadhog.Application.Channels;

public sealed class FixedChannelState
{
    public FixedChannelCorrectionStep Step { get; private set; } = FixedChannelCorrectionStep.Monitoring;

    public bool CorrectionActive => Step != FixedChannelCorrectionStep.Monitoring;

    public bool NormalWorkSuspended { get; private set; }

    public DateTimeOffset NextChannelReadAt { get; set; } = DateTimeOffset.MinValue;

    public string RevivePathName { get; private set; } = string.Empty;

    public IReadOnlyList<Vector3Snapshot> RevivePoints { get; private set; } = Array.Empty<Vector3Snapshot>();

    public DateTimeOffset NextReturnAttemptAt { get; private set; } = DateTimeOffset.MinValue;

    public DateTimeOffset InitialWaitUntil { get; private set; } = DateTimeOffset.MinValue;

    public bool InitialWaitCompleted { get; private set; }

    public bool ReachedRevivalPoint { get; private set; }

    public uint WaitingMapId { get; private set; }

    public uint SwitchAttemptMapId { get; private set; }

    public DateTimeOffset SwitchVerificationDeadline { get; private set; } = DateTimeOffset.MinValue;

    public int SwitchAttemptCount { get; private set; }

    public DateTimeOffset SwitchAttemptStartedAt { get; private set; } = DateTimeOffset.MinValue;

    public string LastDiagnosticKey { get; private set; } = string.Empty;

    public bool MarkNormalWorkSuspended()
    {
        if (NormalWorkSuspended)
        {
            return false;
        }

        NormalWorkSuspended = true;
        return true;
    }

    public void BeginCorrection(string pathName, IReadOnlyList<Vector3Snapshot> revivePoints)
    {
        Step = FixedChannelCorrectionStep.ReturningToRevivalPoint;
        RevivePathName = pathName;
        RevivePoints = revivePoints;
        NextReturnAttemptAt = DateTimeOffset.MinValue;
        InitialWaitUntil = DateTimeOffset.MinValue;
        InitialWaitCompleted = false;
        ReachedRevivalPoint = false;
        WaitingMapId = 0;
        SwitchAttemptMapId = 0;
        SwitchAttemptStartedAt = DateTimeOffset.MinValue;
        SwitchVerificationDeadline = DateTimeOffset.MinValue;
        SwitchAttemptCount = 0;
        LastDiagnosticKey = string.Empty;
    }

    public void SetRevivePath(string pathName, IReadOnlyList<Vector3Snapshot> revivePoints)
    {
        RevivePathName = pathName;
        RevivePoints = revivePoints;
        LastDiagnosticKey = string.Empty;
    }

    public void MarkReturnAttempt(DateTimeOffset retryAt)
    {
        NextReturnAttemptAt = retryAt;
    }

    public void EnterInitialWait(DateTimeOffset now, TimeSpan wait, uint mapId)
    {
        Step = FixedChannelCorrectionStep.WaitingBeforeSwitch;
        ReachedRevivalPoint = true;
        WaitingMapId = mapId;
        InitialWaitUntil = InitialWaitCompleted ? now : now + wait;
        LastDiagnosticKey = string.Empty;
    }

    public void RestartInitialWait(DateTimeOffset now, TimeSpan wait, uint mapId)
    {
        Step = FixedChannelCorrectionStep.WaitingBeforeSwitch;
        ReachedRevivalPoint = true;
        WaitingMapId = mapId;
        InitialWaitUntil = now + wait;
        InitialWaitCompleted = false;
        LastDiagnosticKey = string.Empty;
    }

    public void LeaveRevivalPoint()
    {
        Step = FixedChannelCorrectionStep.ReturningToRevivalPoint;
        ReachedRevivalPoint = false;
        NextReturnAttemptAt = DateTimeOffset.MinValue;
        if (!InitialWaitCompleted)
        {
            InitialWaitUntil = DateTimeOffset.MinValue;
        }

        LastDiagnosticKey = string.Empty;
    }

    public int StartSwitchAttempt(DateTimeOffset now, TimeSpan verificationWindow, uint mapId)
    {
        InitialWaitCompleted = true;
        Step = FixedChannelCorrectionStep.VerifyingSwitch;
        SwitchAttemptMapId = mapId;
        SwitchAttemptStartedAt = now;
        SwitchVerificationDeadline = now + verificationWindow;
        SwitchAttemptCount++;
        LastDiagnosticKey = string.Empty;
        return SwitchAttemptCount;
    }

    public bool ShouldLog(string diagnosticKey)
    {
        if (string.Equals(LastDiagnosticKey, diagnosticKey, StringComparison.Ordinal))
        {
            return false;
        }

        LastDiagnosticKey = diagnosticKey;
        return true;
    }

    public void Reset(DateTimeOffset nextReadAt)
    {
        Step = FixedChannelCorrectionStep.Monitoring;
        NormalWorkSuspended = false;
        NextChannelReadAt = nextReadAt;
        RevivePathName = string.Empty;
        RevivePoints = Array.Empty<Vector3Snapshot>();
        NextReturnAttemptAt = DateTimeOffset.MinValue;
        InitialWaitUntil = DateTimeOffset.MinValue;
        InitialWaitCompleted = false;
        ReachedRevivalPoint = false;
        WaitingMapId = 0;
        SwitchAttemptMapId = 0;
        SwitchAttemptStartedAt = DateTimeOffset.MinValue;
        SwitchVerificationDeadline = DateTimeOffset.MinValue;
        SwitchAttemptCount = 0;
        LastDiagnosticKey = string.Empty;
    }
}

public enum FixedChannelCorrectionStep
{
    Monitoring,
    ReturningToRevivalPoint,
    WaitingBeforeSwitch,
    VerifyingSwitch
}
