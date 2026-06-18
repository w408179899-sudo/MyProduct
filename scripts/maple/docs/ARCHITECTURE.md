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

## Important Files

```text
scripts/maple_control_ui.lua
scripts/maple_account_worker.lua
scripts/maple/bootstrap.lua
scripts/maple/blackboard.lua
scripts/maple/account/store.lua
scripts/maple/account/orchestrator.lua
scripts/maple/environment/mock_environment.lua
scripts/maple/systems/executor.lua
scripts/maple/data/action_specs.lua
```

## Tests

```powershell
.\AetherRunner_tvm.exe scripts/run_tests.lua maple_unit maple_flow
```
