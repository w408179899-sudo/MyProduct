# Maple API Probes

Use probes to validate real `data.lua` integration before enabling long-running business flow.

## Current Validated Conclusions

- Read APIs are usable for actor, world, inventory, skill, and quickslot snapshots.
- Monster snapshots currently provide `Id`/`MobId`/`Name`/`x`/`y`; `Hp`, `MaxHp`, and `Level` may be empty.
- Own drops are identified by fields such as `Source = "mine"`; do not assume `OwnerCID` alone is enough.
- Drop snapshots can keep raw rows whose `ItemId` and `Name` are both empty after pickup. Treat those as empty shell rows, not pickup targets.
- `PickAllDrops` can report a picked count while only nearby reachable drops actually affect inventory/meso. Use normalized drop keys and inventory deltas instead of raw drop count alone.
- `list_quickslot` returns a table shaped as `{ slots = { ... } }`.
- `quickslot_use(slot, "press")` is currently untrusted for skill release: it can return success while the client still performs a normal attack.
- Foreground key input is currently trusted for skill release. Default skill key is `Shift` (`0x10`).
- Background key posting is currently untrusted for skill release even when the adapter reports success.

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

## Raw Snapshot Probe

```powershell
.\AetherRunner_tvm.exe scripts\maple_probe_snapshot.lua
```

The raw snapshot probe is also read-only. It connects to the client, calls the same read APIs as the readonly probe, and prints raw top-level keys plus a few sample rows for:

- actor info
- mobs
- drops
- npcs
- portals
- inventory items
- learned skills
- quickslots

Optional globals:

```lua
probe_sample_count = 3
probe_target_name = "msw.exe"
probe_license_key = nil
```

Use this when a summary count is not enough to diagnose an adapter or normalizer mismatch.

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

## Quickslot Probe

```powershell
.\AetherRunner_tvm.exe scripts\maple_probe_quickslot.lua
```

The quickslot probe only triggers one quickslot. It does not issue basic attack, movement, stop, or pickup commands.

Optional globals:

```lua
probe_quickslot_slot = 1
probe_quickslot_action = "press"
probe_repeat_count = 1
probe_interval_ms = 250
probe_target_name = "msw.exe"
probe_license_key = nil
```

Use this to isolate whether a specific slot actually releases a skill or default action.

Convenience entrypoints:

```powershell
.\AetherRunner_tvm.exe scripts\maple_probe_quickslot_2.lua
.\AetherRunner_tvm.exe scripts\maple_probe_quickslot_3.lua
```

Use these when slot 1 / Shift is ambiguous or bound to a default attack-like action.

## Quickslot Effect Probe

```powershell
.\AetherRunner_tvm.exe scripts\maple_probe_quickslot_effect.lua
```

The quickslot effect probe reads actor/world state before and after one quickslot trigger, then prints HP/MP/mob/drop deltas. Use it when the visual animation is ambiguous.

Optional globals:

```lua
probe_quickslot_slot = 1
probe_quickslot_action = "press"
probe_wait_ms = 900
probe_target_name = "msw.exe"
probe_license_key = nil
```

## Key Effect Probe

```powershell
.\AetherRunner_tvm.exe scripts\maple_probe_key_effect.lua
.\AetherRunner_tvm.exe scripts\maple_probe_key_effect_background.lua
```

The key effect probe executes the `PressKey` ActionSpec through `MapleEnvironment`, then reads actor/world state before and after the input. It is used to verify the keyboard path when `quickslot_use` reports success but has no game effect.

Default input is foreground Shift:

```lua
probe_input_mode = "foreground" -- or "background"
probe_key_code = 0x10           -- Shift
probe_key_mode = nil            -- optional: "api", "driver", or "background"
probe_hold_ms = 0
probe_wait_ms = 900
probe_target_name = "msw.exe"
probe_license_key = nil
```

Common VK codes:

```text
Shift=0x10 Insert=0x2D Home=0x24 PageUp=0x21
Ctrl=0x11 Delete=0x2E End=0x23 PageDown=0x22
```

