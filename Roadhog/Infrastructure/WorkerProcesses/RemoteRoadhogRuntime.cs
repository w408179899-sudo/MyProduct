using Roadhog.Application;
using Roadhog.Application.BagCleanup;
using Roadhog.Application.PersonalShop;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;
using Roadhog.Core.Radar;

namespace Roadhog.Infrastructure.WorkerProcesses;

public sealed class RemoteRoadhogRuntime : IRoadhogRuntime
{
    private readonly WorkerRpcClient _client;
    private readonly string _accountName;
    private readonly Action<string, object?[]>? _notify;

    public RemoteRoadhogRuntime(WorkerRpcClient client, string accountName, Action<string, object?[]>? notify = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        _client = client;
        _accountName = accountName;
        _notify = notify;
    }

    private string Account(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested)
            && !string.Equals(requested, _accountName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The runtime proxy is bound to a different account.");
        return _accountName;
    }

    private void Send(string method, object?[] arguments)
    {
        if (_notify is not null)
        {
            _notify(method, arguments);
            return;
        }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        _client.CallAsync<object?>(method, arguments, deadline.Token).GetAwaiter().GetResult();
    }

    public void ApplyRadarObstacleSettings(string accountName, RadarObstacleScriptSettings settings) =>
        Send(nameof(ApplyRadarObstacleSettings), new object?[] { Account(accountName), settings.Clone() });

    public void NotifyRadarMapSaved(uint mapId) => Send(nameof(NotifyRadarMapSaved), new object?[] { mapId });

    public Task<RadarLiveSnapshot> ReadRadarSnapshotAsync(string accountName, CancellationToken cancellationToken = default) =>
        _client.CallAsync<RadarLiveSnapshot>(nameof(ReadRadarSnapshotAsync), new object?[] { Account(accountName) }, cancellationToken);

    public Task<PlayerSnapshot> ReadPlayerAsync(string? accountName = null, CancellationToken cancellationToken = default) =>
        _client.CallAsync<PlayerSnapshot>(nameof(ReadPlayerAsync), new object?[] { Account(accountName) }, cancellationToken);

    public Task<PlayerAbnormalStatusSnapshot> ReadPlayerAbnormalStatusesAsync(string? accountName = null, CancellationToken cancellationToken = default) =>
        _client.CallAsync<PlayerAbnormalStatusSnapshot>(nameof(ReadPlayerAbnormalStatusesAsync), new object?[] { Account(accountName) }, cancellationToken);

    public Task<LockedTargetAbnormalStatusSnapshot> ReadLockedTargetAbnormalStatusesAsync(string? accountName = null, CancellationToken cancellationToken = default) =>
        _client.CallAsync<LockedTargetAbnormalStatusSnapshot>(nameof(ReadLockedTargetAbnormalStatusesAsync), new object?[] { Account(accountName) }, cancellationToken);

    public Task<SummonedPetSnapshot> ReadSummonedPetAsync(string? accountName = null, CancellationToken cancellationToken = default) =>
        _client.CallAsync<SummonedPetSnapshot>(nameof(ReadSummonedPetAsync), new object?[] { Account(accountName) }, cancellationToken);

    public Task<SummonedPetRosterSnapshot> ReadSummonedPetRosterAsync(string? accountName = null, CancellationToken cancellationToken = default) =>
        _client.CallAsync<SummonedPetRosterSnapshot>(nameof(ReadSummonedPetRosterAsync), new object?[] { Account(accountName) }, cancellationToken);

    public Task<TeamSnapshot> ReadTeamSnapshotAsync(string? accountName = null, CancellationToken cancellationToken = default) =>
        _client.CallAsync<TeamSnapshot>(nameof(ReadTeamSnapshotAsync), new object?[] { Account(accountName) }, cancellationToken);

    public Task<IReadOnlyList<SkillSnapshot>> RefreshSkillsAsync(string? accountName = null, CancellationToken cancellationToken = default) =>
        _client.CallAsync<IReadOnlyList<SkillSnapshot>>(nameof(RefreshSkillsAsync), new object?[] { Account(accountName) }, cancellationToken);

    public Task<QuickbarSnapshot> ReadQuickbarAsync(string? accountName = null, CancellationToken cancellationToken = default) =>
        _client.CallAsync<QuickbarSnapshot>(nameof(ReadQuickbarAsync), new object?[] { Account(accountName) }, cancellationToken);

    public Task<IReadOnlyList<WorldObjectSnapshot>> RefreshWorldObjectsAsync(string? accountName = null, CancellationToken cancellationToken = default) =>
        _client.CallAsync<IReadOnlyList<WorldObjectSnapshot>>(nameof(RefreshWorldObjectsAsync), new object?[] { Account(accountName) }, cancellationToken);

    public Task<PlayerSnapshot> ReadPlayerForVmmDeviceAsync(string accountName, string vmmDeviceName, CancellationToken cancellationToken = default) =>
        _client.CallAsync<PlayerSnapshot>(nameof(ReadPlayerForVmmDeviceAsync), new object?[] { Account(accountName), vmmDeviceName }, cancellationToken);

