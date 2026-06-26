using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
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
    private const ulong EntityLocalPositionOffset = 0x4F4;
    private const ulong EntityProxyManagerVfuncOffset = 0xB8;

    private const ulong ServerNodeServerObjectIdOffset = 0x1C;
    private const ulong ServerNodeEntityIdOffset = 0x20;

    private const ulong ActorEntityOffset = 0x08;
    private const ulong ActorObjectTypeOffset = 0x20;
    private const ulong ActorServerObjectIdOffset = 0x2C;
    private const ulong ActorNpcTemplateIdOffset = 0x30;
    private const ulong ActorLevelOffset = 0x3E;
    private const ulong ActorHpPercentOffset = 0x40;
    private const ulong ActorNameOffset = 0x42;
    private const ulong ActorTargetServerObjectIdOffset = 0x358;
    private const ulong ActorMaxHpOffset = 0x11A0;
    private const ulong ActorCurrentHpOffset = 0x11A4;

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
    private bool _nativeLibrariesLoaded;

    public AionVmmGameApi(AionVmmGameApiOptions options, IRoadhogLogger logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task<OperationResult<PlayerSnapshot>> ReadPlayerAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<PlayerSnapshot>.Fail("Direct VMM player snapshot is not implemented yet."));
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
        return Task.FromResult(OperationResult<IReadOnlyList<WorldObjectSnapshot>>.Fail("Direct VMM world object snapshot is not implemented yet."));
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
            TryFindEntityById(process, entityTreeHeader, localEntityId, out var localEntity) &&
            TryReadEntityPosition(process, localEntity, out var localX, out var localY, out var localZ) &&
            info.Position is { } targetPosition)
        {
            var dx = targetPosition.X - localX;
            var dy = targetPosition.Y - localY;
            var dz = targetPosition.Z - localZ;
            info.DistanceToLocalPlayer = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        if (TryResolveActorFromEntity(process, info.Entity, info.ServerObjectId, out var actor))
        {
            info.Actor = actor;
        }

        return true;
    }

    private static LockedTargetSnapshot ToLockedTargetSnapshot(LockedTargetInfo info)
    {
        if (info.TargetEntityId == 0)
        {
            return LockedTargetSnapshot.Empty(DateTimeOffset.Now);
        }

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
            DateTimeOffset.Now);
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