## Pickup Effect Probe

```powershell
.\AetherRunner_tvm.exe scripts\maple_probe_pickup_effect.lua
```

Stand directly on or next to visible drops before running this probe. It does not move or attack. It reads actor/world state, executes `PickAllDrops`, optionally presses the configured pickup key, then reads actor/world state again.

Use this to separate three problems:

- drop traversal: before snapshot should show `drops > 0`
- API pickup: `pickup_effect pick_result raw=...` should show whether `pick_all` reports picked/skipped counts
- key pickup: `pickup_effect key_result ...` should show the key method and target `hwnd`; after snapshot should show a lower drop count if the configured pickup key works

Default globals:

```lua
probe_repeat_count = 3
probe_wait_ms = 450
probe_pickup_api_enabled = true
probe_pickup_key_enabled = true
probe_pickup_key_name = "Z"
probe_pickup_key_code = 0x5A
probe_pickup_key_hold_ms = 80
probe_input_mode = "foreground"
probe_key_mode = nil
probe_target_name = "msw.exe"
probe_license_key = nil
```

## Pickup Verify Probe

```powershell
.\AetherRunner_tvm.exe scripts\maple_probe_pickup_verify.lua
```

Stand directly on or next to visible drops before running this probe. It does not move or attack.

This is stricter than `maple_probe_pickup_effect.lua`. It records drop keys, meso, used inventory slots, and item count before pickup, runs pickup actions, then samples the same data repeatedly. Use it to verify whether `PickAllDrops` actually clears the same drop keys or whether `list_nearby().drops` stays stale after pickup.

Each sample logs:

- `snapshot=before/after_*ms` with actor position, raw drop count, normalized drop count, meso, used slots, and item count
- first visible drop keys with id, item id, name, coordinates, `can_pick`, source, and free flag
- `compare=after_*ms` with disappeared, unchanged, and newly appeared drop keys
- inventory deltas for meso, used slots, and item count
- final `conclusion verdict=...`

The raw `dropCount` can remain unchanged after pickup because the API may keep empty shell rows. The normalized drop count intentionally ignores rows whose `ItemId` and `Name` are both empty.

Default globals:

```lua
probe_repeat_count = 1
probe_action_wait_ms = 120
probe_verify_waits = "0,200,500,1000,2000"
probe_max_drop_log = 10
probe_max_key_log = 6
probe_pickup_api_enabled = true
probe_pickup_key_enabled = false  -- default isolates PickAllDrops; set true to test Z key together
probe_pickup_key_name = "Z"
probe_pickup_key_code = 0x5A
probe_pickup_key_hold_ms = 80
probe_input_mode = "foreground"
probe_key_mode = nil
probe_target_name = "msw.exe"
probe_license_key = nil
```

Verdict meanings:

- `drop_keys_cleared`: the before drop keys disappeared from later `list_nearby()` samples
- `pick_claimed_but_drop_keys_still_visible`: `PickAllDrops` reported a picked count, but the same drop keys still remained visible
- `inventory_changed_but_drop_keys_still_visible`: inventory/meso changed but the same drop keys still remained visible, so `list_nearby().drops` may be stale or not a reliable completion signal
- `drop_keys_still_visible`: no useful evidence of pickup completion
- `no_visible_drop_to_verify`: the before snapshot had no visible drops

## Basic Combat Probe

```powershell
.\AetherRunner_tvm.exe scripts\maple_probe_basic_combat.lua
```

The basic combat probe runs a short baseline loop against the current live client. It is intentionally not the full smart-combat flow.

Each tick logs:

- actor position, HP/MP, mob count, drop count
- selected nearest target and `dx`/`dy`
- proposal action and reason
- executed ActionSpec result
- after-action mob/drop delta and whether the selected target still exists

Default behavior:

- pick visible drops with `PickAllDrops`
- select nearest monster
- move a short burst toward the target when outside range
- stop movement before attacking when needed
- release skill through foreground `PressKey`, default `Shift`
- fall back to `BasicAttack` when `PressKey` fails

