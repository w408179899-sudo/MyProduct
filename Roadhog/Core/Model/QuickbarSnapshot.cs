namespace Roadhog.Core.Model;

// Existing numeric identities are preserved. Names describe the supported key profile,
// not the historical native dialog class names. Verified on script 1, 2026-10-31.
public enum SkillQuickbar { Main = 0, Alt = 1, Ctrl = 2 }

public sealed record QuickbarSlotSnapshot(SkillQuickbar Bar, int Slot, uint ContentType, uint SkillId,
    uint ItemTemplateId = 0, uint ItemInstanceId = 0);

/// <summary>Current-page bindings for the selected mode's supported bars. Slot and page indices are zero-based.</summary>
public sealed record QuickbarSnapshot(int Page, IReadOnlyList<QuickbarSlotSnapshot> Slots);
