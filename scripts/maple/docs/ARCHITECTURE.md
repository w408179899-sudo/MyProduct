# MapleStory Agent Architecture

The MapleStory project is split into UI, account orchestration, per-account workers, and the atomic agent core.

## Runtime Flow

```text
scripts/maple_control_ui.lua
  -> maple.account.orchestrator
  -> task.run("scripts/maple_account_worker.lua", account opts)
  -> maple.bootstrap
  -> Perception -> Safety -> Planner -> Behavior Tree -> Executor -> Metrics -> Snapshot
```

## Rules

- UI draws state and sends account commands only.
- One account maps to one worker task.
- Worker owns the account loop.
- Agent modules run mock-first.
- Real game APIs belong only in `environment/` adapters.
- Business rules belong in `managers/`.
- Executable operations must be declared in `data/action_specs.lua`.
- Use pragmatic DDD-lite boundaries: domain calculation stays pure, Managers orchestrate, and infrastructure stays in adapters/config/task/UI.
- Combat calculation uses plain `context -> proposal` contracts.

## Important Files

```text
scripts/maple_control_ui.lua
scripts/maple_account_worker.lua
scripts/maple/bootstrap.lua
scripts/maple/blackboard.lua
scripts/maple/account/store.lua
scripts/maple/account/orchestrator.lua
scripts/maple/environment/mock_environment.lua
scripts/maple/environment/maple_environment.lua
scripts/maple/environment/maple_api.lua
scripts/maple/environment/normalizers.lua
scripts/maple/systems/executor.lua
scripts/maple/data/action_specs.lua
scripts/maple/combat/resolver.lua
scripts/maple/combat/immediate_tick.lua
scripts/maple/combat/predictive_tick.lua
scripts/maple/managers/combat_manager.lua
```

## Combat Ports

Maple combat has two reserved proposal ports:

- Immediate tick: `scripts/maple/combat/immediate_tick.lua`
- Predictive tick: `scripts/maple/combat/predictive_tick.lua`

Both ports are adapters around the neutral pure calculation module:

- Core: `scripts/maple/combat/resolver.lua`

`CombatManager` selects which port to use from account/config state, validates the returned proposal, and lets Behavior Tree queue only an `ExecuteCombatDecision` action. Behavior Tree and Managers do not execute combat directly.

The predictive port is reserved for short-horizon simulation, currently 1-3 seconds, where skill windup, monster movement, platform risk, loot timing, and movement cost can be scored before choosing an action.

The resolver accepts plain context data and returns a plain proposal. It must not call Environment, Executor, config, UI, task APIs, sys share APIs, file I/O, or real client APIs.

## Real API Adapter

The real Maple API is opt-in and lives only under `scripts/maple/environment/`.

- `maple_api.lua` wraps documented `data.lua` calls and records diagnostics.
- `normalizers.lua` converts raw API payloads into Blackboard snapshots.
- `maple_environment.lua` exposes the Environment contract to Perception and Executor.

Default Bootstrap runs `mock_environment`. Use `environment_name = "maple"` only when the caller explicitly wants the real adapter.

## Performance And Degradation

Perception owns API reads and writes cached Blackboard snapshots. High-frequency domains such as actor/world can refresh every tick; heavier domains such as inventory, equipment, skill, and quest refresh by interval.

Combat scoring has candidate trimming and tick budgets. If predictive scoring exceeds its budget, `CombatManager` degrades the current account to the immediate/baseline path and records the fallback reason.

## Tests

```powershell
.\AetherRunner_tvm.exe scripts/run_tests.lua maple_unit maple_flow
```
