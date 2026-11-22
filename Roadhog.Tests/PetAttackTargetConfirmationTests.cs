using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;
using static SharedSkillAuditFixture;

internal static class PetAttackTargetConfirmationTests
{
    private const string PetAttackKey = "D7";

    public static async Task RejectsUnidentifiedFirstLockAsync()
    {
        using var f = await CreateAsync();
        var target = await PrepareFullLockAsync(f);
        var confirmationReads = 0;
        f.Api.LockedCombatTargetPublicationRead = _ => OperationResult<PublishedGameSnapshot<LockedCombatTargetSnapshot>>.Ok(
            ++confirmationReads == 1 ? new(10, target) : new(11, target with { ServerObjectId = 0 }));

        await f.TickAsync();

        Sequence([], f.Keyboard.Keys, "the retained lock and a matching entity without an object id cannot authorize the first input");
        Check(confirmationReads == 2 && HasEvent(f, "semi_auto.combat_target.lock_mismatch", "pet_attack_first"),
            "the first input waits for a different publication and rejects its missing identity");
    }

    public static async Task HandsOffWithoutWaitingForAnotherLockVersionAsync()
    {
        using var f = await CreateAsync();
        var target = await PrepareFullLockAsync(f);
        var verifiedReads = 0;
        f.Api.LockedCombatTargetPublicationRead = _ =>
        {
            verifiedReads++;
            return OperationResult<PublishedGameSnapshot<LockedCombatTargetSnapshot>>.Ok(new(11, target));
        };

        await f.TickAsync();

        Sequence([PetAttackKey, "D1"], f.Keyboard.Keys, "the ready ordinary skill follows the single pet command in the same tick");
        Check(verifiedReads == 1,
            "the handoff must not wait for another new lock publication after the pet command");
    }

    public static async Task RejectsDifferentOrDeadFirstLockAsync(bool sameIdentity)
    {
        using var f = await CreateAsync();
        var target = await PrepareFullLockAsync(f);
        var observed = sameIdentity ? target with { IsMonsterAlive = false } : target with { ServerObjectId = target.ServerObjectId + 1 };
        f.Api.LockedCombatTargetPublicationRead = _ => OperationResult<PublishedGameSnapshot<LockedCombatTargetSnapshot>>.Ok(new(11, observed));

        await f.TickAsync();

        Sequence([], f.Keyboard.Keys, "a dead target or a different server id must block both inputs");
        Check(HasEvent(f, "semi_auto.combat_target.lock_mismatch", "pet_attack_first"),
            "the lightweight confirmation rejects the invalid target before the first press");
    }

    public static async Task RetriesFailedConfirmationBeforeSendingAsync()
    {
        using var f = await CreateAsync();
        var target = await PrepareFullLockAsync(f);
        var reads = 0;
        f.Api.LockedCombatTargetPublicationRead = _ =>
        {
            Sequence([], f.Keyboard.Keys, "no key can run before confirmation is published");
            return ++reads == 1
                ? OperationResult<PublishedGameSnapshot<LockedCombatTargetSnapshot>>.Fail("incomplete health")
                : OperationResult<PublishedGameSnapshot<LockedCombatTargetSnapshot>>.Ok(new(11, target));
        };

        await f.TickAsync();

        Check(reads == 2, "provider retries an incomplete capture within the action window");
        Sequence([PetAttackKey, "D1"], f.Keyboard.Keys, "input proceeds after the trusted capture");
    }

    public static async Task StopsDuringConfirmationAsync()
    {
        using var f = await CreateAsync();
        await PrepareFullLockAsync(f);
        f.Api.LockedCombatTargetPublicationRead = stop =>
        {
            f.Cancel();
            stop.ThrowIfCancellationRequested();
            throw new InvalidOperationException("The confirmation must observe the requested stop.");
        };

        try
        {
            await f.TickAsync();
            throw new InvalidOperationException("The combat tick must observe the requested stop.");
        }
        catch (OperationCanceledException)
        {
            Sequence([], f.Keyboard.Keys, "stopping during confirmation prevents all input");
        }
    }

    public static async Task RejectsDifferentPostAttackLockAsync()
    {
        using var f = await CreateAsync();
        f.Keyboard.AfterPress = key =>
        {
            if (key == PetAttackKey) f.Api.TargetOwnServerObjectId++;
        };

        await f.TickAsync();

        Sequence([PetAttackKey], f.Keyboard.Keys, "a changed object id blocks the follow-up skill even when the entity id is unchanged");
        Check(HasEvent(f, "semi_auto.combat_target.lock_mismatch", "after_pet_attack"),
            "the changed identity is caught at the handoff to skill selection");
    }

