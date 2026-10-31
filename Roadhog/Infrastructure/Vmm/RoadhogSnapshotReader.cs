using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

/// <summary>
/// The business-facing edge of the DMA pipeline.  It requests validated
/// publications, retries below the business boundary, and exposes no partial,
/// failed, or default-valued read result.
/// </summary>
internal sealed partial class RoadhogSnapshotReader : IRoadhogSnapshotReader
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan FaultLogInterval = TimeSpan.FromSeconds(2);

    private readonly IRoadhogGameApi _gameApi;
    private readonly IRoadhogLogger _logger;
    private readonly CancellationToken _stopToken;
    private readonly GameApiReadContext _readContext;
    private readonly Action<PlayerSnapshot>? _playerObserved;
    private readonly object _versionSync = new();
    private readonly Dictionary<string, long> _versions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _faultLogAt = new(StringComparer.Ordinal);

    public RoadhogSnapshotReader(
        AccountConfig config,
        IRoadhogGameApi gameApi,
        IRoadhogLogger logger,
        CancellationToken stopToken,
        Action<PlayerSnapshot>? playerObserved = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        _gameApi = gameApi ?? throw new ArgumentNullException(nameof(gameApi));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _stopToken = stopToken;
        _playerObserved = playerObserved;
        _readContext = new GameApiReadContext(
            config.AccountName,
            config.ProcessId,
            config.TargetProcessName,
            config.VmmDeviceName,
            BypassMemoryCache: false,
            IncludeThirdQuickbar: config.ScriptSettings?.QuickbarKeyMode == QuickbarKeyMode.GameDefault);
    }

    public Task<PublishedGameSnapshot<PlayerSnapshot>> ReadPlayerAsync(long afterVersion = 0) =>
        ReadPlayerAsync(_readContext, afterVersion);

    public Task<PublishedGameSnapshot<QuickbarSnapshot>> ReadQuickbarAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync("quickbar",
            stop => _gameApi is IQuickbarGameApi api ? api.ReadQuickbarAsync(_readContext, stop)
                : Missing<QuickbarSnapshot>("Quickbar channel is unavailable."), afterVersion);

    private async Task<PublishedGameSnapshot<PlayerSnapshot>> ReadPlayerAsync(
        GameApiReadContext readContext,
        long afterVersion)
    {
        var published = await ReadUntilPublishedAsync(
            "player",
            stop => _gameApi is IRoadhogScopedGameApi scoped
                ? scoped.ReadPlayerAsync(readContext, stop)
                : _gameApi.ReadPlayerAsync(stop),
            afterVersion,
            static player => player.Position is not null,
            "Player position is unavailable.").ConfigureAwait(false);
        _stopToken.ThrowIfCancellationRequested();
        _playerObserved?.Invoke(published.Value);
        return published;
    }

    public Task<PublishedGameSnapshot<PlayerAbnormalStatusSnapshot>> ReadPlayerAbnormalStatusesAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync(
            "player_abnormal_statuses",
            stop => _gameApi is IRoadhogScopedGameApi scoped
                ? scoped.ReadPlayerAbnormalStatusesAsync(_readContext, stop)
                : _gameApi.ReadPlayerAbnormalStatusesAsync(stop),
            afterVersion);

    public Task<PublishedGameSnapshot<SummonedPetSnapshot>> ReadSummonedPetAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync(
            "summoned_pet",
            stop => _gameApi is IRoadhogScopedGameApi scoped
                ? scoped.ReadSummonedPetAsync(_readContext, stop)
                : _gameApi.ReadSummonedPetAsync(stop),
            afterVersion,
            static pet => !pet.IsSummoned || pet.HasReliableHealth,
            "Summoned-pet snapshot is incomplete.");

    public Task<PublishedGameSnapshot<SummonedPetRosterSnapshot>> ReadSummonedPetRosterAsync(long afterVersion = 0) =>
        ReadSummonedPetRosterAsync(_readContext, afterVersion);

    private Task<PublishedGameSnapshot<SummonedPetRosterSnapshot>> ReadSummonedPetRosterAsync(
        GameApiReadContext readContext,
        long afterVersion) =>
        ReadUntilPublishedAsync(
            "summoned_pet_roster",
            stop => _gameApi is IRoadhogScopedGameApi scoped
                ? scoped.ReadSummonedPetRosterAsync(readContext, stop)
                : _gameApi.ReadSummonedPetRosterAsync(stop),
            afterVersion,
            static roster =>
                HasPublishablePetHealth(roster.LocalPlayerPet.Pet) &&
                roster.PartyMemberPets.All(static pet => HasPublishablePetHealth(pet.Pet)),
            "Summoned-pet roster snapshot is incomplete.");

    private static bool HasPublishablePetHealth(SummonedPetSnapshot pet) =>
        !pet.IsSummoned || pet.HasReliableHealth;

    public Task<PublishedGameSnapshot<PartySnapshot>> ReadPartyAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync(
            "party",
            stop => _gameApi is IRoadhogScopedPartyGameApi scoped
                ? scoped.ReadPartyAsync(_readContext, stop)
                : _gameApi is IRoadhogPartyGameApi api
                    ? api.ReadPartyAsync(stop)
                    : Missing<PartySnapshot>("Party snapshot channel is unavailable."),
            afterVersion);

    public Task<PublishedGameSnapshot<TacticsSignSnapshot>> ReadTacticsSignsAsync(long afterVersion = 0) =>
        ReadTacticsSignsAsync(_readContext, afterVersion);

    private Task<PublishedGameSnapshot<TacticsSignSnapshot>> ReadTacticsSignsAsync(
        GameApiReadContext readContext,
        long afterVersion) =>
        ReadUntilPublishedAsync(
            "tactics_signs",
            stop => _gameApi is IRoadhogScopedTacticsSignGameApi scoped
                ? scoped.ReadTacticsSignsAsync(readContext, stop)
                : _gameApi is IRoadhogTacticsSignGameApi api
                    ? api.ReadTacticsSignsAsync(stop)
                    : Missing<TacticsSignSnapshot>("Tactics-sign snapshot channel is unavailable."),
            afterVersion);

    public Task<PublishedGameSnapshot<ChannelSnapshot>> ReadChannelAsync(long afterVersion = 0) =>
        ReadChannelAsync(_readContext, afterVersion);

    public Task<PublishedGameSnapshot<ChannelSwitchUiSnapshot>> ReadChannelSwitchUiAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync(
            "channel_switch_ui",
            stop => _gameApi is IChannelSwitchUiGameApi api
                ? api.ReadChannelSwitchUiAsync(_readContext, stop)
                : Missing<ChannelSwitchUiSnapshot>("Channel UI snapshot channel is unavailable."),
            afterVersion);

    public Task<PublishedGameSnapshot<PersonalShopSnapshot>> ReadPersonalShopAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync("personal_shop",
            stop => _gameApi is IInventoryInteractionGameApi api ? api.ReadPersonalShopAsync(_readContext, stop)
                : Missing<PersonalShopSnapshot>("Personal shop channel is unavailable."), afterVersion);

    public Task<PublishedGameSnapshot<EquipmentUpgradeInventory>> ReadEquipmentUpgradeInventoryAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync("equipment_upgrade_inventory",
            stop => _gameApi is IEquipmentUpgradeGameApi api ? api.ReadEquipmentUpgradeInventoryAsync(_readContext, stop)
                : Missing<EquipmentUpgradeInventory>("Equipment channel is unavailable."), afterVersion);

    public Task<PublishedGameSnapshot<EquipmentUpgradeUi>> ReadEquipmentUpgradeUiAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync("equipment_upgrade_ui",
            stop => _gameApi is IEquipmentUpgradeGameApi api ? api.ReadEquipmentUpgradeUiAsync(_readContext, stop)
                : Missing<EquipmentUpgradeUi>("Equipment UI channel is unavailable."), afterVersion);

    public Task<PublishedGameSnapshot<AuctionHouseSnapshot>> ReadAuctionHouseAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync("auction_house",
            stop => _gameApi is IAuctionHouseGameApi api ? api.ReadAuctionHouseAsync(_readContext, stop)
                : Missing<AuctionHouseSnapshot>("Auction house channel is unavailable."), afterVersion);

    public Task<PublishedGameSnapshot<NpcTradeSnapshot>> ReadNpcTradeAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync("npc_trade",
            stop => _gameApi is INpcTradeGameApi api ? api.ReadNpcTradeAsync(_readContext, stop)
                : Missing<NpcTradeSnapshot>("NPC trade channel is unavailable."), afterVersion);

    public Task<PublishedGameSnapshot<InventoryInteractionSnapshot>> ReadInventoryInteractionAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync("inventory_interaction",
            stop => _gameApi is IInventoryInteractionGameApi api ? api.ReadInventoryInteractionAsync(_readContext, stop)
                : Missing<InventoryInteractionSnapshot>("Inventory interaction channel is unavailable."), afterVersion);

    public Task<PublishedGameSnapshot<GameUiCursorSnapshot>> ReadUiCursorAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync("ui_cursor",
            stop => _gameApi is IInventoryInteractionGameApi api ? api.ReadUiCursorAsync(_readContext, stop)
                : Missing<GameUiCursorSnapshot>("Personal shop cursor channel is unavailable."), afterVersion);

    public Task<PublishedGameSnapshot<ReviveUiSnapshot>> ReadReviveUiAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync("revive_ui",
            stop => _gameApi is IReviveUiGameApi api ? api.ReadReviveUiAsync(_readContext, stop)
                : Missing<ReviveUiSnapshot>("Revive UI channel is unavailable."), afterVersion);

    public Task<PublishedGameSnapshot<ChannelTransitionSnapshot>> ReadChannelTransitionAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync(
            "channel_transition",
            stop => _gameApi is IChannelTransitionGameApi api
                ? api.ReadChannelTransitionAsync(_readContext, stop)
                : Missing<ChannelTransitionSnapshot>("Channel transition snapshot channel is unavailable."),
            afterVersion);

    private Task<PublishedGameSnapshot<ChannelSnapshot>> ReadChannelAsync(
        GameApiReadContext readContext,
        long afterVersion) =>
        ReadUntilPublishedAsync(
            "channel",
            stop => _gameApi is IRoadhogScopedChannelGameApi scoped
                ? scoped.ReadChannelAsync(readContext, stop)
                : _gameApi is IRoadhogChannelGameApi api
                    ? api.ReadChannelAsync(stop)
                    : Missing<ChannelSnapshot>("Channel snapshot channel is unavailable."),
            afterVersion,
            static channel => channel.IsValid,
            "Channel snapshot is structurally invalid.");

    public Task<PublishedGameSnapshot<LockedTargetSnapshot>> ReadLockedTargetAsync(long afterVersion = 0) =>
        ReadLockedTargetAsync(_readContext, afterVersion);

    private Task<PublishedGameSnapshot<LockedTargetSnapshot>> ReadLockedTargetAsync(
        GameApiReadContext readContext,
        long afterVersion) =>
        ReadUntilPublishedAsync(
            "locked_target",
            stop => _gameApi is IRoadhogScopedGameApi scoped
                ? scoped.ReadLockedTargetAsync(readContext, stop)
                : _gameApi.ReadLockedTargetAsync(stop),
            afterVersion);

    public Task<PublishedGameSnapshot<LockedTargetAbnormalStatusSnapshot>> ReadLockedTargetAbnormalStatusesAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync(
            "locked_target_abnormal_statuses",
            stop => _gameApi is IRoadhogScopedGameApi scoped
                ? scoped.ReadLockedTargetAbnormalStatusesAsync(_readContext, stop)
                : _gameApi.ReadLockedTargetAbnormalStatusesAsync(stop),
            afterVersion);

    public Task<PublishedGameSnapshot<IReadOnlyList<SkillSnapshot>>> ReadSkillsAsync(
        IReadOnlyCollection<uint>? skillIds = null,
        long afterVersion = 0)
    {
        var partition = skillIds is { Count: > 0 }
            ? string.Join(",", skillIds.Where(static id => id != 0).Distinct().Order())
            : "all";
        return ReadUntilPublishedAsync(
            "skills:" + partition,
            stop => _gameApi is IRoadhogScopedGameApi scoped
                ? skillIds is { Count: > 0 }
                    ? scoped.ReadSkillsAsync(_readContext, skillIds, stop)
                    : scoped.ReadSkillsAsync(_readContext, stop)
                : _gameApi.ReadSkillsAsync(stop),
            afterVersion);
    }

    public Task<PublishedGameSnapshot<IReadOnlyList<InventoryItemSnapshot>>> ReadInventoryAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync(
            "inventory",
            stop => _gameApi is IRoadhogScopedGameApi scoped
                ? scoped.ReadInventoryAsync(_readContext, stop)
                : _gameApi.ReadInventoryAsync(stop),
            afterVersion);

    public Task<PublishedGameSnapshot<ulong>> ReadInventoryMoneyAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync(
            "inventory_money",
            stop => _gameApi is IInventoryMoneyGameApi api
                ? api.ReadInventoryMoneyAsync(_readContext, stop)
                : Missing<ulong>("Inventory-money snapshot channel is unavailable."),
            afterVersion);

    public Task<PublishedGameSnapshot<int>> ReadInventoryCapacityAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync(
            "inventory_capacity",
            stop => _gameApi is IInventoryCapacityGameApi api
                ? api.ReadInventoryCapacityAsync(_readContext, stop)
                : Missing<int>("Inventory-capacity snapshot channel is unavailable."),
            afterVersion,
            static capacity => capacity > 0,
            "Inventory capacity must be positive.");

    public Task<PublishedGameSnapshot<IReadOnlyList<WorldObjectSnapshot>>> ReadWorldObjectsAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync(
            "world_objects",
            stop => _gameApi is IRoadhogScopedGameApi scoped
                ? scoped.ReadWorldObjectsAsync(_readContext, stop)
                : _gameApi.ReadWorldObjectsAsync(stop),
            afterVersion);

    public Task<PublishedGameSnapshot<GatherSnapshot>> ReadGatherSnapshotAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync(
            "gather",
            stop => _gameApi is IRoadhogScopedGameApi scoped
                ? scoped.ReadGatherSnapshotAsync(_readContext, stop)
                : _gameApi.ReadGatherSnapshotAsync(stop),
            afterVersion,
            static gather =>
                gather.MonsterDataAvailable &&
                gather.CompetitionDataAvailable &&
                gather.LocalGathering.DataAvailable,
            "Gather snapshot is structurally incomplete.");

    public Task<PublishedGameSnapshot<IReadOnlyList<LootCorpseSnapshot>>> ReadLootCorpsesAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync(
            "loot_corpses",
            stop => _gameApi is IRoadhogScopedGameApi scoped
                ? scoped.ReadLootCorpsesAsync(_readContext, stop)
                : _gameApi.ReadLootCorpsesAsync(stop),
            afterVersion);

    public Task<PublishedGameSnapshot<InventoryWindowSnapshot>> ReadInventoryWindowAsync(
        InventoryWindowRectSource rectSource = InventoryWindowRectSource.LegacyDialogRect,
        long afterVersion = 0) =>
        ReadUntilPublishedAsync(
            "inventory_window:" + rectSource,
            stop => _gameApi is IInventoryWindowGameApi api
                ? api.ReadInventoryWindowAsync(_readContext, rectSource, stop)
                : Missing<InventoryWindowSnapshot>("Inventory-window snapshot channel is unavailable."),
            afterVersion);

    public Task<PublishedGameSnapshot<InventoryDiscardConfirmSnapshot>> ReadInventoryDiscardConfirmAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync(
            "inventory_discard_confirm",
            stop => _gameApi is IInventoryDiscardConfirmGameApi api
                ? api.ReadInventoryDiscardConfirmAsync(_readContext, stop)
                : Missing<InventoryDiscardConfirmSnapshot>("Inventory-discard-confirm snapshot channel is unavailable."),
            afterVersion);

    private async Task<PublishedGameSnapshot<T>> ReadUntilPublishedAsync<T>(
        string channel,
        Func<CancellationToken, Task<OperationResult<T>>> read,
        long afterVersion,
        Func<T, bool>? validate = null,
        string? validationError = null)
    {
        using var budget = SnapshotReadBudget.CreateCancellation(_stopToken);
        var stop = budget.Token;
        try
        {
            while (true)
            {
                stop.ThrowIfCancellationRequested();
                OperationResult<T> observed;
                try
                {
                    observed = await ReadBoundedAsync(channel, read, stop).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    LogReadFault(channel, ex.Message);
                    await Task.Delay(RetryDelay, stop).ConfigureAwait(false);
                    continue;
                }

                stop.ThrowIfCancellationRequested();
                if (observed.Success && observed.Value is { } value && (validate is null || validate(value)))
                {
                    var version = AdvanceVersion(channel);

                    if (version > afterVersion)
                    {
                        return new PublishedGameSnapshot<T>(version, value);
                    }
                }
                else
                {
                    LogReadFault(
                        channel,
                        observed.Success ? validationError ?? "Snapshot validation failed." : observed.Error);
                }

                await Task.Delay(RetryDelay, stop).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !_stopToken.IsCancellationRequested && !SnapshotReadBudget.OperationCancelled)
        {
            throw new SnapshotReadBudgetExceededException(channel);
        }
    }

    private long AdvanceVersion(string channel)
    {
        lock (_versionSync)
        {
            _versions.TryGetValue(channel, out var version);
            version++;
            _versions[channel] = version;
            return version;
        }
    }

    private void LogReadFault(string channel, string? error)
    {
        var now = DateTimeOffset.Now;
        lock (_versionSync)
        {
            if (_faultLogAt.TryGetValue(channel, out var last) && now - last < FaultLogInterval)
            {
                return;
            }

            _faultLogAt[channel] = now;
        }

        _logger.Warn("snapshot.read.retry", new Dictionary<string, object?>
        {
            ["account"] = _readContext.AccountName,
            ["channel"] = channel,
            ["error"] = error ?? string.Empty
        });
    }

    private static Task<OperationResult<T>> Missing<T>(string error) =>
        Task.FromResult(OperationResult<T>.Fail(error));
}
