# 刷金脚本交接说明

这份说明是给新对话里的 AI 看的。目标不是解释业务背景，而是防止它把 `avepointTaskMode = 1` 读成练级 runner、改错层、或者破坏现有刷金状态机。

## 1. 当前真实入口

- `config.json`
  - 当 `avepointTaskMode = 1` 时，走刷金模式。
  - `mainScript = "AvePointStandalone.lua"`；发布包一般会切到 `.luac`，但源码入口关系不变。
- `AvePointStandalone.lua`
  - 只负责定位工程目录、扩展 `package.path`、加载 `scripts/AvePoint.lua`，并支持 `.lua/.luac` 双入口。
- `scripts/AvePoint.lua`
  - 继续加载 `scripts/avepoint/shared.lua`、`guard.lua`、`route.lua`、`interact.lua`、`hotkey.lua`，最后调用全局 `main()`。
- `scripts/avepoint/shared.lua`
  - 定义 `TASK_MODE` 常量，并读取 `config.json` 的 `avepointTaskMode`。
  - 模式为 `1` 时，不会去加载 `scripts/AvePointLeveling.lua` 这种 runner 表，而是继续走刷金模式的老状态机。
  - 这里同时承载 `MAP_CONFIGS`、`RANDOM_MAP_POOL_KEYS`、`BAG_CLEANUP_ACTIONS`、`IMAGE_CLICK_PRESETS`、各种 UI step builder、全局 `state`。
- `scripts/avepoint/interact.lua`
  - 这里的 `STEP_TARGET_RESOLVERS` 负责按顺序解析 step：文本距离、图片模板、锚点邻居、通用控件匹配。
- `scripts/avepoint/hotkey.lua`
  - `TASK_MODE.start_selected()` 在模式 `1` 时直接调用 `start_automation(mode_id, mode_name)`，而不是 `TASK_MODE.load_runner()`。
  - `TASK_MODE.update_selected(now)` 在模式 `1` 时，每 tick 走“异步人类鼠标 -> 死亡检测 -> 自动拾取 -> `update_route()` / `update_stage()`”这条刷金链路。
  - `update_stage()` 现在由 `STAGE_HANDLERS` 分发表驱动，不再是单个大号 `if state.stage == ...` 链。

结论：真正的刷金核心不是 `AvePointStandalone.lua`，也不是 `scripts/AvePointLeveling.lua`。它是一套分散在 `shared.lua + route.lua + interact.lua + hotkey.lua` 的多文件状态机。

## 2. 源码边界

### 2.1 真正应该改的源码

- `scripts/avepoint/shared.lua`
  - 刷金配置层和全局状态定义。
  - 这里放地图池、进图按钮步骤、进图确认策略、图片模板预设、复活/出图/仓库/清包 UI step builder、清包动作序列、全局延迟参数、共享 `state`。
- `scripts/avepoint/route.lua`
  - 路线与运行态骨架。
  - 负责随机选图、续跑快照、各种 reset helper、外场路线/仓库路线/图内路线/出图路线的启动与更新、卡点纠偏、重发 `MoveTo`、退出卡死脱困。
  - `reset_entry_portal_state()` 这类共享 reset 也在这里。
- `scripts/avepoint/interact.lua`
  - UI 交互执行层。
  - 负责按钮识别、文本距离匹配、图片点击验证、自动拾取、清包、存仓、出图按钮点击、复活逻辑。
  - `STEP_TARGET_RESOLVERS` 是 step 解析扩展点。
- `scripts/avepoint/hotkey.lua`
  - 刷金总调度层。
  - 负责模式分发、启动停止、stage 状态机推进、进图按钮链、进门确认、循环切图、热键主循环。
  - `STAGE_HANDLERS` 是 stage 扩展点；地图特例进门逻辑优先通过 `MAP_CONFIGS.entry_confirm_policy` 下沉，而不是继续往这里塞 map key 特判。
- `map/*.txt`
  - 实际图内路线点。
  - 每张刷金地图的 `route_file` 都落在这里，属于刷金模式的重要数据源。

### 2.2 不要当源码改的内容

- `release/` 下的内容
- 所有 `.luac`
- 打包产物 zip
- `scripts/AvePointLeveling.lua` 以及 `scripts/AvePointLeveling*.lua`

