using Roadhog.Application.Input;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Api;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Hardware;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;

namespace Roadhog.Application;

public sealed class RoadhogRuntime
{
    private readonly IRoadhogGameApi _gameApi;
    private readonly IRoadhogLogger _logger;
    private readonly IAccountConfigStore? _accountConfigStore;
    private readonly IHardwareDeviceResolver? _hardwareResolver;
    private readonly IKeyboardInput? _keyboardInput;
    private readonly StationaryCombatController? _stationaryCombatController;
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
    private static readonly TimeSpan InventoryDragSettleDelay = TimeSpan.FromMilliseconds(500);
    private static readonly int[] InventoryTitleDragXOffsets = { 160, 140, 180, 120, 200, 100, 220 };
    private static readonly int[] InventoryTitleDragYOffsets = { 25, 22, 18, 15, 10, 5 };

    public RoadhogRuntime(
        IRoadhogGameApi gameApi,
        IRoadhogLogger logger,
        AccountRuntimeManager accounts,
        AccountOrchestrator orchestrator,
        IAccountConfigStore? accountConfigStore = null,
        IHardwareDeviceResolver? hardwareResolver = null,
        IKeyboardInput? keyboardInput = null,
        StationaryCombatController? stationaryCombatController = null)
    {
        _gameApi = gameApi;
        _logger = logger;
        _accountConfigStore = accountConfigStore;
        _hardwareResolver = hardwareResolver;
        _keyboardInput = keyboardInput;
        _stationaryCombatController = stationaryCombatController;
        Accounts = accounts;
        Orchestrator = orchestrator;
    }

    public AccountRuntimeManager Accounts { get; }

    public AccountOrchestrator Orchestrator { get; }

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
                ["harmfulAbnormalCount"] = result.Value?.HarmfulAbnormalCount ?? 0,
                ["harmfulAbnormalSummary"] = result.Value?.HarmfulAbnormalSummary
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

        var read = await inventoryApi.ReadInventoryWindowAsync(context, cancellationToken).ConfigureAwait(false);
        if (!read.Success || read.Value is null)
        {
            return OperationResult.Fail("Inventory window read failed: " + read.Error);
        }

        var snapshot = read.Value;
        if (!snapshot.IsOpen)
        {
            var open = await PressInventoryToggleAsync(cancellationToken).ConfigureAwait(false);
            if (!open.Success)
            {
                return open;
            }

            await DelayAsync(InventoryOpenSettleDelay, cancellationToken).ConfigureAwait(false);
            read = await inventoryApi.ReadInventoryWindowAsync(context, cancellationToken).ConfigureAwait(false);
            if (!read.Success || read.Value is null)
            {
                return OperationResult.Fail("Inventory window read after open failed: " + read.Error);
            }

            snapshot = read.Value;
            if (!snapshot.IsOpen)
            {
                return OperationResult.Fail("Inventory window did not open after pressing " + InventoryToggleKey + ".");
            }
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

    private Task<OperationResult<PlayerSnapshot>> ReadPlayerSnapshotAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        if (_gameApi is IRoadhogScopedGameApi scopedApi &&
            !string.IsNullOrWhiteSpace(accountName))
        {
            return scopedApi.ReadPlayerAsync(CreateReadContext(accountName), cancellationToken);
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

    private GameApiReadContext CreateReadContext(string accountName)
    {
        var account = Accounts.Snapshot()
            .FirstOrDefault(item => string.Equals(item.AccountName, accountName, StringComparison.OrdinalIgnoreCase));

        if (account is not null)
        {
            return new GameApiReadContext(
                account.AccountName,
                account.ProcessId,
                account.TargetProcessName,
                account.VmmDeviceName);
        }

        var config = LoadSavedAccountConfig(accountName);
        return config is null
            ? new GameApiReadContext(accountName, 0, string.Empty, string.Empty)
            : new GameApiReadContext(
                config.AccountName,
                config.ProcessId,
                config.TargetProcessName,
                config.VmmDeviceName);
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

    private static bool DeviceMatchesHardwareKey(HardwareDeviceFeature device, string hardwareKey)
    {
        var expected = hardwareKey.Trim();
        return string.Equals(device.BindingKey.Trim(), expected, StringComparison.OrdinalIgnoreCase) ||
            device.AliasKeys.Any(alias => string.Equals(alias.Trim(), expected, StringComparison.OrdinalIgnoreCase));
    }
}
