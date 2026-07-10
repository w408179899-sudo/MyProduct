using Roadhog.Core.Model;
using Roadhog.Core.Paths;

namespace Roadhog.Application.BagCleanup;

public sealed class BagCleanupState
{
    public BagCleanupStep Step { get; private set; } = BagCleanupStep.Inactive;

    public DateTimeOffset StartedAt { get; private set; } = DateTimeOffset.MinValue;

    public DateTimeOffset StepStartedAt { get; private set; } = DateTimeOffset.MinValue;

    public int RetryCount { get; private set; }

    public int InitialFreeSlots { get; private set; }

    public int TriggerThreshold { get; private set; }

    public int InitialCandidateCount { get; private set; }

    public ulong? InitialMoney { get; private set; }

    public Vector3Snapshot? TownReturnStartPosition { get; private set; }

    public DateTimeOffset LastCompletedAt { get; private set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastFailedAt { get; private set; } = DateTimeOffset.MinValue;

    public string LastFailureReason { get; private set; } = string.Empty;

    public string ReturnAfterFailureReason { get; private set; } = string.Empty;

    public string ReturnAfterFailureError { get; private set; } = string.Empty;

    public string PathName { get; private set; } = string.Empty;

    public string CleanupNpcName { get; private set; } = string.Empty;

    public SharedPathDocument? CleanupPath { get; private set; }

    public IReadOnlyList<InventoryItemSnapshot> SellCandidates { get; private set; } =
        Array.Empty<InventoryItemSnapshot>();

    public bool HasPressedTownReturn { get; private set; }

    public bool HasOpenedNpcDialog { get; private set; }

    public bool HasClickedSellItemEntry { get; private set; }

    public bool HasNormalizedInventoryWindow { get; private set; }

    public bool HasRegisteredSellItems { get; private set; }

    public bool HasClickedSellButton { get; private set; }

    public bool IsReturningAfterFailure => !string.IsNullOrWhiteSpace(ReturnAfterFailureReason);

    public bool Active => Step != BagCleanupStep.Inactive &&
                          Step != BagCleanupStep.Complete &&
                          Step != BagCleanupStep.Failed &&
                          Step != BagCleanupStep.Aborted;

    public void Start(int freeSlots, int threshold)
    {
        Step = BagCleanupStep.WaitSafeToReturn;
        StartedAt = DateTimeOffset.Now;
        StepStartedAt = StartedAt;
        RetryCount = 0;
        InitialFreeSlots = freeSlots;
        TriggerThreshold = threshold;
        InitialCandidateCount = 0;
        InitialMoney = null;
        ReturnAfterFailureReason = string.Empty;
        ReturnAfterFailureError = string.Empty;
        TownReturnStartPosition = null;
        PathName = string.Empty;
        CleanupNpcName = string.Empty;
        CleanupPath = null;
        SellCandidates = Array.Empty<InventoryItemSnapshot>();
        HasPressedTownReturn = false;
        HasOpenedNpcDialog = false;
        HasClickedSellItemEntry = false;
        HasNormalizedInventoryWindow = false;
        HasRegisteredSellItems = false;
        HasClickedSellButton = false;
    }

    public void SetPath(SharedPathDocument path)
    {
        CleanupPath = path.Clone();
        PathName = path.Name;
        CleanupNpcName = path.CleanupNpcName?.Trim() ?? string.Empty;
    }

    public void SetSellCandidates(IReadOnlyList<InventoryItemSnapshot> candidates)
    {
        SellCandidates = candidates.ToArray();
        InitialCandidateCount = SellCandidates.Count;
    }

    public void SetInitialMoney(ulong money)
    {
        InitialMoney = money;
    }

    public void MarkPressedTownReturn(Vector3Snapshot startPosition)
    {
        TownReturnStartPosition = startPosition;
        HasPressedTownReturn = true;
    }

    public void MarkNpcDialogOpened()
    {
        HasOpenedNpcDialog = true;
    }

    public void MarkSellItemEntryClicked()
    {
        HasClickedSellItemEntry = true;
    }

    public void MarkInventoryWindowNormalized()
    {
        HasNormalizedInventoryWindow = true;
    }

    public void MarkSellItemsRegistered()
    {
        HasRegisteredSellItems = true;
    }

    public void MarkSellButtonClicked()
    {
        HasClickedSellButton = true;
    }

    public void Advance(BagCleanupStep next)
    {
        Step = next;
        StepStartedAt = DateTimeOffset.Now;
        RetryCount = 0;
    }

    public void ReturnAfterFailure(string reason, string error)
    {
        ReturnAfterFailureReason = reason?.Trim() ?? string.Empty;
        ReturnAfterFailureError = error?.Trim() ?? string.Empty;
        Advance(BagCleanupStep.ReturnByReversePath);
    }

    public void IncrementRetry()
    {
        RetryCount++;
    }

    public void Complete()
    {
        Advance(BagCleanupStep.Complete);
    }

    public void MarkCompleted(DateTimeOffset now)
    {
        LastCompletedAt = now;
        LastFailedAt = DateTimeOffset.MinValue;
        LastFailureReason = string.Empty;
    }

    public void Fail()
    {
        Advance(BagCleanupStep.Failed);
    }

    public void MarkFailed(DateTimeOffset now, string reason)
    {
        LastFailedAt = now;
        LastFailureReason = reason?.Trim() ?? string.Empty;
    }

    public bool IsCompletionCooldownActive(DateTimeOffset now, TimeSpan cooldown)
    {
        return cooldown > TimeSpan.Zero &&
               LastCompletedAt != DateTimeOffset.MinValue &&
               now - LastCompletedAt < cooldown;
    }

    public bool IsFailureCooldownActive(DateTimeOffset now, TimeSpan cooldown)
    {
        return cooldown > TimeSpan.Zero &&
               LastFailedAt != DateTimeOffset.MinValue &&
               now - LastFailedAt < cooldown;
    }

    public void Abort()
    {
        Advance(BagCleanupStep.Aborted);
    }

    public void Reset()
    {
        Step = BagCleanupStep.Inactive;
        StartedAt = DateTimeOffset.MinValue;
        StepStartedAt = DateTimeOffset.MinValue;
        RetryCount = 0;
        InitialFreeSlots = 0;
        TriggerThreshold = 0;
        InitialCandidateCount = 0;
        InitialMoney = null;
        ReturnAfterFailureReason = string.Empty;
        ReturnAfterFailureError = string.Empty;
        TownReturnStartPosition = null;
        PathName = string.Empty;
        CleanupNpcName = string.Empty;
        CleanupPath = null;
        SellCandidates = Array.Empty<InventoryItemSnapshot>();
        HasPressedTownReturn = false;
        HasOpenedNpcDialog = false;
        HasClickedSellItemEntry = false;
        HasNormalizedInventoryWindow = false;
        HasRegisteredSellItems = false;
        HasClickedSellButton = false;
    }
}
