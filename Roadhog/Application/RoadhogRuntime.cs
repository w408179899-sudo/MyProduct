using Roadhog.Application.Input;
using Roadhog.Application.BagCleanup;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Radar;
using Roadhog.Application.Team;
using Roadhog.Application.Workers;
using Roadhog.Core.Api;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Hardware;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog.Core.Radar;
using Roadhog.Core.Paths;
using System.Globalization;

namespace Roadhog.Application;

public sealed class RoadhogRuntime
{
    private readonly IRoadhogGameApi _gameApi;
    private readonly IRoadhogLogger _logger;
    private readonly IAccountConfigStore? _accountConfigStore;
    private readonly IHardwareDeviceResolver? _hardwareResolver;
    private readonly IKeyboardInput? _keyboardInput;
    private readonly StationaryCombatController? _stationaryCombatController;
    private readonly StationaryObstacleNavigator? _stationaryObstacleNavigator;
    private readonly RadarLiveSnapshotRegistry? _radarSnapshots;
    private const string InventoryToggleKey = "I";
    private const double InventoryUiMaxWindowX = 699.2;
    private const double InventoryUiMaxWindowY = 324.8;
    private const double InventoryScreenTopLeftTlX = 0.0;
    private const double InventoryScreenTopLeftTlY = 0.0;
    private const double InventoryScreenTopLeftTrX = 808.0;
    private const double InventoryScreenTopLeftTrY = 0.0;
    private const double InventoryScreenTopLeftBlX = 0.0;
    private const double InventoryScreenTopLeftBlY = 378.0;
    private const double InventoryScreenTopLeftBrX = 793.0;
    private const double InventoryScreenTopLeftBrY = 380.0;
    private static readonly TimeSpan InventoryToggleHoldDuration = TimeSpan.FromMilliseconds(35);
    private static readonly TimeSpan InventoryOpenSettleDelay = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan InventoryCloseSettleDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan InventoryDragStartSettleDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan InventoryDragSettleDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan BagCleanupSellRegisterClickHoldDelay = TimeSpan.FromMilliseconds(35);
    private static readonly TimeSpan BagCleanupSellRegisterAfterClickDelay = TimeSpan.FromMilliseconds(80);
    private static readonly int[] InventoryTitleDragXOffsets = { 160 };
    private static readonly int[] InventoryTitleDragYOffsets = { 0, -5, -10, -15, -20, -25, -30, -35, -40 };
    private const int BagSlotColumns = 9;
    private const int BagSlotsPerPage = 27;
    private const double DefaultBagSlot0CenterX = 30.0;
    private const double DefaultBagSlot0CenterY = 86.0;
    private const double DefaultBagSlotStepX = 40.875;
    private const double DefaultBagSlotStepY = 35.5;
    private const double DefaultBagPage1OffsetY = 151.0;
    private const double DefaultBagPage2OffsetY = 298.0;

    public RoadhogRuntime(
        IRoadhogGameApi gameApi,
        IRoadhogLogger logger,
        AccountRuntimeManager accounts,
        AccountOrchestrator orchestrator,
        IAccountConfigStore? accountConfigStore = null,
        IHardwareDeviceResolver? hardwareResolver = null,
        IKeyboardInput? keyboardInput = null,
        StationaryCombatController? stationaryCombatController = null,
        StationaryObstacleNavigator? stationaryObstacleNavigator = null,
        RadarLiveSnapshotRegistry? radarSnapshots = null)
    {
        _gameApi = gameApi;
        _logger = logger;
        _accountConfigStore = accountConfigStore;
        _hardwareResolver = hardwareResolver;
        _keyboardInput = keyboardInput;
        _stationaryCombatController = stationaryCombatController;
        _stationaryObstacleNavigator = stationaryObstacleNavigator;
        _radarSnapshots = radarSnapshots;
        Accounts = accounts;
        Orchestrator = orchestrator;
    }

    public AccountRuntimeManager Accounts { get; }

    public AccountOrchestrator Orchestrator { get; }

    public void ApplyRadarObstacleSettings(string accountName, RadarObstacleScriptSettings settings)
    {
        _stationaryObstacleNavigator?.SetSettingsOverride(accountName, settings);
    }

    public void NotifyRadarMapSaved(uint mapId)
    {
        _stationaryObstacleNavigator?.NotifyMapSaved(mapId);
    }

    public async Task<OperationResult<RadarLiveSnapshot>> ReadRadarSnapshotAsync(
        string accountName,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.Now;
        if (_radarSnapshots is not null &&
            _radarSnapshots.TryGetFresh(
                accountName,
                now,
                TimeSpan.FromMilliseconds(500),
                TimeSpan.FromMilliseconds(750),
                TimeSpan.FromSeconds(2),
                out var cached))
        {
            return OperationResult<RadarLiveSnapshot>.Ok(cached);
        }

        var mapResult = await ReadChannelSnapshotAsync(accountName, cancellationToken).ConfigureAwait(false);
        var playerResult = await ReadPlayerSnapshotAsync(accountName, cancellationToken).ConfigureAwait(false);
        var objectsResult = await ReadWorldObjectsAsync(accountName, cancellationToken).ConfigureAwait(false);
        if (!mapResult.Success || mapResult.Value is null || mapResult.Value.MapId == 0)
        {
            return OperationResult<RadarLiveSnapshot>.Fail(mapResult.Error ?? "MapId is unavailable.");
        }

        if (!playerResult.Success || playerResult.Value?.Position is null)
        {
            return OperationResult<RadarLiveSnapshot>.Fail(playerResult.Error ?? "Player position is unavailable.");
        }

        if (!objectsResult.Success || objectsResult.Value is null)
        {
            return OperationResult<RadarLiveSnapshot>.Fail(objectsResult.Error ?? "World objects are unavailable.");
        }

        var capturedAt = DateTimeOffset.Now;
        _radarSnapshots?.PublishMapId(accountName, mapResult.Value.MapId, capturedAt);
        _radarSnapshots?.PublishPlayer(accountName, playerResult.Value);
        _radarSnapshots?.PublishWorldObjects(accountName, objectsResult.Value, capturedAt);
        return OperationResult<RadarLiveSnapshot>.Ok(new RadarLiveSnapshot(
            mapResult.Value.MapId,
            playerResult.Value,
            objectsResult.Value,
            capturedAt));
    }

