using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using MemProcVmm = Vmmsharp.Vmm;
using Vmmsharp;

namespace Roadhog.Infrastructure.Vmm;

public sealed class AionVmmGameApi : IRoadhogScopedGameApi
{
    private const ulong EntitySystemPointerRva = 0x904690;
    private const ulong ServerObjectTreeRva = 0xD21740;
    private const ulong LocalEntityIdRva = 0xD21798;
    private const ulong LocalMaxHpRva = 0xD267DC;
    private const ulong LocalCurrentHpRva = 0xD267E0;
    private const ulong LocalMaxMpRva = 0xD267E4;
    private const ulong LocalCurrentMpRva = 0xD267E8;
    private const ulong LocalCurrentDpRva = 0xD267EE;
    private const ulong CameraPitchRva = 0xD1AD14;
    private const ulong CameraRollRva = 0xD1AD18;
    private const ulong CameraYawRva = 0xD1AD1C;
    private const ulong SpecialCameraModeRva = 0xD218C8;
    private const ulong SpecialCameraPitchRva = 0xD218D8;
    private const ulong SpecialCameraRollRva = 0xD218DC;
    private const ulong SpecialCameraYawRva = 0xD218E0;
    private const ulong SkillManagerGlobalRva = 0xD004A0;
    private const ulong LearnedSkillTreeOffset = 0x828;
    private const ulong LearnedSkillOuterSkillIdOffset = 0x20;
    private const ulong LearnedSkillOuterLevelTreeHeaderOffset = 0x28;
    private const ulong LearnedSkillOuterLevelTreeSizeOffset = 0x30;
    private const ulong LearnedSkillInnerLevelOffset = 0x20;
    private const ulong LearnedSkillInnerItemListHeaderOffset = 0x28;
    private const ulong LearnedSkillInnerItemListSizeOffset = 0x30;
    private const ulong NodeLeftOffset = 0x00;
    private const ulong NodeParentOffset = 0x08;
    private const ulong NodeRightOffset = 0x10;
    private const ulong NodeIsNilOffset = 0x19;
    private const ulong NodeIdOffset = 0x20;
    private const ulong NodeEntityOffset = 0x28;
    private const ulong ListNodePrevOffset = 0x08;
    private const ulong ListNodeValueOffset = 0x10;

    private const ulong EntityTreeOffset = 0x58;
    private const ulong EntityTypeOffset = 0xF2;
    private const ulong EntityPositionFlagsOffset = 0xC0;
    private const uint EntityUseAlternatePositionFlag = 0x400;
    private const ulong EntityWorldPositionOffset = 0x4B4;
    private const ulong EntityWorldAnglesOffset = 0x4E8;
    private const ulong EntityLocalPositionOffset = 0x4F4;
    private const ulong EntityProxyManagerVfuncOffset = 0xB8;

    private const ulong ServerNodeServerObjectIdOffset = 0x1C;
    private const ulong ServerNodeEntityIdOffset = 0x20;
    private const ushort EntityTypeNpc = 3;

    private const ulong ActorEntityOffset = 0x08;
    private const ulong ActorObjectTypeOffset = 0x20;
    private const ulong ActorServerObjectIdOffset = 0x2C;
    private const ulong ActorNpcTemplateIdOffset = 0x30;
    private const ulong ActorLevelOffset = 0x3E;
    private const ulong ActorHpPercentOffset = 0x40;
    private const ulong ActorNameOffset = 0x42;
    private const ulong ActorInteractionStateOffset = 0x1CC;
    private const ulong ActorTargetServerObjectIdOffset = 0x358;
    private const ulong ActorAbnormalStatusBeginOffset = 0xF18;
    private const ulong ActorAbnormalStatusEndOffset = 0xF20;
    private const ulong ActorAbnormalCategory2CountOffset = 0xF38;
    private const ulong ActorMaxHpOffset = 0x11A0;
    private const ulong ActorCurrentHpOffset = 0x11A4;
    private const ulong ActorLootableFlagOffset = 0x11E0;
    private const ulong AbnormalStatusEntrySize = 0x12;
    private const int MaxActorAbnormalStatusEntries = 512;

    private const ulong SkillItemSkillIdOffset = 0x08;
    private const ulong SkillItemField0COffset = 0x0C;
    private const ulong SkillItemRankValueOffset = 0x10;
    private const ulong SkillItemNameOffset = 0x18;
    private const ulong SkillItemCooldownDurationOffset = 0x50;
    private const ulong SkillItemCooldownEndTimeOffset = 0x54;
    private const ulong SkillItemToggleStateOffset = 0x60;
    private const ulong SkillItemSkillLevelOffset = 0x64;
    private const ulong SkillItemStaticFieldD8Offset = 0x68;
    private const ulong SkillItemRuntimeStateOffset = 0x6C;
    private const ulong SkillItemSourceFlagsOffset = 0x74;

    private readonly AionVmmGameApiOptions _options;
    private readonly IRoadhogLogger _logger;
    private readonly Dictionary<string, VmmConnection> _connections = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _connectionSync = new();
    private readonly object _xmlSync = new();
    private SkillXmlCatalog? _xmlCatalog;
    private NpcXmlCatalog? _npcXmlCatalog;
    private bool _nativeLibrariesLoaded;

