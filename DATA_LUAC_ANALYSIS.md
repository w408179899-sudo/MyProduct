# data.luac Analysis

## Scope

This document is a reverse-engineering summary of [scripts/data.luac](C:/Users/GoldGiven/Desktop/cunnei/scripts/data.luac).

It is based on `./luac.exe -l scripts/data.luac` disassembly, not original source recovery. Function names, exported structure, constants, and major control flow are reliable. Comments, local variable names, and exact original formatting are not recoverable from bytecode alone.

## Quick Conclusion

`data.luac` is a normal Lua bytecode module compiled from `data.lua`, not an opaque encrypted blob. It returns a module table `M` and exposes the core game-memory API used by the higher-level leveling scripts.

The module has three layers:

1. Bootstrap and process/driver initialization
2. Raw memory object/name/path helpers
3. High-level gameplay/data APIs such as player info, task position/path, nearby object enumeration, and UI interaction

The recent `F9 Data API` failures are consistent with stale low-level offsets or stale pointer chains inside this module, not with high-level runner logic.

## File Facts

- Source chunk name embedded in bytecode: `data.lua`
- File: `scripts/data.luac`
- Size: `21579`
- Last write time: `2026-04-17 18:22:19`
- Top-level chunk shape: `main <data.lua:0,0> (74 instructions, 26 functions)`

## Embedded Constant Table

These constants are set into the first module-like offset table at load time:

| Name | Value | Likely meaning |
| --- | ---: | --- |
| `UWorldBase` | `134120232` | world / actor root |
| `ObjectBase` | `132769320` | object array root |
| `FnameBase` | `132666768` | UE name pool root |
| `GameMgr` | `109044160` | main UI / game manager root |
| `UTutorialManager` | `109046440` | main task / route manager root |
| `UIGamepadEventMgr` | `109046472` | selected UI control root |
| `loading` | `133465060` | loading-state flag |
| `Controlsoffset` | `132769784` | UI control array root |
| `moveto` | `16560368` | movement remote call RVA |
| `click` | `8266064` | UI click remote call RVA |
| `move_EMoveCtrl` | `1240` | move-controller offset from player |

The module also embeds a driver license key string:

- `LUA5D744CAA2A7A601FB14514E3D93B9D5A`

## Exported API Surface

From the main disassembly, `data.luac` exports these functions on module table `M`:

| Function | Bytecode source range | Purpose |
| --- | --- | --- |
| `InitGameinfo` | `<data.lua:38,70>` | initialize driver, mode, module base |
| `GetByName` | `<data.lua:110,134>` | resolve UE name by name-id |
| `GetFullName` | `<data.lua:139,163>` | build full object path/class name |
| `GetObjectPtr` | `<data.lua:165,176>` | resolve object pointer from object id |
| `EnumCButton` | `<data.lua:181,232>` | enumerate button-like UI controls |
| `IsOuterVisible` | `<data.lua:236,248>` | visibility helper for UI/control tree |
| `EnumCText` | `<data.lua:250,302>` | enumerate text UI controls |
| `EnumCTextFiltered` | `<data.lua:305,321>` | filtered text enumeration |
| `EnumCImage` | `<data.lua:324,365>` | enumerate image UI controls |
| `EnumMonster` | `<data.lua:368,420>` | enumerate nearby monsters |
| `EnumGroundItem` | `<data.lua:423,481>` | enumerate nearby ground items |
| `EnumPortal` | `<data.lua:484,531>` | enumerate nearby portals |
| `EnumNPC` | `<data.lua:534,581>` | enumerate nearby NPCs |
| `EnumInteractiveItem` | `<data.lua:584,631>` | enumerate nearby interactive objects |
| `GetPlayerAddr` | `<data.lua:635,645>` | resolve player object pointer |
| `GetPlayerinfo` | `<data.lua:647,682>` | read player runtime info |
| `control_click` | `<data.lua:689,694>` | invoke UI click remote call |
| `MoveTo` | `<data.lua:704,724>` | invoke movement remote call |
| `GetChineseName` | `<data.lua:726,739>` | auxiliary move/name helper |
| `IsMainInterface` | `<data.lua:742,746>` | check main interface state |
| `GetCurrentSelected` | `<data.lua:749,776>` | inspect currently selected control |
| `Isloading` | `<data.lua:779,782>` | check loading state |
| `GetMainTaskPos` | `<data.lua:785,794>` | read main task destination |
| `GetMainTaskPath` | `<data.lua:797,815>` | read main task route points |

There are also two globals created outside `M`:

- `_ENV.ensure_driver` `<data.lua:25,36>`
- `_ENV.readWstring` `<data.lua:76,105>`

## Structure and Behavior

### 1. Driver bootstrap

#### `ensure_driver()`

Behavior inferred from bytecode:

- checks `driver.is_loaded()`
- if already loaded, returns `true`
- otherwise calls `driver.load(LICENSE_KEY)`
- returns:
  - `true` on success
  - `false, err or "unknown error"` on failure

This means all later APIs depend on the driver bootstrap being valid before any process/memory read succeeds.

