using Roadhog.Application.Input;
using Roadhog.Application.Channels;
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

public sealed partial class RoadhogRuntime : IRoadhogRuntime
{
    private readonly IRoadhogSnapshotReaderFactory _snapshotReaders;
    private readonly IRoadhogLogger _logger;
    private readonly IAccountConfigStore? _accountConfigStore;
    private readonly IHardwareDeviceResolver? _hardwareResolver;
    private readonly IKeyboardInput? _keyboardInput;
    private readonly StationaryCombatController? _stationaryCombatController;
    private readonly StationaryObstacleNavigator? _stationaryObstacleNavigator;
    private readonly RadarLiveSnapshotRegistry? _radarSnapshots;
    public RoadhogRuntime(
        IRoadhogSnapshotReaderFactory snapshotReaders,
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
        _snapshotReaders = snapshotReaders;
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

    public void ApplyFileLoggingSettings(string accountName, bool enabled)
    {
        if (_logger is IFileLoggingControl control) control.SetFileLoggingEnabled(enabled);
    }

    public void ApplyRadarObstacleSettings(string accountName, RadarObstacleScriptSettings settings)
    {
        _stationaryObstacleNavigator?.SetSettingsOverride(accountName, settings);
    }

    public void NotifyRadarMapSaved(uint mapId)
    {
        _stationaryObstacleNavigator?.NotifyMapSaved(mapId);
    }

    public void ApplyPostCombatReturnSettings(string accountName, bool enabled, double distance) =>
        _stationaryCombatController?.SetPostCombatReturnSettings(accountName, enabled, distance);

    public async Task<RadarLiveSnapshot> ReadRadarSnapshotAsync(
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
            return cached;
        }

        var map = await ReadChannelSnapshotAsync(accountName, cancellationToken).ConfigureAwait(false);
        var player = await ReadPlayerSnapshotAsync(accountName, cancellationToken).ConfigureAwait(false);
        var objects = await ReadWorldObjectsAsync(accountName, cancellationToken).ConfigureAwait(false);

        var capturedAt = DateTimeOffset.Now;
        _radarSnapshots?.PublishMapId(accountName, map.MapId, capturedAt);
        _radarSnapshots?.PublishPlayer(accountName, player);
        _radarSnapshots?.PublishWorldObjects(accountName, objects, capturedAt);
        return new RadarLiveSnapshot(map.MapId, player, objects, capturedAt);
    }