最后这一条很重要：这些文件是模式 `2` 的练级链路，不是 `avepointTaskMode = 1` 的主逻辑落点。

## 3. 配置层分别负责什么

### 3.1 `MAP_CONFIGS`

按地图 key 挂刷金地图配置。

典型字段：

- `label`
- `route_file`
- `exit_point`
- `entry_key_vk / exit_key_vk / reenter_key_vk`
- `entry_button_steps`
- `entry_confirm_step`
- `entry_confirm_policy`
- `map_route_escape_enabled`
- `map_route_interact_points`

典型用途：

- 新增一张刷金地图
- 调整某张图的进图按钮链
- 调整某张图的进图确认按钮
- 调整某张图的“连续进门失败后怎么处理”
- 调整某张图的出图点
- 给某张图加“到第 N 个路线点时按一次 D”的楼层切换 / 机关交互
- 给某张图单独打开 `Esc` 跳过开场

这层是“地图 key -> 刷金地图行为配置”的主入口。

### 3.2 `RANDOM_MAP_POOL_KEYS`

刷金轮换池。

补充说明：

- 新增地图时，不是只改 `MAP_CONFIGS` 就够了，还要把 key 加进 `RANDOM_MAP_POOL_KEYS`。
- 这个池才决定 `activate_random_map()` 能不能抽到该地图。

### 3.3 `ROUTE_POINTS` / `STASH_ROUTE_POINTS` / `STASH_RETURN_ROUTE_POINTS`

- `ROUTE_POINTS`：外场到进图点的固定路线。
- `STASH_ROUTE_POINTS`：去仓库的一段固定路线。
- `STASH_RETURN_ROUTE_POINTS`：从仓库回到刷图入口的一段固定路线。

注意：这三组不是图内 `map/*.txt` 路线文件；它们是刷金外场/仓库往返路径。

### 3.4 UI step builder

主要包括：

- `make_mystic_area_step()`
- `make_mystic_map_distance_step(map_label)`
- `make_common_next_step()`
- `make_common_portal_step()`
- `make_exit_portal_step()`
- `make_revive_at_checkpoint_step()`
- `make_revive_at_town_step()`
- `make_stash_oneclick_store_step()`
- `make_stash_back_step()`

这层负责标准化 UI 识别配置，例如：

- 文本锚点
- 按钮控件名
- 文本到按钮的距离范围
- 屏幕 hint 坐标
- 点击后的等待时间

如果问题本质是“某个按钮识别不到 / 识别错 / 点错”，优先看这里，不要先改状态机。

### 3.5 `entry_confirm_policy` / `entry_confirm_step`

- `entry_confirm_step`
  - 地图级“确认进图”按钮步骤。
  - 不配时，默认走 `make_select_ditu_confirm_step()`。
- `entry_confirm_policy`
  - 地图级进图确认策略。
  - 当前已经支持 `switch_random_map` 这类“连续失败后切图”的策略。

典型用途：

- 某张图的确认按钮不是默认“确认”
- 某张图连续进门失败 N 次后，要 `Esc` 掉当前页面并切别的图
- 某张图要统计哪一个 portal trigger 的点击次数

这层是“地图进图确认期的地图特例承载点”，优先级高于继续往 `hotkey.lua` 塞 map key 特判。

### 3.6 `IMAGE_CLICK_PRESETS`

图片模板点击预设库，当前放在 `shared.lua`。

补充说明：

- `resolve_image_click_preset(step)` 会优先读 `step.image_preset`，否则按 `step.label` 去 `IMAGE_CLICK_PRESETS` 里查。
- 如果只是某个图片模板路径、阈值、点击偏移要改，优先落这里，不要先改 `fetch_button_for_step()`。

### 3.7 `STEP_TARGET_RESOLVERS`

step 目标解析器注册表，定义在 `interact.lua`。

当前顺序：

1. `text_distance`
2. `image`
3. `text_anchor`
4. `generic_match`

这层负责“同一个 step 最终怎么找到要点的目标”。如果以后要支持新的 step 查找方式，优先在这里新增 resolver，而不是继续把 `fetch_button_for_step()` 堆成更大的分支函数。