    public async Task<OperationResult<PlayerSnapshot>> ReadPlayerAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ReadPlayerSnapshotAsync(accountName, cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            _logger.Info("player.refresh.ok", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["hasPosition"] = result.Value?.Position is not null
            });
        }
        else
        {
            _logger.Warn("player.refresh.failed", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["error"] = result.Error
            });
        }

        return result;
    }

    public async Task<OperationResult<PlayerAbnormalStatusSnapshot>> ReadPlayerAbnormalStatusesAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ReadPlayerAbnormalStatusSnapshotAsync(accountName, cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            _logger.Info("player_abnormal.refresh.ok", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["category2EntryCount"] = result.Value?.Category2EntryCount ?? 0,
                ["category2EntrySummary"] = result.Value?.Category2EntrySummary
            });
        }
        else
        {
            _logger.Warn("player_abnormal.refresh.failed", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["error"] = result.Error
            });
        }

        return result;
    }

    public async Task<OperationResult<LockedTargetAbnormalStatusSnapshot>> ReadLockedTargetAbnormalStatusesAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ReadLockedTargetAbnormalStatusSnapshotAsync(accountName, cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            _logger.Info("locked_target_abnormal.refresh.ok", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["hasTarget"] = result.Value?.HasTarget ?? false,
                ["targetEntityId"] = result.Value?.Target.TargetEntityId ?? 0,
                ["abnormalStatusCount"] = result.Value?.AbnormalStatusCount ?? 0,
                ["physicalDebuffCount"] = result.Value?.PhysicalDebuffCount ?? 0
            });
        }
        else
        {
            _logger.Warn("locked_target_abnormal.refresh.failed", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["error"] = result.Error
            });
        }

        return result;
    }

    public async Task<OperationResult<SummonedPetSnapshot>> ReadSummonedPetAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ReadSummonedPetSnapshotAsync(accountName, cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            _logger.Info("summoned_pet.refresh.ok", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["isSummoned"] = result.Value?.IsSummoned ?? false,
                ["serverObjectId"] = result.Value?.ServerObjectId ?? 0,
                ["templateId"] = result.Value?.NpcTemplateId ?? 0,
                ["name"] = result.Value?.Name
            });
        }
        else
        {
            _logger.Warn("summoned_pet.refresh.failed", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["error"] = result.Error
            });
        }

        return result;
    }

    public async Task<OperationResult<SummonedPetRosterSnapshot>> ReadSummonedPetRosterAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ReadSummonedPetRosterSnapshotAsync(accountName, cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            _logger.Info("summoned_pet_roster.refresh.ok", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["localPetSummoned"] = result.Value?.LocalPlayerPet.IsSummoned ?? false,
                ["partyPetCount"] = result.Value?.PartyMemberPetCount ?? 0,
                ["visibleSummonedPetCount"] = result.Value?.VisibleSummonedPetCount ?? 0
            });
        }
        else
        {
            _logger.Warn("summoned_pet_roster.refresh.failed", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["error"] = result.Error
            });
        }

        return result;
    }

    public Task<OperationResult<TeamSnapshot>> ReadTeamSnapshotAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        var monitor = new TeamMonitor(_gameApi, _logger);
        var context = string.IsNullOrWhiteSpace(accountName)
            ? null
            : CreateReadContext(accountName);
        return monitor.ReadSnapshotAsync(context, cancellationToken);
    }

    public async Task<OperationResult<IReadOnlyList<SkillSnapshot>>> RefreshSkillsAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ReadSkillsAsync(accountName, cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            _logger.Info("skills.refresh.ok", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["count"] = result.Value?.Count ?? 0
            });
        }
        else
        {
            _logger.Warn("skills.refresh.failed", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["error"] = result.Error
            });
        }

        return result;
    }

    public async Task<OperationResult<IReadOnlyList<WorldObjectSnapshot>>> RefreshWorldObjectsAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ReadWorldObjectsAsync(accountName, cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            _logger.Info("world_objects.refresh.ok", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["count"] = result.Value?.Count ?? 0
            });
        }
        else
        {
            _logger.Warn("world_objects.refresh.failed", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["error"] = result.Error
            });
        }

        return result;
    }

    public async Task<OperationResult<GatherSnapshot>> RefreshGatherSnapshotAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ReadGatherSnapshotAsync(accountName, cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            _logger.Info("gather.refresh.ok", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["objects"] = result.Value?.Objects.Count ?? 0,
                ["nearbyPlayers"] = result.Value?.NearbyPlayers.Count ?? 0,
                ["nearbyMonsters"] = result.Value?.NearbyMonsters.Count ?? 0,
                ["monsterDataAvailable"] = result.Value?.MonsterDataAvailable ?? false,
                ["competitionDataAvailable"] = result.Value?.CompetitionDataAvailable ?? false
            });
        }
        else
        {
            _logger.Warn("gather.refresh.failed", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["error"] = result.Error
            });
        }

        return result;
    }

    public async Task<OperationResult<IReadOnlyList<InventoryItemSnapshot>>> RefreshInventoryAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ReadInventoryAsync(accountName, cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            _logger.Info("inventory.refresh.ok", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["count"] = result.Value?.Count ?? 0
            });
        }
        else
        {
            _logger.Warn("inventory.refresh.failed", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["error"] = result.Error
            });
        }

        return result;
    }

    public async Task<OperationResult<PlayerSnapshot>> ReadPlayerForPathRecordingAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ReadPlayerSnapshotAsync(
            accountName,
            cancellationToken,
            bypassMemoryCache: true).ConfigureAwait(false);
        if (result.Success)
        {
            _logger.Info("path_record.player_refresh.ok", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["hasPosition"] = result.Value?.Position is not null,
                ["bypassMemoryCache"] = true
            });
        }
        else
        {
            _logger.Warn("path_record.player_refresh.failed", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["error"] = result.Error,
                ["bypassMemoryCache"] = true
            });
        }

        return result;
    }

#if DEBUG
    public async Task<OperationResult<RoadhogApiProbeResult>> RunApiProbeAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        var checks = new List<RoadhogApiProbeCheckResult>(RoadhogApiProbeResult.RequiredCheckNames.Count);

        checks.Add(await RunApiProbeCheckAsync(
            "Player",
            token => ReadPlayerSnapshotAsync(accountName, token),
            player => "entity=" + player.EntityId.ToString(CultureInfo.InvariantCulture) +
                ", name=" + player.CharacterName +
                ", hp=" + player.CurrentHp.ToString(CultureInfo.InvariantCulture) +
                "/" + player.MaxHp.ToString(CultureInfo.InvariantCulture) +
                ", pos=" + (player.Position is null ? "none" : "ok"),
            cancellationToken).ConfigureAwait(false));

        checks.Add(await RunApiProbeCheckAsync(
            "PlayerAbnormalStatuses",
            token => ReadPlayerAbnormalStatusSnapshotAsync(accountName, token),
            abnormal => "entity=" + abnormal.EntityId.ToString(CultureInfo.InvariantCulture) +
                ", entries=" + abnormal.Entries.Count.ToString(CultureInfo.InvariantCulture) +
                ", category2=" + abnormal.Category2EntryCount.ToString(CultureInfo.InvariantCulture),
            cancellationToken).ConfigureAwait(false));

        checks.Add(await RunApiProbeCheckAsync(
            "LockedTarget",
            token => ReadLockedTargetSnapshotAsync(accountName, token),
            target => "hasTarget=" + target.HasTarget.ToString() +
                ", entity=" + target.TargetEntityId.ToString(CultureInfo.InvariantCulture) +
                ", name=" + target.Name,
            cancellationToken).ConfigureAwait(false));

        checks.Add(await RunApiProbeCheckAsync(
            "LockedTargetAbnormalStatuses",
            token => ReadLockedTargetAbnormalStatusSnapshotAsync(accountName, token),
            abnormal => "hasTarget=" + abnormal.HasTarget.ToString() +
                ", entries=" + abnormal.AbnormalStatusCount.ToString(CultureInfo.InvariantCulture) +
                ", physical=" + abnormal.PhysicalDebuffCount.ToString(CultureInfo.InvariantCulture),
            cancellationToken).ConfigureAwait(false));

        checks.Add(await RunApiProbeCheckAsync(
            "SummonedPet",
            token => ReadSummonedPetSnapshotAsync(accountName, token),
            pet => "summoned=" + pet.IsSummoned.ToString() +
                ", serverObjectId=" + pet.ServerObjectId.ToString(CultureInfo.InvariantCulture) +
                ", name=" + pet.Name,
            cancellationToken).ConfigureAwait(false));

        checks.Add(await RunApiProbeCheckAsync(
            "SummonedPetRoster",
            token => ReadSummonedPetRosterSnapshotAsync(accountName, token),
            roster => "localSummoned=" + roster.LocalPlayerPet.IsSummoned.ToString() +
                ", partyPets=" + roster.PartyMemberPetCount.ToString(CultureInfo.InvariantCulture) +
                ", visiblePets=" + roster.VisibleSummonedPetCount.ToString(CultureInfo.InvariantCulture),
            cancellationToken).ConfigureAwait(false));

        checks.Add(await RunApiProbeCheckAsync(
            "Skills",
            token => ReadSkillsAsync(accountName, token),
            skills => "count=" + skills.Count.ToString(CultureInfo.InvariantCulture),
            cancellationToken).ConfigureAwait(false));

        checks.Add(await RunApiProbeCheckAsync(
            "Inventory",
            token => ReadInventoryAsync(accountName, token),
            items => "count=" + items.Count.ToString(CultureInfo.InvariantCulture),
            cancellationToken).ConfigureAwait(false));

        checks.Add(await RunApiProbeCheckAsync(
            "InventoryMoney",
            token => ReadInventoryMoneyAsync(accountName, token),
            money => "money=" + money.ToString(CultureInfo.InvariantCulture),
            cancellationToken).ConfigureAwait(false));

        checks.Add(await RunApiProbeCheckAsync(
            "InventoryCapacity",
            token => ReadInventoryCapacityAsync(accountName, token),
            capacity => "slots=" + capacity.ToString(CultureInfo.InvariantCulture),
            cancellationToken).ConfigureAwait(false));

        checks.Add(await RunApiProbeCheckAsync(
            "WorldObjects",
            token => ReadWorldObjectsAsync(accountName, token),
            objects => "count=" + objects.Count.ToString(CultureInfo.InvariantCulture),
            cancellationToken).ConfigureAwait(false));

        checks.Add(await RunApiProbeCheckAsync(
            "LootCorpses",
            token => ReadLootCorpsesAsync(accountName, token),
            corpses => "count=" + corpses.Count.ToString(CultureInfo.InvariantCulture),
            cancellationToken).ConfigureAwait(false));

        checks.Add(await RunApiProbeCheckAsync(
            "InventoryWindow.LegacyDialogRect",
            token => ReadInventoryWindowSnapshotAsync(accountName, InventoryWindowRectSource.LegacyDialogRect, token),
            window => "open=" + window.IsOpen.ToString() +
                ", x=" + window.X.ToString("0.###", CultureInfo.InvariantCulture) +
                ", y=" + window.Y.ToString("0.###", CultureInfo.InvariantCulture) +
                ", dialog=0x" + window.DialogAddress.ToString("X", CultureInfo.InvariantCulture),
            cancellationToken).ConfigureAwait(false));

        checks.Add(await RunApiProbeCheckAsync(
            "InventoryWindow.RootWidgetRectExperimental",
            token => ReadInventoryWindowSnapshotAsync(accountName, InventoryWindowRectSource.RootWidgetRectExperimental, token),
            window => "open=" + window.IsOpen.ToString() +
                ", x=" + window.X.ToString("0.###", CultureInfo.InvariantCulture) +
                ", y=" + window.Y.ToString("0.###", CultureInfo.InvariantCulture) +
                ", root=0x" + window.RootWidgetAddress.ToString("X", CultureInfo.InvariantCulture),
            cancellationToken).ConfigureAwait(false));

        checks.AddRange(await RunApiAddressProbeChecksAsync(accountName, cancellationToken).ConfigureAwait(false));

        var result = new RoadhogApiProbeResult(checks);
        var fields = new Dictionary<string, object?>
        {
            ["account"] = accountName,
            ["total"] = result.TotalCount,
            ["passed"] = result.PassedCount,
            ["failed"] = result.FailedCount,
            ["failedChecks"] = string.Join(
                ",",
                result.Checks.Where(check => !check.Success).Select(check => check.Name))
        };

        if (result.AllPassed)
        {
            _logger.Info("api_probe.completed", fields);
        }
        else
        {
            _logger.Warn("api_probe.completed", fields);
        }

        return OperationResult<RoadhogApiProbeResult>.Ok(result);
    }
