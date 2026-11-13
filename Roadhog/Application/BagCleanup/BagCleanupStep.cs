namespace Roadhog.Application.BagCleanup;

public enum BagCleanupStep
{
    Inactive,
    PrepareDiscardInventory,
    ReadDiscardCandidates,
    DragDiscardItem,
    WaitDiscardConfirm,
    ClickDiscardConfirm,
    VerifyDiscardItem,
    CloseDiscardInventory,
    WaitSafeToReturn,
    PressTownReturn,
    WaitTownReturnSettle,
    LoadCleanupPath,
    FollowCleanupPath,
    SelectCleanupNpc,
    OpenNpcDialog,
    ClickSellItemEntry,
    PostCleanupJump,
    PressReturnToRevive,
    WaitReturnToReviveSettle,
    ReturnByReversePath,
    Complete,
    Failed,
    Aborted,
    WaitDiscardRetry,
    BuyArrows
}
