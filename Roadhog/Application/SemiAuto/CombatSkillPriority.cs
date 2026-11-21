using SkillScheduling;

namespace Roadhog.Application.SemiAuto;

internal enum CombatSkillPriority
{
    HealthRecovery,
    AlwaysMaintenance,
    Opening,
    PetAction,
    CombatMaintenance,
    OpeningRetry
}

public sealed partial class SemiAutoCombatController
{
    private static SkillCandidate<CombatSkillPriority> Candidate(CombatSkillPriority priority,
        Func<Task<bool>> execute, bool eligible = true) => new(priority, (int)priority, eligible, _ => execute());
}
