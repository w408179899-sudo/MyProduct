using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Gathering;
using MemProcVmm = Vmmsharp.Vmm;
using Vmmsharp;

namespace Roadhog.Infrastructure.Vmm;

public sealed class AionVmmGameApi : IRoadhogScopedGameApi, IRoadhogScopedPartyGameApi, IRoadhogScopedTacticsSignGameApi, IRoadhogScopedChannelGameApi, IInventoryWindowGameApi, IInventoryMoneyGameApi, IInventoryCapacityGameApi
#if DEBUG
    , IRoadhogApiAddressProbe
#endif
{
    private static readonly TimeSpan VmmReconnectDelay = TimeSpan.FromSeconds(5);
    private const int PlayerReadFailuresBeforeReconnect = 3;
    private const uint VmmReadFlagNoCache = 0x00000001;
    private const ulong VmmConfigTickPeriod = 0x2000000400000000UL;
    private const ulong VmmConfigReadCacheTicks = 0x2000000500000000UL;
    private const ulong VmmConfigTlbCacheTicks = 0x2000000600000000UL;
    private const ulong VmmConfigProcCacheTicksPartial = 0x2000000700000000UL;
    private const ulong VmmConfigProcCacheTicksTotal = 0x2000000800000000UL;
    private const ulong TargetVmmTickPeriodMs = 50UL;
    private const ulong TargetVmmReadCacheMs = 150UL;
    private const ulong TargetVmmReadCacheTicks = 3UL;
    private const ulong FallbackVmmTickPeriodMs = 100UL;

    private const ulong EntitySystemPointerRva = 0x94C7B0;
    private const ulong ServerObjectTreeRva = 0xD6CAC0;
    private const ulong PartyIdRva = 0xD66930;
    private const ulong PartyFlagsRva = 0xD66934;
    private const ulong PartyLeaderServerObjectIdRva = 0xD66938;
    private const ulong PrimaryPartyListRva = 0xD66960;
    private const ulong PrimaryPartyCountRva = 0xD66968;
    private const ulong SecondaryPartyListRva = 0xD669C8;
    private const ulong TacticsSignTableRva = 0xD668E0;
    private const int TacticsSignCount = 16;
    private const ulong CurrentMapContextPointerRva = 0xD647D0;
    private const ulong CurrentMapIdOffset = 0x20DC;
    private const ulong CurrentChannelIndexRva = 0xD71CC0;
    private const ulong CurrentChannelCountRva = 0xD71CC4;
    private const ulong LocalEntityIdRva = 0xD6CB18;
    private const ulong LocalMaxHpRva = 0xD71BC4;
    private const ulong LocalCurrentHpRva = 0xD71BC8;
    private const ulong LocalMaxMpRva = 0xD71BCC;
    private const ulong LocalCurrentMpRva = 0xD71BD0;
    private const ulong LocalCurrentDpRva = 0xD71BD6;
    private const ulong CameraPitchRva = 0xD65B84;
    private const ulong CameraRollRva = 0xD65B88;
    private const ulong CameraYawRva = 0xD65B8C;
    private const ulong SpecialCameraModeRva = 0xD6CC48;
    private const ulong SpecialCameraPitchRva = 0xD6CC58;
    private const ulong SpecialCameraRollRva = 0xD6CC5C;
    private const ulong SpecialCameraYawRva = 0xD6CC60;
    private const ulong SkillManagerGlobalRva = 0xD4B020;
    private const ulong LearnedSkillTreeOffset = 0x830;
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
    private const ulong ListNodeNextOffset = 0x00;
    private const ulong ListNodePrevOffset = 0x08;
    private const ulong ListNodeValueOffset = 0x10;

    private const ulong EntityTreeOffset = 0x58;
    private const ulong EntityTypeOffset = 0x122;
    private const ulong EntityPositionFlagsOffset = 0xF0;
    private const uint EntityUseAlternatePositionFlag = 0x400;
    private const ulong EntityWorldPositionOffset = 0x4E4;
    private const ulong EntityWorldAnglesOffset = 0x518;
    private const ulong EntityLocalPositionOffset = 0x524;
    private const ulong EntityPositionVfuncOffset = 0x08;
    private const ulong EntityProxyManagerVfuncOffset = 0xB8;
    private const ulong EntitySystemGetEntityVfuncOffset = 0x30;

    private const ulong ServerNodeServerObjectIdOffset = 0x1C;
    private const ulong ServerNodeEntityIdOffset = 0x20;
    private const ushort EntityTypeNpc = 3;

    private const ulong ActorEntityOffset = 0x08;
    private const ulong ActorObjectTypeOffset = 0x20;
    private const uint ActorPlayerObjectType = 1;
    private const ulong ActorServerObjectIdOffset = 0x2C;
    private const ulong ActorNpcTemplateIdOffset = 0x30;
    private const ulong ActorStanceFlagsOffset = 0x34;
    private const ulong ActorLevelOffset = 0x3E;
    private const ulong ActorHpPercentOffset = 0x40;
    private const ulong ActorNameOffset = 0x42;
    private const ulong ActorSummonOwnerServerObjectIdOffset = 0xFC;
    private const ulong ActorGatherInteractionRadiusOffset = 0x168;
    private const ulong ActorGatherSpawnPositionOffset = 0x19C;
    private const ulong ActorInteractionStateOffset = 0x1CC;
    private const ulong ActorClassIdOffset = 0x228;
    private const ulong ActorMotionModeOffset = 0x2D0;
    private const ulong ActorTargetServerObjectIdOffset = 0x358;
    private const ulong ActorGatherSourceIdCandidateOffset = 0x500;
    private const ulong ActorGatherActionStateOffset = 0xAB0;
    private const ulong ActorGatherActionIdOffset = 0xAB4;
    private const ulong CurrentGatherSourceIdRva = 0xD68CE8;
    private const ulong CurrentGatherTargetEntityRva = 0xD68CF0;
    private const ulong CurrentGatherSkillIdRva = 0xD68CF8;
    private const ulong DlgGatheringPointerRva = 0xD63E38;
    private const ulong DlgGatheringFlagsOffset = 0x28;
    private const ulong DlgGatheringVisibleMask = 0x01;
    private const ulong DlgGatheringSuccessGaugeOffset = 0x4E8;
    private const ulong DlgGatheringFailureGaugeOffset = 0x500;
    private const ulong GatherGaugeMaximumOffset = 0x300;
    private const ulong GatherGaugeDisplayedOffset = 0x308;
    private const ulong GatherGaugeTargetOffset = 0x310;
    private const ulong ActorCurrentSummonedPetServerObjectIdOffset = 0xFA0;
    private const ulong ActorAbnormalStatusBeginOffset = 0xF18;
    private const ulong ActorAbnormalStatusEndOffset = 0xF20;
    private const ulong ActorAbnormalCategory2CountOffset = 0xF38;
    private const ulong ActorMaxHpOffset = 0x11A0;
    private const ulong ActorCurrentHpOffset = 0x11A4;
    private const ulong ActorLootableFlagOffset = 0x11E0;
    private const uint ActorGatherObjectType = 7;
    private const ulong AbnormalStatusEntrySize = 0x12;
    private const int MaxActorAbnormalStatusEntries = 512;
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

    private const ulong InventoryManagerGlobalRva = SkillManagerGlobalRva;
    private const ulong InventoryCurrentMoneyOffset = 0x770;
    private const ulong InventoryMoneyInstanceIdOffset = 0x778;
    private const ulong InventoryCapacityOffset = 0x77C;
    private const ulong InventoryItemTreeHeaderOffset = 0x780;
    private const ulong InventoryItemTreeCountOffset = 0x788;
    private const ulong InventoryEquipmentIdsOffset = 0x790;
    private const int InventoryEquipmentIdCount = 32;
    private const int InventorySlotsPerPage = 27;
    private const int InventoryColumnsPerPage = 9;
    private const ulong InventoryNodeInstanceIdOffset = 0x20;
    private const ulong InventoryNodeItemOffset = 0x28;
    private const ulong InventoryItemInstanceIdOffset = 0x08;
    private const ulong InventoryItemTemplateIdOffset = 0x0C;
    private const ulong InventoryItemCountOffset = 0x10;
    private const ulong InventoryItemNameOffset = 0x18;
    private const ulong InventoryItemTypeOffset = 0x60;
    private const ulong InventoryItemEquipmentMaskOffset = 0x74;
    private const ulong InventoryItemVendorSellUnitPriceOffset = 0x80;
    private const ulong InventoryItemSlotOffset = 0x4F6;
    private const ulong ItemStaticIndexRva = 0xD75428;
    private const ulong StaticResolverChunkListRva = 0xD4E500;
    private const ulong ItemStaticRecordIdOffset = 0x000;
    private const int ItemStaticRecordQualityRankOffset = 0x1E1;
    private const int StaticResolverEntrySize = 0x10;
    private const int StaticResolverPackedHandleOffset = 0x08;
    private const int StaticResolverPackedChunkShift = 14;
    private const uint StaticResolverPackedOffsetMask = 0x3FFF;
    private const uint MaxStaticResolverEntries = 2_000_000;
    private const uint MaxStaticChunkCompressedBytes = 4 * 1024 * 1024;
    private const uint MaxStaticChunkUncompressedBytes = 16 * 1024 * 1024;

    private const ulong DlgInventoryDialog27MethodRva = 0x1C66F0;
    private const ulong DlgInventoryDialog28MethodRva = 0x1CBFB0;
    private const ulong DlgInventoryDialogTableRva = 0xD639A0;
    private const ulong DlgInventoryDialog27PointerRva = DlgInventoryDialogTableRva + (27UL * 8UL);
    private const ulong DlgInventoryDialog28PointerRva = DlgInventoryDialogTableRva + (28UL * 8UL);
    private const ulong DlgInventoryWidgetFlagsOffset = 0x28;
    private const ulong DlgInventoryVisibleMask = 0x01;
    private const ulong DlgInventoryPageDirtyFlagBaseOffset = 0x585;
    private const ulong DlgInventoryWindowRectOffset = 0x58;
    private const ulong DlgInventoryRootWidgetOffset = 0x4D8;
    private const int DlgInventoryVtableBackSlots = 256;
    private const ulong RootWidgetRectScanBytes = 0x800;
    private const ulong RootWidgetRectScanStep = 0x08;
    private const string RootWidgetRectOffsetEnvironmentVariable = "ROADHOG_INVENTORY_ROOT_WIDGET_RECT_OFFSET";
    private const uint InventoryUiMinAllocationSize = 0x400;
    private const uint InventoryUiMaxAllocationSize = 0x3000;
    private const ulong InventoryUiVadScanBytes = 1024UL * 1024UL * 1024UL;
    private const int InventoryUiObjectScanLimit = 32;

    private readonly AionVmmGameApiOptions _options;
    private readonly IRoadhogLogger _logger;
    private readonly Dictionary<string, VmmConnection> _connections = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _connectionRetryNotBefore = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _playerReadFailureCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, InventoryWindowCandidate> _inventoryWindowCandidateCache = new(StringComparer.OrdinalIgnoreCase);
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

    public Task<OperationResult<SummonedPetSnapshot>> ReadSummonedPetAsync(CancellationToken cancellationToken = default)
    {
        var context = new GameApiReadContext(string.Empty, 0, string.Empty, string.Empty);
        return ReadSummonedPetAsync(context, cancellationToken);
    }

    public Task<OperationResult<SummonedPetSnapshot>> ReadSummonedPetAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ReadSummonedPetCore(context), cancellationToken);
    }

    public Task<OperationResult<SummonedPetRosterSnapshot>> ReadSummonedPetRosterAsync(
        CancellationToken cancellationToken = default)
    {
        var context = new GameApiReadContext(string.Empty, 0, string.Empty, string.Empty);
        return ReadSummonedPetRosterAsync(context, cancellationToken);
    }

    public Task<OperationResult<SummonedPetRosterSnapshot>> ReadSummonedPetRosterAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ReadSummonedPetRosterCore(context), cancellationToken);
    }

    public Task<OperationResult<PartySnapshot>> ReadPartyAsync(CancellationToken cancellationToken = default)
    {
        var context = new GameApiReadContext(string.Empty, 0, string.Empty, string.Empty);
        return ReadPartyAsync(context, cancellationToken);
    }

    public Task<OperationResult<PartySnapshot>> ReadPartyAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ReadPartyCore(context), cancellationToken);
    }

    public Task<OperationResult<TacticsSignSnapshot>> ReadTacticsSignsAsync(
        CancellationToken cancellationToken = default)
    {
        var context = new GameApiReadContext(string.Empty, 0, string.Empty, string.Empty);
        return ReadTacticsSignsAsync(context, cancellationToken);
    }

    public Task<OperationResult<TacticsSignSnapshot>> ReadTacticsSignsAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ReadTacticsSignsCore(context), cancellationToken);
    }

    public Task<OperationResult<ChannelSnapshot>> ReadChannelAsync(
        CancellationToken cancellationToken = default)
    {
        var context = new GameApiReadContext(string.Empty, 0, string.Empty, string.Empty);
        return ReadChannelAsync(context, cancellationToken);
    }

    public Task<OperationResult<ChannelSnapshot>> ReadChannelAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ReadChannelCore(context), cancellationToken);
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

    public Task<OperationResult<LockedTargetAbnormalStatusSnapshot>> ReadLockedTargetAbnormalStatusesAsync(
        CancellationToken cancellationToken = default)
    {
        var context = new GameApiReadContext(string.Empty, 0, string.Empty, string.Empty);
        return ReadLockedTargetAbnormalStatusesAsync(context, cancellationToken);
    }

    public Task<OperationResult<LockedTargetAbnormalStatusSnapshot>> ReadLockedTargetAbnormalStatusesAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ReadLockedTargetAbnormalStatusesCore(context), cancellationToken);
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
        var context = new GameApiReadContext(string.Empty, 0, string.Empty, string.Empty);
        return ReadInventoryAsync(context, cancellationToken);
    }

    public Task<OperationResult<IReadOnlyList<InventoryItemSnapshot>>> ReadInventoryAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ReadInventoryCore(context), cancellationToken);
    }

    public Task<OperationResult<ulong>> ReadInventoryMoneyAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ReadInventoryMoneyCore(context), cancellationToken);
    }

    public Task<OperationResult<int>> ReadInventoryCapacityAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ReadInventoryCapacityCore(context), cancellationToken);
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

    public Task<OperationResult<GatherSnapshot>> ReadGatherSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var context = new GameApiReadContext(string.Empty, 0, string.Empty, string.Empty);
        return ReadGatherSnapshotAsync(context, cancellationToken);
    }

    public Task<OperationResult<GatherSnapshot>> ReadGatherSnapshotAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ReadGatherSnapshotCore(context), cancellationToken);
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

    public Task<OperationResult<InventoryWindowSnapshot>> ReadInventoryWindowAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        return ReadInventoryWindowAsync(
            context,
            InventoryWindowRectSource.LegacyDialogRect,
            cancellationToken);
    }

    public Task<OperationResult<InventoryWindowSnapshot>> ReadInventoryWindowAsync(
        GameApiReadContext context,
        InventoryWindowRectSource rectSource,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ReadInventoryWindowCore(context, rectSource), cancellationToken);
    }

