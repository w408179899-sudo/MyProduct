# Testing

Run MapleStory framework tests:

```powershell
.\AetherRunner_tvm.exe scripts/run_tests.lua maple_unit maple_flow
```

Run syntax checks:

```powershell
.\luac_tvm.exe -p scripts/maple_control_ui.lua
.\luac_tvm.exe -p scripts/maple_account_worker.lua
```

## Test Families

- `maple_unit`: Planner, BT, Executor, Managers, Safety, Logger, Snapshot, Replay.
- `maple_flow`: mock agent tick, account store, per-account task start, isolated stop, max parallel start.

Tests must not require a live game client.