    public async Task<PlayerSnapshot> ReadPlayerAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadPlayerSnapshotAsync(accountName, cancellationToken).ConfigureAwait(false);
        _logger.Info("player.refresh.ok", new Dictionary<string, object?>
        {
            ["account"] = accountName
        });
        return snapshot;
    }

    public async Task<PlayerAbnormalStatusSnapshot> ReadPlayerAbnormalStatusesAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadPlayerAbnormalStatusSnapshotAsync(accountName, cancellationToken).ConfigureAwait(false);
        _logger.Info("player_abnormal.refresh.ok", new Dictionary<string, object?>
        {
            ["account"] = accountName,
            ["category2EntryCount"] = snapshot.Category2EntryCount,
            ["category2EntrySummary"] = snapshot.Category2EntrySummary
        });
        return snapshot;
    }

    public async Task<LockedTargetAbnormalStatusSnapshot> ReadLockedTargetAbnormalStatusesAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadLockedTargetAbnormalStatusSnapshotAsync(accountName, cancellationToken).ConfigureAwait(false);
        _logger.Info("locked_target_abnormal.refresh.ok", new Dictionary<string, object?>
        {
            ["account"] = accountName,
            ["hasTarget"] = snapshot.HasTarget,
            ["targetEntityId"] = snapshot.Target.TargetEntityId,
            ["abnormalStatusCount"] = snapshot.AbnormalStatusCount,
            ["physicalDebuffCount"] = snapshot.PhysicalDebuffCount
        });
        return snapshot;
    }

    public async Task<SummonedPetSnapshot> ReadSummonedPetAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadSummonedPetSnapshotAsync(accountName, cancellationToken).ConfigureAwait(false);
        _logger.Info("summoned_pet.refresh.ok", new Dictionary<string, object?>
        {
            ["account"] = accountName,
            ["isSummoned"] = snapshot.IsSummoned,
            ["serverObjectId"] = snapshot.ServerObjectId,
            ["templateId"] = snapshot.NpcTemplateId,
            ["name"] = snapshot.Name
        });
        return snapshot;
    }

    public async Task<SummonedPetRosterSnapshot> ReadSummonedPetRosterAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadSummonedPetRosterSnapshotAsync(accountName, cancellationToken).ConfigureAwait(false);
        _logger.Info("summoned_pet_roster.refresh.ok", new Dictionary<string, object?>
        {
            ["account"] = accountName,
            ["localPetSummoned"] = snapshot.LocalPlayerPet.IsSummoned,
            ["partyPetCount"] = snapshot.PartyMemberPetCount,
            ["visibleSummonedPetCount"] = snapshot.VisibleSummonedPetCount
        });
        return snapshot;
    }

    public async Task<TeamSnapshot> ReadTeamSnapshotAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        var monitor = new TeamMonitor(CreateSnapshotReader(accountName, cancellationToken), _logger);
        return await monitor.ReadSnapshotAsync().ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SkillSnapshot>> RefreshSkillsAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadSkillsAsync(accountName, cancellationToken).ConfigureAwait(false);
        _logger.Info("skills.refresh.ok", new Dictionary<string, object?>
        {
            ["account"] = accountName,
            ["count"] = snapshot.Count
        });
        return snapshot;
    }

    public async Task<QuickbarSnapshot> ReadQuickbarAsync(string? accountName = null, CancellationToken cancellationToken = default) =>
        (await CreateSnapshotReader(accountName, cancellationToken).ReadQuickbarAsync().ConfigureAwait(false)).Value;

    public async Task<IReadOnlyList<WorldObjectSnapshot>> RefreshWorldObjectsAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadWorldObjectsAsync(accountName, cancellationToken).ConfigureAwait(false);
        _logger.Info("world_objects.refresh.ok", new Dictionary<string, object?>
        {
            ["account"] = accountName,
            ["count"] = snapshot.Count
        });
        return snapshot;
    }

    public async Task<PlayerSnapshot> ReadPlayerForVmmDeviceAsync(
        string accountName,
        string vmmDeviceName,
        CancellationToken cancellationToken = default)
    {
        var config = new AccountConfig
        {
            AccountName = accountName,
            ProcessId = 0,
            TargetProcessName = string.Empty,
            VmmDeviceName = vmmDeviceName
        };
        var read = await _snapshotReaders.Create(config, _logger, cancellationToken)
            .ReadPlayerAsync()
            .ConfigureAwait(false);
        return read.Value;
    }

    public async Task<GatherSnapshot> RefreshGatherSnapshotAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadGatherSnapshotAsync(accountName, cancellationToken).ConfigureAwait(false);
        _logger.Info("gather.refresh.ok", new Dictionary<string, object?>
        {
            ["account"] = accountName,
            ["objects"] = snapshot.Objects.Count,
            ["nearbyPlayers"] = snapshot.NearbyPlayers.Count,
            ["nearbyMonsters"] = snapshot.NearbyMonsters.Count
        });
        return snapshot;
    }

    public async Task<IReadOnlyList<InventoryItemSnapshot>> RefreshInventoryAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadInventoryAsync(accountName, cancellationToken).ConfigureAwait(false);
        _logger.Info("inventory.refresh.ok", new Dictionary<string, object?>
        {
            ["account"] = accountName,
            ["count"] = snapshot.Count
        });
        return snapshot;
    }

    public async Task<ChannelTransitionSnapshot> ReadSceneForPathRecordingAsync(
        string? accountName = null, CancellationToken cancellationToken = default) =>
        (await CreateSnapshotReader(accountName, cancellationToken).ReadChannelTransitionAsync()
            .WaitAsync(cancellationToken).ConfigureAwait(false)).Value;

    public async Task<PlayerSnapshot> ReadPlayerForPathRecordingAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadPlayerSnapshotAsync(
            accountName,
            cancellationToken).ConfigureAwait(false);
        _logger.Info("path_record.player_refresh.ok", new Dictionary<string, object?>
        {
            ["account"] = accountName
        });
        return snapshot;
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

    public Task<OperationResult> TestMoveMouseToScreenPointAsync(
        int x, int y, CancellationToken cancellationToken = default)
    {
        Task<OperationResult> Execute() => TestMoveMouseToScreenPointCoreAsync(x, y, cancellationToken);
        return Orchestrator is null ? Execute() : Orchestrator.RunManualInputAsync(Execute);
    }

    private async Task<OperationResult> TestMoveMouseToScreenPointCoreAsync(
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

    public Task<OperationResult> TestSwitchChannelAsync(string accountName, int targetChannelNumber, CancellationToken cancellationToken = default)
    {
        if (_keyboardInput is null) return Task.FromResult(OperationResult.Fail("鼠标输入不可用。"));
        if (targetChannelNumber < 1) return Task.FromResult(OperationResult.Fail("请先选择要切换的频道。"));
        async Task<OperationResult> Execute()
        {
            var config = ResolveSnapshotConfig(accountName);
            return await new DataDrivenChannelSwitchExecutor(_keyboardInput, _snapshotReaders, _logger)
                .ExecuteAsync(new FixedChannelSwitchRequest(accountName, targetChannelNumber, 0, 1,
                    Array.Empty<FixedChannelClickPoint>()) { Config = config }, cancellationToken).ConfigureAwait(false);
        }
        return Orchestrator is null ? Execute() : Orchestrator.RunManualInputAsync(Execute);
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
            _snapshotReaders,
            _logger,
            Accounts,
            new AccountWorkerOptions(),
            cancellationToken);
        var vectors = points.Select(point => point.ToVector3()).ToArray();
        return _stationaryCombatController.ExecutePathOnceAsync(context, pathName, vectors);
    }

