using Roadhog.Application.BagCleanup;
using Roadhog.Application.PersonalShop;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;
using Roadhog.Core.Radar;

namespace Roadhog.Application;

/// <summary>
/// UI operations executed by the runtime that owns an account's hardware and snapshots.
/// Process implementations transport cancellation and progress separately from request data.
/// Account lifecycle and configuration persistence belong to the control application.
/// </summary>
public interface IRoadhogRuntime
{
    void ApplyRadarObstacleSettings(string accountName, RadarObstacleScriptSettings settings);

    void NotifyRadarMapSaved(uint mapId);

    Task<RadarLiveSnapshot> ReadRadarSnapshotAsync(
        string accountName, CancellationToken cancellationToken = default);

    Task<PlayerSnapshot> ReadPlayerAsync(
        string? accountName = null, CancellationToken cancellationToken = default);

    Task<PlayerAbnormalStatusSnapshot> ReadPlayerAbnormalStatusesAsync(
        string? accountName = null, CancellationToken cancellationToken = default);

    Task<LockedTargetAbnormalStatusSnapshot> ReadLockedTargetAbnormalStatusesAsync(
        string? accountName = null, CancellationToken cancellationToken = default);

    Task<SummonedPetSnapshot> ReadSummonedPetAsync(
        string? accountName = null, CancellationToken cancellationToken = default);

    Task<SummonedPetRosterSnapshot> ReadSummonedPetRosterAsync(
        string? accountName = null, CancellationToken cancellationToken = default);

    Task<TeamSnapshot> ReadTeamSnapshotAsync(
        string? accountName = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SkillSnapshot>> RefreshSkillsAsync(
        string? accountName = null, CancellationToken cancellationToken = default);

    Task<QuickbarSnapshot> ReadQuickbarAsync(
        string? accountName = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorldObjectSnapshot>> RefreshWorldObjectsAsync(
        string? accountName = null, CancellationToken cancellationToken = default);

    Task<PlayerSnapshot> ReadPlayerForVmmDeviceAsync(
        string accountName, string vmmDeviceName, CancellationToken cancellationToken = default);

    Task<GatherSnapshot> RefreshGatherSnapshotAsync(
        string? accountName = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryItemSnapshot>> RefreshInventoryAsync(
        string? accountName = null, CancellationToken cancellationToken = default);

    Task<ChannelTransitionSnapshot> ReadSceneForPathRecordingAsync(
        string? accountName = null, CancellationToken cancellationToken = default);

    Task<PlayerSnapshot> ReadPlayerForPathRecordingAsync(
        string? accountName = null, CancellationToken cancellationToken = default);

#if DEBUG
    Task<OperationResult<RoadhogApiProbeResult>> RunApiProbeAsync(
        string? accountName = null, CancellationToken cancellationToken = default);
#endif

    Task<OperationResult> TestMoveMouseToScreenPointAsync(
        int x, int y, CancellationToken cancellationToken = default);

    Task<OperationResult> TestSwitchChannelAsync(
        string accountName, int targetChannelNumber, CancellationToken cancellationToken = default);

    Task<OperationResult> NormalizeInventoryWindowToTopLeftAndCloseAsync(
        string? accountName = null, CancellationToken cancellationToken = default);

    Task<OperationResult> NormalizeInventoryWindowToTopLeftAsync(
        string? accountName = null, CancellationToken cancellationToken = default);

    Task<OperationResult<BagCleanupSellRegistrationResult>> TestRegisterBagCleanupSellItemsAsync(
        string? accountName, MaintenanceScriptSettings settings, CancellationToken cancellationToken = default);

    Task<OperationResult<BagCleanupManualTestResult>> TestBagCleanupFromNpcAsync(
        string? accountName, string npcName, MaintenanceScriptSettings settings,
        CancellationToken cancellationToken = default);

    Task<OperationResult> ExecutePathAsync(
        string accountName, string pathName, IReadOnlyList<SharedPathPoint> points,
        ScriptSettings? settings = null, CancellationToken cancellationToken = default);

    Task<OperationResult<PersonalShopTestResult>> TestPersonalShopAsync(
        string accountName, MaintenanceScriptSettings settings,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    Task<OperationResult<InventoryDiscardTestResult>> TestInventoryDiscardAsync(
        string accountName, MaintenanceScriptSettings settings,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    Task<OperationResult<string>> TestAuctionHouseAsync(
        string accountName, IReadOnlyList<BagCleanupTradeItemConfig> items,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default,
        string? configuredNpcName = null);
}
