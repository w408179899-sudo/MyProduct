# Aion 交互 ID 与重启对比笔记

这个目录是开发笔记，用来记录 F6 诊断时发现的稳定 ID 和重启后会变化的运行时字段。
保留进源码，方便后续实现主线任务；不要打进运行包或发布包。

## 潘德莫尼姆近卫兵

NPC 名称：

```text
판데모니움 근위병
```

已确认比较稳定的字段：

```text
interact_id=2147533452
kind=NPC
tag=NPC
type=2
ct=38
hp=14614/14614
level=50
pos=1655.94,1400.75,194.67
rating=3
flags=1108
```

结论：

- `interact_id=2147533452` 可以作为后续任务配置里的稳定交互 ID 使用。
- 这个值仍然需要在游戏版本或数据更新后重新按 F6 校验。
- `obj`、`current_target_obj`、`id` 都是运行时字段，不要作为长期配置。
- F6 多次样本里 `GetCurrentTarget()` 的 `obj` 和 `GetAroundList()` 实体 `obj` 不一致，当前是靠 `id` 匹配到实体详情；做任务逻辑时优先使用 `interact_id`、名称、位置、类型，不要优先使用 `obj`。

## 主线任务：명암의 찰나

当前角色：

```text
character=HiBroHi
level=1
race=1/魔族
gender=1/女性
job=2
map=판데모니움 입구
big_map_id=120030000
```

任务信息：

```text
qid_hex=506E
quest_id=20590
name=명암의 찰나
tab_name=使命
status_code=3
status_name=正在进行中
req_count=0
lv_text=1레벨
lv_num=1
exp_reward=5810
kinah=0
```

说明：

- `[QuestInfo] sample qid=506E` 对应 `GetQuestList()` 里的 `quest_id=20590`，十六进制 `0x506E = 20590`。
- 这个任务当前是角色 `HiBroHi` 的已接主线任务，`req_count=0` 可先记录为当前主线步骤号。
- `quest_addr`、`elems` 是任务结构运行时地址，会随重启变化，不要做持久配置。

## NPC 对话链路：명암의 찰나

入口 NPC：

```text
npc=판데모니움 근위병
interact_id=2147533452
quest_id=20590
```

任务列表页：

```text
npc_dialog_id=2147533452
content_id=10
quest_id=0
type=select_quest
next=
```

说明：

- `type=select_quest` 是任务选择列表页，还没有进入具体任务。
- 这一页 `quest_id=0` 是正常的，不能直接当任务提交参数。
- 可见的无名子控件 `x=25 y=196` 像任务列表条目；点击后进入具体任务页。

具体任务第一页：

```text
npc_dialog_id=2147533452
content_id=1011
quest_id=20590
type=select1
next=HACTION_SELECT1_1
```

说明：

- 已进入主线 `quest_id=20590` 的任务对话页。
- `content_id` 从 `10` 变成 `1011`，说明对话内容阶段已推进。
- `type=select1` 和 `next=HACTION_SELECT1_1` 可作为当前对话阶段标识。
- 这一页按钮 `accept/ok/refuse/cancel` 仍然不可见，说明还不是最终接受或确认页。
- 可见的无名子控件从 `x=25 y=196` 变成 `x=25 y=221`，像下一段对话选项；继续点击它后再按 F8 观察下一页。

具体任务第二页：

```text
npc_dialog_id=2147533452
content_id=1012
quest_id=20590
type=select1_1
next=HACTION_SELECT1_1_1
```

说明：

- 仍然在主线 `quest_id=20590` 的连续对话链里。
- `content_id` 从 `1011` 推进到 `1012`。
- `type` 从 `select1` 推进到 `select1_1`，`next` 从 `HACTION_SELECT1_1` 推进到 `HACTION_SELECT1_1_1`。
- `accept/ok/refuse/cancel` 仍然不可见，还不是最终接受或确认页。
- 可见的无名子控件位置变成 `x=25 y=282`，这是最后一个对话选项。
- 点击最后一个对话选项后没有接受/确认按钮，会直接传送到另一个位置。

当前自动化逻辑：

```text
quest_id=20590 active
1. 如果角色距离 NPC 超过 4 米，先 MoveTo 到 NPC 固定坐标。
2. 到达 NPC 附近后 InteractNpc(2147533452) 打开对话。
3. type=select_quest/content_id=10 时点击 x=25 的任务条目。
4. type=select1/content_id=1011 时点击 x=25 的继续选项。
5. type=select1_1/content_id=1012 时点击 x=25 的最后选项，并进入等待传送状态。
6. 检测 big_map_id 变化或当前位置相对点击前变化超过 20 米后，认为首个 NPC 对话步骤完成。
```

## 传送后位置：명암의 성채 내성

首个 NPC 最后一段对话点击后，会传送到这个地图。

地图与角色：

```text
character=HiBroHi
level=1
map=명암의 성채 내성
big_map_id=390010000
name_en=IDTransform_SZ_B_01
character_pos=507.64,594.73,322.56
hp=30820/30820
mp=30230/30230
```

当前主线状态：

```text
quest_id=20590
name=명암의 찰나
tab_name=使命
status_code=3
status_name=正在进行中
req_count=2
lv_num=1
```

说明：

- `req_count` 从入口地图的 `0` 变成 `2`，可作为传送后阶段标识。
- `quest_id=20590` 没变，说明仍然是同一条主线 `명암의 찰나`。
- `quest_addr` / `elems` 仍然是运行时地址，不做配置依据。

传送后目标 NPC：

```text
name=시간의 데바 잉그릴
kind=NPC
tag=NPC
type=2
ct=38
id=65497
interact_id=2424368065
level=5
hp=3871652/3871652
pos=522.68,573.38,322.03
rating=0
flags=1108
```

