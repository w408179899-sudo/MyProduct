namespace Roadhog.Core.Model;

/// <summary>
/// One skill slot whose binding and current usability were validated by the
/// provider. Raw rendering fields and read-quality information stay below this
/// contract. Slot numbers use the same zero-based convention as QuickbarSnapshot.
/// </summary>
public sealed record SkillAvailabilitySlotSnapshot(
    SkillQuickbar Bar,
    int Slot,
    uint ContentType,
    uint BaseSkillId,
    uint EffectiveSkillId,
    bool CanUse);

/// <summary>
/// One actual slot binding, including ordinary skills whose icon-opportunity
/// signal is unsupported. IDs retain the exact bound and displayed skill ranks.
/// </summary>
public sealed record SkillAvailabilityBindingSnapshot(
    SkillQuickbar Bar,
    int Slot,
    uint ContentType,
    uint BaseSkillId,
    uint EffectiveSkillId);

/// <summary>Trusted local life and selected-target identity captured with the bar.</summary>
public sealed record SkillAvailabilityCombatSnapshot(
    ushort PlayerEntityId,
    uint PlayerServerObjectId,
    ushort TargetEntityId,
    uint TargetServerObjectId,
    uint CurrentHp,
    uint MaxHp,
    uint CurrentMp = 0,
    uint MaxMp = 0,
    ushort CurrentDp = 0)
{
    public bool IsAlive => MaxHp > 0 && CurrentHp > 0;
    public bool IsDead => MaxHp > 0 && CurrentHp == 0;
    public double HpPercent => MaxHp == 0 ? 100.0 : Math.Clamp(CurrentHp * 100.0 / MaxHp, 0.0, 100.0);
    public double MpPercent => MaxMp == 0 ? 100.0 : Math.Clamp(CurrentMp * 100.0 / MaxMp, 0.0, 100.0);
}

/// <summary>
/// Official skill-bar availability for one page and character session. The
/// release fields identify the client's last actual skill release; they do not
/// identify the most recent key sent by the application.
/// </summary>
public sealed record SkillAvailabilitySnapshot(
    int Page,
    IReadOnlyList<SkillAvailabilitySlotSnapshot> Slots,
    uint LastReleasedSkillId = 0,
    uint LastReleasedSkillTime = 0,
    IReadOnlyList<uint>? UnsupportedSkillIds = null,
    string? BindingSignature = null,
    IReadOnlyList<SkillAvailabilityBindingSnapshot>? BindingSlots = null,
    SkillAvailabilityCombatSnapshot? CombatState = null);