### 3.8 `BAG_CLEANUP_ACTIONS`

清包动作序列配置。

典型用途：

- 打开背包
- 打开回收页
- 点“分解所有”
- 点确认
- 相对上一次点击位置做二次确认点击
- `Esc` 关闭背包

这层是“清包怎么点”的动作脚本，不是整体刷金循环本身。

### 3.9 `state`

刷金运行态是全局共享的，定义在 `shared.lua`，由 `route.lua` / `interact.lua` / `hotkey.lua` 共同读写。

这意味着：

- 它不是某个文件私有状态。
- 你改字段语义，通常会同时影响启动、路线、清包、复活、续跑、停止流程。

## 4. 真实运行逻辑

### 4.1 启动

`scripts/avepoint/hotkey.lua` 里的 `start_automation()` 会：

- 清空 `TASK_MODE.runner`
- 调 `TASK_MODE.prepare_start(mode_id, mode_name)` 重置刷金态
- 优先尝试 `try_resume_automation()` 续跑当前图
- 如果没有续跑快照，则 `activate_random_map("start")`
- `reset_cleanup_schedule("start")`
- 置 `state.running = true`
- 安排一次 human idle move
- 如果启动时人不在外场安全区，则进入 `startup_press_t -> startup_press_d -> startup_begin_outer_route`
- 否则直接 `start_outer_route()`

补充说明：

- `start_outer_route()` 会先看当前位置离 `ROUTE_POINTS` 最后一点是不是已经够近；够近就直接跳到 `press_entry_d`。
- 否则会找外场路线最近点，从最近点接管。

### 4.2 每 tick 更新

模式 `1` 下，`TASK_MODE.update_selected(now)` 大体是下面这个顺序：

1. 推进异步人类鼠标移动
2. 执行 `avepoint_maybe_handle_map_death()`
3. 执行 `maybe_pickup_loot(now)`
4. 如果 `now < state.wait_until`，则先等待
5. 如果当前存在 `state.route`，就走 `update_route(now)`
6. 否则走 `update_stage()`

结论：刷金模式是“stage 状态机 + route 状态机 + UI 交互 + 自动拾取 + 续跑快照”的混合状态机，不是单纯按地图路线巡路。

### 4.3 stage 状态机

`update_stage()` 现在通过 `STAGE_HANDLERS` 分发表串起整条刷金流程；新增阶段时，优先注册新的 stage handler，而不是回退到大号 if-else。

主要阶段包括：

- 启动恢复：
  - `startup_press_t`
  - `startup_press_d`
  - `startup_begin_outer_route`
- 清包与仓库：
  - `bag_cleanup_before_entry`
  - `bag_cleanup_before_reenter`
  - `bag_cleanup`
  - `begin_stash_route_before_entry`
  - `begin_stash_route_before_reenter`
  - `press_stash_d`
  - `stash_store_click`
  - `stash_store_escape`
  - `begin_stash_return`
- 进图：
  - `press_entry_d`
  - `entry_buttons`
  - `entry_portal_confirm`
- 图内：
  - `begin_map_route`
  - `route`
- 出图与切下一个循环：
  - `begin_exit_route`
  - `press_exit_d`
  - `verify_exit_result`
  - `press_reenter_d`
- 异常分支：
  - `map_revive`
  - `exit_interference_escape`
  - `begin_exit_route_for_chumen`
  - `exit_chumen_click`
  - `begin_exit_unstuck_route`

### 4.4 route 更新细节

`route.lua` 的 `start_route()` / `update_route()` 负责刷金路线推进。

关键逻辑：

- `start_route()` 会创建 `state.route`
- `Map route` 启动时会自动打开掉落拾取
- 某些地图可以在图内路线开始后延时发一次 `Esc`
- `update_route()` 到点后会推进到下一个 waypoint
- `Map route` 可以在指定 index 命中 `map_route_interact_points`，触发一次地图内按键交互
- 每隔 `REPATH_INTERVAL_MS` 会重新 `MoveTo` 当前点
- `Map route` 会追踪当前点的最佳距离和停滞时间
- 当单点停滞超过 `MAP_ROUTE_STUCK_SKIP_MS` 时，会先尝试右键破障
- 仍然卡住时，会直接跳到下一个点
- 路线结束后回到 `next_stage`

