# Blackboard Schema

Blackboard state is shared state. Do not add temporary locals here.

## Domains

- `meta`: schema version, project, account index/key, worker task id.
- `runtime`: running, paused, tick, started time, last error, stop request.
- `actor`: level, hp/mp, combat state, position, current map.
- `quest`: active/completed quests, current quest id, objective index.
- `inventory`: used/max slots, items, full flag, required item flag.
- `equipment`: current equipment, candidates, upgrade and durability flags.
- `skill`: learned and available skills, learn/trainer flags.
- `navigation`: route, destination, waypoint index, moving/stuck state.
- `world`: nearby NPCs, targets, resources, selected entity.
- `account`: account record loaded from `script_config.json`.
- `task`: active/previous goal, active action, failure counters, last result.
- `action_queue`: reserved shared view of queued actions.
- `safety`: stop reason, last trigger, circuit breaker state.
- `debug`: last node/branch/action.
- `metrics`: tick count, goal/action/safety counters, average tick time.

## Ownership

- Perception updates environment-derived domains.
- Planner updates only `task.previous_goal` and `task.active_goal`.
- Executor updates action lifecycle and action metrics.
- Safety updates only `safety` fields and stop protection flags.
