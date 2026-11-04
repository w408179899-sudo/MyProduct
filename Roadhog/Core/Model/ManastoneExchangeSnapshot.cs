namespace Roadhog.Core.Model;

public sealed record ManastoneDialogEntry(string Text, string Href, GameUiPoint? Point);

public sealed record ManastoneExchangeSnapshot(
    bool IsOpen,
    uint NpcServerObjectId,
    uint QuestId,
    int PageType,
    int PageId,
    string PageName,
    string Html,
    IReadOnlyList<ManastoneDialogEntry> Entries,
    GameUiPoint? ConfirmButton,
    GameUiPoint? CloseButton,
    bool OtherModalOpen);
