# Aion 主线任务 20621 记录

## 当前已录制流程

20621 在 20620 完成后进入等级门槛阶段，目前已实现两步：

```text
1. quest_20621_level22_grind
2. quest_20621_task_teleport
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

落地后暂时停在：

```text
Idle stage=quest_20621_task_teleport
```

等待下一步 F11 录制。