Optional globals:

```lua
probe_run_seconds = 20
probe_max_ticks = 80
probe_key_name = "Shift"
probe_key_code = 0x10
probe_input_mode = "foreground"
probe_attack_range_x = 95
probe_attack_range_y = 45
probe_stop_range_x = 65
probe_pursuit_y_tolerance = 70
probe_move_ms = 220
probe_attack_wait_ms = 750
probe_pick_wait_ms = 250
probe_target_name = "msw.exe"
probe_license_key = nil
```

Use this only after readonly and key probes have passed. Keep the first live run short, usually 10-30 seconds.

## Manual Platform Recorder

```powershell
.\AetherRunner_tvm.exe scripts\maple_record_platform.lua
```

The recorder connects to the live client and records the actor position into one manual platform file. It is intended to unblock the first single-platform combat and pickup loop before full map recording is available.

Default output:

```text
scripts/maple/maps/manual_platform.lua
```

Hotkeys while the script is running:

```text
F9       start/resume recording
F10      pause recording
F11      save current platform
F12      clear current recording
F1       mark left boundary
F2       mark right boundary
Ctrl+F12 exit recorder
```

Recommended first pass:

- clear the platform manually
- stand on the safe left side
- press `F9`
- walk slowly from left to right along the platform
- press `F1` near the left safe boundary and `F2` near the right safe boundary if needed
- press `F10`
- press `F11`
- press `Ctrl+F12` to exit

Optional globals:

```lua
platform_id = "manual_1"
platform_save_path = nil       -- default scripts/maple/maps/manual_platform.lua
platform_sample_ms = 100
platform_min_distance = 0.05
platform_max_points = 2000
platform_safe_margin = 1
probe_target_name = "msw.exe"
probe_license_key = nil
```

## Platform Mob Sampling Probe

```powershell
.\AetherRunner_tvm.exe scripts\maple_probe_platform_mobs.lua
```

This probe is read-only. Stand on the recorded platform and run it after `manual_platform.lua` exists. It samples actor and nearby monster data for a short period, maps each monster to the recorded platform curve, and prints per-monster movement summaries.

Default behavior:

- loads `scripts/maple/maps/manual_platform.lua`
- connects to `msw.exe`
- samples `player_info` and `list_nearby`
- runs for about 5 seconds
- samples every 100ms when API timing allows
- reports actor platform, monster platform candidates, `platform_y`, `y_delta`, movement range, and rough max observed velocity

Optional globals:

```lua
probe_platform_path = nil          -- default scripts/maple/maps/manual_platform.lua
probe_run_seconds = 5
probe_duration_ms = nil
probe_sample_ms = 100
probe_platform_y_tolerance = 1.2
probe_platform_x_margin = 0.2
probe_platform_merge_epsilon = 0.05
probe_max_log_mobs = 5
probe_target_name = "msw.exe"
probe_license_key = nil
```

Use this to learn whether monster coordinates use foot position, center position, or another anchor relative to the recorded platform.

## Platform Combat Probe

```powershell
.\AetherRunner_tvm.exe scripts\maple_probe_platform_combat.lua
```

This probe runs the first single-platform combat loop against `manual_platform.lua`. It is intentionally scoped to the recorded platform only. By default it switches from combat to pickup only after the recorded platform has zero matching monsters, then moves toward remaining normalized same-platform drops until several scans confirm no normalized drops remain.

Each tick logs:

- actor position, HP/MP, total mob count, total drop count
- actor platform id, platform y, and y delta
- raw API drop scan, including each visible drop's platform classification and filtering reason
- selected same-platform target, target y delta, grounded state, observed velocity, predicted position, attack stand point, and stand distance
- selected proposal action and reason
- executed ActionSpec results
- after-action mob/drop delta and whether the selected target still exists

Default behavior:

