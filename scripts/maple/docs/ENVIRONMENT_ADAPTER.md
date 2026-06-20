# Environment Adapter

Real MapleStory APIs are infrastructure. They are only adapted under:

```text
scripts/maple/environment/
```

Business modules consume Blackboard snapshots and ActionSpec actions. They must not call `data.lua` directly.

## API Buckets

Snapshot APIs:

- `player_info`
- `list_nearby`
- `list_inventory`
- `list_skills`
- `list_quickslot`

Action APIs:

- `do_attack`
- `quickslot_use`
- `walk`
- `pick_all`
- `use_item`
- `equip_item`

Special or diagnostic APIs stay out of ordinary flow until a specific owner is added:

- `call`
- `exec_raw`
- `float`
- `probe_systems`

## Adapter Files

- `maple_api.lua`: safe API calls, timing, failure capture, and Blackboard diagnostics.
- `normalizers.lua`: pure conversion from raw API tables into internal snapshots.
- `maple_environment.lua`: Environment implementation used by Perception and Executor.
- `mock_environment.lua`: mock equivalent for tests and local framework development.
- `probes/api_probe.lua`: real-client validation helpers built on top of the Environment adapter.

## Snapshot Contracts

`ActorSnapshot` fields:

- `level`, `hp`, `max_hp`, `mp`, `max_mp`
- `position = { x, y, z }`
- `current_map`, `map_id`, `map_name`
- `invincible`, `movement.walk_speed`, `movement.gravity`
- `source`

`WorldSnapshot` fields:

- `nearby_targets`
- `nearby_resources`
- `nearby_npcs`
- `nearby_portals`
- `counts`
- `source`

`MonsterSnapshot` fields:

- `id`, `type_id`, `name`, `level`
- `x`, `y`, `z`, `position`
- `vx`, `vy`, `vz`, `has_velocity`
- `hp`, `max_hp`

The first adapter pass does not have real monster instance IDs or velocity. It creates synthetic IDs and sets velocity to zero. Future perception work can replace this with tracked IDs and velocity derived from consecutive snapshots.

`DropSnapshot` fields:

- `id`, `item_id`, `name`
- `owner_cid`, `free`, `can_pick`
- `x`, `y`, `z`, `position`

`SkillSnapshot` fields:

- `point`, `used`
- `learned`
- `available`
- `quickslots`
- `source`, `quickslot_source`

## Diagnostics

Every real API call records:

- `api_name`
- `ok`
- `reason` or `error` when failed
- `elapsed_ms`
- `result_count`
- `account_index`

The latest call is exposed on `bb.debug.last_api_call`. Counters live under:

- `bb.metrics.api_call_count`
- `bb.metrics.api_error_count`
- `bb.metrics.latest_api_latency_ms`

## Real Environment Switch

`Bootstrap.new` stays mock-first. Real API integration is opt-in:

```lua
Bootstrap.new({
    environment_name = "maple",
    data_module = data
})
```

Worker integration should only enable this after account startup and bind flow are explicitly decided.

## Probe Scripts

Probe entrypoints:

```text
scripts/maple_probe_readonly.lua
scripts/maple_probe_actions.lua
```

Use readonly probe before business flow development against a live client. Use action probe only when issuing attack, quickslot, movement, stop, and pickup commands is acceptable.
