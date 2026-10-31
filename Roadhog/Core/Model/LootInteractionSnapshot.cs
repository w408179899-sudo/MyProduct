namespace Roadhog.Core.Model;

/// <summary>The current server-accepted loot interaction and its owned UI.</summary>
public sealed record LootInteractionSnapshot(uint CorpseServerObjectId, bool WindowOpen, bool ConfirmationOpen)
{
    public static LootInteractionSnapshot Closed { get; } = new(0, false, false);
    public bool HasOpenUi => WindowOpen || ConfirmationOpen;
}
