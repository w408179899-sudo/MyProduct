namespace Roadhog.Core.Accounts;

public enum CombatFlowVersion
{
    Legacy = 0,
    CoordinatedV2 = 1
}

public sealed class CombatFlowPolicy
{
    public bool ForceLegacy { get; set; }

    public CombatFlowVersion Resolve(CombatFlowVersion requested) =>
        !ForceLegacy && requested == CombatFlowVersion.CoordinatedV2
            ? CombatFlowVersion.CoordinatedV2
            : CombatFlowVersion.Legacy;
}