#endif

    public async Task<OperationResult> TestMoveMouseToScreenPointAsync(
        int x,
        int y,
        CancellationToken cancellationToken = default)
    {
        if (_keyboardInput is null)
        {
            return OperationResult.Fail("Keyboard input is not available for test movement.");
        }

        var result = await ScreenPointMouseMover
            .MoveToAsync(
                _keyboardInput,
                x,
                y,
                ReadDeathReviveMouseResetCount(),
                TimeSpan.FromMilliseconds(ReadDeathReviveMouseStepDelayMs()),
                cancellationToken)
            .ConfigureAwait(false);
        if (result.Success)
        {
            _logger.Info("mouse.test_move.ok", new Dictionary<string, object?>
            {
                ["x"] = x,
                ["y"] = y
            });
        }
        else
        {
            _logger.Warn("mouse.test_move.failed", new Dictionary<string, object?>
            {
                ["x"] = x,
                ["y"] = y,
                ["error"] = result.Error
            });
        }

        return result;
    }

    public async Task<OperationResult> NormalizeInventoryWindowToTopLeftAndCloseAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        return await NormalizeInventoryWindowToTopLeftCoreAsync(
                accountName,
                closeAfterNormalize: true,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OperationResult> NormalizeInventoryWindowToTopLeftAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        return await NormalizeInventoryWindowToTopLeftCoreAsync(
                accountName,
                closeAfterNormalize: false,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OperationResult<BagCleanupSellRegistrationResult>> TestRegisterBagCleanupSellItemsAsync(
        string? accountName,
        MaintenanceScriptSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (_keyboardInput is null)
        {
            return OperationResult<BagCleanupSellRegistrationResult>.Fail(
                "Keyboard input is not available for bag cleanup sell registration.");
        }

        InventoryWindowSnapshot? coordinateWindow = null;
        if (settings.BagCleanupItemCoordinateMode != BagCleanupItemCoordinateMode.WindowRectRelativeExperimental)
        {
            var normalize = await NormalizeInventoryWindowToTopLeftCoreAsync(
                    accountName,
                    closeAfterNormalize: false,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!normalize.Success)
            {
                return OperationResult<BagCleanupSellRegistrationResult>.Fail(
                    "Inventory window normalization failed: " + normalize.Error);
            }
        }
        else
        {
            var windowRead = await EnsureInventoryWindowOpenAsync(
                    accountName,
                    InventoryWindowRectSource.RootWidgetRectExperimental,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!windowRead.Success || windowRead.Value is null)
            {
                return OperationResult<BagCleanupSellRegistrationResult>.Fail(
                    "Experimental inventory Rect read failed: " + windowRead.Error);
            }

            coordinateWindow = windowRead.Value;
        }

        var read = await ReadInventoryAsync(accountName, cancellationToken).ConfigureAwait(false);
        if (!read.Success || read.Value is null)
        {
            return OperationResult<BagCleanupSellRegistrationResult>.Fail(
                "Inventory read failed: " + read.Error);
        }

        var candidates = BagCleanupItemMatcher
            .SelectSellRegistrationItems(read.Value, settings)
            .ToArray();
        if (candidates.Length == 0)
        {
            _logger.Info("bag_cleanup.sell_register.none", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["inventoryCount"] = read.Value.Count
            });
            return OperationResult<BagCleanupSellRegistrationResult>.Ok(
                new BagCleanupSellRegistrationResult(0, Array.Empty<BagCleanupSellRegistrationItem>()));
        }

        var registered = new List<BagCleanupSellRegistrationItem>();
        foreach (var item in candidates)
        {
            var point = EstimateBagItemScreenPoint(
                item.Slot,
                settings.BagCleanupItemCoordinateMode,
                coordinateWindow);
            _logger.Info("bag_cleanup.sell_register.item", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["name"] = item.Name,
                ["templateId"] = item.TemplateId,
                ["slot"] = item.Slot,
                ["itemType"] = item.ItemType,
                ["qualityRank"] = item.QualityRank,
                ["x"] = point.X,
                ["y"] = point.Y,
                ["coordinateMode"] = settings.BagCleanupItemCoordinateMode.ToString(),
                ["rectSource"] = coordinateWindow?.RectSource.ToString() ?? InventoryWindowRectSource.LegacyDialogRect.ToString()
            });

            var move = await ScreenPointMouseMover
                .MoveToAsync(
                    _keyboardInput,
                    point.X,
                    point.Y,
                    ReadDeathReviveMouseResetCount(),
                    TimeSpan.FromMilliseconds(ReadDeathReviveMouseStepDelayMs()),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!move.Success)
            {
                return OperationResult<BagCleanupSellRegistrationResult>.Fail(
                    "Move to bag item failed: " + move.Error);
            }

            await DelayAsync(TimeSpan.FromMilliseconds(ReadBagSellRegisterHoverMs()), cancellationToken)
                .ConfigureAwait(false);

            var down = await _keyboardInput.MouseDownAsync(RoadhogMouseButton.Right, cancellationToken)
                .ConfigureAwait(false);
            if (!down.Success)
            {
                return OperationResult<BagCleanupSellRegistrationResult>.Fail(
                    "Right mouse down failed: " + down.Error);
            }

            await DelayAsync(BagCleanupSellRegisterClickHoldDelay, cancellationToken).ConfigureAwait(false);
            var up = await _keyboardInput.MouseUpAsync(RoadhogMouseButton.Right, cancellationToken)
                .ConfigureAwait(false);
            if (!up.Success)
            {
                return OperationResult<BagCleanupSellRegistrationResult>.Fail(
                    "Right mouse up failed: " + up.Error);
            }

            registered.Add(new BagCleanupSellRegistrationItem(
                item.TemplateId,
                item.InstanceId,
                item.Name,
                item.Slot,
                point.X,
                point.Y));
            await DelayAsync(BagCleanupSellRegisterAfterClickDelay, cancellationToken).ConfigureAwait(false);
        }

        _logger.Info("bag_cleanup.sell_register.ok", new Dictionary<string, object?>
        {
            ["account"] = accountName,
            ["count"] = registered.Count
        });

        return OperationResult<BagCleanupSellRegistrationResult>.Ok(
            new BagCleanupSellRegistrationResult(registered.Count, registered));
    }

    public async Task<OperationResult<BagCleanupManualTestResult>> TestBagCleanupFromNpcAsync(
        string? accountName,
        string npcName,
        MaintenanceScriptSettings settings,
        CancellationToken cancellationToken = default)
    {
        OperationResult<BagCleanupManualTestResult> Fail(string reason, string error)
        {
            _logger.Warn("bag_cleanup.manual_test.failed", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["reason"] = reason,
                ["error"] = error
            });
            return OperationResult<BagCleanupManualTestResult>.Fail(reason + ": " + error);
        }

        if (_keyboardInput is null)
        {
            return Fail("input_unavailable", "Keyboard input is not available for bag cleanup test.");
        }

        var trimmedNpcName = npcName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedNpcName))
        {
            return Fail("npc_name_empty", "Cleanup NPC name is empty.");
        }

        var account = accountName ?? string.Empty;
        var scriptSettings = LoadSavedAccountConfig(account)?.ScriptSettings?.Clone() ?? new ScriptSettings();
        scriptSettings.Maintenance = settings.Clone();
        var config = LoadExecutionAccountConfig(account, scriptSettings);
        var context = new AccountWorkerContext(
            config,
            _gameApi,
            _logger,
            Accounts,
            new AccountWorkerOptions(),
            cancellationToken);
        var npcInteractor = new BagCleanupNpcInteractor(_keyboardInput);
        var seller = new BagCleanupSeller(_keyboardInput);
        var maintenance = config.ScriptSettings?.Maintenance ?? settings;

        _logger.Info("bag_cleanup.manual_test.start", new Dictionary<string, object?>
        {
            ["account"] = account,
            ["npcName"] = trimmedNpcName
        });

        var select = await npcInteractor.SelectConfiguredNpcAsync(context, trimmedNpcName).ConfigureAwait(false);
        if (!select.Success)
        {
            return Fail("cleanup_npc_select_failed", select.Error ?? "Cleanup NPC select failed.");
        }

        var dialog = await npcInteractor.OpenDialogAsync(context).ConfigureAwait(false);
        if (!dialog.Success)
        {
            return Fail("npc_dialog_open_failed", dialog.Error ?? "NPC dialog open failed.");
        }

        var clickEntry = await seller
            .ClickScreenPointAsync(
                context,
                maintenance.BagCleanupSellItemClickX,
                maintenance.BagCleanupSellItemClickY,
                "sell_item_entry")
            .ConfigureAwait(false);
        if (!clickEntry.Success)
        {
            return Fail("sell_item_entry_click_failed", clickEntry.Error ?? "Sell item entry click failed.");
        }

        await seller.WaitAfterSellItemEntryAsync(context).ConfigureAwait(false);

        var registeredItems = new List<BagCleanupRegisteredItem>();
        var initialCandidateCount = 0;
        ulong? initialMoney = null;
        ulong? finalMoney = null;
        ulong totalMoneyDelta = 0;
        var batchIndex = 0;
        while (true)
        {
            if (batchIndex >= 40)
            {
                return Fail("sell_batch_limit_exceeded", "Bag cleanup exceeded 40 sell batches.");
            }

            if (batchIndex == 0)
            {
                var normalize = await seller.NormalizeInventoryWindowToTopLeftAsync(context).ConfigureAwait(false);
                if (!normalize.Success)
                {
                    return Fail("inventory_window_normalize_failed", normalize.Error ?? "Inventory normalize failed.");
                }
            }
            else
            {
                var openInventory = await seller.OpenInventoryWindowAsync(context).ConfigureAwait(false);
                if (!openInventory.Success)
                {
                    return Fail("inventory_window_open_failed", openInventory.Error ?? "Inventory window open failed.");
                }
            }

            var inventoryRead = await BagCleanupGameApi.ReadInventoryAsync(context).ConfigureAwait(false);
            if (!inventoryRead.Success || inventoryRead.Value is null)
            {
                return Fail("inventory_read_before_sell_failed", inventoryRead.Error ?? "Inventory read failed.");
            }

            var candidates = BagCleanupItemMatcher
                .SelectSellRegistrationItems(inventoryRead.Value, maintenance)
                .ToArray();
            if (initialCandidateCount == 0)
            {
                initialCandidateCount = candidates.Length;
            }

            var batch = BagCleanupSellBatchPlanner.SelectNextBatch(candidates);
            _logger.Info("bag_cleanup.manual_test.sell.candidates", new Dictionary<string, object?>
            {
                ["account"] = account,
                ["count"] = candidates.Length,
                ["batchCount"] = batch.Items.Count,
                ["batchKind"] = batch.KindName,
                ["batchIndex"] = batchIndex + 1,
                ["maxBatchCount"] = batch.MaxBatchCount
            });

            if (candidates.Length == 0)
            {
                if (registeredItems.Count == 0)
                {
                    var emptyResult = new BagCleanupManualTestResult(
                        trimmedNpcName,
                        0,
                        0,
                        null,
                        null,
                        null,
                        Array.Empty<BagCleanupRegisteredItem>());
                    _logger.Info("bag_cleanup.manual_test.no_candidates", new Dictionary<string, object?>
                    {
                        ["account"] = account,
                        ["npcName"] = trimmedNpcName
                    });
                    return OperationResult<BagCleanupManualTestResult>.Ok(emptyResult);
                }

                break;
            }

            InventoryWindowSnapshot? coordinateWindow = null;
            if (maintenance.BagCleanupItemCoordinateMode == BagCleanupItemCoordinateMode.WindowRectRelativeExperimental)
            {
                var windowRead = await BagCleanupGameApi
                    .ReadInventoryWindowAsync(context, InventoryWindowRectSource.RootWidgetRectExperimental)
                    .ConfigureAwait(false);
                if (!windowRead.Success || windowRead.Value is null)
                {
                    return Fail("inventory_window_rect_failed", windowRead.Error ?? "Experimental inventory Rect read failed.");
                }

                coordinateWindow = windowRead.Value;
            }

            var registered = await seller
                .RegisterSellItemsAsync(context, maintenance, batch.Items, coordinateWindow)
                .ConfigureAwait(false);
            if (!registered.Success || registered.Value is null)
            {
                return Fail("sell_register_failed", registered.Error ?? "Sell register failed.");
            }

            await seller.WaitAfterSellRegistrationAsync(context).ConfigureAwait(false);

            var closeInventory = await seller.CloseInventoryWindowAsync(context).ConfigureAwait(false);
            if (!closeInventory.Success)
            {
                return Fail("inventory_window_close_failed", closeInventory.Error ?? "Inventory window close failed.");
            }

            var moneyBefore = await BagCleanupGameApi.ReadInventoryMoneyAsync(context).ConfigureAwait(false);
            if (!moneyBefore.Success)
            {
                return Fail("money_read_before_sell_failed", moneyBefore.Error ?? "Inventory money read before sell failed.");
            }

            initialMoney ??= moneyBefore.Value;
            var clickSell = await seller
                .ClickScreenPointAsync(
                    context,
                    maintenance.BagCleanupSellButtonClickX,
                    maintenance.BagCleanupSellButtonClickY,
                    "sell_button")
                .ConfigureAwait(false);
            if (!clickSell.Success)
            {
                return Fail("sell_button_click_failed", clickSell.Error ?? "Sell button click failed.");
            }

            await DelayAsync(TimeSpan.FromMilliseconds(ReadBagCleanupSellVerifyDelayMs()), cancellationToken)
                .ConfigureAwait(false);
            var moneyAfter = await BagCleanupGameApi.ReadInventoryMoneyAsync(context).ConfigureAwait(false);
            if (!moneyAfter.Success)
            {
                return Fail("money_verify_read_failed", moneyAfter.Error ?? "Inventory money verify read failed.");
            }

            if (moneyAfter.Value <= moneyBefore.Value)
            {
                return Fail(
                    "money_verify_failed",
                    "Money did not increase after selling. before=" +
                    moneyBefore.Value.ToString(CultureInfo.InvariantCulture) +
                    ", after=" +
                    moneyAfter.Value.ToString(CultureInfo.InvariantCulture));
            }

            batchIndex++;
            var moneyDelta = moneyAfter.Value - moneyBefore.Value;
            totalMoneyDelta += moneyDelta;
            finalMoney = moneyAfter.Value;
            registeredItems.AddRange(registered.Value);

            var afterInventoryRead = await BagCleanupGameApi.ReadInventoryAsync(context).ConfigureAwait(false);
            if (!afterInventoryRead.Success || afterInventoryRead.Value is null)
            {
                return Fail("inventory_read_after_sell_failed", afterInventoryRead.Error ?? "Inventory read after sell failed.");
            }

            var remainingSellCandidateCount = BagCleanupItemMatcher
                .SelectSellRegistrationItems(afterInventoryRead.Value, maintenance)
                .Count;
            _logger.Info("bag_cleanup.manual_test.batch.ok", new Dictionary<string, object?>
            {
                ["account"] = account,
                ["batchIndex"] = batchIndex,
                ["batchRegisteredCount"] = registered.Value.Count,
                ["totalRegisteredCount"] = registeredItems.Count,
                ["moneyDelta"] = moneyDelta,
                ["totalMoneyDelta"] = totalMoneyDelta,
                ["remainingSellCandidateCount"] = remainingSellCandidateCount
            });

            if (remainingSellCandidateCount <= 0)
            {
                break;
            }
        }

        var result = new BagCleanupManualTestResult(
            trimmedNpcName,
            initialCandidateCount,
            registeredItems.Count,
            initialMoney,
            finalMoney,
            totalMoneyDelta,
            registeredItems);
        _logger.Info("bag_cleanup.manual_test.ok", new Dictionary<string, object?>
        {
            ["account"] = account,
            ["npcName"] = trimmedNpcName,
            ["candidateCount"] = result.CandidateCount,
            ["registeredCount"] = result.RegisteredCount,
            ["initialMoney"] = result.InitialMoney,
            ["money"] = result.FinalMoney,
            ["moneyDelta"] = result.MoneyDelta
        });
        return OperationResult<BagCleanupManualTestResult>.Ok(result);
    }

    private async Task<OperationResult<InventoryWindowSnapshot>> EnsureInventoryWindowOpenAsync(
        string? accountName,
        InventoryWindowRectSource rectSource,
        CancellationToken cancellationToken)
    {
        if (_keyboardInput is null)
        {
            return OperationResult<InventoryWindowSnapshot>.Fail(
                "Keyboard input is not available for inventory window reading.");
        }

        if (_gameApi is not IInventoryWindowGameApi inventoryApi)
        {
            return OperationResult<InventoryWindowSnapshot>.Fail("Inventory window VMM API is not available.");
        }

        var context = string.IsNullOrWhiteSpace(accountName)
            ? new GameApiReadContext(string.Empty, 0, string.Empty, string.Empty)
            : CreateReadContext(accountName);
        var open = await PressInventoryToggleAsync(cancellationToken).ConfigureAwait(false);
        if (!open.Success)
        {
            return OperationResult<InventoryWindowSnapshot>.Fail(open.Error ?? "Inventory open toggle failed.");
        }

        await DelayAsync(InventoryOpenSettleDelay, cancellationToken).ConfigureAwait(false);
        var read = await inventoryApi
            .ReadInventoryWindowAsync(context, rectSource, cancellationToken)
            .ConfigureAwait(false);
        if (!read.Success || read.Value is null)
        {
            return OperationResult<InventoryWindowSnapshot>.Fail(
                "Inventory window read after blind open failed: " + read.Error);
        }

        return read.Value.IsOpen
            ? read
            : OperationResult<InventoryWindowSnapshot>.Fail(
                "Inventory window did not read as open after blind pressing " + InventoryToggleKey + ".");
    }

    private async Task<OperationResult> NormalizeInventoryWindowToTopLeftCoreAsync(
        string? accountName,
        bool closeAfterNormalize,
        CancellationToken cancellationToken)
    {
        if (_keyboardInput is null)
        {
            return OperationResult.Fail("Keyboard input is not available for inventory window normalization.");
        }

        if (_gameApi is not IInventoryWindowGameApi inventoryApi)
        {
            return OperationResult.Fail("Inventory window VMM API is not available.");
        }

        var context = string.IsNullOrWhiteSpace(accountName)
            ? new GameApiReadContext(string.Empty, 0, string.Empty, string.Empty)
            : CreateReadContext(accountName);

        var open = await PressInventoryToggleAsync(cancellationToken).ConfigureAwait(false);
        if (!open.Success)
        {
            return open;
        }

        await DelayAsync(InventoryOpenSettleDelay, cancellationToken).ConfigureAwait(false);
        var read = await inventoryApi.ReadInventoryWindowAsync(context, cancellationToken).ConfigureAwait(false);
        if (!read.Success || read.Value is null)
        {
            return OperationResult.Fail("Inventory window read after blind open failed: " + read.Error);
        }

        var snapshot = read.Value;
        if (!snapshot.IsOpen)
        {
            return OperationResult.Fail(
                "Inventory window did not read as open after blind pressing " + InventoryToggleKey + ".");
        }

        if (!snapshot.IsAtTopLeft())
        {
            var drag = await DragInventoryWindowToTopLeftAsync(
                    inventoryApi,
                    context,
                    snapshot,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!drag.Success || drag.Value is null)
            {
                return OperationResult.Fail(drag.Error ?? "Inventory window drag failed.");
            }

            snapshot = drag.Value;
        }

        if (!closeAfterNormalize)
        {
            _logger.Info("inventory_window.normalize.ok", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["x"] = snapshot.X,
                ["y"] = snapshot.Y,
                ["width"] = snapshot.Width,
                ["height"] = snapshot.Height
            });

            return OperationResult.Ok();
        }

        var close = await PressInventoryToggleAsync(cancellationToken).ConfigureAwait(false);
        if (!close.Success)
        {
            return close;
        }

        await DelayAsync(InventoryCloseSettleDelay, cancellationToken).ConfigureAwait(false);
        var finalRead = await inventoryApi.ReadInventoryWindowAsync(context, cancellationToken).ConfigureAwait(false);
        if (!finalRead.Success || finalRead.Value is null)
        {
            return OperationResult.Fail("Inventory window read after close failed: " + finalRead.Error);
        }

        if (finalRead.Value.IsOpen)
        {
            return OperationResult.Fail("Inventory window is still open after close toggle.");
        }

        _logger.Info("inventory_window.normalize.ok", new Dictionary<string, object?>
        {
            ["account"] = accountName,
            ["x"] = finalRead.Value.X,
            ["y"] = finalRead.Value.Y,
            ["width"] = finalRead.Value.Width,
            ["height"] = finalRead.Value.Height
        });

        return OperationResult.Ok();
    }

    public Task<OperationResult> ExecutePathAsync(
        string accountName,
        string pathName,
        IReadOnlyList<SharedPathPoint> points,
        ScriptSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        if (_stationaryCombatController is null)
        {
            return Task.FromResult(OperationResult.Fail("Path execution controller is not available."));
        }

        if (points.Count == 0)
        {
            return Task.FromResult(OperationResult.Fail("Path has no points."));
        }

        var config = LoadExecutionAccountConfig(accountName, settings);
        var context = new AccountWorkerContext(
            config,
            _gameApi,
            _logger,
            Accounts,
            new AccountWorkerOptions(),
            cancellationToken);
        var vectors = points.Select(point => point.ToVector3()).ToArray();
        return _stationaryCombatController.ExecutePathOnceAsync(context, pathName, vectors);
    }

    private async Task<OperationResult<InventoryWindowSnapshot>> DragInventoryWindowToTopLeftAsync(
        IInventoryWindowGameApi inventoryApi,
        GameApiReadContext context,
        InventoryWindowSnapshot initialSnapshot,
        CancellationToken cancellationToken)
    {
        var current = initialSnapshot;
        foreach (var xOffset in InventoryTitleDragXOffsets)
        {
            foreach (var yOffset in InventoryTitleDragYOffsets)
            {
                var start = EstimateInventoryTitleDragPoint(current, xOffset, yOffset);
                _logger.Info("inventory_window.drag.attempt", new Dictionary<string, object?>
                {
                    ["account"] = context.AccountName,
                    ["rectX"] = current.X,
                    ["rectY"] = current.Y,
                    ["startX"] = start.X,
                    ["startY"] = start.Y,
                    ["xOffset"] = xOffset,
                    ["yOffset"] = yOffset
                });

                var moveToStart = await ScreenPointMouseMover
                    .MoveToAsync(
                        _keyboardInput!,
                        start.X,
                        start.Y,
                        ReadDeathReviveMouseResetCount(),
                        TimeSpan.FromMilliseconds(ReadDeathReviveMouseStepDelayMs()),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!moveToStart.Success)
                {
                    return OperationResult<InventoryWindowSnapshot>.Fail("Inventory drag start move failed: " + moveToStart.Error);
                }

                await DelayAsync(InventoryDragStartSettleDelay, cancellationToken).ConfigureAwait(false);

                var down = await _keyboardInput!
                    .MouseDownAsync(RoadhogMouseButton.Left, cancellationToken)
                    .ConfigureAwait(false);
                if (!down.Success)
                {
                    return OperationResult<InventoryWindowSnapshot>.Fail("Inventory drag mouse down failed: " + down.Error);
                }

                OperationResult? drag = null;
                try
                {
                    drag = await _keyboardInput
                        .MoveMouseRelativeAsync(
                            ScreenPointMouseMover.AbsoluteMouseResetDelta,
                            ScreenPointMouseMover.AbsoluteMouseResetDelta,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    var up = await _keyboardInput
                        .MouseUpAsync(RoadhogMouseButton.Left, cancellationToken)
                        .ConfigureAwait(false);
                    if (!up.Success)
                    {
                        _logger.Warn("inventory_window.drag.mouse_up_failed", new Dictionary<string, object?>
                        {
                            ["account"] = context.AccountName,
                            ["error"] = up.Error
                        });
                    }
                }

                if (drag is null || !drag.Success)
                {
                    return OperationResult<InventoryWindowSnapshot>.Fail("Inventory drag move failed: " + drag?.Error);
                }

                await DelayAsync(InventoryDragSettleDelay, cancellationToken).ConfigureAwait(false);
                var read = await inventoryApi.ReadInventoryWindowAsync(context, cancellationToken).ConfigureAwait(false);
                if (!read.Success || read.Value is null)
                {
                    return OperationResult<InventoryWindowSnapshot>.Fail("Inventory window read after drag failed: " + read.Error);
                }

                current = read.Value;
                if (current.IsAtTopLeft())
                {
                    _logger.Info("inventory_window.drag.ok", new Dictionary<string, object?>
                    {
                        ["account"] = context.AccountName,
                        ["startX"] = start.X,
                        ["startY"] = start.Y,
                        ["xOffset"] = xOffset,
                        ["yOffset"] = yOffset
                    });
                    return OperationResult<InventoryWindowSnapshot>.Ok(current);
                }
            }
        }

        return OperationResult<InventoryWindowSnapshot>.Fail(
            "Inventory window did not reach top-left. Last rect=(" +
            current.X.ToString("0.###") +
            "," +
            current.Y.ToString("0.###") +
            ").");
    }

    private Task<OperationResult> PressInventoryToggleAsync(CancellationToken cancellationToken)
    {
        return _keyboardInput!.PressKeyAsync(InventoryToggleKey, InventoryToggleHoldDuration, cancellationToken);
    }

    private static (int X, int Y) EstimateInventoryTitleDragPoint(
        InventoryWindowSnapshot snapshot,
        int xOffset,
        int yOffset)
    {
        var topLeft = EstimateInventoryWindowTopLeftScreen(snapshot.X, snapshot.Y);
        return (
            ClampInt((int)Math.Round(topLeft.X + xOffset), 0, short.MaxValue),
            ClampInt((int)Math.Round(topLeft.Y + yOffset), 0, short.MaxValue));
    }

    private static (int X, int Y) EstimateBagItemScreenPoint(
        int slot,
        BagCleanupItemCoordinateMode coordinateMode,
        InventoryWindowSnapshot? window)
    {
        var normalizedSlot = Math.Max(0, slot);
        var page = normalizedSlot / BagSlotsPerPage;
        var indexInPage = normalizedSlot % BagSlotsPerPage;
        var column = indexInPage % BagSlotColumns;
        var rowInPage = indexInPage / BagSlotColumns;
        var slotOriginX = ReadRawDoubleFromEnv("ROADHOG_BAG_SLOT0_CENTER_X", DefaultBagSlot0CenterX);
        var slotOriginY = ReadRawDoubleFromEnv("ROADHOG_BAG_SLOT0_CENTER_Y", DefaultBagSlot0CenterY);
        var x = slotOriginX + (column * ReadRawDoubleFromEnv("ROADHOG_BAG_SLOT_STEP_X", DefaultBagSlotStepX));
        var y = slotOriginY +
            (rowInPage * ReadRawDoubleFromEnv("ROADHOG_BAG_SLOT_STEP_Y", DefaultBagSlotStepY)) +
            ReadBagPageOffsetY(page);
        if (coordinateMode == BagCleanupItemCoordinateMode.WindowRectRelativeExperimental && window is not null)
        {
            var topLeft = EstimateInventoryWindowTopLeftScreen(window.X, window.Y);
            x += topLeft.X;
            y += topLeft.Y;
        }

        return (
            ClampInt((int)Math.Round(x), 0, short.MaxValue),
            ClampInt((int)Math.Round(y), 0, short.MaxValue));
    }

    private static double ReadBagPageOffsetY(int page)
    {
        if (page <= 0)
        {
            return 0.0;
        }

        var page1Offset = ReadRawDoubleFromEnv(
            "ROADHOG_BAG_PAGE1_OFFSET_Y",
            DefaultBagPage1OffsetY);
        if (page == 1)
        {
            return page1Offset;
        }

        var page2Offset = ReadRawDoubleFromEnv(
            "ROADHOG_BAG_PAGE2_OFFSET_Y",
            DefaultBagPage2OffsetY);
        if (page == 2)
        {
            return page2Offset;
        }

        return page2Offset + ((page - 2) * (page2Offset - page1Offset));
    }

    private static (double X, double Y) EstimateInventoryWindowTopLeftScreen(double uiX, double uiY)
    {
        var u = ClampDouble(uiX / InventoryUiMaxWindowX, 0.0, 1.0);
        var v = ClampDouble(uiY / InventoryUiMaxWindowY, 0.0, 1.0);
        return (
            Bilinear(
                InventoryScreenTopLeftTlX,
                InventoryScreenTopLeftTrX,
                InventoryScreenTopLeftBlX,
                InventoryScreenTopLeftBrX,
                u,
                v),
            Bilinear(
                InventoryScreenTopLeftTlY,
                InventoryScreenTopLeftTrY,
                InventoryScreenTopLeftBlY,
                InventoryScreenTopLeftBrY,
                u,
                v));
    }

    private static double Bilinear(
        double topLeft,
        double topRight,
        double bottomLeft,
        double bottomRight,
        double u,
        double v)
    {
        return ((1.0 - u) * (1.0 - v) * topLeft) +
            (u * (1.0 - v) * topRight) +
            ((1.0 - u) * v * bottomLeft) +
            (u * v * bottomRight);
    }

#if DEBUG
    private static async Task<RoadhogApiProbeCheckResult> RunApiProbeCheckAsync<T>(
        string name,
        Func<CancellationToken, Task<OperationResult<T>>> read,
        Func<T, string> describe,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await read(cancellationToken).ConfigureAwait(false);
            if (!result.Success)
            {
                return RoadhogApiProbeCheckResult.Fail(
                    name,
                    result.Error ?? "API read failed.");
            }

            var value = result.Value;
            if (value is null)
            {
                return RoadhogApiProbeCheckResult.Fail(name, "API returned null.");
            }

            return RoadhogApiProbeCheckResult.Pass(name, describe(value));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return RoadhogApiProbeCheckResult.Fail(
                name,
                ex.GetType().Name + ": " + ex.Message);
        }
    }

    private async Task<IReadOnlyList<RoadhogApiProbeCheckResult>> RunApiAddressProbeChecksAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        if (_gameApi is not IRoadhogApiAddressProbe addressProbe)
        {
            return CreateFailedAddressProbeChecks("Address probe provider is unavailable.");
        }

        try
        {
            var result = await addressProbe
                .ProbeAddressesAsync(CreateReadContextOrDefault(accountName), cancellationToken)
                .ConfigureAwait(false);
            if (!result.Success || result.Value is null)
            {
                return CreateFailedAddressProbeChecks(result.Error ?? "Address probe failed.");
            }

            var returned = result.Value.ToDictionary(check => check.Name, StringComparer.Ordinal);
            return GameApiAddressProbeResult.RequiredCheckNames
                .Select(name => returned.TryGetValue(name, out var check)
                    ? new RoadhogApiProbeCheckResult(check.Name, check.Success, check.Detail)
                    : RoadhogApiProbeCheckResult.Fail(name, "Address probe did not return this check."))
                .ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return CreateFailedAddressProbeChecks(ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static IReadOnlyList<RoadhogApiProbeCheckResult> CreateFailedAddressProbeChecks(string error)
    {
        return GameApiAddressProbeResult.RequiredCheckNames
            .Select(name => RoadhogApiProbeCheckResult.Fail(name, error))
            .ToArray();
    }

    private Task<OperationResult<LockedTargetSnapshot>> ReadLockedTargetSnapshotAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        if (_gameApi is IRoadhogScopedGameApi scopedApi &&
            !string.IsNullOrWhiteSpace(accountName))
        {
            return scopedApi.ReadLockedTargetAsync(CreateReadContext(accountName), cancellationToken);
        }

        return _gameApi.ReadLockedTargetAsync(cancellationToken);
    }
#endif

    private Task<OperationResult<IReadOnlyList<SkillSnapshot>>> ReadSkillsAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        if (_gameApi is IRoadhogScopedGameApi scopedApi &&
            !string.IsNullOrWhiteSpace(accountName))
        {
            return scopedApi.ReadSkillsAsync(CreateReadContext(accountName), cancellationToken);
        }

        return _gameApi.ReadSkillsAsync(cancellationToken);
    }

    private Task<OperationResult<IReadOnlyList<WorldObjectSnapshot>>> ReadWorldObjectsAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        if (_gameApi is IRoadhogScopedGameApi scopedApi &&
            !string.IsNullOrWhiteSpace(accountName))
        {
            return scopedApi.ReadWorldObjectsAsync(CreateReadContext(accountName), cancellationToken);
        }

        return _gameApi.ReadWorldObjectsAsync(cancellationToken);
    }

    private Task<OperationResult<ChannelSnapshot>> ReadChannelSnapshotAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        if (_gameApi is IRoadhogScopedChannelGameApi scopedApi &&
            !string.IsNullOrWhiteSpace(accountName))
        {
            return scopedApi.ReadChannelAsync(CreateReadContext(accountName), cancellationToken);
        }

        if (_gameApi is IRoadhogChannelGameApi channelApi)
        {
            return channelApi.ReadChannelAsync(cancellationToken);
        }

        return Task.FromResult(OperationResult<ChannelSnapshot>.Fail("MapId API is unavailable."));
    }

    private Task<OperationResult<GatherSnapshot>> ReadGatherSnapshotAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        if (_gameApi is IRoadhogScopedGameApi scopedApi &&
            !string.IsNullOrWhiteSpace(accountName))
        {
            return scopedApi.ReadGatherSnapshotAsync(CreateReadContext(accountName), cancellationToken);
        }

        return _gameApi.ReadGatherSnapshotAsync(cancellationToken);
    }

    private Task<OperationResult<IReadOnlyList<InventoryItemSnapshot>>> ReadInventoryAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        if (_gameApi is IRoadhogScopedGameApi scopedApi &&
            !string.IsNullOrWhiteSpace(accountName))
        {
            return scopedApi.ReadInventoryAsync(CreateReadContext(accountName), cancellationToken);
        }

        return _gameApi.ReadInventoryAsync(cancellationToken);
    }

