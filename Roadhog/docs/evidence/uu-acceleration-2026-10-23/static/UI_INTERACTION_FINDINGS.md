# UU UI interaction static support

2026-10-23. Local artifact `C:\Program Files (x86)\Netease\UU\5251\ui.dll`, version6.20.1.63. No DMA/KMBox access or target actions in this analysis.

## Home page and acceleration cycle names

`skin/v40/home_page.xml` identifies:

`child_home_page -> layout_home_page -> layout_top_area -> layout_game_card -> game_card_0`.

Other cards are `game_card_1`, `game_card_2`, `game_card_3`. Match the first card's descendant `game_name` to the intended game before choosing its button. Names such as `start_acc` and `stop_acc` occur in multiple cards and in the details page.

Large-card layout `skin/v40/game_card.xml` contains:

- `game_card -> normal_shade -> start_acc`: `%{1300}` = 立即加速.
- `game_card -> acced_shade -> acc_detail`: `%{1301}` = 加速详情.
- `game_card -> acced_shade -> stop_acc`: `%{1279}` = 停止加速.
- `game_card -> acc_progress`.

`skin/v40/my_game.xml` has the expanded list `child_my_game -> layout_my_game -> scroll_layout -> game_layout`, whose cards can use `game_card_small.xml` or `game_card_free.xml`. Both contain `game_name`, `normal_shade`, `start_acc_layout`, `start_acc`, `acced_shade`. Use visible geometry/order and the game title; XML does not identify the currently first configured game.

`skin/v40/game_accing.xml` defines `child_game_accing -> layout_game_accing -> acc_progress`, but its labels are placeholders (返回1, 加速的游戏名称) and colored backgrounds. Treat this as a candidate resource, not proof that the running version displays this page.

Native uu.exe UTF16 layout literals provide additional candidates absent from the extracted card XML:

- file offset0xA5CDB8: `name="accing_shade" visible="false" float="true" pos="0,0,218,320"`.
- file offset0xA5F9B8: `name="acc_progress" float="true" pos="0,310,218,320"` with `card_accing_progress_fore.png`.
- file offset0xA5F830: `name="acced_shade" visible="false" float="true" pos="0,0,218,320"`.

Runtime observation should check this same card subtree's visible `accing_shade`/`acc_progress`, then an accelerated card/details view and advancing `acced_time`. Static strings alone do not establish the transition.

## One export lookup mismatch

Saved live report `.tmp/uu-dma-20261023/cycle-tree-before/report.json` reports `??_7CTextUI@DuiLib@@6B@` lookup actual0x72A3E9EC, expected0x72A4014C. With base0x728A0000:

- actual RVA0x19E9EC equals the local export `??_7CListUI@DuiLib@@6B@`.
- expected RVA0x1A014C equals local `??_7CTextUI@DuiLib@@6B@`.

This precisely identifies what the unexpected address corresponds to; it does not prove whether the remote export table differs, an API name lookup/caching collision occurred, or a read-quality issue occurred. Compare the target EAT's exact name bytes/ordinal/function RVA, preferably via a fully validated table read. Do not substitute the unexpected address for CTextUI or overwrite another type's cache entry. A mismatch in an unused type need not invalidate separately verified accessors/types; record the unsupported types explicitly.

## Mouse and button diagnostics

Exported x86 accessor machine code confirms these CPaintManagerUI offsets:

- `GetMousePos` RVA0xF1850: signed int32 X at+0x80, Y at+0x84.
- `GetFocus` RVA0xF0BD0: focus-control pointer+0x64.
- `IsCaptured` RVA0xF1ED0: byte+0xD1. `SetCapture` and `ReleaseCapture` set/clear it around Win32 capture calls.

MessageHandler RVA0xF2350 confirms:

