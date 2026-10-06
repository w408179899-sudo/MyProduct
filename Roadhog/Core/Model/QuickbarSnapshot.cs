namespace Roadhog.Core.Model;

public enum SkillQuickbar { Main, Alt }

public sealed record QuickbarSlotSnapshot(SkillQuickbar Bar, int Slot, uint ContentType, uint SkillId,
    uint ItemTemplateId = 0, uint ItemInstanceId = 0);

/// <summary>Current-page bindings for the two supported skill bars. Slot and page indices are zero-based.</summary>
public sealed record QuickbarSnapshot(int Page, IReadOnlyList<QuickbarSlotSnapshot> Slots);