说明：

- `interact_id=2424368065` 先记录为第二段主线 NPC 的稳定交互 ID。
- `id=65497`、`obj`、`current obj` 仍是运行时字段，只做日志诊断。
- 角色距离 NPC 约 `26.12` 米，第二段逻辑应先移动到这个 NPC 附近再交互。

传送点到第二个 NPC 的移动路径：

```text
507.642, 594.726, 322.562
507.765, 592.186, 322.562
508.212, 589.944, 322.562
508.513, 588.218, 322.562
509.040, 585.777, 322.155
510.030, 583.427, 322.000
510.996, 581.959, 322.000
512.149, 581.300, 322.000
513.041, 580.791, 322.000
514.999, 579.857, 321.933
516.655, 579.169, 321.717
517.367, 578.922, 321.597
519.268, 578.539, 321.558
520.175, 578.391, 321.616
521.243, 578.218, 321.705
521.746, 576.902, 321.743
522.048, 575.966, 322.029
522.377, 575.077, 322.029
```

第二个 NPC 第一个对话：

```text
npc_dialog_id=2424368065
content_id=1011
quest_id=0
type=select1
next=HACTION_TELEPORT_SIMPLE
```

说明：

- 这个 NPC 对话的 `quest_id=0` 是正常的，当前阶段依靠 NPC `interact_id`、地图、任务 `req_count=2` 和 `type/content_id/next` 识别。
- `type=select1`、`content_id=1011`、`next=HACTION_TELEPORT_SIMPLE` 表示点击第一个无名对话选项会触发简单传送。
- 可见的无名子控件为 `x=25 y=233`，点击后应等待地图或坐标变化。

第二段当前自动化逻辑：

```text
quest_id=20590
req_count=2
big_map_id=390010000
1. 按记录路径从传送点移动到 NPC 时间의 데바 잉그릴 附近。
2. 到达后 InteractNpc(2424368065) 打开对话。
3. type=select1/content_id=1011/next=HACTION_TELEPORT_SIMPLE 时点击 x=25 的无名对话选项。
4. 点击后等待 big_map_id 变化或坐标变化超过 20 米。
```

## 第二段传送后位置：판데모니움 대신전

点击 `시간의 데바 잉그릴` 的 `HACTION_TELEPORT_SIMPLE` 对话后，会传送到这个地图。

地图与角色：

```text
character=HiBroHi
level=1
map=판데모니움 대신전
big_map_id=120010000
name_en=DC1_SUB_A1
character_pos=1468.82,1450.37,176.93
```

当前主线状态：

```text
quest_id=20590
name=명암의 찰나
tab_name=使命
status_code=3
status_name=正在进行中
req_count=3
lv_num=1
```

第三阶段目标 NPC：

```text
name=발데르
kind=NPC
tag=NPC
type=2
ct=38
id=65451
interact_id=2147509246
level=55
hp=18756/18756
pos=1469.00,1466.00,177.82
rating=3
mutant=true
flags=1108
```

说明：

- `req_count=3` 可作为第三阶段标识。
- `interact_id=2147509246` 先记录为第三阶段 NPC `발데르` 的稳定交互 ID。
- 角色当前距离 NPC 约 `15.66` 米，第三阶段逻辑应先移动到 NPC 附近再交互。
- 第三阶段对话还需要 F8 采样，当前自动化只接到“移动并打开 NPC 对话”。

第三阶段当前自动化逻辑：

```text
quest_id=20590
req_count=3
big_map_id=120010000
1. 移动到 NPC 발데르 坐标 1469.00,1466.00,177.82 附近。
2. 到达后 InteractNpc(2147509246) 打开对话。
3. 对话打开后先停在 DumpDialog，等待 F8 样本补全后续点击逻辑。
```

## 重启对比

第一次记录：

```text
QuestInfo map_node=3FEAD410 val=56F7B220 anchor=746F9040 qid=506E
target current_obj=953124864 entity_obj=952295424 id=65534 interact_id=2147533452
character obj=987590048 pos=1662.92,1399.99,194.67
quest_addr=967082816 elems=1169993984
```

重启后记录：

```text
QuestInfo map_node=4177C0E0 val=55E0B220 anchor=746F3040 qid=506E
target current_obj=943282176 entity_obj=947896320 id=65534 interact_id=2147533452
character obj=1182007296 pos=1662.09,1399.54,194.67
quest_addr=955975296 elems=1012187904
```

第三次 F6 记录：

```text
QuestInfo map_node=3D4C1100 val=561CB220 anchor=76B43040 qid=506E
target current_obj=1051973632 entity_obj=962758656 id=65534 interact_id=2147533452
entity_count=15
character obj=1167645520 pos=1659.13,1400.34,194.67
quest_addr=992034752 elems=1029562624
```

重启后变化的字段：

- `QuestInfo map_node`
- `QuestInfo val`
- `QuestInfo anchor`
- 当前目标 `current_obj`
- 周围实体 `entity_obj`
- 当前角色 `obj`
- 任务结构地址 `quest_addr`
- 任务元素地址 `elems`
- 周围实体数量 `entity_count`
- 角色当前坐标有轻微变化

重启后保持稳定或业务上可依赖的字段：

- NPC `interact_id=2147533452`
- NPC 名称 `판데모니움 근위병`
- NPC 固定位置 `1655.94,1400.75,194.67`
- 地图 `big_map_id=120030000`
- 地图名 `판데모니움 입구`
- 主线 `quest_id=20590`
- 主线 `qid_hex=506E`
- 主线名称 `명암의 찰나`
- 主线状态字段语义：`tab_name=使命`、`status_code=3`、`req_count=0`