    public static async Task YieldsAfterFailedPetInputAsync()
    {
        using var f = await CreateAsync();
        f.Keyboard.PressResult = key => key == PetAttackKey ? OperationResult.Fail("pet input failed") : OperationResult.Ok();

        await f.TickAsync();
        Sequence([PetAttackKey], f.Keyboard.Keys, "a failed pet input ends this tick before other keys");

        await f.TickAsync();
        Sequence([PetAttackKey, "D1"], f.Keyboard.Keys, "the next tick can use the ready skill without repeating the pet input");
    }

    public static async Task TimesOutOnRetainedLockAsync()
    {
        using var f = await CreateAsync();
        var target = await PrepareFullLockAsync(f);
        f.Api.LockedCombatTargetPublicationRead = _ => OperationResult<PublishedGameSnapshot<LockedCombatTargetSnapshot>>.Ok(new(10, target));

        await f.TickAsync();

        Sequence([], f.Keyboard.Keys, "no fresh confirmation leaves the pet command unsent");
        Check(HasEvent(f, "semi_auto.spiritmaster.opening_attack_key.confirmation_timeout", "pet_attack_first"),
            "a permanently retained publication ends the action's confirmation window");

        f.Api.LockedCombatTargetPublicationRead = _ => OperationResult<PublishedGameSnapshot<LockedCombatTargetSnapshot>>.Ok(new(11, target));
        await f.TickAsync();
        Sequence([PetAttackKey, "D1"], f.Keyboard.Keys, "a new confirmation can open combat on the next tick");
    }

    public static Task CapturesOnlyTargetIdentityAndHealthAsync()
    {
        var memory = new CombatTargetMemory();

        var live = memory.Capture();
        Check(live.Success && live.Value == new LockedCombatTargetSnapshot(61, 800, true),
            "a validated selected monster with positive HP is publishable");
        Check(memory.UnmappedReads == 0 && memory.Batches == 2,
            "one batched actor lookup and one verification need no name, position, or interaction reads");

        memory.SetCurrentHp(0);
        var dead = memory.Capture();
        Check(dead.Success && dead.Value == new LockedCombatTargetSnapshot(61, 800, false),
            "zero current HP with valid maximum HP publishes a dead target");
        return Task.CompletedTask;
    }

    public static Task RejectsInconsistentCaptureAsync()
    {
        var partial = new CombatTargetMemory { MissingHealth = true };
        Check(!partial.Capture().Success, "incomplete HP cannot publish an alive target");
        var changedSelection = new CombatTargetMemory { ChangesSelection = true };
        Check(!changedSelection.Capture().Success, "a selection change during capture requires another observation");
        var changedEntity = new CombatTargetMemory { RecyclesEntity = true };
        Check(!changedEntity.Capture().Success, "a recycled entity binding invalidates the captured actor");
        var changedActor = new CombatTargetMemory { ChangesActorIdentity = true };
        Check(!changedActor.Capture().Success, "an actor identity change invalidates the captured health");
        var ambiguousActor = new CombatTargetMemory();
        ambiguousActor.AddAmbiguousActor();
        Check(!ambiguousActor.Capture().Success, "multiple eligible actors cannot authorize input by name or order");
        return Task.CompletedTask;
    }

    private static async Task<SharedSkillAuditFixture> CreateAsync()
    {
        var fixture = new SharedSkillAuditFixture();
        fixture.Settings.Skills.Spiritmaster.OpeningAttackKey = PetAttackKey;
        await fixture.PrepareAsync();
        return fixture;
    }

    private static bool HasEvent(SharedSkillAuditFixture f, string name, string phase) => f.Logger.Entries.Any(entry =>
        entry.EventName == name && entry.Fields.TryGetValue("phase", out var value) && Equals(value, phase));

    private static async Task<LockedCombatTargetSnapshot> PrepareFullLockAsync(SharedSkillAuditFixture fixture)
    {
        var target = (await fixture.Api.ReadLockedTargetAsync()).Value!;
        fixture.Api.LockedTargetPublicationRead = afterVersion =>
        {
            Check(afterVersion == 0, "pet attack must not request the full target snapshot for confirmation");
            return new(10, target);
        };
        return LockedCombatTargetSnapshot.FromLockedTarget(target);
    }

    private sealed class CombatTargetMemory
    {
        private const ulong Module = 0x1000000;
        private const ulong Selection = Module + 0xD6CB08 + sizeof(ushort);
        private const ulong Entity = 0x2000000;
        private const ulong Actor = 0x3000000;
        private const ulong Health = Actor + 0x11A0;
        private readonly byte[] entityBlock = new byte[0x800];
        private readonly Dictionary<ulong, byte[]> memory;
        private int selectionReads;
        private int lookups;

        public bool MissingHealth { get; init; }
        public bool ChangesSelection { get; init; }
        public bool RecyclesEntity { get; init; }
        public bool ChangesActorIdentity { get; init; }
        public int UnmappedReads { get; private set; }
        public int Batches { get; private set; }