#if DEBUG
    private Task<OperationResult<ulong>> ReadInventoryMoneyAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        if (_gameApi is not IInventoryMoneyGameApi moneyApi)
        {
            return Task.FromResult(OperationResult<ulong>.Fail(
                "Inventory money VMM API is not available."));
        }

        return moneyApi.ReadInventoryMoneyAsync(CreateReadContextOrDefault(accountName), cancellationToken);
    }

    private Task<OperationResult<int>> ReadInventoryCapacityAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        if (_gameApi is not IInventoryCapacityGameApi capacityApi)
        {
            return Task.FromResult(OperationResult<int>.Fail(
                "Inventory capacity VMM API is not available."));
        }

        return capacityApi.ReadInventoryCapacityAsync(CreateReadContextOrDefault(accountName), cancellationToken);
    }

    private Task<OperationResult<IReadOnlyList<LootCorpseSnapshot>>> ReadLootCorpsesAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        if (_gameApi is IRoadhogScopedGameApi scopedApi &&
            !string.IsNullOrWhiteSpace(accountName))
        {
            return scopedApi.ReadLootCorpsesAsync(CreateReadContext(accountName), cancellationToken);
        }

        return _gameApi.ReadLootCorpsesAsync(cancellationToken);
    }

    private Task<OperationResult<InventoryWindowSnapshot>> ReadInventoryWindowSnapshotAsync(
        string? accountName,
        InventoryWindowRectSource rectSource,
        CancellationToken cancellationToken)
    {
        if (_gameApi is not IInventoryWindowGameApi inventoryWindowApi)
        {
            return Task.FromResult(OperationResult<InventoryWindowSnapshot>.Fail(
                "Inventory window VMM API is not available."));
        }

        return inventoryWindowApi.ReadInventoryWindowAsync(
            CreateReadContextOrDefault(accountName),
            rectSource,
            cancellationToken);
    }