#if DEBUG
    public Task<OperationResult<IReadOnlyList<GameApiAddressProbeResult>>> ProbeAddressesAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ProbeAddressesCore(context), cancellationToken);
    }

    private OperationResult<IReadOnlyList<GameApiAddressProbeResult>> ProbeAddressesCore(GameApiReadContext context)
    {
        try
        {
            var connection = GetOrCreateConnection(context.VmmDeviceName);
            lock (connection.SyncRoot)
            {
                if (!TryResolveProcess(connection.Vmm, context, out var process, out var processError))
                {
                    return OperationResult<IReadOnlyList<GameApiAddressProbeResult>>.Fail(processError);
                }

                var moduleName = ResolveModuleName();
                var gameBase = process.GetModuleBase(moduleName);
                if (gameBase == 0)
                {
                    return OperationResult<IReadOnlyList<GameApiAddressProbeResult>>.Fail("Module not found: " + moduleName);
                }

                var managerSlot = gameBase + SkillManagerGlobalRva;
                var hasManager = TryReadPointer(process, managerSlot, out var manager) && manager != 0;
                var checks = new List<GameApiAddressProbeResult>(GameApiAddressProbeResult.RequiredCheckNames.Count)
                {
                    ProbeEntitySystemPointerAddress(process, gameBase),
                    ProbePointerAddress(process, "Address.ServerObjectTree", gameBase, ServerObjectTreeRva),
                    ProbeUInt32Address(process, "Address.PartyId", gameBase, PartyIdRva),
                    ProbeUInt32Address(process, "Address.PartyFlags", gameBase, PartyFlagsRva),
                    ProbeUInt32Address(process, "Address.PartyLeaderServerObjectId", gameBase, PartyLeaderServerObjectIdRva),
                    ProbePointerAddress(process, "Address.PrimaryPartyList", gameBase, PrimaryPartyListRva, allowZero: true),
                    ProbeUInt64Address(process, "Address.PrimaryPartyCount", gameBase, PrimaryPartyCountRva),
                    ProbePointerAddress(process, "Address.SecondaryPartyList", gameBase, SecondaryPartyListRva, allowZero: true),
                    ProbePartyFirstMemberRecordAddress(process, gameBase),
                    ProbePartyLiveActorPositionAddress(process, gameBase),
                    ProbeUInt16Address(process, "Address.LocalEntityId", gameBase, LocalEntityIdRva),
                    ProbeUInt16Address(process, "Address.LocalTargetEntityId", gameBase, LocalEntityIdRva + 0x02),
                    ProbeUInt32Address(process, "Address.LocalMaxHp", gameBase, LocalMaxHpRva),
                    ProbeUInt32Address(process, "Address.LocalCurrentHp", gameBase, LocalCurrentHpRva),
                    ProbeUInt32Address(process, "Address.LocalMaxMp", gameBase, LocalMaxMpRva),
                    ProbeUInt32Address(process, "Address.LocalCurrentMp", gameBase, LocalCurrentMpRva),
                    ProbeUInt16Address(process, "Address.LocalCurrentDp", gameBase, LocalCurrentDpRva),
                    ProbeSingleAddress(process, "Address.CameraPitch", gameBase, GetCameraPitchRva()),
                    ProbeSingleAddress(process, "Address.CameraRoll", gameBase, GetCameraRollRva()),
                    ProbeSingleAddress(process, "Address.CameraYaw", gameBase, GetCameraYawRva()),
                    ProbeUInt16Address(process, "Address.SpecialCameraMode", gameBase, SpecialCameraModeRva),
                    ProbeSingleAddress(process, "Address.SpecialCameraPitch", gameBase, SpecialCameraPitchRva),
                    ProbeSingleAddress(process, "Address.SpecialCameraRoll", gameBase, SpecialCameraRollRva),
                    ProbeSingleAddress(process, "Address.SpecialCameraYaw", gameBase, SpecialCameraYawRva),
                    ProbePointerAddress(process, "Address.SkillInventoryManager", gameBase, SkillManagerGlobalRva),
                    ProbeObjectPointerAddress(process, "Address.LearnedSkillTree", "SkillManager", manager, LearnedSkillTreeOffset, hasManager),
                    ProbeObjectUInt64Address(process, "Address.InventoryMoney", "InventoryManager", manager, InventoryCurrentMoneyOffset, hasManager),
                    ProbeObjectUInt32Address(process, "Address.InventoryMoneyInstanceId", "InventoryManager", manager, InventoryMoneyInstanceIdOffset, hasManager),
                    ProbeObjectUInt32Address(process, "Address.InventoryCapacity", "InventoryManager", manager, InventoryCapacityOffset, hasManager),
                    ProbeObjectPointerAddress(process, "Address.InventoryItemTreeHeader", "InventoryManager", manager, InventoryItemTreeHeaderOffset, hasManager),
                    ProbeObjectUInt32Address(process, "Address.InventoryItemTreeCount", "InventoryManager", manager, InventoryItemTreeCountOffset, hasManager),
                    ProbeObjectBytesAddress(process, "Address.InventoryEquipmentIds", "InventoryManager", manager, InventoryEquipmentIdsOffset, InventoryEquipmentIdCount * sizeof(uint), hasManager),
                    ProbeInventoryFirstItemNodeAddress(process, manager, hasManager),
                    ProbeItemStaticIndexAddress(process, gameBase),
                    ProbeStaticResolverChunkAddress(process, gameBase),
                    ProbeInventoryDialogPointerAddress(process, "Address.DlgInventoryDialog27Pointer", gameBase, DlgInventoryDialog27PointerRva),
                    ProbeInventoryDialogPointerAddress(process, "Address.DlgInventoryDialog28Pointer", gameBase, DlgInventoryDialog28PointerRva),
                    ProbeCodeAddress(process, "Address.DlgInventoryDialog27Method", gameBase, DlgInventoryDialog27MethodRva),
                    ProbeCodeAddress(process, "Address.DlgInventoryDialog28Method", gameBase, DlgInventoryDialog28MethodRva)
                };

                foreach (var check in checks)
                {
                    _logger.Info("api_probe.address", new Dictionary<string, object?>
                    {
                        ["account"] = context.AccountName,
                        ["name"] = check.Name,
                        ["success"] = check.Success,
                        ["detail"] = check.Detail
                    });
                }

                return OperationResult<IReadOnlyList<GameApiAddressProbeResult>>.Ok(checks);
            }
        }
        catch (Exception ex)
        {
            return OperationResult<IReadOnlyList<GameApiAddressProbeResult>>.Fail(
                ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static GameApiAddressProbeResult ProbePointerAddress(
        VmmProcess process,
        string name,
        ulong gameBase,
        ulong rva,
        bool allowZero = false)
    {
        var address = gameBase + rva;
        if (!TryReadPointer(process, address, out var value))
        {
            if (allowZero)
            {
                if (TryReadUInt64(process, address, out var raw64) && raw64 == 0)
                {
                    return AddressProbePass(name, gameBase, rva, "pointer=0x0");
                }

                if (TryReadUInt32(process, address, out var raw32) && raw32 == 0)
                {
                    return AddressProbePass(name, gameBase, rva, "pointer=0x0");
                }
            }

            return AddressProbeFail(name, gameBase, rva, "pointer read failed");
        }

        if (value == 0 && !allowZero)
        {
            return AddressProbeFail(name, gameBase, rva, "pointer is null");
        }

        return AddressProbePass(name, gameBase, rva, "pointer=0x" + value.ToString("X", CultureInfo.InvariantCulture));
    }

    private static GameApiAddressProbeResult ProbeEntitySystemPointerAddress(VmmProcess process, ulong gameBase)
    {
        const string name = "Address.EntitySystemPointer";
        if (!TryReadPointer(process, gameBase + EntitySystemPointerRva, out var entitySystem) || entitySystem == 0)
        {
            return AddressProbeFail(name, gameBase, EntitySystemPointerRva, "pointer read failed or is null");
        }

        var detail = "pointer=0x" + entitySystem.ToString("X", CultureInfo.InvariantCulture) +
            FormatProbeVfunc(process, entitySystem, EntitySystemGetEntityVfuncOffset, "getEntityVfunc");
        return AddressProbePass(name, gameBase, EntitySystemPointerRva, detail);
    }

    private static GameApiAddressProbeResult ProbeUInt16Address(
        VmmProcess process,
        string name,
        ulong gameBase,
        ulong rva)
    {
        return TryReadUInt16(process, gameBase + rva, out var value)
            ? AddressProbePass(name, gameBase, rva, "value=" + value.ToString(CultureInfo.InvariantCulture))
            : AddressProbeFail(name, gameBase, rva, "UInt16 read failed");
    }

    private static GameApiAddressProbeResult ProbeUInt32Address(
        VmmProcess process,
        string name,
        ulong gameBase,
        ulong rva)
    {
        return TryReadUInt32(process, gameBase + rva, out var value)
            ? AddressProbePass(name, gameBase, rva, "value=" + value.ToString(CultureInfo.InvariantCulture))
            : AddressProbeFail(name, gameBase, rva, "UInt32 read failed");
    }

    private static GameApiAddressProbeResult ProbeUInt64Address(
        VmmProcess process,
        string name,
        ulong gameBase,
        ulong rva)
    {
        return TryReadUInt64(process, gameBase + rva, out var value)
            ? AddressProbePass(name, gameBase, rva, "value=" + value.ToString(CultureInfo.InvariantCulture))
            : AddressProbeFail(name, gameBase, rva, "UInt64 read failed");
    }

    private static GameApiAddressProbeResult ProbeSingleAddress(
        VmmProcess process,
        string name,
        ulong gameBase,
        ulong rva)
    {
        return TryReadSingle(process, gameBase + rva, out var value) && float.IsFinite(value)
            ? AddressProbePass(name, gameBase, rva, "value=" + value.ToString("0.######", CultureInfo.InvariantCulture))
            : AddressProbeFail(name, gameBase, rva, "Single read failed or value is not finite");
    }

    private static GameApiAddressProbeResult ProbeObjectPointerAddress(
        VmmProcess process,
        string name,
        string objectName,
        ulong objectAddress,
        ulong offset,
        bool hasObject)
    {
        if (!hasObject)
        {
            return ObjectAddressProbeFail(name, objectName, objectAddress, offset, "root pointer is unavailable");
        }

        return TryReadPointer(process, objectAddress + offset, out var value) && value != 0
            ? ObjectAddressProbePass(name, objectName, objectAddress, offset, "pointer=0x" + value.ToString("X", CultureInfo.InvariantCulture))
            : ObjectAddressProbeFail(name, objectName, objectAddress, offset, "pointer read failed or is null");
    }

    private static GameApiAddressProbeResult ProbeObjectUInt64Address(
        VmmProcess process,
        string name,
        string objectName,
        ulong objectAddress,
        ulong offset,
        bool hasObject)
    {
        if (!hasObject)
        {
            return ObjectAddressProbeFail(name, objectName, objectAddress, offset, "root pointer is unavailable");
        }

        return TryReadUInt64(process, objectAddress + offset, out var value)
            ? ObjectAddressProbePass(name, objectName, objectAddress, offset, "value=" + value.ToString(CultureInfo.InvariantCulture))
            : ObjectAddressProbeFail(name, objectName, objectAddress, offset, "UInt64 read failed");
    }

    private static GameApiAddressProbeResult ProbeObjectUInt32Address(
        VmmProcess process,
        string name,
        string objectName,
        ulong objectAddress,
        ulong offset,
        bool hasObject)
    {
        if (!hasObject)
        {
            return ObjectAddressProbeFail(name, objectName, objectAddress, offset, "root pointer is unavailable");
        }

        return TryReadUInt32(process, objectAddress + offset, out var value)
            ? ObjectAddressProbePass(name, objectName, objectAddress, offset, "value=" + value.ToString(CultureInfo.InvariantCulture))
            : ObjectAddressProbeFail(name, objectName, objectAddress, offset, "UInt32 read failed");
    }

    private static GameApiAddressProbeResult ProbeObjectBytesAddress(
        VmmProcess process,
        string name,
        string objectName,
        ulong objectAddress,
        ulong offset,
        int length,
        bool hasObject)
    {
        if (!hasObject)
        {
            return ObjectAddressProbeFail(name, objectName, objectAddress, offset, "root pointer is unavailable");
        }

        return TryReadBytes(process, objectAddress + offset, length, out var bytes) && bytes.Length == length
            ? ObjectAddressProbePass(name, objectName, objectAddress, offset, "bytes=" + bytes.Length.ToString(CultureInfo.InvariantCulture))
            : ObjectAddressProbeFail(name, objectName, objectAddress, offset, "byte range read failed");
    }

    private static GameApiAddressProbeResult ProbePartyFirstMemberRecordAddress(VmmProcess process, ulong gameBase)
    {
        const string name = "Address.PartyFirstMemberRecord";
        TryReadUInt32(process, gameBase + PartyIdRva, out var partyId);
        TryReadUInt64(process, gameBase + PrimaryPartyCountRva, out var primaryPartyCount);
        if (partyId == 0 && primaryPartyCount == 0)
        {
            return AddressProbePass(
                name,
                gameBase,
                PrimaryPartyListRva,
                "not in party; no PartyMemberRecord expected");
        }

        if (!TryReadPartyMemberSnapshots(process, gameBase, out var members, out var error))
        {
            return AddressProbeFail(
                name,
                gameBase,
                PrimaryPartyListRva,
                "party member list read failed: " + error);
        }

        var member = members.FirstOrDefault(item => item.ServerObjectId != 0) ?? members.FirstOrDefault();
        if (member is null)
        {
            return AddressProbePass(
                name,
                gameBase,
                PrimaryPartyListRva,
                "party lists are readable but no PartyMemberRecord is loaded" +
                (string.IsNullOrWhiteSpace(error) ? string.Empty : "; " + error));
        }

        var detail = "list=" + member.ListName +
            ", node=0x" + member.NodeAddress.ToString("X", CultureInfo.InvariantCulture) +
            ", slot=" + member.PartySlot.ToString(CultureInfo.InvariantCulture) +
            ", serverObjectId=" + member.ServerObjectId.ToString(CultureInfo.InvariantCulture) +
            ", name=" + member.Name +
            ", classId=" + member.ClassId.ToString(CultureInfo.InvariantCulture) +
            ", level=" + member.Level.ToString(CultureInfo.InvariantCulture) +
            ", hp=" + member.CurrentHp.ToString(CultureInfo.InvariantCulture) +
            "/" + member.MaxHp.ToString(CultureInfo.InvariantCulture) +
            ", mp=" + member.CurrentMp.ToString(CultureInfo.InvariantCulture) +
            "/" + member.MaxMp.ToString(CultureInfo.InvariantCulture) +
            ", cachedPosition=" + FormatProbeVector(member.CachedPosition) +
            ", dataFlags=0x" + member.DataFlags.ToString("X2", CultureInfo.InvariantCulture) +
            ", abnormalRaw=" + member.RawAbnormalCount.ToString(CultureInfo.InvariantCulture) +
            ", abnormalEntries=" + member.AbnormalStatuses.Count.ToString(CultureInfo.InvariantCulture) +
            ", updateTime=" + member.UpdateTime.ToString(CultureInfo.InvariantCulture);
        return ObjectAddressProbePass(name, "PartyMemberRecord", member.MemberAddress, 0, detail);
    }

    private static GameApiAddressProbeResult ProbePartyLiveActorPositionAddress(VmmProcess process, ulong gameBase)
    {
        const string name = "Address.PartyLiveActorPosition";
        if (!TryReadPartyLiveContext(process, gameBase, out var context, out var error))
        {
            return AddressProbeFail(name, gameBase, EntitySystemPointerRva, error);
        }

        if (TryReadPartyMemberSnapshots(process, gameBase, out var members, out _) && members.Count > 0)
        {
            var enriched = ApplyPartyMemberLiveContext(members, 0, context);
            var liveMember = enriched.FirstOrDefault(member => !member.IsSelf && member.LivePosition is not null) ??
                enriched.FirstOrDefault(member => member.LivePosition is not null);
            if (liveMember?.LivePosition is { } livePosition)
            {
                var detail = "member=" + liveMember.Name +
                    ", serverObjectId=" + liveMember.ServerObjectId.ToString(CultureInfo.InvariantCulture) +
                    ", entityId=" + liveMember.LiveEntityId.ToString(CultureInfo.InvariantCulture) +
                    ", actor=0x" + liveMember.LiveActorAddress.ToString("X", CultureInfo.InvariantCulture) +
                    ", position=" + FormatProbeVector(livePosition) +
                    FormatProbeEntityPositionCandidates(process, liveMember.LiveEntityAddress) +
                    ", targetServerObjectId=" + liveMember.LiveTargetServerObjectId.ToString(CultureInfo.InvariantCulture) +
                    ", visibility=" + liveMember.VisibilityState +
                    ", distance=" + FormatProbeNullableDouble(liveMember.DistanceToLocalPlayer);
                return ObjectAddressProbePass(name, "CEntity", liveMember.LiveEntityAddress, 0, detail);
            }
        }

        if (context.LocalPosition is { } localPosition)
        {
            var detail = "no loaded party member live actor matched; local live position chain ok" +
                ", localServerObjectId=" + context.LocalServerObjectId.ToString(CultureInfo.InvariantCulture) +
                ", localActor=0x" + context.LocalActorAddress.ToString("X", CultureInfo.InvariantCulture) +
                ", localPosition=" + FormatProbeVector(localPosition) +
                FormatProbeEntityPositionCandidates(process, context.LocalEntityAddress) +
                ", visiblePlayerActors=" + context.VisiblePlayerActorsByServerObjectId.Count.ToString(CultureInfo.InvariantCulture);
            return ObjectAddressProbePass(name, "CEntity", context.LocalEntityAddress, 0, detail);
        }

        return ObjectAddressProbeFail(
            name,
            "CEntity",
            context.LocalEntityAddress,
            0,
            "live actor context was read but no reasonable local or party position was available");
    }

    private static GameApiAddressProbeResult ProbeInventoryFirstItemNodeAddress(
        VmmProcess process,
        ulong manager,
        bool hasManager)
    {
        const string name = "Address.InventoryFirstItemNode";
        if (!hasManager)
        {
            return ObjectAddressProbeFail(name, "InventoryManager", manager, InventoryItemTreeHeaderOffset, "root pointer is unavailable");
        }

        TryReadUInt64(process, manager + InventoryItemTreeCountOffset, out var treeCount);
        if (treeCount == 0)
        {
            return ObjectAddressProbePass(name, "InventoryManager", manager, InventoryItemTreeCountOffset, "inventory item tree is empty");
        }

        if (!TryReadPointer(process, manager + InventoryItemTreeHeaderOffset, out var header) || header == 0)
        {
            return ObjectAddressProbeFail(name, "InventoryManager", manager, InventoryItemTreeHeaderOffset, "tree header pointer read failed");
        }

        if (!TryReadPointer(process, header + NodeLeftOffset, out var node))
        {
            return ObjectAddressProbeFail(name, "InventoryTreeHeader", header, NodeLeftOffset, "begin node pointer read failed");
        }

        var equipmentInstanceIds = ReadInventoryEquipmentInstanceIds(process, manager);
        var visited = new HashSet<ulong>();
        var guardLimit = treeCount is > 0 and < 100000
            ? checked((int)treeCount + 16)
            : 100000;

        for (var guard = 0; node != 0 && node != header && guard < guardLimit; guard++)
        {
            if (!visited.Add(node) || IsNilNode(process, node, header))
            {
                break;
            }

            if (TryReadUInt32(process, node + InventoryNodeInstanceIdOffset, out var nodeInstanceId) &&
                TryReadPointer(process, node + InventoryNodeItemOffset, out var itemAddress) &&
                TryReadInventoryItemFromNode(process, node, equipmentInstanceIds, out var item))
            {
                var detail = "treeCount=" + treeCount.ToString(CultureInfo.InvariantCulture) +
                    ", nodeInstanceId=" + nodeInstanceId.ToString(CultureInfo.InvariantCulture) +
                    ", item=0x" + itemAddress.ToString("X", CultureInfo.InvariantCulture) +
                    ", instanceId=" + item.InstanceId.ToString(CultureInfo.InvariantCulture) +
                    ", templateId=" + item.TemplateId.ToString(CultureInfo.InvariantCulture) +
                    ", count=" + item.Count.ToString(CultureInfo.InvariantCulture) +
                    ", slot=" + item.Slot.ToString(CultureInfo.InvariantCulture) +
                    ", type=" + item.ItemType.ToString(CultureInfo.InvariantCulture) +
                    ", quality=" + item.QualityRank.ToString(CultureInfo.InvariantCulture) +
                    ", equipped=" + item.IsInEquipmentArray.ToString() +
                    ", name=" + item.Name;
                return ObjectAddressProbePass(name, "InventoryNode", node, 0, detail);
            }

            if (!TryGetNextTreeNode(process, header, node, out var next) || next == node)
            {
                break;
            }

            node = next;
        }

        return ObjectAddressProbeFail(
            name,
            "InventoryTreeHeader",
            header,
            0,
            "treeCount=" + treeCount.ToString(CultureInfo.InvariantCulture) +
            " but no readable InventoryItem node was found");
    }

    private static string FormatProbeVector(Vector3Snapshot position)
    {
        return position.X.ToString("0.###", CultureInfo.InvariantCulture) +
            "," + position.Y.ToString("0.###", CultureInfo.InvariantCulture) +
            "," + position.Z.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string FormatProbeEntityPositionCandidates(VmmProcess process, ulong entity)
    {
        var flagsText = TryReadUInt32(process, entity + EntityPositionFlagsOffset, out var flags)
            ? "0x" + flags.ToString("X", CultureInfo.InvariantCulture)
            : "unreadable";
        var worldText = TryReadPositionVector(
            process,
            entity + EntityWorldPositionOffset,
            out var worldX,
            out var worldY,
            out var worldZ)
            ? FormatProbeVector(new Vector3Snapshot(worldX, worldY, worldZ))
            : "unreadable";
        var alternateText = TryReadPositionVector(
            process,
            entity + EntityLocalPositionOffset,
            out var alternateX,
            out var alternateY,
            out var alternateZ)
            ? FormatProbeVector(new Vector3Snapshot(alternateX, alternateY, alternateZ))
            : "unreadable";

        return ", flags=" + flagsText +
            ", world=" + worldText +
            ", alternate=" + alternateText +
            FormatProbeVfunc(process, entity, EntityPositionVfuncOffset, "positionVfunc") +
            FormatProbeEntityFloatTriples(process, entity);
    }

    private static string FormatProbeVfunc(
        VmmProcess process,
        ulong instance,
        ulong slotOffset,
        string label)
    {
        if (!TryReadPointer(process, instance, out var vtable) ||
            !TryReadPointer(process, vtable + slotOffset, out var function) ||
            !TryReadBytes(process, function, 48, out var code) ||
            code.Length == 0)
        {
            return ", " + label + "=unreadable";
        }

        return ", vtable=0x" + vtable.ToString("X", CultureInfo.InvariantCulture) +
            ", " + label + "=0x" + function.ToString("X", CultureInfo.InvariantCulture) +
            ":" + Convert.ToHexString(code);
    }

    private static string FormatProbeEntityFloatTriples(VmmProcess process, ulong entity)
    {
        const int scanLength = 0x1000;
        const int maxResults = 16;
        if (!TryReadBytes(process, entity, scanLength, out var bytes) || bytes.Length < 12)
        {
            return ", plausibleTriples=unreadable";
        }

        var results = new List<string>();
        for (var offset = 0; offset <= bytes.Length - 12 && results.Count < maxResults; offset += 4)
        {
            var x = BitConverter.ToSingle(bytes, offset);
            var y = BitConverter.ToSingle(bytes, offset + 4);
            var z = BitConverter.ToSingle(bytes, offset + 8);
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) ||
                Math.Abs(x) < 10.0F || Math.Abs(y) < 10.0F ||
                Math.Abs(x) > 100000.0F || Math.Abs(y) > 100000.0F || Math.Abs(z) > 100000.0F)
            {
                continue;
            }

            results.Add(
                "+0x" + offset.ToString("X", CultureInfo.InvariantCulture) + "=" +
                x.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                y.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                z.ToString("0.###", CultureInfo.InvariantCulture));
        }

        return ", plausibleTriples=" + (results.Count == 0 ? "none" : string.Join("|", results));
    }

    private static string FormatProbeNullableDouble(double? value)
    {
        return value.HasValue
            ? value.Value.ToString("0.###", CultureInfo.InvariantCulture)
            : "unknown";
    }

    private static GameApiAddressProbeResult ProbeItemStaticIndexAddress(VmmProcess process, ulong gameBase)
    {
        const string name = "Address.ItemStaticIndex";
        var rva = ItemStaticIndexRva;
        if (!TryReadUInt32(process, gameBase + rva + 0x04, out var count) ||
            !TryReadPointer(process, gameBase + rva + 0x10, out var entries))
        {
            return AddressProbeFail(name, gameBase, rva, "count or entries read failed");
        }

        if (count == 0 || count > MaxStaticResolverEntries || entries == 0)
        {
            return AddressProbeFail(
                name,
                gameBase,
                rva,
                "invalid count=" + count.ToString(CultureInfo.InvariantCulture) +
                ", entries=0x" + entries.ToString("X", CultureInfo.InvariantCulture));
        }

        return AddressProbePass(
            name,
            gameBase,
            rva,
            "count=" + count.ToString(CultureInfo.InvariantCulture) +
            ", entries=0x" + entries.ToString("X", CultureInfo.InvariantCulture));
    }

    private static GameApiAddressProbeResult ProbeStaticResolverChunkAddress(VmmProcess process, ulong gameBase)
    {
        const string name = "Address.StaticResolverChunk0";
        var rva = StaticResolverChunkListRva;
        if (!TryReadPointer(process, gameBase + rva, out var chunk) || chunk == 0)
        {
            return AddressProbeFail(name, gameBase, rva, "chunk 0 pointer read failed or is null");
        }

        if (!TryReadUInt32(process, chunk, out var compressedSize) ||
            !TryReadUInt32(process, chunk + 0x04, out var uncompressedSize))
        {
            return AddressProbeFail(name, gameBase, rva, "chunk header read failed at 0x" + chunk.ToString("X"));
        }

        var valid = compressedSize > 6 &&
            compressedSize <= MaxStaticChunkCompressedBytes &&
            uncompressedSize > 0 &&
            uncompressedSize <= MaxStaticChunkUncompressedBytes;
        var detail = "chunk=0x" + chunk.ToString("X", CultureInfo.InvariantCulture) +
            ", compressed=" + compressedSize.ToString(CultureInfo.InvariantCulture) +
            ", uncompressed=" + uncompressedSize.ToString(CultureInfo.InvariantCulture);
        return valid
            ? AddressProbePass(name, gameBase, rva, detail)
            : AddressProbeFail(name, gameBase, rva, "invalid " + detail);
    }

    private static GameApiAddressProbeResult ProbeInventoryDialogPointerAddress(
        VmmProcess process,
        string name,
        ulong gameBase,
        ulong rva)
    {
        var address = gameBase + rva;
        if (!TryReadPointer(process, address, out var dialog))
        {
            if ((TryReadUInt64(process, address, out var raw64) && raw64 == 0) ||
                (TryReadUInt32(process, address, out var raw32) && raw32 == 0))
            {
                return AddressProbePass(name, gameBase, rva, "dialog=0x0");
            }

            return AddressProbeFail(name, gameBase, rva, "dialog pointer read failed");
        }

        if (!TryReadUInt64(process, dialog + DlgInventoryWidgetFlagsOffset, out var flags))
        {
            return AddressProbeFail(
                name,
                gameBase,
                rva,
                "dialog=0x" + dialog.ToString("X", CultureInfo.InvariantCulture) +
                "; widget flags read failed at +0x" + DlgInventoryWidgetFlagsOffset.ToString("X", CultureInfo.InvariantCulture));
        }

        var visible = (flags & DlgInventoryVisibleMask) != 0;
        return AddressProbePass(
            name,
            gameBase,
            rva,
            "dialog=0x" + dialog.ToString("X", CultureInfo.InvariantCulture) +
            "; flags=0x" + flags.ToString("X", CultureInfo.InvariantCulture) +
            "; visible=" + visible.ToString());
    }

    private static GameApiAddressProbeResult ProbeCodeAddress(
        VmmProcess process,
        string name,
        ulong gameBase,
        ulong rva)
    {
        if (!TryReadBytes(process, gameBase + rva, 8, out var bytes) || bytes.Length < 8)
        {
            return AddressProbeFail(name, gameBase, rva, "code read failed");
        }

        return AddressProbePass(name, gameBase, rva, "bytes=" + Convert.ToHexString(bytes));
    }

    private static GameApiAddressProbeResult AddressProbePass(
        string name,
        ulong gameBase,
        ulong rva,
        string detail)
    {
        return new GameApiAddressProbeResult(name, true, FormatProbeAddress(gameBase, rva) + "; " + detail);
    }

    private static GameApiAddressProbeResult AddressProbeFail(
        string name,
        ulong gameBase,
        ulong rva,
        string detail)
    {
        return new GameApiAddressProbeResult(name, false, FormatProbeAddress(gameBase, rva) + "; " + detail);
    }

    private static string FormatProbeAddress(ulong gameBase, ulong rva)
    {
        return "Game.dll base=0x" + gameBase.ToString("X", CultureInfo.InvariantCulture) +
            ", RVA=0x" + rva.ToString("X", CultureInfo.InvariantCulture) +
            ", address=0x" + (gameBase + rva).ToString("X", CultureInfo.InvariantCulture);
    }

    private static GameApiAddressProbeResult ObjectAddressProbePass(
        string name,
        string objectName,
        ulong objectAddress,
        ulong offset,
        string detail)
    {
        return new GameApiAddressProbeResult(
            name,
            true,
            FormatObjectProbeAddress(objectName, objectAddress, offset) + "; " + detail);
    }

    private static GameApiAddressProbeResult ObjectAddressProbeFail(
        string name,
        string objectName,
        ulong objectAddress,
        ulong offset,
        string detail)
    {
        return new GameApiAddressProbeResult(
            name,
            false,
            FormatObjectProbeAddress(objectName, objectAddress, offset) + "; " + detail);
    }

    private static string FormatObjectProbeAddress(string objectName, ulong objectAddress, ulong offset)
    {
        return objectName + "=0x" + objectAddress.ToString("X", CultureInfo.InvariantCulture) +
            ", offset=0x" + offset.ToString("X", CultureInfo.InvariantCulture) +
            ", address=0x" + (objectAddress + offset).ToString("X", CultureInfo.InvariantCulture);
    }
#endif

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

                if (!TryReadLockedTarget(process, gameBase, context.BypassMemoryCache, out var target, out var readError))
                {
                    return OperationResult<LockedTargetSnapshot>.Fail(readError);
                }

                var snapshot = ToLockedTargetSnapshot(target);
                _logger.Info("vmm.locked_target.read", new Dictionary<string, object?>
                {
                    ["account"] = context.AccountName,
                    ["pid"] = SafeGetProcessPid(process),
                    ["targetEntityId"] = snapshot.TargetEntityId,
                    ["localServerObjectId"] = target.LocalServerObjectId,
                    ["serverObjectId"] = snapshot.ServerObjectId,
                    ["targetServerObjectId"] = snapshot.ServerObjectId,
                    ["targetingServerObjectId"] = snapshot.TargetServerObjectId,
                    ["targetingMe"] = snapshot.IsTargetingLocalPlayer,
                    ["objectType"] = snapshot.ObjectType,
                    ["hp"] = snapshot.CurrentHp,
                    ["maxHp"] = snapshot.MaxHp,
                    ["lootRaw"] = snapshot.LootableRaw,
                    ["interactionState"] = snapshot.InteractionState,
                    ["isMonsterAlive"] = snapshot.IsMonsterAlive,
                    ["bypassMemoryCache"] = context.BypassMemoryCache,
                    ["actorAddress"] = target.Actor?.Actor.ToString("X") ?? string.Empty,
                    ["actorResolveSource"] = target.Actor?.ResolveSource ?? string.Empty
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

    private OperationResult<LockedTargetAbnormalStatusSnapshot> ReadLockedTargetAbnormalStatusesCore(GameApiReadContext context)
    {
        try
        {
            var connection = GetOrCreateConnection(context.VmmDeviceName);
            lock (connection.SyncRoot)
            {
                if (!TryResolveProcess(connection.Vmm, context, out var process, out var processError))
                {
                    return OperationResult<LockedTargetAbnormalStatusSnapshot>.Fail(processError);
                }

                var moduleName = ResolveModuleName();
                var gameBase = process.GetModuleBase(moduleName);
                if (gameBase == 0)
                {
                    return OperationResult<LockedTargetAbnormalStatusSnapshot>.Fail("Module not found: " + moduleName);
                }

                if (!TryReadLockedTarget(process, gameBase, context.BypassMemoryCache, out var target, out var readError))
                {
                    return OperationResult<LockedTargetAbnormalStatusSnapshot>.Fail(readError);
                }

                var capturedAt = DateTimeOffset.Now;
                var targetSnapshot = ToLockedTargetSnapshot(target, capturedAt);
                if (target.TargetEntityId == 0)
                {
                    var empty = new LockedTargetAbnormalStatusSnapshot(
                        targetSnapshot,
                        0,
                        Array.Empty<AbnormalStatusEntrySnapshot>(),
                        capturedAt);
                    _logger.Info("vmm.locked_target_abnormal.read", new Dictionary<string, object?>
                    {
                        ["account"] = context.AccountName,
                        ["pid"] = SafeGetProcessPid(process),
                        ["hasTarget"] = false,
                        ["targetEntityId"] = 0,
                        ["abnormalCount"] = 0,
                        ["physicalDebuffCount"] = 0,
                        ["abnormalIds"] = string.Empty,
                        ["physicalDebuffIds"] = string.Empty
                    });
                    return OperationResult<LockedTargetAbnormalStatusSnapshot>.Ok(empty);
                }

                if (target.Actor is null || target.Actor.Actor == 0)
                {
                    return OperationResult<LockedTargetAbnormalStatusSnapshot>.Fail("locked target actor is not resolved");
                }

                TryReadUInt32(
                    process,
                    target.Actor.Actor + ActorAbnormalCategory2CountOffset,
                    out var abnormalCategory2Count);

                if (!TryReadActorAbnormalStatusEntries(process, target.Actor.Actor, out var entries, out readError))
                {
                    return OperationResult<LockedTargetAbnormalStatusSnapshot>.Fail(readError);
                }

                var snapshot = new LockedTargetAbnormalStatusSnapshot(
                    targetSnapshot,
                    abnormalCategory2Count,
                    entries,
                    capturedAt);

                _logger.Info("vmm.locked_target_abnormal.read", new Dictionary<string, object?>
                {
                    ["account"] = context.AccountName,
                    ["pid"] = SafeGetProcessPid(process),
                    ["hasTarget"] = snapshot.HasTarget,
                    ["targetEntityId"] = snapshot.Target.TargetEntityId,
                    ["serverObjectId"] = snapshot.Target.ServerObjectId,
                    ["objectType"] = snapshot.Target.ObjectType,
                    ["isMonsterAlive"] = snapshot.Target.IsMonsterAlive,
                    ["abnormalCategory2Count"] = snapshot.AbnormalCategory2Count,
                    ["abnormalCount"] = snapshot.AbnormalStatusCount,
                    ["physicalDebuffCount"] = snapshot.PhysicalDebuffCount,
                    ["abnormalIds"] = FormatAbnormalIds(snapshot.Entries),
                    ["physicalDebuffIds"] = FormatAbnormalIds(snapshot.Entries.Where(entry => entry.IsPhysicalDebuffCategory)),
                    ["actorAddress"] = target.Actor.Actor.ToString("X"),
                    ["actorResolveSource"] = target.Actor.ResolveSource
                });

                return OperationResult<LockedTargetAbnormalStatusSnapshot>.Ok(snapshot);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("vmm.locked_target_abnormal.exception", ex, new Dictionary<string, object?> { ["account"] = context.AccountName });
            return OperationResult<LockedTargetAbnormalStatusSnapshot>.Fail(ex.Message);
        }
    }

    private static string FormatAbnormalIds(IEnumerable<AbnormalStatusEntrySnapshot> entries)
    {
        var ids = entries
            .Where(entry => entry.AbnormalId != 0)
            .Select(entry => entry.AbnormalId.ToString(CultureInfo.InvariantCulture))
            .Distinct(StringComparer.Ordinal)
            .Take(32)
            .ToArray();

        return ids.Length == 0 ? string.Empty : string.Join(",", ids);
    }

    private OperationResult<PlayerSnapshot> ReadPlayerCore(GameApiReadContext context)
    {
        try
        {
            var first = ReadPlayerCoreOnce(context);
            if (first.Success)
            {
                ClearPlayerReadFailure(context);
                return first;
            }

            if (!ShouldReconnectAfterPlayerReadFailure(first.Error))
            {
                return first;
            }

            var failureCount = RecordPlayerReadFailure(context);
            if (failureCount < PlayerReadFailuresBeforeReconnect)
            {
                return first;
            }

            ClearPlayerReadFailure(context);
            ResetConnection(context.VmmDeviceName, context.AccountName, "player_read_failed", first.Error);
            return first;
        }
        catch (VmmException ex)
        {
            DelayConnectionRetry(context.VmmDeviceName, VmmReconnectDelay);
            _logger.Warn("vmm.connection.init_failed", new Dictionary<string, object?>
            {
                ["account"] = context.AccountName,
                ["device"] = ResolveVmmDeviceName(context.VmmDeviceName),
                ["remote"] = ResolveVmmRemote(),
                ["error"] = ex.Message,
                ["retryAfterMs"] = (int)VmmReconnectDelay.TotalMilliseconds
            });

            return OperationResult<PlayerSnapshot>.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.Error("vmm.player.exception", ex, new Dictionary<string, object?> { ["account"] = context.AccountName });
            return OperationResult<PlayerSnapshot>.Fail(ex.Message);
        }
    }

    private OperationResult<PlayerSnapshot> ReadPlayerCoreOnce(GameApiReadContext context)
    {
        if (TryGetConnectionRetryDelay(context.VmmDeviceName, out var retryAfterMs))
        {
            return OperationResult<PlayerSnapshot>.Fail("VMM reconnect cooling down for " + retryAfterMs + "ms");
        }

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

            if (!TryReadLocalPlayer(process, gameBase, context.BypassMemoryCache, out var snapshot, out var readError))
            {
                return OperationResult<PlayerSnapshot>.Fail(readError);
            }

            _logger.Info("vmm.player.read", new Dictionary<string, object?>
            {
                ["account"] = context.AccountName,
                ["pid"] = SafeGetProcessPid(process),
                ["entityId"] = snapshot.EntityId,
                ["targetEntityId"] = snapshot.TargetEntityId,
                ["classId"] = snapshot.CharacterClassId.HasValue ? (object)(uint)snapshot.CharacterClassId.Value : null,
                ["class"] = snapshot.CharacterClass,
                ["hp"] = snapshot.CurrentHp,
                ["maxHp"] = snapshot.MaxHp,
                ["mp"] = snapshot.CurrentMp,
                ["maxMp"] = snapshot.MaxMp,
                ["hasPosition"] = snapshot.Position is not null,
                ["x"] = snapshot.Position is { } position ? Math.Round(position.X, 3) : null,
                ["y"] = snapshot.Position is { } positionY ? Math.Round(positionY.Y, 3) : null,
                ["z"] = snapshot.Position is { } positionZ ? Math.Round(positionZ.Z, 3) : null,
                ["bypassMemoryCache"] = context.BypassMemoryCache
            });

            return OperationResult<PlayerSnapshot>.Ok(snapshot);
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
                    ["category2EntryCount"] = snapshot.Category2EntryCount,
                    ["category2EntrySummary"] = snapshot.Category2EntrySummary
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

    private OperationResult<SummonedPetSnapshot> ReadSummonedPetCore(GameApiReadContext context)
    {
        try
        {
            var connection = GetOrCreateConnection(context.VmmDeviceName);
            lock (connection.SyncRoot)
            {
                if (!TryResolveProcess(connection.Vmm, context, out var process, out var processError))
                {
                    return OperationResult<SummonedPetSnapshot>.Fail(processError);
                }

                var moduleName = ResolveModuleName();
                var gameBase = process.GetModuleBase(moduleName);
                if (gameBase == 0)
                {
                    return OperationResult<SummonedPetSnapshot>.Fail("Module not found: " + moduleName);
                }

                var npcCatalog = GetNpcXmlCatalog();
                if (!TryReadSummonedPet(process, gameBase, npcCatalog.Details, out var snapshot, out var readError))
                {
                    return OperationResult<SummonedPetSnapshot>.Fail(readError);
                }

                _logger.Info("vmm.summoned_pet.read", new Dictionary<string, object?>
                {
                    ["account"] = context.AccountName,
                    ["pid"] = SafeGetProcessPid(process),
                    ["isSummoned"] = snapshot.IsSummoned,
                    ["localServerObjectId"] = snapshot.LocalServerObjectId,
                    ["serverObjectId"] = snapshot.ServerObjectId,
                    ["entityId"] = snapshot.EntityId,
                    ["templateId"] = snapshot.NpcTemplateId,
                    ["name"] = snapshot.Name
                });

                return OperationResult<SummonedPetSnapshot>.Ok(snapshot);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("vmm.summoned_pet.exception", ex, new Dictionary<string, object?> { ["account"] = context.AccountName });
            return OperationResult<SummonedPetSnapshot>.Fail(ex.Message);
        }
    }

    private OperationResult<SummonedPetRosterSnapshot> ReadSummonedPetRosterCore(GameApiReadContext context)
    {
        try
        {
            var connection = GetOrCreateConnection(context.VmmDeviceName);
            lock (connection.SyncRoot)
            {
                if (!TryResolveProcess(connection.Vmm, context, out var process, out var processError))
                {
                    return OperationResult<SummonedPetRosterSnapshot>.Fail(processError);
                }

                var moduleName = ResolveModuleName();
                var gameBase = process.GetModuleBase(moduleName);
                if (gameBase == 0)
                {
                    return OperationResult<SummonedPetRosterSnapshot>.Fail("Module not found: " + moduleName);
                }

                var npcCatalog = GetNpcXmlCatalog();
                if (!TryReadSummonedPetRoster(process, gameBase, npcCatalog.Details, out var snapshot, out var readError))
                {
                    return OperationResult<SummonedPetRosterSnapshot>.Fail(readError);
                }

                _logger.Info("vmm.summoned_pet_roster.read", new Dictionary<string, object?>
                {
                    ["account"] = context.AccountName,
                    ["pid"] = SafeGetProcessPid(process),
                    ["localServerObjectId"] = snapshot.LocalServerObjectId,
                    ["localPetSummoned"] = snapshot.LocalPlayerPet.IsSummoned,
                    ["localPetServerObjectId"] = snapshot.LocalPlayerPet.Pet.ServerObjectId,
                    ["partyMemberCount"] = snapshot.PartyMemberServerObjectIds.Count,
                    ["partyPetCount"] = snapshot.PartyMemberPetCount,
                    ["visibleSummonedPetCount"] = snapshot.VisibleSummonedPetCount
                });

                return OperationResult<SummonedPetRosterSnapshot>.Ok(snapshot);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("vmm.summoned_pet_roster.exception", ex, new Dictionary<string, object?> { ["account"] = context.AccountName });
            return OperationResult<SummonedPetRosterSnapshot>.Fail(ex.Message);
        }
    }

    private OperationResult<PartySnapshot> ReadPartyCore(GameApiReadContext context)
    {
        try
        {
            var connection = GetOrCreateConnection(context.VmmDeviceName);
            lock (connection.SyncRoot)
            {
                if (!TryResolveProcess(connection.Vmm, context, out var process, out var processError))
                {
                    return OperationResult<PartySnapshot>.Fail(processError);
                }

                var moduleName = ResolveModuleName();
                var gameBase = process.GetModuleBase(moduleName);
                if (gameBase == 0)
                {
                    return OperationResult<PartySnapshot>.Fail("Module not found: " + moduleName);
                }

                if (!TryReadPartySnapshot(process, gameBase, out var snapshot, out var readError))
                {
                    return OperationResult<PartySnapshot>.Fail(readError);
                }

                _logger.Info("vmm.party.read", new Dictionary<string, object?>
                {
                    ["account"] = context.AccountName,
                    ["pid"] = SafeGetProcessPid(process),
                    ["partyId"] = snapshot.PartyId,
                    ["memberCount"] = snapshot.Members.Count,
                    ["localServerObjectId"] = snapshot.LocalServerObjectId,
                    ["leaderServerObjectId"] = snapshot.LeaderServerObjectId,
                    ["localIsLeader"] = snapshot.LocalIsLeader,
                    ["visiblePlayerActorCount"] = snapshot.VisiblePlayerActorCount
                });

                return OperationResult<PartySnapshot>.Ok(snapshot);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("vmm.party.exception", ex, new Dictionary<string, object?> { ["account"] = context.AccountName });
            return OperationResult<PartySnapshot>.Fail(ex.Message);
        }
    }

    private OperationResult<TacticsSignSnapshot> ReadTacticsSignsCore(GameApiReadContext context)
    {
        try
        {
            var connection = GetOrCreateConnection(context.VmmDeviceName);
            lock (connection.SyncRoot)
            {
                if (!TryResolveProcess(connection.Vmm, context, out var process, out var processError))
                {
                    return OperationResult<TacticsSignSnapshot>.Fail(processError);
                }

                var moduleName = ResolveModuleName();
                var gameBase = process.GetModuleBase(moduleName);
                if (gameBase == 0)
                {
                    return OperationResult<TacticsSignSnapshot>.Fail("Module not found: " + moduleName);
                }

                const int byteCount = TacticsSignCount * sizeof(uint);
                if (!TryReadBytes(
                        process,
                        gameBase + TacticsSignTableRva,
                        byteCount,
                        out var bytes,
                        context.BypassMemoryCache))
                {
                    return OperationResult<TacticsSignSnapshot>.Fail(
                        "failed to read tactics sign table at Game.dll+0x" +
                        TacticsSignTableRva.ToString("X", CultureInfo.InvariantCulture));
                }

                var serverObjectIds = new uint[TacticsSignCount];
                for (var index = 0; index < serverObjectIds.Length; index++)
                {
                    serverObjectIds[index] = BitConverter.ToUInt32(bytes, index * sizeof(uint));
                }

                return OperationResult<TacticsSignSnapshot>.Ok(
                    new TacticsSignSnapshot(serverObjectIds, DateTimeOffset.Now));
            }
        }
        catch (Exception ex)
        {
            _logger.Error("vmm.tactics_signs.exception", ex, new Dictionary<string, object?>
            {
                ["account"] = context.AccountName
            });
            return OperationResult<TacticsSignSnapshot>.Fail(ex.Message);
        }
    }

    private OperationResult<ChannelSnapshot> ReadChannelCore(GameApiReadContext context)
    {
        try
        {
            var connection = GetOrCreateConnection(context.VmmDeviceName);
            lock (connection.SyncRoot)
            {
                if (!TryResolveProcess(connection.Vmm, context, out var process, out var processError))
                {
                    return OperationResult<ChannelSnapshot>.Fail(processError);
                }

                var moduleName = ResolveModuleName();
                var gameBase = process.GetModuleBase(moduleName);
                if (gameBase == 0)
                {
                    return OperationResult<ChannelSnapshot>.Fail("Module not found: " + moduleName);
                }

                const int channelByteCount = sizeof(uint) * 2;
                if (!TryReadBytes(
                        process,
                        gameBase + CurrentChannelIndexRva,
                        channelByteCount,
                        out var channelBytes,
                        context.BypassMemoryCache))
                {
                    return OperationResult<ChannelSnapshot>.Fail(
                        "failed to read channel index/count at Game.dll+0x" +
                        CurrentChannelIndexRva.ToString("X", CultureInfo.InvariantCulture));
                }

                if (!TryReadPointer(
                        process,
                        gameBase + CurrentMapContextPointerRva,
                        out var mapContext,
                        context.BypassMemoryCache) ||
                    mapContext == 0)
                {
                    return OperationResult<ChannelSnapshot>.Fail(
                        "failed to read current map context at Game.dll+0x" +
                        CurrentMapContextPointerRva.ToString("X", CultureInfo.InvariantCulture));
                }

                if (!TryReadUInt32(
                        process,
                        mapContext + CurrentMapIdOffset,
                        out var mapId,
                        context.BypassMemoryCache))
                {
                    return OperationResult<ChannelSnapshot>.Fail(
                        "failed to read current map id at MapContext+0x" +
                        CurrentMapIdOffset.ToString("X", CultureInfo.InvariantCulture));
                }

                var channelIndex = unchecked((int)BitConverter.ToUInt32(channelBytes, 0));
                var channelCount = unchecked((int)BitConverter.ToUInt32(channelBytes, sizeof(uint)));
                return OperationResult<ChannelSnapshot>.Ok(
                    new ChannelSnapshot(channelIndex, channelCount, mapId, DateTimeOffset.Now));
            }
        }
        catch (Exception ex)
        {
            _logger.Error("vmm.channel.exception", ex, new Dictionary<string, object?>
            {
                ["account"] = context.AccountName
            });
            return OperationResult<ChannelSnapshot>.Fail(ex.Message);
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

    private OperationResult<IReadOnlyList<InventoryItemSnapshot>> ReadInventoryCore(GameApiReadContext context)
    {
        try
        {
            var connection = GetOrCreateConnection(context.VmmDeviceName);
            lock (connection.SyncRoot)
            {
                if (!TryResolveProcess(connection.Vmm, context, out var process, out var processError))
                {
                    return OperationResult<IReadOnlyList<InventoryItemSnapshot>>.Fail(processError);
                }

                var moduleName = ResolveModuleName();
                var gameBase = process.GetModuleBase(moduleName);
                if (gameBase == 0)
                {
                    return OperationResult<IReadOnlyList<InventoryItemSnapshot>>.Fail("Module not found: " + moduleName);
                }

                if (!TryReadInventoryItems(process, gameBase, out var items, out var readError))
                {
                    return OperationResult<IReadOnlyList<InventoryItemSnapshot>>.Fail(readError);
                }

                var qualityByTemplate = new Dictionary<uint, byte>();
                var staticChunkCache = new Dictionary<uint, byte[]>();
                for (var i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    item.QualityRank = ReadItemQualityRank(
                        process,
                        gameBase,
                        item.TemplateId,
                        qualityByTemplate,
                        staticChunkCache);
                    items[i] = item;
                }

                var snapshots = items
                    .Where(IsNormalBagInventoryItem)
                    .OrderBy(item => item.Slot)
                    .ThenBy(item => item.TemplateId)
                    .ThenBy(item => item.InstanceId)
                    .Select(item => new InventoryItemSnapshot(
                        item.TemplateId,
                        item.InstanceId,
                        item.Name,
                        ClampInventoryCount(item.Count),
                        item.Slot,
                        IsEquippedInventoryItem(item),
                        item.ItemType,
                        item.QualityRank,
                        item.VendorSellUnitPrice))
                    .ToArray();

                _logger.Info("vmm.inventory.read", new Dictionary<string, object?>
                {
                    ["account"] = context.AccountName,
                    ["pid"] = SafeGetProcessPid(process),
                    ["processName"] = process.Name,
                    ["count"] = snapshots.Length
                });

                return OperationResult<IReadOnlyList<InventoryItemSnapshot>>.Ok(snapshots);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("vmm.inventory.exception", ex, new Dictionary<string, object?> { ["account"] = context.AccountName });
            return OperationResult<IReadOnlyList<InventoryItemSnapshot>>.Fail(ex.Message);
        }
    }

    private OperationResult<GatherSnapshot> ReadGatherSnapshotCore(GameApiReadContext context)
    {
        try
        {
            var connection = GetOrCreateConnection(context.VmmDeviceName);
            lock (connection.SyncRoot)
            {
                if (!TryResolveProcess(connection.Vmm, context, out var process, out var processError))
                {
                    return OperationResult<GatherSnapshot>.Fail(processError);
                }

                var moduleName = ResolveModuleName();
                var gameBase = process.GetModuleBase(moduleName);
                if (gameBase == 0)
                {
                    return OperationResult<GatherSnapshot>.Fail("Module not found: " + moduleName);
                }

                var gatherCatalog = GatherSourceCatalog.Default;
                var npcCatalog = GetNpcXmlCatalog();
                if (!TryReadGatherSnapshot(
                        process,
                        gameBase,
                        gatherCatalog,
                        npcCatalog.Details,
                        out var snapshot,
                        out var counters,
                        out var readError))
                {
                    return OperationResult<GatherSnapshot>.Fail(readError);
                }

                _logger.Info("vmm.gather.read", new Dictionary<string, object?>
                {
                    ["account"] = context.AccountName,
                    ["pid"] = SafeGetProcessPid(process),
                    ["objects"] = snapshot.Objects.Count,
                    ["nearbyPlayers"] = snapshot.NearbyPlayers.Count,
                    ["nearbyMonsters"] = snapshot.NearbyMonsters.Count,
                    ["scannedServerObjects"] = counters.ScannedServerObjects,
                    ["resolvedEntities"] = counters.ResolvedEntities,
                    ["resolvedActors"] = counters.ResolvedActors,
                    ["catalogLoaded"] = gatherCatalog.Loaded,
                    ["catalogRows"] = gatherCatalog.Count,
                    ["catalogError"] = gatherCatalog.Error
                });

                return OperationResult<GatherSnapshot>.Ok(snapshot);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("vmm.gather.exception", ex, new Dictionary<string, object?> { ["account"] = context.AccountName });
            return OperationResult<GatherSnapshot>.Fail(ex.Message);
        }
    }

    private OperationResult<ulong> ReadInventoryMoneyCore(GameApiReadContext context)
    {
        try
        {
            var connection = GetOrCreateConnection(context.VmmDeviceName);
            lock (connection.SyncRoot)
            {
                if (!TryResolveProcess(connection.Vmm, context, out var process, out var processError))
                {
                    return OperationResult<ulong>.Fail(processError);
                }

                var moduleName = ResolveModuleName();
                var gameBase = process.GetModuleBase(moduleName);
                if (gameBase == 0)
                {
                    return OperationResult<ulong>.Fail("Module not found: " + moduleName);
                }

                if (!TryReadPointer(process, gameBase + InventoryManagerGlobalRva, out var manager) || manager == 0)
                {
                    return OperationResult<ulong>.Fail(
                        "failed to read InventoryManager pointer at Game.dll+0x" +
                        InventoryManagerGlobalRva.ToString("X", CultureInfo.InvariantCulture));
                }

                if (!TryReadUInt64(process, manager + InventoryCurrentMoneyOffset, out var money))
                {
                    return OperationResult<ulong>.Fail(
                        "failed to read inventory money at InventoryManager+0x" +
                        InventoryCurrentMoneyOffset.ToString("X", CultureInfo.InvariantCulture));
                }

                TryReadUInt32(process, manager + InventoryMoneyInstanceIdOffset, out var moneyInstanceId);
                _logger.Info("vmm.inventory.money.read", new Dictionary<string, object?>
                {
                    ["account"] = context.AccountName,
                    ["pid"] = SafeGetProcessPid(process),
                    ["processName"] = process.Name,
                    ["money"] = money,
                    ["moneyInstanceId"] = moneyInstanceId
                });

                return OperationResult<ulong>.Ok(money);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("vmm.inventory.money.exception", ex, new Dictionary<string, object?>
            {
                ["account"] = context.AccountName
            });
            return OperationResult<ulong>.Fail(ex.Message);
        }
    }

    private OperationResult<int> ReadInventoryCapacityCore(GameApiReadContext context)
    {
        try
        {
            var connection = GetOrCreateConnection(context.VmmDeviceName);
            lock (connection.SyncRoot)
            {
                if (!TryResolveProcess(connection.Vmm, context, out var process, out var processError))
                {
                    return OperationResult<int>.Fail(processError);
                }

                var moduleName = ResolveModuleName();
                var gameBase = process.GetModuleBase(moduleName);
                if (gameBase == 0)
                {
                    return OperationResult<int>.Fail("Module not found: " + moduleName);
                }

                if (!TryReadPointer(process, gameBase + InventoryManagerGlobalRva, out var manager) || manager == 0)
                {
                    return OperationResult<int>.Fail(
                        "failed to read InventoryManager pointer at Game.dll+0x" +
                        InventoryManagerGlobalRva.ToString("X", CultureInfo.InvariantCulture));
                }

                if (!TryReadUInt32(process, manager + InventoryCapacityOffset, out var capacity))
                {
                    return OperationResult<int>.Fail(
                        "failed to read inventory capacity at InventoryManager+0x" +
                        InventoryCapacityOffset.ToString("X", CultureInfo.InvariantCulture));
                }

                _logger.Info("vmm.inventory.capacity.read", new Dictionary<string, object?>
                {
                    ["account"] = context.AccountName,
                    ["pid"] = SafeGetProcessPid(process),
                    ["processName"] = process.Name,
                    ["capacity"] = capacity
                });

                return OperationResult<int>.Ok(checked((int)capacity));
            }
        }
        catch (Exception ex)
        {
            _logger.Error("vmm.inventory.capacity.exception", ex, new Dictionary<string, object?>
            {
                ["account"] = context.AccountName
            });
            return OperationResult<int>.Fail(ex.Message);
        }
    }

    private OperationResult<InventoryWindowSnapshot> ReadInventoryWindowCore(
        GameApiReadContext context,
        InventoryWindowRectSource rectSource)
    {
        try
        {
            var connection = GetOrCreateConnection(context.VmmDeviceName);
            lock (connection.SyncRoot)
            {
                if (!TryResolveProcess(connection.Vmm, context, out var process, out var processError))
                {
                    return OperationResult<InventoryWindowSnapshot>.Fail(processError);
                }

                var moduleName = ResolveModuleName();
                var gameBase = process.GetModuleBase(moduleName);
                if (gameBase == 0)
                {
                    return OperationResult<InventoryWindowSnapshot>.Fail("Module not found: " + moduleName);
                }

                var dialogTableCandidates = FindInventoryWindowDialogTableCandidates(process, gameBase);
                if (dialogTableCandidates.Count > 0)
                {
                    if (TryReadPreferredInventoryWindowSnapshot(
                            process,
                            dialogTableCandidates,
                            rectSource,
                            out var dialogTableSnapshot,
                            out var dialogTableError,
                            out var hasVisibleDialog))
                    {
                        LogInventoryWindowRead(context, process, dialogTableSnapshot, cacheHit: false);
                        return OperationResult<InventoryWindowSnapshot>.Ok(dialogTableSnapshot);
                    }

                    if (hasVisibleDialog)
                    {
                        return OperationResult<InventoryWindowSnapshot>.Fail(dialogTableError);
                    }
                }

                var cacheKey = BuildInventoryWindowCacheKey(context, process, gameBase);
                if (_inventoryWindowCandidateCache.TryGetValue(cacheKey, out var cachedCandidate) &&
                    TryReadInventoryWindowSnapshot(
                        process,
                        cachedCandidate,
                        rectSource,
                        out var cachedSnapshot,
                        out _))
                {
                    LogInventoryWindowRead(context, process, cachedSnapshot, cacheHit: true);
                    return OperationResult<InventoryWindowSnapshot>.Ok(cachedSnapshot);
                }

                _inventoryWindowCandidateCache.Remove(cacheKey);

                if (!TryFindInventoryWindowCandidate(
                    process,
                    gameBase,
                    moduleName,
                    out var candidate,
                    out var findError))
                {
                    return OperationResult<InventoryWindowSnapshot>.Fail(findError);
                }

                if (!TryReadInventoryWindowSnapshot(process, candidate, rectSource, out var snapshot, out var readError))
                {
                    return OperationResult<InventoryWindowSnapshot>.Fail(readError);
                }

                _inventoryWindowCandidateCache[cacheKey] = candidate;
                LogInventoryWindowRead(context, process, snapshot, cacheHit: false);
                return OperationResult<InventoryWindowSnapshot>.Ok(snapshot);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("vmm.inventory_window.exception", ex, new Dictionary<string, object?> { ["account"] = context.AccountName });
            return OperationResult<InventoryWindowSnapshot>.Fail(ex.Message);
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
        var key = BuildConnectionKey(deviceName, remote);

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

            var vmm = new MemProcVmm(args);
            ConfigureVmmReadCache(vmm, deviceName, remote);

            var created = new VmmConnection(deviceName, remote, vmm);
            _connections[key] = created;
            _connectionRetryNotBefore.Remove(key);
            _logger.Info("vmm.connection.created", new Dictionary<string, object?>
            {
                ["device"] = deviceName,
                ["remote"] = remote
            });

            return created;
        }
    }

    private void ConfigureVmmReadCache(MemProcVmm vmm, string deviceName, string remote)
    {
        try
        {
            var previousTickPeriodMs = ReadVmmConfigOrDefault(vmm, VmmConfigTickPeriod, FallbackVmmTickPeriodMs);
            var previousTlbCacheTicks = vmm.GetConfig(VmmConfigTlbCacheTicks);
            var previousProcCacheTicksPartial = vmm.GetConfig(VmmConfigProcCacheTicksPartial);
            var previousProcCacheTicksTotal = vmm.GetConfig(VmmConfigProcCacheTicksTotal);
            var tickPeriodConfigured = vmm.SetConfig(VmmConfigTickPeriod, TargetVmmTickPeriodMs);
            var tickPeriodMs = tickPeriodConfigured ? TargetVmmTickPeriodMs : previousTickPeriodMs;
            var readCacheTicks = tickPeriodConfigured
                ? TargetVmmReadCacheTicks
                : CalculateVmmCacheTicks(TargetVmmReadCacheMs, tickPeriodMs);
            var preservedCacheOptions = tickPeriodConfigured
                ? PreserveVmmCachePeriods(
                    vmm,
                    previousTickPeriodMs,
                    tickPeriodMs,
                    previousTlbCacheTicks,
                    previousProcCacheTicksPartial,
                    previousProcCacheTicksTotal)
                : 0;
            var configured = vmm.SetConfig(VmmConfigReadCacheTicks, readCacheTicks);
            var fields = new Dictionary<string, object?>
            {
                ["device"] = deviceName,
                ["remote"] = remote,
                ["targetReadCacheMs"] = TargetVmmReadCacheMs,
                ["readCacheTicks"] = readCacheTicks,
                ["previousTickPeriodMs"] = previousTickPeriodMs,
                ["tickPeriodMs"] = tickPeriodMs,
                ["tickPeriodConfigured"] = tickPeriodConfigured,
                ["effectiveReadCacheMs"] = tickPeriodMs * readCacheTicks,
                ["preservedCacheOptions"] = preservedCacheOptions
            };

            if (configured)
            {
                _logger.Info("vmm.read_cache.configured", fields);
            }
            else
            {
                _logger.Warn("vmm.read_cache.configure_failed", fields);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn("vmm.read_cache.configure_failed", new Dictionary<string, object?>
            {
                ["device"] = deviceName,
                ["remote"] = remote,
                ["error"] = ex.Message
            });
        }
    }

    private static ulong ReadVmmConfigOrDefault(MemProcVmm vmm, ulong option, ulong fallback)
    {
        var value = vmm.GetConfig(option);
        return value == 0 ? fallback : value;
    }

    private static ulong CalculateVmmCacheTicks(ulong targetMs, ulong tickPeriodMs)
    {
        if (tickPeriodMs == 0)
        {
            return 1UL;
        }

        return Math.Max(1UL, (targetMs + tickPeriodMs - 1UL) / tickPeriodMs);
    }

    private static int PreserveVmmCachePeriods(
        MemProcVmm vmm,
        ulong previousTickPeriodMs,
        ulong tickPeriodMs,
        ulong previousTlbCacheTicks,
        ulong previousProcCacheTicksPartial,
        ulong previousProcCacheTicksTotal)
    {
        var preserved = 0;
        if (TryPreserveVmmCachePeriod(vmm, VmmConfigTlbCacheTicks, previousTlbCacheTicks, previousTickPeriodMs, tickPeriodMs))
        {
            preserved++;
        }

        if (TryPreserveVmmCachePeriod(vmm, VmmConfigProcCacheTicksPartial, previousProcCacheTicksPartial, previousTickPeriodMs, tickPeriodMs))
        {
            preserved++;
        }

        if (TryPreserveVmmCachePeriod(vmm, VmmConfigProcCacheTicksTotal, previousProcCacheTicksTotal, previousTickPeriodMs, tickPeriodMs))
        {
            preserved++;
        }

        return preserved;
    }

    private static bool TryPreserveVmmCachePeriod(
        MemProcVmm vmm,
        ulong option,
        ulong previousTicks,
        ulong previousTickPeriodMs,
        ulong tickPeriodMs)
    {
        if (previousTicks == 0 ||
            previousTickPeriodMs == 0 ||
            tickPeriodMs == 0 ||
            previousTickPeriodMs == tickPeriodMs)
        {
            return false;
        }

        var adjustedTicks = Math.Max(
            1UL,
            (ulong)Math.Ceiling(previousTicks * (double)previousTickPeriodMs / tickPeriodMs));
        return vmm.SetConfig(option, adjustedTicks);
    }

    private void ResetConnection(string? contextVmmDeviceName, string accountName, string reason, string? error)
    {
        var deviceName = ResolveVmmDeviceName(contextVmmDeviceName);
        var remote = ResolveVmmRemote();
        var key = BuildConnectionKey(deviceName, remote);
        VmmConnection? removed = null;

        lock (_connectionSync)
        {
            if (_connections.TryGetValue(key, out removed))
            {
                _connections.Remove(key);
            }

            _connectionRetryNotBefore[key] = DateTimeOffset.Now + VmmReconnectDelay;
        }

        if (removed?.Vmm is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch
            {
                // Reconnect best-effort; disposing an already unhealthy VMM handle must not hide the original read failure.
            }
        }

        _logger.Warn("vmm.connection.reset", new Dictionary<string, object?>
        {
            ["account"] = accountName,
            ["device"] = deviceName,
            ["remote"] = remote,
            ["reason"] = reason,
            ["error"] = error ?? string.Empty,
            ["hadConnection"] = removed is not null,
            ["retryAfterMs"] = (int)VmmReconnectDelay.TotalMilliseconds
        });
    }

    private static string BuildConnectionKey(string deviceName, string remote)
    {
        return deviceName + "|" + remote;
    }

    private string BuildConnectionKey(string? contextVmmDeviceName)
    {
        return BuildConnectionKey(ResolveVmmDeviceName(contextVmmDeviceName), ResolveVmmRemote());
    }

    private string BuildPlayerReadFailureKey(GameApiReadContext context)
    {
        return BuildConnectionKey(context.VmmDeviceName) + "|" + context.AccountName;
    }

    private int RecordPlayerReadFailure(GameApiReadContext context)
    {
        var key = BuildPlayerReadFailureKey(context);
        lock (_connectionSync)
        {
            _playerReadFailureCounts.TryGetValue(key, out var count);
            count++;
            _playerReadFailureCounts[key] = count;
            return count;
        }
    }

    private void ClearPlayerReadFailure(GameApiReadContext context)
    {
        var key = BuildPlayerReadFailureKey(context);
        lock (_connectionSync)
        {
            _playerReadFailureCounts.Remove(key);
        }
    }

    private void DelayConnectionRetry(string? contextVmmDeviceName, TimeSpan delay)
    {
        var key = BuildConnectionKey(contextVmmDeviceName);
        lock (_connectionSync)
        {
            _connectionRetryNotBefore[key] = DateTimeOffset.Now + delay;
        }
    }

    private bool TryGetConnectionRetryDelay(string? contextVmmDeviceName, out int retryAfterMs)
    {
        retryAfterMs = 0;
        var key = BuildConnectionKey(contextVmmDeviceName);
        lock (_connectionSync)
        {
            if (!_connectionRetryNotBefore.TryGetValue(key, out var notBefore))
            {
                return false;
            }

            var remaining = notBefore - DateTimeOffset.Now;
            if (remaining <= TimeSpan.Zero)
            {
                _connectionRetryNotBefore.Remove(key);
                return false;
            }

            retryAfterMs = Math.Max(1, (int)Math.Ceiling(remaining.TotalMilliseconds));
            return true;
        }
    }

    private static bool ShouldReconnectAfterPlayerReadFailure(string? error)
    {
        return !string.IsNullOrWhiteSpace(error) &&
               error.IndexOf("failed to read local entity id", StringComparison.OrdinalIgnoreCase) >= 0;
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
        detail.SkillCategory = GetSkillXmlValue(element, "skill_category", "skillcategory");
        detail.SkillType = GetSkillXmlValue(element, "type");
        detail.SubType = GetSkillXmlValue(element, "sub_type", "subtype");
        detail.ActivationAttribute = GetSkillXmlValue(element, "activation_attribute", "activationattribute");
        detail.TargetSlot = GetSkillXmlValue(element, "target_slot", "targetslot");
        detail.DispelCategory = GetSkillXmlValue(element, "dispel_category", "dispelcategory");
        detail.FirstTarget = GetSkillXmlValue(element, "first_target", "firsttarget");
        detail.TargetRelationRestriction = GetSkillXmlValue(element, "target_relation_restriction", "targetrelationrestriction");
        detail.TargetRange = GetSkillXmlValue(element, "target_range", "targetrange");
        detail.ChainCategoryName = GetSkillXmlValue(element, "chain_category_name", "chaincategoryname");
        detail.PrechainCategoryName = GetSkillXmlValue(element, "prechain_category_name", "prechaincategoryname");
        detail.ChainTime = GetSkillXmlValue(element, "chain_time", "chaintime");
        detail.StatusFx = GetSkillXmlValue(element, "status_fx", "statusfx");
        detail.AuraFx = GetSkillXmlValue(element, "aura_fx", "aurafx");
        detail.CounterSkill = GetSkillXmlValue(element, "counter_skill", "counterskill");
        detail.TargetValidStatuses = FormatSkillXmlTargetValidStatuses(element);
        detail.CostDp = GetSkillXmlValue(element, "cost_dp", "costdp");
        detail.UltraSkill = GetSkillXmlValue(element, "ultra_skill", "ultraskill");
        detail.Effect1Type = GetSkillXmlValue(element, "effect1_type", "effect_1_type", "effect1type");
        detail.Effect2Type = GetSkillXmlValue(element, "effect2_type", "effect_2_type", "effect2type");
        detail.Effect3Type = GetSkillXmlValue(element, "effect3_type", "effect_3_type", "effect3type");
        detail.Effect4Type = GetSkillXmlValue(element, "effect4_type", "effect_4_type", "effect4type");
        detail.EffectRemainMs = GetMaxSkillXmlIntValue(
            element,
            "effect1_remain1",
            "effect1_remain2",
            "effect2_remain1",
            "effect2_remain2",
            "effect3_remain1",
            "effect3_remain2",
            "effect4_remain1",
            "effect4_remain2");
        detail.EffectCheckTimeMs = GetMaxSkillXmlIntValue(
            element,
            "effect1_checktime",
            "effect2_checktime",
            "effect3_checktime",
            "effect4_checktime");
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

    private static int? GetMaxSkillXmlIntValue(XElement element, params string[] names)
    {
        int? max = null;
        foreach (var name in names)
        {
            if (!TryGetSkillXmlValue(element, out var value, name) ||
                !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ||
                parsed <= 0)
            {
                continue;
            }

            max = max is null ? parsed : Math.Max(max.Value, parsed);
        }

        return max;
    }

    private static string FormatSkillXmlTargetValidStatuses(XElement element)
    {
        var statuses = new List<string>();
        for (var i = 1; i <= 8; i++)
        {
            var value = GetSkillXmlValue(
                element,
                "target_valid_status" + i.ToString(CultureInfo.InvariantCulture),
                "targetvalidstatus" + i.ToString(CultureInfo.InvariantCulture));
            if (HasUsefulSkillXmlValue(value))
            {
                statuses.Add(value.Trim());
            }
        }

        return statuses.Count == 0
            ? string.Empty
            : string.Join(",", statuses.Distinct(StringComparer.OrdinalIgnoreCase));
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

    private static bool TryReadInventoryItems(
        VmmProcess process,
        ulong gameBase,
        out List<InventoryItemInfo> items,
        out string error)
    {
        items = new List<InventoryItemInfo>();
        error = string.Empty;

        if (!TryReadPointer(process, gameBase + InventoryManagerGlobalRva, out var manager) || manager == 0)
        {
            error = "failed to read InventoryManager pointer at Game.dll+0x" + InventoryManagerGlobalRva.ToString("X");
            return false;
        }

        var equipmentInstanceIds = ReadInventoryEquipmentInstanceIds(process, manager);
        TryReadUInt64(process, manager + InventoryItemTreeCountOffset, out var treeCount);

        if (!TryReadPointer(process, manager + InventoryItemTreeHeaderOffset, out var header) || header == 0)
        {
            error = "failed to read inventory item tree header at InventoryManager+0x" + InventoryItemTreeHeaderOffset.ToString("X");
            return false;
        }

        if (!TryReadPointer(process, header + NodeLeftOffset, out var node))
        {
            error = "failed to read inventory item tree begin node";
            return false;
        }

        var visited = new HashSet<ulong>();
        var guardLimit = treeCount is > 0 and < 100000
            ? checked((int)treeCount + 16)
            : 100000;

        for (var guard = 0; node != 0 && node != header && guard < guardLimit; guard++)
        {
            if (!visited.Add(node) || IsNilNode(process, node, header))
            {
                break;
            }

            if (TryReadInventoryItemFromNode(process, node, equipmentInstanceIds, out var item))
            {
                items.Add(item);
            }

            if (!TryGetNextTreeNode(process, header, node, out var next) || next == node)
            {
                break;
            }

            node = next;
        }

        return true;
    }

    private static bool TryReadInventoryItemFromNode(
        VmmProcess process,
        ulong node,
        IReadOnlyCollection<uint> equipmentInstanceIds,
        out InventoryItemInfo info)
    {
        info = new InventoryItemInfo
        {
            Name = string.Empty,
            Slot = -1
        };

        if (!TryReadUInt32(process, node + InventoryNodeInstanceIdOffset, out var nodeInstanceId) ||
            !TryReadPointer(process, node + InventoryNodeItemOffset, out var item) ||
            item == 0)
        {
            return false;
        }

        if (!TryReadUInt32(process, item + InventoryItemInstanceIdOffset, out var instanceId) ||
            instanceId == 0 ||
            instanceId != nodeInstanceId)
        {
            return false;
        }

        info.InstanceId = instanceId;
        info.IsInEquipmentArray = ContainsUInt32(equipmentInstanceIds, instanceId);

        TryReadUInt32(process, item + InventoryItemTemplateIdOffset, out info.TemplateId);

        if (TryReadUInt64(process, item + InventoryItemCountOffset, out var count))
        {
            info.Count = count;
        }

        if (TryReadMsvcWString(process, item + InventoryItemNameOffset, out var name))
        {
            info.Name = name;
        }

        TryReadUInt32(process, item + InventoryItemTypeOffset, out info.ItemType);
        TryReadUInt32(process, item + InventoryItemEquipmentMaskOffset, out info.EquipmentMask);
        TryReadUInt64(process, item + InventoryItemVendorSellUnitPriceOffset, out info.VendorSellUnitPrice);

        if (TryReadInt16(process, item + InventoryItemSlotOffset, out var slot))
        {
            info.Slot = slot;
        }

        return true;
    }

    private static uint[] ReadInventoryEquipmentInstanceIds(VmmProcess process, ulong manager)
    {
        var result = new uint[InventoryEquipmentIdCount];
        for (var i = 0; i < result.Length; i++)
        {
            if (TryReadUInt32(process, manager + InventoryEquipmentIdsOffset + (ulong)(i * 4), out var value))
            {
                result[i] = value;
            }
        }

        return result;
    }

    private static bool IsNormalBagInventoryItem(InventoryItemInfo item)
    {
        return item.Slot >= 0;
    }

    private static bool IsEquippedInventoryItem(InventoryItemInfo item)
    {
        return item.Slot < 0 || item.IsInEquipmentArray;
    }

    private static bool ContainsUInt32(IEnumerable<uint> values, uint value)
    {
        return value != 0 && values.Any(candidate => candidate == value);
    }

    private static uint ClampInventoryCount(ulong count)
    {
        return count > uint.MaxValue ? uint.MaxValue : (uint)count;
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
        var targetValidStatuses = skill.HasXmlStaticDetail ? EmptyToNull(skill.XmlStaticDetail.TargetValidStatuses) : null;
        var costDp = skill.HasXmlStaticDetail ? EmptyToNull(skill.XmlStaticDetail.CostDp) : null;
        var skillCategory = skill.HasXmlStaticDetail ? EmptyToNull(skill.XmlStaticDetail.SkillCategory) : null;
        var skillType = skill.HasXmlStaticDetail ? EmptyToNull(skill.XmlStaticDetail.SkillType) : null;
        var subType = skill.HasXmlStaticDetail ? EmptyToNull(skill.XmlStaticDetail.SubType) : null;
        var dispelCategory = skill.HasXmlStaticDetail ? EmptyToNull(skill.XmlStaticDetail.DispelCategory) : null;
        var firstTarget = skill.HasXmlStaticDetail ? EmptyToNull(skill.XmlStaticDetail.FirstTarget) : null;
        var targetRelation = skill.HasXmlStaticDetail ? EmptyToNull(skill.XmlStaticDetail.TargetRelationRestriction) : null;
        var targetRange = skill.HasXmlStaticDetail ? EmptyToNull(skill.XmlStaticDetail.TargetRange) : null;
        var effects = skill.HasXmlStaticDetail ? FormatSkillXmlEffects(skill.XmlStaticDetail) : null;
        var effectRemainMs = skill.HasXmlStaticDetail ? skill.XmlStaticDetail.EffectRemainMs : null;
        var effectCheckTimeMs = skill.HasXmlStaticDetail ? skill.XmlStaticDetail.EffectCheckTimeMs : null;

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
            costDp,
            skillCategory,
            skillType,
            subType,
            dispelCategory,
            firstTarget,
            targetRelation,
            targetRange,
            effects,
            effectRemainMs,
            effectCheckTimeMs,
            targetValidStatuses);
    }

    private static string? FormatSkillXmlEffects(SkillXmlStaticDetail detail)
    {
        var values = new[]
            {
                detail.Effect1Type,
                detail.Effect2Type,
                detail.Effect3Type,
                detail.Effect4Type
            }
            .Where(HasUsefulSkillXmlValue)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return values.Length == 0 ? null : string.Join(",", values);
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

        if (HasUsefulSkillXmlValue(detail.TargetValidStatuses))
        {
            tags.Add("condition");
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
        bool bypassMemoryCache,
        out LockedTargetInfo info,
        out string error)
    {
        info = new LockedTargetInfo();
        error = string.Empty;

        if (!TryReadUInt16(process, gameBase + LocalEntityIdRva + 2, out info.TargetEntityId, bypassMemoryCache))
        {
            error = "failed to read current target entity id at Game.dll+0x" + (LocalEntityIdRva + 2).ToString("X");
            return false;
        }

        if (info.TargetEntityId == 0)
        {
            return true;
        }

        if (TryFindServerObjectByEntityId(process, gameBase, info.TargetEntityId, out var serverObjectId, out _, bypassMemoryCache))
        {
            info.ServerObjectId = serverObjectId;
        }

        if (!TryReadPointer(process, gameBase + EntitySystemPointerRva, out var entitySystem, bypassMemoryCache))
        {
            error = "failed to read EntitySystem pointer at Game.dll+0x" + EntitySystemPointerRva.ToString("X");
            return false;
        }

        if (!TryReadPointer(process, entitySystem + EntityTreeOffset, out var entityTreeHeader, bypassMemoryCache))
        {
            error = "failed to read EntitySystem tree header at EntitySystem+0x" + EntityTreeOffset.ToString("X");
            return false;
        }

        if (!TryFindEntityById(process, entityTreeHeader, info.TargetEntityId, out info.Entity, bypassMemoryCache))
        {
            error = "target entity id " + info.TargetEntityId + " was not found in EntitySystem tree";
            return false;
        }

        TryReadUInt16(process, info.Entity + EntityTypeOffset, out info.EntityType, bypassMemoryCache);

        if (TryReadEntityPosition(process, info.Entity, out var x, out var y, out var z, bypassMemoryCache))
        {
            info.Position = new Vector3Snapshot(x, y, z);
        }

        if (TryReadUInt16(process, gameBase + LocalEntityIdRva, out var localEntityId, bypassMemoryCache) &&
            TryFindEntityById(process, entityTreeHeader, localEntityId, out var localEntity, bypassMemoryCache))
        {
            if (TryReadEntityPosition(process, localEntity, out var localX, out var localY, out var localZ, bypassMemoryCache) &&
                info.Position is { } targetPosition)
            {
                var dx = targetPosition.X - localX;
                var dy = targetPosition.Y - localY;
                var dz = targetPosition.Z - localZ;
                info.DistanceToLocalPlayer = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }

            if (TryResolveActorFromEntity(process, localEntity, 0, out var localActor, bypassMemoryCache))
            {
                info.LocalServerObjectId = localActor.ServerObjectId;
            }
        }

        if (TryResolveActorFromEntity(process, info.Entity, info.ServerObjectId, out var actor, bypassMemoryCache))
        {
            info.Actor = actor;
        }

        return true;
    }

    private static bool TryReadLocalPlayer(
        VmmProcess process,
        ulong gameBase,
        bool bypassMemoryCache,
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

        if (!TryReadEntityPosition(process, localEntity, out var x, out var y, out var z, bypassMemoryCache))
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
        ushort characterLevel = 0;
        AionClassId? characterClassId = null;
        var characterClass = string.Empty;
        uint stanceFlags = 0;
        uint motionMode = 0;
        double? actorYaw = null;
        if (TryResolveActorFromEntity(process, localEntity, 0, out var actor))
        {
            if (maxHp == 0 && actor.MaxHp > 0)
            {
                currentHp = actor.CurrentHp;
                maxHp = actor.MaxHp;
            }

            characterName = actor.Name;
            characterLevel = actor.Level;
            if (TryReadActorClassId(process, actor.Actor, out var resolvedClassId))
            {
                characterClassId = resolvedClassId;
                characterClass = AionClassCatalog.GetChineseName(resolvedClassId);
            }

            TryReadUInt32(process, actor.Actor + ActorStanceFlagsOffset, out stanceFlags);
            TryReadUInt32(process, actor.Actor + ActorMotionModeOffset, out motionMode);
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
            actorYaw,
            Level: characterLevel,
            CharacterClass: characterClass,
            StanceFlags: stanceFlags,
            MotionMode: motionMode,
            CharacterClassId: characterClassId);
        return true;
    }

    private static bool TryReadActorClassId(
        VmmProcess process,
        ulong actorAddress,
        out AionClassId classId)
    {
        classId = default;
        return actorAddress != 0 &&
               TryReadUInt32(process, actorAddress + ActorClassIdOffset, out var rawClassId) &&
               AionClassCatalog.TryFromRaw(rawClassId, out classId);
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

    private static bool TryReadPartySnapshot(
        VmmProcess process,
        ulong gameBase,
        out PartySnapshot snapshot,
        out string error)
    {
        var capturedAt = DateTimeOffset.Now;
        snapshot = PartySnapshot.Empty(capturedAt);
        error = string.Empty;

        if (!TryReadPartyMemberSnapshots(process, gameBase, out var members, out var memberReadError))
        {
            error = memberReadError;
            return false;
        }

        TryReadUInt32(process, gameBase + PartyIdRva, out var partyId);
        TryReadUInt32(process, gameBase + PartyFlagsRva, out var partyFlags);
        TryReadUInt32(process, gameBase + PartyLeaderServerObjectIdRva, out var leaderServerObjectId);
        TryReadUInt64(process, gameBase + PrimaryPartyCountRva, out var primaryPartyCount);

        var liveActorReadError = string.Empty;
        var localServerObjectId = 0U;
        var localEntityId = (ushort)0;
        var localName = string.Empty;
        var localPosition = default(Vector3Snapshot?);
        var localTargetServerObjectId = 0U;
        var visiblePlayerActorCount = 0;

        if (TryReadPartyLiveContext(process, gameBase, out var liveContext, out liveActorReadError))
        {
            localServerObjectId = liveContext.LocalServerObjectId;
            localEntityId = liveContext.LocalEntityId;
            localName = liveContext.LocalName;
            localPosition = liveContext.LocalPosition;
            localTargetServerObjectId = liveContext.LocalTargetServerObjectId;
            visiblePlayerActorCount = liveContext.VisiblePlayerActorsByServerObjectId.Count;
            members = ApplyPartyMemberLiveContext(members, leaderServerObjectId, liveContext);
        }
        else
        {
            members = ApplyPartyLeaderOnly(members, leaderServerObjectId);
        }

        snapshot = new PartySnapshot(
            partyId,
            partyFlags,
            primaryPartyCount,
            leaderServerObjectId,
            localServerObjectId,
            localEntityId,
            localName,
            localPosition,
            localTargetServerObjectId,
            visiblePlayerActorCount,
            capturedAt,
            members,
            memberReadError,
            liveActorReadError);
        return true;
    }

    private static bool TryReadPartyMemberSnapshots(
        VmmProcess process,
        ulong gameBase,
        out IReadOnlyList<PartyMemberSnapshot> snapshots,
        out string error)
    {
        var result = new List<PartyMemberSnapshot>();
        var seen = new HashSet<uint>();
        var errors = new List<string>();

        if (!ReadPartyMemberSnapshotList(process, gameBase + PrimaryPartyListRva, "primary", result, seen, out var primaryError))
        {
            errors.Add(primaryError);
        }

        if (!ReadPartyMemberSnapshotList(process, gameBase + SecondaryPartyListRva, "secondary", result, seen, out var secondaryError))
        {
            errors.Add(secondaryError);
        }

        snapshots = result;
        error = errors.Count == 0 ? string.Empty : string.Join("; ", errors);
        return result.Count > 0 || errors.Count < 2;
    }

    private static bool ReadPartyMemberSnapshotList(
        VmmProcess process,
        ulong listGlobalAddress,
        string listName,
        List<PartyMemberSnapshot> snapshots,
        HashSet<uint> seenServerObjectIds,
        out string error)
    {
        error = string.Empty;

        if (!TryReadPointer(process, listGlobalAddress, out var head) || head == 0)
        {
            error = "failed to read " + listName + " party list head at 0x" + listGlobalAddress.ToString("X");
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

            if (TryReadPointer(process, node + ListNodeValueOffset, out var member) &&
                member != 0 &&
                TryReadPartyMemberSnapshot(process, member, node, listName, listIndex, out var snapshot) &&
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

    private static bool TryReadPartyMemberSnapshot(
        VmmProcess process,
        ulong member,
        ulong node,
        string listName,
        int listIndex,
        out PartyMemberSnapshot snapshot)
    {
        snapshot = CreateEmptyPartyMemberSnapshot(listName, listIndex, node, member);
        if (!IsLikelyUserPointer(member))
        {
            return false;
        }

        TryReadUInt32(process, member + PartyMemberPartySlotOffset, out var partySlot);
        TryReadUInt32(process, member + PartyMemberServerObjectIdOffset, out var serverObjectId);
        TryReadUInt32(process, member + PartyMemberMaxHpOffset, out var maxHp);
        TryReadUInt32(process, member + PartyMemberCurrentHpOffset, out var currentHp);
        TryReadUInt32(process, member + PartyMemberMaxMpOffset, out var maxMp);
        TryReadUInt32(process, member + PartyMemberCurrentMpOffset, out var currentMp);
        TryReadUInt32(process, member + PartyMemberMaxFlightTimeOffset, out var maxFlightTime);
        TryReadUInt32(process, member + PartyMemberCurrentFlightTimeOffset, out var currentFlightTime);
        TryReadUInt32(process, member + PartyMemberAreaField0Offset, out var areaField0);
        TryReadUInt32(process, member + PartyMemberAreaField1Offset, out var areaField1);
        TryReadSingle(process, member + PartyMemberCachedXOffset, out var cachedX);
        TryReadSingle(process, member + PartyMemberCachedYOffset, out var cachedY);
        TryReadSingle(process, member + PartyMemberCachedZOffset, out var cachedZ);
        TryReadByte(process, member + PartyMemberClassIdOffset, out var classId);
        TryReadByte(process, member + PartyMemberLevelOffset, out var level);
        TryReadByte(process, member + PartyMemberDataFlagsOffset, out var dataFlags);
        TryReadByte(process, member + PartyMemberFlightAreaFlagOffset, out var flightAreaFlag);
        TryReadByte(process, member + PartyMemberFlightFlagsOffset, out var flightFlags);
        TryReadByte(process, member + PartyMemberRuntimeStateOffset, out var runtimeState);
        TryReadUtf16String(process, member + PartyMemberNameOffset, 26, out var name);
        TryReadUInt64(process, member + PartyMemberControlStatusMaskOffset, out var controlStatusMask);
        TryReadInt16(process, member + PartyMemberAbnormalCountOffset, out var rawAbnormalCount);
        TryReadUInt32(process, member + PartyMemberUpdateTimeOffset, out var updateTime);

        var hasAbnormalBlock = (dataFlags & PartyMemberHasAbnormalBlockFlag) != 0;
        var count = rawAbnormalCount;
        if (count < 0)
        {
            count = 0;
        }
        else if (count > PartyMemberMaxAbnormalCount)
        {
            count = PartyMemberMaxAbnormalCount;
        }

        var entries = new List<AbnormalStatusEntrySnapshot>();
        var entriesAddress = member + PartyMemberAbnormalEntriesOffset;
        for (var i = 0; i < count; i++)
        {
            if (TryReadPartyMemberAbnormalStatusEntry(process, entriesAddress + (ulong)i * AbnormalStatusEntrySize, out var entry))
            {
                entries.Add(entry);
            }
        }

        AionClassId? resolvedClass = null;
        var className = string.Empty;
        if (AionClassCatalog.TryFromRaw(classId, out var knownClass))
        {
            resolvedClass = knownClass;
            className = AionClassCatalog.GetChineseName(knownClass);
        }

        snapshot = new PartyMemberSnapshot(
            listName,
            listIndex,
            node,
            member,
            partySlot,
            serverObjectId,
            name,
            classId,
            resolvedClass,
            className,
            level,
            currentHp,
            maxHp,
            currentMp,
            maxMp,
            currentFlightTime,
            maxFlightTime,
            areaField0,
            areaField1,
            new Vector3Snapshot(cachedX, cachedY, cachedZ),
            dataFlags,
            flightAreaFlag,
            flightFlags,
            runtimeState,
            controlStatusMask,
            hasAbnormalBlock,
            rawAbnormalCount,
            updateTime,
            entries,
            false,
            false,
            false,
            0,
            0,
            0,
            string.Empty,
            0,
            null,
            null,
            PartyMemberVisibilityState.Unknown);

        return snapshot.ServerObjectId != 0 ||
               !string.IsNullOrWhiteSpace(snapshot.Name) ||
               snapshot.MaxHp != 0 ||
               snapshot.MaxMp != 0 ||
               snapshot.RawAbnormalCount != 0 ||
               snapshot.HasAbnormalBlock;
    }

    private static PartyMemberSnapshot CreateEmptyPartyMemberSnapshot(
        string listName,
        int listIndex,
        ulong node,
        ulong member)
    {
        return new PartyMemberSnapshot(
            listName,
            listIndex,
            node,
            member,
            0,
            0,
            string.Empty,
            0,
            null,
            string.Empty,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            new Vector3Snapshot(),
            0,
            0,
            0,
            0,
            0,
            false,
            0,
            0,
            Array.Empty<AbnormalStatusEntrySnapshot>(),
            false,
            false,
            false,
            0,
            0,
            0,
            string.Empty,
            0,
            null,
            null,
            PartyMemberVisibilityState.Unknown);
    }

    private static bool TryReadPartyMemberAbnormalStatusEntry(
        VmmProcess process,
        ulong address,
        out AbnormalStatusEntrySnapshot entry)
    {
        entry = new AbnormalStatusEntrySnapshot(0, 0, 0, 0, 0, address);
        if (!TryReadUInt32(process, address + 0x00, out var field00) ||
            !TryReadUInt32(process, address + 0x04, out var abnormalId) ||
            !TryReadUInt32(process, address + 0x08, out var category))
        {
            return false;
        }

        TryReadUInt32(process, address + 0x0C, out var rawTimeOrSource);
        TryReadUInt16(process, address + 0x10, out var levelOrStack);
        entry = new AbnormalStatusEntrySnapshot(
            field00,
            abnormalId,
            category,
            unchecked((int)rawTimeOrSource),
            levelOrStack,
            address);
        return true;
    }

    private static bool TryReadPartyLiveContext(
        VmmProcess process,
        ulong gameBase,
        out PartyLiveContext context,
        out string error)
    {
        context = new PartyLiveContext();
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

        if (!TryFindEntityById(process, entityTreeHeader, localEntityId, out var localEntity))
        {
            error = "local entity id " + localEntityId.ToString(CultureInfo.InvariantCulture) + " was not found in EntitySystem tree";
            return false;
        }

        if (!TryResolveActorFromEntity(process, localEntity, 0, out var localActor) ||
            localActor.ServerObjectId == 0)
        {
            error = "failed to resolve local actor/server object id";
            return false;
        }

        context.LocalEntityId = localEntityId;
        context.LocalEntityAddress = localEntity;
        context.LocalActorAddress = localActor.Actor;
        context.LocalServerObjectId = localActor.ServerObjectId;
        context.LocalTargetServerObjectId = localActor.TargetServerObjectId;
        context.LocalName = localActor.Name;

        if (TryReadEntityPosition(process, localEntity, out var localX, out var localY, out var localZ) &&
            IsReasonablePosition(localX, localY, localZ))
        {
            context.LocalPosition = new Vector3Snapshot(localX, localY, localZ);
        }

        if (!TryReadVisibleActorInfos(process, gameBase, entityTreeHeader, localEntityId, out var visibleActors, out error))
        {
            return false;
        }

        foreach (var actor in visibleActors)
        {
            if (actor.Actor.ObjectType != ActorPlayerObjectType || actor.Actor.ServerObjectId == 0)
            {
                continue;
            }

            var livePosition = default(Vector3Snapshot?);
            if (TryReadEntityPosition(process, actor.Entity, out var x, out var y, out var z) &&
                IsReasonablePosition(x, y, z))
            {
                livePosition = new Vector3Snapshot(x, y, z);
            }

            context.VisiblePlayerActorsByServerObjectId[actor.Actor.ServerObjectId] = new PartyLiveActorInfo(
                actor.EntityId,
                actor.Entity,
                actor.Actor.Actor,
                actor.Actor.Name,
                actor.Actor.TargetServerObjectId,
                actor.Actor.HasRestState,
                actor.Actor.StanceFlags,
                actor.Actor.MotionMode,
                livePosition);
        }

        return true;
    }

    private static IReadOnlyList<PartyMemberSnapshot> ApplyPartyLeaderOnly(
        IReadOnlyList<PartyMemberSnapshot> members,
        uint leaderServerObjectId)
    {
        return members
            .Select(member => member with
            {
                IsLeader = leaderServerObjectId != 0 && member.ServerObjectId == leaderServerObjectId
            })
            .ToArray();
    }

    private static IReadOnlyList<PartyMemberSnapshot> ApplyPartyMemberLiveContext(
        IReadOnlyList<PartyMemberSnapshot> members,
        uint leaderServerObjectId,
        PartyLiveContext context)
    {
        var result = new List<PartyMemberSnapshot>(members.Count);
        foreach (var member in members)
        {
            var isSelf = member.ServerObjectId != 0 &&
                         member.ServerObjectId == context.LocalServerObjectId;
            var isLeader = leaderServerObjectId != 0 &&
                           member.ServerObjectId == leaderServerObjectId;
            var visibility = PartyMemberVisibilityState.NotLoaded;
            var hasLiveActor = false;
            var liveEntityId = (ushort)0;
            var liveEntityAddress = 0UL;
            var liveActorAddress = 0UL;
            var liveActorName = string.Empty;
            var liveTargetServerObjectId = 0U;
            var livePosition = default(Vector3Snapshot?);
            var hasLiveRestState = false;
            var liveStanceFlags = 0U;
            var liveMotionMode = 0U;
            var distanceToLocal = default(double?);

            if (member.ServerObjectId != 0 &&
                context.VisiblePlayerActorsByServerObjectId.TryGetValue(member.ServerObjectId, out var actor))
            {
                hasLiveActor = true;
                liveEntityId = actor.EntityId;
                liveEntityAddress = actor.EntityAddress;
                liveActorAddress = actor.ActorAddress;
                liveActorName = actor.ActorName;
                liveTargetServerObjectId = actor.TargetServerObjectId;
                hasLiveRestState = actor.HasRestState;
                liveStanceFlags = actor.StanceFlags;
                liveMotionMode = actor.MotionMode;

                if (actor.Position is { } position)
                {
                    livePosition = position;
                    if (context.LocalPosition is { } localPosition)
                    {
                        var dx = position.X - localPosition.X;
                        var dy = position.Y - localPosition.Y;
                        var dz = position.Z - localPosition.Z;
                        distanceToLocal = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                        visibility = distanceToLocal <= 50.0D
                            ? PartyMemberVisibilityState.ScreenVisible
                            : PartyMemberVisibilityState.LoadedOutOfRange;
                    }
                    else
                    {
                        visibility = PartyMemberVisibilityState.LoadedDistanceUnknown;
                    }
                }
                else
                {
                    visibility = PartyMemberVisibilityState.LoadedPositionUnknown;
                }
            }

            result.Add(member with
            {
                IsSelf = isSelf,
                IsLeader = isLeader,
                HasLiveActor = hasLiveActor,
                LiveEntityId = liveEntityId,
                LiveEntityAddress = liveEntityAddress,
                LiveActorAddress = liveActorAddress,
                LiveActorName = liveActorName,
                LiveTargetServerObjectId = liveTargetServerObjectId,
                LivePosition = livePosition,
                DistanceToLocalPlayer = distanceToLocal,
                VisibilityState = visibility,
                HasLiveRestState = hasLiveRestState,
                LiveStanceFlags = liveStanceFlags,
                LiveMotionMode = liveMotionMode
            });
        }

        return result;
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
        return ToLockedTargetSnapshot(info, DateTimeOffset.Now);
    }

    private static LockedTargetSnapshot ToLockedTargetSnapshot(LockedTargetInfo info, DateTimeOffset capturedAt)
    {
        if (info.TargetEntityId == 0)
        {
            return LockedTargetSnapshot.Empty(capturedAt);
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
            capturedAt,
            targetServerObjectId,
            info.LocalServerObjectId != 0 && targetServerObjectId == info.LocalServerObjectId,
            info.LocalServerObjectId,
            info.Actor?.LootableRaw ?? 0,
            info.Actor?.InteractionState ?? 0);
    }

    private static bool TryReadSummonedPet(
        VmmProcess process,
        ulong gameBase,
        IReadOnlyDictionary<uint, NpcStaticDetail> npcStaticDetails,
        out SummonedPetSnapshot snapshot,
        out string error)
    {
        var capturedAt = DateTimeOffset.Now;
        snapshot = SummonedPetSnapshot.NotSummoned(0, capturedAt);
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

        if (!TryFindEntityById(process, entityTreeHeader, localEntityId, out var localEntity))
        {
            error = "local entity id " + localEntityId + " was not found in EntitySystem tree";
            return false;
        }

        if (!TryResolveActorFromEntity(process, localEntity, 0, out var localActor) ||
            localActor.ServerObjectId == 0)
        {
            error = "failed to resolve local actor/server object id";
            return false;
        }

        TryReadUInt32(
            process,
            localActor.Actor + ActorCurrentSummonedPetServerObjectIdOffset,
            out var linkedPetServerObjectId);

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

        var localPositionKnown = TryReadEntityPosition(process, localEntity, out var localX, out var localY, out var localZ);
        for (var guard = 0; node != 0 && node != serverTreeHeader && guard < 100000; guard++)
        {
            if (IsNilNode(process, node, serverTreeHeader))
            {
                break;
            }

            if (TryReadUInt32(process, node + ServerNodeServerObjectIdOffset, out var serverObjectId) &&
                TryReadUInt16(process, node + ServerNodeEntityIdOffset, out var entityId) &&
                entityId != 0 &&
                entityId != localEntityId &&
                TryFindEntityById(process, entityTreeHeader, entityId, out var entity) &&
                entity != 0 &&
                TryResolveActorFromEntity(process, entity, serverObjectId, out var actor) &&
                actor.ObjectType == SummonedPetSnapshot.ActorObjectType)
            {
                var actorServerObjectId = actor.ServerObjectId != 0 ? actor.ServerObjectId : serverObjectId;
                var localLinkMatches = linkedPetServerObjectId != 0 && actorServerObjectId == linkedPetServerObjectId;
                var ownerConfirmed =
                    TryReadUInt32(process, actor.Actor + ActorSummonOwnerServerObjectIdOffset, out var ownerServerObjectId) &&
                    ownerServerObjectId == localActor.ServerObjectId;
                var hasStaticDetail = npcStaticDetails.TryGetValue(actor.NpcTemplateId, out var npcStaticDetail);
                var isSummonPetStatic = hasStaticDetail && IsSummonPetNpcStaticDetail(npcStaticDetail);

                if (localLinkMatches && ownerConfirmed && isSummonPetStatic)
                {
                    Vector3Snapshot? position = null;
                    double? distance = null;
                    if (TryReadEntityPosition(process, entity, out var x, out var y, out var z) &&
                        IsReasonablePosition(x, y, z))
                    {
                        position = new Vector3Snapshot(x, y, z);
                        if (localPositionKnown)
                        {
                            var dx = x - localX;
                            var dy = y - localY;
                            var dz = z - localZ;
                            distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                        }
                    }

                    snapshot = new SummonedPetSnapshot(
                        true,
                        entityId,
                        actorServerObjectId,
                        TryReadUInt16(process, entity + EntityTypeOffset, out var entityType) ? entityType : (ushort)0,
                        actor.ObjectType,
                        actor.NpcTemplateId,
                        actor.Name,
                        hasStaticDetail ? npcStaticDetail.Name : string.Empty,
                        hasStaticDetail ? npcStaticDetail.NpcType : string.Empty,
                        hasStaticDetail ? npcStaticDetail.Tribe : string.Empty,
                        actor.Level,
                        actor.CurrentHp,
                        actor.MaxHp,
                        actor.HpPercent,
                        position,
                        distance,
                        localActor.ServerObjectId,
                        capturedAt,
                        linkedPetServerObjectId,
                        ownerConfirmed,
                        BuildSummonedPetEvidenceSource(localLinkMatches, ownerConfirmed, isSummonPetStatic));
                    return true;
                }
            }

            if (!TryGetNextTreeNode(process, serverTreeHeader, node, out var next) || next == node)
            {
                break;
            }

            node = next;
        }

        snapshot = SummonedPetSnapshot.NotSummoned(localActor.ServerObjectId, capturedAt);
        return true;
    }

    private static bool TryReadSummonedPetRoster(
        VmmProcess process,
        ulong gameBase,
        IReadOnlyDictionary<uint, NpcStaticDetail> npcStaticDetails,
        out SummonedPetRosterSnapshot snapshot,
        out string error)
    {
        var capturedAt = DateTimeOffset.Now;
        snapshot = SummonedPetRosterSnapshot.Empty(0, capturedAt);
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

        if (!TryFindEntityById(process, entityTreeHeader, localEntityId, out var localEntity))
        {
            error = "local entity id " + localEntityId + " was not found in EntitySystem tree";
            return false;
        }

        if (!TryResolveActorFromEntity(process, localEntity, 0, out var localActor) ||
            localActor.ServerObjectId == 0)
        {
            error = "failed to resolve local actor/server object id";
            return false;
        }

        TryReadUInt32(
            process,
            localActor.Actor + ActorCurrentSummonedPetServerObjectIdOffset,
            out var linkedPetServerObjectId);

        var partyMemberReadError = string.Empty;
        if (!TryReadPartyMemberServerObjectIds(process, gameBase, out var partyMembers, out partyMemberReadError))
        {
            partyMembers = Array.Empty<PartyMemberInfo>();
        }

        if (!TryReadVisibleActorInfos(process, gameBase, entityTreeHeader, localEntityId, out var visibleActors, out error))
        {
            return false;
        }

        var owners = new Dictionary<uint, SummonedPetOwnerInfo>();
        AionClassId? localOwnerClassId = null;
        var localOwnerClassName = string.Empty;
        if (TryReadActorClassId(process, localActor.Actor, out var resolvedLocalOwnerClassId))
        {
            localOwnerClassId = resolvedLocalOwnerClassId;
            localOwnerClassName = AionClassCatalog.GetChineseName(resolvedLocalOwnerClassId);
        }

        owners[localActor.ServerObjectId] = new SummonedPetOwnerInfo(
            SummonedPetOwnerKind.LocalPlayer,
            localActor.ServerObjectId,
            localActor.Name,
            string.Empty,
            localOwnerClassId,
            localOwnerClassName);

        foreach (var member in partyMembers)
        {
            if (member.ServerObjectId == 0 ||
                member.ServerObjectId == localActor.ServerObjectId ||
                owners.ContainsKey(member.ServerObjectId))
            {
                continue;
            }

            owners[member.ServerObjectId] = new SummonedPetOwnerInfo(
                SummonedPetOwnerKind.PartyMember,
                member.ServerObjectId,
                string.Empty,
                member.ListName);
        }

        foreach (var visibleActor in visibleActors)
        {
            var actorServerObjectId = visibleActor.Actor.ServerObjectId;
            if (actorServerObjectId == 0 ||
                visibleActor.Actor.ObjectType != ActorPlayerObjectType ||
                !owners.TryGetValue(actorServerObjectId, out var ownerInfo))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(ownerInfo.OwnerName) &&
                !string.IsNullOrWhiteSpace(visibleActor.Actor.Name))
            {
                ownerInfo.OwnerName = visibleActor.Actor.Name;
            }

            if (!ownerInfo.OwnerClassId.HasValue &&
                TryReadActorClassId(process, visibleActor.Actor.Actor, out var ownerClassId))
            {
                ownerInfo.OwnerClassId = ownerClassId;
                ownerInfo.OwnerClassName = AionClassCatalog.GetChineseName(ownerClassId);
            }
        }

        OwnedSummonedPetSnapshot? localPlayerPet = null;
        var partyMemberPets = new List<OwnedSummonedPetSnapshot>();
        foreach (var visibleActor in visibleActors)
        {
            if (visibleActor.EntityId == localEntityId ||
                visibleActor.Actor.Actor == localActor.Actor ||
                visibleActor.Actor.ObjectType != SummonedPetSnapshot.ActorObjectType)
            {
                continue;
            }

            if (!TryReadUInt32(
                    process,
                    visibleActor.Actor.Actor + ActorSummonOwnerServerObjectIdOffset,
                    out var ownerServerObjectId) ||
                !owners.TryGetValue(ownerServerObjectId, out var ownerInfo))
            {
                continue;
            }

            var actorServerObjectId = visibleActor.Actor.ServerObjectId;
            var localLinkMatches =
                ownerInfo.OwnerKind == SummonedPetOwnerKind.LocalPlayer &&
                linkedPetServerObjectId != 0 &&
                actorServerObjectId == linkedPetServerObjectId;
            var hasStaticDetail = npcStaticDetails.TryGetValue(visibleActor.Actor.NpcTemplateId, out var npcStaticDetail);
            var isSummonPetStatic = hasStaticDetail && IsSummonPetNpcStaticDetail(npcStaticDetail);

            if (!isSummonPetStatic)
            {
                continue;
            }

            if (ownerInfo.OwnerKind == SummonedPetOwnerKind.LocalPlayer && !localLinkMatches)
            {
                continue;
            }

            var ownedPet = BuildOwnedSummonedPetSnapshot(
                process,
                visibleActor,
                ownerInfo,
                localActor.ServerObjectId,
                ownerInfo.OwnerKind == SummonedPetOwnerKind.LocalPlayer ? linkedPetServerObjectId : 0,
                localEntity,
                capturedAt,
                hasStaticDetail,
                npcStaticDetail,
                localLinkMatches,
                true,
                isSummonPetStatic);

            if (ownerInfo.OwnerKind == SummonedPetOwnerKind.LocalPlayer)
            {
                if (localPlayerPet is null ||
                    localLinkMatches ||
                    (!localPlayerPet.Pet.OwnerConfirmed && ownedPet.Pet.OwnerConfirmed))
                {
                    localPlayerPet = ownedPet;
                }
            }
            else
            {
                partyMemberPets.Add(ownedPet);
            }
        }

        localPlayerPet ??= new OwnedSummonedPetSnapshot(
            SummonedPetOwnerKind.LocalPlayer,
            localActor.ServerObjectId,
            localActor.Name,
            string.Empty,
            SummonedPetSnapshot.NotSummoned(localActor.ServerObjectId, capturedAt),
            0,
            Array.Empty<AbnormalStatusEntrySnapshot>(),
            OwnerClassId: localOwnerClassId,
            OwnerClassName: localOwnerClassName);

        snapshot = new SummonedPetRosterSnapshot(
            localActor.ServerObjectId,
            linkedPetServerObjectId,
            capturedAt,
            localPlayerPet,
            partyMemberPets,
            partyMembers
                .Select(member => member.ServerObjectId)
                .Where(serverObjectId => serverObjectId != 0 && serverObjectId != localActor.ServerObjectId)
                .Distinct()
                .ToArray(),
            partyMemberReadError);
        return true;
    }

    private static OwnedSummonedPetSnapshot BuildOwnedSummonedPetSnapshot(
        VmmProcess process,
        VisibleActorInfo visibleActor,
        SummonedPetOwnerInfo ownerInfo,
        uint localServerObjectId,
        uint localLinkedPetServerObjectId,
        ulong localEntity,
        DateTimeOffset capturedAt,
        bool hasStaticDetail,
        NpcStaticDetail npcStaticDetail,
        bool localLinkMatches,
        bool ownerConfirmed,
        bool isSummonPetStatic)
    {
        Vector3Snapshot? position = null;
        double? distance = null;
        if (TryReadEntityPosition(process, visibleActor.Entity, out var x, out var y, out var z) &&
            IsReasonablePosition(x, y, z))
        {
            position = new Vector3Snapshot(x, y, z);
            if (TryReadEntityPosition(process, localEntity, out var localX, out var localY, out var localZ))
            {
                var dx = x - localX;
                var dy = y - localY;
                var dz = z - localZ;
                distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }
        }

        TryReadUInt32(
            process,
            visibleActor.Actor.Actor + ActorAbnormalCategory2CountOffset,
            out var abnormalCategory2Count);

        var abnormalStatusReadError = string.Empty;
        if (!TryReadActorAbnormalStatusEntries(
                process,
                visibleActor.Actor.Actor,
                out var abnormalStatuses,
                out abnormalStatusReadError))
        {
            abnormalStatuses = Array.Empty<AbnormalStatusEntrySnapshot>();
        }

        var pet = new SummonedPetSnapshot(
            true,
            visibleActor.EntityId,
            visibleActor.Actor.ServerObjectId,
            visibleActor.EntityType,
            visibleActor.Actor.ObjectType,
            visibleActor.Actor.NpcTemplateId,
            visibleActor.Actor.Name,
            hasStaticDetail ? npcStaticDetail.Name : string.Empty,
            hasStaticDetail ? npcStaticDetail.NpcType : string.Empty,
            hasStaticDetail ? npcStaticDetail.Tribe : string.Empty,
            visibleActor.Actor.Level,
            visibleActor.Actor.CurrentHp,
            visibleActor.Actor.MaxHp,
            visibleActor.Actor.HpPercent,
            position,
            distance,
            localServerObjectId,
            capturedAt,
            localLinkedPetServerObjectId,
            ownerConfirmed,
            BuildSummonedPetEvidenceSource(localLinkMatches, ownerConfirmed, isSummonPetStatic));

        return new OwnedSummonedPetSnapshot(
            ownerInfo.OwnerKind,
            ownerInfo.ServerObjectId,
            ownerInfo.OwnerName,
            ownerInfo.PartyListName,
            pet,
            abnormalCategory2Count,
            abnormalStatuses,
            abnormalStatusReadError,
            ownerInfo.OwnerClassId,
            ownerInfo.OwnerClassName);
    }

    private static bool TryReadVisibleActorInfos(
        VmmProcess process,
        ulong gameBase,
        ulong entityTreeHeader,
        ushort localEntityId,
        out List<VisibleActorInfo> actors,
        out string error)
    {
        actors = new List<VisibleActorInfo>();
        error = string.Empty;

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

            if (TryReadUInt32(process, node + ServerNodeServerObjectIdOffset, out var serverObjectId) &&
                TryReadUInt16(process, node + ServerNodeEntityIdOffset, out var entityId) &&
                entityId != 0 &&
                TryFindEntityById(process, entityTreeHeader, entityId, out var entity) &&
                entity != 0 &&
                TryResolveActorFromEntity(process, entity, serverObjectId, out var actor) &&
                (actor.ObjectType == ActorPlayerObjectType ||
                 actor.ObjectType == SummonedPetSnapshot.ActorObjectType))
            {
                actors.Add(new VisibleActorInfo(
                    entityId,
                    TryReadUInt16(process, entity + EntityTypeOffset, out var entityType) ? entityType : (ushort)0,
                    entity,
                    actor));
            }

            if (!TryGetNextTreeNode(process, serverTreeHeader, node, out var next) || next == node)
            {
                break;
            }

            node = next;
        }

        return true;
    }

    private static bool TryReadPartyMemberServerObjectIds(
        VmmProcess process,
        ulong gameBase,
        out IReadOnlyList<PartyMemberInfo> members,
        out string error)
    {
        var result = new List<PartyMemberInfo>();
        var seen = new HashSet<uint>();
        var errors = new List<string>();

        if (!ReadPartyMemberServerObjectIdList(process, gameBase + PrimaryPartyListRva, "primary", result, seen, out var primaryError))
        {
            errors.Add(primaryError);
        }

        if (!ReadPartyMemberServerObjectIdList(process, gameBase + SecondaryPartyListRva, "secondary", result, seen, out var secondaryError))
        {
            errors.Add(secondaryError);
        }

        members = result;
        error = errors.Count == 0 ? string.Empty : string.Join("; ", errors);
        return result.Count > 0 || errors.Count < 2;
    }

    private static bool ReadPartyMemberServerObjectIdList(
        VmmProcess process,
        ulong listGlobalAddress,
        string listName,
        List<PartyMemberInfo> members,
        HashSet<uint> seenServerObjectIds,
        out string error)
    {
        error = string.Empty;

        if (!TryReadPointer(process, listGlobalAddress, out var head) || head == 0)
        {
            error = "failed to read " + listName + " party list head at 0x" + listGlobalAddress.ToString("X");
            return false;
        }

        if (!TryReadPointer(process, head + ListNodeNextOffset, out var node))
        {
            error = "failed to read " + listName + " party list first node";
            return false;
        }

        var visited = new HashSet<ulong>();
        for (var guard = 0; node != 0 && node != head && guard < 256; guard++)
        {
            if (!visited.Add(node))
            {
                break;
            }

            if (TryReadPointer(process, node + ListNodeValueOffset, out var member) &&
                member != 0 &&
                TryReadUInt32(process, member + PartyMemberServerObjectIdOffset, out var serverObjectId) &&
                serverObjectId != 0 &&
                seenServerObjectIds.Add(serverObjectId))
            {
                members.Add(new PartyMemberInfo(listName, member, serverObjectId));
            }

            if (!TryReadPointer(process, node + ListNodeNextOffset, out var next) || next == node)
            {
                break;
            }

            node = next;
        }

        return true;
    }

    private static bool IsSummonPetNpcStaticDetail(NpcStaticDetail detail)
    {
        var npcType = NormalizeNpcXmlToken(detail.NpcType);
        if (string.Equals(npcType, "summon_pet", StringComparison.Ordinal))
        {
            return true;
        }

        var tribe = NormalizeNpcXmlToken(detail.Tribe);
        return string.Equals(tribe, "pet", StringComparison.Ordinal) ||
               string.Equals(tribe, "pet_dark", StringComparison.Ordinal);
    }

    private static string NormalizeNpcXmlToken(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace('-', '_').ToLowerInvariant();
    }

    private static string BuildSummonedPetEvidenceSource(
        bool localLinkMatches,
        bool ownerConfirmed,
        bool staticSummonPet)
    {
        var evidence = new List<string>();
        if (localLinkMatches)
        {
            evidence.Add("local-link");
        }

        if (ownerConfirmed)
        {
            evidence.Add("owner");
        }

        if (staticSummonPet)
        {
            evidence.Add("static-summon-pet");
        }

        return string.Join("+", evidence);
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
                        npcStaticDetails.TryGetValue(actor.NpcTemplateId, out var npcStaticDetail))
                    {
                        var dx = x - localX;
                        var dy = y - localY;
                        var dz = z - localZ;
                        var distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                        var isMonster = npcStaticDetail.IsMonsterKnown && npcStaticDetail.IsMonster;
                        var name = string.IsNullOrWhiteSpace(actor.Name) ? npcStaticDetail.Name : actor.Name;
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            continue;
                        }

                        result.Add(new WorldObjectSnapshot(
                            entityId,
                            serverObjectId,
                            name,
                            isMonster ? "monster" : "npc",
                            new Vector3Snapshot(x, y, z),
                            distance,
                            actor.CurrentHp,
                            actor.MaxHp,
                            actor.TargetServerObjectId,
                            localServerObjectId != 0 && actor.TargetServerObjectId == localServerObjectId,
                            npcStaticDetail.AggressiveKnown,
                            npcStaticDetail.AggressiveToPlayer,
                            npcStaticDetail.AggressiveSource,
                            actor.LootableRaw,
                            actor.InteractionState));
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
                        var deadByHp = actor.MaxHp > 0 && (actor.CurrentHp == 0 || actor.HpPercent == 0);
                        if (deadByHp || actor.LootableRaw != 0)
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
                                actor.LootableRaw,
                                actor.InteractionState,
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

    private static bool TryFindEntityById(
        VmmProcess process,
        ulong header,
        ushort entityId,
        out ulong entity,
        bool bypassMemoryCache = false)
    {
        entity = 0;
        if (header == 0 || entityId == 0)
        {
            return false;
        }

        if (!TryReadPointer(process, header + NodeParentOffset, out var node, bypassMemoryCache))
        {
            return false;
        }

        for (var guard = 0; node != 0 && node != header && guard < 65536; guard++)
        {
            if (IsNilNode(process, node, header, bypassMemoryCache))
            {
                return false;
            }

            if (!TryReadUInt16(process, node + NodeIdOffset, out var nodeId, bypassMemoryCache))
            {
                return false;
            }

            if (entityId < nodeId)
            {
                if (!TryReadPointer(process, node + NodeLeftOffset, out node, bypassMemoryCache))
                {
                    return false;
                }
            }
            else if (entityId > nodeId)
            {
                if (!TryReadPointer(process, node + NodeRightOffset, out node, bypassMemoryCache))
                {
                    return false;
                }
            }
            else
            {
                return TryReadPointer(process, node + NodeEntityOffset, out entity, bypassMemoryCache);
            }
        }

        return false;
    }

    private static bool TryReadEntityPosition(
        VmmProcess process,
        ulong entity,
        out float x,
        out float y,
        out float z,
        bool bypassMemoryCache = false)
    {
        x = 0;
        y = 0;
        z = 0;

        // Navigation paths are world-space. Do not fall back to the alternate
        // local/pending vector because it can be a transform basis or a parent-
        // relative value and make a nearby waypoint appear kilometres away.
        return TryReadPositionVector(
                   process,
                   entity + EntityWorldPositionOffset,
                   out x,
                   out y,
                   out z,
                   bypassMemoryCache) &&
               IsUsableEntityPosition(x, y, z);
    }

    private static bool TryReadPositionVector(
        VmmProcess process,
        ulong address,
        out float x,
        out float y,
        out float z,
        bool bypassMemoryCache = false)
    {
        x = 0;
        y = 0;
        z = 0;

        return TryReadSingle(process, address, out x, bypassMemoryCache) &&
               TryReadSingle(process, address + 4, out y, bypassMemoryCache) &&
               TryReadSingle(process, address + 8, out z, bypassMemoryCache);
    }

    private static bool IsUsableEntityPosition(float x, float y, float z)
    {
        if (!IsReasonablePosition(x, y, z))
        {
            return false;
        }

        if (Math.Abs(x) < 0.001F &&
            Math.Abs(y) < 0.001F &&
            Math.Abs(z) < 0.001F)
        {
            return false;
        }

        // (0,1,0) and its siblings are transform basis vectors, not map
        // coordinates. Treat them as a hard read failure so navigation stops.
        var squaredLength = (x * x) + (y * y) + (z * z);
        var unitAxisLike = Math.Abs(squaredLength - 1.0F) <= 0.001F &&
            Math.Abs(x - MathF.Round(x)) <= 0.001F &&
            Math.Abs(y - MathF.Round(y)) <= 0.001F &&
            Math.Abs(z - MathF.Round(z)) <= 0.001F;
        return !unitAxisLike;
    }

    private static bool TryResolveActorFromEntity(
        VmmProcess process,
        ulong entity,
        uint expectedServerObjectId,
        out ActorInfo actor,
        bool bypassMemoryCache = false)
    {
        actor = new ActorInfo();

        if (TryResolveProxyManagerFromEntityVfunc(process, entity, out var proxyManager, out var proxyOffset, bypassMemoryCache) &&
            TryFindActorCandidateInPointerRegion(
                process,
                proxyManager,
                0x400,
                entity,
                expectedServerObjectId,
                "proxyManager(vfunc_0xB8, entity+0x" + proxyOffset.ToString("X") + ")",
                out actor,
                bypassMemoryCache))
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
            out actor,
            bypassMemoryCache))
        {
            return true;
        }

        for (ulong offset = 0; offset < 0x800; offset += 8)
        {
            if (!TryReadPointer(process, entity + offset, out var pointer, bypassMemoryCache))
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
                out actor,
                bypassMemoryCache))
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
        out ulong proxyOffset,
        bool bypassMemoryCache = false)
    {
        proxyManager = 0;
        proxyOffset = 0;

        if (!TryReadPointer(process, entity, out var vtable, bypassMemoryCache) ||
            !TryReadPointer(process, vtable + EntityProxyManagerVfuncOffset, out var function, bypassMemoryCache) ||
            !TryReadBytes(process, function, 16, out var code, bypassMemoryCache))
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

        return TryReadPointer(process, entity + proxyOffset, out proxyManager, bypassMemoryCache);
    }

    private static bool TryFindActorCandidateInPointerRegion(
        VmmProcess process,
        ulong region,
        ulong regionSize,
        ulong expectedEntity,
        uint expectedServerObjectId,
        string source,
        out ActorInfo actor,
        bool bypassMemoryCache = false)
    {
        actor = new ActorInfo();
        var bestScore = -1;

        if (!IsLikelyUserPointer(region))
        {
            return false;
        }

        for (ulong offset = 0; offset < regionSize; offset += 8)
        {
            if (TryReadPointer(process, region + offset, out var candidate, bypassMemoryCache) &&
                TryReadActorInfo(
                    process,
                    candidate,
                    expectedEntity,
                    expectedServerObjectId,
                    source + "+0x" + offset.ToString("X"),
                    out var candidateInfo,
                    out var score,
                    bypassMemoryCache) &&
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
        out int score,
        bool bypassMemoryCache = false)
    {
        actor = new ActorInfo();
        score = 0;

        if (!IsLikelyUserPointer(actorAddress))
        {
            return false;
        }

        if (!TryReadPointer(process, actorAddress + ActorEntityOffset, out var actorEntity, bypassMemoryCache) ||
            !TryReadUInt32(process, actorAddress + ActorObjectTypeOffset, out var objectType, bypassMemoryCache) ||
            !TryReadUInt32(process, actorAddress + ActorServerObjectIdOffset, out var serverObjectId, bypassMemoryCache))
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

        TryReadUInt32(process, actorAddress + ActorNpcTemplateIdOffset, out actor.NpcTemplateId, bypassMemoryCache);
        TryReadUInt16(process, actorAddress + ActorLevelOffset, out actor.Level, bypassMemoryCache);
        TryReadByte(process, actorAddress + ActorHpPercentOffset, out actor.HpPercent);
        TryReadUInt32(process, actorAddress + ActorTargetServerObjectIdOffset, out actor.TargetServerObjectId, bypassMemoryCache);
        TryReadUInt32(process, actorAddress + ActorInteractionStateOffset, out actor.InteractionState, bypassMemoryCache);
        var hasStanceFlags = TryReadUInt32(process, actorAddress + ActorStanceFlagsOffset, out actor.StanceFlags, bypassMemoryCache);
        var hasMotionMode = TryReadUInt32(process, actorAddress + ActorMotionModeOffset, out actor.MotionMode, bypassMemoryCache);
        actor.HasRestState = hasStanceFlags && hasMotionMode;
        TryReadUInt32(process, actorAddress + ActorMaxHpOffset, out actor.MaxHp, bypassMemoryCache);
        TryReadUInt32(process, actorAddress + ActorCurrentHpOffset, out actor.CurrentHp, bypassMemoryCache);
        TryReadUInt32(process, actorAddress + ActorLootableFlagOffset, out actor.LootableRaw, bypassMemoryCache);

        if (TryReadUtf16String(process, actorAddress + ActorNameOffset, 64, out var name, bypassMemoryCache))
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
        out ulong serverTreeHeader,
        bool bypassMemoryCache = false)
    {
        serverObjectId = 0;
        serverTreeHeader = 0;

        if (entityId == 0 ||
            !TryReadPointer(process, gameBase + ServerObjectTreeRva, out serverTreeHeader, bypassMemoryCache) ||
            serverTreeHeader == 0)
        {
            return false;
        }

        if (!TryReadPointer(process, serverTreeHeader + NodeLeftOffset, out var node, bypassMemoryCache))
        {
            return false;
        }

        for (var guard = 0; node != 0 && node != serverTreeHeader && guard < 100000; guard++)
        {
            if (IsNilNode(process, node, serverTreeHeader, bypassMemoryCache))
            {
                return false;
            }

            if (!TryReadUInt16(process, node + ServerNodeEntityIdOffset, out var nodeEntityId, bypassMemoryCache))
            {
                return false;
            }

            if (nodeEntityId == entityId)
            {
                return TryReadUInt32(process, node + ServerNodeServerObjectIdOffset, out serverObjectId, bypassMemoryCache);
            }

            if (!TryGetNextTreeNode(process, serverTreeHeader, node, out var next, bypassMemoryCache) || next == node)
            {
                return false;
            }

            node = next;
        }

        return false;
    }

    private static bool TryGetNextTreeNode(
        VmmProcess process,
        ulong header,
        ulong node,
        out ulong next,
        bool bypassMemoryCache = false)
    {
        next = 0;
        if (!TryReadPointer(process, node + NodeRightOffset, out var right, bypassMemoryCache))
        {
            return false;
        }

        if (!IsNilNode(process, right, header, bypassMemoryCache))
        {
            var current = right;
            for (var guard = 0; guard < 1024; guard++)
            {
                if (!TryReadPointer(process, current + NodeLeftOffset, out var left, bypassMemoryCache))
                {
                    return false;
                }

                if (IsNilNode(process, left, header, bypassMemoryCache))
                {
                    next = current;
                    return true;
                }

                current = left;
            }

            return false;
        }

        if (!TryReadPointer(process, node + NodeParentOffset, out var parent, bypassMemoryCache))
        {
            return false;
        }

        for (var guard = 0; !IsNilNode(process, parent, header, bypassMemoryCache) && guard < 1024; guard++)
        {
            if (!TryReadPointer(process, parent + NodeRightOffset, out var parentRight, bypassMemoryCache))
            {
                return false;
            }

            if (node != parentRight)
            {
                break;
            }

            node = parent;
            if (!TryReadPointer(process, parent + NodeParentOffset, out parent, bypassMemoryCache))
            {
                return false;
            }
        }

        next = parent;
        return true;
    }

    private static bool IsNilNode(
        VmmProcess process,
        ulong node,
        ulong header,
        bool bypassMemoryCache = false)
    {
        if (node == 0 || node == header)
        {
            return true;
        }

        return !TryReadByte(process, node + NodeIsNilOffset, out var isNil, bypassMemoryCache) || isNil != 0;
    }

    private static List<InventoryWindowCandidate> FindInventoryWindowDialogTableCandidates(
        VmmProcess process,
        ulong gameBase)
    {
        var candidates = new List<InventoryWindowCandidate>(2);
        TryAddInventoryWindowDialogTableCandidate(
            process,
            gameBase + DlgInventoryDialog27PointerRva,
            "DlgInventory.Dialog27Pointer",
            candidates);
        TryAddInventoryWindowDialogTableCandidate(
            process,
            gameBase + DlgInventoryDialog28PointerRva,
            "DlgInventory.Dialog28Pointer",
            candidates);
        return candidates;
    }

    private static void TryAddInventoryWindowDialogTableCandidate(
        VmmProcess process,
        ulong pointerAddress,
        string source,
        List<InventoryWindowCandidate> candidates)
    {
        if (!TryReadPointer(process, pointerAddress, out var objectAddress) ||
            !IsLikelyUserPointer(objectAddress) ||
            candidates.Any(candidate => candidate.ObjectAddress == objectAddress))
        {
            return;
        }

        TryReadPointer(process, objectAddress, out var vtableAddress);
        candidates.Add(new InventoryWindowCandidate(objectAddress, vtableAddress, source, 0));
    }

    private static bool TryReadPreferredInventoryWindowSnapshot(
        VmmProcess process,
        IReadOnlyList<InventoryWindowCandidate> candidates,
        InventoryWindowRectSource rectSource,
        out InventoryWindowSnapshot snapshot,
        out string error,
        out bool hasVisibleDialog)
    {
        snapshot = default!;
        error = string.Empty;
        hasVisibleDialog = false;

        var states = new List<InventoryWindowCandidateState>(candidates.Count);
        var errors = new List<string>();
        foreach (var candidate in candidates)
        {
            if (!TryReadInventoryWindowVisibility(process, candidate, out var isVisible, out var widgetFlags, out var visibilityError))
            {
                errors.Add(candidate.Source + ": " + visibilityError);
                continue;
            }

            hasVisibleDialog |= isVisible;
            states.Add(new InventoryWindowCandidateState(candidate, isVisible, widgetFlags));
        }

        if (states.Count == 0)
        {
            error = errors.Count == 0
                ? "DlgInventory dialog table has no readable widget flags."
                : string.Join("; ", errors);
            return false;
        }

        var orderedStates = hasVisibleDialog
            ? states.Where(state => state.IsVisible)
            : states;
        foreach (var state in orderedStates)
        {
            if (TryReadInventoryWindowSnapshot(
                    process,
                    state.Candidate,
                    rectSource,
                    state.IsVisible,
                    state.WidgetFlags,
                    out snapshot,
                    out var readError))
            {
                return true;
            }

            errors.Add(state.Candidate.Source + ": " + readError);
        }

        error = errors.Count == 0
            ? "DlgInventory dialog table candidates could not be read."
            : string.Join("; ", errors);
        return false;
    }

    private bool TryFindInventoryWindowCandidate(
        VmmProcess process,
        ulong gameBase,
        string moduleName,
        out InventoryWindowCandidate candidate,
        out string error)
    {
        candidate = default;
        error = string.Empty;

        if (!TryGetModuleImageSize(process, moduleName, out var moduleSize) || moduleSize == 0)
        {
            moduleSize = 0x02000000;
        }

        var targetMethods = new Dictionary<ulong, string>
        {
            [gameBase + DlgInventoryDialog27MethodRva] = "DlgInventory.Dialog27Method",
            [gameBase + DlgInventoryDialog28MethodRva] = "DlgInventory.Dialog28Method"
        };

        var methodSlots = FindPointerOccurrencesInRange(
            process,
            gameBase,
            moduleSize,
            targetMethods,
            0x1000);

        if (methodSlots.Count == 0)
        {
            error = "DlgInventory method references were not found in " + moduleName + ".";
            return false;
        }

        var vtableCandidates = new Dictionary<ulong, string>();
        foreach (var slot in methodSlots)
        {
            for (var i = 0; i <= DlgInventoryVtableBackSlots; i++)
            {
                var vtable = slot.Address - ((ulong)i * 8UL);
                if (vtable < gameBase || vtable >= gameBase + moduleSize)
                {
                    continue;
                }

                vtableCandidates.TryAdd(vtable, slot.Label + "-back" + i.ToString(CultureInfo.InvariantCulture));
            }
        }

        var candidates = FindHeapObjectsWithVtables(
            process,
            vtableCandidates,
            InventoryUiMinAllocationSize,
            InventoryUiMaxAllocationSize,
            InventoryUiObjectScanLimit);

        if (candidates.Count == 0)
        {
            candidates = FindVadObjectsWithVtables(
                process,
                vtableCandidates,
                InventoryUiVadScanBytes,
                InventoryUiObjectScanLimit);
        }

        if (candidates.Count == 0)
        {
            error = "DlgInventory object was not found in heap or private VAD memory.";
            return false;
        }

        candidate = SelectInventoryWindowCandidate(candidates);
        return true;
    }

    private static InventoryWindowCandidate SelectInventoryWindowCandidate(
        IReadOnlyList<InventoryWindowCandidate> candidates)
    {
        return candidates
            .OrderBy(GetInventoryWindowCandidateScore)
            .ThenBy(candidate => candidate.ObjectAddress)
            .First();
    }

    private static int GetInventoryWindowCandidateScore(InventoryWindowCandidate candidate)
    {
        var source = candidate.Source ?? string.Empty;
        if (source.Contains("DlgInventory.Dialog27Method-back3", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (source.Contains("DlgInventory.Dialog27Method", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (source.Contains("DlgInventory.Dialog28Method", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 3;
    }

    private static bool TryReadInventoryWindowSnapshot(
        VmmProcess process,
        InventoryWindowCandidate candidate,
        InventoryWindowRectSource rectSource,
        out InventoryWindowSnapshot snapshot,
        out string error)
    {
        if (!TryReadInventoryWindowVisibility(process, candidate, out var isVisible, out var widgetFlags, out error))
        {
            snapshot = default!;
            return false;
        }

        return TryReadInventoryWindowSnapshot(
            process,
            candidate,
            rectSource,
            isVisible,
            widgetFlags,
            out snapshot,
            out error);
    }

    private static bool TryReadInventoryWindowVisibility(
        VmmProcess process,
        InventoryWindowCandidate candidate,
        out bool isVisible,
        out ulong widgetFlags,
        out string error)
    {
        isVisible = false;
        widgetFlags = 0;
        error = string.Empty;

        if (!TryReadUInt64(process, candidate.ObjectAddress + DlgInventoryWidgetFlagsOffset, out widgetFlags))
        {
            error = "Failed to read DlgInventory widget flags at +0x" +
                DlgInventoryWidgetFlagsOffset.ToString("X", CultureInfo.InvariantCulture) +
                ".";
            return false;
        }

        isVisible = (widgetFlags & DlgInventoryVisibleMask) != 0;
        return true;
    }

    private static bool TryReadGatherSnapshot(
        VmmProcess process,
        ulong gameBase,
        GatherSourceCatalog catalog,
        IReadOnlyDictionary<uint, NpcStaticDetail> npcStaticDetails,
        out GatherSnapshot snapshot,
        out GatherReadCounters counters,
        out string error)
    {
        var capturedAt = DateTimeOffset.Now;
        snapshot = GatherSnapshot.Empty(capturedAt);
        counters = new GatherReadCounters();
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

        var localPosition = new Vector3Snapshot(localX, localY, localZ);
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

        var objects = new List<GatherObjectSnapshot>();
        var players = new List<GatherCompetitionPlayerSnapshot>();
        var monsters = new List<WorldObjectSnapshot>();
        for (var guard = 0; node != 0 && node != serverTreeHeader && guard < 100000; guard++)
        {
            if (IsNilNode(process, node, serverTreeHeader))
            {
                break;
            }

            counters.ScannedServerObjects++;
            if (TryReadUInt32(process, node + ServerNodeServerObjectIdOffset, out var nodeServerObjectId) &&
                TryReadUInt16(process, node + ServerNodeEntityIdOffset, out var entityId) &&
                entityId != 0 &&
                entityId != localEntityId &&
                TryFindEntityById(process, entityTreeHeader, entityId, out var entity) &&
                entity != 0)
            {
                counters.ResolvedEntities++;
                if (TryResolveActorFromEntity(process, entity, nodeServerObjectId, out var actor))
                {
                    counters.ResolvedActors++;
                    var serverObjectId = actor.ServerObjectId != 0
                        ? actor.ServerObjectId
                        : nodeServerObjectId;
                    if (actor.ObjectType == ActorGatherObjectType)
                    {
                        var position = TryReadEntityPosition(process, entity, out var x, out var y, out var z) &&
                                       IsReasonablePosition(x, y, z)
                            ? new Vector3Snapshot(x, y, z)
                            : (Vector3Snapshot?)null;
                        var spawnPosition =
                            TryReadPositionVector(
                                process,
                                actor.Actor + ActorGatherSpawnPositionOffset,
                                out var spawnX,
                                out var spawnY,
                                out var spawnZ) &&
                            IsReasonablePosition(spawnX, spawnY, spawnZ)
                                ? new Vector3Snapshot(spawnX, spawnY, spawnZ)
                                : (Vector3Snapshot?)null;
                        var distance = CalculateDistance(localPosition, position ?? spawnPosition);

                        TryReadSingle(
                            process,
                            actor.Actor + ActorGatherInteractionRadiusOffset,
                            out var interactionRadius);
                        if (!float.IsFinite(interactionRadius) || interactionRadius < 0 || interactionRadius > 100)
                        {
                            interactionRadius = 0;
                        }

                        catalog.TryGet(actor.NpcTemplateId, out var source);
                        var name = string.IsNullOrWhiteSpace(actor.Name)
                            ? source?.InternalName ?? string.Empty
                            : actor.Name;
                        objects.Add(
                            new GatherObjectSnapshot(
                                entityId,
                                serverObjectId,
                                actor.NpcTemplateId,
                                name,
                                actor.Level,
                                actor.HpPercent,
                                interactionRadius,
                                actor.InteractionState,
                                position,
                                spawnPosition,
                                distance,
                                entityId == targetEntityId,
                                source,
                                capturedAt));
                    }
                    else if (actor.ObjectType == ActorPlayerObjectType)
                    {
                        var position = TryReadEntityPosition(process, entity, out var x, out var y, out var z) &&
                                       IsReasonablePosition(x, y, z)
                            ? new Vector3Snapshot(x, y, z)
                            : (Vector3Snapshot?)null;
                        TryReadUInt32(
                            process,
                            actor.Actor + ActorGatherActionStateOffset,
                            out var gatherActionStateRaw);
                        TryReadUInt32(
                            process,
                            actor.Actor + ActorGatherActionIdOffset,
                            out var gatherActionIdRaw);
                        TryReadUInt32(
                            process,
                            actor.Actor + ActorGatherSourceIdCandidateOffset,
                            out var gatherSourceIdCandidateRaw);
                        players.Add(
                            new GatherCompetitionPlayerSnapshot(
                                entityId,
                                serverObjectId,
                                actor.Name,
                                position,
                                CalculateDistance(localPosition, position),
                                gatherActionStateRaw,
                                gatherActionIdRaw,
                                gatherSourceIdCandidateRaw,
                                capturedAt));
                    }
                    else if (TryReadUInt16(process, entity + EntityTypeOffset, out var entityType) &&
                             entityType == EntityTypeNpc &&
                             npcStaticDetails.TryGetValue(actor.NpcTemplateId, out var npcStaticDetail) &&
                             npcStaticDetail.IsMonsterKnown &&
                             npcStaticDetail.IsMonster &&
                             TryReadEntityPosition(process, entity, out var x, out var y, out var z) &&
                             IsReasonablePosition(x, y, z))
                    {
                        var name = string.IsNullOrWhiteSpace(actor.Name)
                            ? npcStaticDetail.Name
                            : actor.Name;
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            var position = new Vector3Snapshot(x, y, z);
                            monsters.Add(
                                new WorldObjectSnapshot(
                                    entityId,
                                    serverObjectId,
                                    name,
                                    "monster",
                                    position,
                                    CalculateDistance(localPosition, position),
                                    actor.CurrentHp,
                                    actor.MaxHp,
                                    actor.TargetServerObjectId,
                                    localServerObjectId != 0 &&
                                    actor.TargetServerObjectId == localServerObjectId,
                                    npcStaticDetail.AggressiveKnown,
                                    npcStaticDetail.AggressiveToPlayer,
                                    npcStaticDetail.AggressiveSource,
                                    actor.LootableRaw,
                                    actor.InteractionState));
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

        objects.Sort(static (left, right) =>
        {
            var distance = (left.DistanceToLocalPlayer ?? double.MaxValue)
                .CompareTo(right.DistanceToLocalPlayer ?? double.MaxValue);
            return distance != 0 ? distance : left.ServerObjectId.CompareTo(right.ServerObjectId);
        });
        players.Sort(static (left, right) =>
        {
            var distance = (left.DistanceToLocalPlayer ?? double.MaxValue)
                .CompareTo(right.DistanceToLocalPlayer ?? double.MaxValue);
            return distance != 0 ? distance : left.ServerObjectId.CompareTo(right.ServerObjectId);
        });
        monsters.Sort(static (left, right) =>
        {
            var distance = (left.DistanceToLocalPlayer ?? double.MaxValue)
                .CompareTo(right.DistanceToLocalPlayer ?? double.MaxValue);
            return distance != 0 ? distance : left.ServerObjectId.CompareTo(right.ServerObjectId);
        });

        var localGathering = TryReadLocalGatheringSnapshot(process, gameBase, out var progress)
            ? progress
            : LocalGatheringSnapshot.Unavailable;
        snapshot = new GatherSnapshot(
            localEntityId,
            localServerObjectId,
            localPosition,
            objects,
            players,
            monsters,
            true,
            true,
            localGathering,
            capturedAt);
        return true;
    }

    private static bool TryReadLocalGatheringSnapshot(
        VmmProcess process,
        ulong gameBase,
        out LocalGatheringSnapshot snapshot)
    {
        snapshot = LocalGatheringSnapshot.Unavailable;
        if (!TryReadUInt32(process, gameBase + CurrentGatherSourceIdRva, out var gatherSourceId) ||
            !TryReadUInt64(process, gameBase + CurrentGatherTargetEntityRva, out var targetEntity) ||
            !TryReadUInt64(process, gameBase + DlgGatheringPointerRva, out var dialog))
        {
            return false;
        }

        TryReadUInt32(process, gameBase + CurrentGatherSkillIdRva, out var skillId);
        if ((targetEntity != 0 && !IsLikelyUserPointer(targetEntity)) ||
            (dialog != 0 && !IsLikelyUserPointer(dialog)))
        {
            return false;
        }

        var visible = false;
        GatherGaugeSnapshot? successGauge = null;
        GatherGaugeSnapshot? failureGauge = null;
        if (dialog != 0)
        {
            if (!TryReadUInt64(process, dialog + DlgGatheringFlagsOffset, out var flags))
            {
                return false;
            }

            visible = (flags & DlgGatheringVisibleMask) != 0;
            if (visible)
            {
                successGauge = TryReadGatherGauge(process, dialog + DlgGatheringSuccessGaugeOffset);
                failureGauge = TryReadGatherGauge(process, dialog + DlgGatheringFailureGaugeOffset);
            }
        }

        snapshot = new LocalGatheringSnapshot(
            true,
            visible,
            gatherSourceId,
            targetEntity != 0,
            skillId,
            successGauge,
            failureGauge);
        return true;
    }

    private static GatherGaugeSnapshot? TryReadGatherGauge(VmmProcess process, ulong gaugePointerAddress)
    {
        if (!TryReadPointer(process, gaugePointerAddress, out var gauge) ||
            !TryReadDouble(process, gauge + GatherGaugeMaximumOffset, out var maximum) ||
            !TryReadDouble(process, gauge + GatherGaugeDisplayedOffset, out var displayed) ||
            !TryReadDouble(process, gauge + GatherGaugeTargetOffset, out var target) ||
            !double.IsFinite(maximum) ||
            !double.IsFinite(displayed) ||
            !double.IsFinite(target))
        {
            return null;
        }

        return new GatherGaugeSnapshot(maximum, displayed, target);
    }

    private static double? CalculateDistance(Vector3Snapshot origin, Vector3Snapshot? target)
    {
        if (target is not { } position)
        {
            return null;
        }

        var dx = position.X - origin.X;
        var dy = position.Y - origin.Y;
        var dz = position.Z - origin.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private static bool TryReadInventoryWindowSnapshot(
        VmmProcess process,
        InventoryWindowCandidate candidate,
        InventoryWindowRectSource rectSource,
        bool isVisible,
        ulong widgetFlags,
        out InventoryWindowSnapshot snapshot,
        out string error)
    {
        snapshot = default!;
        error = string.Empty;

        var rootWidgetAddress = 0UL;
        var rectAddress = 0UL;
        if (!TryReadInventoryWindowRect(
                process,
                candidate,
                rectSource,
                out var x,
                out var y,
                out var width,
                out var height,
                out rootWidgetAddress,
                out rectAddress,
                out error))
        {
            return false;
        }

        snapshot = new InventoryWindowSnapshot(
            isVisible,
            x,
            y,
            width,
            height,
            candidate.ObjectAddress,
            candidate.VtableAddress,
            DateTimeOffset.Now,
            rectSource,
            rootWidgetAddress,
            rectAddress,
            widgetFlags,
            candidate.Source);
        return true;
    }

    private static bool TryReadInventoryWindowRect(
        VmmProcess process,
        InventoryWindowCandidate candidate,
        InventoryWindowRectSource rectSource,
        out double x,
        out double y,
        out double width,
        out double height,
        out ulong rootWidgetAddress,
        out ulong rectAddress,
        out string error)
    {
        x = 0;
        y = 0;
        width = 0;
        height = 0;
        rootWidgetAddress = 0;
        rectAddress = 0;
        error = string.Empty;

        switch (rectSource)
        {
            case InventoryWindowRectSource.LegacyDialogRect:
                rectAddress = candidate.ObjectAddress + DlgInventoryWindowRectOffset;
                break;

            case InventoryWindowRectSource.RootWidgetRectExperimental:
                if (!TryReadPointer(process, candidate.ObjectAddress + DlgInventoryRootWidgetOffset, out rootWidgetAddress) ||
                    !IsLikelyUserPointer(rootWidgetAddress))
                {
                    error = "Failed to read DlgInventory+0x4D8 root widget pointer.";
                    return false;
                }

                if (!TryResolveRootWidgetRectAddress(process, rootWidgetAddress, out rectAddress, out error))
                {
                    return false;
                }

                break;

            default:
                error = "Unsupported inventory window rect source: " + rectSource + ".";
                return false;
        }

        if (!TryReadDouble(process, rectAddress, out x) ||
            !TryReadDouble(process, rectAddress + 0x08, out y) ||
            !TryReadDouble(process, rectAddress + 0x10, out width) ||
            !TryReadDouble(process, rectAddress + 0x18, out height))
        {
            error = "Failed to read inventory window rect at 0x" + rectAddress.ToString("X") + ".";
            return false;
        }

        if (!IsPlausibleInventoryWindowRect(x, y, width, height))
        {
            error = "Inventory window rect at 0x" + rectAddress.ToString("X") + " is outside expected bounds.";
            return false;
        }

        return true;
    }

    private static bool TryResolveRootWidgetRectAddress(
        VmmProcess process,
        ulong rootWidgetAddress,
        out ulong rectAddress,
        out string error)
    {
        rectAddress = 0;
        error = string.Empty;

        var configuredOffset = ReadRvaFromEnv(RootWidgetRectOffsetEnvironmentVariable, ulong.MaxValue);
        if (configuredOffset != ulong.MaxValue)
        {
            rectAddress = rootWidgetAddress + configuredOffset;
            return true;
        }

        var candidates = new List<ulong>();
        for (var offset = 0UL; offset + 0x18 < RootWidgetRectScanBytes; offset += RootWidgetRectScanStep)
        {
            var address = rootWidgetAddress + offset;
            if (!TryReadDouble(process, address, out var x) ||
                !TryReadDouble(process, address + 0x08, out var y) ||
                !TryReadDouble(process, address + 0x10, out var width) ||
                !TryReadDouble(process, address + 0x18, out var height) ||
                !IsPlausibleInventoryWindowRect(x, y, width, height))
            {
                continue;
            }

            candidates.Add(address);
        }

        if (candidates.Count == 1)
        {
            rectAddress = candidates[0];
            return true;
        }

        if (candidates.Count == 0)
        {
            error = "No plausible UiRect was found within root widget +0x" +
                RootWidgetRectScanBytes.ToString("X") +
                ". Configure " +
                RootWidgetRectOffsetEnvironmentVariable +
                " after validating the Rect offset.";
            return false;
        }

        var offsets = string.Join(
            ", ",
            candidates
                .Take(8)
                .Select(address => "+0x" + (address - rootWidgetAddress).ToString("X")));
        error = "Root widget has " +
            candidates.Count.ToString(CultureInfo.InvariantCulture) +
            " plausible UiRect candidates (" +
            offsets +
            "). Configure " +
            RootWidgetRectOffsetEnvironmentVariable +
            " to select one.";
        return false;
    }

    private static bool IsPlausibleInventoryWindowRect(double x, double y, double width, double height)
    {
        return double.IsFinite(x) &&
            double.IsFinite(y) &&
            double.IsFinite(width) &&
            double.IsFinite(height) &&
            x >= -1000.0 &&
            y >= -1000.0 &&
            x <= 4000.0 &&
            y <= 4000.0 &&
            width >= 100.0 &&
            height >= 100.0 &&
            width <= 2000.0 &&
            height <= 2000.0;
    }

    private void LogInventoryWindowRead(
        GameApiReadContext context,
        VmmProcess process,
        InventoryWindowSnapshot snapshot,
        bool cacheHit)
    {
        _logger.Info("vmm.inventory_window.read", new Dictionary<string, object?>
        {
            ["account"] = context.AccountName,
            ["pid"] = SafeGetProcessPid(process),
            ["isOpen"] = snapshot.IsOpen,
            ["x"] = snapshot.X,
            ["y"] = snapshot.Y,
            ["width"] = snapshot.Width,
            ["height"] = snapshot.Height,
            ["dialog"] = snapshot.DialogAddress.ToString("X"),
            ["vtable"] = snapshot.VtableAddress.ToString("X"),
            ["rectSource"] = snapshot.RectSource.ToString(),
            ["rootWidget"] = snapshot.RootWidgetAddress == 0 ? string.Empty : snapshot.RootWidgetAddress.ToString("X"),
            ["rectAddress"] = snapshot.RectAddress == 0 ? string.Empty : snapshot.RectAddress.ToString("X"),
            ["widgetFlags"] = snapshot.WidgetFlags.ToString("X"),
            ["dialogSource"] = snapshot.DialogSource,
            ["cacheHit"] = cacheHit
        });
    }

    private string BuildInventoryWindowCacheKey(
        GameApiReadContext context,
        VmmProcess process,
        ulong gameBase)
    {
        var pid = SafeGetProcessPid(process);
        return ResolveVmmDeviceName(context.VmmDeviceName) +
            "|" +
            pid.ToString(CultureInfo.InvariantCulture) +
            "|" +
            gameBase.ToString("X", CultureInfo.InvariantCulture);
    }

    private static bool TryGetModuleImageSize(VmmProcess process, string moduleName, out ulong size)
    {
        size = 0;
        try
        {
            var module = process.MapModuleFromName(moduleName);
            if (module.fValid && module.cbImageSize != 0)
            {
                size = module.cbImageSize;
                return true;
            }
        }
        catch
        {
            size = 0;
        }

        return false;
    }

    private static List<PointerOccurrence> FindPointerOccurrencesInRange(
        VmmProcess process,
        ulong start,
        ulong size,
        IReadOnlyDictionary<ulong, string> targets,
        int chunkSize)
    {
        var results = new List<PointerOccurrence>();
        if (size == 0 || targets.Count == 0)
        {
            return results;
        }

        var end = start + size;
        for (var address = start; address < end; address += (ulong)chunkSize)
        {
            var readSize = (int)Math.Min((ulong)chunkSize, end - address);
            if (!TryReadBytes(process, address, readSize, out var bytes) || bytes.Length < 8)
            {
                continue;
            }

            for (var i = 0; i <= bytes.Length - 8; i += 8)
            {
                var value = BitConverter.ToUInt64(bytes, i);
                if (targets.TryGetValue(value, out var label))
                {
                    results.Add(new PointerOccurrence(address + (ulong)i, value, label));
                }
            }
        }

        return results;
    }

    private static List<InventoryWindowCandidate> FindHeapObjectsWithVtables(
        VmmProcess process,
        IReadOnlyDictionary<ulong, string> vtableCandidates,
        uint minAlloc,
        uint maxAlloc,
        int maxResults)
    {
        var results = new List<InventoryWindowCandidate>();
        if (vtableCandidates.Count == 0 || maxResults <= 0)
        {
            return results;
        }

        try
        {
            var heaps = process.MapHeap();
            if (heaps.heaps is null)
            {
                return results;
            }

            var seen = new HashSet<ulong>();
            foreach (var heap in heaps.heaps)
            {
                VmmProcess.HeapAllocEntry[] allocations;
                try
                {
                    allocations = process.MapHeapAlloc(heap.iHeapNum);
                }
                catch
                {
                    continue;
                }

                if (allocations is null)
                {
                    continue;
                }

                foreach (var allocation in allocations)
                {
                    if (results.Count >= maxResults)
                    {
                        return results;
                    }

                    if (allocation.va == 0 ||
                        allocation.cb < minAlloc ||
                        allocation.cb > maxAlloc ||
                        !seen.Add(allocation.va))
                    {
                        continue;
                    }

                    if (TryReadUInt64(process, allocation.va, out var vtable) &&
                        vtableCandidates.TryGetValue(vtable, out var source))
                    {
                        results.Add(new InventoryWindowCandidate(
                            allocation.va,
                            vtable,
                            source,
                            allocation.cb));
                    }
                }
            }
        }
        catch
        {
            return results;
        }

        return results;
    }

    private static List<InventoryWindowCandidate> FindVadObjectsWithVtables(
        VmmProcess process,
        IReadOnlyDictionary<ulong, string> vtableCandidates,
        ulong maxScanBytes,
        int maxResults)
    {
        var results = new List<InventoryWindowCandidate>();
        if (vtableCandidates.Count == 0 || maxScanBytes == 0 || maxResults <= 0)
        {
            return results;
        }

        const int chunkSize = 0x10000;
        var scanned = 0UL;
        var seen = new HashSet<ulong>();

        VmmProcess.VadEntry[] vads;
        try
        {
            vads = process.MapVAD(true);
        }
        catch
        {
            return results;
        }

        if (vads is null)
        {
            return results;
        }

        foreach (var vad in vads.OrderBy(vad => vad.vaStart))
        {
            if (results.Count >= maxResults || scanned >= maxScanBytes)
            {
                break;
            }

            if (vad.vaStart == 0 ||
                vad.vaEnd <= vad.vaStart ||
                vad.fImage ||
                vad.fTeb ||
                vad.sText is not null && vad.sText.IndexOf("Game.dll", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            var vadSize = vad.vaEnd - vad.vaStart + 1;
            if (vadSize < 8)
            {
                continue;
            }

            var offset = 0UL;
            while (offset < vadSize && scanned < maxScanBytes && results.Count < maxResults)
            {
                var readSize = (int)Math.Min((ulong)chunkSize, vadSize - offset);
                var address = vad.vaStart + offset;
                if (TryReadBytes(process, address, readSize, out var bytes) && bytes.Length >= 8)
                {
                    for (var i = 0; i <= bytes.Length - 8 && results.Count < maxResults; i += 8)
                    {
                        var value = BitConverter.ToUInt64(bytes, i);
                        if (vtableCandidates.TryGetValue(value, out var source))
                        {
                            var objectAddress = address + (ulong)i;
                            if (seen.Add(objectAddress))
                            {
                                results.Add(new InventoryWindowCandidate(
                                    objectAddress,
                                    value,
                                    "vad:" + source,
                                    (uint)Math.Min(vadSize, uint.MaxValue)));
                            }
                        }
                    }
                }

                offset += (ulong)readSize;
                scanned += (ulong)readSize;
            }
        }

        return results;
    }

    private static byte[] MemRead(VmmProcess process, ulong address, uint count, bool bypassMemoryCache = false)
    {
        return bypassMemoryCache
            ? process.MemRead(address, count, VmmReadFlagNoCache)
            : process.MemRead(address, count);
    }

    private static bool TryReadByte(
        VmmProcess process,
        ulong address,
        out byte value,
        bool bypassMemoryCache = false)
    {
        value = 0;
        try
        {
            var buffer = MemRead(process, address, 1, bypassMemoryCache);
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

    private static bool TryReadBytes(
        VmmProcess process,
        ulong address,
        int count,
        out byte[] value,
        bool bypassMemoryCache = false)
    {
        value = Array.Empty<byte>();
        try
        {
            var buffer = MemRead(process, address, (uint)count, bypassMemoryCache);
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

    private static byte ReadItemQualityRank(
        VmmProcess process,
        ulong gameBase,
        uint templateId,
        Dictionary<uint, byte> qualityByTemplate,
        Dictionary<uint, byte[]> staticChunkCache)
    {
        if (templateId == 0)
        {
            return 0;
        }

        if (qualityByTemplate.TryGetValue(templateId, out var cached))
        {
            return cached;
        }

        var quality = TryReadItemStaticQualityRank(
            process,
            gameBase,
            templateId,
            staticChunkCache,
            out var rank)
                ? rank
                : (byte)0;
        qualityByTemplate[templateId] = quality;
        return quality;
    }

    private static bool TryReadItemStaticQualityRank(
        VmmProcess process,
        ulong gameBase,
        uint templateId,
        Dictionary<uint, byte[]> staticChunkCache,
        out byte qualityRank)
    {
        qualityRank = 0;
        if (!TryFindStaticItemPackedHandle(process, gameBase, templateId, out var packedHandle))
        {
            return false;
        }

        var rawChunkIndex = packedHandle >> StaticResolverPackedChunkShift;
        var chunkIndex = rawChunkIndex == 0 ? 0 : rawChunkIndex - 1;
        var recordOffset = packedHandle & StaticResolverPackedOffsetMask;
        if (!TryReadStaticResolverChunk(process, gameBase, chunkIndex, staticChunkCache, out var chunk) ||
            recordOffset + ItemStaticRecordQualityRankOffset >= chunk.Length ||
            recordOffset + sizeof(uint) > chunk.Length)
        {
            return false;
        }

        var offset = checked((int)recordOffset);
        var recordId = BitConverter.ToUInt32(chunk, offset + (int)ItemStaticRecordIdOffset);
        if (recordId != templateId)
        {
            return false;
        }

        qualityRank = chunk[offset + ItemStaticRecordQualityRankOffset];
        return true;
    }

    private static bool TryFindStaticItemPackedHandle(
        VmmProcess process,
        ulong gameBase,
        uint templateId,
        out uint packedHandle)
    {
        packedHandle = 0;
        if (!TryReadUInt32(process, gameBase + ItemStaticIndexRva + 0x04, out var count) ||
            count == 0 ||
            count > MaxStaticResolverEntries ||
            !TryReadPointer(process, gameBase + ItemStaticIndexRva + 0x10, out var entries) ||
            entries == 0)
        {
            return false;
        }

        var left = 0;
        var right = checked((int)count) - 1;
        while (left <= right)
        {
            var middle = left + ((right - left) / 2);
            var entry = entries + ((ulong)middle * StaticResolverEntrySize);
            if (!TryReadUInt32(process, entry, out var key))
            {
                return false;
            }

            if (templateId < key)
            {
                right = middle - 1;
                continue;
            }

            if (templateId > key)
            {
                left = middle + 1;
                continue;
            }

            if (!TryReadUInt64(process, entry + StaticResolverPackedHandleOffset, out var rawHandle))
            {
                return false;
            }

            packedHandle = unchecked((uint)rawHandle);
            return packedHandle != 0;
        }

        return false;
    }

    private static bool TryReadStaticResolverChunk(
        VmmProcess process,
        ulong gameBase,
        uint chunkIndex,
        Dictionary<uint, byte[]> staticChunkCache,
        out byte[] chunk)
    {
        if (staticChunkCache.TryGetValue(chunkIndex, out chunk!))
        {
            return true;
        }

        chunk = Array.Empty<byte>();
        if (!TryReadPointer(process, gameBase + StaticResolverChunkListRva + ((ulong)chunkIndex * 8), out var chunkPointer) ||
            chunkPointer == 0 ||
            !TryReadUInt32(process, chunkPointer, out var compressedSize) ||
            !TryReadUInt32(process, chunkPointer + 0x04, out var uncompressedSize) ||
            compressedSize <= 6 ||
            compressedSize > MaxStaticChunkCompressedBytes ||
            uncompressedSize == 0 ||
            uncompressedSize > MaxStaticChunkUncompressedBytes ||
            !TryReadBytes(process, chunkPointer + 0x08, checked((int)compressedSize), out var compressed))
        {
            return false;
        }

        if (!TryInflateZlib(compressed, checked((int)uncompressedSize), out chunk))
        {
            return false;
        }

        staticChunkCache[chunkIndex] = chunk;
        return true;
    }

    private static bool TryInflateZlib(byte[] zlib, int expectedSize, out byte[] inflated)
    {
        inflated = Array.Empty<byte>();
        if (zlib.Length <= 6 || expectedSize <= 0)
        {
            return false;
        }

        try
        {
            using var input = new MemoryStream(zlib, 2, zlib.Length - 6);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream(expectedSize);
            deflate.CopyTo(output);
            inflated = output.ToArray();
            return inflated.Length >= expectedSize;
        }
        catch
        {
            inflated = Array.Empty<byte>();
            return false;
        }
    }

    private static bool TryReadUtf16String(
        VmmProcess process,
        ulong address,
        int maxChars,
        out string value,
        bool bypassMemoryCache = false)
    {
        value = string.Empty;
        if (maxChars <= 0)
        {
            return true;
        }

        if (!TryReadBytes(process, address, maxChars * 2, out var buffer, bypassMemoryCache))
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

    private static bool TryReadUInt16(
        VmmProcess process,
        ulong address,
        out ushort value,
        bool bypassMemoryCache = false)
    {
        value = 0;
        try
        {
            var buffer = MemRead(process, address, 2, bypassMemoryCache);
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

    private static bool TryReadInt16(VmmProcess process, ulong address, out short value)
    {
        value = 0;
        try
        {
            var buffer = process.MemRead(address, 2);
            if (buffer is null || buffer.Length < 2)
            {
                return false;
            }

            value = BitConverter.ToInt16(buffer, 0);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadSingle(
        VmmProcess process,
        ulong address,
        out float value,
        bool bypassMemoryCache = false)
    {
        value = 0;
        try
        {
            var buffer = MemRead(process, address, 4, bypassMemoryCache);
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

    private static bool TryReadDouble(VmmProcess process, ulong address, out double value)
    {
        value = 0;
        try
        {
            var buffer = process.MemRead(address, 8);
            if (buffer is null || buffer.Length < 8)
            {
                return false;
            }

            value = BitConverter.ToDouble(buffer, 0);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadUInt32(
        VmmProcess process,
        ulong address,
        out uint value,
        bool bypassMemoryCache = false)
    {
        value = 0;
        try
        {
            var buffer = MemRead(process, address, 4, bypassMemoryCache);
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

    private static bool TryReadUInt64(
        VmmProcess process,
        ulong address,
        out ulong value,
        bool bypassMemoryCache = false)
    {
        value = 0;
        try
        {
            var buffer = MemRead(process, address, 8, bypassMemoryCache);
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

    private static bool TryReadPointer(
        VmmProcess process,
        ulong address,
        out ulong value,
        bool bypassMemoryCache = false)
    {
        value = 0;
        if (TryReadUInt64(process, address, out var v64, bypassMemoryCache) && IsLikelyUserPointer(v64))
        {
            value = v64;
            return true;
        }

        if (TryReadUInt32(process, address, out var v32, bypassMemoryCache) && v32 != 0)
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

    private readonly record struct PointerOccurrence(
        ulong Address,
        ulong Value,
        string Label);

    private readonly record struct InventoryWindowCandidate(
        ulong ObjectAddress,
        ulong VtableAddress,
        string Source,
        uint AllocationSize);

    private readonly record struct InventoryWindowCandidateState(
        InventoryWindowCandidate Candidate,
        bool IsVisible,
        ulong WidgetFlags);

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
        public uint InteractionState;
        public uint MaxHp;
        public uint CurrentHp;
        public uint LootableRaw;
        public string Name = string.Empty;
        public string ResolveSource = string.Empty;
        public bool HasRestState;
        public uint StanceFlags;
        public uint MotionMode;
    }

    private sealed record VisibleActorInfo(
        ushort EntityId,
        ushort EntityType,
        ulong Entity,
        ActorInfo Actor);

    private sealed record PartyMemberInfo(
        string ListName,
        ulong MemberAddress,
        uint ServerObjectId);

    private sealed class PartyLiveContext
    {
        public ushort LocalEntityId { get; set; }

        public ulong LocalEntityAddress { get; set; }

        public ulong LocalActorAddress { get; set; }

        public uint LocalServerObjectId { get; set; }

        public uint LocalTargetServerObjectId { get; set; }

        public string LocalName { get; set; } = string.Empty;

        public Vector3Snapshot? LocalPosition { get; set; }

        public Dictionary<uint, PartyLiveActorInfo> VisiblePlayerActorsByServerObjectId { get; } = new();
    }

    private sealed record PartyLiveActorInfo(
        ushort EntityId,
        ulong EntityAddress,
        ulong ActorAddress,
        string ActorName,
        uint TargetServerObjectId,
        bool HasRestState,
        uint StanceFlags,
        uint MotionMode,
        Vector3Snapshot? Position);

    private struct InventoryItemInfo
    {
        public uint InstanceId;
        public uint TemplateId;
        public ulong Count;
        public string Name;
        public uint ItemType;
        public byte QualityRank;
        public uint EquipmentMask;
        public ulong VendorSellUnitPrice;
        public short Slot;
        public bool IsInEquipmentArray;
    }

    private sealed class SummonedPetOwnerInfo
    {
        public SummonedPetOwnerInfo(
            SummonedPetOwnerKind ownerKind,
            uint serverObjectId,
            string ownerName,
            string partyListName,
            AionClassId? ownerClassId = null,
            string ownerClassName = "")
        {
            OwnerKind = ownerKind;
            ServerObjectId = serverObjectId;
            OwnerName = ownerName;
            PartyListName = partyListName;
            OwnerClassId = ownerClassId;
            OwnerClassName = ownerClassName;
        }

        public SummonedPetOwnerKind OwnerKind { get; }

        public uint ServerObjectId { get; }

        public string OwnerName { get; set; }

        public string PartyListName { get; }

        public AionClassId? OwnerClassId { get; set; }

        public string OwnerClassName { get; set; }
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

    private struct GatherReadCounters
    {
        public int ScannedServerObjects;
        public int ResolvedEntities;
        public int ResolvedActors;
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
        public string SkillCategory;
        public string SkillType;
        public string SubType;
        public string ActivationAttribute;
        public string TargetSlot;
        public string DispelCategory;
        public string FirstTarget;
        public string TargetRelationRestriction;
        public string TargetRange;
        public string ChainCategoryName;
        public string PrechainCategoryName;
        public string ChainTime;
        public string StatusFx;
        public string AuraFx;
        public string CounterSkill;
        public string TargetValidStatuses;
        public string CostDp;
        public string UltraSkill;
        public string Effect1Type;
        public string Effect2Type;
        public string Effect3Type;
        public string Effect4Type;
        public int? EffectRemainMs;
        public int? EffectCheckTimeMs;
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
