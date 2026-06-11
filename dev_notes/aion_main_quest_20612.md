# Aion 20612 Mission Notes

## Start NPC

F11 sample after quest `20611` hotspot reward:

```text
quest_id=20612
status_code=3
req_count=0
map_id=220010000
char_pos=489.88,2300.02,249.28
target_interact_id=2147515597
target_dist=3.56
target_pos=493.15,2298.88,248.42
dialog_type=select_quest
content_id=10
action_hint=dialog_click_x child_index=6 x=25
```

Recorded operation:

```text
1. Quest 20612 requires level 11. After quest 20611 finishes, keep task-local stationary grind active until character level is at least 11.
2. Move to 477.137,2304.421,250.734 first.
3. Mark the recorded start point as reached, then move toward the NPC position 493.15,2298.88,248.42 until within normal NPC interaction range.
4. Open NPC dialog for interact_id=2147515597.
5. When dialog type=select_quest/content_id=10, run the stage-local continuous x=25 click helper.
6. Mark only stage quest_20612_start_npc complete after the continuous click finishes.
7. Open the current tracked quest from the right-side quest tracker.
8. After the current quest panel is visible, call `QuestTeleport(20612)` or the current tracked quest id and wait for a position change.
```

Do not fall through to later level-blocked missions while quest `20612` is active or level-blocked and this step is incomplete.

Observed on 2026-06-11: after the continuous start dialog click, the live quest snapshot can become:

```text
q20612 status_code=4 req_count=0
q20613 status_code=6 lv_num=14
char_level=11
```

This is still the post-20612 task teleport step. Do not start level grinding for `20613` immediately. Open the current right-side tracker and call the current tracked quest teleport first. In this state the actual `QuestTeleport` quest id can be `20613`, while the local stage remains `quest_20612_task_teleport` so the post-dialog teleport gate is completed before any later grind.

## Task Teleport

After the start NPC dialog completes, the right-side quest tracker shows the next quest objective. Use the same current-tracker flow as the 20611 target teleport:

```text
ClickUiControl parent=quest_indicator_dialog name=prototype stage=quest_20611_indicator_title
QuestTeleport(20612 or current tracked quest, observed 20613) stage=quest_20612_task_teleport
WaitPositionChanged / CompleteQuestTeleport stage=quest_20612_task_teleport
```

The shared `quest_20611_indicator_title` stage name is historical; for this step the action params carry `quest_id=20612` and the teleport stage is `quest_20612_task_teleport`.