    public Task<GatherSnapshot> RefreshGatherSnapshotAsync(string? accountName = null, CancellationToken cancellationToken = default) =>
        _client.CallAsync<GatherSnapshot>(nameof(RefreshGatherSnapshotAsync), new object?[] { Account(accountName) }, cancellationToken);

    public Task<IReadOnlyList<InventoryItemSnapshot>> RefreshInventoryAsync(string? accountName = null, CancellationToken cancellationToken = default) =>
        _client.CallAsync<IReadOnlyList<InventoryItemSnapshot>>(nameof(RefreshInventoryAsync), new object?[] { Account(accountName) }, cancellationToken);

    public Task<ChannelTransitionSnapshot> ReadSceneForPathRecordingAsync(string? accountName = null, CancellationToken cancellationToken = default) =>
        _client.CallAsync<ChannelTransitionSnapshot>(nameof(ReadSceneForPathRecordingAsync), new object?[] { Account(accountName) }, cancellationToken);

    public Task<PlayerSnapshot> ReadPlayerForPathRecordingAsync(string? accountName = null, CancellationToken cancellationToken = default) =>
        _client.CallAsync<PlayerSnapshot>(nameof(ReadPlayerForPathRecordingAsync), new object?[] { Account(accountName) }, cancellationToken);

#if DEBUG
    public Task<OperationResult<RoadhogApiProbeResult>> RunApiProbeAsync(string? accountName = null, CancellationToken cancellationToken = default) =>
        _client.CallAsync<OperationResult<RoadhogApiProbeResult>>(nameof(RunApiProbeAsync), new object?[] { Account(accountName) }, cancellationToken);
#endif

    public Task<OperationResult> TestMoveMouseToScreenPointAsync(int x, int y, CancellationToken cancellationToken = default) =>
        _client.CallAsync<OperationResult>(nameof(TestMoveMouseToScreenPointAsync), new object?[] { x, y }, cancellationToken);

    public Task<OperationResult> TestSwitchChannelAsync(string accountName, int targetChannelNumber, CancellationToken cancellationToken = default) =>
        _client.CallAsync<OperationResult>(nameof(TestSwitchChannelAsync), new object?[] { Account(accountName), targetChannelNumber }, cancellationToken);

    public Task<OperationResult> NormalizeInventoryWindowToTopLeftAndCloseAsync(string? accountName = null, CancellationToken cancellationToken = default) =>
        _client.CallAsync<OperationResult>(nameof(NormalizeInventoryWindowToTopLeftAndCloseAsync), new object?[] { Account(accountName) }, cancellationToken);

    public Task<OperationResult> NormalizeInventoryWindowToTopLeftAsync(string? accountName = null, CancellationToken cancellationToken = default) =>
        _client.CallAsync<OperationResult>(nameof(NormalizeInventoryWindowToTopLeftAsync), new object?[] { Account(accountName) }, cancellationToken);

    public Task<OperationResult<BagCleanupSellRegistrationResult>> TestRegisterBagCleanupSellItemsAsync(string? accountName,
        MaintenanceScriptSettings settings, CancellationToken cancellationToken = default) =>
        _client.CallAsync<OperationResult<BagCleanupSellRegistrationResult>>(nameof(TestRegisterBagCleanupSellItemsAsync),
            new object?[] { Account(accountName), settings }, cancellationToken);

    public Task<OperationResult<BagCleanupManualTestResult>> TestBagCleanupFromNpcAsync(string? accountName, string npcName,
        MaintenanceScriptSettings settings, CancellationToken cancellationToken = default) =>
        _client.CallAsync<OperationResult<BagCleanupManualTestResult>>(nameof(TestBagCleanupFromNpcAsync),
            new object?[] { Account(accountName), npcName, settings }, cancellationToken);

    public Task<OperationResult> ExecutePathAsync(string accountName, string pathName, IReadOnlyList<SharedPathPoint> points,
        ScriptSettings? settings = null, CancellationToken cancellationToken = default) =>
        _client.CallAsync<OperationResult>(nameof(ExecutePathAsync), new object?[] { Account(accountName), pathName, points, settings }, cancellationToken);

    public Task<OperationResult<PersonalShopTestResult>> TestPersonalShopAsync(string accountName, MaintenanceScriptSettings settings,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        _client.CallAsync<OperationResult<PersonalShopTestResult>>(nameof(TestPersonalShopAsync),
            new object?[] { Account(accountName), settings }, cancellationToken, progress);

    public Task<OperationResult<InventoryDiscardTestResult>> TestInventoryDiscardAsync(string accountName, MaintenanceScriptSettings settings,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        _client.CallAsync<OperationResult<InventoryDiscardTestResult>>(nameof(TestInventoryDiscardAsync),
            new object?[] { Account(accountName), settings }, cancellationToken, progress);

    public Task<OperationResult<string>> TestAuctionHouseAsync(string accountName, IReadOnlyList<BagCleanupTradeItemConfig> items,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default, string? configuredNpcName = null) =>
        _client.CallAsync<OperationResult<string>>(nameof(TestAuctionHouseAsync),
            new object?[] { Account(accountName), items, configuredNpcName }, cancellationToken, progress);
}