#if DEBUG
    private static async Task<RoadhogApiProbeCheckResult> RunApiProbeCheckAsync<T>(
        string name,
        Func<CancellationToken, Task<T>> read,
        Func<T, string> describe,
        CancellationToken cancellationToken)
    {
        try
        {
            var value = await read(cancellationToken).ConfigureAwait(false);
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
        if (_snapshotReaders is not IRoadhogSnapshotDiagnostics diagnostics)
        {
            return CreateFailedAddressProbeChecks("Address probe provider is unavailable.");
        }

        try
        {
            var result = await diagnostics
                .ProbeProviderAsync(ResolveSnapshotConfig(accountName), cancellationToken)
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

    private async Task<LockedTargetSnapshot> ReadLockedTargetSnapshotAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        var read = await CreateSnapshotReader(accountName, cancellationToken)
            .ReadLockedTargetAsync()
            .ConfigureAwait(false);
        return read.Value;
    }
#endif

    private async Task<IReadOnlyList<SkillSnapshot>> ReadSkillsAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        var read = await CreateSnapshotReader(accountName, cancellationToken)
            .ReadSkillsAsync()
            .ConfigureAwait(false);
        return read.Value;
    }

    private async Task<IReadOnlyList<WorldObjectSnapshot>> ReadWorldObjectsAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        var read = await CreateSnapshotReader(accountName, cancellationToken)
            .ReadWorldObjectsAsync()
            .ConfigureAwait(false);
        return read.Value;
    }

    private async Task<ChannelSnapshot> ReadChannelSnapshotAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        var read = await CreateSnapshotReader(accountName, cancellationToken)
            .ReadChannelAsync()
            .ConfigureAwait(false);
        return read.Value;
    }

    private async Task<GatherSnapshot> ReadGatherSnapshotAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        var read = await CreateSnapshotReader(accountName, cancellationToken)
            .ReadGatherSnapshotAsync()
            .ConfigureAwait(false);
        return read.Value;
    }

    private async Task<IReadOnlyList<InventoryItemSnapshot>> ReadInventoryAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        var read = await CreateSnapshotReader(accountName, cancellationToken)
            .ReadInventoryAsync()
            .ConfigureAwait(false);
        return read.Value;
    }