#endif

    private Task<OperationResult<PlayerSnapshot>> ReadPlayerSnapshotAsync(
        string? accountName,
        CancellationToken cancellationToken,
        bool bypassMemoryCache = false)
    {
        if (_gameApi is IRoadhogScopedGameApi scopedApi &&
            !string.IsNullOrWhiteSpace(accountName))
        {
            return scopedApi.ReadPlayerAsync(CreateReadContext(accountName, bypassMemoryCache), cancellationToken);
        }

        return _gameApi.ReadPlayerAsync(cancellationToken);
    }

    private Task<OperationResult<PlayerAbnormalStatusSnapshot>> ReadPlayerAbnormalStatusSnapshotAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        if (_gameApi is IRoadhogScopedGameApi scopedApi &&
            !string.IsNullOrWhiteSpace(accountName))
        {
            return scopedApi.ReadPlayerAbnormalStatusesAsync(CreateReadContext(accountName), cancellationToken);
        }

        return _gameApi.ReadPlayerAbnormalStatusesAsync(cancellationToken);
    }

    private Task<OperationResult<LockedTargetAbnormalStatusSnapshot>> ReadLockedTargetAbnormalStatusSnapshotAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        if (_gameApi is IRoadhogScopedGameApi scopedApi &&
            !string.IsNullOrWhiteSpace(accountName))
        {
            return scopedApi.ReadLockedTargetAbnormalStatusesAsync(CreateReadContext(accountName), cancellationToken);
        }

        return _gameApi.ReadLockedTargetAbnormalStatusesAsync(cancellationToken);
    }

    private Task<OperationResult<SummonedPetSnapshot>> ReadSummonedPetSnapshotAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        if (_gameApi is IRoadhogScopedGameApi scopedApi &&
            !string.IsNullOrWhiteSpace(accountName))
        {
            return scopedApi.ReadSummonedPetAsync(CreateReadContext(accountName), cancellationToken);
        }

        return _gameApi.ReadSummonedPetAsync(cancellationToken);
    }

    private Task<OperationResult<SummonedPetRosterSnapshot>> ReadSummonedPetRosterSnapshotAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        if (_gameApi is IRoadhogScopedGameApi scopedApi &&
            !string.IsNullOrWhiteSpace(accountName))
        {
            return scopedApi.ReadSummonedPetRosterAsync(CreateReadContext(accountName), cancellationToken);
        }

        return _gameApi.ReadSummonedPetRosterAsync(cancellationToken);
    }

