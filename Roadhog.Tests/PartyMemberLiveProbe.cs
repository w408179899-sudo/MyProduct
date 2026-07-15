using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Hardware.KmBox;
using MemProcVmm = Vmmsharp.Vmm;
using Vmmsharp;

internal static class PartyMemberLiveProbe
{
    private const ulong PartyIdRva = 0xD1BAB8;
    private const ulong PartyFlagsRva = 0xD1BABC;
    private const ulong PartyLeaderServerObjectIdRva = 0xD1BAC0;
    private const ulong PrimaryPartyListRva = 0xD1BAE8;
    private const ulong PrimaryPartyCountRva = 0xD1BAF0;
    private const ulong SecondaryPartyListRva = 0xD1BB50;
    private const ulong EntitySystemPointerRva = 0x904690;
    private const ulong ServerObjectTreeRva = 0xD21740;
    private const ulong LocalEntityIdRva = 0xD21798;

    private const ulong NodeLeftOffset = 0x00;
    private const ulong NodeParentOffset = 0x08;
    private const ulong NodeRightOffset = 0x10;
    private const ulong NodeIsNilOffset = 0x19;
    private const ulong NodeIdOffset = 0x20;
    private const ulong NodeEntityOffset = 0x28;
    private const ulong ListNodeNextOffset = 0x00;
    private const ulong PartyListNodeDataOffset = 0x10;

    private const ulong EntityTreeOffset = 0x58;
    private const ulong EntityPositionFlagsOffset = 0xC0;
    private const uint EntityUseAlternatePositionFlag = 0x400;
    private const ulong EntityWorldPositionOffset = 0x4B4;
    private const ulong EntityLocalPositionOffset = 0x4F4;
    private const ulong EntityProxyManagerVfuncOffset = 0xB8;

    private const ulong ServerNodeServerObjectIdOffset = 0x1C;
    private const ulong ServerNodeEntityIdOffset = 0x20;

    private const ulong ActorEntityOffset = 0x08;
    private const ulong ActorObjectTypeOffset = 0x20;
    private const uint ActorPlayerObjectType = 1;
    private const ulong ActorServerObjectIdOffset = 0x2C;
    private const ulong ActorNameOffset = 0x42;
    private const ulong ActorTargetServerObjectIdOffset = 0x358;

    private const uint SpiritmasterClassId = 8;

    private const ulong PartyMemberPartySlotOffset = 0x00;
    private const ulong PartyMemberServerObjectIdOffset = 0x04;
    private const ulong PartyMemberMaxHpOffset = 0x08;
    private const ulong PartyMemberCurrentHpOffset = 0x0C;
    private const ulong PartyMemberMaxMpOffset = 0x10;
    private const ulong PartyMemberCurrentMpOffset = 0x14;
    private const ulong PartyMemberMaxFlightTimeOffset = 0x18;
    private const ulong PartyMemberCurrentFlightTimeOffset = 0x1C;
    private const ulong PartyMemberAreaField0Offset = 0x20;
    private const ulong PartyMemberAreaField1Offset = 0x24;
    private const ulong PartyMemberCachedXOffset = 0x28;
    private const ulong PartyMemberCachedYOffset = 0x2C;
    private const ulong PartyMemberCachedZOffset = 0x30;
    private const ulong PartyMemberClassIdOffset = 0x34;
    private const ulong PartyMemberLevelOffset = 0x36;
    private const ulong PartyMemberDataFlagsOffset = 0x37;
    private const ulong PartyMemberFlightAreaFlagOffset = 0x38;
    private const ulong PartyMemberFlightFlagsOffset = 0x39;
    private const ulong PartyMemberRuntimeStateOffset = 0x3A;
    private const ulong PartyMemberNameOffset = 0x3B;
    private const ulong PartyMemberControlStatusMaskOffset = 0x6F;
    private const byte PartyMemberHasAbnormalBlockFlag = 0x08;
    private const ulong PartyMemberAbnormalCountOffset = 0x77;
    private const ulong PartyMemberAbnormalEntriesOffset = 0x79;
    private const ulong PartyMemberUpdateTimeOffset = 0x859;
    private const int PartyMemberMaxAbnormalCount = 112;

    private const uint AbnormalCategoryPhysical = 2;
    private const string AbnormalKindPositive = "Positive";
    private const string AbnormalKindNegative = "Negative";
    private const string AbnormalKindUnknown = "Unknown";
    private const ulong AbnormalEntrySize = 0x12;
    private const ulong AbnormalEntryField00Offset = 0x00;
    private const ulong AbnormalEntryIdOffset = 0x04;
    private const ulong AbnormalEntryDispelCategoryOffset = 0x08;
    private const ulong AbnormalEntryTimeOrSourceOffset = 0x0C;
    private const ulong AbnormalEntryLevelOrStackOffset = 0x10;

