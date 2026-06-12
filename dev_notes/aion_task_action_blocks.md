# Aion 主线任务 Action 积木笔记

这份笔记用于后续按“搭积木”的方式继续补主线任务。

核心原则：

- 任务模块只判断“下一步要做什么”，返回 action。
- 执行层只执行 action，不自己猜任务流程。
- 打怪只能作为任务明确要求的一步触发，不能靠启动状态或旧 runtime 状态自动触发。
- 新任务优先复用已有 action；只有现有 action 不能表达时再加新积木。

## 任务信息模板

以后补新任务时，尽量按这个格式给信息：

```text
任务 ID：
等级要求：
当前任务状态：status_code / req_count / lv_num
步骤 1：
  类型：移动 / 找 NPC / 接任务 / 交任务 / 任务传送 / 地图传送 / 打怪 / 特殊确认
  坐标：
  NPC 名称：
  interact_id：
  F11 对话：
    npc_dialog_id：
    dialog_content_id：
    quest_id：
    type_text：
  UI 信息：
    parent：
    name：
    depth：
    x/y/tolerance：
  传送信息：
    quest_id / node_id / node_name / big_map_id：
```

## 读取与等待类

### `ReadState`

用途：角色、等级、任务列表、坐标等关键信息没读到时等待。

常见参数：

- `quest_id`
- `stage`

### `Idle`

用途：当前没有需要执行的任务动作，或者该步骤已经完成。

常见参数：

- `quest_id`
- `quest_step`
- `stage`

### `DumpDialog`

用途：遇到未知 NPC 对话页时记录日志，方便用 F11 信息补规则。

需要信息：

- `npc_dialog_id`
- `dialog_content_id`
- `quest_id`
- `type_text`
- `stage`
- 期望 NPC 的 `interact_id` / `npc_name_key`

### `WaitPositionChanged`

用途：传送调用后等待角色坐标变化。

常见参数：

- `quest_id`
- `stage`
- `min_distance`

后续通常接：

- `CompleteQuestTeleport`
- `CompleteMapNodeTeleport`

## 移动类

### `NavigateToNpc`

用途：移动到 NPC 或指定任务点。

需要信息：

- `x`
- `y`
- `z`
- `range`
- `quest_id`
- `quest_step`
- `stage`
- 可选：`interact_id`、`npc_name`、`npc_name_key`

常见场景：

- 去 NPC 身边接任务
- 去 NPC 身边交任务
- 去任务指定坐标后再找 NPC

### `FinalMoveToNpc`

用途：某些任务需要最后贴近 NPC 或精确点位。

用法和 `NavigateToNpc` 类似。

### `NavigateToGrindPoint`

用途：移动到打怪点，但不启动打怪。

需要信息：

- `x`
- `y`
- `z`
- `range`
- `quest_id`
- `stage`

### `FollowRoute`

用途：按一组路线点移动。

需要信息：

- `route_points`
- `route_index`
- `stage`

### `WaitRouteComplete`

用途：等待路线走完。

通常和 `FollowRoute` 成对使用。

## NPC 交互类

### `InteractNpc`

用途：打开 NPC 对话。

需要信息：

- `npc_name`
- `npc_name_key`
- `interact_id`
- `stage`
- `quest_id`
- `quest_step`

可选参数：

- `allow_interact_id_fallback = true`
- `mark_20612_start_point_reached = true`

推荐做法：

- 优先按 `npc_name` 交互。
- `interact_id` 作为备用或确认信息。
- 打开对话后再根据 F11 的 `type_text` 和 `dialog_content_id` 决定下一步。

## 接任务与推进对话类

### `ClickDialogX`

用途：普通点一次 X，推进一页对话。

需要信息：

- `type_text`
- `expected_content_id`
- `content_id`
- `click_x`
- 可选：`click_y`、`click_y_tolerance`
- `quest_id`
- `quest_step`
- `stage`

适合：

- 普通多页对话
- 接任务第一层 `select_quest`

### `ClickDialogXContinuous`

用途：连续点 X，直到对话关闭或达到点击上限。

需要信息：

- `type_text`
- `expected_content_id`
- `content_id`
- `click_x`
- 可选：`click_y`、`click_y_tolerance`
- 可选：`max_steps`、`delay_ms`
- `quest_id`
- `stage`

