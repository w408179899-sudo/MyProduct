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
        var target = (await f.Api.ReadLockedTargetAsync()).Value!;
        var confirmationReads = 0;
        f.Api.LockedTargetPublicationRead = afterVersion => afterVersion == 0 || ++confirmationReads == 1
            ? new(10, target)
            : new(11, target with { ServerObjectId = 0 });

        await f.TickAsync();

        Sequence([], f.Keyboard.Keys, "the retained lock and a matching entity without an object id cannot authorize the first input");
        Check(confirmationReads == 2 && HasEvent(f, "semi_auto.combat_target.lock_mismatch", "pet_attack_first"),
            "the first input waits for a different publication and rejects its missing identity");
    }

    public static async Task RejectsDifferentRepeatLockAsync()
    {
        using var f = await CreateAsync();
        var target = (await f.Api.ReadLockedTargetAsync()).Value!;
        var repeatReads = 0;
        f.Api.LockedTargetPublicationRead = afterVersion => afterVersion switch
        {
            0 => new(10, target),
            10 => new(11, target),
            _ when ++repeatReads == 1 => new(11, target),
            _ => new(12, target with { ServerObjectId = target.ServerObjectId + 1 })
        };

        await f.TickAsync();

        Sequence([PetAttackKey], f.Keyboard.Keys, "only the first input has a confirmed object identity");
        Check(repeatReads == 2 && HasEvent(f, "semi_auto.combat_target.lock_mismatch", "pet_attack_repeat"),
            "the repeat waits for a different publication and rejects its changed identity");
    }

    public static async Task TimesOutOnRetainedLockAsync()
    {
        using var f = await CreateAsync();
        var target = (await f.Api.ReadLockedTargetAsync()).Value!;
        f.Api.LockedTargetPublicationRead = _ => new(10, target);

        await f.TickAsync();

        Sequence([], f.Keyboard.Keys, "no fresh confirmation leaves both pet inputs unsent");
        Check(HasEvent(f, "semi_auto.spiritmaster.opening_attack_key.confirmation_timeout", "pet_attack_first"),
            "a permanently retained publication ends the action's confirmation window");
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
}

sealed partial class FakeGameApi : ILockedTargetPublicationSource
{
    public Func<long, PublishedGameSnapshot<LockedTargetSnapshot>>? LockedTargetPublicationRead { get; set; }
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
}
