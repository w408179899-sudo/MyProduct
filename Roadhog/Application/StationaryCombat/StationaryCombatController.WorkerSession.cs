using Roadhog.Application.Workers;

namespace Roadhog.Application.StationaryCombat;

public sealed partial class StationaryCombatController
{
    public void StopWorkerBackgroundWork(AccountWorkerContext context, StationaryCombatState state)
    {
        // A recovered worker must not leave the abandoned controller's poller or camera task alive.
        try { StopNextTargetPreAim(context, state, "worker_exit", clearCandidate: true); }
        finally { StopPathFollowPoller(state); }
    }
}
