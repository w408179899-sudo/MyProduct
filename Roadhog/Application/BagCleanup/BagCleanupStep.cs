namespace Roadhog.Application.BagCleanup;

public enum BagCleanupStep
{
    Inactive,
    WaitSafeToReturn,
    PressTownReturn,
    WaitTownReturnSettle,
    LoadCleanupPath,
    FollowCleanupPath,
    SelectCleanupNpc,
    OpenNpcDialog,
    ClickSellItemEntry,
    NormalizeInventoryWindow,
    ReadSellCandidates,
    RegisterSellItems,
    ClickSellButton,
    VerifyInventory,
    ReturnByReversePath,
    Complete,
    Failed,
    Aborted
}
