# Aion 20611 Mission Notes

## Level Gate

Quest `20611` is the first yellow mission after the level-8 gate. If the character level is below `lv_num`, the runner should keep stationary grind active at the current safe point. Once the recorded target level is reached, run `QuestTeleport(20611)` only after opening the quest panel.

After the teleport call succeeds, do not talk to the NPC immediately. Set `waiting_teleport=true` with `teleport_stage=quest_20611_level_move`, then wait for the character position to change by at least `20m` or for the map id to change. Only mark `completed_20611_level_move=true` on `CompleteQuestTeleport`.

## Mission NPC After Teleport

F11 sample on 2026-06-10 after `QuestTeleport(20611)`:

- character: `HiBroHi`, level `10`
- map: `DF1_SZ_Alder_Town`, `big_map_id=220010000`
- quest: `20611`, `status_code=3`, `req_count=0`
- target NPC: `리네비르`
- `interact_id=2147503111`
- NPC position: `586.22,2465.17,278.58`
- character position: `586.19,2467.40,278.62`
- distance: `2.23`

Use the centralized NPC name key `MQ20611_NPC_001_MISSION`.

## Dialog

The first visible dialog is a mission list page:

```text
type=select_quest
content_id=10
quest_id=0
npc_dialog_id=2147503111
```

The yellow quest entry is a visible `dlg_dialog` child at x approximately `25`. Initial F11 saw the list item around `y=208`, but the later F3 check on the real dialog showed that the target yellow mission row is lower in the same list:

```text
F3 nearby: depth=2 obj=697775328 name=(no-name) visible=true x=25 y=324 parent=dlg_dialog
```

Click this child through the normal `ClickDialogX` path using dialog child coordinates `x=25,y=324`. Do not use absolute screen coordinates.

The follow-up pages are expected to use the same first-branch dialog ids seen in earlier mission chains:

```text
select1         content_id=1011 -> click x=25
select1_1       content_id=1012 -> click x=25
select1_1_1     content_id=1013 -> click x=25
select1_1_1_1   content_id=1014 -> click x=25 and mark this dialog chain complete
```

If the client shows a different `type/content_id`, let the runner dump the dialog instead of re-interacting with the NPC.
