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
internal sealed class RoadhogSnapshotReader : IRoadhogSnapshotReader
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan FaultLogInterval = TimeSpan.FromSeconds(2);

    private readonly IRoadhogGameApi _gameApi;
    private readonly IRoadhogLogger _logger;
    private readonly CancellationToken _stopToken;
    private readonly GameApiReadContext _readContext;
    private readonly GameApiReadContext _currentReadContext;
    private readonly object _versionSync = new();
    private readonly Dictionary<string, long> _versions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _faultLogAt = new(StringComparer.Ordinal);

    public RoadhogSnapshotReader(
        AccountConfig config,
        IRoadhogGameApi gameApi,
        IRoadhogLogger logger,
        CancellationToken stopToken)
    {
        ArgumentNullException.ThrowIfNull(config);
        _gameApi = gameApi ?? throw new ArgumentNullException(nameof(gameApi));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _stopToken = stopToken;
        _readContext = new GameApiReadContext(
            config.AccountName,
            config.ProcessId,
            config.TargetProcessName,
            config.VmmDeviceName,
            BypassMemoryCache: false,
            RequireFresh: true);
        _currentReadContext = _readContext with { BypassMemoryCache = true };
    }

    public Task<PublishedGameSnapshot<PlayerSnapshot>> ReadPlayerAsync(long afterVersion = 0) =>
        ReadPlayerAsync(_readContext, afterVersion);

    public Task<PublishedGameSnapshot<PlayerSnapshot>> ReadCurrentPlayerAsync(long afterVersion = 0) =>
        ReadPlayerAsync(_currentReadContext, afterVersion);

    private Task<PublishedGameSnapshot<PlayerSnapshot>> ReadPlayerAsync(
        GameApiReadContext readContext,
        long afterVersion) =>
        ReadUntilPublishedAsync(
            "player",
            () => _gameApi is IRoadhogScopedGameApi scoped
                ? scoped.ReadPlayerAsync(readContext, _stopToken)
                : _gameApi.ReadPlayerAsync(_stopToken),
            afterVersion,
            static player => player.Position is not null,
            "Player position is unavailable.");

    public Task<PublishedGameSnapshot<PlayerAbnormalStatusSnapshot>> ReadPlayerAbnormalStatusesAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync(
            "player_abnormal_statuses",
            () => _gameApi is IRoadhogScopedGameApi scoped
                ? scoped.ReadPlayerAbnormalStatusesAsync(_readContext, _stopToken)
                : _gameApi.ReadPlayerAbnormalStatusesAsync(_stopToken),
            afterVersion);

    public Task<PublishedGameSnapshot<SummonedPetSnapshot>> ReadSummonedPetAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync(
            "summoned_pet",
            () => _gameApi is IRoadhogScopedGameApi scoped
                ? scoped.ReadSummonedPetAsync(_readContext, _stopToken)
                : _gameApi.ReadSummonedPetAsync(_stopToken),
            afterVersion);

    public Task<PublishedGameSnapshot<SummonedPetRosterSnapshot>> ReadSummonedPetRosterAsync(long afterVersion = 0) =>
        ReadSummonedPetRosterAsync(_readContext, afterVersion);

    public Task<PublishedGameSnapshot<SummonedPetRosterSnapshot>> ReadCurrentSummonedPetRosterAsync(long afterVersion = 0) =>
        ReadSummonedPetRosterAsync(_currentReadContext, afterVersion);

    private Task<PublishedGameSnapshot<SummonedPetRosterSnapshot>> ReadSummonedPetRosterAsync(
        GameApiReadContext readContext,
        long afterVersion) =>
        ReadUntilPublishedAsync(
            "summoned_pet_roster",
            () => _gameApi is IRoadhogScopedGameApi scoped
                ? scoped.ReadSummonedPetRosterAsync(readContext, _stopToken)
                : _gameApi.ReadSummonedPetRosterAsync(_stopToken),
            afterVersion);

    public Task<PublishedGameSnapshot<PartySnapshot>> ReadPartyAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync(
            "party",
            () => _gameApi is IRoadhogScopedPartyGameApi scoped
                ? scoped.ReadPartyAsync(_readContext, _stopToken)
                : _gameApi is IRoadhogPartyGameApi api
                    ? api.ReadPartyAsync(_stopToken)
                    : Missing<PartySnapshot>("Party snapshot channel is unavailable."),
            afterVersion);

    public Task<PublishedGameSnapshot<TacticsSignSnapshot>> ReadTacticsSignsAsync(long afterVersion = 0) =>
        ReadTacticsSignsAsync(_readContext, afterVersion);

    public Task<PublishedGameSnapshot<TacticsSignSnapshot>> ReadCurrentTacticsSignsAsync(long afterVersion = 0) =>
        ReadTacticsSignsAsync(_currentReadContext, afterVersion);

    private Task<PublishedGameSnapshot<TacticsSignSnapshot>> ReadTacticsSignsAsync(
        GameApiReadContext readContext,
        long afterVersion) =>
        ReadUntilPublishedAsync(
            "tactics_signs",
            () => _gameApi is IRoadhogScopedTacticsSignGameApi scoped
                ? scoped.ReadTacticsSignsAsync(readContext, _stopToken)
                : _gameApi is IRoadhogTacticsSignGameApi api
                    ? api.ReadTacticsSignsAsync(_stopToken)
                    : Missing<TacticsSignSnapshot>("Tactics-sign snapshot channel is unavailable."),
            afterVersion);

    public Task<PublishedGameSnapshot<ChannelSnapshot>> ReadChannelAsync(long afterVersion = 0) =>
        ReadChannelAsync(_readContext, afterVersion);

    public Task<PublishedGameSnapshot<ChannelSnapshot>> ReadCurrentChannelAsync(long afterVersion = 0) =>
        ReadChannelAsync(_currentReadContext, afterVersion);

    private Task<PublishedGameSnapshot<ChannelSnapshot>> ReadChannelAsync(
        GameApiReadContext readContext,
        long afterVersion) =>
        ReadUntilPublishedAsync(
            "channel",
            () => _gameApi is IRoadhogScopedChannelGameApi scoped
                ? scoped.ReadChannelAsync(readContext, _stopToken)
                : _gameApi is IRoadhogChannelGameApi api
                    ? api.ReadChannelAsync(_stopToken)
                    : Missing<ChannelSnapshot>("Channel snapshot channel is unavailable."),
            afterVersion);

    public Task<PublishedGameSnapshot<LockedTargetSnapshot>> ReadLockedTargetAsync(long afterVersion = 0) =>
        ReadLockedTargetAsync(_readContext, afterVersion);

    public Task<PublishedGameSnapshot<LockedTargetSnapshot>> ReadCurrentLockedTargetAsync(long afterVersion = 0) =>
        ReadLockedTargetAsync(_currentReadContext, afterVersion);

    private Task<PublishedGameSnapshot<LockedTargetSnapshot>> ReadLockedTargetAsync(
        GameApiReadContext readContext,
        long afterVersion) =>
        ReadUntilPublishedAsync(
            "locked_target",
            () => _gameApi is IRoadhogScopedGameApi scoped
                ? scoped.ReadLockedTargetAsync(readContext, _stopToken)
                : _gameApi.ReadLockedTargetAsync(_stopToken),
            afterVersion);

    public Task<PublishedGameSnapshot<LockedTargetAbnormalStatusSnapshot>> ReadLockedTargetAbnormalStatusesAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync(
            "locked_target_abnormal_statuses",
            () => _gameApi is IRoadhogScopedGameApi scoped
                ? scoped.ReadLockedTargetAbnormalStatusesAsync(_readContext, _stopToken)
                : _gameApi.ReadLockedTargetAbnormalStatusesAsync(_stopToken),
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
            () => _gameApi is IRoadhogScopedGameApi scoped
                ? skillIds is { Count: > 0 }
                    ? scoped.ReadSkillsAsync(_readContext, skillIds, _stopToken)
                    : scoped.ReadSkillsAsync(_readContext, _stopToken)
                : _gameApi.ReadSkillsAsync(_stopToken),
            afterVersion);
    }

    public Task<PublishedGameSnapshot<IReadOnlyList<InventoryItemSnapshot>>> ReadInventoryAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync(
            "inventory",
            () => _gameApi is IRoadhogScopedGameApi scoped
                ? scoped.ReadInventoryAsync(_readContext, _stopToken)
                : _gameApi.ReadInventoryAsync(_stopToken),
            afterVersion);

    public Task<PublishedGameSnapshot<ulong>> ReadInventoryMoneyAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync(
            "inventory_money",
            () => _gameApi is IInventoryMoneyGameApi api
                ? api.ReadInventoryMoneyAsync(_readContext, _stopToken)
                : Missing<ulong>("Inventory-money snapshot channel is unavailable."),
            afterVersion);

    public Task<PublishedGameSnapshot<int>> ReadInventoryCapacityAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync(
            "inventory_capacity",
            () => _gameApi is IInventoryCapacityGameApi api
                ? api.ReadInventoryCapacityAsync(_readContext, _stopToken)
                : Missing<int>("Inventory-capacity snapshot channel is unavailable."),
            afterVersion,
            static capacity => capacity > 0,
            "Inventory capacity must be positive.");

    public Task<PublishedGameSnapshot<IReadOnlyList<WorldObjectSnapshot>>> ReadWorldObjectsAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync(
            "world_objects",
            () => _gameApi is IRoadhogScopedGameApi scoped
                ? scoped.ReadWorldObjectsAsync(_readContext, _stopToken)
                : _gameApi.ReadWorldObjectsAsync(_stopToken),
            afterVersion);

    public Task<PublishedGameSnapshot<GatherSnapshot>> ReadGatherSnapshotAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync(
            "gather",
            () => _gameApi is IRoadhogScopedGameApi scoped
                ? scoped.ReadGatherSnapshotAsync(_readContext, _stopToken)
                : _gameApi.ReadGatherSnapshotAsync(_stopToken),
            afterVersion);

    public Task<PublishedGameSnapshot<IReadOnlyList<LootCorpseSnapshot>>> ReadLootCorpsesAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync(
            "loot_corpses",
            () => _gameApi is IRoadhogScopedGameApi scoped
                ? scoped.ReadLootCorpsesAsync(_readContext, _stopToken)
                : _gameApi.ReadLootCorpsesAsync(_stopToken),
            afterVersion);

    public Task<PublishedGameSnapshot<InventoryWindowSnapshot>> ReadInventoryWindowAsync(
        InventoryWindowRectSource rectSource = InventoryWindowRectSource.LegacyDialogRect,
        long afterVersion = 0) =>
        ReadUntilPublishedAsync(
            "inventory_window:" + rectSource,
            () => _gameApi is IInventoryWindowGameApi api
                ? api.ReadInventoryWindowAsync(_readContext, rectSource, _stopToken)
                : Missing<InventoryWindowSnapshot>("Inventory-window snapshot channel is unavailable."),
            afterVersion);

    public Task<PublishedGameSnapshot<InventoryDiscardConfirmSnapshot>> ReadInventoryDiscardConfirmAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync(
            "inventory_discard_confirm",
            () => _gameApi is IInventoryDiscardConfirmGameApi api
                ? api.ReadInventoryDiscardConfirmAsync(_readContext, _stopToken)
                : Missing<InventoryDiscardConfirmSnapshot>("Inventory-discard-confirm snapshot channel is unavailable."),
            afterVersion);

    private async Task<PublishedGameSnapshot<T>> ReadUntilPublishedAsync<T>(
        string channel,
        Func<Task<OperationResult<T>>> read,
        long afterVersion,
        Func<T, bool>? validate = null,
        string? validationError = null)
    {
        while (true)
        {
            _stopToken.ThrowIfCancellationRequested();
            OperationResult<T> observed;
            try
            {
                observed = await read().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stopToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogReadFault(channel, ex.Message);
                await Task.Delay(RetryDelay, _stopToken).ConfigureAwait(false);
                continue;
            }

            if (observed.Success && observed.Value is { } value && (validate is null || validate(value)))
            {
                long version;
                lock (_versionSync)
                {
                    _versions.TryGetValue(channel, out version);
                    version++;
                    _versions[channel] = version;
                }

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

            await Task.Delay(RetryDelay, _stopToken).ConfigureAwait(false);
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
