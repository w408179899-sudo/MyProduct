# Aion 主线任务 20615 笔记

## 当前已实现步骤

20615 当前已实现三段：

1. 20614 奖励完成后，走到记录终点并启动任务专用定点挂机，到 20 级停止。
2. 任务快照变成 `20615 status_code=3 req_count=0` 后，打开右侧当前追踪任务并执行任务传送。
3. 任务传送落地后，和 `울고른` 对话；对话采用连续点最后一条，最后 OK 兜底。

阶段名：

```text
quest_20615_level20_grind
```

路线名：

```text
main_quest_20615_level20_grind
```

终点：

```text
666.227, 1535.341, 294.009
```

## 进入条件

必须满足以下条件之一：

```text
20615 status_code=6
或当前正在 quest_20615_level20_grind 阶段挂机
```

并且 20614 已经清掉：

```text
completed_20614_reward_dialog=true
或任务列表里已经看不到 20614
```

这样可以避免 20614 奖励还没点完就提前跑去挂机。

## 20 级后的任务传送

F11 快照：

```text
character=HiBroHiI
level=20
map=오델라 재배지
big_map_id=220010000
quest_id=20615
status_code=3
req_count=0
char_pos=665.95,1536.75,294.27
dialog=closed
target=no_target
```

执行顺序：

```text
ClickUiControl parent=quest_indicator_dialog name=prototype stage=quest_20611_indicator_title
QuestTeleport(20615) stage=quest_20615_task_teleport
WaitPositionChanged / CompleteQuestTeleport stage=quest_20615_task_teleport
```

说明：

1. 先打开右侧当前追踪任务，不直接按固定坐标。
2. 面板可见后才调用 `QuestTeleport(20615)`。
3. 调用 20615 传送前，停止 `quest_20615_level20_grind` 等级挂机。
4. 传送完成后设置 `completed_20615_task_teleport=true`，进入 `quest_20615_target_npc`。

## 落地后的 NPC 对话

F11 快照：

```text
character=HiBroHiI
level=20
map=알데르 마을
big_map_id=220010000
quest_id=20615
status_code=3
req_count=0
char_pos=587.72,2451.15,278.38
target=울고른
target_interact_id=2147520815
target_dist=1.90
target_pos=589.35,2450.16,278.38
dialog=closed
```

阶段名：

```text
quest_20615_target_npc
```

执行顺序：

```text
InteractNpc interact_id=2147520815 stage=quest_20615_target_npc
ClickDialogLastContinuousOk stage=quest_20615_target_npc
```

说明：
1. NPC 是 `울고른`，和 20611 target NPC 共用 `interact_id=2147520815`，但 20615 必须走独立阶段，不能按 20611 target dialog 完成。
2. 打开对话后连续调用“点击最后一条”。
3. 如果已经没有可点选项或达到点击上限，点 `ok` 兜底。
4. 完成后设置 `completed_20615_target_dialog=true`，然后等待下一步 F11。

## 等级挂机执行顺序

1. 如果当前有 NPC 对话打开，先等待对话关闭。
2. 读取角色等级，目标等级取任务 `lv_num`，兜底为 20。
3. 如果角色已经 20 级或更高，返回 `Idle`，停止本阶段挂机，等待下一步。
4. 如果当前地图不是 `220010000`，返回 `Idle`，不乱跑。
5. 如果本阶段挂机已经启动，返回 `WaitLevelGrind`。
6. 如果路线还在执行，返回 `WaitRouteComplete`。
7. 如果角色离终点超过范围，返回 `FollowRoute`，使用下面完整路线。
8. 到终点后返回 `StartStationaryGrind`，`until_level=20`。

## 路线

```text
600.922, 1485.689, 298.540
602.456, 1486.625, 297.866
604.295, 1487.749, 297.296
606.092, 1488.846, 296.425
607.941, 1489.975, 295.524
609.696, 1491.047, 295.127
611.505, 1492.097, 294.718
613.347, 1493.034, 294.247
615.190, 1493.917, 293.965
617.044, 1494.718, 293.332
618.943, 1495.536, 292.639
620.901, 1496.380, 292.332
622.898, 1497.081, 292.250
624.863, 1497.722, 292.250
626.852, 1498.371, 292.130
628.863, 1499.028, 291.974
630.829, 1499.670, 291.928
632.794, 1500.312, 291.825
634.771, 1500.958, 291.702
636.748, 1501.603, 291.578
638.751, 1502.257, 291.453
640.708, 1502.896, 291.331
642.640, 1503.526, 291.210
644.630, 1504.176, 291.086
646.594, 1504.817, 290.948
648.431, 1505.695, 290.742
650.326, 1506.717, 290.625
652.170, 1507.711, 290.625
654.068, 1508.734, 290.621
655.868, 1509.705, 290.508
657.724, 1510.726, 290.506
659.333, 1511.926, 290.500
660.604, 1513.667, 290.462
661.675, 1515.379, 290.309
662.690, 1517.260, 290.423
663.408, 1519.159, 290.645
664.040, 1521.157, 290.967
664.495, 1523.237, 291.570
664.821, 1525.330, 292.288
665.079, 1527.469, 292.681
665.379, 1529.528, 292.845
665.679, 1531.586, 293.272
665.987, 1533.694, 293.692
666.227, 1535.341, 294.009
```

## 路线参数

```text
quest_20615_level20_grind_point_range=3
quest_20615_route_waypoint_radius=6
quest_20615_route_final_radius=2.5
quest_20615_route_resend_interval=0.5
main_quest_smooth_route=true
```

这套参数只挂在 20615 主线阶段，不改普通打怪移动逻辑。
