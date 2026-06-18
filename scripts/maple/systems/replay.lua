local Replay = {}

function Replay.from_snapshot(snapshot)
    return { tick = snapshot.tick, active_goal = snapshot.active_goal, failure_count = snapshot.failure_count }
end

return Replay
