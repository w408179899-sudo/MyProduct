# Aion 主线任务 20614 笔记

## 等级门槛

20614 需要 17 级。20613 奖励完成后，先沿记录路线移动到挂机点，然后启动任务专用定点挂机，到 17 级停止。

阶段名：

```text
quest_20614_level17_grind
```

挂机终点：

```text
851.740, 1738.932, 261.266
```

路线移动必须使用主线专用丝滑路线参数，不影响普通打怪路线逻辑。

## 第一次任务传送

到 17 级后，打开右侧当前追踪任务，然后执行 20614 任务传送：

```text
ClickUiControl parent=quest_indicator_dialog name=prototype stage=quest_20611_indicator_title
QuestTeleport(20614) stage=quest_20614_task_teleport
WaitPositionChanged / CompleteQuestTeleport stage=quest_20614_task_teleport
```

完成后设置 `completed_20614_task_teleport=true`。

## 起始 NPC：미요우

落地后 F11：

```text
quest_id=20614
status_code=3
req_count=0
target=미요우
target_interact_id=2147507242
target_pos=946.25,1702.77,259.62
dialog_type=select_quest
content_id=10
action_hint=dialog_click_x child_index=6 x=25
```

NPC 匹配优先用名字 `미요우`，interact id 只做兜底。

执行：

```text
InteractNpc npc_name=미요우 interact_id=2147507242 stage=quest_20614_start_npc
ClickDialogXContinuous click_x=25 content_id=10 type_text=select_quest
```

完成后设置 `completed_20614_start_dialog=true`。

## 第二次任务传送

起始 NPC 对话完成后，按顺序打开右侧当前追踪任务并执行任务传送。

注意：实测这一步任务状态可能是 `status_code=3`，也可能已经变成 `status_code=4`。两种都允许继续执行传送，不要因为已完成状态就跳过。

```text
ClickUiControl parent=quest_indicator_dialog name=prototype stage=quest_20611_indicator_title
QuestTeleport(20614) stage=quest_20614_after_start_teleport
WaitPositionChanged / CompleteQuestTeleport stage=quest_20614_after_start_teleport
```

完成后设置 `completed_20614_after_start_teleport=true`。

## 奖励 NPC：드발린

第二次传送落地后 F11：

```text
quest_id=20614
status_code=4
req_count=0
map=오델라 재배지
char_pos=600.78,1480.36,299.94
target=드발린
target_interact_id=2147511075
target_pos=602.85,1480.65,299.79
dialog_type=select_success
content_id=10002
next=HACTION_SELECT_QUEST_REWARD
```

NPC 匹配优先用名字 `드발린`，interact id 只做兜底。

执行：

```text
InteractNpc npc_name=드발린 interact_id=2147511075 stage=quest_20614_reward_npc
ClickDialogXContinuous click_x=25 content_id=10002 type_text=select_success
```

完成后设置 `completed_20614_reward_dialog=true`，然后等待 20615 的 20 级门槛流程。
