namespace Roadhog.Application.BagCleanup;

public enum BagCleanupTickStatus
{
    NotStarted,
    Running,
    Completed,
    Skipped,
    RecoverableFailure,
    FatalFailure
}

public sealed record BagCleanupTickResult(
    BagCleanupTickStatus Status,
    string Reason,
    string? Error = null)
{
    public static BagCleanupTickResult NotStarted(string reason)
    {
        return new BagCleanupTickResult(BagCleanupTickStatus.NotStarted, reason);
    }

    public static BagCleanupTickResult Running(string reason)
    {
        return new BagCleanupTickResult(BagCleanupTickStatus.Running, reason);
    }

    public static BagCleanupTickResult Completed(string reason)
    {
        return new BagCleanupTickResult(BagCleanupTickStatus.Completed, reason);
    }

    public static BagCleanupTickResult Skipped(string reason)
    {
        return new BagCleanupTickResult(BagCleanupTickStatus.Skipped, reason);
    }

    public static BagCleanupTickResult RecoverableFailure(string reason, string error)
    {
        return new BagCleanupTickResult(BagCleanupTickStatus.RecoverableFailure, reason, error);
    }

    public static BagCleanupTickResult FatalFailure(string reason, string error)
    {
        return new BagCleanupTickResult(BagCleanupTickStatus.FatalFailure, reason, error);
    }
}
