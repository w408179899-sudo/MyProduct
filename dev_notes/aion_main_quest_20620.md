# Aion 主线任务 20620 记录

## 当前已录制流程

20620 在 20615 完成并从任务列表消失后出现。

整体顺序：

```text
1. quest_20620_start_npc
2. quest_20620_task_teleport
3. quest_20620_after_teleport_npc
4. quest_20620_socket_stigma
5. quest_20620_after_stigma_teleport
6. quest_20620_after_stigma_npc
7. quest_20620_obelisk
8. quest_20620_after_obelisk_teleport
```

后续普通 NPC 对话默认使用 `ClickDialogLastContinuousOk`。

## 阶段一：起始 NPC 对话

阶段名：

```text
quest_20620_start_npc
```

F11 快照：

```text
quest_id=20620
status_code=3
req_count=0
big_map_id=220020000
map=Morheim / DF2_SZ_OP
char_pos=224.60,2416.30,454.56
target=아에기르
target_id=65520
target_interact_id=2147488159
target_pos=224.83,2415.82,454.11
target_dist=0.70
dialog=closed
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
completed_20620_after_teleport_npc_dialog~=true
completed_20620_stigma_socket~=true
completed_20620_after_stigma_teleport~=true
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

之后只等待坐标变化，不重复 call。落地后完成标记：

```text
completed_20620_task_teleport=true
```

## 阶段三：传送落地后 NPC 对话

阶段名：

```text
quest_20620_after_teleport_npc
```

F11 快照：

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

## 阶段四：后台镶嵌烙印

阶段名：

```text
quest_20620_socket_stigma
```

需求：

```text
不要人工右键，不走前台鼠标。
优先使用后台 API：inventory.list() 找到烙印石 item.id，然后 inventory.useItem(item.id)。
```

触发条件：

```text
completed_20620_after_teleport_npc_dialog=true
completed_20620_stigma_socket~=true
```

物品识别：

```text
优先关键词：파멸의 방패
兜底关键词：스티그마 / Stigma / 烙印
```

执行方式：

```text
UseQuestStigmaStone stage=quest_20620_socket_stigma
inventory.list() -> 找 item.id
inventory.useItem(item.id)
```

完成标记：

```text
completed_20620_stigma_socket=true
```

如果背包 API 里找不到烙印石，会持续等待并在日志里输出库存数量和匹配关键词；不使用 UI 坐标右键作为默认路径。

## 阶段五：烙印后任务传送

阶段名：

```text
quest_20620_after_stigma_teleport
```

触发条件：

```text
completed_20620_stigma_socket=true
completed_20620_after_stigma_teleport~=true
```

执行方式：

```text
QuestTeleport quest_id=20620 stage=quest_20620_after_stigma_teleport
direct_quest_id_only=true
wait_teleport=true
```

这里同样只按 `quest_id=20620` call 任务传送，避免当前同时有 20621 等其他任务时传错。

传送 call 成功后：

```text
waiting_teleport=true
teleport_quest_id=20620
teleport_stage=quest_20620_after_stigma_teleport
```

之后只等待坐标变化，不重复 call。落地后完成标记：

```text
completed_20620_after_stigma_teleport=true
```

## 阶段六：烙印后传送落地 NPC 对话

阶段名：

```text
quest_20620_after_stigma_npc
```

F11 快照：

```text
quest_id=20620
status_code=3
req_count=3
big_map_id=220020000
char_pos=268.68,2339.90,443.74
target_id=65416
target_interact_id=2147515902
target_pos=269.42,2337.65,443.74
target_dist=2.37
dialog=closed
```

NPC：

```text
name_key=MQ20620_NPC_003_AFTER_STIGMA
interact_id=2147515902
x=269.42
y=2337.65
z=443.74
big_map_id=220020000
```

触发条件：

```text
quest_id=20620
req_count=3
completed_20620_after_stigma_teleport=true
completed_20620_after_stigma_npc_dialog~=true
```

如果运行时传送完成标记丢失，但当前角色已经在这个 NPC 附近或已打开这个 NPC 对话，也直接进入本阶段，避免回退执行旧传送。

执行顺序：

```text
InteractNpc stage=quest_20620_after_stigma_npc interact_id=2147515902
ClickDialogLastContinuousOk stage=quest_20620_after_stigma_npc
```

完成标记：

```text
completed_20620_after_stigma_npc_dialog=true
```

## 阶段七：立复活点

阶段名：

```text
quest_20620_obelisk
```

F11 快照：

```text
quest_id=20620
status_code=3
req_count=4
big_map_id=220020000
char_pos=269.49,2340.11,443.74
target_id=50
target_interact_id=2147499094
target_pos=268.00,2338.62,443.75
target_dist=2.11
dialog=closed
```

目标：

```text
name_key=MQ20620_NPC_004_OBELISK
interact_id=2147499094
x=268.00
y=2338.62
z=443.75
big_map_id=220020000
```

触发条件：

```text
quest_id=20620
req_count=4
completed_20620_after_stigma_npc_dialog=true
completed_20620_obelisk~=true
```

执行方式仿照 20611 主线立复活点：

```text
InteractNpc stage=quest_20620_obelisk interact_id=2147499094
ClickObeliskConfirm stage=quest_20620_obelisk
```

交互说明：

```text
NPC 名字在 F11 中是乱码，不依赖名字。
InteractNpc 使用 allow_interact_id_fallback=true，通过 interact_id 打开复活点确认弹窗。
确认弹窗复用已有 ClickObeliskConfirm 逻辑。
```

完成标记：

```text
completed_20620_obelisk=true
```

## 阶段八：立复活点后任务传送

阶段名：

```text
quest_20620_after_obelisk_teleport
```

触发条件：

```text
completed_20620_obelisk=true
completed_20620_after_obelisk_teleport~=true
```

如果运行时标记丢失，但 `quest_id=20620` 的任务步数已经大于 4，也直接进入本阶段，避免回退执行前面的 NPC 或复活点步骤。

执行方式：

```text
QuestTeleport quest_id=20620 stage=quest_20620_after_obelisk_teleport
direct_quest_id_only=true
wait_teleport=true
```

这里继续只按 `quest_id=20620` call 任务传送，不依赖当前追踪面板，避免同时有 20621 等任务时传错。

传送 call 成功后：

```text
waiting_teleport=true
teleport_quest_id=20620
teleport_stage=quest_20620_after_obelisk_teleport
```

之后只等待坐标变化，不重复 call。落地后完成标记：

```text
completed_20620_after_obelisk_teleport=true
```

## 防回退约束

```text
如果 completed_20620_after_teleport_npc_dialog=true、
completed_20620_stigma_socket=true
completed_20620_after_stigma_teleport=true
completed_20620_after_stigma_npc_dialog=true
completed_20620_obelisk=true
或 completed_20620_after_obelisk_teleport=true，
即使 completed_20620_task_teleport 标记丢失，也不能回退执行 quest_20620_task_teleport。
```
