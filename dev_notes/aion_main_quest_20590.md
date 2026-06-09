# Aion 主线任务 20590 笔记

这份笔记只记录开发和自动化判断需要的稳定业务字段，不作为运行包资源。

## 任务

```text
quest_id=20590
qid_hex=506E
name=명암의 찰나
tab_name=使命
level=1
```

运行时地址类字段会变化，不能作为任务配置依据：

- `obj`
- `id`
- `current_obj`
- `quest_addr`
- `elems`
- `QuestInfo map_node/val/anchor`

## 阶段 1：판데모니움 근위병

```text
map=판데모니움 입구
big_map_id=120030000
npc=판데모니움 근위병
interact_id=2147533452
npc_pos=1655.94,1400.75,194.67
```

对话链路：

```text
select_quest content_id=10 quest_id=0       -> 点击 x=25
select1      content_id=1011 quest_id=20590 -> 点击 x=25
select1_1    content_id=1012 quest_id=20590 -> 点击 x=25
select1_1_1  content_id=1013 quest_id=20590 next=HACTION_SETPRO1 -> 点击 x=25 后直接传送
```

自动化逻辑：

1. 移动到 `interact_id=2147533452` 附近。
2. 到 NPC 附近后等待短暂站稳，再打开 NPC 对话，避免移动打断对话。
3. 按上面的 `type/content_id` 顺序点击 `x=25` 的无名对话项。
4. 最后一页点击后等待 `big_map_id` 或坐标变化超过阈值。

## 阶段 2：时间의 데바 잉그릴

第一段传送后：

```text
map=명암의 성채 내성
big_map_id=390010000
character_pos=507.64,594.73,322.56
quest_id=20590
status_code=3
req_count=2
npc=시간의 데바 잉그릴
interact_id=2424368065 / 2427168962
npc_pos=522.68,573.38,322.03
```

注意：这个 NPC 的 `interact_id` 已经出现过变化。自动化不要只依赖固定 `interact_id`，应优先按最近的精确 NPC 名称 `시간의 데바 잉그릴` 交互，再用历史 `interact_id` 兜底。

移动路径：

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

对话链路：

```text
select1 content_id=1011 quest_id=0 next=HACTION_TELEPORT_SIMPLE -> 点击 x=25 后直接传送
```

## 阶段 3：발데르

第二段传送后：

```text
map=판데모니움 대신전
big_map_id=120010000
character_pos=1468.82,1450.37,176.93
quest_id=20590
status_code=3
req_count=3
npc=발데르
interact_id=2147509246
npc_pos=1469.00,1466.00,177.82
```

对话链路。当前样本显示这些页面都可以点击 `x=25` 的无名对话项向下推进：

```text
select_quest   content_id=10   quest_id=0     next=                         -> 点击 x=25
select4        content_id=2034 quest_id=20590 next=HACTION_SELECT4_1        -> 点击 x=25
select4_1      content_id=2035 quest_id=20590 next=HACTION_SELECT4_1_1      -> 点击 x=25
select4_1_1    content_id=2036 quest_id=20590 next=HACTION_SELECT4_1_1_1    -> 点击 x=25
select4_1_1_1  content_id=2037 quest_id=20590 next=HACTION_SELECT4_2        -> 点击 x=25
select4_2      content_id=2120 quest_id=20590 next=HACTION_SET_SUCCEED      -> 点击 x=25 后直接传送
```

## 阶段 4：아스크 领奖

第三段传送后：

```text
map=알데르 분지
big_map_id=220010000
character_pos=564.00,2785.00,299.50
quest_id=20590
status_code=4
status_name=已完成
req_count=3
npc=아스크
interact_id=2147492916
npc_pos=560.99,2786.03,299.06
```

同一时间任务列表会多出一些任务，例如 `20610`、`20611`、`20612`、`20613`、`20614`、`20615`。当前自动化先只处理并提交 `20590`，不切换到后续任务。

领奖对话链路：

```text
select_success       content_id=10002 quest_id=20590 next=HACTION_SELECT_QUEST_REWARD -> 点击 x=25
select_quest_reward1 content_id=5     quest_id=20590                                  -> 点击 name=ok 按钮
```

`select_quest_reward1` 页有可见 `ok` 按钮：

```text
ok visible=true x=129 y=419
cancel visible=true x=205 y=419
```

自动化逻辑：

1. 当 `20590 status_code=4`，或者已经在 `big_map_id=220010000` 且仍能识别到 `20590` 时，进入领奖阶段。
2. 移动到 `아스크 interact_id=2147492916` 附近。
3. 打开 NPC 对话。
4. `select_success/content_id=10002` 点击 `x=25`。
5. `select_quest_reward1/content_id=5` 点击可见 `name=ok` 按钮。
6. OK 成功后把运行时 `completed_20590_reward=true`，本任务链路停止，避免重复点 NPC。

## 已知问题：对话打开后又关闭

2026-06-09 观察到自动流程里可能出现 NPC 对话刚打开又关闭的现象。高概率原因不是战斗逻辑，而是主线状态机在“已经有对话框，但当前页没有识别到”时又返回 `InteractNpc`。Aion 客户端里重复交互同一个 NPC 可能会切换/关闭当前对话。

处理规则：

1. 只要 `state.dialog` 已经存在，主线任务只允许点击已录制的 `type_text/content_id` 页面。
2. 如果对话页未知，返回 `DumpDialog` 打印 `stage/type/content/npc_dialog_id/expected_interact_id`，不要再次 `InteractNpc`。
3. `InteractNpc` 成功后设置短暂 `wait_dialog_stage/wait_dialog_until`，默认按 `npc_dialog.wait_dialog_ms=3000` 等待对话读取，避免读取慢半拍时重复交互。
4. 已知页点击、领奖 OK、阶段完成时清理等待标记。

补充结构规则：

1. 路径跟随和直接寻路都视为独立 action。
2. 主线专用路径 `main_quest_20590:*` 仍在 `following=true` 时，不允许执行后续 `InteractNpc/ClickDialog/OK`，只返回 `WaitRouteComplete`。
3. 从移动类 action（`FollowRoute/NavigateToNpc/WaitRouteComplete`）切到对话类 action（`InteractNpc/ClickDialog/OK`）时，统一等待 `leveling.action_delay_seconds`，默认 `0.5` 秒。
4. 诊断日志使用 `[AionMainQuest20590Trace]` 前缀，重点看 `dialog-change`、`decision`、`action-switch`、`action-delay`、`interact-before/after`、`click-before/after`。

相关测试：

```text
aion_main_quest_20590: waits for active inner route before opening npc dialog
aion_main_quest_20590: does not re-interact when inner npc dialog is already open but unknown
aion_main_quest_20590: does not re-interact when temple npc dialog is already open but unknown
aion_main_quest_20590: does not re-interact when reward dialog is already open but unknown
```
