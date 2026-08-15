using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

/// <summary>
/// Typed registration for one externally-read data stream.  Production reads
/// must use one of these tokens so a new DMA datum cannot silently bypass the
/// stable-snapshot channel.
/// </summary>
internal interface IDmaSnapshotChannel
{
    string Name { get; }

    Type ValueType { get; }

    bool Partitioned { get; }

    DmaSnapshotReadPolicy ReadPolicy { get; }

    DmaSnapshotMergePolicy MergePolicy { get; }
}

internal enum DmaSnapshotReadPolicy
{
    Stable = 0,
    RequireFresh = 1
}

internal enum DmaSnapshotMergePolicy
{
    Replace = 0,
    FieldAware = 1
}

internal sealed class DmaSnapshotChannel<T> : IDmaSnapshotChannel
{
    private readonly DmaSnapshotChannelRegistry _owner;

    internal DmaSnapshotChannel(
        DmaSnapshotChannelRegistry owner,
        string name,
        bool partitioned,
        DmaSnapshotReadPolicy readPolicy,
        DmaSnapshotMergePolicy mergePolicy)
    {
        _owner = owner;
        Name = name;
        Partitioned = partitioned;
        ReadPolicy = readPolicy;
        MergePolicy = mergePolicy;
    }

    public string Name { get; }

    public Type ValueType => typeof(T);

    public bool Partitioned { get; }

    public DmaSnapshotReadPolicy ReadPolicy { get; }

    public DmaSnapshotMergePolicy MergePolicy { get; }

    public string ResolveDataKey(string? partitionKey = null)
    {
        return _owner.ResolveDataKey(this, partitionKey);
    }
}

internal sealed record DmaSnapshotChannelMetadata(
    string Name,
    Type ValueType,
    bool Partitioned,
    DmaSnapshotReadPolicy ReadPolicy,
    DmaSnapshotMergePolicy MergePolicy);

internal sealed class DmaSnapshotReadCoordinator
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, object> _gates = new(StringComparer.Ordinal);

    public TResult Execute<TChannel, TResult>(
        string scopeKey,
        DmaSnapshotChannel<TChannel> channel,
        Func<TResult> readAndCommit,
        string? partitionKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeKey);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(readAndCommit);

        var dataKey = channel.ResolveDataKey(partitionKey);
        var key = scopeKey + "\u001f" + dataKey;
        object gate;
        lock (_syncRoot)
        {
            if (!_gates.TryGetValue(key, out gate!))
            {
                gate = new object();
                _gates.Add(key, gate);
            }
        }

        lock (gate)
        {
            return readAndCommit();
        }
    }
}

internal sealed class DmaSnapshotChannelRegistry
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, IDmaSnapshotChannel> _channels = new(StringComparer.Ordinal);
    private bool _sealed;

    public DmaSnapshotChannel<T> Register<T>(
        string name,
        bool partitioned = false,
        DmaSnapshotReadPolicy readPolicy = DmaSnapshotReadPolicy.Stable,
        DmaSnapshotMergePolicy mergePolicy = DmaSnapshotMergePolicy.Replace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.IndexOfAny(['\u001d', '\u001e', '\u001f']) >= 0)
        {
            throw new ArgumentException("DMA snapshot channel name contains a reserved separator.", nameof(name));
        }

        lock (_syncRoot)
        {
            if (_sealed)
            {
                throw new InvalidOperationException("DMA snapshot channel registry is sealed.");
            }

            if (_channels.ContainsKey(name))
            {
                throw new InvalidOperationException("DMA snapshot channel is already registered: " + name);
            }

            var channel = new DmaSnapshotChannel<T>(
                this,
                name,
                partitioned,
                readPolicy,
                mergePolicy);
            _channels.Add(name, channel);
            return channel;
        }
    }

    public void Seal()
    {
        lock (_syncRoot)
        {
            _sealed = true;
        }
    }

    public bool IsSealed
    {
        get
        {
            lock (_syncRoot)
            {
                return _sealed;
            }
        }
    }

    public IReadOnlyList<DmaSnapshotChannelMetadata> Registrations
    {
        get
        {
            lock (_syncRoot)
            {
                return _channels.Values
                    .Select(static channel => new DmaSnapshotChannelMetadata(
                        channel.Name,
                        channel.ValueType,
                        channel.Partitioned,
                        channel.ReadPolicy,
                        channel.MergePolicy))
                    .OrderBy(static channel => channel.Name, StringComparer.Ordinal)
                    .ToArray();
            }
        }
    }

    internal string ResolveDataKey<T>(DmaSnapshotChannel<T> channel, string? partitionKey)
    {
        ArgumentNullException.ThrowIfNull(channel);
        lock (_syncRoot)
        {
            if (!_sealed)
            {
                throw new InvalidOperationException(
                    "DMA snapshot channel registry must be sealed before pipeline use.");
            }

            if (!_channels.TryGetValue(channel.Name, out var registered) ||
                !ReferenceEquals(registered, channel))
            {
                throw new InvalidOperationException(
                    "DMA snapshot channel is not registered in this pipeline: " + channel.Name);
            }
        }

        if (!channel.Partitioned)
        {
            if (partitionKey is not null)
            {
                throw new InvalidOperationException(
                    "DMA snapshot channel does not accept a partition: " + channel.Name);
            }

            return channel.Name;
        }

        if (string.IsNullOrWhiteSpace(partitionKey))
        {
            throw new InvalidOperationException(
                "DMA snapshot channel requires a non-empty partition: " + channel.Name);
        }

        return channel.Name + "\u001d" + partitionKey.Length + ":" + partitionKey;
    }
}

