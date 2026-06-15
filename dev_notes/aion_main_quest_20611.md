# Aion 主线任务 20611 笔记

## 等级门槛

20611 是 8 级黄色主线。

角色等级低于任务 `lv_num` 时，保持任务专用定点挂机。到 8 级后先打开当前追踪任务详情，再调用：

```text
QuestTeleport(20611)
```

传送后设置 `waiting_teleport=true` 和 `teleport_stage=quest_20611_level_move`，通过坐标变化或地图变化确认完成，再设置 `completed_20611_level_move=true`。

## 使命 NPC

传送后记录：

```text
quest_id=20611
status_code=3
req_count=0
big_map_id=220010000
interact_id=2147503111
npc_pos=586.22,2465.17,278.58
```

使用 `MQ20611_NPC_001_MISSION` 名称 key。

对话链：

```text
select_quest content_id=10 quest_id=0 -> 点击任务项 x=25,y=324
select1 content_id=1011 -> 点击 x=25
select1_1 content_id=1012 -> 点击 x=25
select1_1_1 content_id=1013 -> 点击 x=25
select1_1_1_1 content_id=1014 -> 点击 x=25，标记使命对话完成
```

未知页面先 dump，不要重复打开 NPC。

## 方尖碑步骤

记录：

```text
quest_id=20611
status_code=3
req_count=1
interact_id=2147505051
npc_pos=587.69,2467.10,278.79
```

使用 `MQ20611_NPC_002_OBELISK` 名称 key。优先按名称交互，id 只做兜底。

交互后不是普通 `dlg_dialog`，而是确认弹窗。点击可见未命名 popup root 下的 `ok`，必要时用 Enter 兜底。不要因为按钮点击返回 true 就永久标记完成，必须等下一次任务快照推进。

## 当前追踪任务传送

方尖碑确认后：

```text
quest_id=20611
status_code=3
req_count=2
```

必须先点击右侧任务追踪行打开当前任务，不要直接按 `J`：

```text
ClickUiControl parent=quest_indicator_dialog name=prototype stage=quest_20611_indicator_title
QuestTeleport(20611) stage=quest_20611_target_teleport
WaitPositionChanged / CompleteQuestTeleport stage=quest_20611_target_teleport
```

`quest_20611_indicator_title` 是历史 stage 名，实际点击的是右侧当前任务行。

不要把旧的 `dictionary_dialog.teleport_to_npc` 当作默认入口。它可能是残留窗口。

## 目标 NPC

追踪任务传送后：

```text
quest_id=20611
status_code=3
req_count=2
interact_id=2147520815
npc_pos=589.35,2450.16,278.38
dialog type=select_quest
content_id=10
```

使用 `MQ20611_NPC_003_TARGET` 名称 key。

这个 NPC 用连续 `x=25` 点击：

```text
ClickDialogXContinuous stage=quest_20611_target_npc click_x=25
```

连续 x 点击只用于明确记录过的线性对话阶段，不要全局套用。

## 地图节点传送

目标 NPC 对话结束后会打开地图。使用地图节点 API，不点固定屏幕坐标：

```text
AionData.GetMapNodeList(big_map_id)
node.name_en == HOTSPOT_DF1_04
node_id=66
price=0
AionData.NodeTeleport(node_id=66, price=0)
```

等待坐标变化后设置 `completed_20611_hotspot_teleport=true`。

## 热点奖励 NPC

落地后不要直接进入下一个等级挂机，先交 20611：

```text
stage=quest_20611_hotspot_reward_npc
target_name=볼리크
quest_id=20611
status_code=4
req_count=3
interact_id=2147515597
npc_pos=493.15,2298.88,248.42
dialog type=select_success
content_id=10002
```

打开 NPC 后执行：

```text
ClickDialogXContinuous stage=quest_20611_hotspot_reward_npc click_x=25
```

完成后设置 `completed_20611_hotspot_reward=true`，等待下一步。