- Mouse coordinates are copied from signed LOWORD/HIWORD of mouse-message lParam; the same coordinates feed FindControl. The mouse-move case sets tracking and writes these at RVA0xF4013/0xF4019. For the current100% UI scale, compare them with the client-space control rectangles.
- Manager+0x68 is the hovered-control pointer: the old object receives event0xA and the pointer is cleared; a new hit object receives event0xB and is stored at RVA0xF42CF.
- Manager+0x6C stores the control receiving the button press; mouse-up clears it (e.g.RVA0xF4744).

`CButtonUI::DoEvent` RVA0x10B770 confirms state DWORD at button+0x8AC:

- event0xB sets bit0x08 (hover), event0xA clears bit0x08.
- press sets0x50 (bits0x10 and0x40); release clears0x50 and activates only when the release point is within the button RECT.

Read manager+0x60 for0x78 bytes to obtain root/focus/hover/click, last mouse coordinates and capture byte; read the exact button's+0x8AC DWORD separately after validating its type/version.

These fields show the last messages processed by this UI; they can be stale if a covering window receives the input. They do not alone establish current global cursor position, foreground window, or successful business action. A fresh mouse coordinate inside the button plus hovered pointer equal to that button is stronger evidence of mouse delivery, while acceleration state must still be verified separately.

Detailed excerpts are in `manager-mouse-contexts.json` and `button-mouse-state-contexts.json`.

## Stop-acceleration confirmation dialog

Local uu.exe 6.20.1.63 (preferred image base 0x400000) has three UTF16 `%{1547}` literals. The branch at VA0x722D78 supplies this text to VA0x6A3140; the similar branch at VA0x6C7AFA does the same. These are executable references, not merely the language table.

VA0x6A3140 constructs a generic confirm object via VA0x6A5EA0. Its skin global VA0x113F198 is initialized at VA0x44B6A9 with the literal `v40\popupxml\common_alert_with_option.xml` (VA0xE3B080). It calls CWindowWnd::Create with title `COMMON_ALERT_BOX`. The class-name method at VA0x6A6480 returns `Confirm`.

Exact controls and stop-specific text:

- Unnamed root Container; window resource size400x270 before dynamic adjustments.
- `tip_title` receives `%{1503}`.
- `tip_desc` receives `%{1547}`: 停止加速可能导致游戏重连，请确保已退出游戏或不在对局中。
- `Ok` receives `%{2766}`: 停止.
- `Cancel` receives `%{1453}`: 取消.
- `option_value` is the optional 不再提醒我 checkbox (`%{1042}`).
- `close` is the corner close button.

The click handler at VA0x6A64D0 compares `Ok` or `bt_yes`, writes confirm-object+0x378=0 and posts close; Cancel/bt_no writes1 and close/bt_close writes2. Callers proceed with stopping only for result0. Do not click the similarly named main-page stop button again while its owner is disabled.

Window object mapping differs by class:

- ui.dll RVA0xE1190 CWindowWnd::__WndProc sets GWLP_USERDATA(-21) to the object supplied in WM_NCCREATE lpCreateParams. Object+4 is HWND.
- Generic DuiLib WindowImplBase embeds its CPaintManagerUI at object+0x38 (constructor RVA0xA9460).
- **This UU confirmation class directly derives from CWindowWnd and embeds CPaintManagerUI at object+0x30**, proven by VA0x6A5F0E add ecx,0x30; call [0xD4D604], whose imported symbol is `??0CPaintManagerUI@DuiLib@@QAE@XZ`. Its object size is0x380 and primary vtable localVA0x107D37C (RVA0xC7D37C).
- Therefore validate the confirm userData object's+4 against its HWND, then manager=userData+0x30; manager+0xC must match that same HWND, and manager+0x60 gives root. Use runtime control rectangles and the modal client origin rather than resource positions for input.

Parent's saved main-window style changed0x160F0000 to0x1E0F0000; the difference is WS_DISABLED0x08000000, consistent with an active modal. This is evidence of disabled owner state, not by itself proof of a specific modal's content.