/// <summary>
/// Complete production catalog.  Add new business-facing VMM snapshots here
/// before wiring a reader; the registry is sealed after static initialization.
/// </summary>
internal static class AionVmmSnapshotChannels
{
    private static readonly DmaSnapshotChannelRegistry ChannelRegistry = new();

    public static readonly DmaSnapshotChannel<PlayerSnapshot> Player =
        ChannelRegistry.Register<PlayerSnapshot>("player");

    public static readonly DmaSnapshotChannel<PlayerAbnormalStatusSnapshot> PlayerAbnormalStatuses =
        ChannelRegistry.Register<PlayerAbnormalStatusSnapshot>("player_abnormal_statuses");

    public static readonly DmaSnapshotChannel<SummonedPetSnapshot> SummonedPet =
        ChannelRegistry.Register<SummonedPetSnapshot>("summoned_pet");

    public static readonly DmaSnapshotChannel<SummonedPetRosterSnapshot> SummonedPetRoster =
        ChannelRegistry.Register<SummonedPetRosterSnapshot>(
            "summoned_pet_roster",
            mergePolicy: DmaSnapshotMergePolicy.FieldAware);

    public static readonly DmaSnapshotChannel<PartySnapshot> Party =
        ChannelRegistry.Register<PartySnapshot>("party");

    public static readonly DmaSnapshotChannel<TacticsSignSnapshot> TacticsSigns =
        ChannelRegistry.Register<TacticsSignSnapshot>("tactics_signs");

    public static readonly DmaSnapshotChannel<ChannelSnapshot> Channel =
        ChannelRegistry.Register<ChannelSnapshot>("channel");

    public static readonly DmaSnapshotChannel<LockedTargetSnapshot> LockedTarget =
        ChannelRegistry.Register<LockedTargetSnapshot>("locked_target");

    public static readonly DmaSnapshotChannel<LockedTargetAbnormalStatusSnapshot> LockedTargetAbnormalStatuses =
        ChannelRegistry.Register<LockedTargetAbnormalStatusSnapshot>("locked_target_abnormal_statuses");

    public static readonly DmaSnapshotChannel<IReadOnlyList<SkillSnapshot>> Skills =
        ChannelRegistry.Register<IReadOnlyList<SkillSnapshot>>("skills", partitioned: true);

    public static readonly DmaSnapshotChannel<IReadOnlyList<InventoryItemSnapshot>> Inventory =
        ChannelRegistry.Register<IReadOnlyList<InventoryItemSnapshot>>(
            "inventory",
            mergePolicy: DmaSnapshotMergePolicy.FieldAware);

    public static readonly DmaSnapshotChannel<ulong> InventoryMoney =
        ChannelRegistry.Register<ulong>("inventory_money");

    public static readonly DmaSnapshotChannel<int> InventoryCapacity =
        ChannelRegistry.Register<int>("inventory_capacity");

    public static readonly DmaSnapshotChannel<IReadOnlyList<WorldObjectSnapshot>> WorldObjects =
        ChannelRegistry.Register<IReadOnlyList<WorldObjectSnapshot>>(
            "world_objects",
            mergePolicy: DmaSnapshotMergePolicy.FieldAware);

    public static readonly DmaSnapshotChannel<GatherSnapshot> Gather =
        ChannelRegistry.Register<GatherSnapshot>("gather");

    public static readonly DmaSnapshotChannel<IReadOnlyList<LootCorpseSnapshot>> LootCorpses =
        ChannelRegistry.Register<IReadOnlyList<LootCorpseSnapshot>>("loot_corpses");

    public static readonly DmaSnapshotChannel<InventoryWindowSnapshot> InventoryWindow =
        ChannelRegistry.Register<InventoryWindowSnapshot>(
            "inventory_window",
            partitioned: true,
            readPolicy: DmaSnapshotReadPolicy.RequireFresh);

    public static readonly DmaSnapshotChannel<InventoryDiscardConfirmSnapshot> InventoryDiscardConfirm =
        ChannelRegistry.Register<InventoryDiscardConfirmSnapshot>(
            "inventory_discard_confirm",
            readPolicy: DmaSnapshotReadPolicy.RequireFresh);

    static AionVmmSnapshotChannels()
    {
        ChannelRegistry.Seal();
    }

    public static DmaSnapshotChannelRegistry Registry => ChannelRegistry;

    public static IReadOnlyList<DmaSnapshotChannelMetadata> All => ChannelRegistry.Registrations;
}