#if DEBUG
    private GameApiReadContext CreateReadContextOrDefault(string? accountName)
    {
        return string.IsNullOrWhiteSpace(accountName)
            ? new GameApiReadContext(string.Empty, 0, string.Empty, string.Empty)
            : CreateReadContext(accountName);
    }
#endif

    private GameApiReadContext CreateReadContext(string accountName, bool bypassMemoryCache = false)
    {
        var account = Accounts.Snapshot()
            .FirstOrDefault(item => string.Equals(item.AccountName, accountName, StringComparison.OrdinalIgnoreCase));

        if (account is not null)
        {
            return new GameApiReadContext(
                account.AccountName,
                account.ProcessId,
                account.TargetProcessName,
                account.VmmDeviceName,
                bypassMemoryCache);
        }

        var config = LoadSavedAccountConfig(accountName);
        return config is null
            ? new GameApiReadContext(accountName, 0, string.Empty, string.Empty, bypassMemoryCache)
            : new GameApiReadContext(
                config.AccountName,
                config.ProcessId,
                config.TargetProcessName,
                config.VmmDeviceName,
                bypassMemoryCache);
    }

    private AccountConfig? LoadSavedAccountConfig(string accountName)
    {
        if (_accountConfigStore is null)
        {
            return null;
        }

        var result = _accountConfigStore.LoadAllAsync().GetAwaiter().GetResult();
        if (!result.Success || result.Value is null)
        {
            _logger.Warn("account_config.read_context.load_failed", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["error"] = result.Error
            });
            return null;
        }

        var config = result.Value
            .FirstOrDefault(item => string.Equals(item.AccountName, accountName, StringComparison.OrdinalIgnoreCase))
            ?.Clone();
        if (config is not null)
        {
            config.VmmDeviceName = ResolveCurrentVmmDeviceName(config);
        }

        return config;
    }

    private AccountConfig LoadExecutionAccountConfig(string accountName, ScriptSettings? settings)
    {
        var config = LoadSavedAccountConfig(accountName) ?? new AccountConfig { AccountName = accountName };
        var runtime = Accounts.Snapshot()
            .FirstOrDefault(item => string.Equals(item.AccountName, accountName, StringComparison.OrdinalIgnoreCase));
        if (runtime is not null)
        {
            config.ProcessId = runtime.ProcessId;
            config.TargetProcessName = string.IsNullOrWhiteSpace(runtime.TargetProcessName)
                ? config.TargetProcessName
                : runtime.TargetProcessName;
            config.VmmDeviceName = string.IsNullOrWhiteSpace(runtime.VmmDeviceName)
                ? config.VmmDeviceName
                : runtime.VmmDeviceName;
            config.HardwareKey = string.IsNullOrWhiteSpace(runtime.HardwareKey)
                ? config.HardwareKey
                : runtime.HardwareKey;
        }

        if (settings is not null)
        {
            config.ScriptSettings = settings.Clone();
            config.MainMode = config.ScriptSettings.MainMode;
            config.CombatMode = config.ScriptSettings.CombatMode;
        }

        return config;
    }

    private string ResolveCurrentVmmDeviceName(AccountConfig config)
    {
        if (_hardwareResolver is null ||
            string.IsNullOrWhiteSpace(config.HardwareKey) ||
            !IsDefaultVmmDeviceName(config.VmmDeviceName))
        {
            return config.VmmDeviceName;
        }

        var device = _hardwareResolver.ListDevices()
            .FirstOrDefault(item => DeviceMatchesHardwareKey(item, config.HardwareKey));
        return device?.VmmDeviceName ?? config.VmmDeviceName;
    }

    private static bool IsDefaultVmmDeviceName(string vmmDeviceName)
    {
        return string.IsNullOrWhiteSpace(vmmDeviceName) ||
            string.Equals(vmmDeviceName.Trim(), "fpga", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadDeathReviveMouseResetCount()
    {
        return ClampInt(
            ReadRawIntFromEnv("ROADHOG_DEATH_REVIVE_MOUSE_RESET_COUNT", ScreenPointMouseMover.DefaultResetCount),
            1,
            10);
    }

    private static int ReadDeathReviveMouseStepDelayMs()
    {
        return ClampInt(
            ReadRawIntFromEnv("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS", ScreenPointMouseMover.DefaultStepDelayMs),
            0,
            1000);
    }

    private static int ReadBagSellRegisterHoverMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_BAG_SELL_REGISTER_HOVER_MS", 200), 0, 5000);
    }

    private static int ReadBagCleanupSellVerifyDelayMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_BAG_CLEANUP_SELL_VERIFY_DELAY_MS", 1000), 0, 10000);
    }

    private static int ClampInt(int value, int min, int max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    private static double ClampDouble(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    private static Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return delay <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(delay, cancellationToken);
    }

    private static int ReadRawIntFromEnv(string name, int defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return int.TryParse(raw, out var value) ? value : defaultValue;
    }

    private static double ReadRawDoubleFromEnv(string name, double defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : defaultValue;
    }

    private static bool DeviceMatchesHardwareKey(HardwareDeviceFeature device, string hardwareKey)
    {
        var expected = hardwareKey.Trim();
        return string.Equals(device.BindingKey.Trim(), expected, StringComparison.OrdinalIgnoreCase) ||
            device.AliasKeys.Any(alias => string.Equals(alias.Trim(), expected, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record BagCleanupSellRegistrationResult(
    int RegisteredCount,
    IReadOnlyList<BagCleanupSellRegistrationItem> Items);

public sealed record BagCleanupSellRegistrationItem(
    uint TemplateId,
    ulong InstanceId,
    string Name,
    int Slot,
    int X,
    int Y);

public sealed record BagCleanupManualTestResult(
    string NpcName,
    int CandidateCount,
    int RegisteredCount,
    ulong? InitialMoney,
    ulong? FinalMoney,
    ulong? MoneyDelta,
    IReadOnlyList<BagCleanupRegisteredItem> Items);