- load `scripts/maple/maps/manual_platform.lua`
- only fight monsters that map onto the actor's recorded platform
- prefer targets already inside the configured skill hit box
- move toward a computed stand point with a foreground arrow key when the target is out of range
- briefly face the target with a foreground arrow key, then release the configured foreground skill key
- wait when the only same-platform target is airborne above the configured skill Y range
- move toward normalized same-platform drops with the same foreground direction-key path used by combat movement, then call `PickAllDrops` and press the configured pickup key when close enough on X
- during combat, opportunistically pick drops already near the actor, and briefly detour for same-platform drops that have stayed visible for enough ticks
- treat raw drop Y as an anchor/classification signal, not as a strict pickup distance, because item Y may represent the sprite/center rather than the ground point
- if `PickAllDrops` plus the pickup key does not reduce visible drops, temporarily ignore that drop id and nearby same-cluster candidates, then move to the next candidate
- include same-platform drops even when the API does not mark them as `can_pick`
- sweep the whole recorded platform only when `probe_pickup_sweep_enabled = true`; this is a fallback for suspected API drop misses, not the default pickup path
- return from pickup to combat if same-platform monsters reappear
- stop after several consecutive scans confirm no normalized same-platform drops remain
- fall back to `BasicAttack` only when foreground `PressKey` fails

Default first-pass combat constants:

```lua
probe_run_seconds = 0              -- 0 means no duration limit
probe_max_ticks = 0                -- 0 means no tick limit
probe_clear_remaining_threshold = 0
probe_pickup_empty_confirm_ticks = 5
probe_key_name = "Shift"
probe_key_code = 0x10
probe_input_mode = "foreground"
probe_skill_range_x = 2.0
probe_skill_range_y = 0.3
probe_preferred_attack_distance = 1.4
probe_cast_delay_seconds = 0.7
probe_actor_platform_y_tolerance = 0.6
probe_platform_y_tolerance = 1.0
probe_grounded_y_tolerance = 0.2
probe_move_ms = 180
probe_pickup_move_ms = 360
probe_pickup_move_method = "key"      -- pickup movement reuses the foreground direction-key path by default
probe_move_method = "key"          -- "key" uses arrow keys; "walk_api" uses SetWalkDirection
probe_move_left_key_code = 0x25
probe_move_right_key_code = 0x27
probe_face_ms = 80
probe_face_method = "key"          -- "key" uses arrow keys; "walk_api" uses SetWalkDirection
probe_face_left_key_code = 0x25
probe_face_right_key_code = 0x27
probe_attack_wait_ms = 750
probe_pick_wait_ms = 250
probe_pickup_pick_repeat = 1
probe_pickup_pick_repeat_ms = 100
probe_pickup_drop_fail_threshold = 1
probe_pickup_drop_ignore_ticks = 60
probe_pickup_drop_ignore_cluster_x = 0.75
probe_pickup_drop_ignore_cluster_y = 1.0
probe_pickup_key_enabled = true
probe_pickup_key_name = "Z"
probe_pickup_key_code = 0x5A
probe_pickup_key_repeat = 3
probe_pickup_key_repeat_ms = 80
probe_pickup_key_hold_ms = 80
probe_pickup_range_x = 0.65
probe_pickup_range_y = 0.5
probe_pickup_ignore_raw_y = true
probe_pickup_during_combat_enabled = true
probe_pickup_during_combat_nearby_range_x = 0.8
probe_pickup_during_combat_max_detour_x = 1.5
probe_pickup_age_priority_ticks = 80
probe_pickup_include_all_drops = true
probe_pickup_sweep_enabled = false    -- precise normalized-drop pickup is default; set true only for sweep fallback
probe_pickup_sweep_step = 0.35
probe_pickup_sweep_arrival_x = 0.15
probe_pickup_sweep_safe_margin = 0.1
probe_pickup_sweep_max_ticks = 600
probe_target_name = "msw.exe"
probe_license_key = nil
```

Use this after:

- `manual_platform.lua` exists
- readonly/key probes are valid
- the character is standing on the recorded platform
- the skill key, usually `Shift`, works with foreground input

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