结论：图内纠偏不是全靠 `map/*.txt`。真正的全局兜底在 `update_route()` 里的 repath / 右键破障 / skip-next-point。

### 4.5 自动拾取、出图、复活、续跑

- 自动拾取：
  - `maybe_pickup_loot(now)` 会轮询 `nav.enum_ground_items()`
  - 有东西时按 `A`
  - 如果长时间捡不动，会置 `pickup_skip_until_exit = true`
  - 如果疑似背包满，还会置 `force_cleanup_after_exit = true`
- 出图：
  - 出图前会先 `maybe_wait_for_loot_before_exit()`
  - 正常路径优先点击 `PortalBtn`
  - 失败后会走 `Esc` 清干扰，再尝试 `出门` 图像按钮
  - 还不行就 reroute，必要时构造 `build_exit_unstuck_route()` 做脱困
- 复活：
  - 只有在 `Map route` 期间检测到人物死亡，才会切到 `map_revive`
  - 会尝试“记录点复活”或“城镇复活”
  - 恢复稳定后，从最近图内路线点继续
- 续跑：
  - `stop_automation("AvePoint automation stopped")` 时，若人还在图内，会 `capture_resume_snapshot()`
  - 下次 start 优先 `try_resume_automation()`，可能直接从当前图续刷，而不是先回外场重开

## 5. 行为来源优先级

### 5.1 进图行为来源

进图相关行为的来源优先级大体是：

1. `MAP_CONFIGS[map_key].entry_button_steps`
2. `MAP_CONFIGS[map_key].entry_confirm_step`
3. `MAP_CONFIGS[map_key].entry_confirm_policy`
4. 这些 step 引用的通用 step builder
5. `hotkey.lua` 中 `update_entry_buttons()` / `avepoint_update_entry_portal_confirm()` 的执行器

所以：

- 如果只是某张图的选图按钮点错，优先改地图配置。
- 如果是某张图连续进门失败后的切图/重试策略，优先改 `entry_confirm_policy`。
- 只有当现有 step / policy 表达能力不够时，才需要动执行器。

### 5.2 图内路线行为来源

图内路线行为的来源优先级大体是：

1. `map/<route_file>.txt` 的普通 waypoint
2. `MAP_CONFIGS.map_route_interact_points` 的点位交互
3. `route.lua` 的全局 repath / 卡点恢复 / skip-next-point

所以：

- 纯坐标偏一点，优先改 `map/*.txt`
- 到某个点要按 D/切楼层，优先改 `map_route_interact_points`
- 只有多张图共用的全局卡住模式，才应该考虑改 `route.lua`

### 5.3 出图行为来源

出图相关行为的来源优先级大体是：

1. `maybe_wait_for_loot_before_exit()`
2. `PortalBtn` 点击
3. `Esc` 清页面干扰
4. `出门` 图像点击
5. reroute / `build_exit_unstuck_route()`

所以：

- 不要看到一次出图失败，就直接把问题判断成“退出按钮模板失效”。
- 它可能是地上还有掉落、页面卡错层、人物没真正离开 `exit_point`、或者被地形卡住。

### 5.4 step 目标解析来源

step 查找目标的来源优先级大体是：

1. `distance_anchor_exact_text + distance_button_name`
2. `step.image_preset`
3. `IMAGE_CLICK_PRESETS[step.label]`
4. `anchor_exact_texts / anchor_include_patterns`
5. `include_patterns / exclude_patterns`

所以：

- 只是模板图路径或阈值问题，优先改 `step.image_preset` 或 `IMAGE_CLICK_PRESETS`
- 只是文本距离参数问题，优先改 step 本身
- 只有需要“新增一种全新的查找方式”时，才去扩 `STEP_TARGET_RESOLVERS`

## 6. 关键状态，不要随便破坏

### 6.1 stage / route 主状态

- `state.stage`
- `state.wait_until`
- `state.route`
- `state.map_points`
- `state.current_map_key`
- `state.current_map_label`
- `state.cycle_index`

这些字段一起约束“当前到底处于哪个阶段、是否在路线中、现在跑的是哪张图、当前第几轮”。

