# Aion 主线任务 20621 记录

## 当前已录制流程

20621 在 20620 完成后进入等级门槛阶段，目前已实现五步：

```text
1. quest_20621_level22_grind
2. quest_20621_task_teleport
3. quest_20621_after_teleport_npc
4. quest_20621_after_dialog_teleport
5. quest_20621_after_dialog_teleport_npc
```

## 阶段一：固定点打怪升到 22 级

阶段名：

```text
quest_20621_level22_grind
```

固定打怪点：

```text
x=174.508
y=2298.396
z=438.510
big_map_id=220020000
```

触发条件：

```text
quest_id=20621
status_code=6
lv_num=22
completed_20620_after_obelisk_npc_dialog=true
```

如果 20620 已经从任务列表消失，也允许进入本阶段。

执行顺序：

```text
NavigateToGrindPoint stage=quest_20621_level22_grind
StartStationaryGrind stage=quest_20621_level22_grind until_level=22
WaitLevelGrind stage=quest_20621_level22_grind
```

练级期间如果出现已完成的被动蓝色任务：

```text
tab=1
status_code=4
```

会走后台 `SubmitBlueQuest` 提交，不打开 UI，不停止当前练级流程。

## 阶段二：达到 22 级后任务传送

阶段名：

```text
quest_20621_task_teleport
```

触发条件：

```text
char_level >= 22
completed_20621_task_teleport ~= true
```

执行动作：

```text
QuestTeleport quest_id=20621 stage=quest_20621_task_teleport
```

本阶段使用后台任务传送 call：

```text
direct_quest_id_only=true
wait_teleport=true
```

传送完成后记录：

```text
completed_20621_task_teleport=true
```

落地后进入下一阶段：

```text
quest_20621_after_teleport_npc
```

## 阶段三：传送落地后和 NPC 对话

阶段名：

```text
quest_20621_after_teleport_npc
```

F11 记录：

```text
quest_id=20621
step=0
map_id=220020000
char_pos=193.78,2267.62,439.01
target_interact_id=2147535533
target_dist=1.18
target_pos=193.00,2268.50,439.12
dialog=closed
```

触发条件：

```text
completed_20621_task_teleport=true
```

如果重启后 runtime 里没有传送完成标记，但角色已经在该 NPC 附近，也直接进入本阶段，避免重复 call 任务传送。

执行动作：

```text
InteractNpc quest_id=20621 stage=quest_20621_after_teleport_npc
```

打开对话后使用统一的最后一条连续 OK 对话方式：

```text
ClickDialogLastContinuousOk
```

完成后记录：

```text
completed_20621_after_teleport_npc_dialog=true
```

注意：本阶段完成后不会自动进入 20622 打怪练级，而是先进入 20621 的下一次任务传送。即使任务列表里已经出现：

```text
quest_id=20622
status_code=6
lv_num=25
```

也必须优先走 `quest_20621_after_dialog_teleport`，不能落到通用 20622 等级练级分支。

如果脚本重启导致 runtime 标记丢失，但当前状态满足：

```text
20621 已不在任务列表或已完成
20622 status_code=6
20622 lv_num=25
角色等级 >= 22
角色仍在 20621 对话 NPC 附近
```

也恢复为 `quest_20621_after_dialog_teleport`，直接执行 20621 任务传送。

## 阶段四：20621 对话完成后的任务传送

阶段名：

```text
quest_20621_after_dialog_teleport
```

触发条件：

```text
completed_20621_after_teleport_npc_dialog=true
completed_20621_after_dialog_teleport ~= true
```

执行动作：

```text
QuestTeleport quest_id=20621 stage=quest_20621_after_dialog_teleport
```

本阶段使用后台任务传送 call：

```text
direct_quest_id_only=true
wait_teleport=true
```

传送完成后记录：

```text
completed_20621_after_dialog_teleport=true
```

传送完成后进入下一阶段：

```text
quest_20621_after_dialog_teleport_npc
```

## 阶段五：第二次任务传送落地后和 NPC 对话

阶段名：

```text
quest_20621_after_dialog_teleport_npc
```

F11 记录：

```text
quest_id=20621
step=0
map_id=220020000
char_pos=417.70,1850.90,441.97
target=할프단
target_interact_id=2147520888
target_dist=4.17
target_pos=414.75,1848.00,442.53
dialog=closed
```

触发条件：

```text
completed_20621_after_dialog_teleport=true
completed_20621_after_dialog_teleport_npc_dialog ~= true
```

执行动作：

```text
InteractNpc quest_id=20621 stage=quest_20621_after_dialog_teleport_npc npc_name=할프단
```

本阶段 NPC 打开对话使用名字匹配：

```text
npc_name=할프단
allow_interact_id_fallback=false
```

`target_interact_id=2147520888` 只保留为记录和日志字段，不作为后台交互兜底。

打开对话后使用统一的最后一条连续 OK 对话方式：

```text
ClickDialogLastContinuousOk
```

完成后记录：

```text
completed_20621_after_dialog_teleport_npc_dialog=true
```

完成后停在：

```text
Idle stage=quest_20621_after_dialog_teleport_npc
```

等待下一步 F11 录制。
