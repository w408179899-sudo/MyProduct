# Maple API Probes

Use probes to validate real `data.lua` integration before enabling long-running business flow.

## Readonly Probe

```powershell
.\AetherRunner_tvm.exe scripts\maple_probe_readonly.lua
```

The readonly probe:

- connects to `msw.exe`
- reads `player_info`
- reads `list_nearby`
- reads `list_inventory`
- reads `list_skills`
- reads `list_quickslot`
- prints actor, world, skill, and inventory summaries

It does not issue movement, attack, item, or pickup commands.

## Action Probe

```powershell
.\AetherRunner_tvm.exe scripts\maple_probe_actions.lua
```

The action probe connects to the client and issues:

- `BasicAttack`
- `UseQuickslot`
- `SetWalkDirection` left
- `StopMove`
- `SetWalkDirection` right
- `StopMove`
- `PickAllDrops`

Optional globals:

```lua
probe_quickslot_slot = 1
probe_move_ms = 300
probe_target_name = "msw.exe"
probe_license_key = nil
```

Run this only when issuing these actions in the current client is acceptable.

## Fixtures

Fixture samples live in:

```text
scripts/maple/probes/fixtures/
```

The first sample is based on the colleague-provided API contract. Replace or add new fixture files after readonly probe captures real client payloads.

Tests must keep using fixtures or fake `data` modules by default; they must not require a live client.

## Combat Switch

Account setting:

```lua
smart_combat_enabled = true
```

means predictive combat is allowed.

When `smart_combat_enabled` is false, `CombatManager` forces immediate/basic combat even if an old `combat_logic_mode = "predictive"` value exists in config.
