namespace Roadhog.Core.Model;

public enum PartyMemberVisibilityState
{
    Unknown = 0,
    NotLoaded = 1,
    LoadedPositionUnknown = 2,
    LoadedDistanceUnknown = 3,
    LoadedOutOfRange = 4,
    ScreenVisible = 5
}
