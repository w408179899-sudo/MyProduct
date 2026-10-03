using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal enum SkillOpportunityReadCompleteness { Complete, Partial, Failed }

internal sealed record SkillOpportunitySlotObservation(
    SkillQuickbar Bar, int Slot, uint ContentType, uint BaseSkillId,
    uint EffectiveSkillId, bool Supported, bool? CanUse);

internal sealed record SkillOpportunityRead(
    SkillOpportunityReadCompleteness Completeness,
    int Page,
    string BindingSignature,
    uint ActorServerObjectId,
    IReadOnlyList<SkillOpportunitySlotObservation> Slots,
    uint? LastReleasedSkillId,
    uint? LastReleasedSkillTime,
    string? Error = null,
    string? LayoutIdentity = null,
    bool PresentationInactive = false,
    bool ScopeChanged = false,
    ushort PlayerEntityId = 0,
    ushort? TargetEntityId = null,
    uint? TargetServerObjectId = null,
    uint? CurrentHp = null,
    uint? MaxHp = null,
    uint? CurrentMp = null,
    uint? MaxMp = null,
    ushort? CurrentDp = null)
{
    public static SkillOpportunityRead Failed(string error, bool scopeChanged = false) => new(
        SkillOpportunityReadCompleteness.Failed, 0, string.Empty, 0,
        Array.Empty<SkillOpportunitySlotObservation>(), null, null, error, ScopeChanged: scopeChanged);
}

// This is the provider's canonical official value. Keeping all slots, including
// unsupported ones, lets a Partial capture retain one coupled identity/usability
// observation without inventing a false or upgrading a skill rank. Only the
// explicit supported projection crosses the business interface.
internal sealed record SkillAvailabilityPublication(
    int Page, string BindingSignature, uint ActorServerObjectId,
    IReadOnlyList<SkillOpportunitySlotObservation> Slots,
    uint LastReleasedSkillId, uint LastReleasedSkillTime,
    SkillAvailabilityCombatSnapshot? CombatState = null)
{
    public SkillAvailabilitySnapshot Snapshot { get; } = new(
        Page,
        Slots.Where(s => s.ContentType == 21 && s.Supported)
            .Select(s => new SkillAvailabilitySlotSnapshot(s.Bar, s.Slot, s.ContentType,
                s.BaseSkillId, s.EffectiveSkillId, s.CanUse!.Value)).ToArray(),
        LastReleasedSkillId,
        LastReleasedSkillTime,
        Slots.Where(s => s.ContentType == 21 && !s.Supported)
            .Select(s => s.EffectiveSkillId).Distinct().Order().ToArray(),
        BindingSignature,
        Slots.Select(s => new SkillAvailabilityBindingSnapshot(s.Bar, s.Slot,
            s.ContentType, s.BaseSkillId, s.EffectiveSkillId)).ToArray(),
        CombatState);

    public SkillAvailabilitySnapshot ToSnapshot() => Snapshot;

    public static SkillAvailabilityPublication? Merge(
        SkillOpportunityRead read, SkillAvailabilityPublication? previous)
    {
        if (read.Completeness == SkillOpportunityReadCompleteness.Failed) return previous;
        if (previous is not null && (previous.Page != read.Page ||
            previous.BindingSignature != read.BindingSignature ||
            previous.ActorServerObjectId != read.ActorServerObjectId ||
            read.PlayerEntityId != 0 && previous.CombatState is { } priorCombat && priorCombat.PlayerEntityId != read.PlayerEntityId))
            previous = null;

        var merged = new List<SkillOpportunitySlotObservation>(read.Slots.Count);
        foreach (var observed in read.Slots)
        {
            if (!observed.Supported || observed.CanUse.HasValue)
            {
                merged.Add(observed);
                continue;
            }

            // Usability and its current effective identity are one coupled
            // observation. Never mix an old usable value with a new skill ID.
            var prior = previous?.Slots.SingleOrDefault(s =>
                s.Bar == observed.Bar && s.Slot == observed.Slot &&
                s.ContentType == observed.ContentType && s.BaseSkillId == observed.BaseSkillId);
            if (prior is null) return null;
            merged.Add(prior);
        }

        var releaseId = read.LastReleasedSkillId ?? previous?.LastReleasedSkillId;
        var releaseTime = read.LastReleasedSkillTime ?? previous?.LastReleasedSkillTime;
        if (!releaseId.HasValue || !releaseTime.HasValue) return null;

        var currentHp = read.CurrentHp ?? previous?.CombatState?.CurrentHp;
        var maxHp = read.MaxHp ?? previous?.CombatState?.MaxHp;
        var targetEntityId = read.TargetEntityId ?? previous?.CombatState?.TargetEntityId;
        var targetServerId = read.TargetServerObjectId ?? previous?.CombatState?.TargetServerObjectId;
        var currentMp = read.CurrentMp ?? previous?.CombatState?.CurrentMp;
        var maxMp = read.MaxMp ?? previous?.CombatState?.MaxMp;
        var currentDp = read.CurrentDp ?? previous?.CombatState?.CurrentDp;
        SkillAvailabilityCombatSnapshot? combat = null;
        if (read.PlayerEntityId != 0)
        {
            if (!currentHp.HasValue || !maxHp.HasValue || !targetEntityId.HasValue || !targetServerId.HasValue ||
                !currentMp.HasValue || !maxMp.HasValue || !currentDp.HasValue) return null;
            combat = new(read.PlayerEntityId, read.ActorServerObjectId, targetEntityId.Value,
                targetServerId.Value, currentHp.Value, maxHp.Value, currentMp.Value, maxMp.Value, currentDp.Value);
        }
        return new(read.Page, read.BindingSignature, read.ActorServerObjectId,
            merged.AsReadOnly(), releaseId.Value, releaseTime.Value, combat);
    }
}
