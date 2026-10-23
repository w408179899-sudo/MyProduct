# UU 6.20.1.63 static findings, 2026-10-23

Scope: read-only local installed artifacts, no DMA device access, process actions, clicks, memory writes, or credential inspection.

## Artifact identity

Directory `C:\Program Files (x86)\Netease\UU\5251`; uu.exe, ui.dll, skin.dll file version 6.20.1.63.

- uu.exe SHA256 `967c86c19ccbb05a151e522288389e0f842c21c52a94924ca495d846ea3e76fb`; x86 native UI and acceleration service/business code. Contains AccService, VPNStatus, AccServiceCallbackHandler, StartVPN/StopVPN and game acceleration query/start commands. Prior matching artifact evidence is in `.tmp/uu-analysis-20261020`.
- ui.dll SHA256 `1939022846a612be7802794c0c6f5d5d2ee38146b4ec47d48cdcc9f16f9bb72c`; x86 DuiLib with exported accessors, containers and manager registry.
- skin.dll SHA256 `fb0ffd42663f5c61b6d6dda7536da24b21a193e9ea0ea99e800f661d89598c05`; embedded ZIP with XML layouts and localization. See `skin-source.json`, `skin-zip-inventory.json`, and `extract_skin.py` for extraction provenance.

## Buttons and page

`skin/v40/game_acced.xml:8`: `launch_game`, localized string id1277 = 启动游戏 (`skin/v40/lang/chs.xml:280`). Width110, height36.

`skin/v40/game_acced.xml:10`: `stop_acc`, localized string id1279 = 停止加速 (`skin/v40/lang/chs.xml:282`). Width110, height36; left margin16. This button stops acceleration; it is not a stop-game button.

`skin/v40/game_acced.xml:123`: `acced_time` text defaults00:00:00. Runtime elapsed text, parent visibility, game_name and live button state are useful corroborating observations; static defaults do not prove active acceleration.

`skin/v40/uu.xml:2`: main window1000x688. `child_game_acced` at line37 is initially hidden and positioned off-client [1000,94,2000,676], so static XML coordinates cannot be directly used as current click targets. Runtime visibility, movement and DPI must be resolved.

Conditional geometry only: if game detail is at client(0,94), 100% scale, and PC-game `set_console_net` is hidden, launch rectangle[40,328,150,364], center(95,346); stop rectangle[166,328,276,364], center(221,346). Actual runtime rectangles take precedence.

## DMA-readable field layout derived from exact exported x86 accessors

These are version-specific local static observations. Validate target module identity/code before applying remotely. All pointers are32-bit.

- `ui.dll + 0x1F2C84`: exported `CPaintManagerUI::s_all_managers_`, CStdPtrArray with data pointer+0, count+4, capacity+8.
- `ui.dll + 0x1F2911`: exported registry-enabled byte. `AddManager` returns immediately when0; an empty registry then does not prove no UI.
- Manager+0xC HWND; manager+0x60 root CControlUI pointer.
- CControlUI+0x14C manager pointer; +0x150 parent pointer.
- CControlUI+0x1D8 CDuiString name; +0x2E4 CDuiString raw text. CDuiString firstDWORD points to terminated UTF16; short string storage starts at string object+4.
- CControlUI+0x260 RECT, four signed int32 values.
- CControlUI+0x2B0 public visible byte AND +0x2B1 internal visible byte; +0x2B2 enabled byte. Ancestor visibility also matters.
- CContainerUI secondary IContainer interface starts at control+0x768. Children array at control+0x76C: data pointer+0x76C, count+0x770, capacity+0x774. Do not use the secondary-interface GetCount this+4 as if it were control+4.
- Manager+0x318 CDPI pointer. CDPI+0 raw percentage, +4 custom percentage, +8 awareness enum. GetDPI returns96 when awareness0, MulDiv(customPercent,96,100) when1, else MulDiv(rawPercent,96,100). Whether rect coordinates need scaling must be corroborated against actual runtime geometry.

`ui-accessors.json` and `ui-accessors-extra.json` retain disassembly, export RVAs and artifact hash. `inspect_ui.py` reproduces it with local Capstone isolated in `static/deps`.

## Remaining unknowns

- Current target-machine acceleration state and actual on-screen coordinates: must come from live target DMA evidence, outside this static task.
- Exact VPNStatus numeric mapping and stable AccService instance/state-member address: strings establish relevant code paths only; no confirmed numeric mapping here.
- Client origin in screen coordinates and dynamic window scaling: a client RECT does not by itself contain the desktop origin.

REA opened the exact uu.exe artifact. The native string search did not complete within this investigation; no findings rely on that pending response. Resource parsing and directly decoded local exported machine code are the evidence used above.
