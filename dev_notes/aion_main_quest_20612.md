# Aion 主线任务 20612 笔记

## 等级门槛

20612 需要 11 级。20611 热点奖励完成后，如果角色未到 11 级，继续任务专用定点挂机，到 11 级后再进入 20612 起始流程。

## 起始 NPC

F11 记录：

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

执行顺序：

1. 先移动到记录起点 `477.137,2304.421,250.734`。
2. 再靠近 NPC `493.15,2298.88,248.42`。
3. 打开 `interact_id=2147515597` 对话。
4. `select_quest/content_id=10` 用本阶段连续 `x=25` 点击。
5. 连续点击完成后只标记 `quest_20612_start_npc` 完成。
6. 打开右侧当前追踪任务。
7. 当前任务面板可见后调用任务传送。

## 起始后任务传送

20612 起始对话后，任务快照可能变成：

```text
20612 status_code=4 req_count=0
20613 status_code=6 lv_num=14
char_level=11
```

这仍然是 20612 的“起始后任务传送”阶段，不要马上开始 20613 等级挂机。

执行：

```text
ClickUiControl parent=quest_indicator_dialog name=prototype stage=quest_20611_indicator_title
QuestTeleport(20612 或当前追踪任务 id，实测可能是 20613)
WaitPositionChanged / CompleteQuestTeleport stage=quest_20612_task_teleport
```

虽然打开右侧任务行的 stage 名仍叫 `quest_20611_indicator_title`，这里实际业务阶段是 `quest_20612_task_teleport`。

## 奖励与交接

传送完成后走 20612 奖励 NPC 阶段。奖励完成后才允许进入 20613 的 14 级门槛。

硬规则：

1. 20612 活跃或等级限制阶段未完成时，不要穿透到后续主线。
2. 对话打开后优先处理当前对话，未知页先 dump。
3. 任务传送必须在任务面板打开后执行。
