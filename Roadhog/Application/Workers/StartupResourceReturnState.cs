using Roadhog.Core.Model;
using Roadhog.Application.Travel;

namespace Roadhog.Application.Workers;

// Owned by the worker context so recovering the loop does not repeat the login decision.
internal sealed class StartupResourceReturnState
{
    public PlayerSnapshot? Player { get; private set; }
    public bool Handled { get; set; }
    public TownReturnTransition? Transition { get; set; }

    public bool RequiresReturn => Player is { IsAlive: true, MaxMp: > 0 } player &&
        (ulong)player.CurrentHp * 100 < (ulong)player.MaxHp * 30 &&
        (ulong)player.CurrentMp * 100 < (ulong)player.MaxMp * 30;

    public void Observe(PlayerSnapshot player) => Player ??= player;
}