#if DEBUG
    private async Task<ulong> ReadInventoryMoneyAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        var read = await CreateSnapshotReader(accountName, cancellationToken)
            .ReadInventoryMoneyAsync()
            .ConfigureAwait(false);
        return read.Value;
    }

    private async Task<int> ReadInventoryCapacityAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        var read = await CreateSnapshotReader(accountName, cancellationToken)
            .ReadInventoryCapacityAsync()
            .ConfigureAwait(false);
        return read.Value;
    }

    private async Task<IReadOnlyList<LootCorpseSnapshot>> ReadLootCorpsesAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        var read = await CreateSnapshotReader(accountName, cancellationToken)
            .ReadLootCorpsesAsync()
            .ConfigureAwait(false);
        return read.Value;
    }

    private async Task<InventoryWindowSnapshot> ReadInventoryWindowSnapshotAsync(
        string? accountName,
        InventoryWindowRectSource rectSource,
        CancellationToken cancellationToken)
    {
        var read = await CreateSnapshotReader(accountName, cancellationToken)
            .ReadInventoryWindowAsync(rectSource)
            .ConfigureAwait(false);
        return read.Value;
    }
#endif

    private async Task<PlayerSnapshot> ReadPlayerSnapshotAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        var snapshots = CreateSnapshotReader(accountName, cancellationToken);
        var read = await snapshots.ReadPlayerAsync().ConfigureAwait(false);
        return read.Value;
    }

    private async Task<PlayerAbnormalStatusSnapshot> ReadPlayerAbnormalStatusSnapshotAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        var read = await CreateSnapshotReader(accountName, cancellationToken)
            .ReadPlayerAbnormalStatusesAsync()
            .ConfigureAwait(false);
        return read.Value;
    }

    private async Task<LockedTargetAbnormalStatusSnapshot> ReadLockedTargetAbnormalStatusSnapshotAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        var read = await CreateSnapshotReader(accountName, cancellationToken)
            .ReadLockedTargetAbnormalStatusesAsync()
            .ConfigureAwait(false);
        return read.Value;
    }

    private async Task<SummonedPetSnapshot> ReadSummonedPetSnapshotAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        var read = await CreateSnapshotReader(accountName, cancellationToken)
            .ReadSummonedPetAsync()
            .ConfigureAwait(false);
        return read.Value;
    }

    private async Task<SummonedPetRosterSnapshot> ReadSummonedPetRosterSnapshotAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        var read = await CreateSnapshotReader(accountName, cancellationToken)
            .ReadSummonedPetRosterAsync()
            .ConfigureAwait(false);
        return read.Value;
    }

    private IRoadhogSnapshotReader CreateSnapshotReader(
        string? accountName,
        CancellationToken cancellationToken)
    {
        return _snapshotReaders.Create(ResolveSnapshotConfig(accountName), _logger, cancellationToken);
    }

    private AccountConfig ResolveSnapshotConfig(string? accountName)
    {
        AccountConfig config;
        if (string.IsNullOrWhiteSpace(accountName))
        {
            config = new AccountConfig();
        }
        else
        {
            var runtimeAccount = Accounts.Snapshot()
                .FirstOrDefault(item => string.Equals(
                    item.AccountName,
                    accountName,
                    StringComparison.OrdinalIgnoreCase));
            config = runtimeAccount is null
                ? LoadSavedAccountConfig(accountName) ?? new AccountConfig { AccountName = accountName }
                : new AccountConfig
                {
                    AccountName = runtimeAccount.AccountName,
                    ProcessId = runtimeAccount.ProcessId,
                    TargetProcessName = runtimeAccount.TargetProcessName,
                    VmmDeviceName = runtimeAccount.VmmDeviceName
                };
        }

        return config;
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

    private static int ClampInt(int value, int min, int max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    private static int ReadRawIntFromEnv(string name, int defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return int.TryParse(raw, out var value) ? value : defaultValue;
    }

    private static bool DeviceMatchesHardwareKey(HardwareDeviceFeature device, string hardwareKey)
    {
        var expected = hardwareKey.Trim();
        return string.Equals(device.BindingKey.Trim(), expected, StringComparison.OrdinalIgnoreCase) ||
            device.AliasKeys.Any(alias => string.Equals(alias.Trim(), expected, StringComparison.OrdinalIgnoreCase));
    }
}
