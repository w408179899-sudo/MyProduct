# Aion 20613 Mission Notes

## Step 0 Current Tracker Teleport

F11 sample after the post-20612 level-14 grind:

```text
character=HiBroHiI level=14
map=DF1_SZ_Munihelr big_map_id=220010000
char_pos=1103.14,2225.12,253.32
quest=20613 status_code=3 req_count=0 seq=3 lv_num=14
quest=20614 status_code=6 req_count=0 seq=4 lv_num=17
quest=20615 status_code=6 req_count=0 seq=5 lv_num=20
target=no_target
dialog=closed
```

Recorded operation:

```text
ClickUiControl parent=quest_indicator_dialog name=prototype stage=quest_20611_indicator_title
QuestTeleport(20613) stage=quest_20613_task_teleport
WaitPositionChanged / CompleteQuestTeleport stage=quest_20613_task_teleport
```

The shared `quest_20611_indicator_title` stage name is historical and still opens
the current right-side tracker row. The 20613-specific stage is only used for the
actual teleport wait/completion.

After `CompleteQuestTeleport`, set `completed_20613_task_teleport=true`, then run
the start NPC dialog step below.

Runtime flags:

```text
clicked_20611_indicator_title
completed_20613_task_teleport
```

## Start NPC Dialog

F11 sample after `quest_20613_task_teleport` lands:

```text
quest_id=20613
status_code=3
req_count=0
map_id=220010000
char_pos=1048.52,2198.80,262.33
target_interact_id=2147495609
target_dist=3.22
target_pos=1050.70,2201.12,262.81
dialog=open
npc_dialog_id=2147495609
content_id=10
quest_id=0
type_text=select_quest
action_hint=dialog_click_x child_index=6 x=25
```

Recorded operation:

```text
NavigateToNpc / InteractNpc interact_id=2147495609 stage=quest_20613_start_npc
ClickDialogXContinuous click_x=25 content_id=10 type_text=select_quest
```

The NPC shares the same interact id and position as the previous 20612 reward NPC,
but this stage is identified by `quest_id=20613`, stage
`quest_20613_start_npc`, and dialog `type_text=select_quest`.

If the bot restarts or loses `completed_20613_task_teleport` after landing, a
20613 step-0 character within NPC range of this position is treated as already
landed and should continue with the start NPC dialog instead of opening the
tracker teleport again.

`InteractNpc` for this stage must also pass `after_open_continuous_x=true` so
opening the NPC immediately waits for the dialog and runs continuous x-click.
This prevents a missed next tick from leaving the dialog open without clicking.
An already open dialog can still be recovered by `content_id=10` even if
`type_text` is missing.

After the continuous x-click finishes, set `completed_20613_start_dialog=true`,
then run the current-tracker teleport below.

## After Start Dialog Teleport

Recorded operation after the start NPC dialog closes:

```text
ClickUiControl parent=quest_indicator_dialog name=prototype stage=quest_20611_indicator_title
QuestTeleport(20613) stage=quest_20613_after_start_teleport
WaitPositionChanged / CompleteQuestTeleport stage=quest_20613_after_start_teleport
```

This is the second quest-20613 teleport and must use its own stage/runtime flag
so it does not collide with `quest_20613_task_teleport`.

Runtime flags:

```text
completed_20613_start_dialog
completed_20613_after_start_teleport
```

After `CompleteQuestTeleport`, set `completed_20613_after_start_teleport=true`
and run the after-start reward NPC dialog step below.

## After Start Reward NPC Dialog

F11 sample after `quest_20613_after_start_teleport` lands:

```text
quest_id=20613
status_code=4
req_count=0
map_id=220010000
char_pos=944.00,1701.69,259.66
target_interact_id=2147507242
target_dist=2.50
target_pos=946.25,1702.77,259.62
dialog=open
npc_dialog_id=2147507242
content_id=10002
quest_id=20613
type_text=select_success
next=HACTION_SELECT_QUEST_REWARD
action_hint=dialog_click_x child_index=6 x=25
```

Recorded operation:

```text
NavigateToNpc / InteractNpc interact_id=2147507242 stage=quest_20613_after_start_reward_npc
ClickDialogXContinuous click_x=25 content_id=10002 type_text=select_success
```

This reward NPC uses a dedicated 20613 stage and runtime flag. Do not reuse the
20612 reward handler even though both dialogs use `content_id=10002`; the NPC id,
quest id, and stage are different.

If the bot restarts after landing and loses
`completed_20613_after_start_teleport`, a character near `946.25,1702.77,259.62`
or an already open dialog with `npc_dialog_id=2147507242` should continue with
this reward dialog instead of opening the tracker teleport again.

After the continuous x-click finishes, set:

```text
completed_20613_after_start_teleport=true
completed_20613_after_start_reward_dialog=true
```

Then idle until the next recorded instruction.