#### `InitGameinfo(pid, mode)`

Behavior:

- stores `pid` into `M.pid`
- validates pid, logs invalid PID and returns `false, "invalid PID"` if missing
- calls `ensure_driver()`
- initializes `M.g_base = driver.init_call(pid)` if not already available
- checks `proc.get_mode()` and switches with `proc.set_mode(mode)` if needed
- resolves `M.GameBase = proc.module(pid, "torchlight_infinite.exe")`
- logs module base like `0x%X`
- returns `true` on success, otherwise `false, err`

This function is the required bootstrap for everything else. If `GameBase` or `g_base` is stale or zero, every downstream API becomes suspect.

### 2. String and object helpers

#### `readWstring(addr, len)`

Behavior:

- reads `len * 2` bytes through `driver.read_memory`
- defaults `len` to `128`
- manually decodes UTF-16LE bytes into Lua string output
- stops on `0x0000`

This helper is used later to read selected UI text strings.

#### `GetByName(nameId)`

Behavior:

- uses `M.GameBase + FnameBase`
- resolves a UE name-pool block with:
  - upper 16 bits for block index
  - lower 16 bits for entry index
- reads entry length via `proc.read_u16`
- reads string bytes via `proc.read_string`

This is a standard UE-style FName lookup pattern.

#### `GetFullName(obj)`

Behavior:

- walks the `outer` chain using `proc.read_u64(obj + 32)`
- reads class/name ids using offsets around `+24`
- repeatedly prefixes outer names
- strips outer path prefix with `gsub(".*()/", "")`
- formats final result with `string.format("%s %s%s", ...)`

This is used for diagnostics and UI object identification.

#### `GetObjectPtr(index)`

Behavior:

- uses `M.GameBase + ObjectBase`
- uses high bits / low bits split of object index
- resolves object chunk pointer then object entry pointer
- final object stride observed: `24`

This is the low-level object-array resolver for UE objects.

### 3. UI enumeration

#### `EnumCButton`, `EnumCText`, `EnumCImage`

All three families are built around `Controlsoffset` and read a UI control array from:

- array pointer near `Controlsoffset + 280`
- count near `Controlsoffset + 288`

`EnumCButton` specifically filters out `Default__UIButton`, then walks children/sub-controls and returns a structured list of UI buttons.

`EnumCText` and `EnumCImage` follow the same overall pattern, but collect different control payloads.

`IsOuterVisible` is a helper used to decide whether nested UI controls should be treated as visible.

### 4. World / nearby entity enumeration

#### `EnumMonster`, `EnumGroundItem`, `EnumPortal`, `EnumNPC`, `EnumInteractiveItem`

These functions are parallel variants over a shared world/nearby-object structure rooted from `UWorldBase`.

Although the full loops were not transcribed line by line here, the structure is clear from disassembly:

- resolve one or more root pointers from `M.GameBase + UWorldBase`
- walk a nearby-object array
- inspect class or type name through `GetByName` / `GetFullName`
- extract actor coordinates and ids
- return structured tables for higher-level logic

Operationally, these five APIs rise and fall together. If the world root or nearby array path changes after a game update, all five will fail together.

### 5. Player and movement

#### `GetPlayerAddr()`

This function constructs and logs the exact pointer chain string:

```text
[[[[[[0x%X]+0x210]+0x38]]+0x30]+0x2A0]
```

Where `%X` is:

```text
M.GameBase + UWorldBase
```

It then calls:

- `proc.eval_addr(M.pid, expr)`

If the chain fails, it returns:

- `nil, "对象指针解析失败"`

This is the first critical root for player-dependent APIs.

#### `GetPlayerinfo()`

Behavior:

- calls `GetPlayerAddr()`
- returns `nil, "获取玩家对象失败"` if player pointer cannot be resolved
- reads:
  - `entityId` from `player + 1836`
  - `eRole` from `player + 1376`
- if `eRole` is valid, reads:
  - HP block around `eRole + 1424`
  - HP seal at `+1480`
  - MP block around `+1504`
  - MP seal at `+1552`
  - shield block around `+1576`
- reads transform data:
  - `x` at `player+304 + 264`
  - `y` at `player+304 + 268`
  - `z` at `player+304 + 272`
  - `angle` at `player+304 + 308`

Returned fields observed in bytecode:

- `entityId`
- `eRole`
- `curHp`
- `maxHp`
- `Hpseal`
- `curMp`
- `maxMp`
- `Mpseal`
- `curShield`
- `maxShield`
- `x`
- `y`
- `z`
- `angle`

#### `MoveTo(x, y)`

Behavior:

- gets player pointer via `GetPlayerAddr()`
- resolves move controller via `player + move_EMoveCtrl`
- writes target x/y floats into `M.g_base + 0/+4`
- computes function address `M.GameBase + moveto`
- calls `driver.exec_call(M.pid, func, moveCtrl, M.g_base)`

This is the core injected movement call used by higher-level navigation.

#### `control_click(addr)`

Behavior:

- computes function address `M.GameBase + click`
- transforms control pointer with `addr + 1016`
- calls `driver.exec_call(M.pid, func, addr_plus_1016, 0)`

