using Roadhog.Application.GameSessions;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.GameSessions;

// Geometry stays inside the input adapter; the application only receives published facts.
internal sealed record GameExitUi(int Width, int Height, bool MenuOpen, bool CountdownOpen,
    bool OtherModalOpen, ulong MenuAddress, ulong CountdownAddress, GameUiPoint? Quit,
    GameUiPoint? Cancel, string MenuName = "", string CountdownName = "");

internal sealed record GameExitObservation(GameExitSnapshot Snapshot, uint SessionPid,
    string Window, Rectangle Bounds, GameExitUi Ui);