    public static bool ShouldRun(string[] args)
    {
        if (args.Any(arg =>
                string.Equals(arg, "party_probe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "party", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "team_heal_probe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "team_support_probe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "heal_probe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--party-probe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--team-support-probe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--team-heal-probe", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var mode = Environment.GetEnvironmentVariable("ROADHOG_TEST_MODE")
                   ?? Environment.GetEnvironmentVariable("AION_TEST_MODE");

        return string.Equals(mode, "party_probe", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mode, "party", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mode, "party_member", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mode, "party_members", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mode, "team_heal_probe", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mode, "team_support_probe", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mode, "heal_probe", StringComparison.OrdinalIgnoreCase);
    }

    public static int Run(string[] args)
    {
        TrySetConsoleEncoding();

        var deviceName = ReadOption(args, "--device=", "ROADHOG_PARTY_PROBE_DEVICE", "VMM_DEVICE", "fpga");
        var processName = ReadOption(args, "--process=", "ROADHOG_PARTY_PROBE_PROCESS", "VMM_PROCESS", "Aion.bin");
        var moduleName = ReadOption(args, "--module=", "ROADHOG_PARTY_PROBE_MODULE", "VMM_MODULE", "Game.dll");
        var remote = ReadOption(args, "--remote=", "ROADHOG_PARTY_PROBE_REMOTE", "VMM_REMOTE", string.Empty);
        var processId = ReadIntOption(args, "--pid=", "ROADHOG_PARTY_PROBE_PID", "VMM_PID", 0);
        var printAllEntries = ReadBoolFromEnv("AION_PARTY_PRINT_ABNORMAL_ENTRIES", false);
        var abnormalStatusCatalog = LoadAbnormalStatusCatalog(args);

        Console.WriteLine("Roadhog party member live probe.");
        Console.WriteLine("Device=" + deviceName +
                          " Remote=" + (string.IsNullOrWhiteSpace(remote) ? "<none>" : remote) +
                          " Process=" + processName +
                          " Pid=" + (processId > 0 ? processId.ToString(CultureInfo.InvariantCulture) : "<by-name>") +
                          " Module=" + moduleName);
        Console.WriteLine("Reads PartyMemberRecord from primary/secondary party lists. CachedPosition is diagnostic only.");
        Console.WriteLine("AbnormalStatusCatalog Loaded=" + (abnormalStatusCatalog.Loaded ? "yes" : "no") +
                          " Count=" + abnormalStatusCatalog.Count.ToString(CultureInfo.InvariantCulture) +
                          " Source=\"" + abnormalStatusCatalog.SourcePath + "\"" +
                          " Error=\"" + abnormalStatusCatalog.Error + "\"");

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

            if (IsTeamHealProbeRequested(args))
            {
                return RunTeamHealProbe(process, gameBase, args, abnormalStatusCatalog);
            }

            if (!TryReadPartyMemberProbeSnapshots(process, gameBase, out var members, out var error))
            {
                Console.Error.WriteLine("Party member read failed: " + error);
                return 4;
            }

            var partyGlobals = ReadPartyGlobalProbeSnapshot(process, gameBase);
            ApplyPartyLeader(members, partyGlobals.LeaderServerObjectId);

            var hasLiveSummary = TryEnrichPartyMembersWithLiveActors(process, gameBase, members, out var liveSummary, out var liveError);
            if (hasLiveSummary)
            {
                Console.WriteLine(FormatLiveActorProbeSummary(liveSummary));
            }
            else
            {
                Console.WriteLine("LiveActorProbe=failed Error=\"" + liveError + "\"");
            }

            Console.WriteLine(FormatPartyGlobalProbeSnapshot(
                partyGlobals,
                members,
                hasLiveSummary ? liveSummary.LocalServerObjectId : 0,
                hasLiveSummary));

            PrintPartyMemberProbeSnapshots(members, printAllEntries, abnormalStatusCatalog);
            return members.Count == 0 ? 5 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Party probe exception: " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }

    private static bool IsTeamHealProbeRequested(string[] args)
    {
        if (args.Any(arg =>
                string.Equals(arg, "team_heal_probe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "team_support_probe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "heal_probe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--team-support-probe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--team-heal-probe", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var mode = Environment.GetEnvironmentVariable("ROADHOG_TEST_MODE")
                   ?? Environment.GetEnvironmentVariable("AION_TEST_MODE");

        return string.Equals(mode, "team_heal_probe", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mode, "team_support_probe", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mode, "heal_probe", StringComparison.OrdinalIgnoreCase);
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

    private static bool TryReadPartyMemberProbeSnapshots(
        VmmProcess process,
        ulong gameBase,
        out List<PartyMemberProbeSnapshot> snapshots,
        out string error)
    {
        snapshots = new List<PartyMemberProbeSnapshot>();
        error = string.Empty;

        var seen = new HashSet<uint>();
        ReadPartyMemberProbeList(process, gameBase + PrimaryPartyListRva, "primary", snapshots, seen, out var primaryError);
        ReadPartyMemberProbeList(process, gameBase + SecondaryPartyListRva, "secondary", snapshots, seen, out var secondaryError);

        if (snapshots.Count == 0 && primaryError.Length > 0 && secondaryError.Length > 0)
        {
            error = primaryError + "; " + secondaryError;
            return false;
        }

        return true;
    }

    private static bool ReadPartyMemberProbeList(
        VmmProcess process,
        ulong listGlobalAddress,
        string listName,
        List<PartyMemberProbeSnapshot> snapshots,
        HashSet<uint> seenServerObjectIds,
        out string error)
    {
        error = string.Empty;

        if (!TryReadPointer(process, listGlobalAddress, out var head) || head == 0)
        {
            error = "failed to read " + listName + " party list head at " + FormatAddress(listGlobalAddress);
            return false;
        }

        if (!TryReadPointer(process, head + ListNodeNextOffset, out var node))
        {
            error = "failed to read " + listName + " party list first node";
            return false;
        }

        var listIndex = 0;
        var visited = new HashSet<ulong>();
        for (var guard = 0; node != 0 && node != head && guard < 256; guard++)
        {
            if (!visited.Add(node))
            {
                break;
            }

            if (TryReadPointer(process, node + PartyListNodeDataOffset, out var member) &&
                member != 0 &&
                TryReadPartyMemberProbeSnapshot(process, member, node, listName, listIndex, out var snapshot) &&
                (snapshot.ServerObjectId == 0 || seenServerObjectIds.Add(snapshot.ServerObjectId)))
            {
                snapshots.Add(snapshot);
            }

            listIndex++;

            if (!TryReadPointer(process, node + ListNodeNextOffset, out var next) || next == node)
            {
                break;
            }

            node = next;
        }

        return true;
    }

    private static bool TryReadPartyMemberProbeSnapshot(
        VmmProcess process,
        ulong member,
        ulong node,
        string listName,
        int listIndex,
        out PartyMemberProbeSnapshot snapshot)
    {
        snapshot = new PartyMemberProbeSnapshot
        {
            ListName = listName,
            ListIndex = listIndex,
            Node = node,
            Member = member,
            Entries = new List<AbnormalStatusEntry>()
        };

        if (!IsLikelyUserPointer(member))
        {
            return false;
        }

        TryReadUInt32(process, member + PartyMemberPartySlotOffset, out snapshot.PartySlot);
        TryReadUInt32(process, member + PartyMemberServerObjectIdOffset, out snapshot.ServerObjectId);
        TryReadUInt32(process, member + PartyMemberMaxHpOffset, out snapshot.MaxHp);
        TryReadUInt32(process, member + PartyMemberCurrentHpOffset, out snapshot.CurrentHp);
        TryReadUInt32(process, member + PartyMemberMaxMpOffset, out snapshot.MaxMp);
        TryReadUInt32(process, member + PartyMemberCurrentMpOffset, out snapshot.CurrentMp);
        TryReadUInt32(process, member + PartyMemberMaxFlightTimeOffset, out snapshot.MaxFlightTime);
        TryReadUInt32(process, member + PartyMemberCurrentFlightTimeOffset, out snapshot.CurrentFlightTime);
        TryReadUInt32(process, member + PartyMemberAreaField0Offset, out snapshot.AreaField0);
        TryReadUInt32(process, member + PartyMemberAreaField1Offset, out snapshot.AreaField1);
        TryReadSingle(process, member + PartyMemberCachedXOffset, out snapshot.CachedX);
        TryReadSingle(process, member + PartyMemberCachedYOffset, out snapshot.CachedY);
        TryReadSingle(process, member + PartyMemberCachedZOffset, out snapshot.CachedZ);
        TryReadByte(process, member + PartyMemberClassIdOffset, out snapshot.ClassId);
        TryReadByte(process, member + PartyMemberLevelOffset, out snapshot.Level);
        TryReadByte(process, member + PartyMemberDataFlagsOffset, out snapshot.DataFlags);
        TryReadByte(process, member + PartyMemberFlightAreaFlagOffset, out snapshot.FlightAreaFlag);
        TryReadByte(process, member + PartyMemberFlightFlagsOffset, out snapshot.FlightFlags);
        TryReadByte(process, member + PartyMemberRuntimeStateOffset, out snapshot.RuntimeState);
        TryReadUtf16String(process, member + PartyMemberNameOffset, 26, out snapshot.Name);
        TryReadUInt64(process, member + PartyMemberControlStatusMaskOffset, out snapshot.ControlStatusMask);

        snapshot.HasAbnormalBlock = (snapshot.DataFlags & PartyMemberHasAbnormalBlockFlag) != 0;
        TryReadInt16(process, member + PartyMemberAbnormalCountOffset, out snapshot.RawAbnormalCount);
        TryReadUInt32(process, member + PartyMemberUpdateTimeOffset, out snapshot.UpdateTime);

        var count = snapshot.RawAbnormalCount;
        if (count < 0)
        {
            count = 0;
        }
        else if (count > PartyMemberMaxAbnormalCount)
        {
            count = PartyMemberMaxAbnormalCount;
        }

        var entriesAddress = member + PartyMemberAbnormalEntriesOffset;
        for (var i = 0; i < count; i++)
        {
            if (TryReadAbnormalStatusEntry(process, entriesAddress + (ulong)i * AbnormalEntrySize, out var entry))
            {
                snapshot.Entries.Add(entry);
                if (entry.DispelCategory == AbnormalCategoryPhysical)
                {
                    snapshot.PhysicalCount++;
                }
            }
        }

        return snapshot.ServerObjectId != 0 ||
               !string.IsNullOrWhiteSpace(snapshot.Name) ||
               snapshot.MaxHp != 0 ||
               snapshot.MaxMp != 0 ||
               snapshot.RawAbnormalCount != 0 ||
               snapshot.HasAbnormalBlock;
    }

    private static PartyGlobalProbeSnapshot ReadPartyGlobalProbeSnapshot(VmmProcess process, ulong gameBase)
    {
        var snapshot = new PartyGlobalProbeSnapshot();

        snapshot.HasPartyId = TryReadUInt32(process, gameBase + PartyIdRva, out snapshot.PartyId);
        snapshot.HasPartyFlags = TryReadUInt32(process, gameBase + PartyFlagsRva, out snapshot.PartyFlags);
        snapshot.HasLeaderServerObjectId = TryReadUInt32(
            process,
            gameBase + PartyLeaderServerObjectIdRva,
            out snapshot.LeaderServerObjectId);
        snapshot.HasPrimaryPartyCount = TryReadUInt64(
            process,
            gameBase + PrimaryPartyCountRva,
            out snapshot.PrimaryPartyCount);

        return snapshot;
    }

    private static void ApplyPartyLeader(List<PartyMemberProbeSnapshot> members, uint leaderServerObjectId)
    {
        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            member.IsLeader = leaderServerObjectId != 0 &&
                              member.ServerObjectId == leaderServerObjectId;
            members[i] = member;
        }
    }

    private static bool TryReadAbnormalStatusEntry(
        VmmProcess process,
        ulong address,
        out AbnormalStatusEntry entry)
    {
        entry = new AbnormalStatusEntry { Address = address };
        return TryReadUInt32(process, address + AbnormalEntryField00Offset, out entry.Field00) &&
               TryReadUInt32(process, address + AbnormalEntryIdOffset, out entry.AbnormalId) &&
               TryReadUInt32(process, address + AbnormalEntryDispelCategoryOffset, out entry.DispelCategory) &&
               TryReadUInt32(process, address + AbnormalEntryTimeOrSourceOffset, out entry.TimeOrSource) &&
               TryReadUInt16(process, address + AbnormalEntryLevelOrStackOffset, out entry.LevelOrStack);
    }

    private static bool TryEnrichPartyMembersWithLiveActors(
        VmmProcess process,
        ulong gameBase,
        List<PartyMemberProbeSnapshot> members,
        out LiveActorProbeSummary summary,
        out string error)
    {
        summary = new LiveActorProbeSummary();
        error = string.Empty;

        if (!TryReadPointer(process, gameBase + EntitySystemPointerRva, out var entitySystem))
        {
            error = "failed to read EntitySystem pointer at Game.dll+0x" + EntitySystemPointerRva.ToString("X", CultureInfo.InvariantCulture);
            return false;
        }

        if (!TryReadPointer(process, entitySystem + EntityTreeOffset, out var entityTreeHeader))
        {
            error = "failed to read EntitySystem tree header at EntitySystem+0x" + EntityTreeOffset.ToString("X", CultureInfo.InvariantCulture);
            return false;
        }

        if (!TryReadUInt16(process, gameBase + LocalEntityIdRva, out var localEntityId) || localEntityId == 0)
        {
            error = "failed to read local entity id at Game.dll+0x" + LocalEntityIdRva.ToString("X", CultureInfo.InvariantCulture);
            return false;
        }

        summary.LocalEntityId = localEntityId;

        if (!TryFindEntityById(process, entityTreeHeader, localEntityId, out var localEntity))
        {
            error = "local entity id " + localEntityId.ToString(CultureInfo.InvariantCulture) + " was not found in EntitySystem tree";
            return false;
        }

        summary.LocalEntity = localEntity;

        if (!TryResolveActorFromEntity(process, localEntity, 0, out var localActor) ||
            localActor.ServerObjectId == 0)
        {
            error = "failed to resolve local actor/server object id";
            return false;
        }

        summary.LocalActor = localActor.Actor;
        summary.LocalServerObjectId = localActor.ServerObjectId;
        summary.LocalTargetServerObjectId = localActor.TargetServerObjectId;
        summary.LocalName = localActor.Name;

        if (TryReadEntityPosition(process, localEntity, out var localX, out var localY, out var localZ) &&
            IsReasonablePosition(localX, localY, localZ))
        {
            summary.HasLocalPosition = true;
            summary.LocalX = localX;
            summary.LocalY = localY;
            summary.LocalZ = localZ;
        }

        if (!TryReadVisiblePlayerActors(process, gameBase, entityTreeHeader, out var actorsByServerId, out error))
        {
            return false;
        }

        summary.VisiblePlayerActorCount = actorsByServerId.Count;

        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            member.IsSelf = member.ServerObjectId != 0 &&
                            member.ServerObjectId == summary.LocalServerObjectId;

            if (member.ServerObjectId != 0 &&
                actorsByServerId.TryGetValue(member.ServerObjectId, out var liveActor))
            {
                member.HasLiveActor = true;
                member.LiveActor = liveActor.Actor.Actor;
                member.LiveEntity = liveActor.Entity;
                member.LiveTargetServerObjectId = liveActor.Actor.TargetServerObjectId;
                member.LiveActorName = liveActor.Actor.Name;

                if (liveActor.HasPosition)
                {
                    member.HasLivePosition = true;
                    member.LiveX = liveActor.X;
                    member.LiveY = liveActor.Y;
                    member.LiveZ = liveActor.Z;

                    if (summary.HasLocalPosition)
                    {
                        var dx = liveActor.X - summary.LocalX;
                        var dy = liveActor.Y - summary.LocalY;
                        var dz = liveActor.Z - summary.LocalZ;
                        member.HasDistanceToLocal = true;
                        member.DistanceToLocal = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                        member.VisibilityState = member.DistanceToLocal <= 50.0
                            ? "ScreenVisible"
                            : "LoadedOutOfRange";
                    }
                    else
                    {
                        member.VisibilityState = "LoadedDistanceUnknown";
                    }
                }
                else
                {
                    member.VisibilityState = "LoadedPositionUnknown";
                }
            }
            else
            {
                member.VisibilityState = "NotLoaded";
            }

            members[i] = member;
        }

        return true;
    }

    private static int RunTeamHealProbe(
        VmmProcess process,
        ulong gameBase,
        string[] args,
        AbnormalStatusCatalog abnormalStatusCatalog)
    {
        var monitorMs = Math.Max(
            1000,
            ReadIntOption(args, "--monitor-ms=", "ROADHOG_TEAM_HEAL_MONITOR_MS", "ROADHOG_MONITOR_MS", 60000));
        var intervalMs = Math.Max(
            250,
            ReadIntOption(args, "--interval-ms=", "ROADHOG_TEAM_HEAL_INTERVAL_MS", "ROADHOG_INTERVAL_MS", 1000));
        var stopOnIncrease = ReadBoolFromEnv("ROADHOG_TEAM_HEAL_STOP_ON_INCREASE", false);
        var autoPressHeal = ReadBoolFromEnv("ROADHOG_TEAM_HEAL_AUTO_PRESS", false);
        var autoPressSupport = ReadBoolFromEnv("ROADHOG_TEAM_SUPPORT_AUTO_PRESS", autoPressHeal);
        var autoPressCleanse = ReadBoolFromEnv("ROADHOG_TEAM_CLEANSE_AUTO_PRESS", autoPressSupport);
        var repeatAutoPress = ReadBoolFromEnv("ROADHOG_TEAM_HEAL_REPEAT_PRESS", false);
        var autoPressIntervalMs = Math.Max(
            500,
            ReadIntOption(args, "--auto-press-interval-ms=", "ROADHOG_TEAM_HEAL_PRESS_INTERVAL_MS", "ROADHOG_PRESS_INTERVAL_MS", 2500));
        var healKey = ReadOption(args, "--heal-key=", "ROADHOG_TEAM_HEAL_KEY", "ROADHOG_HEAL_KEY", "NumPad1");
        var cleanseKey = ReadOption(args, "--cleanse-key=", "ROADHOG_TEAM_CLEANSE_KEY", "ROADHOG_CLEANSE_KEY", "NumPad7");
        var mentalCleanseKey = ReadOption(args, "--mental-cleanse-key=", "ROADHOG_TEAM_MENTAL_CLEANSE_KEY", "ROADHOG_MENTAL_CLEANSE_KEY", "NumPad8");
        var autoPressMentalCleanse = ReadBoolFromEnv(
            "ROADHOG_TEAM_MENTAL_CLEANSE_AUTO_PRESS",
            autoPressSupport);
        var selectConfirmDelayMs = Math.Max(
            50,
            ReadIntOption(args, "--select-confirm-delay-ms=", "ROADHOG_TEAM_SELECT_CONFIRM_DELAY_MS", "ROADHOG_SELECT_CONFIRM_DELAY_MS", 220));
        var selectRetryCount = Math.Max(
            1,
            ReadIntOption(args, "--select-retry-count=", "ROADHOG_TEAM_SELECT_RETRY_COUNT", "ROADHOG_SELECT_RETRY_COUNT", 4));
        var targetActionCooldownMs = Math.Max(
            0,
            ReadIntOption(args, "--target-action-cooldown-ms=", "ROADHOG_TEAM_TARGET_ACTION_COOLDOWN_MS", "ROADHOG_TARGET_ACTION_COOLDOWN_MS", autoPressIntervalMs));
        var kmboxIp = ReadOption(args, "--kmbox-ip=", "ROADHOG_KMBOX_IP", "KMBOX_NET_IP", "192.168.2.188");
        var kmboxPort = ReadIntOption(args, "--kmbox-port=", "ROADHOG_KMBOX_PORT", "KMBOX_NET_PORT", 4967);
        var kmboxMac = ReadOption(args, "--kmbox-mac=", "ROADHOG_KMBOX_MAC", "KMBOX_NET_MAC", "5BF7E466");
        var kmboxHoldMs = Math.Max(1, ReadIntOption(args, "--kmbox-hold-ms=", "ROADHOG_KMBOX_HOLD_MS", "KMBOX_HOLD_MS", 70));
        var kmboxGapMs = Math.Max(0, ReadIntOption(args, "--kmbox-gap-ms=", "ROADHOG_KMBOX_GAP_MS", "KMBOX_GAP_MS", 350));

        Console.WriteLine("Roadhog team support live probe.");
        Console.WriteLine("MonitorMs=" + monitorMs.ToString(CultureInfo.InvariantCulture) +
                          " IntervalMs=" + intervalMs.ToString(CultureInfo.InvariantCulture) +
                          " StopOnIncrease=" + (stopOnIncrease ? "yes" : "no") +
                          " AutoPressHeal=" + (autoPressHeal ? "yes" : "no") +
                          " AutoPressCleanse=" + (autoPressCleanse ? "yes" : "no") +
                          " AutoPressMentalCleanse=" + (autoPressMentalCleanse ? "yes" : "no") +
                          " RepeatAutoPress=" + (repeatAutoPress ? "yes" : "no") +
                          " AutoPressIntervalMs=" + autoPressIntervalMs.ToString(CultureInfo.InvariantCulture) +
                          " HealKey=" + healKey +
                          " CleanseKey=" + cleanseKey +
                          " MentalCleanseKey=" + (string.IsNullOrWhiteSpace(mentalCleanseKey) ? "<none>" : mentalCleanseKey) +
                          " SelectRetryCount=" + selectRetryCount.ToString(CultureInfo.InvariantCulture) +
                          " SelectConfirmDelayMs=" + selectConfirmDelayMs.ToString(CultureInfo.InvariantCulture) +
                          " TargetActionCooldownMs=" + targetActionCooldownMs.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("Watch party HP and classified abnormal statuses from PartyMemberRecord; select the member body before pressing maintenance keys.");

        var started = DateTime.UtcNow;
        var previousHpByMember = new Dictionary<uint, uint>();
        var previousPhysicalByMember = new Dictionary<uint, int>();
        var previousMentalCleanseByMember = new Dictionary<uint, int>();
        var sawDamageByMember = new HashSet<uint>();
        var sawHealByMember = new HashSet<uint>();
        var sawPhysicalByMember = new HashSet<uint>();
        var sawPhysicalClearedByMember = new HashSet<uint>();
        var sawMentalCleanseByMember = new HashSet<uint>();
        var sawMentalCleanseClearedByMember = new HashSet<uint>();
        var autoPressAttempted = false;
        var autoPressSucceeded = false;
        var autoPressStatus = "not_attempted";
        var autoPressCount = 0;
        var autoPressSuccessCount = 0;
        var cleansePressCount = 0;
        var cleansePressSuccessCount = 0;
        var mentalCleansePressCount = 0;
        var mentalCleansePressSuccessCount = 0;
        var healPressCount = 0;
        var healPressSuccessCount = 0;
        var lastActionKind = "none";
        var lastActionTarget = "none";
        var lastAutoPressAt = DateTime.MinValue;
        var lastActionAtByMemberAction = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        bool IsTargetActionReady(PartyMemberProbeSnapshot member, string kind, DateTime now)
        {
            if (targetActionCooldownMs == 0)
            {
                return true;
            }

            var key = member.ServerObjectId.ToString(CultureInfo.InvariantCulture) + ":" + kind;
            return !lastActionAtByMemberAction.TryGetValue(key, out var lastActionAt) ||
                   (now - lastActionAt).TotalMilliseconds >= targetActionCooldownMs;
        }

        void MarkTargetActionAttempt(PartyMemberProbeSnapshot member, string kind, DateTime now)
        {
            var key = member.ServerObjectId.ToString(CultureInfo.InvariantCulture) + ":" + kind;
            lastActionAtByMemberAction[key] = now;
        }

        for (var sample = 1; ; sample++)
        {
            var elapsedMs = (int)(DateTime.UtcNow - started).TotalMilliseconds;
            if (elapsedMs > monitorMs)
            {
                break;
            }

            if (!TryReadPartyMemberProbeSnapshots(process, gameBase, out var members, out var error))
            {
                Console.WriteLine("SupportProbe sample=" + sample.ToString(CultureInfo.InvariantCulture) +
                                  " elapsedMs=" + elapsedMs.ToString(CultureInfo.InvariantCulture) +
                                  " Read=failed Error=\"" + error + "\"");
            }
            else
            {
                var globals = ReadPartyGlobalProbeSnapshot(process, gameBase);
                ApplyPartyLeader(members, globals.LeaderServerObjectId);

                var hasLiveSummary = TryEnrichPartyMembersWithLiveActors(
                    process,
                    gameBase,
                    members,
                    out var liveSummary,
                    out var liveError);

                var hasLocal = hasLiveSummary && liveSummary.LocalServerObjectId != 0;
                var localMember = hasLocal
                    ? members.FirstOrDefault(member => member.ServerObjectId == liveSummary.LocalServerObjectId)
                    : default;
                var leaderMember = globals.HasLeaderServerObjectId && globals.LeaderServerObjectId != 0
                    ? members.FirstOrDefault(member => member.ServerObjectId == globals.LeaderServerObjectId)
                    : default;

                Console.WriteLine("SupportProbe sample=" + sample.ToString(CultureInfo.InvariantCulture) +
                                  " elapsedMs=" + elapsedMs.ToString(CultureInfo.InvariantCulture) +
                                  " Local=" + FormatHealProbeMemberName(localMember, hasLocal) +
                                  " LocalIsLeader=" + (hasLiveSummary && globals.HasLeaderServerObjectId && liveSummary.LocalServerObjectId == globals.LeaderServerObjectId ? "yes" : "no") +
                                  " Leader=" + FormatHealProbeMemberName(leaderMember, leaderMember.ServerObjectId != 0) +
                                  " LocalTargetServerId=" + (hasLiveSummary ? liveSummary.LocalTargetServerObjectId.ToString(CultureInfo.InvariantCulture) : "Unknown") +
                                  " LiveSummary=" + (hasLiveSummary ? "ok" : "failed:" + liveError));

                var supportMembers = members
                    .Where(member => member.ServerObjectId != 0 &&
                                     (!hasLocal || member.ServerObjectId != liveSummary.LocalServerObjectId))
                    .ToList();

                for (var i = 0; i < supportMembers.Count; i++)
                {
                    var member = supportMembers[i];
                    var previousHpKnown = previousHpByMember.TryGetValue(member.ServerObjectId, out var previousHp);
                    var hpDelta = previousHpKnown
                        ? (long)member.CurrentHp - previousHp
                        : 0;
                    var needsHeal = member.MaxHp > 0 &&
                                    member.CurrentHp > 0 &&
                                    member.CurrentHp < member.MaxHp;
                    if (previousHpKnown && member.CurrentHp < previousHp)
                    {
                        sawDamageByMember.Add(member.ServerObjectId);
                    }

                    if (sawDamageByMember.Contains(member.ServerObjectId) &&
                        previousHpKnown &&
                        member.CurrentHp > previousHp)
                    {
                        sawHealByMember.Add(member.ServerObjectId);
                    }

                    var previousPhysicalKnown = previousPhysicalByMember.TryGetValue(member.ServerObjectId, out var previousPhysicalCount);
                    if (member.PhysicalCount > 0)
                    {
                        sawPhysicalByMember.Add(member.ServerObjectId);
                    }

                    if (previousPhysicalKnown &&
                        previousPhysicalCount > 0 &&
                        member.PhysicalCount == 0)
                    {
                        sawPhysicalClearedByMember.Add(member.ServerObjectId);
                    }

                    previousHpByMember[member.ServerObjectId] = member.CurrentHp;
                    previousPhysicalByMember[member.ServerObjectId] = member.PhysicalCount;

                    var selectKey = ComputeSelectMemberKey(members, member.ServerObjectId, hasLocal ? liveSummary.LocalServerObjectId : 0);
                    var isSelected = hasLiveSummary &&
                                     liveSummary.LocalTargetServerObjectId != 0 &&
                                     liveSummary.LocalTargetServerObjectId == member.ServerObjectId;
                    var pressMode = isSelected ? "action_only_current_target" : "select_then_action";
                    var positiveCount = CountAbnormalStatuses(member, abnormalStatusCatalog, AbnormalKindPositive);
                    var negativeCount = CountAbnormalStatuses(member, abnormalStatusCatalog, AbnormalKindNegative);
                    var unknownStatusCount = CountAbnormalStatuses(member, abnormalStatusCatalog, AbnormalKindUnknown);
                    var cleanseCandidateCount = CountCleanseCandidateAbnormals(member, abnormalStatusCatalog);
                    var mentalCleanseCandidateCount = CountMentalCleanseCandidateAbnormals(member, abnormalStatusCatalog);
                    var needsCleanse = cleanseCandidateCount > 0;
                    var needsMentalCleanse = mentalCleanseCandidateCount > 0;

                    var previousMentalCleanseKnown = previousMentalCleanseByMember.TryGetValue(member.ServerObjectId, out var previousMentalCleanseCount);
                    if (mentalCleanseCandidateCount > 0)
                    {
                        sawMentalCleanseByMember.Add(member.ServerObjectId);
                    }

                    if (previousMentalCleanseKnown &&
                        previousMentalCleanseCount > 0 &&
                        mentalCleanseCandidateCount == 0)
                    {
                        sawMentalCleanseClearedByMember.Add(member.ServerObjectId);
                    }

                    previousMentalCleanseByMember[member.ServerObjectId] = mentalCleanseCandidateCount;

                    Console.WriteLine("SupportProbeMember sample=" + sample.ToString(CultureInfo.InvariantCulture) +
                                      " Member=" + member.Name +
                                      " ServerId=" + member.ServerObjectId.ToString(CultureInfo.InvariantCulture) +
                                      " IsLeader=" + (member.IsLeader ? "yes" : "no") +
                                      " ClassId=" + member.ClassId.ToString(CultureInfo.InvariantCulture) +
                                      " SelectKey=" + selectKey +
                                      " HP=" + member.CurrentHp.ToString(CultureInfo.InvariantCulture) + "/" + member.MaxHp.ToString(CultureInfo.InvariantCulture) +
                                      " HpPercent=" + FormatPercent(member.CurrentHp, member.MaxHp) +
                                      " NeedsHeal=" + (needsHeal ? "yes" : "no") +
                                      " HpDelta=" + hpDelta.ToString(CultureInfo.InvariantCulture) +
                                      " PhysicalCount=" + member.PhysicalCount.ToString(CultureInfo.InvariantCulture) +
                                      " PhysicalIds=" + FormatPhysicalAbnormalIds(member) +
                                      " PositiveCount=" + positiveCount.ToString(CultureInfo.InvariantCulture) +
                                      " PositiveIds=" + FormatAbnormalIdsByKind(member, abnormalStatusCatalog, AbnormalKindPositive) +
                                      " NegativeCount=" + negativeCount.ToString(CultureInfo.InvariantCulture) +
                                      " NegativeIds=" + FormatAbnormalIdsByKind(member, abnormalStatusCatalog, AbnormalKindNegative) +
                                      " UnknownStatusCount=" + unknownStatusCount.ToString(CultureInfo.InvariantCulture) +
                                      " CleanseCandidateCount=" + cleanseCandidateCount.ToString(CultureInfo.InvariantCulture) +
                                      " CleanseCandidateIds=" + FormatCleanseCandidateAbnormalIds(member, abnormalStatusCatalog) +
                                      " MentalCleanseCandidateCount=" + mentalCleanseCandidateCount.ToString(CultureInfo.InvariantCulture) +
                                      " MentalCleanseCandidateIds=" + FormatMentalCleanseCandidateAbnormalIds(member, abnormalStatusCatalog) +
                                      " NeedsCleanse=" + (needsCleanse ? "yes" : "no") +
                                      " NeedsMentalCleanse=" + (needsMentalCleanse ? "yes" : "no") +
                                      " IsSelected=" + (isSelected ? "yes" : "no") +
                                      " PressMode=" + pressMode +
                                      " LiveActor=" + (member.HasLiveActor ? "yes" : "no") +
                                      " DistanceToLocal=" + (member.HasDistanceToLocal ? member.DistanceToLocal.ToString("F2", CultureInfo.InvariantCulture) : "Unknown") +
                                      " VisibilityState=" + (string.IsNullOrWhiteSpace(member.VisibilityState) ? "Unknown" : member.VisibilityState));
                }

                var actionKind = string.Empty;
                var actionKey = string.Empty;
                var actionMember = default(PartyMemberProbeSnapshot);
                var hasAction = false;

                if (autoPressCleanse)
                {
                    actionMember = supportMembers.FirstOrDefault(member =>
                        CountCleanseCandidateAbnormals(member, abnormalStatusCatalog) > 0 &&
                        IsTargetActionReady(member, "cleanse", DateTime.UtcNow));
                    if (actionMember.ServerObjectId != 0)
                    {
                        actionKind = "cleanse";
                        actionKey = cleanseKey;
                        hasAction = true;
                    }
                }

                if (!hasAction && autoPressMentalCleanse)
                {
                    actionMember = supportMembers.FirstOrDefault(member =>
                        CountMentalCleanseCandidateAbnormals(member, abnormalStatusCatalog) > 0 &&
                        IsTargetActionReady(member, "mental_cleanse", DateTime.UtcNow));
                    if (actionMember.ServerObjectId != 0)
                    {
                        actionKind = "mental_cleanse";
                        actionKey = mentalCleanseKey;
                        hasAction = true;
                    }
                }

                if (!hasAction && autoPressHeal)
                {
                    actionMember = supportMembers.FirstOrDefault(member =>
                        member.MaxHp > 0 &&
                        member.CurrentHp > 0 &&
                        member.CurrentHp < member.MaxHp &&
                        IsTargetActionReady(member, "heal", DateTime.UtcNow));
                    if (actionMember.ServerObjectId != 0)
                    {
                        actionKind = "heal";
                        actionKey = healKey;
                        hasAction = true;
                    }
                }

                var canRepeatPress = repeatAutoPress ||
                                     !autoPressAttempted;
                var pressIntervalElapsed = lastAutoPressAt == DateTime.MinValue ||
                                           (DateTime.UtcNow - lastAutoPressAt).TotalMilliseconds >= autoPressIntervalMs;

                if (hasAction &&
                    canRepeatPress &&
                    pressIntervalElapsed)
                {
                    var selectKey = ComputeSelectMemberKey(members, actionMember.ServerObjectId, hasLocal ? liveSummary.LocalServerObjectId : 0);
                    var isSelected = hasLiveSummary &&
                                     liveSummary.LocalTargetServerObjectId != 0 &&
                                     liveSummary.LocalTargetServerObjectId == actionMember.ServerObjectId;
                    autoPressAttempted = true;
                    autoPressCount++;
                    MarkTargetActionAttempt(actionMember, actionKind, DateTime.UtcNow);
                    if (string.Equals(actionKind, "cleanse", StringComparison.OrdinalIgnoreCase))
                    {
                        cleansePressCount++;
                    }
                    else if (string.Equals(actionKind, "mental_cleanse", StringComparison.OrdinalIgnoreCase))
                    {
                        mentalCleansePressCount++;
                    }
                    else if (string.Equals(actionKind, "heal", StringComparison.OrdinalIgnoreCase))
                    {
                        healPressCount++;
                    }

                    lastAutoPressAt = DateTime.UtcNow;
                    lastActionKind = actionKind;
                    lastActionTarget = actionMember.Name + "(" + actionMember.ServerObjectId.ToString(CultureInfo.InvariantCulture) + ")";
                    var pressResult = PressTeamMaintenanceActionAsync(
                        process,
                        gameBase,
                        kmboxIp,
                        kmboxPort,
                        kmboxMac,
                        selectKey,
                        actionKey,
                        actionKind,
                        actionMember.ServerObjectId,
                        kmboxHoldMs,
                        kmboxGapMs,
                        isSelected,
                        selectRetryCount,
                        selectConfirmDelayMs).GetAwaiter().GetResult();
                    autoPressSucceeded = pressResult.Success;
                    autoPressStatus = pressResult.Status;
                    if (pressResult.Success)
                    {
                        autoPressSuccessCount++;
                        if (string.Equals(actionKind, "cleanse", StringComparison.OrdinalIgnoreCase))
                        {
                            cleansePressSuccessCount++;
                        }
                        else if (string.Equals(actionKind, "mental_cleanse", StringComparison.OrdinalIgnoreCase))
                        {
                            mentalCleansePressSuccessCount++;
                        }
                        else if (string.Equals(actionKind, "heal", StringComparison.OrdinalIgnoreCase))
                        {
                            healPressSuccessCount++;
                        }
                    }

                    Console.WriteLine("SupportProbeAction sample=" + sample.ToString(CultureInfo.InvariantCulture) +
                                      " Action=" + actionKind +
                                      " Target=" + actionMember.Name +
                                      " TargetServerId=" + actionMember.ServerObjectId.ToString(CultureInfo.InvariantCulture) +
                                      " TargetPhysicalCount=" + actionMember.PhysicalCount.ToString(CultureInfo.InvariantCulture) +
                                      " TargetPhysicalIds=" + FormatPhysicalAbnormalIds(actionMember) +
                                      " TargetCleanseCandidateIds=" + FormatCleanseCandidateAbnormalIds(actionMember, abnormalStatusCatalog) +
                                      " TargetMentalCleanseCandidateIds=" + FormatMentalCleanseCandidateAbnormalIds(actionMember, abnormalStatusCatalog) +
                                      " TargetHP=" + actionMember.CurrentHp.ToString(CultureInfo.InvariantCulture) + "/" + actionMember.MaxHp.ToString(CultureInfo.InvariantCulture) +
                                      " SelectKey=" + selectKey +
                                      " ActionKey=" + actionKey +
                                      " SkipSelect=" + (isSelected ? "yes" : "no") +
                                      " Success=" + (pressResult.Success ? "yes" : "no") +
                                      " Status=\"" + pressResult.Status + "\"");
                }
                else if (hasAction)
                {
                    Console.WriteLine("SupportProbeAction sample=" + sample.ToString(CultureInfo.InvariantCulture) +
                                      " Action=" + actionKind +
                                      " Target=" + actionMember.Name +
                                      " TargetServerId=" + actionMember.ServerObjectId.ToString(CultureInfo.InvariantCulture) +
                                      " Skipped=yes Reason=\"" + (!canRepeatPress ? "repeat_disabled" : "press_interval") + "\"");
                }

                if (stopOnIncrease &&
                    (sawHealByMember.Count > 0 || sawPhysicalClearedByMember.Count > 0))
                {
                    break;
                }
            }

            var remainingMs = monitorMs - (int)(DateTime.UtcNow - started).TotalMilliseconds;
            if (remainingMs <= 0)
            {
                break;
            }

            Thread.Sleep(Math.Min(intervalMs, remainingMs));
        }

        Console.WriteLine("SupportProbeSummary SawDamage=" + (sawDamageByMember.Count > 0 ? "yes" : "no") +
                          " SawHealAfterDamage=" + (sawHealByMember.Count > 0 ? "yes" : "no") +
                          " SawPhysicalAbnormal=" + (sawPhysicalByMember.Count > 0 ? "yes" : "no") +
                          " SawPhysicalCleared=" + (sawPhysicalClearedByMember.Count > 0 ? "yes" : "no") +
                          " SawMentalCleanseCandidate=" + (sawMentalCleanseByMember.Count > 0 ? "yes" : "no") +
                          " SawMentalCleanseCleared=" + (sawMentalCleanseClearedByMember.Count > 0 ? "yes" : "no") +
                          " AutoPressAttempted=" + (autoPressAttempted ? "yes" : "no") +
                          " AutoPressCount=" + autoPressCount.ToString(CultureInfo.InvariantCulture) +
                          " AutoPressSuccessCount=" + autoPressSuccessCount.ToString(CultureInfo.InvariantCulture) +
                          " CleansePressCount=" + cleansePressCount.ToString(CultureInfo.InvariantCulture) +
                          " CleansePressSuccessCount=" + cleansePressSuccessCount.ToString(CultureInfo.InvariantCulture) +
                          " MentalCleansePressCount=" + mentalCleansePressCount.ToString(CultureInfo.InvariantCulture) +
                          " MentalCleansePressSuccessCount=" + mentalCleansePressSuccessCount.ToString(CultureInfo.InvariantCulture) +
                          " HealPressCount=" + healPressCount.ToString(CultureInfo.InvariantCulture) +
                          " HealPressSuccessCount=" + healPressSuccessCount.ToString(CultureInfo.InvariantCulture) +
                          " AutoPressSucceeded=" + (autoPressSucceeded ? "yes" : "no") +
                          " AutoPressStatus=\"" + autoPressStatus + "\"" +
                          " LastAction=" + lastActionKind +
                          " LastActionTarget=\"" + lastActionTarget + "\"");

        return sawHealByMember.Count > 0 ||
               sawPhysicalClearedByMember.Count > 0 ||
               autoPressSuccessCount > 0
            ? 0
            : 6;
    }

    private static async Task<(bool Success, string Status)> PressTeamMaintenanceActionAsync(
        VmmProcess process,
        ulong gameBase,
        string ip,
        int port,
        string mac,
        string selectKey,
        string actionKey,
        string actionKind,
        uint targetServerObjectId,
        int holdMs,
        int gapMs,
        bool skipSelect,
        int selectRetryCount,
        int selectConfirmDelayMs)
    {
        if (targetServerObjectId == 0)
        {
            return (false, "target_unknown:" + actionKind);
        }

        if (!KmboxKeyPressProbe.TryResolveKeyCode(actionKey, out var actionKeyCode))
        {
            return (false, "unsupported_action_key:" + actionKey);
        }

        var selectKeyCode = 0;
        if (!skipSelect)
        {
            if (string.IsNullOrWhiteSpace(selectKey) ||
                string.Equals(selectKey, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "select_key_unknown");
            }

            if (!KmboxKeyPressProbe.TryResolveKeyCode(selectKey, out selectKeyCode))
            {
                return (false, "unsupported_select_key:" + selectKey);
            }
        }

        try
        {
            using var device = new KmBoxNetDevice(new KmBoxOptions
            {
                IpAddress = ip,
                Port = port,
                Mac = mac,
                CommandTimeoutMs = 1500,
                SendTimeoutMs = 1500,
                ReceiveTimeoutMs = 1500,
                DefaultClickHoldMs = holdMs,
                TypeKeyDelayMs = gapMs
            });

            if (!await device.ConnectAsync().ConfigureAwait(false))
            {
                return (false, "kmbox_connect_failed");
            }

            try
            {
                if (skipSelect)
                {
                    await device.PressKeyAsync(actionKeyCode, holdMs).ConfigureAwait(false);
                    return (true, "pressed:" + actionKey + ":" + actionKind + ":already_selected");
                }

                var lastTarget = 0u;
                var lastReadError = string.Empty;
                for (var attempt = 1; attempt <= selectRetryCount; attempt++)
                {
                    await device.PressKeyAsync(selectKeyCode, holdMs).ConfigureAwait(false);
                    if (selectConfirmDelayMs > 0)
                    {
                        await Task.Delay(selectConfirmDelayMs).ConfigureAwait(false);
                    }

                    if (TryReadLocalTargetServerObjectId(process, gameBase, out _, out var currentTargetServerObjectId, out var targetReadError))
                    {
                        lastTarget = currentTargetServerObjectId;
                        if (currentTargetServerObjectId == targetServerObjectId)
                        {
                            if (gapMs > 0)
                            {
                                await Task.Delay(gapMs).ConfigureAwait(false);
                            }

                            await device.PressKeyAsync(actionKeyCode, holdMs).ConfigureAwait(false);
                            return (true,
                                "pressed:" + selectKey + "," + actionKey + ":" + actionKind + ":target_confirmed:attempt=" +
                                attempt.ToString(CultureInfo.InvariantCulture));
                        }
                    }
                    else
                    {
                        lastReadError = targetReadError;
                    }

                    if (gapMs > 0 && attempt < selectRetryCount)
                    {
                        await Task.Delay(gapMs).ConfigureAwait(false);
                    }
                }

                return (false,
                    "target_confirm_failed:" + actionKind +
                    ":lastTarget=" + lastTarget.ToString(CultureInfo.InvariantCulture) +
                    ":lastError=" + lastReadError);
            }
            finally
            {
                await device.ReleaseAllAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            return (false, "exception:" + ex.GetType().Name + ":" + ex.Message);
        }
    }

    private static string ComputeSelectMemberKey(
        List<PartyMemberProbeSnapshot> members,
        uint targetServerObjectId,
        uint localServerObjectId)
    {
        if (targetServerObjectId == 0)
        {
            return "Unknown";
        }

        if (localServerObjectId != 0 && targetServerObjectId == localServerObjectId)
        {
            return "F1";
        }

        var selectableIndex = 0;
        for (var i = 0; i < members.Count; i++)
        {
            if (localServerObjectId != 0 && members[i].ServerObjectId == localServerObjectId)
            {
                continue;
            }

            if (members[i].ServerObjectId == targetServerObjectId)
            {
                return "F" + (selectableIndex + 2).ToString(CultureInfo.InvariantCulture);
            }

            selectableIndex++;
        }

        return "Unknown";
    }

    private static string FormatHealProbeMemberName(PartyMemberProbeSnapshot member, bool hasMember)
    {
        if (!hasMember || member.ServerObjectId == 0)
        {
            return "Unknown";
        }

        return member.Name + "(" + member.ServerObjectId.ToString(CultureInfo.InvariantCulture) + ")";
    }

    private static string FormatPhysicalAbnormalIds(PartyMemberProbeSnapshot member)
    {
        if (member.Entries.Count == 0 || member.PhysicalCount == 0)
        {
            return "None";
        }

        var ids = member.Entries
            .Where(entry => entry.DispelCategory == AbnormalCategoryPhysical)
            .Select(entry => entry.AbnormalId.ToString(CultureInfo.InvariantCulture) +
                             ":L" + entry.LevelOrStack.ToString(CultureInfo.InvariantCulture))
            .ToArray();

        return ids.Length == 0
            ? "None"
            : string.Join(",", ids);
    }

    private static int CountAbnormalStatuses(
        PartyMemberProbeSnapshot member,
        AbnormalStatusCatalog abnormalStatusCatalog,
        string statusKind)
    {
        var count = 0;
        foreach (var entry in member.Entries)
        {
            if (string.Equals(ClassifyAbnormalStatus(entry, abnormalStatusCatalog), statusKind, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountCleanseCandidateAbnormals(
        PartyMemberProbeSnapshot member,
        AbnormalStatusCatalog abnormalStatusCatalog)
    {
        var count = 0;
        foreach (var entry in member.Entries)
        {
            if (IsCleanseCandidateAbnormal(entry, abnormalStatusCatalog))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountMentalCleanseCandidateAbnormals(
        PartyMemberProbeSnapshot member,
        AbnormalStatusCatalog abnormalStatusCatalog)
    {
        var count = 0;
        foreach (var entry in member.Entries)
        {
            if (IsMentalCleanseCandidateAbnormal(entry, abnormalStatusCatalog))
            {
                count++;
            }
        }

        return count;
    }

    private static string FormatAbnormalIdsByKind(
        PartyMemberProbeSnapshot member,
        AbnormalStatusCatalog abnormalStatusCatalog,
        string statusKind)
    {
        var ids = member.Entries
            .Where(entry => string.Equals(ClassifyAbnormalStatus(entry, abnormalStatusCatalog), statusKind, StringComparison.Ordinal))
            .Select(entry => FormatAbnormalIdWithStaticInfo(entry, abnormalStatusCatalog))
            .ToArray();

        return ids.Length == 0 ? "None" : string.Join(",", ids);
    }

    private static string FormatCleanseCandidateAbnormalIds(
        PartyMemberProbeSnapshot member,
        AbnormalStatusCatalog abnormalStatusCatalog)
    {
        var ids = member.Entries
            .Where(entry => IsCleanseCandidateAbnormal(entry, abnormalStatusCatalog))
            .Select(entry => FormatAbnormalIdWithStaticInfo(entry, abnormalStatusCatalog))
            .ToArray();

        return ids.Length == 0 ? "None" : string.Join(",", ids);
    }

    private static string FormatMentalCleanseCandidateAbnormalIds(
        PartyMemberProbeSnapshot member,
        AbnormalStatusCatalog abnormalStatusCatalog)
    {
        var ids = member.Entries
            .Where(entry => IsMentalCleanseCandidateAbnormal(entry, abnormalStatusCatalog))
            .Select(entry => FormatAbnormalIdWithStaticInfo(entry, abnormalStatusCatalog))
            .ToArray();

        return ids.Length == 0 ? "None" : string.Join(",", ids);
    }

    private static string FormatAbnormalIdWithStaticInfo(
        AbnormalStatusEntry entry,
        AbnormalStatusCatalog abnormalStatusCatalog)
    {
        var text = entry.AbnormalId.ToString(CultureInfo.InvariantCulture) +
                   ":L" + entry.LevelOrStack.ToString(CultureInfo.InvariantCulture);
        if (abnormalStatusCatalog.TryGet(entry.AbnormalId, out var detail) &&
            !string.IsNullOrWhiteSpace(detail.TargetSlot))
        {
            text += ":" + detail.TargetSlot;
        }

        return text;
    }

    private static bool IsCleanseCandidateAbnormal(
        AbnormalStatusEntry entry,
        AbnormalStatusCatalog abnormalStatusCatalog)
    {
        return entry.AbnormalId != 0 &&
               string.Equals(ClassifyAbnormalStatus(entry, abnormalStatusCatalog), AbnormalKindNegative, StringComparison.Ordinal) &&
               IsPhysicalCleanseCategory(entry, abnormalStatusCatalog);
    }

    private static bool IsMentalCleanseCandidateAbnormal(
        AbnormalStatusEntry entry,
        AbnormalStatusCatalog abnormalStatusCatalog)
    {
        return entry.AbnormalId != 0 &&
               string.Equals(ClassifyAbnormalStatus(entry, abnormalStatusCatalog), AbnormalKindNegative, StringComparison.Ordinal) &&
               IsMentalCleanseCategory(entry, abnormalStatusCatalog);
    }

    private static bool IsPhysicalCleanseCategory(
        AbnormalStatusEntry entry,
        AbnormalStatusCatalog abnormalStatusCatalog)
    {
        if (abnormalStatusCatalog.TryGet(entry.AbnormalId, out var detail) &&
            !string.IsNullOrWhiteSpace(detail.DispelCategory))
        {
            var category = NormalizeSkillXmlToken(detail.DispelCategory);
            return string.Equals(category, "debuffphy", StringComparison.Ordinal) ||
                   string.Equals(category, "physicaldebuff", StringComparison.Ordinal) ||
                   string.Equals(category, "physical", StringComparison.Ordinal) ||
                   string.Equals(category, "2", StringComparison.Ordinal);
        }

        return entry.DispelCategory == AbnormalCategoryPhysical;
    }

    private static bool IsMentalCleanseCategory(
        AbnormalStatusEntry entry,
        AbnormalStatusCatalog abnormalStatusCatalog)
    {
        if (!abnormalStatusCatalog.TryGet(entry.AbnormalId, out var detail) ||
            string.IsNullOrWhiteSpace(detail.DispelCategory))
        {
            return false;
        }

        var category = NormalizeSkillXmlToken(detail.DispelCategory);
        return string.Equals(category, "debuffmen", StringComparison.Ordinal) ||
               string.Equals(category, "mentaldebuff", StringComparison.Ordinal) ||
               string.Equals(category, "mental", StringComparison.Ordinal);
    }

    private static string ClassifyAbnormalStatus(
        AbnormalStatusEntry entry,
        AbnormalStatusCatalog abnormalStatusCatalog)
    {
        if (entry.AbnormalId == 0)
        {
            return AbnormalKindUnknown;
        }

        if (!abnormalStatusCatalog.TryGet(entry.AbnormalId, out var detail))
        {
            return AbnormalKindUnknown;
        }

        return detail.StatusKind;
    }

    private static AbnormalStatusCatalog LoadAbnormalStatusCatalog(string[] args)
    {
        var path = ResolveClientSkillsXmlPath(args);
        if (string.IsNullOrWhiteSpace(path))
        {
            return AbnormalStatusCatalog.Failed(string.Empty, "client_skills.xml not found");
        }

        try
        {
            var document = XDocument.Load(path);
            var entries = new Dictionary<uint, AbnormalStatusStaticInfo>();
            foreach (var element in document.Descendants("skill_base_client"))
            {
                var idText = GetSkillXmlValue(element, "id", "skill_id", "skillid");
                if (!uint.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id == 0)
                {
                    continue;
                }

                var info = new AbnormalStatusStaticInfo
                {
                    Id = id,
                    XmlName = GetSkillXmlValue(element, "name", "skill_name", "skillname"),
                    TargetSlot = GetSkillXmlValue(element, "target_slot", "targetslot"),
                    TargetRelationRestriction = GetSkillXmlValue(element, "target_relation_restriction", "targetrelationrestriction"),
                    DispelCategory = GetSkillXmlValue(element, "dispel_category", "dispelcategory"),
                    Effect1Type = GetSkillXmlValue(element, "effect1_type", "effect_1_type", "effect1type"),
                    Effect2Type = GetSkillXmlValue(element, "effect2_type", "effect_2_type", "effect2type"),
                    Effect3Type = GetSkillXmlValue(element, "effect3_type", "effect_3_type", "effect3type"),
                    Effect4Type = GetSkillXmlValue(element, "effect4_type", "effect_4_type", "effect4type")
                };
                info.StatusKind = ClassifyStaticAbnormalStatus(info);
                entries[id] = info;
            }

            return AbnormalStatusCatalog.LoadedFrom(path, entries);
        }
        catch (Exception ex)
        {
            return AbnormalStatusCatalog.Failed(path, ex.GetType().Name + ":" + ex.Message);
        }
    }

    private static string ResolveClientSkillsXmlPath(string[] args)
    {
        var explicitPath = ReadOption(
            args,
            "--client-skills=",
            "ROADHOG_CLIENT_SKILLS_XML",
            "AION_CLIENT_SKILLS_XML",
            string.Empty);
        if (IsReadableFile(explicitPath))
        {
            return explicitPath;
        }

        var memProcFsHome = Environment.GetEnvironmentVariable("MEMPROCFS_HOME");
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(memProcFsHome))
        {
            candidates.Add(Path.Combine(memProcFsHome, "Source", "client_skills.xml"));
            candidates.Add(Path.Combine(memProcFsHome, "client_skills.xml"));
        }

        candidates.Add(Path.Combine(Environment.CurrentDirectory, "Roadhog", "Source", "client_skills.xml"));
        candidates.Add(Path.Combine(Environment.CurrentDirectory, "Source", "client_skills.xml"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "Source", "client_skills.xml"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Roadhog", "Source", "client_skills.xml"));

        foreach (var candidate in candidates)
        {
            if (IsReadableFile(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return string.Empty;
    }

    private static bool IsReadableFile(string path)
    {
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }

    private static string GetSkillXmlValue(XElement element, params string[] names)
    {
        foreach (var name in names)
        {
            foreach (var child in element.Elements())
            {
                if (string.Equals(child.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
                {
                    return child.Value.Trim();
                }
            }
        }

        return string.Empty;
    }

    private static string ClassifyStaticAbnormalStatus(AbnormalStatusStaticInfo info)
    {
        var targetSlot = NormalizeSkillXmlToken(info.TargetSlot);
        if (string.Equals(targetSlot, "debuff", StringComparison.Ordinal) ||
            string.Equals(targetSlot, "1", StringComparison.Ordinal))
        {
            return AbnormalKindNegative;
        }

        if (string.Equals(targetSlot, "buff", StringComparison.Ordinal) ||
            string.Equals(targetSlot, "chant", StringComparison.Ordinal) ||
            string.Equals(targetSlot, "boost", StringComparison.Ordinal) ||
            string.Equals(targetSlot, "0", StringComparison.Ordinal) ||
            string.Equals(targetSlot, "2", StringComparison.Ordinal) ||
            string.Equals(targetSlot, "5", StringComparison.Ordinal))
        {
            return AbnormalKindPositive;
        }

        var relation = NormalizeSkillXmlToken(info.TargetRelationRestriction);
        if (string.Equals(relation, "enemy", StringComparison.Ordinal))
        {
            return AbnormalKindNegative;
        }

        if (string.Equals(relation, "friend", StringComparison.Ordinal) &&
            HasAnyEffectType(info))
        {
            return AbnormalKindPositive;
        }

        return AbnormalKindUnknown;
    }

    private static bool HasAnyEffectType(AbnormalStatusStaticInfo info)
    {
        return !string.IsNullOrWhiteSpace(info.Effect1Type) ||
               !string.IsNullOrWhiteSpace(info.Effect2Type) ||
               !string.IsNullOrWhiteSpace(info.Effect3Type) ||
               !string.IsNullOrWhiteSpace(info.Effect4Type);
    }

    private static string NormalizeSkillXmlToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim()
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    private static bool TryReadLocalTargetServerObjectId(
        VmmProcess process,
        ulong gameBase,
        out uint localServerObjectId,
        out uint targetServerObjectId,
        out string error)
    {
        localServerObjectId = 0;
        targetServerObjectId = 0;
        error = string.Empty;

        if (!TryReadPointer(process, gameBase + EntitySystemPointerRva, out var entitySystem))
        {
            error = "failed to read EntitySystem pointer";
            return false;
        }

        if (!TryReadPointer(process, entitySystem + EntityTreeOffset, out var entityTreeHeader))
        {
            error = "failed to read EntitySystem tree header";
            return false;
        }

        if (!TryReadUInt16(process, gameBase + LocalEntityIdRva, out var localEntityId) || localEntityId == 0)
        {
            error = "failed to read local entity id";
            return false;
        }

        if (!TryFindEntityById(process, entityTreeHeader, localEntityId, out var localEntity))
        {
            error = "local entity not found";
            return false;
        }

        if (!TryResolveActorFromEntity(process, localEntity, 0, out var localActor) ||
            localActor.ServerObjectId == 0)
        {
            error = "local actor not resolved";
            return false;
        }

        localServerObjectId = localActor.ServerObjectId;
        targetServerObjectId = localActor.TargetServerObjectId;
        return true;
    }

    private static bool TryReadVisiblePlayerActors(
        VmmProcess process,
        ulong gameBase,
        ulong entityTreeHeader,
        out Dictionary<uint, LivePlayerActorSnapshot> actorsByServerId,
        out string error)
    {
        actorsByServerId = new Dictionary<uint, LivePlayerActorSnapshot>();
        error = string.Empty;

        if (!TryReadPointer(process, gameBase + ServerObjectTreeRva, out var serverTreeHeader) || serverTreeHeader == 0)
        {
            error = "failed to read ServerObject tree header at Game.dll+0x" + ServerObjectTreeRva.ToString("X", CultureInfo.InvariantCulture);
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

            if (TryReadUInt32(process, node + ServerNodeServerObjectIdOffset, out var serverObjectId) &&
                TryReadUInt16(process, node + ServerNodeEntityIdOffset, out var entityId) &&
                serverObjectId != 0 &&
                entityId != 0 &&
                TryFindEntityById(process, entityTreeHeader, entityId, out var entity) &&
                TryResolveActorFromEntity(process, entity, serverObjectId, out var actor) &&
                actor.ObjectType == ActorPlayerObjectType &&
                actor.ServerObjectId != 0)
            {
                var live = new LivePlayerActorSnapshot
                {
                    EntityId = entityId,
                    Entity = entity,
                    Actor = actor
                };

                if (TryReadEntityPosition(process, entity, out var x, out var y, out var z) &&
                    IsReasonablePosition(x, y, z))
                {
                    live.HasPosition = true;
                    live.X = x;
                    live.Y = y;
                    live.Z = z;
                }

                actorsByServerId[actor.ServerObjectId] = live;
            }

            if (!TryGetNextTreeNode(process, serverTreeHeader, node, out var next) || next == node)
            {
                break;
            }

            node = next;
        }

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
                "proxyManager(vfunc_0xB8, entity+0x" + proxyOffset.ToString("X", CultureInfo.InvariantCulture) + ")",
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
                    "CEntity+0x" + offset.ToString("X", CultureInfo.InvariantCulture) + " nested scan",
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
                    source + "+0x" + offset.ToString("X", CultureInfo.InvariantCulture),
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
        TryReadUInt32(process, actorAddress + ActorTargetServerObjectIdOffset, out actor.TargetServerObjectId);

        if (TryReadUtf16String(process, actorAddress + ActorNameOffset, 64, out var name))
        {
            actor.Name = name;
        }

        return true;
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

    private static void PrintPartyMemberProbeSnapshots(
        List<PartyMemberProbeSnapshot> snapshots,
        bool printAllEntries,
        AbnormalStatusCatalog abnormalStatusCatalog)
    {
        Console.WriteLine("PartyMemberRecords Count=" + snapshots.Count);
        if (snapshots.Count == 0)
        {
            Console.WriteLine("PartyMemberRecords=[]");
            return;
        }

        for (var i = 0; i < snapshots.Count; i++)
        {
            Console.WriteLine(FormatPartyMemberProbeSnapshot(i + 1, snapshots[i], abnormalStatusCatalog));
            if (printAllEntries)
            {
                PrintAbnormalEntries(snapshots[i].Entries, abnormalStatusCatalog);
            }
        }
    }

    private static string FormatPartyMemberProbeSnapshot(
        int index,
        PartyMemberProbeSnapshot snapshot,
        AbnormalStatusCatalog abnormalStatusCatalog)
    {
        return "Party#" + index.ToString("00", CultureInfo.InvariantCulture) +
               " List=" + snapshot.ListName +
               " ListIndex=" + snapshot.ListIndex.ToString(CultureInfo.InvariantCulture) +
               " IsSelf=" + (snapshot.IsSelf ? "yes" : "no") +
               " IsLeader=" + (snapshot.IsLeader ? "yes" : "no") +
               " Node=" + FormatAddress(snapshot.Node) +
               " Member=" + FormatAddress(snapshot.Member) +
               " PartySlot=" + snapshot.PartySlot.ToString(CultureInfo.InvariantCulture) +
               " ServerId=" + snapshot.ServerObjectId.ToString(CultureInfo.InvariantCulture) +
               " Name=\"" + snapshot.Name + "\"" +
               " ClassId=" + snapshot.ClassId.ToString(CultureInfo.InvariantCulture) +
               " Level=" + snapshot.Level.ToString(CultureInfo.InvariantCulture) +
               " HP=" + snapshot.CurrentHp.ToString(CultureInfo.InvariantCulture) + "/" + snapshot.MaxHp.ToString(CultureInfo.InvariantCulture) +
               " HpPercent=" + FormatPercent(snapshot.CurrentHp, snapshot.MaxHp) +
               " Alive=" + FormatPartyAlive(snapshot) +
               " MP=" + snapshot.CurrentMp.ToString(CultureInfo.InvariantCulture) + "/" + snapshot.MaxMp.ToString(CultureInfo.InvariantCulture) +
               " MpPercent=" + FormatPercent(snapshot.CurrentMp, snapshot.MaxMp) +
               " FlightMs=" + snapshot.CurrentFlightTime.ToString(CultureInfo.InvariantCulture) + "/" + snapshot.MaxFlightTime.ToString(CultureInfo.InvariantCulture) +
               " LiveActor=" + FormatLiveActor(snapshot) +
               " Area=0x" + snapshot.AreaField0.ToString("X", CultureInfo.InvariantCulture) + "/0x" + snapshot.AreaField1.ToString("X", CultureInfo.InvariantCulture) +
               " CachedPositionCandidate=" + FormatPartyCachedPosition(snapshot) +
               " Flags=" + FormatPartyMemberFlags(snapshot) +
               " ControlMask=0x" + snapshot.ControlStatusMask.ToString("X", CultureInfo.InvariantCulture) +
               " HasAbnormalBlock=" + (snapshot.HasAbnormalBlock ? "yes" : "no") +
               " RawAbnormalCount=" + snapshot.RawAbnormalCount.ToString(CultureInfo.InvariantCulture) +
               " EntryCount=" + snapshot.Entries.Count.ToString(CultureInfo.InvariantCulture) +
               " PhysicalCount=" + snapshot.PhysicalCount.ToString(CultureInfo.InvariantCulture) +
               " PositiveCount=" + CountAbnormalStatuses(snapshot, abnormalStatusCatalog, AbnormalKindPositive).ToString(CultureInfo.InvariantCulture) +
               " PositiveIds=" + FormatAbnormalIdsByKind(snapshot, abnormalStatusCatalog, AbnormalKindPositive) +
               " NegativeCount=" + CountAbnormalStatuses(snapshot, abnormalStatusCatalog, AbnormalKindNegative).ToString(CultureInfo.InvariantCulture) +
               " NegativeIds=" + FormatAbnormalIdsByKind(snapshot, abnormalStatusCatalog, AbnormalKindNegative) +
               " UnknownStatusCount=" + CountAbnormalStatuses(snapshot, abnormalStatusCatalog, AbnormalKindUnknown).ToString(CultureInfo.InvariantCulture) +
               " CleanseCandidateCount=" + CountCleanseCandidateAbnormals(snapshot, abnormalStatusCatalog).ToString(CultureInfo.InvariantCulture) +
               " CleanseCandidateIds=" + FormatCleanseCandidateAbnormalIds(snapshot, abnormalStatusCatalog) +
               " MentalCleanseCandidateCount=" + CountMentalCleanseCandidateAbnormals(snapshot, abnormalStatusCatalog).ToString(CultureInfo.InvariantCulture) +
               " MentalCleanseCandidateIds=" + FormatMentalCleanseCandidateAbnormalIds(snapshot, abnormalStatusCatalog) +
               " UpdateTime=0x" + snapshot.UpdateTime.ToString("X", CultureInfo.InvariantCulture);
    }

    private static string FormatPartyGlobalProbeSnapshot(
        PartyGlobalProbeSnapshot snapshot,
        List<PartyMemberProbeSnapshot> members,
        uint localServerObjectId,
        bool hasLocalServerObjectId)
    {
        var leaderName = "Unknown";
        var matchedMember = members.FirstOrDefault(member =>
            snapshot.HasLeaderServerObjectId &&
            snapshot.LeaderServerObjectId != 0 &&
            member.ServerObjectId == snapshot.LeaderServerObjectId);

        if (matchedMember.ServerObjectId != 0 && !string.IsNullOrWhiteSpace(matchedMember.Name))
        {
            leaderName = matchedMember.Name;
        }

        var isLocalLeader = hasLocalServerObjectId &&
                            snapshot.HasLeaderServerObjectId &&
                            snapshot.LeaderServerObjectId != 0 &&
                            snapshot.LeaderServerObjectId == localServerObjectId;

        return "PartyGlobals" +
               " PartyId=" + (snapshot.HasPartyId ? snapshot.PartyId.ToString(CultureInfo.InvariantCulture) : "Unknown") +
               " Flags=0x" + (snapshot.HasPartyFlags ? snapshot.PartyFlags.ToString("X", CultureInfo.InvariantCulture) : "Unknown") +
               " PrimaryCount=" + (snapshot.HasPrimaryPartyCount ? snapshot.PrimaryPartyCount.ToString(CultureInfo.InvariantCulture) : "Unknown") +
               " LeaderServerId=" + (snapshot.HasLeaderServerObjectId ? snapshot.LeaderServerObjectId.ToString(CultureInfo.InvariantCulture) : "Unknown") +
               " LeaderName=\"" + leaderName + "\"" +
               " LocalIsLeader=" + (hasLocalServerObjectId ? (isLocalLeader ? "yes" : "no") : "Unknown");
    }

    private static string FormatLiveActorProbeSummary(LiveActorProbeSummary summary)
    {
        return "LiveActorProbe=ok" +
               " LocalServerId=" + summary.LocalServerObjectId.ToString(CultureInfo.InvariantCulture) +
               " LocalName=\"" + summary.LocalName + "\"" +
               " LocalEntityId=" + summary.LocalEntityId.ToString(CultureInfo.InvariantCulture) +
               " LocalEntity=" + FormatAddress(summary.LocalEntity) +
               " LocalActor=" + FormatAddress(summary.LocalActor) +
               " LocalPosition=" + FormatLivePosition(summary.HasLocalPosition, summary.LocalX, summary.LocalY, summary.LocalZ) +
               " VisiblePlayerActors=" + summary.VisiblePlayerActorCount.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatLiveActor(PartyMemberProbeSnapshot snapshot)
    {
        if (!snapshot.HasLiveActor)
        {
            return "no VisibilityState=" + (string.IsNullOrWhiteSpace(snapshot.VisibilityState) ? "Unknown" : snapshot.VisibilityState);
        }

        return "yes" +
               " Actor=" + FormatAddress(snapshot.LiveActor) +
               " Entity=" + FormatAddress(snapshot.LiveEntity) +
               " ActorName=\"" + snapshot.LiveActorName + "\"" +
               " Position=" + FormatLivePosition(snapshot.HasLivePosition, snapshot.LiveX, snapshot.LiveY, snapshot.LiveZ) +
               " DistanceToLocal=" + (snapshot.HasDistanceToLocal ? snapshot.DistanceToLocal.ToString("F2", CultureInfo.InvariantCulture) : "Unknown") +
               " VisibilityState=" + (string.IsNullOrWhiteSpace(snapshot.VisibilityState) ? "Unknown" : snapshot.VisibilityState) +
               " LiveTargetId=" + snapshot.LiveTargetServerObjectId.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatLivePosition(bool hasPosition, float x, float y, float z)
    {
        if (!hasPosition)
        {
            return "Unknown";
        }

        return "X=" + x.ToString("F2", CultureInfo.InvariantCulture) +
               " Y=" + y.ToString("F2", CultureInfo.InvariantCulture) +
               " Z=" + z.ToString("F2", CultureInfo.InvariantCulture) +
               " Source=LiveActor";
    }

    private static string FormatPercent(uint current, uint max)
    {
        if (max == 0)
        {
            return "Unknown";
        }

        return (current * 100.0 / max).ToString("F1", CultureInfo.InvariantCulture) + "%";
    }

    private static string FormatPartyAlive(PartyMemberProbeSnapshot snapshot)
    {
        if (snapshot.MaxHp == 0 && snapshot.CurrentHp == 0)
        {
            return "Unknown";
        }

        return snapshot.CurrentHp == 0 ? "Dead" : "Alive";
    }

    private static string FormatPartyCachedPosition(PartyMemberProbeSnapshot snapshot)
    {
        return "X=" + snapshot.CachedX.ToString("F2", CultureInfo.InvariantCulture) +
               " Y=" + snapshot.CachedY.ToString("F2", CultureInfo.InvariantCulture) +
               " Z=" + snapshot.CachedZ.ToString("F2", CultureInfo.InvariantCulture) +
               " Source=PartyMemberRecordDiagnosticOnly";
    }

    private static string FormatPartyMemberFlags(PartyMemberProbeSnapshot snapshot)
    {
        return "Data=0x" + snapshot.DataFlags.ToString("X2", CultureInfo.InvariantCulture) +
               " FlightArea=0x" + snapshot.FlightAreaFlag.ToString("X2", CultureInfo.InvariantCulture) +
               " Flight=0x" + snapshot.FlightFlags.ToString("X2", CultureInfo.InvariantCulture) +
               " Runtime=0x" + snapshot.RuntimeState.ToString("X2", CultureInfo.InvariantCulture);
    }

    private static void PrintAbnormalEntries(
        List<AbnormalStatusEntry> entries,
        AbnormalStatusCatalog abnormalStatusCatalog)
    {
        if (entries.Count == 0)
        {
            Console.WriteLine("  AbnormalEntries=[]");
            return;
        }

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var statusKind = ClassifyAbnormalStatus(entry, abnormalStatusCatalog);
            var detail = abnormalStatusCatalog.TryGet(entry.AbnormalId, out var staticInfo)
                ? staticInfo
                : default;
            Console.WriteLine(
                "  Abnormal#" + (i + 1).ToString("00", CultureInfo.InvariantCulture) +
                " Address=" + FormatAddress(entry.Address) +
                " Field00=0x" + entry.Field00.ToString("X", CultureInfo.InvariantCulture) +
                " Id=" + entry.AbnormalId.ToString(CultureInfo.InvariantCulture) +
                " DispelCategory=" + entry.DispelCategory.ToString(CultureInfo.InvariantCulture) +
                " StatusKind=" + statusKind +
                " XmlName=\"" + (detail is null ? string.Empty : detail.XmlName) + "\"" +
                " TargetSlot=\"" + (detail is null ? string.Empty : detail.TargetSlot) + "\"" +
                " XmlDispelCategory=\"" + (detail is null ? string.Empty : detail.DispelCategory) + "\"" +
                " Relation=\"" + (detail is null ? string.Empty : detail.TargetRelationRestriction) + "\"" +
                " TimeOrSource=0x" + entry.TimeOrSource.ToString("X", CultureInfo.InvariantCulture) +
                " LevelOrStack=" + entry.LevelOrStack.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static bool TryReadByte(VmmProcess process, ulong address, out byte value)
    {
        value = 0;
        return TryReadBytes(process, address, 1, out var buffer) && (value = buffer[0]) >= 0;
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
        if (!TryReadBytes(process, address, 2, out var buffer))
        {
            return false;
        }

        value = BitConverter.ToUInt16(buffer, 0);
        return true;
    }

    private static bool TryReadInt16(VmmProcess process, ulong address, out short value)
    {
        value = 0;
        if (!TryReadBytes(process, address, 2, out var buffer))
        {
            return false;
        }

        value = BitConverter.ToInt16(buffer, 0);
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

    private static bool TryReadSingle(VmmProcess process, ulong address, out float value)
    {
        value = 0;
        if (!TryReadBytes(process, address, 4, out var buffer))
        {
            return false;
        }

        value = BitConverter.ToSingle(buffer, 0);
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

    private static bool IsReasonablePosition(float x, float y, float z)
    {
        return float.IsFinite(x) &&
               float.IsFinite(y) &&
               float.IsFinite(z) &&
               Math.Abs(x) < 1_000_000 &&
               Math.Abs(y) < 1_000_000 &&
               Math.Abs(z) < 1_000_000;
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

    private sealed class AbnormalStatusCatalog
    {
        private readonly Dictionary<uint, AbnormalStatusStaticInfo> byId;

        private AbnormalStatusCatalog(
            string sourcePath,
            string error,
            Dictionary<uint, AbnormalStatusStaticInfo> byId)
        {
            SourcePath = sourcePath;
            Error = error;
            this.byId = byId;
        }

        public string SourcePath { get; }

        public string Error { get; }

        public bool Loaded => string.IsNullOrWhiteSpace(Error);

        public int Count => byId.Count;

        public static AbnormalStatusCatalog LoadedFrom(
            string sourcePath,
            Dictionary<uint, AbnormalStatusStaticInfo> byId)
        {
            return new AbnormalStatusCatalog(sourcePath, string.Empty, byId);
        }

        public static AbnormalStatusCatalog Failed(string sourcePath, string error)
        {
            return new AbnormalStatusCatalog(sourcePath, error, new Dictionary<uint, AbnormalStatusStaticInfo>());
        }

        public bool TryGet(uint abnormalId, out AbnormalStatusStaticInfo info)
        {
            if (byId.TryGetValue(abnormalId, out var value))
            {
                info = value;
                return true;
            }

            info = null!;
            return false;
        }
    }

    private sealed class AbnormalStatusStaticInfo
    {
        public uint Id;
        public string XmlName = string.Empty;
        public string TargetSlot = string.Empty;
        public string TargetRelationRestriction = string.Empty;
        public string DispelCategory = string.Empty;
        public string Effect1Type = string.Empty;
        public string Effect2Type = string.Empty;
        public string Effect3Type = string.Empty;
        public string Effect4Type = string.Empty;
        public string StatusKind = AbnormalKindUnknown;
    }

    private struct AbnormalStatusEntry
    {
        public ulong Address;
        public uint Field00;
        public uint AbnormalId;
        public uint DispelCategory;
        public uint TimeOrSource;
        public ushort LevelOrStack;
    }

    private struct PartyMemberProbeSnapshot
    {
        public string ListName;
        public int ListIndex;
        public bool IsSelf;
        public bool IsLeader;
        public ulong Node;
        public ulong Member;
        public uint PartySlot;
        public uint ServerObjectId;
        public uint MaxHp;
        public uint CurrentHp;
        public uint MaxMp;
        public uint CurrentMp;
        public uint MaxFlightTime;
        public uint CurrentFlightTime;
        public uint AreaField0;
        public uint AreaField1;
        public float CachedX;
        public float CachedY;
        public float CachedZ;
        public byte ClassId;
        public byte Level;
        public byte DataFlags;
        public byte FlightAreaFlag;
        public byte FlightFlags;
        public byte RuntimeState;
        public string Name;
        public ulong ControlStatusMask;
        public bool HasAbnormalBlock;
        public short RawAbnormalCount;
        public uint UpdateTime;
        public List<AbnormalStatusEntry> Entries;
        public int PhysicalCount;
        public bool HasLiveActor;
        public ulong LiveActor;
        public ulong LiveEntity;
        public string LiveActorName;
        public uint LiveTargetServerObjectId;
        public bool HasLivePosition;
        public float LiveX;
        public float LiveY;
        public float LiveZ;
        public bool HasDistanceToLocal;
        public double DistanceToLocal;
        public string VisibilityState;
    }

    private struct PartyGlobalProbeSnapshot
    {
        public bool HasPartyId;
        public uint PartyId;
        public bool HasPartyFlags;
        public uint PartyFlags;
        public bool HasLeaderServerObjectId;
        public uint LeaderServerObjectId;
        public bool HasPrimaryPartyCount;
        public ulong PrimaryPartyCount;
    }

    private struct LiveActorProbeSummary
    {
        public ushort LocalEntityId;
        public uint LocalServerObjectId;
        public uint LocalTargetServerObjectId;
        public string LocalName;
        public ulong LocalEntity;
        public ulong LocalActor;
        public bool HasLocalPosition;
        public float LocalX;
        public float LocalY;
        public float LocalZ;
        public int VisiblePlayerActorCount;
    }

    private struct LivePlayerActorSnapshot
    {
        public ushort EntityId;
        public ulong Entity;
        public ActorInfo Actor;
        public bool HasPosition;
        public float X;
        public float Y;
        public float Z;
    }

    private struct ActorInfo
    {
        public ulong Actor;
        public ulong Entity;
        public uint ObjectType;
        public uint ServerObjectId;
        public uint TargetServerObjectId;
        public string Name;
        public string ResolveSource;
    }
}
