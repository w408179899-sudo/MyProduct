# Aion 主线任务 20620 记录

## 当前已录制阶段

20620 在 20615 完成并从任务列表消失后出现。

F11 快照信息：

```text
quest_id=20620
status_code=3
req_count=0
big_map_id=220020000
map=Morheim / DF2_SZ_OP
char_pos=224.60,2416.30,454.56
target=AEGIR
target_id=65520
target_interact_id=2147488159
target_pos=224.83,2415.82,454.11
target_dist=0.70
dialog=closed
```

## 阶段一：起始 NPC 对话

阶段名：

```text
quest_20620_start_npc
```

NPC：

```text
name_key=MQ20620_NPC_001_START_AEGIR
interact_id=2147488159
x=224.83
y=2415.82
z=454.11
big_map_id=220020000
```

执行顺序：

```text
InteractNpc stage=quest_20620_start_npc interact_id=2147488159
ClickDialogLastContinuousOk stage=quest_20620_start_npc
```

对话约定：

后续 20620 的普通 NPC 对话默认使用 `ClickDialogLastContinuousOk`，除非新的 F11 记录证明需要换方式。

你说下面这些词时，都按这个方法处理：

```text
最新 call 对话条方式
点最后一条连续 call
连续最后一个，最后 OK 兜底
```

完成标记：

```text
completed_20620_start_dialog=true
```

## 阶段二：任务传送

阶段名：

```text
quest_20620_task_teleport
```

触发条件：

```text
completed_20620_start_dialog=true
completed_20620_task_teleport~=true
```

执行方式：

```text
QuestTeleport quest_id=20620 stage=quest_20620_task_teleport
direct_quest_id_only=true
wait_teleport=true
```

这里直接按 `quest_id=20620` call 任务传送，不依赖当前追踪面板，避免同时存在多个任务时传错。

传送 call 成功后：

```text
waiting_teleport=true
teleport_quest_id=20620
teleport_stage=quest_20620_task_teleport
```

之后只等待坐标变化，不重复 call。

落地后完成标记：

```text
completed_20620_task_teleport=true
```

完成这个标记后进入阶段三，和传送落地后的 NPC 对话。

## 阶段三：传送落地后的 NPC 对话

F11 快照信息：

```text
quest_id=20620
status_code=3
req_count=1
big_map_id=220020000
char_pos=233.55,2324.88,446.17
target_id=65526
target_interact_id=2147511717
target_pos=234.21,2321.90,446.32
target_dist=3.06
dialog=closed
```

阶段名：

```text
quest_20620_after_teleport_npc
```

NPC：

```text
name_key=MQ20620_NPC_002_AFTER_TELEPORT
interact_id=2147511717
x=234.21
y=2321.90
z=446.32
big_map_id=220020000
```

触发条件：

```text
quest_id=20620
req_count=1
completed_20620_task_teleport=true
```

如果运行时标记丢失，但当前角色已经在这个 NPC 附近，也直接进入这个对话阶段，避免重复 call 任务传送。

执行顺序：

```text
InteractNpc stage=quest_20620_after_teleport_npc interact_id=2147511717
ClickDialogLastContinuousOk stage=quest_20620_after_teleport_npc
```

完成标记：

```text
completed_20620_after_teleport_npc_dialog=true
```

完成这个标记后，20620 暂停等待下一次 F11 录制。
