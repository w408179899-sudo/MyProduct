# Aion 20611 Mission Notes

## Level Gate

Quest `20611` is the first yellow mission after the level-8 gate.

If the character level is below the quest `lv_num`, keep stationary grind active at the current safe point. Once the recorded target level is reached, open the current tracked quest detail before calling `QuestTeleport(20611)`.

After `QuestTeleport` succeeds, do not talk to the NPC immediately. Set `waiting_teleport=true` with `teleport_stage=quest_20611_level_move`, then wait until the character position changes by at least `20m` or the map id changes. Only mark `completed_20611_level_move=true` from `CompleteQuestTeleport`.

## Mission NPC

F11 sample after `QuestTeleport(20611)`:

```text
character=HiBroHi level=10
map=DF1_SZ_Alder_Town big_map_id=220010000
quest=20611 status_code=3 req_count=0
target=리네비르
interact_id=2147503111
npc_pos=586.22,2465.17,278.58
char_pos=586.19,2467.40,278.62
distance=2.23
```

Use centralized NPC name key `MQ20611_NPC_001_MISSION`.

## Mission Dialog

The first visible dialog is a mission list page:

```text
type=select_quest
content_id=10
quest_id=0
npc_dialog_id=2147503111
```

The target yellow quest entry is a visible `dlg_dialog` child:

```text
F3 nearby: depth=2 obj=697775328 name=(no-name) visible=true x=25 y=324 parent=dlg_dialog
```

Click it through `ClickDialogX` using dialog child coordinates `x=25,y=324`. Do not use absolute screen coordinates.

Known follow-up dialog chain:

```text
select1         content_id=1011 -> click x=25
select1_1       content_id=1012 -> click x=25
select1_1_1     content_id=1013 -> click x=25
select1_1_1_1   content_id=1014 -> click x=25 and mark mission dialog complete
```

If the client shows another `type/content_id`, dump the dialog instead of re-interacting with the NPC.

## Obelisk Step

F11 sample after the mission dialog:

```text
quest=20611 status_code=3 req_count=1
target=키벨리스크
interact_id=2147505051
npc_pos=587.69,2467.10,278.79
char_pos=584.72,2466.97,278.62
distance=2.97
```

Use centralized NPC name key `MQ20611_NPC_002_OBELISK`.

Interact with `키벨리스크` by name, not by id fallback. This opens a confirmation popup instead of the normal `dlg_dialog` tree. After a successful NPC interaction, set `opened_20611_obelisk=true` and run `ClickObeliskConfirm`.

Do not mark this step permanently complete just because `ClickButton` returned true. Click/press confirmation, wait briefly, then let the next quest snapshot decide whether the step advanced.

## Obelisk Popup UI

F3 with the mouse on the visual `예` button still returned only `move_state_dialog` children:

```text
name=static_rightward parent=move_state_dialog
name=static_forward parent=move_state_dialog
```

So do not use F3-nearest selection for this popup.

F2 initially showed a misleading `common_alert_dialog` pair:

```text
CHILD parent=common_alert_dialog 02. name=cancel visible=true x=0 y=0
CHILD parent=common_alert_dialog 04. name=cancel visible=true x=0 y=0
```

Do not prefer these common-alert children.

The useful F2 popup-root diagnostics exposed the real popup root and button:

```text
visible unnamed popup roots count=1
popup root 1 obj=707726848 x=511.0 y=300.35
POPUP_CHILD root=707726848 05. name=ok visible=true x=658 y=396
POPUP_CHILD root=707726848 06. name=cancel visible=true x=712 y=396
POPUP_CHILD root=707726848 08. name=cancel visible=true x=746 y=304
```

For `ClickObeliskConfirm`, prefer the visible unnamed popup root child named `ok`. Use `common_alert_dialog.cancel[1]` only as a last fallback. If UI clicking still fails while `opened_20611_obelisk=true`, press `Enter` as a stage-local fallback.

Useful debug keywords:

```text
decision quest=20611
interact-before:quest_20611_obelisk
interact-after:quest_20611_obelisk
obelisk confirm action
obelisk-confirm:quest_20611_obelisk
visible unnamed popup roots
POPUP_CHILD
```

## Current Tracker / Immediate Move

After the obelisk confirmation, the next recorded step is:

```text
quest=20611 status_code=3 req_count=2
```

Open the current tracked quest from the right quest tracker first. Do not use plain `J` for this step, because `J` may open the panel without selecting the current quest. A visible `v3_quest_dialog` is not enough by itself; the runner must first record a successful `quest_20611_indicator_title` click in this session. The required flow is:

```text
ClickUiControl parent=quest_indicator_dialog name=prototype stage=quest_20611_indicator_title
QuestTeleport(20611) stage=quest_20611_target_teleport only after v3_quest_dialog is visible
WaitPositionChanged / CompleteQuestTeleport stage=quest_20611_target_teleport
```

F3 sample for the right quest tracker entry:

```text
parent=quest_indicator_dialog
name=prototype
x=1080 y=260
```

The child `title` at `x=1095 y=260` was tested on 2026-06-10. `ClickButton` returned success, but the quest detail did not open. Use the `prototype` row container instead.

If `prototype` also clicks without opening `v3_quest_dialog`, rotate through the same row's stable F2-visible text/container candidates without using fixed screen coordinates:

```text
prototype -> htmltext -> title
```

The right tracker child named `teleport` is not an open-panel candidate. If the text/container candidates do not open `v3_quest_dialog`, click `quest_indicator_dialog.teleport` as `ClickUiControlWaitTeleport` and immediately enter the normal position-change wait:

```text
ClickUiControlWaitTeleport parent=quest_indicator_dialog name=teleport stage=quest_20611_target_teleport
WaitPositionChanged / CompleteQuestTeleport stage=quest_20611_target_teleport
```

Do not auto-use `over_img` in this flow. It was visible in F3 only while the mouse hovered the row, but F2 showed it as `visible=false` in normal script execution.

Do not treat `dictionary_dialog.teleport_to_npc` visibility as permission to teleport. On 2026-06-10, a stale dictionary dialog was visible on startup and the runner skipped the proper open-current-quest step, then tried to click `teleport_to_npc` directly.

The older blue-link path was observed but is not used by default now:

```text
v3_quest_dialog target link near x=463 y=171
```

Runtime flags:

```text
clicked_20611_indicator_title
clicked_20611_target_link
clicked_20611_dictionary_teleport
completed_20611_target_teleport
```

## Target NPC

After the right tracker teleport, the next recorded state is still quest `20611` with `status_code=3 req_count=2`, but the character is next to `울고른`:

```text
target=울고른
interact_id=2147520815
npc_pos=589.35,2450.16,278.38
char_pos=589.70,2450.37,278.38
distance=0.41
dialog=open
npc_dialog_id=2147520815
content_id=10
type=select_quest
```

Use centralized NPC name key `MQ20611_NPC_003_TARGET`. If the character is already near this NPC, handle this stage directly even if `completed_20611_target_teleport` was not set by an older run.

The first target NPC dialog entry is child `[06]` at `x=24.67 y=171.53`, but this NPC should use the same continuous x-click helper as the NPC test button:

```text
ClickDialogXContinuous stage=quest_20611_target_npc
click_x=25
```

This action repeatedly reads the current NPC dialog and clicks the visible `dlg_dialog` child nearest `x=25` until the dialog closes or the configured click limit is reached.

Do not apply continuous x-click globally. It is only for explicitly recorded dialog stages that are known to be linear x=25 chains. Unknown target NPC pages should still dump first, then be added to `target_dialog_steps` only after observation.
