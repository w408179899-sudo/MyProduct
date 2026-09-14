namespace Roadhog.Core.Model;

public sealed record ChannelUiPoint(int X, int Y);
public sealed record ChannelUiOption(int Number, bool Enabled, ChannelUiPoint? Point);

/// <summary>One coherent UI tree publication. Null controls are absent/hidden controls.</summary>
public sealed record ChannelSwitchUiSnapshot(
    int Width, int Height, ChannelUiPoint Cursor,
    ChannelUiPoint? MenuButton, ChannelUiPoint? ServiceItem,
    ChannelUiPoint? SwitchChannelItem, bool DialogOpen, bool DropdownOpen,
    int SelectedChannelNumber, ChannelUiPoint? DropdownButton,
    ChannelUiPoint? MoveButton, IReadOnlyList<ChannelUiOption> Options,
    DateTimeOffset CapturedAt);