        public CombatTargetMemory()
        {
            BitConverter.GetBytes(Actor).CopyTo(entityBlock, sizeof(ulong));
            memory = new()
            {
                [Entity] = entityBlock,
                [Actor + 8] = ActorIdentity(800),
                [Health] = new byte[2 * sizeof(uint)]
            };
            BitConverter.GetBytes(100U).CopyTo(memory[Health], 0);
            SetCurrentHp(50);
        }

        public void SetCurrentHp(uint hp) => BitConverter.GetBytes(hp).CopyTo(memory[Health], sizeof(uint));

        public void AddAmbiguousActor()
        {
            const ulong other = Actor + 0x10000;
            BitConverter.GetBytes(other).CopyTo(entityBlock, 2 * sizeof(ulong));
            memory[other + 8] = ActorIdentity(801);
        }

        public OperationResult<LockedCombatTargetSnapshot> Capture()
        {
            selectionReads = lookups = 0;
            return AionVmmGameApi.CaptureLockedCombatTarget(Module, Read, Batch,
                id => id == 61 && (++lookups == 1 || !RecyclesEntity) ? Entity : 0);
        }

        private static byte[] ActorIdentity(uint serverId)
        {
            var identity = new byte[40];
            BitConverter.GetBytes(Entity).CopyTo(identity, 0);
            BitConverter.GetBytes(LockedTargetSnapshot.MonsterObjectType).CopyTo(identity, 24);
            BitConverter.GetBytes(serverId).CopyTo(identity, 36);
            return identity;
        }

        private byte[] Read(ulong address, int size)
        {
            if (address == Selection) return BitConverter.GetBytes((ushort)(++selectionReads == 1 || !ChangesSelection ? 61 : 62));
            if (address == Health && MissingHealth) return Array.Empty<byte>();
            foreach (var (start, bytes) in memory)
            {
                if (address >= start && address - start + (ulong)size <= (ulong)bytes.Length)
                    return bytes.AsSpan((int)(address - start), size).ToArray();
            }
            UnmappedReads++;
            return Array.Empty<byte>();
        }

        private byte[][] Batch(IReadOnlyList<(ulong Address, int Size)> requests)
        {
            Batches++;
            if (ChangesActorIdentity && Batches == 2)
                memory[Actor + 8] = ActorIdentity(801);
            return requests.Select(request => Read(request.Address, request.Size)).ToArray();
        }
    }
}

sealed partial class FakeGameApi : ILockedTargetPublicationSource
{
    public Func<long, PublishedGameSnapshot<LockedTargetSnapshot>>? LockedTargetPublicationRead { get; set; }
    public Func<CancellationToken, OperationResult<PublishedGameSnapshot<LockedCombatTargetSnapshot>>>? LockedCombatTargetPublicationRead { get; set; }
    private long _lockedTargetPublicationVersion;

    async Task<OperationResult<PublishedGameSnapshot<LockedTargetSnapshot>>> ILockedTargetPublicationSource.ReadLockedTargetPublicationAsync(
        GameApiReadContext context, long afterVersion, CancellationToken cancellationToken)
    {
        if (LockedTargetPublicationRead is { } read)
            return OperationResult<PublishedGameSnapshot<LockedTargetSnapshot>>.Ok(read(afterVersion));

        var observed = await ReadLockedTargetAsync(context, cancellationToken).ConfigureAwait(false);
        return observed is { Success: true, Value: { } target }
            ? OperationResult<PublishedGameSnapshot<LockedTargetSnapshot>>.Ok(new(Interlocked.Increment(ref _lockedTargetPublicationVersion), target))
            : OperationResult<PublishedGameSnapshot<LockedTargetSnapshot>>.Fail(observed.Error ?? "Fake locked target read failed.");
    }

    async Task<OperationResult<PublishedGameSnapshot<LockedCombatTargetSnapshot>>> ILockedTargetPublicationSource.ReadLockedCombatTargetPublicationAsync(
        GameApiReadContext context, CancellationToken cancellationToken)
    {
        if (LockedCombatTargetPublicationRead is { } read)
            return read(cancellationToken);

        var observed = await ReadLockedTargetAsync(context, cancellationToken).ConfigureAwait(false);
        return observed is { Success: true, Value: { } target }
            ? OperationResult<PublishedGameSnapshot<LockedCombatTargetSnapshot>>.Ok(
                new(Interlocked.Increment(ref _lockedTargetPublicationVersion), LockedCombatTargetSnapshot.FromLockedTarget(target)))
            : OperationResult<PublishedGameSnapshot<LockedCombatTargetSnapshot>>.Fail(observed.Error ?? "Fake locked combat target read failed.");
    }
}