This is the core injected UI click used by button activation.

### 6. UI state / selection / task routing

#### `IsMainInterface()`

Behavior:

- reads `u8` at:
  - `M.GameBase + GameMgr + 84`
- returns `true` if value is `1`

#### `GetCurrentSelected()`

Behavior:

- reads `M.GameBase + UIGamepadEventMgr`
- then reads selected control pointer at `+648`
- if valid:
  - resolves full name via `GetFullName`
  - reads screen-ish `x/y` floats through nested pointers at offsets around `+224`, then `+212/+216`
  - reads text through chain:
    - selected control `+1832`
    - then `+392`
    - then `+56`
    - finally `readWstring(...)`
- returns table with:
  - `addr`
  - `Fullname`
  - `x`
  - `y`
  - `text`

#### `Isloading()`

Behavior:

- reads `u8` at:
  - `M.GameBase + loading`
- returns `true` if value is `1`

#### `GetMainTaskPos()`

Behavior:

- reads tutorial/task manager root at:
  - `M.GameBase + UTutorialManager`
- dereferences manager object at `+216`
- if valid, reads float destination:
  - `x` from `+656`
  - `y` from `+660`
  - `z` from `+664`

#### `GetMainTaskPath()`

Behavior:

- reads tutorial/task manager root at:
  - `M.GameBase + UTutorialManager`
- dereferences manager object at `+216`
- reads:
  - route base pointer at `+624`
  - route point count at `+632`
- iterates `count` entries
- each point stride is `16`
- each point stores floats:
  - `x` at `+4`
  - `y` at `+8`
  - `z` at `+12`
- returns array of `{x, y, z}`

This is the route source used by the leveling nav worker and any smooth point-by-point movement logic.

## F9 Failure Mapping

Based on the recent `F9 Data API` log, the failures split into a few clusters.

### Cluster A: player root chain failure

Failed APIs:

- `GetPlayerAddr`
- `GetPlayerinfo`
- `nav.player_info`
- `nav.player_pos`

Observed failure symptom:

- bogus pointer chain resolution at a non-sensical address such as `0xEB8348FD0348D6FF`

Most likely cause:

- `UWorldBase` changed
- or the `[[[[[[base]+0x210]+0x38]]+0x30]+0x2A0]` chain changed

### Cluster B: nearby world array failure

Failed APIs:

- `EnumMonster`
- `EnumGroundItem`
- `EnumPortal`
- `EnumNPC`
- `EnumInteractiveItem`
- `nav.enum_ground_items`
- `nav.enum_portals`
- `nav.enum_npcs`
- `nav.enum_monsters`

Observed failure symptom:

- repeated nearby-array parse failures at invalid address like `0x520067006F00E4`

Most likely cause:

- world root / actor array offsets changed
- or one of the intermediate nearby-array dereference paths changed

### Cluster C: task manager path failure

Failed APIs:

- `GetMainTaskPos`
- `GetMainTaskPath`
- `nav.get_main_task_pos`
- `nav.get_main_task_path`

Most likely cause:

- `UTutorialManager` offset changed
- or manager internal offsets `+216`, `+624`, `+632`, `+656`, `+660`, `+664` changed

### Cluster D: selected control unavailable

Failed APIs:

- `GetCurrentSelected`
- `nav.get_current_selected_button`

Possible causes:

- `UIGamepadEventMgr` moved
- selected-control offset `+648` changed
- or no selected control existed at test time

### Still working in that F9 log

These APIs still returned valid values:

- `IsMainInterface`
- `Isloading`
- `nav.is_main_interface`
- `nav.is_loading`

This matters because it suggests not every root offset is broken. At least some static globals still line up.

## Practical Repair Priority

If the goal is to restore the current stack with minimum effort, the repair order should be:

1. `GetPlayerAddr()` chain
2. world / nearby entity array path used by `Enum*`
3. `UTutorialManager` task position/path chain
4. `UIGamepadEventMgr` selected-control chain
5. re-verify `control_click` and `MoveTo` only after player and UI roots are stable

Reason:

- player pointer recovery restores position and movement dependencies
- nearby enumeration recovery restores combat, portal, NPC, and gather logic
- task manager recovery restores call-task routing
- selected-control recovery is diagnostic/auxiliary and lower priority

## Notes for Future Recovery

- Do not patch `scripts/data.luac` directly unless there is no source regeneration path.
- Prefer reconstructing a readable `data.lua` equivalent and re-compiling.
- When validating offset fixes, use `F9 Data API diagnostics` first before testing runner logic.
- Treat mass API failure as a root-offset problem, not as separate feature bugs.
- The leveling runner depends on this module much more than it appears from high-level Lua logs. When `data.luac` is stale, runner symptoms will look random even if the runner code is unchanged.

## Suggested Next Step

If needed, create a second document focused only on repair work:

- map each exported function to exact root offsets
- mark which ones are confirmed broken after the latest game update
- track replacement offsets and re-test status

That repair sheet should stay separate from this analysis document so the structural understanding and the live offset triage do not get mixed together.
