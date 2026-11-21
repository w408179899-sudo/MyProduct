using SkillScheduling;

namespace Roadhog.Application.SemiAuto;

internal enum CombatSkillPriority
{
    PetAttack,
    HealthRecovery,
    AlwaysMaintenance,
    Opening,
    PetAction,
    CombatMaintenance,
    OpeningRetry,
    StatusMaintenance,
    Quickbar
}

internal readonly record struct QuickbarCombatInput(bool IsFirstDecision, bool CanOfferStatus, Func<Task<bool>> VerifyStatusBeforeInput);

internal readonly record struct QuickbarSkillPolicies(
    Func<Task<IReadOnlySet<uint>>>? ReadSuppressedRootSkillIds,
    Func<QuickbarSkillNode, Task>? OnSkillPressed,
    Func<Task<IReadOnlySet<uint>>>? ReadSuppressedSkillIds);

public sealed class QuickbarCombatActions
{
    internal QuickbarCombatActions(Func<Task> observeAsync,
        Func<QuickbarCombatInput, IReadOnlyList<SkillCandidate<CombatSkillPriority>>> nominate,
        Func<Task<QuickbarSkillPolicies>>? prepareQuickbarAsync = null)
    {
        ObserveAsync = observeAsync;
        Nominate = nominate;
        PrepareQuickbarAsync = prepareQuickbarAsync;
    }

    internal Func<Task> ObserveAsync { get; }
    internal Func<QuickbarCombatInput, IReadOnlyList<SkillCandidate<CombatSkillPriority>>> Nominate { get; }
    internal Func<Task<QuickbarSkillPolicies>>? PrepareQuickbarAsync { get; }
}

public sealed partial class SemiAutoCombatController
{
    private static SkillCandidate<CombatSkillPriority> Candidate(CombatSkillPriority priority,
        Func<Task<bool>> execute, bool eligible = true) => new(priority, (int)priority, eligible, _ => execute());
}
