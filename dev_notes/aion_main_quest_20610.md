# Aion 主线任务 20610 笔记

## 起始 NPC

F11 记录到 20610 激活时：

```text
quest_id=20610
status_code=3
req_count=0
big_map_id=220010000
interact_id=2147514375
npc_pos=560.99,2786.03,299.06
```

使用 `2147514375` 打开 20610 起始 NPC。旧的 `2147492916` 属于 20590 奖励上下文，不要在 20610 起始阶段优先使用。

## 任务传送

起始 NPC 对话完成后，用任务传送 API，不用屏幕坐标：

```text
GetQuestTeleportId(20610)
QuestTeleport(20610, teleport_id)
```

历史 UI 控件只做诊断参考：

```text
quest_indicator_dialog.teleport
v3_quest_dialog 无名 NPC 链接
dictionary_dialog.teleport_to_npc
```

## 传送后奖励 NPC

落地后奖励 NPC：

```text
interact_id=2147524326
npc_pos=223.97,2679.86,295.25
char_pos=223.17,2680.63,295.25
```

奖励对话：

```text
select_success content_id=10002 quest_id=20610 -> 点击 x=25
select_quest_reward1 content_id=5 quest_id=20610 -> 点击 ok
```

OK 成功后设置 20610 奖励完成标记，不要再次和 NPC 交互。

## 后续蓝色打怪任务

20610 交完后，后续蓝色普通任务由 20611 打怪 runner 接手。

关键规则：

```text
普通任务 id: 24340 或 24341，必须从 GetQuestList() 读取
active: status_code=3
complete: status_code=4
grind_point=194.491,2689.982,300.625
```

不要只因为 20611 到 20615 主线是等级限制就启动蓝色打怪。必须先看到普通蓝色任务处于 `status_code=3`。

远程提交时使用实际蓝色任务 id：

```text
OpenQuestSubmit(actual_blue_quest_id)
select_quest_reward_remote content_id=56 -> 点击 ok
```

## 20611 等级门槛交接

蓝色任务完成后，任务列表会出现黄色主线等级限制：

```text
20611 status_code=6 lv_num=8
20612 status_code=6 lv_num=11
20613 status_code=6 lv_num=14
20614 status_code=6 lv_num=17
20615 status_code=6 lv_num=20
```

选择最早的 `seq` 等级限制任务，也就是 20611。打开任务面板后再调用：

```text
QuestTeleport(20611)
```

不要在任务面板关闭时直接调用任务传送。

## 启动恢复

脚本启动时先读取角色快照：

```text
角色名/等级
地图
GetQuestList()
当前打开的 NPC/任务对话
```

然后由 `aion.main_quest_resume` 补运行时标记，防止切角色或重启后复用旧状态。