### 6.2 进图按钮与确认状态

- `state.button_index`
- `state.button_retry_index`
- `state.button_retry_started_at`
- `state.entry_portal_started_at`
- `state.entry_portal_ready_at`
- `state.entry_portal_retry_due_at`
- `state.entry_portal_click_attempts`
- `state.last_clicked_entry_label`

这是一整套进图按钮链会话状态。它决定什么时候还能重试、什么时候算确认阶段、什么时候该判定终极王座没票。

### 6.3 路线与卡住恢复状态

- `state.route_escape_due_at`
- `state.route_escape_sent`
- `state.route_escape_hold_until`
- `state.route_start_key`
- `state.route_start_started_at`
- `state.route_start_last_warn_at`
- `state.route_start_ready_at`

补充说明：

- `route_escape_*` 是图内开场 `Esc` 逻辑，不是普通冷却。
- `route_start_*` 是“路线能不能真正开始”的等待与重试控制，不要只删一条 warn 就以为没影响。

### 6.4 拾取 / 清包 / 仓库 / 出图状态

- `state.pickup_active`
- `state.pickup_next_at`
- `state.pickup_stuck_reference_count`
- `state.pickup_stuck_attempts`
- `state.pickup_skip_until_exit`
- `state.force_cleanup_after_exit`
- `state.cleanup_runs_completed`
- `state.cleanup_runs_target`
- `state.bag_cleanup_*`
- `state.stash_*`
- `state.exit_verify_*`

尤其注意：

- `pickup_skip_until_exit` 和 `force_cleanup_after_exit` 经常成对工作。
- 前者表示这轮先别捡了，后者表示出图后必须进清包流程。

### 6.5 复活与续跑状态

- `state.revive_started_at`
- `state.revive_clicked_at`
- `state.revive_click_count`
- `state.revive_resume_ready_at`
- `resume_snapshot`

这些状态共同约束“人物死了以后如何恢复”和“手动 stop 后能不能续刷当前图”。

## 7. 改动应该落在哪一层

- 新增一张刷金地图：
  - 优先改 `MAP_CONFIGS`
  - 同时改 `RANDOM_MAP_POOL_KEYS`
  - 再补 `map/<route_file>.txt`
- 调整某张图的选图 / 开门按钮：
  - 优先改 `MAP_CONFIGS.entry_button_steps`
  - 需要复用时再改相关 step builder
- 调整某张图的进图确认按钮：
  - 优先改 `MAP_CONFIGS.entry_confirm_step`
- 调整某张图连续进门失败后的切图 / 重试策略：
  - 优先改 `MAP_CONFIGS.entry_confirm_policy`
- 某张图在固定路线点要按一次 D / 切楼层 / 触发机关：
  - 优先改 `MAP_CONFIGS.map_route_interact_points`
- 调整外场进图前路线：
  - 改 `ROUTE_POINTS`
- 调整仓库往返路线：
  - 改 `STASH_ROUTE_POINTS` / `STASH_RETURN_ROUTE_POINTS`
- 调整自动拾取、捡不动判定、背包满后处理：
  - 改 `scripts/avepoint/interact.lua`
- 调整清包按钮识别或确认点击顺序：
  - 优先改 `BAG_CLEANUP_ACTIONS` 或相关 step builder
  - 只有执行能力不够才改 `interact.lua`
- 调整图片模板路径、阈值、偏移：
  - 优先改 `step.image_preset` 或 `IMAGE_CLICK_PRESETS`
- 新增一种 step 目标查找方式：
  - 改 `scripts/avepoint/interact.lua` 里的 `STEP_TARGET_RESOLVERS`
- 调整全局 repath、卡住破障、skip-next-point、续跑快照：
  - 改 `scripts/avepoint/route.lua`
- 调整 stage 切换顺序、启动流程、切下一轮流程：
  - 优先改 `scripts/avepoint/hotkey.lua` 里的 `STAGE_HANDLERS`
- 调整任务模式分发：
  - 改 `shared.lua` / `hotkey.lua`

## 8. 最容易改错的地方

