using System.Globalization;
using System.Reflection;
using System.Text;
using MemProcVmm = Vmmsharp.Vmm;
using Vmmsharp;

internal static class TacticsSignLiveProbe
{
    private const ulong TacticsSignTableRva = 0xD1BA68;
    private const int TacticsSignCount = 16;
    private const ulong ServerObjectTreeRva = 0xD21740;
    private const ulong LocalEntityIdRva = 0xD21798;

    private const ulong NodeLeftOffset = 0x00;
    private const ulong NodeParentOffset = 0x08;
    private const ulong NodeRightOffset = 0x10;
    private const ulong NodeIsNilOffset = 0x19;
    private const ulong ServerNodeServerObjectIdOffset = 0x1C;
    private const ulong ServerNodeEntityIdOffset = 0x20;

    public static bool ShouldRun(string[] args)
    {
        if (args.Any(arg =>
                string.Equals(arg, "tactics_sign_probe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "sign_probe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "signs", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--tactics-sign-probe", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var mode = Environment.GetEnvironmentVariable("ROADHOG_TEST_MODE")
                   ?? Environment.GetEnvironmentVariable("AION_TEST_MODE");

        return string.Equals(mode, "tactics_sign_probe", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mode, "sign_probe", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mode, "signs", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mode, "mark_probe", StringComparison.OrdinalIgnoreCase);
    }

    public static int Run(string[] args)
    {
        TrySetConsoleEncoding();

        var deviceName = ReadOption(args, "--device=", "ROADHOG_SIGN_PROBE_DEVICE", "VMM_DEVICE", "fpga");
        var processName = ReadOption(args, "--process=", "ROADHOG_SIGN_PROBE_PROCESS", "VMM_PROCESS", "Aion.bin");
        var moduleName = ReadOption(args, "--module=", "ROADHOG_SIGN_PROBE_MODULE", "VMM_MODULE", "Game.dll");
        var remote = ReadOption(args, "--remote=", "ROADHOG_SIGN_PROBE_REMOTE", "VMM_REMOTE", string.Empty);
        var processId = ReadIntOption(args, "--pid=", "ROADHOG_SIGN_PROBE_PID", "VMM_PID", 0);
        var leaderMarkIndex = Clamp(ReadIntFromEnv("ROADHOG_SIGN_PROBE_MARK_INDEX", 0), 0, TacticsSignCount - 1);
        var printEmptySlots = ReadBoolFromEnv("ROADHOG_SIGN_PROBE_PRINT_EMPTY", true);

        Console.WriteLine("Roadhog tactics sign live probe.");
        Console.WriteLine("Device=" + deviceName +
                          " Remote=" + (string.IsNullOrWhiteSpace(remote) ? "<none>" : remote) +
                          " Process=" + processName +
                          " Pid=" + (processId > 0 ? processId.ToString(CultureInfo.InvariantCulture) : "<by-name>") +
                          " Module=" + moduleName +
                          " LeaderMarkIndex=" + leaderMarkIndex.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("Reads GameBase+0xD1BA68 as uint32 ServerObjectId[16].");

        try
        {
            LoadNativeLibraries();

            var vmmArgs = string.IsNullOrWhiteSpace(remote)
                ? new[] { "-device", deviceName }
                : new[] { "-device", deviceName, "-remote", remote };

            using var vmm = new MemProcVmm(vmmArgs);
            if (!TryResolveProcess(vmm, processName, processId, out var process, out var processError))
            {
                Console.Error.WriteLine("Process resolve failed: " + processError);
                return 2;
            }

            Console.WriteLine("Connected to process: " + process.Name + " (PID " + SafeGetProcessPid(process) + ")");

            var gameBase = process.GetModuleBase(moduleName);
            if (gameBase == 0)
            {
                Console.Error.WriteLine("Module not found: " + moduleName);
                return 3;
            }

            Console.WriteLine("Module base: " + moduleName + " = " + FormatAddress(gameBase));

            TryReadUInt16(process, gameBase + LocalEntityIdRva, out var localEntityId);
            TryReadUInt16(process, gameBase + LocalEntityIdRva + 2, out var targetEntityId);

            uint currentTargetServerId = 0;
            var hasTargetServerId = targetEntityId != 0 &&
                                    TryFindServerObjectIdByEntityId(process, gameBase, targetEntityId, out currentTargetServerId);

            Console.WriteLine("LocalEntityId=" + localEntityId.ToString(CultureInfo.InvariantCulture) +
                              " CurrentTargetEntityId=" + targetEntityId.ToString(CultureInfo.InvariantCulture) +
                              " CurrentTargetServerId=" + (hasTargetServerId ? currentTargetServerId.ToString(CultureInfo.InvariantCulture) : "Unknown"));

            var activeCount = 0;
            var currentTargetMatched = false;
            for (var index = 0; index < TacticsSignCount; index++)
            {
                if (!TryReadUInt32(process, gameBase + TacticsSignTableRva + (ulong)(index * 4), out var serverId))
                {
                    Console.WriteLine("SignSlot#" + index.ToString("00", CultureInfo.InvariantCulture) +
                                      " Address=" + FormatAddress(gameBase + TacticsSignTableRva + (ulong)(index * 4)) +
                                      " Read=failed");
                    continue;
                }

                var active = serverId != 0;
                if (active)
                {
                    activeCount++;
                }

                var matchesCurrentTarget = hasTargetServerId && serverId != 0 && serverId == currentTargetServerId;
                currentTargetMatched |= matchesCurrentTarget;

                if (!printEmptySlots && !active)
                {
                    continue;
                }

                Console.WriteLine("SignSlot#" + index.ToString("00", CultureInfo.InvariantCulture) +
                                  " DisplayNumber=" + (index + 1).ToString(CultureInfo.InvariantCulture) +
                                  " Resource=sign_" + (index + 1).ToString(CultureInfo.InvariantCulture) +
                                  " Address=" + FormatAddress(gameBase + TacticsSignTableRva + (ulong)(index * 4)) +
                                  " ServerId=" + serverId.ToString(CultureInfo.InvariantCulture) +
                                  " Active=" + (active ? "yes" : "no") +
                                  " MatchesCurrentTarget=" + (matchesCurrentTarget ? "yes" : "no"));
            }

            var leaderMarkedTargetId = TryReadUInt32(
                process,
                gameBase + TacticsSignTableRva + (ulong)(leaderMarkIndex * 4),
                out var configuredSlotServerId)
                    ? configuredSlotServerId
                    : 0;

            Console.WriteLine("TacticsSignSummary" +
                              " ActiveCount=" + activeCount.ToString(CultureInfo.InvariantCulture) +
                              " CurrentTargetMatched=" + (currentTargetMatched ? "yes" : "no") +
                              " LeaderMarkIndex=" + leaderMarkIndex.ToString(CultureInfo.InvariantCulture) +
                              " MarkedTargetId=" + leaderMarkedTargetId.ToString(CultureInfo.InvariantCulture));

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Tactics sign probe exception: " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }

    private static bool TryFindServerObjectIdByEntityId(
        VmmProcess process,
        ulong gameBase,
        ushort entityId,
        out uint serverObjectId)
    {
        serverObjectId = 0;
        if (entityId == 0 ||
            !TryReadPointer(process, gameBase + ServerObjectTreeRva, out var serverTreeHeader) ||
            serverTreeHeader == 0 ||
            !TryReadPointer(process, serverTreeHeader + NodeLeftOffset, out var node))
        {
            return false;
        }

        for (var guard = 0; node != 0 && node != serverTreeHeader && guard < 100000; guard++)
        {
            if (IsNilNode(process, node, serverTreeHeader))
            {
                break;
            }

            if (TryReadUInt16(process, node + ServerNodeEntityIdOffset, out var nodeEntityId) &&
                nodeEntityId == entityId)
            {
                return TryReadUInt32(process, node + ServerNodeServerObjectIdOffset, out serverObjectId) &&
                       serverObjectId != 0;
            }

            if (!TryGetNextTreeNode(process, serverTreeHeader, node, out var next) || next == node)
            {
                break;
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

    private static void LoadNativeLibraries()
    {
        var memProcFsHome = Environment.GetEnvironmentVariable("MEMPROCFS_HOME");
        if (string.IsNullOrWhiteSpace(memProcFsHome) &&
            File.Exists(Path.Combine(AppContext.BaseDirectory, "vmm.dll")))
        {
            memProcFsHome = AppContext.BaseDirectory;
        }

        if (string.IsNullOrWhiteSpace(memProcFsHome) && Directory.Exists(@"C:\MemProcFS"))
        {
            memProcFsHome = @"C:\MemProcFS";
        }

        if (!string.IsNullOrWhiteSpace(memProcFsHome))
        {
            MemProcVmm.LoadNativeLibrary(memProcFsHome);
            Console.WriteLine("MemProcFS native path: " + memProcFsHome);
        }
    }

    private static bool TryResolveProcess(
        MemProcVmm vmm,
        string processName,
        int processId,
        out VmmProcess process,
        out string error)
    {
        process = default!;
        error = string.Empty;

        if (processId > 0 && TryGetVmmProcessByPid(vmm, processId, out process, out error))
        {
            return process.IsValid;
        }

        process = vmm.Process(processName);
        if (!process.IsValid)
        {
            error = "Target process not found: " + processName;
            return false;
        }

        return true;
    }

    private static bool TryGetVmmProcessByPid(
        MemProcVmm vmm,
        int processId,
        out VmmProcess process,
        out string error)
    {
        process = default!;
        error = string.Empty;

        foreach (var method in typeof(MemProcVmm).GetMethods(BindingFlags.Instance | BindingFlags.Public))
        {
            var parameters = method.GetParameters();
            if (parameters.Length != 1 ||
                !typeof(VmmProcess).IsAssignableFrom(method.ReturnType) ||
                !string.Equals(method.Name, "Process", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var argument = Convert.ChangeType(processId, parameters[0].ParameterType, CultureInfo.InvariantCulture);
                if (method.Invoke(vmm, new[] { argument }) is VmmProcess resolved)
                {
                    process = resolved;
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = ex.GetBaseException().Message;
            }
        }

        error = error.Length == 0
            ? "This vmmsharp build does not expose PID binding."
            : error;
        return false;
    }

    private static bool TryReadByte(VmmProcess process, ulong address, out byte value)
    {
        value = 0;
        if (!TryReadBytes(process, address, 1, out var buffer))
        {
            return false;
        }

        value = buffer[0];
        return true;
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

    private static bool TryReadUInt16(VmmProcess process, ulong address, out ushort value)
    {
        value = 0;
        if (!TryReadBytes(process, address, 2, out var buffer))
        {
            return false;
        }

        value = BitConverter.ToUInt16(buffer, 0);
        return true;
    }

    private static bool TryReadUInt32(VmmProcess process, ulong address, out uint value)
    {
        value = 0;
        if (!TryReadBytes(process, address, 4, out var buffer))
        {
            return false;
        }

        value = BitConverter.ToUInt32(buffer, 0);
        return true;
    }

    private static bool TryReadUInt64(VmmProcess process, ulong address, out ulong value)
    {
        value = 0;
        if (!TryReadBytes(process, address, 8, out var buffer))
        {
            return false;
        }

        value = BitConverter.ToUInt64(buffer, 0);
        return true;
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

    private static int SafeGetProcessPid(VmmProcess process)
    {
        try
        {
            return checked((int)process.PID);
        }
        catch
        {
            return 0;
        }
    }

    private static string FormatAddress(ulong address)
    {
        return "0x" + address.ToString("X", CultureInfo.InvariantCulture);
    }

    private static string ReadOption(
        string[] args,
        string argumentPrefix,
        string primaryEnvironmentName,
        string fallbackEnvironmentName,
        string defaultValue)
    {
        var argument = args.FirstOrDefault(arg => arg.StartsWith(argumentPrefix, StringComparison.OrdinalIgnoreCase));
        if (argument is not null)
        {
            var value = argument[argumentPrefix.Length..].Trim();
            if (value.Length > 0)
            {
                return value;
            }
        }

        var primary = Environment.GetEnvironmentVariable(primaryEnvironmentName);
        if (!string.IsNullOrWhiteSpace(primary))
        {
            return primary.Trim();
        }

        var fallback = Environment.GetEnvironmentVariable(fallbackEnvironmentName);
        return string.IsNullOrWhiteSpace(fallback) ? defaultValue : fallback.Trim();
    }

    private static int ReadIntOption(
        string[] args,
        string argumentPrefix,
        string primaryEnvironmentName,
        string fallbackEnvironmentName,
        int defaultValue)
    {
        var text = ReadOption(args, argumentPrefix, primaryEnvironmentName, fallbackEnvironmentName, string.Empty);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : defaultValue;
    }

    private static int ReadIntFromEnv(string name, int defaultValue)
    {
        var text = Environment.GetEnvironmentVariable(name);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : defaultValue;
    }

    private static bool ReadBoolFromEnv(string name, bool defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Min(max, Math.Max(min, value));
    }

    private static void TrySetConsoleEncoding()
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch
        {
        }
    }
}
