# Isolated combat-flow validation

This console owns the ordinary `MultiAccountWorkspace` / `WorkerProcessManager` lifecycle for one isolated account. It does not bypass authorization, hardware verification, KMBox mutexes, or DMA leases. No command means read-only status. It never initializes the manager from `--status` or `--stop`.

Prepare a separate client directory with its own `Roadhog.exe`, copied/rebased configuration and credentials, and **exactly one account** in `config/accounts.json`. Do not copy production `config/workers` manifests or run-intent. Stop the production account through its existing manager and confirm its worker exits first.

Create `combat-flow-soak-isolated.json` in that directory:

```json
{"Purpose":"CombatFlowSoak","InstanceId":"11111111-2222-4333-8444-555555555555"}
```

Build with an isolated desktop-copy sink (the Roadhog project has an after-build desktop copy hook):

```powershell
dotnet build tools/CombatFlowSoak/CombatFlowSoak.csproj -c Release -o .tmp/combat-flow-soak-tool -p:RoadhogDesktopScriptRoot=.tmp/combat-flow-soak-build-sink
```

Run examples (replace the isolated root):

```powershell
.tmp/combat-flow-soak-tool/CombatFlowSoak.exe --root .tmp/combat-flow-live --account 11111111-2222-4333-8444-555555555555 --status
.tmp/combat-flow-soak-tool/CombatFlowSoak.exe --root .tmp/combat-flow-live --account 11111111-2222-4333-8444-555555555555 --run --version CoordinatedV2 --minutes 15
.tmp/combat-flow-soak-tool/CombatFlowSoak.exe --root .tmp/combat-flow-live --account 11111111-2222-4333-8444-555555555555 --run --version CoordinatedV2 --continuous
.tmp/combat-flow-soak-tool/CombatFlowSoak.exe --root .tmp/combat-flow-live --account 11111111-2222-4333-8444-555555555555 --run --version CoordinatedV2 --minutes 5 --rollback-on-completion
.tmp/combat-flow-soak-tool/CombatFlowSoak.exe --root .tmp/combat-flow-live --account 11111111-2222-4333-8444-555555555555 --stop
```

`--stop` sends a session-specific request to a running harness. Ctrl+C also requests orderly shutdown. Both use the manager's explicit `StopAsync`, then verify observed worker processes disappear. They do not stop production accounts or kill arbitrary processes. The console will report shutdown failure and return nonzero if stopping/exiting cannot be confirmed within `--stop-seconds` (default 60). Do not start the production worker again until actual test-worker exit is confirmed.

`--run` requires an explicit version and exactly one of `--minutes N` or `--continuous`. Continuous runs have no automatic end; stop them with `--stop` or Ctrl+C. They cannot use `--rollback-on-completion`. Their reports set `Continuous=true` and `RequestedDurationSeconds=null`. It persists the version in the **isolated** account config before initialization. A pre-existing `ForceLegacy` policy blocks a V2 run; the harness never clears it automatically. The actual worker version is checked while running. The executable for children is always `<root>/Roadhog.exe`, not the console executable.

`--rollback-on-completion` is accepted only for `--run --version CoordinatedV2`. After a healthy completed observation window, it calls the same manager's `SetForceLegacyCombatFlowAsync(true)` before ordinary stop cleanup. It verifies exactly one successful result for the isolated account, reads back `ForceLegacy=true`, and confirms that rollback itself stopped its workers. The ordinary `StopAsync` and exit verification still run, including after rollback failure or timeout. Rollback has its own `--stop-seconds` budget; fallback stop receives a fresh full budget. Cancellation or an unsuccessful observation skips rollback and still stops the worker. `RollbackVerified` is true only when both rollback and final shutdown are confirmed. The persisted isolated rollback policy stays enabled; the harness does not restart the account or clear this policy. A subsequent explicit Legacy run can validate the fallback while the policy remains enabled.

The JSON report is `<root>/combat-flow-soak-status.json`; worker logs remain under `<root>/logs/accounts/<instance>/`. Console samples report process/version, kills, heartbeat and log timestamp/size. Full launch specs, RPC tokens and credential contents are never printed. A completed duration requires the final sample to report a running worker with the requested version and status less than 10 seconds old. This is evidence of an observation window, **not** a claim of no defects; inspect worker logs and gameplay progress separately. Kill progress uses the first sample of each process as its baseline, so prior adopted-worker kills are excluded and kills between process startup and its first sample are not counted.

Exit codes: `0` completed or deliberately cancelled with confirmed shutdown; `2` preflight/argument failure; `3` run/start/rollback failure or the requested running version is not confirmed at the end; `4` shutdown/disposal not confirmed. Default polling is 5 seconds; startup timeout 120 seconds; all are configurable.

Report publication is telemetry, not a stop condition. Atomic writes use `File.Replace` for existing files and bounded retries for Windows reader conflicts. Persistent report I/O failures are counted in `ReportPublishing` and emitted as `report_write_failed`; the running account continues and publication is retried on the next sample. Startup publication and explicit stop-request writes remain strict. Readers should open reports with `FileShare.ReadWrite | FileShare.Delete`. A stale report must be checked against the harness process and console; it does not prove the worker stopped.

Run the independent file-conflict regressions with `dotnet run --project tools/CombatFlowSoak.Tests/CombatFlowSoak.Tests.csproj -c Release`. They reproduce the old overwrite failure, test bounded failure and subsequent recovery, preserve strict stop writes, and verify compatible readers and cancellation.

## Scheduled log inspection

`Inspect-LongRun.ps1 -ManifestPath <json>` reads an isolated session without controlling the account. The manifest contains `Root`, `AccountId`, `SessionId`, `StartedAtUtc`, `CheckAtUtc` (ISO 8601 timestamps), `OutputDirectory`, `ConsoleLog`, and `SymbolsPath` (the matching `FileLogSymbols.v1.json`). Capture the session ID and start time from the newly running harness. Set the check time only after its actual V2 startup is confirmed. The inspection refuses to run before that time.

The script copies account logs for review, decodes compact R1 records, and checks kills, deaths, errors, worker state/version, worker changes, and long gaps between kills. It writes `summary.json`, `attention-events.json`, a Chinese report, and a completion receipt. A successful script exit means the inspection completed; the report's findings determine whether problems need investigation. Deaths and long gaps require reviewing their context rather than inferring a cause from counts. The inspection does not stop a continuous run or send a chat notification.

On Windows, schedule this read-only script with a one-time Task Scheduler trigger, the current interactive user, and hidden PowerShell. Verify the task's next run time after registration. Do not start the isolated worker while the production account still has running intent or a live worker.
