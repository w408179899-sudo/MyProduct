# Aion 主线任务 20621 记录

## 当前已录制流程

20621 在 20620 完成后进入等级门槛阶段。

当前只实现第一步：

```text
1. quest_20621_level22_grind
```

到 22 级后不自动点任务传送，等待下一步指令。

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

到达 22 级后：

```text
Idle stage=quest_20621_level22_grind
```

注意：本阶段到 22 级后只停在 Idle，等待下一步录制；不自动执行 20621 任务传送。
