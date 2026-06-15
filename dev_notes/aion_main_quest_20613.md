# Aion 主线任务 20613 笔记

## 第一步：当前追踪任务传送

20612 后的 14 级挂机完成后，F11 记录：

```text
character_level=14
big_map_id=220010000
quest_id=20613
status_code=3
req_count=0
seq=3
lv_num=14
```

执行：

```text
ClickUiControl parent=quest_indicator_dialog name=prototype stage=quest_20611_indicator_title
QuestTeleport(20613) stage=quest_20613_task_teleport
WaitPositionChanged / CompleteQuestTeleport stage=quest_20613_task_teleport
```

完成后设置 `completed_20613_task_teleport=true`。

## 第二步：起始 NPC 对话

落地后 F11：

```text
quest_id=20613
status_code=3
req_count=0
char_pos=1048.52,2198.80,262.33
target_interact_id=2147495609
target_dist=3.22
target_pos=1050.70,2201.12,262.81
dialog_type=select_quest
content_id=10
```

执行：

```text
NavigateToNpc / InteractNpc interact_id=2147495609 stage=quest_20613_start_npc
ClickDialogXContinuous click_x=25 content_id=10 type_text=select_quest
```

这个 NPC 和 20612 奖励 NPC 共享位置和 id，但必须用 `quest_id=20613`、`stage=quest_20613_start_npc`、`dialog_type=select_quest` 区分。

如果脚本重启后角色已经在 NPC 附近，直接继续起始 NPC 对话，不要重新执行第一次传送。

完成后设置 `completed_20613_start_dialog=true`。

## 第三步：起始后传送

起始 NPC 对话关闭后：

```text
ClickUiControl parent=quest_indicator_dialog name=prototype stage=quest_20611_indicator_title
QuestTeleport(20613) stage=quest_20613_after_start_teleport
WaitPositionChanged / CompleteQuestTeleport stage=quest_20613_after_start_teleport
```

完成后设置 `completed_20613_after_start_teleport=true`。

## 第四步：落地奖励 NPC

第二次传送落地后 F11：

```text
quest_id=20613
status_code=4
req_count=0
char_pos=944.00,1701.69,259.66
target=미요우
target_interact_id=2147492704
target_dist=2.50
target_pos=946.25,1702.77,259.62
dialog_type=select_success
content_id=10002
next=HACTION_SELECT_QUEST_REWARD
```

执行：

```text
NavigateToNpc / InteractNpc interact_id=2147492704 stage=quest_20613_after_start_reward_npc
ClickDialogXContinuous click_x=25 content_id=10002 type_text=select_success
```

完成后设置：

```text
completed_20613_after_start_teleport=true
completed_20613_after_start_reward_dialog=true
```

之后等待 20614 的 17 级门槛流程。