适合：

- 接任务时可以连续 X 的链路
- 已知不需要选奖励、不需要分支选择的对话
- F11 里只需要指定起始页，例如 `type_text=select_quest`、`dialog_content_id=10`

### `ClickDialogLastContinuousOk`

用途：连续点当前 NPC 对话里的最后一条可点选项，最后用 OK 兜底。

需要信息：

- `quest_id`
- `stage`
- `npc_dialog_id`
- `interact_id`
- 可选：`type_text`、`content_id`
- 可选：`click_x`、`max_steps`、`delay_ms`

适合：

- 对话分支不固定，但始终要选最后一条的任务链
- 最后一页可能只剩 `ok` 按钮的流程
- 例如 20615 落地后和 `울고른` 对话

### `ClickDialogXWaitTeleport`

用途：点 X 后会触发传送。

需要信息：

- 普通 X 对话参数
- `stage`
- 当前坐标作为传送前坐标

执行后会设置：

- `waiting_teleport = true`
- `teleport_stage = stage`

后续接：

- `WaitPositionChanged`
- `CompleteQuestTeleport`

### `ClickDialogXCompleteQuest`

用途：点 X 后标记某个任务阶段完成。

适合：

- 开场对话结束
- 某个任务接受完成
- 任务步骤进入下一阶段

### `ClickDialogOkCompleteQuest`

用途：点 OK / 确认按钮，完成交任务或领奖。

适合：

- 交任务领奖
- 远程奖励确认
- NPC 奖励页确认

需要信息：

- `quest_id`
- `type_text`
- `content_id`
- `stage`

## 交任务与远程提交类

### `OpenQuestSubmit`

用途：打开远程提交/远程领奖任务。

需要信息：

- `quest_id`
- `quest_step`
- `stage`

后续通常接：

- `ClickDialogOkCompleteQuest`

### NPC 交任务组合

常用组合：

```text
NavigateToNpc
InteractNpc
ClickDialogX / ClickDialogXContinuous
ClickDialogOkCompleteQuest
```

如果有奖励选择页，需要 F11 给：

- 奖励页 `type_text`
- 奖励页 `dialog_content_id`
- 确认按钮状态或 OK 页信息

## 右侧任务追踪与任务面板传送类

### `ClickUiControl`

用途：点击 UI 控件，例如右侧任务追踪标题。

需要信息：

- `parent`
- `name`
- `depth`
- `quest_id`
- `stage`

当前常用：

```text
parent = quest_indicator_dialog
name = prototype / htmltext / title
depth = 4
```

### `ClickUiControlAt`

用途：按坐标点击 UI 控件。

需要信息：

- `parent`
- `x`
- `y`
- `tolerance`
- `depth`

适合：

- UI 名称不稳定，但坐标稳定的按钮或链接

### `ClickUiControlWaitTeleport`

用途：点击 UI 控件后直接等待传送。

需要信息：

- UI 控件信息
- `quest_id`
- `stage`
- `wait_teleport = true`

### `OpenQuestPanel`

用途：打开任务面板。

需要信息：

- `quest_id`
- `stage`

### `QuestTeleport`

用途：调用任务立即移动/任务传送。

需要信息：

- `quest_id`
- `quest_step`
- `stage`
- `wait_teleport`
- 可选：`open_panel_key`
- 可选：`require_panel_visible`

重要规则：

- 先打开右侧任务追踪或任务面板。
- 确认 `v3_quest_dialog` 可见后再调用。
- 调用后如果会移动，接 `WaitPositionChanged`。

### `CompleteQuestTeleport`

用途：确认任务传送结束，并写入对应 runtime 标记。

需要信息：

- `quest_id`
- `stage`

## 地图节点传送类

### `MapNodeTeleportByName`

用途：通过地图节点传送。

需要信息：

- `big_map_id`
- `node_id`
- 或 `node_name`
- 或 `node_name_en`
- `quest_id`
- `stage`
- 可选：`price`
- 可选：`wait_teleport`

适合：

- 传送到热点节点
- 使用地图上的传送点

### `CompleteMapNodeTeleport`

用途：确认地图节点传送完成。

需要信息：

- `quest_id`
- `stage`

## 打怪类

### `StartStationaryGrind`

用途：启动定点打怪。