1. 不要把 `scripts/AvePointLeveling.lua` 当成刷金逻辑本体。模式 `1` 根本不走它。
2. 不要把 `release/` 或 `.luac` 当成源文件改。
3. 不要只改 `map/*.txt` 就试图修所有问题。很多问题其实是进图按钮、出图按钮或拾取 gating。
4. 不要新增 `MAP_CONFIGS` 后忘了同步改 `RANDOM_MAP_POOL_KEYS`。
5. 不要把 `ROUTE_POINTS` 和 `map/<route_file>.txt` 混为一谈。前者是外场/仓库，后者是图内。
6. 不要把 `map_route_interact_points` 理解成普通路线点列表。它是“命中某个既有路线 index 后触发交互”的补充层。
7. 不要删除各种 `delay_ms / wait_until / timeout / retry`。这些基本都是在压 UI race condition 和网络抖动。
8. 不要删 `maybe_wait_for_loot_before_exit()` 这类 gating。否则很容易出现地上有东西、脚本却开始强退图。
9. 不要忽略 `pickup_skip_until_exit` 和 `force_cleanup_after_exit` 的联动；它们直接影响“捡不动后是否强制清包”。
10. 不要忘记手动 stop 也可能留下 `resume_snapshot`，下次 start 不是必然从外场重新开始。
11. 不要再把地图特例直接硬塞回 `hotkey.lua` 的 map key 判断。现有“连续进门失败后切图”已经下沉到 `MAP_CONFIGS.entry_confirm_policy`。
12. 不要新增 stage 时回退到 `if state.stage == ...` 大串判断；优先挂到 `STAGE_HANDLERS`。
13. 不要把图片模板判断继续堆回 `resolve_image_click_preset()` 的条件分支；优先用 `step.image_preset` 或 `IMAGE_CLICK_PRESETS`。
14. 不要先动全局状态机去修单图 UI 偏差。单图问题优先落地图配置、step 配置、entry confirm policy。

## 9. 建议新 AI 的阅读顺序

1. `config.json`
2. `AvePointStandalone.lua`
3. `scripts/AvePoint.lua`
4. `scripts/avepoint/shared.lua`
5. `scripts/avepoint/route.lua`
6. `scripts/avepoint/interact.lua`
7. `scripts/avepoint/hotkey.lua`
8. 目标地图对应的 `map/*.txt`

## 10. 可直接发给新 AI 的提示词

```text
先不要直接改代码，先按下面顺序读文件并复述调用链和改动落点：

1. config.json
2. AvePointStandalone.lua
3. scripts/AvePoint.lua
4. scripts/avepoint/shared.lua
5. scripts/avepoint/route.lua
6. scripts/avepoint/interact.lua
7. scripts/avepoint/hotkey.lua
8. 目标地图对应的 map/*.txt

这是一个“`STAGE_HANDLERS` 驱动的 stage 状态机 + route 状态机 + `STEP_TARGET_RESOLVERS` 驱动的 UI 交互 + 自动拾取 + 续跑快照”的刷金状态机，不是独立 runner 文件。

请遵守这些约束：
- 不要改 release/ 或任何 .luac
- 不要把 AvePointLeveling.lua 当成 avepointTaskMode=1 的主逻辑
- 不要删除等待/结算/重试时间
- 不要忘记 MAP_CONFIGS 和 RANDOM_MAP_POOL_KEYS 要同步
- 不要把外场 ROUTE_POINTS 和图内 map/*.txt 混为一谈
- 如果需求是地图级连续进门失败后的处理，优先改 `MAP_CONFIGS.entry_confirm_policy`
- 如果需求是地图级确认按钮，优先改 `MAP_CONFIGS.entry_confirm_step`
- 如果需求是图片模板路径/阈值/偏移，优先改 `step.image_preset` 或 `IMAGE_CLICK_PRESETS`
- 如果需求是新增一种 step 查找能力，优先扩 `STEP_TARGET_RESOLVERS`
- 如果需求是新增或改造某个 stage，优先改 `STAGE_HANDLERS`
- 如果问题是单图的按钮识别或交互，优先改地图配置或 step 配置
- 如果问题只发生在某个路线 index 的交互，优先考虑 map_route_interact_points
- 只有在现有配置层完全承载不了需求时，才去改 route.lua / interact.lua / hotkey.lua

```
