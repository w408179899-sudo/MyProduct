# Aion 20611 Mission Notes

## Level Gate

Quest `20611` is the first yellow mission after the level-8 gate.

If the character level is below the quest `lv_num`, keep stationary grind active at the current safe point. Once the recorded target level is reached, call `QuestTeleport(20611)` only after opening the quest panel with `J`.

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