需要信息：

- `quest_id`
- `quest_step`
- `stage`
- `x`
- `y`
- `z`
- 可选：`required_level`
- 可选：`char_level`
- 可选：`until_level`

硬规则：

- 必须是任务明确返回的 action。
- 必须带 `requires_combat = true`。
- 必须带 `task_step = "grind"`。
- 不能因为启动、旧状态、等级不足就自动进入打怪。

### `WaitLevelGrind`

用途：等级不够时，等待定点打怪达到目标等级。

需要信息：

- `quest_id`
- `required_level`
- `char_level`
- `stage`

达到等级后通常接：

- `QuestTeleport`
- 或 `NavigateToNpc`
- 或任务下一步

### `WaitQuestComplete`

用途：任务要求打怪完成目标，等待任务完成。

需要信息：

- `quest_id`
- `quest_step`
- `stage`

### `CompleteQuestGrind`

用途：任务打怪完成后的收尾。

会停止任务打怪状态并刷新任务缓存。

## 特殊确认类

### `ClickObeliskConfirm`

用途：点击绑定方尖碑这类特殊确认弹窗。

需要信息：

- `confirm_x`
- `confirm_y`
- `confirm_tolerance`
- `quest_id`
- `stage`

适合：

- 不是普通 NPC 对话树的确认弹窗
- 可以坐标点击或回车兜底的弹窗

## 常用组合

### 接任务

```text
NavigateToNpc
InteractNpc
ClickDialogX / ClickDialogXContinuous
```

需要给：

- NPC 坐标
- NPC 名称 / interact_id
- F11 起始对话页
- 是单次 X 还是连续 X

### 交任务

```text
NavigateToNpc
InteractNpc
ClickDialogX / ClickDialogXContinuous
ClickDialogOkCompleteQuest
```

需要给：

- NPC 坐标
- NPC 名称 / interact_id
- 完成页 F11
- 奖励确认页 F11

### 接任务后任务传送

```text
ClickUiControl
QuestTeleport
WaitPositionChanged
CompleteQuestTeleport
```

需要给：

- `quest_id`
- 右侧任务 UI 信息
- 传送 stage
- 传送前后坐标变化是否明显

### 地图节点传送

```text
MapNodeTeleportByName
WaitPositionChanged
CompleteMapNodeTeleport
```

需要给：

- `big_map_id`
- `node_id` 或 `node_name_en`
- 传送前坐标
- 目标节点信息

### 等级不足先打怪

```text
StartStationaryGrind
WaitLevelGrind
达到等级后执行下一步
```

需要给：

- 当前等级
- 目标等级
- 打怪点坐标
- 后续任务步骤

### 任务要求打怪

```text
NavigateToGrindPoint
StartStationaryGrind
WaitQuestComplete
OpenQuestSubmit / InteractNpc
ClickDialogOkCompleteQuest
```

需要给：

- 打怪点坐标
- 任务目标状态
- 任务完成后是远程交还是 NPC 交

## 新任务记录建议

每个任务可以单独建一个笔记：

```text
dev_notes/aion_main_quest_任务ID.md
```

笔记里至少记录：

- 任务 ID
- 等级要求
- 当前 `status_code` / `req_count` 含义
- 每个 NPC 的名称、坐标、interact_id
- 每个 F11 对话页
- 每个 UI 点击点
- 每个传送步骤
- 每个需要 runtime 标记的完成点

## 新增任务时的实现顺序

1. 先补 `dev_notes` 任务笔记。
2. 在任务模块里补常量：NPC、坐标、stage、dialog steps。
3. 在 `nextAction` 里按任务快照判断下一步。
4. 优先返回已有 action。
5. 如需新 action，先在执行层补通用执行器，再在任务模块使用。
6. 给恢复模块补 startup snapshot 识别。
7. 给 tests 补 mock 快照测试。

## 不要再犯的结构问题

- 不要让启动逻辑直接推断“去打怪”。
- 不要让 `active_20611_grind` 这种 runtime 残留单独放行战斗。
- 不要把任务状态判断写到战斗 tick 里。
- 不要跳过当前任务快照。
- 不要用一个泛化等级卡住分支覆盖已经接到的具体任务。
- 不要在未知对话页上乱点；先 `DumpDialog`，再按 F11 补规则。