    public AionVmmGameApi(AionVmmGameApiOptions options, IRoadhogLogger logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task<OperationResult<PlayerSnapshot>> ReadPlayerAsync(CancellationToken cancellationToken = default)
    {
        var context = new GameApiReadContext(string.Empty, 0, string.Empty, string.Empty);
        return ReadPlayerAsync(context, cancellationToken);
    }

    public Task<OperationResult<PlayerSnapshot>> ReadPlayerAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ReadPlayerCore(context), cancellationToken);
    }

    public Task<OperationResult<PlayerAbnormalStatusSnapshot>> ReadPlayerAbnormalStatusesAsync(
        CancellationToken cancellationToken = default)
    {
        var context = new GameApiReadContext(string.Empty, 0, string.Empty, string.Empty);
        return ReadPlayerAbnormalStatusesAsync(context, cancellationToken);
    }

    public Task<OperationResult<PlayerAbnormalStatusSnapshot>> ReadPlayerAbnormalStatusesAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ReadPlayerAbnormalStatusesCore(context), cancellationToken);
    }

    public Task<OperationResult<LockedTargetSnapshot>> ReadLockedTargetAsync(CancellationToken cancellationToken = default)
    {
        var context = new GameApiReadContext(string.Empty, 0, string.Empty, string.Empty);
        return ReadLockedTargetAsync(context, cancellationToken);
    }

    public Task<OperationResult<LockedTargetSnapshot>> ReadLockedTargetAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ReadLockedTargetCore(context), cancellationToken);
    }

    public Task<OperationResult<IReadOnlyList<SkillSnapshot>>> ReadSkillsAsync(CancellationToken cancellationToken = default)
    {
        var context = new GameApiReadContext(string.Empty, 0, string.Empty, string.Empty);
        return ReadSkillsAsync(context, cancellationToken);
    }

    public Task<OperationResult<IReadOnlyList<SkillSnapshot>>> ReadSkillsAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ReadSkillsCore(context, null), cancellationToken);
    }

    public Task<OperationResult<IReadOnlyList<SkillSnapshot>>> ReadSkillsAsync(
        GameApiReadContext context,
        IReadOnlyCollection<uint> skillIds,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ReadSkillsCore(context, skillIds), cancellationToken);
    }

    public Task<OperationResult<IReadOnlyList<InventoryItemSnapshot>>> ReadInventoryAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<IReadOnlyList<InventoryItemSnapshot>>.Fail("Direct VMM inventory snapshot is not implemented yet."));
    }

    public Task<OperationResult<IReadOnlyList<WorldObjectSnapshot>>> ReadWorldObjectsAsync(CancellationToken cancellationToken = default)
    {
        var context = new GameApiReadContext(string.Empty, 0, string.Empty, string.Empty);
        return ReadWorldObjectsAsync(context, cancellationToken);
    }

    public Task<OperationResult<IReadOnlyList<WorldObjectSnapshot>>> ReadWorldObjectsAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ReadWorldObjectsCore(context), cancellationToken);
    }

    public Task<OperationResult<IReadOnlyList<LootCorpseSnapshot>>> ReadLootCorpsesAsync(CancellationToken cancellationToken = default)
    {
        var context = new GameApiReadContext(string.Empty, 0, string.Empty, string.Empty);
        return ReadLootCorpsesAsync(context, cancellationToken);
    }

    public Task<OperationResult<IReadOnlyList<LootCorpseSnapshot>>> ReadLootCorpsesAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ReadLootCorpsesCore(context), cancellationToken);
    }

    private OperationResult<LockedTargetSnapshot> ReadLockedTargetCore(GameApiReadContext context)
    {
        try
        {
            var connection = GetOrCreateConnection(context.VmmDeviceName);
            lock (connection.SyncRoot)
            {
                if (!TryResolveProcess(connection.Vmm, context, out var process, out var processError))
                {
                    return OperationResult<LockedTargetSnapshot>.Fail(processError);
                }

                var moduleName = ResolveModuleName();
                var gameBase = process.GetModuleBase(moduleName);
                if (gameBase == 0)
                {
                    return OperationResult<LockedTargetSnapshot>.Fail("Module not found: " + moduleName);
                }

                if (!TryReadLockedTarget(process, gameBase, out var target, out var readError))
                {
                    return OperationResult<LockedTargetSnapshot>.Fail(readError);
                }

                var snapshot = ToLockedTargetSnapshot(target);
                _logger.Info("vmm.locked_target.read", new Dictionary<string, object?>
                {
                    ["account"] = context.AccountName,
                    ["pid"] = SafeGetProcessPid(process),
                    ["targetEntityId"] = snapshot.TargetEntityId,
                    ["objectType"] = snapshot.ObjectType,
                    ["hp"] = snapshot.CurrentHp,
                    ["maxHp"] = snapshot.MaxHp,
                    ["isMonsterAlive"] = snapshot.IsMonsterAlive
                });

                return OperationResult<LockedTargetSnapshot>.Ok(snapshot);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("vmm.locked_target.exception", ex, new Dictionary<string, object?> { ["account"] = context.AccountName });
            return OperationResult<LockedTargetSnapshot>.Fail(ex.Message);
        }
    }

    private OperationResult<PlayerSnapshot> ReadPlayerCore(GameApiReadContext context)
    {
        try
        {
            var connection = GetOrCreateConnection(context.VmmDeviceName);
            lock (connection.SyncRoot)
            {
                if (!TryResolveProcess(connection.Vmm, context, out var process, out var processError))
                {
                    return OperationResult<PlayerSnapshot>.Fail(processError);
                }

                var moduleName = ResolveModuleName();
                var gameBase = process.GetModuleBase(moduleName);
                if (gameBase == 0)
                {
                    return OperationResult<PlayerSnapshot>.Fail("Module not found: " + moduleName);
                }

                if (!TryReadLocalPlayer(process, gameBase, out var snapshot, out var readError))
                {
                    return OperationResult<PlayerSnapshot>.Fail(readError);
                }

                _logger.Info("vmm.player.read", new Dictionary<string, object?>
                {
                    ["account"] = context.AccountName,
                    ["pid"] = SafeGetProcessPid(process),
                    ["entityId"] = snapshot.EntityId,
                    ["targetEntityId"] = snapshot.TargetEntityId,
                    ["hp"] = snapshot.CurrentHp,
                    ["maxHp"] = snapshot.MaxHp,
                    ["mp"] = snapshot.CurrentMp,
                    ["maxMp"] = snapshot.MaxMp,
                    ["hasPosition"] = snapshot.Position is not null
                });

                return OperationResult<PlayerSnapshot>.Ok(snapshot);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("vmm.player.exception", ex, new Dictionary<string, object?> { ["account"] = context.AccountName });
            return OperationResult<PlayerSnapshot>.Fail(ex.Message);
        }
    }

    private OperationResult<PlayerAbnormalStatusSnapshot> ReadPlayerAbnormalStatusesCore(GameApiReadContext context)
    {
        try
        {
            var connection = GetOrCreateConnection(context.VmmDeviceName);
            lock (connection.SyncRoot)
            {
                if (!TryResolveProcess(connection.Vmm, context, out var process, out var processError))
                {
                    return OperationResult<PlayerAbnormalStatusSnapshot>.Fail(processError);
                }

                var moduleName = ResolveModuleName();
                var gameBase = process.GetModuleBase(moduleName);
                if (gameBase == 0)
                {
                    return OperationResult<PlayerAbnormalStatusSnapshot>.Fail("Module not found: " + moduleName);
                }

                if (!TryReadLocalPlayerAbnormalStatuses(process, gameBase, out var snapshot, out var readError))
                {
                    return OperationResult<PlayerAbnormalStatusSnapshot>.Fail(readError);
                }

                _logger.Info("vmm.player_abnormal.read", new Dictionary<string, object?>
                {
                    ["account"] = context.AccountName,
                    ["pid"] = SafeGetProcessPid(process),
                    ["entityId"] = snapshot.EntityId,
                    ["abnormalCategory2Count"] = snapshot.AbnormalCategory2Count,
                    ["abnormalEntryCount"] = snapshot.Entries.Count,
                    ["harmfulAbnormalCount"] = snapshot.HarmfulAbnormalCount,
                    ["harmfulAbnormalSummary"] = snapshot.HarmfulAbnormalSummary
                });

                return OperationResult<PlayerAbnormalStatusSnapshot>.Ok(snapshot);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("vmm.player_abnormal.exception", ex, new Dictionary<string, object?> { ["account"] = context.AccountName });
            return OperationResult<PlayerAbnormalStatusSnapshot>.Fail(ex.Message);
        }
    }

    private OperationResult<IReadOnlyList<WorldObjectSnapshot>> ReadWorldObjectsCore(GameApiReadContext context)
    {
        try
        {
            var connection = GetOrCreateConnection(context.VmmDeviceName);
            lock (connection.SyncRoot)
            {
                if (!TryResolveProcess(connection.Vmm, context, out var process, out var processError))
                {
                    return OperationResult<IReadOnlyList<WorldObjectSnapshot>>.Fail(processError);
                }

                var moduleName = ResolveModuleName();
                var gameBase = process.GetModuleBase(moduleName);
                if (gameBase == 0)
                {
                    return OperationResult<IReadOnlyList<WorldObjectSnapshot>>.Fail("Module not found: " + moduleName);
                }

                var npcCatalog = GetNpcXmlCatalog();
                if (!TryReadWorldObjects(process, gameBase, npcCatalog.Details, out var objects, out var counters, out var readError))
                {
                    return OperationResult<IReadOnlyList<WorldObjectSnapshot>>.Fail(readError);
                }

                return OperationResult<IReadOnlyList<WorldObjectSnapshot>>.Ok(objects);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("vmm.world_objects.exception", ex, new Dictionary<string, object?> { ["account"] = context.AccountName });
            return OperationResult<IReadOnlyList<WorldObjectSnapshot>>.Fail(ex.Message);
        }
    }

    private OperationResult<IReadOnlyList<LootCorpseSnapshot>> ReadLootCorpsesCore(GameApiReadContext context)
    {
        try
        {
            var connection = GetOrCreateConnection(context.VmmDeviceName);
            lock (connection.SyncRoot)
            {
                if (!TryResolveProcess(connection.Vmm, context, out var process, out var processError))
                {
                    return OperationResult<IReadOnlyList<LootCorpseSnapshot>>.Fail(processError);
                }

                var moduleName = ResolveModuleName();
                var gameBase = process.GetModuleBase(moduleName);
                if (gameBase == 0)
                {
                    return OperationResult<IReadOnlyList<LootCorpseSnapshot>>.Fail("Module not found: " + moduleName);
                }

                if (!TryReadLootCorpses(process, gameBase, out var corpses, out var counters, out var readError))
                {
                    return OperationResult<IReadOnlyList<LootCorpseSnapshot>>.Fail(readError);
                }

                _logger.Info("vmm.loot_corpses.read", new Dictionary<string, object?>
                {
                    ["account"] = context.AccountName,
                    ["pid"] = SafeGetProcessPid(process),
                    ["rows"] = corpses.Count,
                    ["scannedServerObjects"] = counters.ScannedServerObjects,
                    ["resolvedEntities"] = counters.ResolvedEntities,
                    ["npcLikeEntities"] = counters.NpcLikeEntities
                });

                return OperationResult<IReadOnlyList<LootCorpseSnapshot>>.Ok(corpses);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("vmm.loot_corpses.exception", ex, new Dictionary<string, object?> { ["account"] = context.AccountName });
            return OperationResult<IReadOnlyList<LootCorpseSnapshot>>.Fail(ex.Message);
        }
    }

    private OperationResult<IReadOnlyList<SkillSnapshot>> ReadSkillsCore(
        GameApiReadContext context,
        IReadOnlyCollection<uint>? requestedSkillIds)
    {
        try
        {
            var skillIdFilter = BuildSkillIdFilter(requestedSkillIds);
            var connection = GetOrCreateConnection(context.VmmDeviceName);
            lock (connection.SyncRoot)
            {
                if (!TryResolveProcess(connection.Vmm, context, out var process, out var processError))
                {
                    return OperationResult<IReadOnlyList<SkillSnapshot>>.Fail(processError);
                }

                var moduleName = ResolveModuleName();
                var gameBase = process.GetModuleBase(moduleName);
                if (gameBase == 0)
                {
                    return OperationResult<IReadOnlyList<SkillSnapshot>>.Fail("Module not found: " + moduleName);
                }

                if (!TryReadHighestLearnedSkills(process, gameBase, skillIdFilter, out var skills, out _, out var readError))
                {
                    return OperationResult<IReadOnlyList<SkillSnapshot>>.Fail(readError);
                }

                AttachSkillXmlStaticDetails(GetSkillXmlCatalog().Details, skills);

                if (_options.GroupByDisplayName)
                {
                    skills = SelectHighestDisplaySkillPerName(skills);
                }

                if (_options.FilterUtilitySkills)
                {
                    skills = FilterUsefulLearnedSkills(skills);
                }

                var snapshots = skills
                    .Select(ToSkillSnapshot)
                    .OrderBy(skill => skill.Name, StringComparer.CurrentCulture)
                    .ToArray();

                _logger.Info("vmm.skills.read", new Dictionary<string, object?>
                {
                    ["account"] = context.AccountName,
                    ["pid"] = SafeGetProcessPid(process),
                    ["processName"] = process.Name,
                    ["count"] = snapshots.Length,
                    ["requestedSkillCount"] = skillIdFilter?.Count
                });

                return OperationResult<IReadOnlyList<SkillSnapshot>>.Ok(snapshots);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("vmm.skills.exception", ex, new Dictionary<string, object?> { ["account"] = context.AccountName });
            return OperationResult<IReadOnlyList<SkillSnapshot>>.Fail(ex.Message);
        }
    }

    private VmmConnection GetOrCreateConnection(string? contextVmmDeviceName)
    {
        var deviceName = ResolveVmmDeviceName(contextVmmDeviceName);
        var remote = ResolveVmmRemote();
        var key = deviceName + "|" + remote;

        lock (_connectionSync)
        {
            if (_connections.TryGetValue(key, out var existing))
            {
                return existing;
            }

            LoadNativeLibrariesOnce();
            var args = string.IsNullOrWhiteSpace(remote)
                ? new[] { "-device", deviceName }
                : new[] { "-device", deviceName, "-remote", remote };

            var created = new VmmConnection(deviceName, remote, new MemProcVmm(args));
            _connections[key] = created;
            _logger.Info("vmm.connection.created", new Dictionary<string, object?>
            {
                ["device"] = deviceName,
                ["remote"] = remote
            });

            return created;
        }
    }

    private void LoadNativeLibrariesOnce()
    {
        if (_nativeLibrariesLoaded)
        {
            return;
        }

        var memProcFsHome = _options.MemProcFsHome;
        if (string.IsNullOrWhiteSpace(memProcFsHome))
        {
            memProcFsHome = Environment.GetEnvironmentVariable("MEMPROCFS_HOME");
        }

        if (string.IsNullOrWhiteSpace(memProcFsHome) && Directory.Exists(@"C:\MemProcFS"))
        {
            memProcFsHome = @"C:\MemProcFS";
        }

        if (!string.IsNullOrWhiteSpace(memProcFsHome))
        {
            MemProcVmm.LoadNativeLibrary(memProcFsHome);
            _logger.Info("vmm.native.loaded", new Dictionary<string, object?> { ["path"] = memProcFsHome });
        }

        _nativeLibrariesLoaded = true;
    }

    private bool TryResolveProcess(
        MemProcVmm vmm,
        GameApiReadContext context,
        out VmmProcess process,
        out string error)
    {
        error = string.Empty;
        var processName = ResolveProcessName(context.TargetProcessName);

        if (context.ProcessId > 0)
        {
            if (TryGetVmmProcessByPid(vmm, context.ProcessId, out process, out var foundPidMethod, out var pidError))
            {
                if (process.IsValid)
                {
                    return true;
                }

                error = "Target process not found by PID: " + context.ProcessId;
                return false;
            }

            if (foundPidMethod)
            {
                error = pidError;
                return false;
            }

            if (HasMultipleLocalProcesses(processName))
            {
                error = "Multiple '" + processName + "' processes exist, but this Vmmsharp build does not expose PID binding. Account PID=" + context.ProcessId + ".";
                process = default!;
                return false;
            }
        }

        if (context.ProcessId <= 0 && HasMultipleLocalProcesses(processName))
        {
            error = "Multiple '" + processName + "' processes exist. Start/bind the account first so Roadhog has a ProcessId before refreshing skills.";
            process = default!;
            return false;
        }

        process = vmm.Process(processName);
        if (!process.IsValid)
        {
            error = "Target process not found: " + processName;
            return false;
        }

        var actualPid = SafeGetProcessPid(process);
        if (context.ProcessId > 0 && actualPid > 0 && actualPid != context.ProcessId)
        {
            error = "Resolved process PID mismatch. Expected " + context.ProcessId + ", got " + actualPid + ".";
            return false;
        }

        return true;
    }

    private static bool TryGetVmmProcessByPid(
        MemProcVmm vmm,
        int pid,
        out VmmProcess process,
        out bool foundPidMethod,
        out string error)
    {
        process = default!;
        foundPidMethod = false;
        error = string.Empty;

        foreach (var method in typeof(MemProcVmm).GetMethods(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!IsPidProcessMethod(method))
            {
                continue;
            }

            foundPidMethod = true;
            try
            {
                var parameterType = method.GetParameters()[0].ParameterType;
                var argument = Convert.ChangeType(pid, parameterType, CultureInfo.InvariantCulture);
                var result = method.Invoke(vmm, new[] { argument });
                if (result is VmmProcess resolved)
                {
                    process = resolved;
                    return true;
                }
            }
            catch (TargetInvocationException ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
        }

        return false;
    }

    private static bool IsPidProcessMethod(MethodInfo method)
    {
        var parameters = method.GetParameters();
        if (parameters.Length != 1)
        {
            return false;
        }

        var parameterType = parameters[0].ParameterType;
        if (parameterType != typeof(int) &&
            parameterType != typeof(uint) &&
            parameterType != typeof(long) &&
            parameterType != typeof(ulong))
        {
            return false;
        }

        return string.Equals(method.Name, "Process", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(method.Name, "ProcessFromPid", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(method.Name, "ProcessFromPID", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(method.Name, "PidGetProcess", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(method.Name, "ProcessGet", StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveProcessName(string? contextProcessName)
    {
        if (!string.IsNullOrWhiteSpace(contextProcessName))
        {
            return Path.GetFileName(contextProcessName.Trim());
        }

        var envProcessName = Environment.GetEnvironmentVariable(_options.ProcessEnvironmentVariable);
        return string.IsNullOrWhiteSpace(envProcessName)
            ? _options.DefaultProcessName
            : envProcessName.Trim();
    }

    private string ResolveModuleName()
    {
        var envModuleName = Environment.GetEnvironmentVariable(_options.ModuleEnvironmentVariable);
        return string.IsNullOrWhiteSpace(envModuleName)
            ? _options.DefaultModuleName
            : envModuleName.Trim();
    }

    private string ResolveVmmDeviceName(string? contextVmmDeviceName)
    {
        if (!string.IsNullOrWhiteSpace(contextVmmDeviceName))
        {
            return contextVmmDeviceName.Trim();
        }

        var envDeviceName = Environment.GetEnvironmentVariable(_options.VmmDeviceEnvironmentVariable);
        return string.IsNullOrWhiteSpace(envDeviceName)
            ? _options.DefaultVmmDeviceName
            : envDeviceName.Trim();
    }

    private string ResolveVmmRemote()
    {
        var remote = Environment.GetEnvironmentVariable(_options.VmmRemoteEnvironmentVariable);
        return string.IsNullOrWhiteSpace(remote) ? string.Empty : remote.Trim();
    }

    private static bool HasMultipleLocalProcesses(string targetName)
    {
        try
        {
            return Process.GetProcesses().Count(process => MatchesTargetName(process, targetName)) > 1;
        }
        catch
        {
            return false;
        }
    }

    private static bool MatchesTargetName(Process process, string targetName)
    {
        var expectedFileName = Path.GetFileName(targetName);
        var expectedBaseName = Path.GetFileNameWithoutExtension(targetName);
        var processName = GetSafeProcessName(process);

        return EqualsName(processName, targetName) ||
               EqualsName(processName, expectedFileName) ||
               EqualsName(processName, expectedBaseName);
    }

    private static string GetSafeProcessName(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool EqualsName(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left) &&
               !string.IsNullOrWhiteSpace(right) &&
               string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static int SafeGetProcessPid(VmmProcess process)
    {
        try
        {
            return Convert.ToInt32(process.PID, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0;
        }
    }

    private SkillXmlCatalog GetSkillXmlCatalog()
    {
        var xmlPath = ResolveSkillXmlPath(out var resolveError);
        if (string.IsNullOrWhiteSpace(xmlPath) || !string.IsNullOrWhiteSpace(resolveError))
        {
            return new SkillXmlCatalog(xmlPath, DateTimeOffset.MinValue, 0, new Dictionary<uint, SkillXmlStaticDetail>(), resolveError);
        }

        var fileInfo = new FileInfo(xmlPath);
        lock (_xmlSync)
        {
            if (_xmlCatalog is not null &&
                string.Equals(_xmlCatalog.Path, xmlPath, StringComparison.OrdinalIgnoreCase) &&
                _xmlCatalog.LastWriteTime == fileInfo.LastWriteTimeUtc &&
                _xmlCatalog.Length == fileInfo.Length)
            {
                return _xmlCatalog;
            }

            var details = LoadSkillXmlStaticDetails(xmlPath, out var loadError);
            _xmlCatalog = new SkillXmlCatalog(xmlPath, fileInfo.LastWriteTimeUtc, fileInfo.Length, details, loadError);
            _logger.Info("skills.xml.loaded", new Dictionary<string, object?>
            {
                ["path"] = xmlPath,
                ["rows"] = details.Count,
                ["error"] = loadError
            });

            return _xmlCatalog;
        }
    }

    private NpcXmlCatalog GetNpcXmlCatalog()
    {
        var npcXmlPath = ResolveNpcStaticXmlPath(out var npcResolveError);
        var tribeXmlPath = ResolveNpcTribeXmlPath(out var tribeResolveError);
        if (string.IsNullOrWhiteSpace(npcXmlPath) || !string.IsNullOrWhiteSpace(npcResolveError))
        {
            return new NpcXmlCatalog(
                npcXmlPath,
                DateTimeOffset.MinValue,
                0,
                new Dictionary<uint, NpcStaticDetail>(),
                npcResolveError);
        }

        var npcInfo = new FileInfo(npcXmlPath);
        var tribeInfo = string.IsNullOrWhiteSpace(tribeXmlPath) || !File.Exists(tribeXmlPath)
            ? null
            : new FileInfo(tribeXmlPath);
        var catalogKey = npcXmlPath + "|" + tribeXmlPath;
        var length = npcInfo.Length + (tribeInfo?.Length ?? 0);
        var npcLastWrite = new DateTimeOffset(npcInfo.LastWriteTimeUtc, TimeSpan.Zero);
        var tribeLastWrite = tribeInfo is null
            ? DateTimeOffset.MinValue
            : new DateTimeOffset(tribeInfo.LastWriteTimeUtc, TimeSpan.Zero);
        var lastWrite = npcLastWrite > tribeLastWrite ? npcLastWrite : tribeLastWrite;

        lock (_xmlSync)
        {
            if (_npcXmlCatalog is not null &&
                string.Equals(_npcXmlCatalog.Path, catalogKey, StringComparison.OrdinalIgnoreCase) &&
                _npcXmlCatalog.LastWriteTime == lastWrite &&
                _npcXmlCatalog.Length == length)
            {
                return _npcXmlCatalog;
            }

            var tribeRelations = LoadNpcTribeRelations(tribeXmlPath, out var tribeLoadError);
            var details = LoadNpcStaticDetails(npcXmlPath, tribeRelations, out var npcLoadError);
            var error = !string.IsNullOrWhiteSpace(npcLoadError)
                ? npcLoadError
                : !string.IsNullOrWhiteSpace(tribeResolveError)
                    ? tribeResolveError
                    : tribeLoadError;

            _npcXmlCatalog = new NpcXmlCatalog(catalogKey, lastWrite, length, details, error);
            _logger.Info("npcs.xml.loaded", new Dictionary<string, object?>
            {
                ["npcPath"] = npcXmlPath,
                ["tribePath"] = tribeXmlPath,
                ["npcRows"] = details.Count,
                ["tribeRows"] = tribeRelations.Count,
                ["error"] = error
            });

            return _npcXmlCatalog;
        }
    }

    private string ResolveSkillXmlPath(out string error)
    {
        error = string.Empty;
        var explicitPath = Environment.GetEnvironmentVariable(_options.SkillXmlEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(explicitPath))
        {
            explicitPath = Environment.GetEnvironmentVariable(_options.SkillXmlLegacyEnvironmentVariable);
        }

        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var expanded = Environment.ExpandEnvironmentVariables(explicitPath.Trim().Trim('"'));
            try
            {
                expanded = Path.GetFullPath(expanded);
            }
            catch
            {
                // Keep the original value in the error.
            }

            if (File.Exists(expanded))
            {
                return expanded;
            }

            error = "client_skills.xml path not found: " + expanded;
            return expanded;
        }

        var baseDirectory = AppContext.BaseDirectory;
        var desktopPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "client_skills.xml");
        var candidates = new[]
        {
            Path.Combine("Source", "client_skills.xml"),
            Path.Combine("Roadhog", "Source", "client_skills.xml"),
            Path.Combine(baseDirectory, "Source", "client_skills.xml"),
            Path.Combine(baseDirectory, "client_skills.xml"),
            Path.Combine(baseDirectory, "TXT", "client_skills.xml"),
            Path.Combine(baseDirectory, "..", "..", "..", "Source", "client_skills.xml"),
            Path.Combine("TXT", "client_skills.xml"),
            "client_skills.xml",
            desktopPath
        };

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                return Path.GetFullPath(candidate);
            }
            catch
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    private static string ResolveNpcStaticXmlPath(out string error)
    {
        return ResolveXmlFilePath(
            "client_npcs.xml",
            new[] { "AION_CLIENT_NPCS_XML", "AION_CLIENT_NPC_XML", "AION_NPC_XML" },
            out error);
    }

    private static string ResolveNpcTribeXmlPath(out string error)
    {
        return ResolveXmlFilePath(
            "npc_tribe_relation.xml",
            new[] { "AION_NPC_TRIBE_RELATION_XML", "AION_NPC_TRIBE_XML" },
            out error);
    }

    private static string ResolveXmlFilePath(
        string fileName,
        IReadOnlyList<string> environmentVariables,
        out string error)
    {
        error = string.Empty;
        foreach (var environmentVariable in environmentVariables)
        {
            var explicitPath = Environment.GetEnvironmentVariable(environmentVariable);
            if (string.IsNullOrWhiteSpace(explicitPath))
            {
                continue;
            }

            var expanded = Environment.ExpandEnvironmentVariables(explicitPath.Trim().Trim('"'));
            try
            {
                expanded = Path.GetFullPath(expanded);
            }
            catch
            {
            }

            if (File.Exists(expanded))
            {
                return expanded;
            }

            error = fileName + " path not found: " + expanded;
            return expanded;
        }

        var baseDirectory = AppContext.BaseDirectory;
        var currentDirectory = Environment.CurrentDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "Source", fileName),
            Path.Combine(baseDirectory, fileName),
            Path.Combine(currentDirectory, "Roadhog", "Source", fileName),
            Path.Combine(currentDirectory, "Tool", "Source", fileName),
            Path.Combine(currentDirectory, "Source", fileName),
            Path.Combine("Roadhog", "Source", fileName),
            Path.Combine("Tool", "Source", fileName),
            Path.Combine("Source", fileName),
            fileName
        };

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                return Path.GetFullPath(candidate);
            }
            catch
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    private static Dictionary<uint, SkillXmlStaticDetail> LoadSkillXmlStaticDetails(string xmlPath, out string error)
    {
        var details = new Dictionary<uint, SkillXmlStaticDetail>();
        error = string.Empty;

        try
        {
            var document = XDocument.Load(xmlPath);
            if (document.Root is null)
            {
                error = "client_skills.xml has no root element";
                return details;
            }

            foreach (var element in document.Root.DescendantsAndSelf())
            {
                if (TryReadSkillXmlStaticDetail(element, out var detail))
                {
                    details[detail.Id] = detail;
                }
            }
        }
        catch (Exception ex)
        {
            error = "failed to load client_skills.xml: " + ex.Message;
            details.Clear();
        }

        return details;
    }

    private static Dictionary<uint, NpcStaticDetail> LoadNpcStaticDetails(
        string xmlPath,
        IReadOnlyDictionary<string, NpcTribeRelation> tribeRelations,
        out string error)
    {
        var details = new Dictionary<uint, NpcStaticDetail>();
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(xmlPath) || !File.Exists(xmlPath))
        {
            return details;
        }

        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                IgnoreComments = true,
                IgnoreWhitespace = true
            };

            using var reader = XmlReader.Create(xmlPath, settings);
            while (!reader.EOF)
            {
                if (reader.NodeType == XmlNodeType.Element &&
                    string.Equals(reader.Name, "npc_client", StringComparison.OrdinalIgnoreCase))
                {
                    var element = (XElement)XNode.ReadFrom(reader);
                    if (TryReadNpcStaticDetail(element, tribeRelations, out var detail))
                    {
                        details[detail.Id] = detail;
                    }
                }
                else if (!reader.Read())
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            error = "failed to load client_npcs.xml: " + ex.Message;
            details.Clear();
        }

        return details;
    }

    private static Dictionary<string, NpcTribeRelation> LoadNpcTribeRelations(
        string xmlPath,
        out string error)
    {
        var relations = new Dictionary<string, NpcTribeRelation>(StringComparer.OrdinalIgnoreCase);
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(xmlPath) || !File.Exists(xmlPath))
        {
            return relations;
        }

        try
        {
            var document = XDocument.Load(xmlPath);
            if (document.Root is null)
            {
                error = "npc_tribe_relation.xml has no root element";
                return relations;
            }

            foreach (var element in document.Root.Elements())
            {
                if (!string.Equals(element.Name.LocalName, "tribe", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var tribe = GetSkillXmlValue(element, "Tribe", "tribe");
                if (string.IsNullOrWhiteSpace(tribe))
                {
                    continue;
                }

                var relation = new NpcTribeRelation
                {
                    Tribe = tribe,
                    BaseTribe = GetSkillXmlValue(element, "base_tribe", "basetribe"),
                    Aggressive = GetSkillXmlValue(element, "aggressive")
                };
                relation.AggressiveToPlayer =
                    ContainsRelationToken(relation.Aggressive, "PC") ||
                    ContainsRelationToken(relation.Aggressive, "PC_Dark");
                relations[tribe] = relation;
            }
        }
        catch (Exception ex)
        {
            error = "failed to load npc_tribe_relation.xml: " + ex.Message;
            relations.Clear();
        }

        return relations;
    }

    private static bool TryReadNpcStaticDetail(
        XElement element,
        IReadOnlyDictionary<string, NpcTribeRelation> tribeRelations,
        out NpcStaticDetail detail)
    {
        detail = new NpcStaticDetail();
        var idText = GetSkillXmlValue(element, "id");
        if (!TryParseSkillXmlUInt(idText, out var id))
        {
            return false;
        }

        detail.Id = id;
        detail.Name = GetSkillXmlValue(element, "name");
        detail.UiType = GetSkillXmlValue(element, "ui_type", "uitype");
        detail.CursorType = GetSkillXmlValue(element, "cursor_type", "cursortype");
        detail.NpcType = GetSkillXmlValue(element, "npc_type", "npctype");
        detail.Tribe = GetSkillXmlValue(element, "tribe");

        var aggressive = GetSkillXmlValue(element, "aggressive");
        detail.HasDirectAggressive = !string.IsNullOrWhiteSpace(aggressive);
        detail.DirectAggressive = IsTruthyNpcXmlValue(aggressive);
        ApplyNpcStaticClassification(tribeRelations, ref detail);
        return true;
    }

    private static void ApplyNpcStaticClassification(
        IReadOnlyDictionary<string, NpcTribeRelation> tribeRelations,
        ref NpcStaticDetail detail)
    {
        if (!string.IsNullOrWhiteSpace(detail.NpcType))
        {
            detail.IsMonsterKnown = true;
            detail.IsMonster = string.Equals(detail.NpcType, "monster", StringComparison.OrdinalIgnoreCase);
        }
        else if (LooksLikeMonsterUi(detail) || IsTribeDerivedFrom(detail.Tribe, "Monster", tribeRelations))
        {
            detail.IsMonsterKnown = true;
            detail.IsMonster = true;
        }

        if (detail.HasDirectAggressive)
        {
            detail.AggressiveKnown = true;
            detail.AggressiveToPlayer = detail.DirectAggressive;
            detail.AggressiveSource = "npc_xml";
            return;
        }

        if (!string.IsNullOrWhiteSpace(detail.Tribe) && IsAggressiveToPlayerTribe(detail.Tribe, tribeRelations))
        {
            detail.AggressiveKnown = true;
            detail.AggressiveToPlayer = true;
            detail.AggressiveSource = "tribe_relation";
            return;
        }

        if (detail.IsMonsterKnown && detail.IsMonster)
        {
            detail.AggressiveKnown = true;
            detail.AggressiveToPlayer = false;
            detail.AggressiveSource = "tribe_relation";
        }
    }

    private static bool LooksLikeMonsterUi(NpcStaticDetail detail)
    {
        var monsterUi =
            string.Equals(detail.UiType, "monster", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(detail.UiType, "monster_raid", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(detail.UiType, "monster_subordinate", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(detail.UiType, "hidden_monster", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(detail.UiType, "monster_notitle", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(detail.UiType, "monster_namedisplay", StringComparison.OrdinalIgnoreCase);
        var attackCursor = string.Equals(detail.CursorType, "attack", StringComparison.OrdinalIgnoreCase);
        return monsterUi && attackCursor;
    }

    private static bool IsTribeDerivedFrom(
        string tribe,
        string expectedBase,
        IReadOnlyDictionary<string, NpcTribeRelation> tribeRelations)
    {
        if (string.IsNullOrWhiteSpace(tribe) || string.IsNullOrWhiteSpace(expectedBase))
        {
            return false;
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = tribe;
        for (var guard = 0; guard < 32 && !string.IsNullOrWhiteSpace(current); guard++)
        {
            if (!visited.Add(current))
            {
                return false;
            }

            if (string.Equals(current, expectedBase, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!tribeRelations.TryGetValue(current, out var relation) ||
                string.IsNullOrWhiteSpace(relation.BaseTribe))
            {
                return false;
            }

            current = relation.BaseTribe;
        }

        return false;
    }

    private static bool IsAggressiveToPlayerTribe(
        string tribe,
        IReadOnlyDictionary<string, NpcTribeRelation> tribeRelations)
    {
        if (string.IsNullOrWhiteSpace(tribe))
        {
            return false;
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = tribe;
        for (var guard = 0; guard < 32 && !string.IsNullOrWhiteSpace(current); guard++)
        {
            if (!visited.Add(current))
            {
                return false;
            }

            if (!tribeRelations.TryGetValue(current, out var relation))
            {
                return false;
            }

            if (relation.AggressiveToPlayer)
            {
                return true;
            }

            current = relation.BaseTribe;
        }

        return false;
    }

    private static bool ContainsRelationToken(string text, string token)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var parts = text.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(part => string.Equals(part.Trim(), token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTruthyNpcXmlValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        value = value.Trim();
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadSkillXmlStaticDetail(XElement element, out SkillXmlStaticDetail detail)
    {
        detail = new SkillXmlStaticDetail();
        var idText = GetSkillXmlValue(element, "skill_id", "skillid", "id");
        if (!TryParseSkillXmlUInt(idText, out var id))
        {
            return false;
        }

        detail.Id = id;
        detail.XmlName = GetSkillXmlValue(element, "name", "skill_name", "skillname");
        detail.ActivationAttribute = GetSkillXmlValue(element, "activation_attribute", "activationattribute");
        detail.TargetSlot = GetSkillXmlValue(element, "target_slot", "targetslot");
        detail.ChainCategoryName = GetSkillXmlValue(element, "chain_category_name", "chaincategoryname");
        detail.PrechainCategoryName = GetSkillXmlValue(element, "prechain_category_name", "prechaincategoryname");
        detail.ChainTime = GetSkillXmlValue(element, "chain_time", "chaintime");
        detail.StatusFx = GetSkillXmlValue(element, "status_fx", "statusfx");
        detail.AuraFx = GetSkillXmlValue(element, "aura_fx", "aurafx");
        detail.CounterSkill = GetSkillXmlValue(element, "counter_skill", "counterskill");
        detail.CostDp = GetSkillXmlValue(element, "cost_dp", "costdp");
        detail.UltraSkill = GetSkillXmlValue(element, "ultra_skill", "ultraskill");
        return true;
    }

    private static bool TryParseSkillXmlUInt(string text, out uint value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                value = Convert.ToUInt32(text[2..], 16);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static string GetSkillXmlValue(XElement element, params string[] names)
    {
        return TryGetSkillXmlValue(element, out var value, names) ? value : string.Empty;
    }

    private static bool TryGetSkillXmlValue(XElement element, out string value, params string[] names)
    {
        value = string.Empty;

        foreach (var attribute in element.Attributes())
        {
            if (MatchesSkillXmlName(attribute.Name.LocalName, names))
            {
                value = CleanSkillXmlValue(attribute.Value);
                return true;
            }
        }

        foreach (var child in element.Elements())
        {
            if (MatchesSkillXmlName(child.Name.LocalName, names))
            {
                value = CleanSkillXmlValue(child.Value);
                return true;
            }
        }

        return false;
    }

    private static bool MatchesSkillXmlName(string candidate, string[] names)
    {
        var normalizedCandidate = NormalizeSkillXmlName(candidate);
        return names.Any(name => string.Equals(normalizedCandidate, NormalizeSkillXmlName(name), StringComparison.Ordinal));
    }

    private static string NormalizeSkillXmlName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if ((c >= 'A' && c <= 'Z') ||
                (c >= 'a' && c <= 'z') ||
                (c >= '0' && c <= '9'))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    private static string CleanSkillXmlValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static int AttachSkillXmlStaticDetails(
        IReadOnlyDictionary<uint, SkillXmlStaticDetail> xmlDetails,
        List<LearnedSkillInfo> skills)
    {
        if (xmlDetails.Count == 0 || skills.Count == 0)
        {
            return 0;
        }

        var count = 0;
        for (var i = 0; i < skills.Count; i++)
        {
            var skill = skills[i];
            if (!xmlDetails.TryGetValue(skill.SkillId, out var detail))
            {
                continue;
            }

            skill.HasXmlStaticDetail = true;
            skill.XmlStaticDetail = detail;
            skills[i] = skill;
            count++;
        }

        return count;
    }

    private static IReadOnlySet<uint>? BuildSkillIdFilter(IReadOnlyCollection<uint>? requestedSkillIds)
    {
        if (requestedSkillIds is null || requestedSkillIds.Count == 0)
        {
            return null;
        }

        return requestedSkillIds
            .Where(id => id != 0)
            .ToHashSet();
    }

    private static bool TryReadHighestLearnedSkills(
        VmmProcess process,
        ulong gameBase,
        IReadOnlySet<uint>? skillIdFilter,
        out List<LearnedSkillInfo> skills,
        out int outerNodeCount,
        out string error)
    {
        skills = new List<LearnedSkillInfo>();
        outerNodeCount = 0;
        error = string.Empty;

        if (!TryReadPointer(process, gameBase + SkillManagerGlobalRva, out var skillManager) || skillManager == 0)
        {
            error = "failed to read SkillManager pointer at Game.dll+0x" + SkillManagerGlobalRva.ToString("X");
            return false;
        }

        if (!TryReadPointer(process, skillManager + LearnedSkillTreeOffset, out var outerHeader) || outerHeader == 0)
        {
            error = "failed to read learned skill tree header at SkillManager+0x" + LearnedSkillTreeOffset.ToString("X");
            return false;
        }

        if (!TryReadPointer(process, outerHeader + NodeLeftOffset, out var outerNode))
        {
            error = "failed to read learned skill tree begin node";
            return false;
        }

        var visited = new HashSet<ulong>();
        for (var guard = 0; outerNode != 0 && outerNode != outerHeader && guard < 65536; guard++)
        {
            if (!visited.Add(outerNode) || IsNilNode(process, outerNode, outerHeader))
            {
                break;
            }

            outerNodeCount++;
            if (!TryReadUInt32(process, outerNode + LearnedSkillOuterSkillIdOffset, out var skillId) ||
                skillId == 0 ||
                (skillIdFilter is not null && !skillIdFilter.Contains(skillId)))
            {
                if (!TryGetNextTreeNode(process, outerHeader, outerNode, out var filteredNext) || filteredNext == outerNode)
                {
                    break;
                }

                outerNode = filteredNext;
                continue;
            }

            if (TryReadHighestLearnedSkillFromOuterNode(process, outerNode, skillId, out var skill))
            {
                skills.Add(skill);
            }

            if (!TryGetNextTreeNode(process, outerHeader, outerNode, out var next) || next == outerNode)
            {
                break;
            }

            outerNode = next;
        }

        skills.Sort((left, right) => left.SkillId.CompareTo(right.SkillId));
        return true;
    }

    private static bool TryReadHighestLearnedSkillFromOuterNode(
        VmmProcess process,
        ulong outerNode,
        uint skillId,
        out LearnedSkillInfo skill)
    {
        skill = new LearnedSkillInfo
        {
            Name = string.Empty,
            DisplayBaseName = string.Empty
        };

        if (!TryReadPointer(process, outerNode + LearnedSkillOuterLevelTreeHeaderOffset, out var innerHeader) || innerHeader == 0)
        {
            return false;
        }

        if (TryReadUInt64(process, outerNode + LearnedSkillOuterLevelTreeSizeOffset, out var levelTreeSize))
        {
            skill.LevelTreeSize = levelTreeSize;
        }

        if (!TryReadPointer(process, innerHeader + NodeRightOffset, out var highestLevelNode) ||
            highestLevelNode == 0 ||
            highestLevelNode == innerHeader ||
            IsNilNode(process, highestLevelNode, innerHeader))
        {
            return false;
        }

        if (!TryReadUInt16(process, highestLevelNode + LearnedSkillInnerLevelOffset, out var level))
        {
            return false;
        }

        if (!TryReadPointer(process, highestLevelNode + LearnedSkillInnerItemListHeaderOffset, out var itemListHeader) ||
            itemListHeader == 0)
        {
            return false;
        }

        if (TryReadUInt64(process, highestLevelNode + LearnedSkillInnerItemListSizeOffset, out var itemListSize))
        {
            skill.ItemListSize = itemListSize;
        }

        if (!TryReadPointer(process, itemListHeader + ListNodePrevOffset, out var lastNode) ||
            lastNode == 0 ||
            lastNode == itemListHeader)
        {
            return false;
        }

        if (!TryReadPointer(process, lastNode + ListNodeValueOffset, out var item) || item == 0)
        {
            return false;
        }

        if (!TryReadUInt32(process, item + SkillItemSkillIdOffset, out var itemSkillId) || itemSkillId != skillId)
        {
            return false;
        }

        skill.SkillId = skillId;
        skill.HighestLevel = level;
        skill.SkillItem = item;

        if (TryReadMsvcWString(process, item + SkillItemNameOffset, out var name))
        {
            skill.Name = name;
        }

        GetSkillDisplayNameParts(skill.Name, out var displayBaseName, out var displayTier);
        skill.DisplayBaseName = displayBaseName;
        skill.DisplayTier = displayTier;

        TryReadUInt32(process, item + SkillItemField0COffset, out skill.Field0C);
        TryReadUInt64(process, item + SkillItemRankValueOffset, out skill.RankValue);
        TryReadUInt32(process, item + SkillItemCooldownDurationOffset, out skill.CooldownDuration);
        TryReadUInt32(process, item + SkillItemCooldownEndTimeOffset, out skill.CooldownEndTime);
        TryReadUInt32(process, item + SkillItemToggleStateOffset, out skill.ToggleState);
        TryReadUInt32(process, item + SkillItemSkillLevelOffset, out skill.SkillLevel);
        TryReadUInt32(process, item + SkillItemStaticFieldD8Offset, out skill.StaticFieldD8);
        TryReadUInt32(process, item + SkillItemRuntimeStateOffset, out skill.RuntimeState);
        TryReadUInt32(process, item + SkillItemSourceFlagsOffset, out skill.SourceFlags);
        return true;
    }

    private static List<LearnedSkillInfo> SelectHighestDisplaySkillPerName(List<LearnedSkillInfo> skills)
    {
        var selected = new Dictionary<string, LearnedSkillInfo>(StringComparer.Ordinal);
        foreach (var skill in skills)
        {
            var key = GetLearnedSkillDisplayGroupKey(skill);
            if (!selected.TryGetValue(key, out var current) ||
                CompareLearnedSkillDisplayLevel(skill, current) > 0)
            {
                selected[key] = skill;
            }
        }

        var result = selected.Values.ToList();
        result.Sort((left, right) => left.SkillId.CompareTo(right.SkillId));
        return result;
    }

    private static List<LearnedSkillInfo> FilterUsefulLearnedSkills(List<LearnedSkillInfo> skills)
    {
        return skills.Where(IsUsefulLearnedSkill).ToList();
    }

    private static bool IsUsefulLearnedSkill(LearnedSkillInfo skill)
    {
        var name = skill.Name ?? string.Empty;
        var baseName = string.IsNullOrWhiteSpace(skill.DisplayBaseName)
            ? name
            : skill.DisplayBaseName;

        if (skill.SkillId >= 50000)
        {
            return false;
        }

        if (IsIgnoredUtilitySkillName(name) ||
            IsIgnoredUtilitySkillName(baseName) ||
            ContainsAny(name, IgnoredSkillNameParts) ||
            ContainsAny(baseName, IgnoredSkillNameParts))
        {
            return false;
        }

        if (skill.HighestLevel == 0 && skill.SkillLevel == 0)
        {
            return false;
        }

        if (skill.HasXmlStaticDetail)
        {
            return IsManualSkillXmlActivation(skill.XmlStaticDetail.ActivationAttribute) ||
                   IsPassiveSkillXmlActivation(skill.XmlStaticDetail.ActivationAttribute) ||
                   IsChainSkill(skill.XmlStaticDetail) ||
                   IsStatusSkill(skill.XmlStaticDetail);
        }

        return skill.ToggleState != 0 ||
               skill.CooldownDuration > 0 ||
               skill.StaticFieldD8 != 0 ||
               skill.RuntimeState != 0 ||
               skill.SourceFlags != 0;
    }

    private static SkillSnapshot ToSkillSnapshot(LearnedSkillInfo skill)
    {
        var activation = skill.HasXmlStaticDetail ? EmptyToNull(skill.XmlStaticDetail.ActivationAttribute) : null;
        var tags = skill.HasXmlStaticDetail ? FormatSkillXmlTags(skill.XmlStaticDetail) : FormatRuntimeSkillTags(skill);
        var targetSlot = skill.HasXmlStaticDetail ? EmptyToNull(skill.XmlStaticDetail.TargetSlot) : null;
        var chainCategory = skill.HasXmlStaticDetail ? EmptyToNull(skill.XmlStaticDetail.ChainCategoryName) : null;
        var prechainCategory = skill.HasXmlStaticDetail ? EmptyToNull(skill.XmlStaticDetail.PrechainCategoryName) : null;
        var chainTime = skill.HasXmlStaticDetail ? EmptyToNull(skill.XmlStaticDetail.ChainTime) : null;
        var counterSkill = skill.HasXmlStaticDetail ? EmptyToNull(skill.XmlStaticDetail.CounterSkill) : null;
        var costDp = skill.HasXmlStaticDetail ? EmptyToNull(skill.XmlStaticDetail.CostDp) : null;

        return new SkillSnapshot(
            skill.SkillId,
            skill.Name,
            skill.HighestLevel,
            (int)skill.SkillLevel,
            EmptyToNull(skill.DisplayBaseName),
            skill.DisplayTier > 0 ? skill.DisplayTier : null,
            skill.ToggleState != 0,
            skill.CooldownDuration,
            skill.CooldownEndTime,
            activation,
            tags,
            targetSlot,
            chainCategory,
            prechainCategory,
            chainTime,
            counterSkill,
            costDp);
    }

    private static string? FormatSkillXmlTags(SkillXmlStaticDetail detail)
    {
        var tags = new List<string>();
        var activation = FormatSkillXmlActivationTag(detail.ActivationAttribute);
        if (!string.IsNullOrWhiteSpace(activation))
        {
            tags.Add(activation);
        }

        if (IsManualSkillXmlActivation(detail.ActivationAttribute))
        {
            tags.Add("manual");
        }

        if (IsPassiveSkillXmlActivation(detail.ActivationAttribute))
        {
            tags.Add("passive");
        }

        if (IsChainSkill(detail))
        {
            tags.Add("chain");
        }

        if (IsStatusSkill(detail))
        {
            tags.Add("status");
        }

        var targetSlot = FormatSkillXmlTargetSlotTag(detail.TargetSlot);
        if (!string.IsNullOrWhiteSpace(targetSlot) &&
            !string.Equals(targetSlot, "default", StringComparison.Ordinal) &&
            !string.Equals(targetSlot, "none", StringComparison.Ordinal) &&
            !string.Equals(targetSlot, "null", StringComparison.Ordinal) &&
            !string.Equals(targetSlot, "false", StringComparison.Ordinal) &&
            !string.Equals(targetSlot, "na", StringComparison.Ordinal) &&
            !string.Equals(targetSlot, "7", StringComparison.Ordinal))
        {
            tags.Add(targetSlot);
        }

        if (HasUsefulSkillXmlValue(detail.CounterSkill))
        {
            tags.Add("counter");
        }

        if (HasUsefulSkillXmlValue(detail.CostDp) || HasUsefulSkillXmlValue(detail.UltraSkill))
        {
            tags.Add("dp");
        }

        return tags.Count == 0 ? null : string.Join(",", tags.Distinct(StringComparer.Ordinal));
    }

    private static string? FormatRuntimeSkillTags(LearnedSkillInfo skill)
    {
        var tags = new List<string>();
        if (skill.ToggleState != 0)
        {
            tags.Add("toggle");
            tags.Add("manual");
        }
        else if (skill.CooldownDuration > 0 || skill.StaticFieldD8 != 0 || skill.RuntimeState != 0 || skill.SourceFlags != 0)
        {
            tags.Add("active");
            tags.Add("manual");
        }

        return tags.Count == 0 ? null : string.Join(",", tags);
    }

    private static bool IsManualSkillXmlActivation(string activation)
    {
        var token = NormalizeSkillXmlName(activation);
        return string.Equals(token, "active", StringComparison.Ordinal) ||
               string.Equals(token, "act", StringComparison.Ordinal) ||
               string.Equals(token, "action", StringComparison.Ordinal) ||
               string.Equals(token, "manual", StringComparison.Ordinal) ||
               string.Equals(token, "toggle", StringComparison.Ordinal) ||
               string.Equals(token, "maintain", StringComparison.Ordinal) ||
               string.Equals(token, "2", StringComparison.Ordinal) ||
               string.Equals(token, "1", StringComparison.Ordinal) ||
               string.Equals(token, "4", StringComparison.Ordinal);
    }

    private static bool IsPassiveSkillXmlActivation(string activation)
    {
        var token = NormalizeSkillXmlName(activation);
        return string.Equals(token, "passive", StringComparison.Ordinal) ||
               string.Equals(token, "provoked", StringComparison.Ordinal) ||
               string.Equals(token, "8", StringComparison.Ordinal) ||
               string.Equals(token, "16", StringComparison.Ordinal);
    }

    private static bool IsChainSkill(SkillXmlStaticDetail detail)
    {
        return HasUsefulSkillXmlValue(detail.ChainCategoryName) ||
               HasUsefulSkillXmlValue(detail.PrechainCategoryName) ||
               HasUsefulSkillXmlValue(detail.ChainTime);
    }

    private static bool IsStatusSkill(SkillXmlStaticDetail detail)
    {
        var targetSlot = NormalizeSkillXmlName(detail.TargetSlot);
        return string.Equals(targetSlot, "buff", StringComparison.Ordinal) ||
               string.Equals(targetSlot, "debuff", StringComparison.Ordinal) ||
               string.Equals(targetSlot, "chant", StringComparison.Ordinal) ||
               string.Equals(targetSlot, "boost", StringComparison.Ordinal) ||
               string.Equals(targetSlot, "0", StringComparison.Ordinal) ||
               string.Equals(targetSlot, "1", StringComparison.Ordinal) ||
               string.Equals(targetSlot, "2", StringComparison.Ordinal) ||
               string.Equals(targetSlot, "5", StringComparison.Ordinal) ||
               HasUsefulSkillXmlValue(detail.StatusFx) ||
               HasUsefulSkillXmlValue(detail.AuraFx);
    }

    private static bool HasUsefulSkillXmlValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (TryParseSkillXmlUInt(trimmed, out var uintValue) && uintValue == 0)
        {
            return false;
        }

        if (IsZeroSkillXmlNumber(trimmed))
        {
            return false;
        }

        var token = NormalizeSkillXmlName(trimmed);
        return !string.Equals(token, "none", StringComparison.Ordinal) &&
               !string.Equals(token, "null", StringComparison.Ordinal) &&
               !string.Equals(token, "false", StringComparison.Ordinal) &&
               !string.Equals(token, "na", StringComparison.Ordinal);
    }

    private static bool IsZeroSkillXmlNumber(string value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var invariantNumber) &&
               Math.Abs(invariantNumber) < 0.000001 ||
               double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var currentNumber) &&
               Math.Abs(currentNumber) < 0.000001;
    }

    private static string FormatSkillXmlActivationTag(string activation)
    {
        var token = NormalizeSkillXmlName(activation);
        return token switch
        {
            "1" => "toggle",
            "2" => "active",
            "4" => "maintain",
            "8" => "passive",
            "16" => "provoked",
            _ => token
        };
    }

    private static string FormatSkillXmlTargetSlotTag(string targetSlot)
    {
        var token = NormalizeSkillXmlName(targetSlot);
        return token switch
        {
            "0" => "buff",
            "1" => "debuff",
            "2" => "chant",
            "3" => "special",
            "4" => "special2",
            "5" => "boost",
            "6" => "noshow",
            "7" => "default",
            _ => token
        };
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool IsIgnoredUtilitySkillName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return IgnoredUtilitySkillNames.Any(value => string.Equals(name.Trim(), value, StringComparison.Ordinal));
    }

    private static bool ContainsAny(string text, string[] values)
    {
        return !string.IsNullOrWhiteSpace(text) &&
               values.Any(value => text.IndexOf(value, StringComparison.Ordinal) >= 0);
    }

    private static string GetLearnedSkillDisplayGroupKey(LearnedSkillInfo skill)
    {
        if (skill.DisplayTier > 0 && !string.IsNullOrWhiteSpace(skill.DisplayBaseName))
        {
            return "name:" + skill.DisplayBaseName;
        }

        return "id:" + skill.SkillId.ToString(CultureInfo.InvariantCulture);
    }

    private static int CompareLearnedSkillDisplayLevel(LearnedSkillInfo left, LearnedSkillInfo right)
    {
        if (left.DisplayTier != right.DisplayTier)
        {
            return left.DisplayTier.CompareTo(right.DisplayTier);
        }

        if (left.SkillLevel != right.SkillLevel)
        {
            return left.SkillLevel.CompareTo(right.SkillLevel);
        }

        if (left.HighestLevel != right.HighestLevel)
        {
            return left.HighestLevel.CompareTo(right.HighestLevel);
        }

        return left.SkillId.CompareTo(right.SkillId);
    }

    private static void GetSkillDisplayNameParts(string name, out string baseName, out int tier)
    {
        name = (name ?? string.Empty).Trim();
        baseName = name;
        tier = 0;

        if (name.Length == 0)
        {
            return;
        }

        var end = name.Length - 1;
        while (end >= 0 && char.IsWhiteSpace(name[end]))
        {
            end--;
        }

        var romanStart = end;
        while (romanStart >= 0 && IsRomanNumeralChar(name[romanStart]))
        {
            romanStart--;
        }

        var suffixStart = romanStart + 1;
        if (suffixStart > end)
        {
            return;
        }

        var roman = name.Substring(suffixStart, end - suffixStart + 1).ToUpperInvariant();
        if (!TryParseRomanNumeral(roman, out var parsedTier) || parsedTier <= 0 || parsedTier > 50)
        {
            return;
        }

        var before = romanStart >= 0 ? name[romanStart] : '\0';
        if (roman.Length == 1 && IsAsciiLetterOrDigit(before))
        {
            return;
        }

        var parsedBaseName = name.Substring(0, suffixStart).TrimEnd(' ', '\t', '　', '-', '－');
        if (string.IsNullOrWhiteSpace(parsedBaseName))
        {
            return;
        }

        baseName = parsedBaseName;
        tier = parsedTier;
    }

    private static bool IsRomanNumeralChar(char value)
    {
        value = char.ToUpperInvariant(value);
        return value is 'I' or 'V' or 'X' or 'L' or 'C' or 'D' or 'M';
    }

    private static bool TryParseRomanNumeral(string value, out int result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        value = value.Trim().ToUpperInvariant();
        var previous = 0;
        for (var i = value.Length - 1; i >= 0; i--)
        {
            var current = GetRomanNumeralValue(value[i]);
            if (current == 0)
            {
                result = 0;
                return false;
            }

            if (current < previous)
            {
                result -= current;
            }
            else
            {
                result += current;
                previous = current;
            }
        }

        return result > 0 && string.Equals(ToRomanNumeral(result), value, StringComparison.Ordinal);
    }

    private static int GetRomanNumeralValue(char value)
    {
        return char.ToUpperInvariant(value) switch
        {
            'I' => 1,
            'V' => 5,
            'X' => 10,
            'L' => 50,
            'C' => 100,
            'D' => 500,
            'M' => 1000,
            _ => 0
        };
    }

    private static string ToRomanNumeral(int value)
    {
        if (value <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        AppendRomanNumeral(builder, ref value, 1000, "M");
        AppendRomanNumeral(builder, ref value, 900, "CM");
        AppendRomanNumeral(builder, ref value, 500, "D");
        AppendRomanNumeral(builder, ref value, 400, "CD");
        AppendRomanNumeral(builder, ref value, 100, "C");
        AppendRomanNumeral(builder, ref value, 90, "XC");
        AppendRomanNumeral(builder, ref value, 50, "L");
        AppendRomanNumeral(builder, ref value, 40, "XL");
        AppendRomanNumeral(builder, ref value, 10, "X");
        AppendRomanNumeral(builder, ref value, 9, "IX");
        AppendRomanNumeral(builder, ref value, 5, "V");
        AppendRomanNumeral(builder, ref value, 4, "IV");
        AppendRomanNumeral(builder, ref value, 1, "I");
        return builder.ToString();
    }

    private static void AppendRomanNumeral(StringBuilder builder, ref int value, int number, string text)
    {
        while (value >= number)
        {
            builder.Append(text);
            value -= number;
        }
    }

    private static bool IsAsciiLetterOrDigit(char value)
    {
        return value is >= 'A' and <= 'Z' ||
               value is >= 'a' and <= 'z' ||
               value is >= '0' and <= '9';
    }

    private static bool TryReadLockedTarget(
        VmmProcess process,
        ulong gameBase,
        out LockedTargetInfo info,
        out string error)
    {
        info = new LockedTargetInfo();
        error = string.Empty;

        if (!TryReadUInt16(process, gameBase + LocalEntityIdRva + 2, out info.TargetEntityId))
        {
            error = "failed to read current target entity id at Game.dll+0x" + (LocalEntityIdRva + 2).ToString("X");
            return false;
        }

        if (info.TargetEntityId == 0)
        {
            return true;
        }

        if (TryFindServerObjectByEntityId(process, gameBase, info.TargetEntityId, out var serverObjectId, out _))
        {
            info.ServerObjectId = serverObjectId;
        }

        if (!TryReadPointer(process, gameBase + EntitySystemPointerRva, out var entitySystem))
        {
            error = "failed to read EntitySystem pointer at Game.dll+0x" + EntitySystemPointerRva.ToString("X");
            return false;
        }

        if (!TryReadPointer(process, entitySystem + EntityTreeOffset, out var entityTreeHeader))
        {
            error = "failed to read EntitySystem tree header at EntitySystem+0x" + EntityTreeOffset.ToString("X");
            return false;
        }

        if (!TryFindEntityById(process, entityTreeHeader, info.TargetEntityId, out info.Entity))
        {
            error = "target entity id " + info.TargetEntityId + " was not found in EntitySystem tree";
            return false;
        }

        TryReadUInt16(process, info.Entity + EntityTypeOffset, out info.EntityType);

        if (TryReadEntityPosition(process, info.Entity, out var x, out var y, out var z))
        {
            info.Position = new Vector3Snapshot(x, y, z);
        }

        if (TryReadUInt16(process, gameBase + LocalEntityIdRva, out var localEntityId) &&
            TryFindEntityById(process, entityTreeHeader, localEntityId, out var localEntity))
        {
            if (TryReadEntityPosition(process, localEntity, out var localX, out var localY, out var localZ) &&
                info.Position is { } targetPosition)
            {
                var dx = targetPosition.X - localX;
                var dy = targetPosition.Y - localY;
                var dz = targetPosition.Z - localZ;
                info.DistanceToLocalPlayer = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }

            if (TryResolveActorFromEntity(process, localEntity, 0, out var localActor))
            {
                info.LocalServerObjectId = localActor.ServerObjectId;
            }
        }

        if (TryResolveActorFromEntity(process, info.Entity, info.ServerObjectId, out var actor))
        {
            info.Actor = actor;
        }

        return true;
    }

    private static bool TryReadLocalPlayer(
        VmmProcess process,
        ulong gameBase,
        out PlayerSnapshot snapshot,
        out string error)
    {
        snapshot = new PlayerSnapshot(0, 0, string.Empty, 0, 0, 0, 0, 0, null, DateTimeOffset.Now);
        error = string.Empty;

        if (!TryReadUInt16(process, gameBase + LocalEntityIdRva, out var localEntityId) || localEntityId == 0)
        {
            error = "failed to read local entity id at Game.dll+0x" + LocalEntityIdRva.ToString("X");
            return false;
        }

        TryReadUInt16(process, gameBase + LocalEntityIdRva + 2, out var targetEntityId);

        if (!TryReadPointer(process, gameBase + EntitySystemPointerRva, out var entitySystem))
        {
            error = "failed to read EntitySystem pointer at Game.dll+0x" + EntitySystemPointerRva.ToString("X");
            return false;
        }

        if (!TryReadPointer(process, entitySystem + EntityTreeOffset, out var entityTreeHeader))
        {
            error = "failed to read EntitySystem tree header at EntitySystem+0x" + EntityTreeOffset.ToString("X");
            return false;
        }

        if (!TryFindEntityById(process, entityTreeHeader, localEntityId, out var localEntity))
        {
            error = "local entity id " + localEntityId + " was not found in EntitySystem tree";
            return false;
        }

        if (!TryReadEntityPosition(process, localEntity, out var x, out var y, out var z))
        {
            error = "failed to read local entity position at CEntity+0x" + EntityWorldPositionOffset.ToString("X");
            return false;
        }

        TryReadUInt32(process, gameBase + LocalCurrentHpRva, out var currentHp);
        TryReadUInt32(process, gameBase + LocalMaxHpRva, out var maxHp);
        TryReadUInt32(process, gameBase + LocalCurrentMpRva, out var currentMp);
        TryReadUInt32(process, gameBase + LocalMaxMpRva, out var maxMp);
        TryReadUInt16(process, gameBase + LocalCurrentDpRva, out var currentDp);
        var characterName = string.Empty;
        double? actorYaw = null;
        if (TryResolveActorFromEntity(process, localEntity, 0, out var actor))
        {
            if (maxHp == 0 && actor.MaxHp > 0)
            {
                currentHp = actor.CurrentHp;
                maxHp = actor.MaxHp;
            }

            characterName = actor.Name;
        }

        if (TryReadSingle(process, localEntity + EntityWorldAnglesOffset + 8, out var rawActorYaw))
        {
            actorYaw = NormalizeSignedDegrees(rawActorYaw);
        }

        double? cameraYaw = null;
        double? cameraPitch = null;
        if (TryReadCameraAngles(process, gameBase, out var rawCameraPitch, out _, out var rawCameraYaw))
        {
            cameraPitch = GetCameraPitchDegrees(rawCameraPitch);
            cameraYaw = GetCameraYawDegrees(rawCameraYaw);
        }

        snapshot = new PlayerSnapshot(
            localEntityId,
            targetEntityId,
            characterName,
            currentHp,
            maxHp,
            currentMp,
            maxMp,
            currentDp,
            new Vector3Snapshot(x, y, z),
            DateTimeOffset.Now,
            cameraYaw,
            cameraPitch,
            actorYaw);
        return true;
    }

    private static bool TryReadLocalPlayerAbnormalStatuses(
        VmmProcess process,
        ulong gameBase,
        out PlayerAbnormalStatusSnapshot snapshot,
        out string error)
    {
        snapshot = PlayerAbnormalStatusSnapshot.Empty();
        error = string.Empty;

        if (!TryReadUInt16(process, gameBase + LocalEntityIdRva, out var localEntityId) || localEntityId == 0)
        {
            error = "failed to read local entity id at Game.dll+0x" + LocalEntityIdRva.ToString("X");
            return false;
        }

        if (!TryReadPointer(process, gameBase + EntitySystemPointerRva, out var entitySystem))
        {
            error = "failed to read EntitySystem pointer at Game.dll+0x" + EntitySystemPointerRva.ToString("X");
            return false;
        }

        if (!TryReadPointer(process, entitySystem + EntityTreeOffset, out var entityTreeHeader))
        {
            error = "failed to read EntitySystem tree header at EntitySystem+0x" + EntityTreeOffset.ToString("X");
            return false;
        }

        if (!TryFindEntityById(process, entityTreeHeader, localEntityId, out var localEntity))
        {
            error = "local entity id " + localEntityId + " was not found in EntitySystem tree";
            return false;
        }

        if (!TryResolveActorFromEntity(process, localEntity, 0, out var actor))
        {
            error = "failed to resolve local actor for entity id " + localEntityId;
            return false;
        }

        TryReadUInt32(process, actor.Actor + ActorAbnormalCategory2CountOffset, out var abnormalCategory2Count);
        if (!TryReadActorAbnormalStatusEntries(process, actor.Actor, out var entries, out error))
        {
            return false;
        }

        snapshot = new PlayerAbnormalStatusSnapshot(
            localEntityId,
            DateTimeOffset.Now,
            abnormalCategory2Count,
            entries);
        return true;
    }

    private static bool TryReadActorAbnormalStatusEntries(
        VmmProcess process,
        ulong actorAddress,
        out IReadOnlyList<AbnormalStatusEntrySnapshot> entries,
        out string error)
    {
        entries = Array.Empty<AbnormalStatusEntrySnapshot>();
        error = string.Empty;

        if (!TryReadPointer(process, actorAddress + ActorAbnormalStatusBeginOffset, out var begin) ||
            !TryReadPointer(process, actorAddress + ActorAbnormalStatusEndOffset, out var end) ||
            begin == 0 ||
            end <= begin)
        {
            return true;
        }

        var size = end - begin;
        if (size < AbnormalStatusEntrySize)
        {
            return true;
        }

        if (size > AbnormalStatusEntrySize * (ulong)MaxActorAbnormalStatusEntries)
        {
            error = "local actor abnormal status list is too large: " + size.ToString(CultureInfo.InvariantCulture);
            return false;
        }

        var result = new List<AbnormalStatusEntrySnapshot>();
        for (var entry = begin; entry <= end - AbnormalStatusEntrySize; entry += AbnormalStatusEntrySize)
        {
            TryReadUInt32(process, entry + 0x00, out var field00);
            if (!TryReadUInt32(process, entry + 0x04, out var abnormalId))
            {
                continue;
            }

            if (!TryReadUInt32(process, entry + 0x08, out var category))
            {
                continue;
            }

            TryReadUInt32(process, entry + 0x0C, out var rawTimeOrSource);
            TryReadUInt16(process, entry + 0x10, out var levelOrStack);
            result.Add(new AbnormalStatusEntrySnapshot(
                field00,
                abnormalId,
                category,
                unchecked((int)rawTimeOrSource),
                levelOrStack,
                entry));
        }

        entries = result;
        return true;
    }

    private static bool TryReadCameraAngles(
        VmmProcess process,
        ulong gameBase,
        out float pitch,
        out float roll,
        out float yaw)
    {
        pitch = 0;
        roll = 0;
        yaw = 0;

        TryReadUInt16(process, gameBase + SpecialCameraModeRva, out var specialCameraMode);
        var useSpecialCamera = specialCameraMode != 0 && !HasCameraRvaOverride();
        var pitchRva = useSpecialCamera ? SpecialCameraPitchRva : GetCameraPitchRva();
        var rollRva = useSpecialCamera ? SpecialCameraRollRva : GetCameraRollRva();
        var yawRva = useSpecialCamera ? SpecialCameraYawRva : GetCameraYawRva();

        return TryReadSingle(process, gameBase + pitchRva, out pitch) &&
               TryReadSingle(process, gameBase + rollRva, out roll) &&
               TryReadSingle(process, gameBase + yawRva, out yaw);
    }

    private static double GetCameraYawDegrees(float rawYaw)
    {
        var unit = (Environment.GetEnvironmentVariable("AION_CAMERA_YAW_UNIT") ?? "deg").Trim().ToLowerInvariant();
        if (unit is "rad" or "radian" or "radians")
        {
            return NormalizeSignedDegrees(RadiansToDegrees(rawYaw));
        }

        if (unit == "auto" && Math.Abs(rawYaw) <= Math.PI * 2.0 + 0.25)
        {
            return NormalizeSignedDegrees(RadiansToDegrees(rawYaw));
        }

        return NormalizeSignedDegrees(rawYaw);
    }

    private static double GetCameraPitchDegrees(float rawPitch)
    {
        var unit = (Environment.GetEnvironmentVariable("AION_CAMERA_PITCH_UNIT") ?? "deg").Trim().ToLowerInvariant();
        double pitch = unit is "rad" or "radian" or "radians"
            ? RadiansToDegrees(rawPitch)
            : unit == "auto" && Math.Abs(rawPitch) <= Math.PI * 2.0 + 0.25
                ? RadiansToDegrees(rawPitch)
                : rawPitch;
        return Math.Max(-65.0, Math.Min(85.0, pitch));
    }

    private static ulong GetCameraPitchRva()
    {
        return ReadRvaFromEnv("AION_CAMERA_PITCH_RVA", CameraPitchRva);
    }

    private static ulong GetCameraRollRva()
    {
        return ReadRvaFromEnv("AION_CAMERA_ROLL_RVA", CameraRollRva);
    }

    private static ulong GetCameraYawRva()
    {
        return ReadRvaFromEnv("AION_CAMERA_YAW_RVA", CameraYawRva);
    }

    private static bool HasCameraRvaOverride()
    {
        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AION_CAMERA_PITCH_RVA")) ||
               !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AION_CAMERA_ROLL_RVA")) ||
               !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AION_CAMERA_YAW_RVA"));
    }

    private static ulong ReadRvaFromEnv(string name, ulong defaultValue)
    {
        var text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text))
        {
            return defaultValue;
        }

        text = text.Trim();
        try
        {
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return Convert.ToUInt64(text[2..], 16);
            }

            return Convert.ToUInt64(text, CultureInfo.InvariantCulture);
        }
        catch
        {
            return defaultValue;
        }
    }

    private static double NormalizeSignedDegrees(double angle)
    {
        angle %= 360.0;
        if (angle > 180.0)
        {
            angle -= 360.0;
        }
        else if (angle <= -180.0)
        {
            angle += 360.0;
        }

        return angle;
    }

    private static double RadiansToDegrees(double radians)
    {
        return radians * 180.0 / Math.PI;
    }

    private static bool IsReasonablePosition(float x, float y, float z)
    {
        return !float.IsNaN(x) &&
               !float.IsNaN(y) &&
               !float.IsNaN(z) &&
               !float.IsInfinity(x) &&
               !float.IsInfinity(y) &&
               !float.IsInfinity(z) &&
               Math.Abs(x) < 10000000.0F &&
               Math.Abs(y) < 10000000.0F &&
               Math.Abs(z) < 10000000.0F;
    }

    private static LockedTargetSnapshot ToLockedTargetSnapshot(LockedTargetInfo info)
    {
        if (info.TargetEntityId == 0)
        {
            return LockedTargetSnapshot.Empty(DateTimeOffset.Now);
        }

        var targetServerObjectId = info.Actor?.TargetServerObjectId ?? 0;
        return new LockedTargetSnapshot(
            info.TargetEntityId,
            info.Actor?.ServerObjectId ?? info.ServerObjectId,
            info.EntityType,
            info.Actor?.ObjectType ?? 0,
            info.Actor?.Name ?? string.Empty,
            info.Actor?.CurrentHp ?? 0,
            info.Actor?.MaxHp ?? 0,
            info.Position,
            info.DistanceToLocalPlayer,
            DateTimeOffset.Now,
            targetServerObjectId,
            info.LocalServerObjectId != 0 && targetServerObjectId == info.LocalServerObjectId);
    }

    private static bool TryReadWorldObjects(
        VmmProcess process,
        ulong gameBase,
        IReadOnlyDictionary<uint, NpcStaticDetail> npcStaticDetails,
        out IReadOnlyList<WorldObjectSnapshot> objects,
        out WorldObjectReadCounters counters,
        out string error)
    {
        var result = new List<WorldObjectSnapshot>();
        objects = result;
        counters = new WorldObjectReadCounters();
        error = string.Empty;

        if (!TryReadPointer(process, gameBase + EntitySystemPointerRva, out var entitySystem))
        {
            error = "failed to read EntitySystem pointer at Game.dll+0x" + EntitySystemPointerRva.ToString("X");
            return false;
        }

        if (!TryReadPointer(process, entitySystem + EntityTreeOffset, out var entityTreeHeader))
        {
            error = "failed to read EntitySystem tree header at EntitySystem+0x" + EntityTreeOffset.ToString("X");
            return false;
        }

        if (!TryReadUInt16(process, gameBase + LocalEntityIdRva, out var localEntityId) || localEntityId == 0)
        {
            error = "failed to read local entity id at Game.dll+0x" + LocalEntityIdRva.ToString("X");
            return false;
        }

        TryReadUInt16(process, gameBase + LocalEntityIdRva + 2, out var targetEntityId);

        if (!TryFindEntityById(process, entityTreeHeader, localEntityId, out var localEntity) ||
            !TryReadEntityPosition(process, localEntity, out var localX, out var localY, out var localZ))
        {
            error = "failed to read local entity position";
            return false;
        }

        uint localServerObjectId = 0;
        if (TryResolveActorFromEntity(process, localEntity, 0, out var localActor))
        {
            localServerObjectId = localActor.ServerObjectId;
        }

        if (!TryReadPointer(process, gameBase + ServerObjectTreeRva, out var serverTreeHeader) || serverTreeHeader == 0)
        {
            error = "failed to read ServerObject tree header at Game.dll+0x" + ServerObjectTreeRva.ToString("X");
            return false;
        }

        if (!TryReadPointer(process, serverTreeHeader + NodeLeftOffset, out var node))
        {
            error = "failed to read ServerObject tree begin node";
            return false;
        }

        for (var guard = 0; node != 0 && node != serverTreeHeader && guard < 100000; guard++)
        {
            if (IsNilNode(process, node, serverTreeHeader))
            {
                break;
            }

            counters.ScannedServerObjects++;

            if (TryReadUInt32(process, node + ServerNodeServerObjectIdOffset, out var serverObjectId) &&
                TryReadUInt16(process, node + ServerNodeEntityIdOffset, out var entityId) &&
                entityId != 0 &&
                entityId != localEntityId &&
                TryFindEntityById(process, entityTreeHeader, entityId, out var entity) &&
                entity != 0)
            {
                counters.ResolvedEntities++;

                if (TryReadUInt16(process, entity + EntityTypeOffset, out var entityType) &&
                    entityType == EntityTypeNpc)
                {
                    counters.NpcLikeEntities++;

                    if (TryReadEntityPosition(process, entity, out var x, out var y, out var z) &&
                        IsReasonablePosition(x, y, z) &&
                        TryResolveActorFromEntity(process, entity, serverObjectId, out var actor) &&
                        npcStaticDetails.TryGetValue(actor.NpcTemplateId, out var npcStaticDetail) &&
                        npcStaticDetail.IsMonsterKnown &&
                        npcStaticDetail.IsMonster)
                    {
                        var dx = x - localX;
                        var dy = y - localY;
                        var dz = z - localZ;
                        var distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                        result.Add(new WorldObjectSnapshot(
                            entityId,
                            serverObjectId,
                            string.IsNullOrWhiteSpace(actor.Name) ? npcStaticDetail.Name : actor.Name,
                            "monster",
                            new Vector3Snapshot(x, y, z),
                            distance,
                            actor.CurrentHp,
                            actor.MaxHp,
                            actor.TargetServerObjectId,
                            localServerObjectId != 0 && actor.TargetServerObjectId == localServerObjectId,
                            npcStaticDetail.AggressiveKnown,
                            npcStaticDetail.AggressiveToPlayer,
                            npcStaticDetail.AggressiveSource));
                    }
                }
            }

            if (!TryGetNextTreeNode(process, serverTreeHeader, node, out var next) || next == node)
            {
                break;
            }

            node = next;
        }

        result.Sort(static (left, right) =>
        {
            var leftDistance = left.DistanceToLocalPlayer ?? double.MaxValue;
            var rightDistance = right.DistanceToLocalPlayer ?? double.MaxValue;
            return leftDistance.CompareTo(rightDistance);
        });

        return true;
    }

    private static bool TryReadLootCorpses(
        VmmProcess process,
        ulong gameBase,
        out IReadOnlyList<LootCorpseSnapshot> corpses,
        out WorldObjectReadCounters counters,
        out string error)
    {
        var result = new List<LootCorpseSnapshot>();
        corpses = result;
        counters = new WorldObjectReadCounters();
        error = string.Empty;

        if (!TryReadPointer(process, gameBase + EntitySystemPointerRva, out var entitySystem))
        {
            error = "failed to read EntitySystem pointer at Game.dll+0x" + EntitySystemPointerRva.ToString("X");
            return false;
        }

        if (!TryReadPointer(process, entitySystem + EntityTreeOffset, out var entityTreeHeader))
        {
            error = "failed to read EntitySystem tree header at EntitySystem+0x" + EntityTreeOffset.ToString("X");
            return false;
        }

        if (!TryReadUInt16(process, gameBase + LocalEntityIdRva, out var localEntityId) || localEntityId == 0)
        {
            error = "failed to read local entity id at Game.dll+0x" + LocalEntityIdRva.ToString("X");
            return false;
        }

        if (!TryFindEntityById(process, entityTreeHeader, localEntityId, out var localEntity) ||
            !TryReadEntityPosition(process, localEntity, out var localX, out var localY, out var localZ))
        {
            error = "failed to read local entity position";
            return false;
        }

        if (!TryReadPointer(process, gameBase + ServerObjectTreeRva, out var serverTreeHeader) || serverTreeHeader == 0)
        {
            error = "failed to read ServerObject tree header at Game.dll+0x" + ServerObjectTreeRva.ToString("X");
            return false;
        }

        if (!TryReadPointer(process, serverTreeHeader + NodeLeftOffset, out var node))
        {
            error = "failed to read ServerObject tree begin node";
            return false;
        }

        for (var guard = 0; node != 0 && node != serverTreeHeader && guard < 100000; guard++)
        {
            if (IsNilNode(process, node, serverTreeHeader))
            {
                break;
            }

            counters.ScannedServerObjects++;

            if (TryReadUInt32(process, node + ServerNodeServerObjectIdOffset, out var serverObjectId) &&
                TryReadUInt16(process, node + ServerNodeEntityIdOffset, out var entityId) &&
                entityId != 0 &&
                entityId != localEntityId &&
                TryFindEntityById(process, entityTreeHeader, entityId, out var entity) &&
                entity != 0)
            {
                counters.ResolvedEntities++;

                if (TryReadUInt16(process, entity + EntityTypeOffset, out var entityType) &&
                    entityType == EntityTypeNpc)
                {
                    counters.NpcLikeEntities++;

                    if (TryReadEntityPosition(process, entity, out var x, out var y, out var z) &&
                        IsReasonablePosition(x, y, z) &&
                        TryResolveActorFromEntity(process, entity, serverObjectId, out var actor))
                    {
                        TryReadUInt32(process, actor.Actor + ActorLootableFlagOffset, out var lootableRaw);
                        TryReadUInt32(process, actor.Actor + ActorInteractionStateOffset, out var interactionState);

                        var deadByHp = actor.MaxHp > 0 && (actor.CurrentHp == 0 || actor.HpPercent == 0);
                        if (deadByHp || lootableRaw != 0)
                        {
                            var dx = x - localX;
                            var dy = y - localY;
                            var dz = z - localZ;
                            var distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                            result.Add(new LootCorpseSnapshot(
                                entityId,
                                actor.ServerObjectId != 0 ? actor.ServerObjectId : serverObjectId,
                                entityType,
                                actor.ObjectType,
                                actor.NpcTemplateId,
                                actor.Level,
                                actor.Name,
                                new Vector3Snapshot(x, y, z),
                                distance,
                                actor.CurrentHp,
                                actor.MaxHp,
                                actor.HpPercent,
                                lootableRaw,
                                interactionState,
                                DateTimeOffset.Now));
                        }
                    }
                }
            }

            if (!TryGetNextTreeNode(process, serverTreeHeader, node, out var next) || next == node)
            {
                break;
            }

            node = next;
        }

        result.Sort(static (left, right) =>
        {
            if (left.IsLootable != right.IsLootable)
            {
                return left.IsLootable ? -1 : 1;
            }

            var leftDistance = left.DistanceToLocalPlayer ?? double.MaxValue;
            var rightDistance = right.DistanceToLocalPlayer ?? double.MaxValue;
            return leftDistance.CompareTo(rightDistance);
        });

        return true;
    }

    private static bool TryFindEntityById(VmmProcess process, ulong header, ushort entityId, out ulong entity)
    {
        entity = 0;
        if (header == 0 || entityId == 0)
        {
            return false;
        }

        if (!TryReadPointer(process, header + NodeParentOffset, out var node))
        {
            return false;
        }

        for (var guard = 0; node != 0 && node != header && guard < 65536; guard++)
        {
            if (IsNilNode(process, node, header))
            {
                return false;
            }

            if (!TryReadUInt16(process, node + NodeIdOffset, out var nodeId))
            {
                return false;
            }

            if (entityId < nodeId)
            {
                if (!TryReadPointer(process, node + NodeLeftOffset, out node))
                {
                    return false;
                }
            }
            else if (entityId > nodeId)
            {
                if (!TryReadPointer(process, node + NodeRightOffset, out node))
                {
                    return false;
                }
            }
            else
            {
                return TryReadPointer(process, node + NodeEntityOffset, out entity);
            }
        }

        return false;
    }

    private static bool TryReadEntityPosition(VmmProcess process, ulong entity, out float x, out float y, out float z)
    {
        x = 0;
        y = 0;
        z = 0;

        if (!TryReadUInt32(process, entity + EntityPositionFlagsOffset, out var flags))
        {
            return false;
        }

        var positionOffset = (flags & EntityUseAlternatePositionFlag) != 0
            ? EntityLocalPositionOffset
            : EntityWorldPositionOffset;

        return TryReadSingle(process, entity + positionOffset, out x) &&
               TryReadSingle(process, entity + positionOffset + 4, out y) &&
               TryReadSingle(process, entity + positionOffset + 8, out z);
    }

    private static bool TryResolveActorFromEntity(
        VmmProcess process,
        ulong entity,
        uint expectedServerObjectId,
        out ActorInfo actor)
    {
        actor = new ActorInfo();

        if (TryResolveProxyManagerFromEntityVfunc(process, entity, out var proxyManager, out var proxyOffset) &&
            TryFindActorCandidateInPointerRegion(
                process,
                proxyManager,
                0x400,
                entity,
                expectedServerObjectId,
                "proxyManager(vfunc_0xB8, entity+0x" + proxyOffset.ToString("X") + ")",
                out actor))
        {
            return true;
        }

        if (TryFindActorCandidateInPointerRegion(
            process,
            entity,
            0x800,
            entity,
            expectedServerObjectId,
            "CEntity direct scan",
            out actor))
        {
            return true;
        }

        for (ulong offset = 0; offset < 0x800; offset += 8)
        {
            if (!TryReadPointer(process, entity + offset, out var pointer))
            {
                continue;
            }

            if (TryFindActorCandidateInPointerRegion(
                process,
                pointer,
                0x300,
                entity,
                expectedServerObjectId,
                "CEntity+0x" + offset.ToString("X") + " nested scan",
                out actor))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveProxyManagerFromEntityVfunc(
        VmmProcess process,
        ulong entity,
        out ulong proxyManager,
        out ulong proxyOffset)
    {
        proxyManager = 0;
        proxyOffset = 0;

        if (!TryReadPointer(process, entity, out var vtable) ||
            !TryReadPointer(process, vtable + EntityProxyManagerVfuncOffset, out var function) ||
            !TryReadBytes(process, function, 16, out var code))
        {
            return false;
        }

        if (code.Length >= 7 &&
            code[0] == 0x48 &&
            code[1] == 0x8B &&
            code[2] == 0x81)
        {
            proxyOffset = BitConverter.ToUInt32(code, 3);
        }
        else if (code.Length >= 4 &&
                 code[0] == 0x48 &&
                 code[1] == 0x8B &&
                 code[2] == 0x41)
        {
            proxyOffset = code[3];
        }
        else
        {
            return false;
        }

        return TryReadPointer(process, entity + proxyOffset, out proxyManager);
    }

    private static bool TryFindActorCandidateInPointerRegion(
        VmmProcess process,
        ulong region,
        ulong regionSize,
        ulong expectedEntity,
        uint expectedServerObjectId,
        string source,
        out ActorInfo actor)
    {
        actor = new ActorInfo();
        var bestScore = -1;

        if (!IsLikelyUserPointer(region))
        {
            return false;
        }

        for (ulong offset = 0; offset < regionSize; offset += 8)
        {
            if (TryReadPointer(process, region + offset, out var candidate) &&
                TryReadActorInfo(
                    process,
                    candidate,
                    expectedEntity,
                    expectedServerObjectId,
                    source + "+0x" + offset.ToString("X"),
                    out var candidateInfo,
                    out var score) &&
                score > bestScore)
            {
                bestScore = score;
                actor = candidateInfo;
            }
        }

        return bestScore >= 60;
    }

    private static bool TryReadActorInfo(
        VmmProcess process,
        ulong actorAddress,
        ulong expectedEntity,
        uint expectedServerObjectId,
        string source,
        out ActorInfo actor,
        out int score)
    {
        actor = new ActorInfo();
        score = 0;

        if (!IsLikelyUserPointer(actorAddress))
        {
            return false;
        }

        if (!TryReadPointer(process, actorAddress + ActorEntityOffset, out var actorEntity) ||
            !TryReadUInt32(process, actorAddress + ActorObjectTypeOffset, out var objectType) ||
            !TryReadUInt32(process, actorAddress + ActorServerObjectIdOffset, out var serverObjectId))
        {
            return false;
        }

        if (actorEntity != expectedEntity)
        {
            return false;
        }

        score += 50;

        if (objectType is 0 or > 32)
        {
            return false;
        }

        score += 10;

        if (expectedServerObjectId != 0 && serverObjectId == expectedServerObjectId)
        {
            score += 40;
        }
        else if (serverObjectId != 0)
        {
            score += 10;
        }

        actor.Actor = actorAddress;
        actor.Entity = actorEntity;
        actor.ObjectType = objectType;
        actor.ServerObjectId = serverObjectId;
        actor.ResolveSource = source;

        TryReadUInt32(process, actorAddress + ActorNpcTemplateIdOffset, out actor.NpcTemplateId);
        TryReadUInt16(process, actorAddress + ActorLevelOffset, out actor.Level);
        TryReadByte(process, actorAddress + ActorHpPercentOffset, out actor.HpPercent);
        TryReadUInt32(process, actorAddress + ActorTargetServerObjectIdOffset, out actor.TargetServerObjectId);
        TryReadUInt32(process, actorAddress + ActorMaxHpOffset, out actor.MaxHp);
        TryReadUInt32(process, actorAddress + ActorCurrentHpOffset, out actor.CurrentHp);

        if (TryReadUtf16String(process, actorAddress + ActorNameOffset, 64, out var name))
        {
            actor.Name = name;
            if (!string.IsNullOrWhiteSpace(name))
            {
                score += 10;
            }
        }

        return true;
    }

    private static bool TryFindServerObjectByEntityId(
        VmmProcess process,
        ulong gameBase,
        ushort entityId,
        out uint serverObjectId,
        out ulong serverTreeHeader)
    {
        serverObjectId = 0;
        serverTreeHeader = 0;

        if (entityId == 0 ||
            !TryReadPointer(process, gameBase + ServerObjectTreeRva, out serverTreeHeader) ||
            serverTreeHeader == 0)
        {
            return false;
        }

        if (!TryReadPointer(process, serverTreeHeader + NodeLeftOffset, out var node))
        {
            return false;
        }

        for (var guard = 0; node != 0 && node != serverTreeHeader && guard < 100000; guard++)
        {
            if (IsNilNode(process, node, serverTreeHeader))
            {
                return false;
            }

            if (!TryReadUInt16(process, node + ServerNodeEntityIdOffset, out var nodeEntityId))
            {
                return false;
            }

            if (nodeEntityId == entityId)
            {
                return TryReadUInt32(process, node + ServerNodeServerObjectIdOffset, out serverObjectId);
            }

            if (!TryGetNextTreeNode(process, serverTreeHeader, node, out var next) || next == node)
            {
                return false;
            }

            node = next;
        }

        return false;
    }

    private static bool TryGetNextTreeNode(VmmProcess process, ulong header, ulong node, out ulong next)
    {
        next = 0;
        if (!TryReadPointer(process, node + NodeRightOffset, out var right))
        {
            return false;
        }

        if (!IsNilNode(process, right, header))
        {
            var current = right;
            for (var guard = 0; guard < 1024; guard++)
            {
                if (!TryReadPointer(process, current + NodeLeftOffset, out var left))
                {
                    return false;
                }

                if (IsNilNode(process, left, header))
                {
                    next = current;
                    return true;
                }

                current = left;
            }

            return false;
        }

        if (!TryReadPointer(process, node + NodeParentOffset, out var parent))
        {
            return false;
        }

        for (var guard = 0; !IsNilNode(process, parent, header) && guard < 1024; guard++)
        {
            if (!TryReadPointer(process, parent + NodeRightOffset, out var parentRight))
            {
                return false;
            }

            if (node != parentRight)
            {
                break;
            }

            node = parent;
            if (!TryReadPointer(process, parent + NodeParentOffset, out parent))
            {
                return false;
            }
        }

        next = parent;
        return true;
    }

    private static bool IsNilNode(VmmProcess process, ulong node, ulong header)
    {
        if (node == 0 || node == header)
        {
            return true;
        }

        return !TryReadByte(process, node + NodeIsNilOffset, out var isNil) || isNil != 0;
    }

    private static bool TryReadByte(VmmProcess process, ulong address, out byte value)
    {
        value = 0;
        try
        {
            var buffer = process.MemRead(address, 1);
            if (buffer is null || buffer.Length < 1)
            {
                return false;
            }

            value = buffer[0];
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadBytes(VmmProcess process, ulong address, int count, out byte[] value)
    {
        value = Array.Empty<byte>();
        try
        {
            var buffer = process.MemRead(address, (uint)count);
            if (buffer is null || buffer.Length < count)
            {
                return false;
            }

            value = buffer;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadMsvcWString(VmmProcess process, ulong stringObject, out string value)
    {
        value = string.Empty;
        if (!TryReadUInt64(process, stringObject + 0x10, out var length) ||
            !TryReadUInt64(process, stringObject + 0x18, out var capacity))
        {
            return false;
        }

        if (length == 0)
        {
            return true;
        }

        if (length > 256 || capacity > 0x100000)
        {
            return false;
        }

        var characters = stringObject;
        if (capacity >= 8 && !TryReadPointer(process, stringObject, out characters))
        {
            return false;
        }

        return characters != 0 && TryReadUtf16StringByLength(process, characters, (int)length, out value);
    }

    private static bool TryReadUtf16StringByLength(VmmProcess process, ulong address, int charCount, out string value)
    {
        value = string.Empty;
        if (charCount <= 0)
        {
            return true;
        }

        if (!TryReadBytes(process, address, charCount * 2, out var buffer))
        {
            return false;
        }

        var byteCount = buffer.Length;
        for (var i = 0; i + 1 < buffer.Length; i += 2)
        {
            if (buffer[i] == 0 && buffer[i + 1] == 0)
            {
                byteCount = i;
                break;
            }
        }

        value = byteCount == 0 ? string.Empty : Encoding.Unicode.GetString(buffer, 0, byteCount);
        return true;
    }

    private static bool TryReadUtf16String(VmmProcess process, ulong address, int maxChars, out string value)
    {
        value = string.Empty;
        if (maxChars <= 0)
        {
            return true;
        }

        if (!TryReadBytes(process, address, maxChars * 2, out var buffer))
        {
            return false;
        }

        var byteCount = buffer.Length;
        for (var i = 0; i + 1 < buffer.Length; i += 2)
        {
            if (buffer[i] == 0 && buffer[i + 1] == 0)
            {
                byteCount = i;
                break;
            }
        }

        value = byteCount == 0 ? string.Empty : Encoding.Unicode.GetString(buffer, 0, byteCount);
        return true;
    }

    private static bool TryReadUInt16(VmmProcess process, ulong address, out ushort value)
    {
        value = 0;
        try
        {
            var buffer = process.MemRead(address, 2);
            if (buffer is null || buffer.Length < 2)
            {
                return false;
            }

            value = BitConverter.ToUInt16(buffer, 0);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadSingle(VmmProcess process, ulong address, out float value)
    {
        value = 0;
        try
        {
            var buffer = process.MemRead(address, 4);
            if (buffer is null || buffer.Length < 4)
            {
                return false;
            }

            value = BitConverter.ToSingle(buffer, 0);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadUInt32(VmmProcess process, ulong address, out uint value)
    {
        value = 0;
        try
        {
            var buffer = process.MemRead(address, 4);
            if (buffer is null || buffer.Length < 4)
            {
                return false;
            }

            value = BitConverter.ToUInt32(buffer, 0);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadUInt64(VmmProcess process, ulong address, out ulong value)
    {
        value = 0;
        try
        {
            var buffer = process.MemRead(address, 8);
            if (buffer is null || buffer.Length < 8)
            {
                return false;
            }

            value = BitConverter.ToUInt64(buffer, 0);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadPointer(VmmProcess process, ulong address, out ulong value)
    {
        value = 0;
        if (TryReadUInt64(process, address, out var v64) && IsLikelyUserPointer(v64))
        {
            value = v64;
            return true;
        }

        if (TryReadUInt32(process, address, out var v32) && v32 != 0)
        {
            value = v32;
            return true;
        }

        return false;
    }

    private static bool IsLikelyUserPointer(ulong value)
    {
        return value != 0 && value <= 0x00007FFFFFFFFFFFUL;
    }

    private sealed class LockedTargetInfo
    {
        public ushort TargetEntityId;
        public uint ServerObjectId;
        public uint LocalServerObjectId;
        public ulong Entity;
        public ushort EntityType;
        public Vector3Snapshot? Position;
        public double? DistanceToLocalPlayer;
        public ActorInfo? Actor;
    }

    private sealed class ActorInfo
    {
        public ulong Actor;
        public ulong Entity;
        public uint ObjectType;
        public uint ServerObjectId;
        public uint NpcTemplateId;
        public ushort Level;
        public byte HpPercent;
        public uint TargetServerObjectId;
        public uint MaxHp;
        public uint CurrentHp;
        public string Name = string.Empty;
        public string ResolveSource = string.Empty;
    }

    private sealed record VmmConnection(string DeviceName, string Remote, MemProcVmm Vmm)
    {
        public object SyncRoot { get; } = new();
    }

    private sealed record SkillXmlCatalog(
        string Path,
        DateTimeOffset LastWriteTime,
        long Length,
        IReadOnlyDictionary<uint, SkillXmlStaticDetail> Details,
        string Error);

    private sealed record NpcXmlCatalog(
        string Path,
        DateTimeOffset LastWriteTime,
        long Length,
        IReadOnlyDictionary<uint, NpcStaticDetail> Details,
        string Error);

    private struct NpcStaticDetail
    {
        public uint Id;
        public string Name;
        public string UiType;
        public string CursorType;
        public string NpcType;
        public string Tribe;
        public bool HasDirectAggressive;
        public bool DirectAggressive;
        public bool IsMonsterKnown;
        public bool IsMonster;
        public bool AggressiveKnown;
        public bool AggressiveToPlayer;
        public string AggressiveSource;
    }

    private struct NpcTribeRelation
    {
        public string Tribe;
        public string BaseTribe;
        public string Aggressive;
        public bool AggressiveToPlayer;
    }

    private struct WorldObjectReadCounters
    {
        public int ScannedServerObjects;
        public int ResolvedEntities;
        public int NpcLikeEntities;
    }

    private struct LearnedSkillInfo
    {
        public uint SkillId;
        public ushort HighestLevel;
        public ulong SkillItem;
        public string Name;
        public string DisplayBaseName;
        public int DisplayTier;
        public uint Field0C;
        public ulong RankValue;
        public uint CooldownDuration;
        public uint CooldownEndTime;
        public uint ToggleState;
        public uint SkillLevel;
        public uint StaticFieldD8;
        public uint RuntimeState;
        public uint SourceFlags;
        public ulong LevelTreeSize;
        public ulong ItemListSize;
        public bool HasXmlStaticDetail;
        public SkillXmlStaticDetail XmlStaticDetail;
    }

    private struct SkillXmlStaticDetail
    {
        public uint Id;
        public string XmlName;
        public string ActivationAttribute;
        public string TargetSlot;
        public string ChainCategoryName;
        public string PrechainCategoryName;
        public string ChainTime;
        public string StatusFx;
        public string AuraFx;
        public string CounterSkill;
        public string CostDp;
        public string UltraSkill;
    }

    private static readonly string[] IgnoredUtilitySkillNames =
    {
        "紧急返回",
        "精气提取",
        "奥德提取",
        "炼金术",
        "物质变幻",
        "宠物管理",
        "宠物礼物",
        "自动使用物品",
        "自动拾取物品",
        "战斗/一般转换",
        "休息/一般转换",
        "捡取道具",
        "选择对象的对象",
        "切换武器",
        "走/跑 转换",
        "攻击/对话",
        "飞行/着陆切换",
        "封魂石 使用/解除",
        "自动打猎申报"
    };

    private static readonly string[] IgnoredSkillNameParts =
    {
        "基础",
        "基本",
        "穿着",
        "修炼",
        "防御力增加",
        "抵抗强化",
        "返回",
        "提取",
        "炼金术",
        "物质变幻",
        "宠物",
        "一般转换",
        "捡取道具",
        "选择对象",
        "切换武器",
        "走/跑",
        "攻击/对话",
        "飞行/着陆",
        "封魂石",
        "自动打猎",
        "自动使用物品",
        "自动拾取物品",
        "显示标志",
        "选择证物"
    };
}
