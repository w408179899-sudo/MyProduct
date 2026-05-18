# 练级脚本交接说明

- 2026-05-18 note: treasure persistence must be strictly character-scoped. `scripts/avepoint_treasure_state.lua` records and resume snapshots must carry the current persistence `character_id`; missing or mismatched IDs are treated as old treasure state and discarded/reset. Do not restore legacy/global treasure routes or resume snapshots across characters.
- 2026-04-26 note: main-task calling is back on the control-click path only. Resolve the current session's button addr via `nav.find_button_by_locator()` / task-panel entry / `find_button_near_point()`, then `control_click(addr)`. Do not reintroduce `window_post_click`, anchor screen-click fallback, or any real mouse movement for the mainline task button path.
- 2026-04-26 note: `nav.get_main_task_pos()` treats `(0,0,*)` as invalid and returns `GetMainTaskPos returned zero coordinates.` The runner already rejected zero world points; this wrapper change only makes the error explicit instead of surfacing as `pos_err=nil`.
- 2026-04-26 note: F12 caches a dynamic locator, not a reusable addr, when `GetCurrentSelected` is `TaskItem_C.WidgetTree.TaskBtn`. The locator stores widget fullname, client hint, and nearest related text geometry in `_G.AVEPOINT_MAIN_TASK_BUTTON_LOCATOR`; leveling resolves the current session's fresh addr via `nav.find_button_by_locator()` and then calls `control_click(addr)`. Do not cache a raw TaskBtn addr across game launches.
- 2026-04-26 note: a cached F12 TaskBtn locator can become stale when the task row text changes. If it misses, leveling clears `_G.AVEPOINT_MAIN_TASK_BUTTON_LOCATOR` and retries the built-in TaskBtn locator in the same main-task call; do not fall back to mouse hover to refresh selected-state.
- 2026-04-26 note: if the F12 TaskBtn locator is not populated, leveling still uses the built-in TaskBtn locator derived from the verified F12 sample (`TaskItem_C.WidgetTree.TaskBtn` near client `(94.04,224.16)`) to resolve a fresh addr. Pressing F12 only refreshes/adds related-text geometry; it is not meant to preserve an addr.
- 2026-04-26 note: `follow_idle` main-task refresh during normal task-path movement must stay non-destructive. When there is still a live `task_target`, the refresh click should use `preserve_target=true` and must not pause navigation; otherwise a transient `GetMainTaskPath/GetMainTaskPos` zero-result will clear the current path and freeze the runner near the endpoint.
- 2026-04-26 note: `F12` no longer clicks the configured locator target. It now dumps the raw `GetCurrentSelected` output via `nav.dump_current_selected_button` for API diagnosis. Keep `F4/F10/F11` on the old preview/match/click workflows; do not mix cursor-nearby control enumeration semantics back into those hotkeys.
- 2026-04-26 note: task-panel parsing can surface a mainline item whose `button_x/button_y` are valid while `button_addr` is invalid. Main-task calling now prefers a window-click fallback on that parsed panel-item position before falling back to the old global hint path. Keep this scoped to the recognized mainline panel item; do not turn it into a generic screen-click search.
- 2026-04-26 note: `F10/F11` cursor probe now tries `GetCurrentSelected` first and only falls back to the old cursor-nearby button scan when selected is unavailable. The selected path logs `source=locator` when the generated locator re-fetches cleanly, or `source=GetCurrentSelected` when it clicks the selected address directly.
- 2026-04-26 note: main-task button hint is now based on the latest verified `GetCurrentSelected` TaskBtn position `(94.04, 224.16)` at 1440x900 client size, with ratio fallback `(0.065306, 0.249067)`. Keep the main-task lookup centered on the actual `TaskItem_C.WidgetTree.TaskBtn`, not the old left text-anchor position.
- 2026-04-26 note: the direct main-task `TaskBtn` hint lookup must keep the configured `MAIN_TASK_BUTTON_STEP.hint_max_distance` search radius. Do not clamp this pass to `50`, otherwise the real TaskBtn can fall outside the lookup window even when it is still within the verified main-task row band.

这份说明是给新对话里的 AI 看的。目标不是解释业务背景，而是防止它读错入口、改错层、破坏现有状态机。

- 2026-04-24 note: 仓库根目录新增 `GAME_SCRIPT_AI_BLUEPRINT.md`。它不是当前练级 runner 的实现说明，而是“新游戏项目接入模板 + 给 AI 的通用实现蓝图”。后续如果是做新游戏脚本骨架、平台化分层、快速复制项目，优先看这份；如果是修当前练级逻辑，继续以本 handoff 和 `scripts/AvePointLeveling*.lua` 为准。
- 2026-04-24 note: 仓库根目录新增 `DATA_LUAC_ANALYSIS.md`。这是 `scripts/data.luac` 的反汇编分析文档，主要用于排查底层 Data API 失效、偏移过期、根指针链断裂。修 `F9 Data API` 类问题时先看这份，不要直接把 runner 层日志当根因。
- 2026-04-24 note: 仓库根目录新增 `DATA_LUAC_PSEUDOCODE.lua`。这是根据 `scripts/data.luac` 反汇编重建的可读伪源码，目的是方便看结构、导出函数、偏移和字段，不是可直接替换线上 `data.luac` 的权威源码。后续如果要修偏移，优先拿它对照分析，不要直接当生产文件用。

## 1. 当前真实入口

- `config.json`
  - `mainScript = "AvePointStandalone.lua"`
  - `avepointTaskMode = 2`，也就是当前跑的是练级模式，不是刷金模式。
- `AvePointStandalone.lua`
  - 只负责定位工程目录、扩展 `package.path`、加载 `scripts/AvePoint.lua`，并且支持 `.lua/.luac` 双入口。
- `scripts/AvePoint.lua`
  - 继续加载 `scripts/avepoint/shared.lua`、`guard.lua`、`route.lua`、`interact.lua`、`hotkey.lua`，最后调用全局 `main()`。
- `scripts/avepoint/shared.lua`
  - 读取 `config.json` 的 `avepointTaskMode`。
  - 当模式为 `2` 时，加载 `scripts/AvePointLeveling.lua` 作为 runner。
- `scripts/avepoint/hotkey.lua`
  - `TASK_MODE.start_selected()` 调 `runner.start(ctx)`。
  - `TASK_MODE.update_selected(now)` 每 tick 调 `runner.update(now, ctx)`。

结论：真正的练级核心不是 `AvePointStandalone.lua`，也不是 `scripts/AvePoint.lua`，而是 `scripts/AvePointLeveling.lua`。

## 2. 源码边界

### 2.1 真正应该改的源码

- `scripts/AvePointLeveling.lua`
  - 练级总控 runner。
  - 负责状态机、任务目标刷新、导航、战斗补偿、任务完成判定、UI 交互、卡住恢复、藏宝地接管。
- `scripts/AvePointLevelingConfig.lua`
  - 配置层，不是主流程代码。
  - 这里定义任务名映射、Boss 房间点、路线动作点、藏宝地配置、通用按钮步骤，以及任务级对话补充流。
- `scripts/AvePointLevelingActions.lua`
  - 配置构造器。用于标准化 `boss_kite`、`world_map_send`、`dialogue_flow`、`route_point_action`、`revive_reentry` 这类配置。
- `scripts/AvePointLevelingPolicy.lua`
  - 匹配策略层。
  - 负责 `task_name/task_detail` 命中配置、地图名命中配置、`objective_ready_distance` 这种策略判断。
- `scripts/AvePointLevelingTreasure.lua`
  - 藏宝地子状态机。
  - 会在练级主流程中接管执行，并且把运行态持久化到 `scripts/avepoint_treasure_state.lua`。

### 2.2 不要当源码改的内容

- `release/` 下的内容
- 所有 `.luac`
- 打包产物 zip

这些是发布产物，不是当前维护源。改这里基本等于改错地方。

## 3. 配置层分别负责什么

`scripts/AvePointLeveling.lua` 启动后，会把内置默认值替换成 `scripts/AvePointLevelingConfig.lua` 里的实际配置，所以后者是当前行为的主要数据源。

### 3.1 `TASK_NAME_CONFIGS`

按任务名或任务详情匹配特殊逻辑。

典型用途：

- 某个任务要进 Boss kite 模式
- 某个任务不是普通跟随，而是要走 `world_map_send`
- 某个任务在进入对话/交互窗口后，正式点 `JumpBtn` 前，要先补一个或多个 locator 按钮步骤
- 某个任务需要额外的 `retry_call_task`
- 某个任务要用 `task_patterns/task_detail_patterns/constraint_mode` 收窄匹配范围
- 某个任务到终点后先正常 NPC 对话，`JumpBtn` 后又弹出任务详情按钮需要再点一次：优先配成该 detail 下的 `objective.mode="task_objective_button"` + `objective.button_steps`，不要用全局按钮规则，也不要改主线寻路。

这层是“当前任务文本 -> 特殊执行策略”的主入口。

补充说明：

- `dialogue_flow` 是挂在 `TASK_NAME_CONFIGS` 某个 task cfg 里的任务级对话收尾补充流，不是新的全局配置层。
- 它只在 runner 已经进入 `pending_interaction` / 对话窗口期间生效。
- 它的职责是“在正常 `JumpBtn` 之前，先执行一小段额外 UI 步骤”，例如先点任务详情里的某个按钮。
- 这类需求优先落在任务配置；只有当现有 runner 完全没有承载点时，才需要改 `scripts/AvePointLeveling.lua`。

### 3.2 `OBJECTIVE_POINT_CONFIGS`

按世界坐标触发目标点逻辑。

典型用途：

- 某个 Boss 房间到了指定区域后切入 kite
- 某个清怪房需要“怪清空并稳定一段时间”才算完成
- 某个 Boss 房复活后需要重新进门
- 主线 Boss 房这种“到任务寻路终点后刷 Boss / 切任务详情”的场景，要同时在 `TASK_NAME_CONFIGS` 和 `OBJECTIVE_POINT_CONFIGS` 配同一个 objective key，并把“寻找 Boss”和“打败 Boss”两段文本都放进约束，避免 detail 切换时漏触发。
- 如果只拿到两个风筝点，当前 runner 的 configured kite 至少需要 3 个点；优先在配置里写成 A/B/A/B 四点循环，不要为了单个 Boss 先改全局 kite 生成逻辑。
- 如果某个主线 Boss 需要原地打，不要新加全局“站桩模式”；优先复用 `boss_kite`，把 `kite_radius=0` 且 `kite_points` 写成同一个终点重复 4 次，让 runner 只保持原地补战斗 pulse。
- 终点循环跑打统一走 `boss_kite + kite_points`。配置了 `kite_points`、`seamless_kite=true`、`immediate_kite_on_reached=true` 或显式 `async_route_worker=true` 的 boss_kite，会自动交给 `AvePointLevelingNavWorker.lua` 的 `route_loop` 独立寻路；主 runner 只负责怪物、任务、死亡、后续 UI 判断。
- 终点 Boss 需要“死后先捡东西再进入后续 NPC / UI”的场景，优先在对应 objective 上加 `post_combat_loot`，不要写成全局固定拾取。该阶段只在怪物清场后短暂按拾取键，并在结束后继续原来的 `followup_route_action_key` / 任务刷新。
- 个别 Boss 如果不适合异步路线循环，可以在该 task/objective 配置上显式写 `async_route_worker=false`，不要改全局 worker 开关。

这层是“空间位置 -> 特殊 objective 行为”。

### 3.3 `ROUTE_POINT_ACTIONS`

补充：
- 除了电梯 / NPC / Portal / Gather 这类固定点交互，`ROUTE_POINT_ACTIONS` 也可以承载“坏路径点局部纠偏”。
- 这类纠偏推荐使用 `recorded_route_point`：命中 trigger 后按配置 waypoint 序列走一段录制路径，走完再重新 call 主任务按钮。
- 单点卡住纠偏可以只配一个 waypoint：trigger 放坏寻路卡点，waypoint 放脱困点，走到后让 runtime 自动重新 call 主任务。
- 如果任务面板还有主线文本但 `GetMainTaskPath` 已经没有路径，可给该 `recorded_route_point` 显式加 `allow_without_task_target = true`；runner 只会在无 `task_target` 的等待阶段扫描带这个开关的 route action，不会放开所有 route action。
- `recorded_route_point` 不是纯跑点；如果这段路本来需要沿路清怪/跑打，runtime 也应该保留 nearby-monster / combat pulse。
- 某些“任务终点后再去手工 NPC 坐标对话”的场景，不一定靠 route action 自己扫 trigger；也可以由 `objective.followup_route_action_key` 在 `task_reached` 后主动 arm 一个 `npc_dialogue_point`。
- 如果任务终点那一刻 `detail` 不稳定、为空，或者实际收尾阶段更像“固定房间终点”，这类 follow-up 优先挂到 `OBJECTIVE_POINT_CONFIGS.followup_route_action_key`，不要只挂 `TASK_NAME_CONFIGS`。
- 但如果终点房间附近还有杂怪，`OBJECTIVE_POINT_CONFIGS` 会把角色拉进 `boss_kite/clear_room`，而任务 detail 又会很快切成“和某 NPC 交谈”，这时更稳的是直接配一个带 `task_detail_patterns + trigger` 的 `ROUTE_POINT_ACTIONS.npc_dialogue_point`，让 detail 一更新就接管到手工 NPC 坐标。
- 如果问题只发生在“固定任务 + 固定坐标附近”，优先落在这里，不要先改全局 follow / stall 恢复逻辑。
- 2026-04-21 note: `叹息之墙 / 和阿瑞娅交谈` 当前手工对话锚点是 `(18017,-2362,403)`，实现继续落在任务局部 `ROUTE_POINT_ACTIONS.npc_dialogue_point`；不要误挂到后续 `吟游者之手`，也不要把它抽成 `叹息之墙` 的全局 NPC 坐标规则。
- 2026-04-21 note: `自由的焰火 / 和伊尔莎交谈` 当前手工对话锚点是 `(19020,-6640,606)`，实现继续落在任务局部 `ROUTE_POINT_ACTIONS.npc_dialogue_point`；最近日志里到点后附近文本会漂到 `狂战士之手`，不能把这个当成任务已变化后直接 call 主线，也不要把该 NPC 对话挂到 `狂战士之手` 全局规则。
- 2026-04-21 note: runner 现在多了一层局部 `loading_transition_reacquire` 恢复状态。只有在刚经历 `loading_state / main_interface_false_wait` 后，恢复出来的玩家位置与旧 `task_path/task_target` 明显脱节时，才会强制 `schedule_task_refresh_after_transition(... force_task_call=true)` 重取主线；不要把这层恢复改成普通 `follow_task` 的全局重刷条件。
- 2026-04-21 note: `treasure_empire_ashes_wolf_ambush_entry` 当前要求 `inside_detect_task_panel_text=false`。外面跑主线到入口附近时，task/detail 可能短暂出现“前往藏宝地：曙光大道”，这不能当成“已经在藏宝地里面”；inside 判定继续依赖 inside map / spawn / route 等更硬的信号，不要改回全局 task panel 文本直判。
- 2026-04-21 note: 藏宝地 `treasure_path` 现在复用 `AvePointLevelingNavWorker.lua` 的 `path_route` 数组路线 worker；Treasure 模块只负责激活/记录/注入路线目标，不再退回每 tick 单点 MoveTo。后续如果要调丝滑度，优先调 treasure cfg 上的 `route_worker_move_interval_ms` / `route_arrive_tolerance`，不要改主线 `task_path` 的 call/刷新逻辑。
- 2026-04-21 note: `treasure_empire_ashes_wolf_ambush_entry` 必须刷到 `target_level=38` 才允许走出口。38 级前重刷门只能点 `求生之欲/MapTrapBtn`，禁止 `fallback_interact`，否则会按 E 点到出口门；如果误落到外部 `exit_landing`，`wait_restart` 会回到 `pending_entry` 重新进藏宝地。配套入口 route action 的 `skip_when_player_level_at_least` 也必须保持 38，避免达标后又被拉回藏宝地入口。
- 2026-04-21 note: `treasure_empire_ashes_wolf_ambush_entry` 的入口、路线、Boss 点、出口门和 `exit_landing` 已有日志支撑；离开门触发锚点按用户 F6 更新为 `16509,-12043,105`，重刷门触发锚点按用户 F6 更新为 `17066,-12015,105`，但 `求生之欲` 按钮 F8 和真实 `restart_landing` 仍缺数据。不要再把 Boss 后枚举到的 `16457.17,-12098.53,105` 当成已验证重刷门，它在最近日志里匹配的是 exit EPortal。
- 2026-04-21 note: 藏宝地点击离开门不等于完成。`record.completed` 只能在 `wait_exit` 确认 `exit_landing` 后写入；否则会在离开门附近释放藏宝地控制权，导致主线过早 `call task`。
- 2026-04-21 note: 藏宝地有一层窄恢复：如果持久化记录已经 `completed=true`，但角色仍在该藏宝地 `exit` 门触发范围且没有到 `exit_landing`，runner 会撤销这次 premature completed，重新进入 `post_boss_portal` 点离开门。不要把这层扩成全局 completed 重置。
- 2026-04-21 note: 藏宝地 `wait_exit` 如果配置了 `exit_landing`，但离开门点击后的 settle 超时仍未到落点，会回到 `post_boss_portal` 重试 exit 门，而不是继续等死或交还主线。
- 2026-04-21 note: `treasure_new_sprout_hill_entry` 的 `route_store_key` 是 `treasure_new_sprout_hill_entry_v2`，当前持久化状态里 `route_acquired=false/run_count=0`，属于已配入口/Boss/门数据但还没完成首轮路径采集；配套入口 route action 的 skip key 也必须继续用 `_v2`，不要改回 cfg key。

按任务文本 + 坐标触发路线动作。

典型用途：

- 电梯按钮
- 固定点 NPC 对话
- 固定点 PortalBtn
- 固定点 GatherBtn
- 某条任务链里的多段交互流

注意：这不是普通 route point 列表，而是“到点后执行一段动作”的配置。

### 3.4 `MAP_TASK_CONFIGS`

按当前地图名挂载特殊逻辑。

典型用途：

- 地图内 portal transition
- 地图级 objective
- 地图级 revive_reentry

### 3.5 `TASK_OBJECTIVE_BUTTON_STEPS`

定义任务目标按钮的 UI 识别步骤，比如：

- `GatherBtn`
- `FunctionBtn`
- `TransportBtn`
- `JumpBtn`

这是“到点后怎么点按钮”的通用识别库。

补充：
- 如果某些 call 任务到终点后只是 `FightInteractiveView` 里的按钮名发生变体，例如同一位置有时是 `FunctionBtn`，有时是 `TransportBtn`，优先扩充 `TASK_OBJECTIVE_BUTTON_STEPS`，不要先改 runner 的 `task_reached` 主流程。
- 这类全局任务目标按钮优先用稳定的 `hint_client_x/y + include_patterns` 做可见性匹配；不要把 F10/F11 临时生成、且锚到血量/动态文本的 `distance_anchor_exact_text` step 直接抄进全局配置。
- 如果只有某个任务终点需要精确锚定特定文本按钮，优先把 locator 放到该 `objective.button_steps`。一旦配置了局部 `button_steps/button_step`，runner 默认只扫这些局部按钮，不会再退回全局 `TASK_OBJECTIVE_BUTTON_STEPS`，除非该 objective 显式写 `include_global_button_steps=true`。这种局部步骤可以带 `distance_anchor_exact_text`，避免污染全局 GatherBtn/FunctionBtn 规则，也避免精确按钮没出现时误点通用 `FunctionBtn`。
- 如果终点按钮 objective 期间任务名/detail 会短暂漂移但 objective key 没变，可在该 objective 上显式写 `ignore_terminal_text_change_when_objective_same=true`；不要为单个任务放宽全局任务更新判断。

### 3.6 `GUIDE_SKIP_STEP` / `GLOBAL_TASK_PORTAL_STEP`

- `GUIDE_SKIP_STEP`：新手引导跳过按钮
- `GLOBAL_TASK_PORTAL_STEP`：任务链上的通用 PortalBtn

### 3.7 `TREASURE_DUNGEON_CONFIGS`

藏宝地专用配置，包含：

- 激活条件
- 入场步骤
- 路线记录
- Boss 战
- 掉落拾取
- Restart / Exit 门
- 返回主线条件

这部分不是“附属小功能”，而是一个独立 executor。

补充：
- 如果当前只拿到了新藏宝地的入口坐标，还没拿到任务文本、面板 query、Boss 点、Restart / Exit 门、落地点这些完整数据，先在 `TREASURE_DUNGEON_CONFIGS` 里加一个 `enabled=false` 的占位 cfg。
- 这种占位 cfg 至少要落真实 `entry_trigger` 和唯一 `key/route_store_key`，并在 `notes` 里写清缺哪些数据；不要拿半套假数据直接启用。
- 如果某条主线任务本身不会把人精准带到藏宝地入口，但你已经知道固定入口坐标，可以配一个任务约束下的 `ROUTE_POINT_ACTIONS.recorded_route_point` 把角色先拉到入口，再让 `TREASURE_DUNGEON_CONFIGS` 在入口 trigger 处接管。
- 如果确认只是“同一类藏宝地的另一处外部入口”，可以先复用上一条藏宝地的 Boss / Portal / landing 逻辑，但要在 `notes` 里明确写这是临时假设，并观察首轮日志验证。
- 当前藏宝地 Boss 死亡判定只看 `EnumPortal()` 是否遍历到配置门坐标附近的 `EPortal`；匹配到后进入 `boss_loot`，并用 `boss.clear_settle_ms` 作为“门已出现后的掉落缓冲”，UI 按钮探测只用于后续点门，不再用于判定 Boss 死亡。
- 如果 Boss 已交战、怪物枚举暂时为 0、但 `EnumPortal()` 还没看到门，treasure runner 会继续按 Boss 房风筝点巡航探测；不要把这段等待改回“零怪即清场”。

## 4. 真实运行逻辑

### 4.1 启动

`scripts/AvePointLeveling.lua` 的 `start(ctx)` 会：

- 重置 runner 状态
- 初始化导航
- 当前版本默认使用 `AvePointLevelingNavWorker.lua` 的 `path_route` 模式做普通任务跟随；主 runner 发布从当前最近点到终点的剩余任务路径段并暂停/恢复 worker
- 尝试恢复藏宝地断点
- 打开启动期状态探测窗口和启动期 Boss 交战窗口

### 4.2 每 tick 更新

补充：
- `ROUTE_POINT_ACTIONS` 不只是按钮点击；某些任务在固定坏路径点也会先被 route action 临时接管成一段录制路线，结束后再把主线任务按钮重新 call 回来。
- 某些藏宝地入口引导也会挂在 `ROUTE_POINT_ACTIONS.recorded_route_point` 上；如果藏宝地有等级门槛，入口引导必须同步配置 `skip_when_treasure_completed_key / skip_when_player_level_at_least`，否则 treasure runner 已停用后仍可能被主线路径局部接管带回入口。

`update(now, ctx)` 大体是下面这个顺序：

1. 检查 nav 是否可用
2. 处理 loading / potion / startup state
3. 先给藏宝地模块机会接管
4. 如果没有 `task_target`，尝试点主线任务按钮
5. 等待任务路径返回；如果任务属于 `entry_action=world_map_send`，则执行“开地图 -> 选点 -> Send”
6. 刷新 `task_target/task_path`
7. 如果仍然没有目标，处理低优先级 UI，然后进入等待
8. 如果有目标：
   - 先处理 `ROUTE_POINT_ACTIONS`
   - 再处理地图级 transition
   - 再处理全局任务 portal
   - 再处理战斗完成判定 / 强制 kite
   - 如果到达 objective ready distance，则走 `task_reached`
   - 否则继续 follow task
9. 如果已经进入对话/交互窗口，则可能先执行 task cfg 上挂的 `dialogue_flow`，然后才走正常 `JumpBtn`
10. follow 过程中如果卡住，会触发重试、任务按钮刷新、路径偏离刷新、path loss 刷新、idle 刷新；其中有一条更直接的 watchdog：如果角色在 follow task 期间连续约 6 秒没有真实坐标变化，会主动重新 call 主线任务按钮以重取任务寻路
11. 移动过程中会补发战斗 pulse，兼顾沿路怪和近身怪

结论：它是“任务按钮驱动 + 任务路径驱动 + 空间动作点驱动 + UI 可见性驱动”的混合状态机，不是简单巡路脚本。

## 5. 配置命中优先级

### 5.1 任务配置命中

优先走 `scripts/AvePointLevelingPolicy.lua`：

- 先用 `current_task_name` 查
- 查不到再用 `current_task_detail` 查
- 会尝试去掉任务名前缀里的“主线”

所以：

- 不能只看任务名，不看任务详情
- 不能把 `task_name` 和 `task_detail` 的职责粗暴合并

### 5.2 objective 选择顺序

当前 objective 来源优先级是：

1. `task_cfg.objective`
2. `map_cfg.objective`
3. `OBJECTIVE_POINT_CONFIGS` 命中的空间点

### 5.3 藏宝地目标覆盖

在正常刷新主线 `task_target` 前，会先给藏宝地模块一个 `provide_task_target_override()` 的机会。

所以：

- 看到 `task_target` 被替换，不一定是主线逻辑出错
- 可能是藏宝地在 `grinding` 阶段主动注入路线目标

## 6. 关键状态，不要随便破坏

### 6.1 主线任务刷新相关

- `state.task_target`
- `state.task_path`
- `state.last_task_button_click_at`
- `state.task_path_wait_until`
- `state.next_task_refresh_at`
- `state.require_task_button_refresh`
- `state.task_update_wait_until`
- `state.last_position_change_at`

这些字段共同约束“什么时候能重新点任务按钮、什么时候必须等路径、什么时候必须等 UI settle”。不要只删一个判断。

补充说明：

- `state.last_position_change_at` 是 follow 期间“角色真实坐标最近一次变化”的 watchdog 锚点。
- 它和 `state.last_progress_at` 不是一回事：后者偏向进度/路线前进判定，前者偏向“角色有没有真的动起来”。

### 6.2 任务进场动作相关

- `state.task_entry_action_button_click_at`
- `state.task_entry_action_center_clicked_at`
- `state.task_entry_action_pre_clicked_at`
- `state.task_entry_action_send_clicked_at`

这是一整套 `world_map_send` 会话状态。它在执行时会主动压制新的任务按钮点击。

### 6.3 任务名缓存相关

runner 会把当前任务名和详情发布到全局，并且缓存最后一个“非泛化任务名”。

原因：

- 当前任务名可能退化成“主线 XXX”
- 或者暂时显示成地图名

所以它需要 `AVEPOINT_LAST_TASK_NAME / AVEPOINT_LAST_TASK_DETAIL` 兜底。不要把这套缓存删掉。

### 6.4 藏宝地运行态

藏宝地状态在 `state.treasure_runtime`，持久化文件是 `scripts/avepoint_treasure_state.lua`。

因此：

- 练级 stop 时会保存藏宝地 resume
- 下次 start 时可能直接恢复藏宝地流程

## 7. 改动应该落在哪一层

补充：
- 新增某段坏寻路纠偏（固定 trigger 点 -> 录制路径 -> 回调任务按钮）：优先改 `ROUTE_POINT_ACTIONS`。
- 新增某个任务“到终点后转去手工 NPC 坐标对话”：优先改 `TASK_NAME_CONFIGS.objective.followup_route_action_key`，再配一个 `ROUTE_POINT_ACTIONS.npc_dialogue_point`。
- 如果这个“到终点后转去手工 NPC 坐标对话”发生在固定房间终点，且运行时 `detail` 可能为空或滞后，优先改 `OBJECTIVE_POINT_CONFIGS.followup_route_action_key`，不要只靠 `TASK_NAME_CONFIGS`。
- 如果这段房间终点附近还会残留杂怪，导致 `OBJECTIVE_POINT_CONFIGS` 把角色拉进 `boss_kite/clear_room`，优先改成 `ROUTE_POINT_ACTIONS.npc_dialogue_point + task_detail_patterns + trigger`，不要硬塞一个房间 clear 配置。
- 新增某个藏宝地但目前只知道入口坐标：先改 `TREASURE_DUNGEON_CONFIGS`，加一个 `enabled=false` 的占位 cfg，等 Boss / 门 / 落地点 / 任务文本补齐后再启用。
- 新增某个主线任务需要先偏航去固定藏宝地入口，再由藏宝地模块接管：优先改 `ROUTE_POINT_ACTIONS.recorded_route_point` 做入口引导，再改 `TREASURE_DUNGEON_CONFIGS`。

- 新增某个任务的 Boss 房特殊处理：优先改 `scripts/AvePointLevelingConfig.lua`
- 某个藏宝地的门按钮本体和 hint 点已经稳定，但 `distance_anchor_exact_text` 偶发漂移，导致 `boss portal probe` 误判没刷门：优先在该 portal `step` 上加 `prefer_hint_fallback = true`，不要先改 `scripts/AvePointLevelingTreasure.lua`
- 新增某个任务的“开地图 -> 选点 -> Send”：改 `TASK_NAME_CONFIGS`，必要时配 `selection_step`
- 新增某个任务在对话收尾阶段、`JumpBtn` 前的额外按钮步骤：优先改 `TASK_NAME_CONFIGS` 里的 `dialogue_flow`
- 新增某个电梯 / NPC / Gather / Portal 固定点动作：改 `ROUTE_POINT_ACTIONS`
- 新增某个房间坐标触发的 Boss / 清怪判定：改 `OBJECTIVE_POINT_CONFIGS`
- 新增某类 call 任务到终点后的通用交互按钮变体（例如 `TransportBtn`）：优先改 `TASK_OBJECTIVE_BUTTON_STEPS`
- 调整任务文本匹配策略：改 `scripts/AvePointLevelingPolicy.lua`
- 调整整体 tick 流程、刷新节流、卡住恢复、战斗补发：改 `scripts/AvePointLeveling.lua`
- 调整藏宝地完整流程：改 `scripts/AvePointLevelingTreasure.lua`
- 只是为了复用配置构造模式：改 `scripts/AvePointLevelingActions.lua`

## 8. 最容易改错的地方

补充：
- 不要把某个任务上的单点坏寻路，直接修进全局 follow / retry / stall 恢复逻辑；如果它只发生在固定任务和固定坐标附近，优先考虑 `ROUTE_POINT_ACTIONS.recorded_route_point` 这种局部接管。

1. 不要把 `release/` 或 `.luac` 当成源文件改。
2. 不要把 `AvePointStandalone.lua` 当成练级逻辑本体，它只是入口壳。
3. 不要看到“没有 task_target”就判定成“没有任务”。它也可能只是还在等任务按钮结算、任务路径返回、地图传送、对话收尾。
4. 不要在 `task_entry_action` 期间强行重新点主线任务按钮。
5. 不要删除各种 `settle_ms / wait_until / retry_ms / cooldown`，这些基本都是在压 UI race condition。
6. 不要把 `ROUTE_POINT_ACTIONS` 理解成普通导航点，它们本质上是“到点执行动作”。
6.1. 不要只改 `TREASURE_DUNGEON_CONFIGS.target_level` 就以为外部入口引导会自动停；如果有配套的 `recorded_route_point`，也要加等级/完成态 skip gate。
6.2. 当前 `treasure_milu_creek`（藏宝地：蜜露溪谷）退出门槛是 `target_level = 25`；日志里如果看到 `level=25 target_level=26`，说明配置被改回去了。
7. 不要把 Boss 房逻辑只塞进 `TASK_NAME_CONFIGS`，很多行为还依赖 `OBJECTIVE_POINT_CONFIGS` 的空间触发。
8. 不要忽略 `task_detail_patterns`。很多任务名重复，真正区分靠 detail。
9. 不要批量“修复中文字符串”或重写 Unicode 转义。这里有很多精确匹配文本，终端里看到乱码不等于源码有问题。
10. 不要忘记藏宝地会暂停主线任务刷新，并且可能覆盖主线目标。
11. 不要把 `dialogue_flow` 当成全局 UI 扫描器。它应该只服务于“某个任务在对话窗口里的前置补充步骤”，而不是替代 `ROUTE_POINT_ACTIONS`、`TASK_OBJECTIVE_BUTTON_STEPS` 或通用 low-priority UI 逻辑。
12. 不要因为某个藏宝地 Boss 房掉落偶发慢一拍，就直接改 treasure runner 的 `boss_loot` 状态机；优先先在对应藏宝地 cfg 上调 `loot_stuck_max_attempts / loot_press_interval_ms` 这类局部参数。
13. 不要再用 `monster_count == 0` 或 `clear_settle_ms` 推断藏宝地 Boss 已死；现在 Boss 完成信号是 `EnumPortal()` 枚举到配置门，`clear_settle_ms` 只允许作为门出现后的掉落缓冲。

## 9. 建议新 AI 的阅读顺序

1. `config.json`
2. `AvePointStandalone.lua`
3. `scripts/AvePoint.lua`
4. `scripts/avepoint/shared.lua`
5. `scripts/avepoint/hotkey.lua`
6. `scripts/AvePointLeveling.lua`
7. `scripts/AvePointLevelingConfig.lua`
8. `scripts/AvePointLevelingActions.lua`
9. `scripts/AvePointLevelingPolicy.lua`
10. `scripts/AvePointLevelingTreasure.lua`

## 10. 可直接发给新 AI 的提示词

补充提示：
- 如果需求是“某个任务在固定坐标附近原生寻路会卡住，需要手动走一小段录制路径再回主线”，优先考虑 `ROUTE_POINT_ACTIONS` 里的 `recorded_route_point`。

```text
先不要直接改代码，先按下面顺序读文件并复述调用链和改动落点：

1. config.json
2. AvePointStandalone.lua
3. scripts/AvePoint.lua
4. scripts/avepoint/shared.lua
5. scripts/avepoint/hotkey.lua
6. scripts/AvePointLeveling.lua
7. scripts/AvePointLevelingConfig.lua
8. scripts/AvePointLevelingActions.lua
9. scripts/AvePointLevelingPolicy.lua
10. scripts/AvePointLevelingTreasure.lua

这是一个“任务按钮驱动 + 任务路径驱动 + 空间动作点驱动 + UI 可见性驱动”的练级状态机，不是简单巡路脚本。

请遵守这些约束：
- 不要改 release/ 或任何 .luac
- 不要把入口壳文件当成主逻辑
- 不要删除等待/结算/重试时间
- 不要忽略 task_detail_patterns
- 不要在 task_entry_action 或 treasure 接管期间乱点主线任务按钮
- 如果需求是“某个任务在对话窗口里、JumpBtn 前先补一步按钮”，优先考虑 `TASK_NAME_CONFIGS.dialogue_flow`
- 先判断需求应该落在 runner、config、policy、actions、treasure 哪一层，再动手


```


补充硬性约束：
- `boss_kite`/终点循环跑打的退出要以任务面板或按钮刷新确认的任务更新为准；`nearby_text` 只是候选文本，跑打期间不能单独触发 `boss task changed`，否则会在 Boss 未死时提前停跑打并乱 call 主线。

## 10.1 Current Hard Requirements

- Prefix-only terminal title drift (`XXX` <-> `主线 XXX`) follows the same rule as other terminal title drift: if locked `detail` is unchanged during `task_reached` / `interaction_prompt`, keep the NPC/interaction flow alive and do not schedule `task_changed` refresh. The guard belongs in `scripts/AvePointLeveling.lua::maybe_handle_terminal_task_change()`.
- `task_reached` / `interaction_prompt` 阶段，如果当前 `detail` 和终点锁定的 `detail` 一致，只是 `task` 标题在“主线任务名”和 detail 文本之间切换，这属于 UI 文本漂移，不能当成任务完成或新任务刷新；必须继续走 NPC/交互流程。只有 `detail` 或 `objective` 真变化时，才释放旧接管并重新 call 主线任务。

- F8 是只读诊断热键，只打印当前鼠标在游戏窗口客户区内的坐标、比例、屏幕坐标和窗口尺寸；它不能启动/停止任何业务状态，也不能影响主线 call 任务。
- 鼠标相对坐标点击属于 action。个别任务详情按钮如果 UI 枚举不稳定，且需求是 NPC 对话结束后再点按钮，必须通过 `AvePointLevelingActions.make_post_dialogue_flow_task` + `make_fixed_client_click_step` 挂在该任务的局部 `post_dialogue_flow.steps`；点击必须走拟人鼠标移动，不要放进 objective 或全局按钮规则，也不要让所有对话任务默认执行。
- Startup must keep the original behavior: after starting the leveling runner, first reacquire/call the current main task from the task panel and fetch `GetMainTaskPath/GetMainTaskPos`; do not let treasure, route actions, PortalBtn, revive reentry, or other special actions steal this first call.
- Post-transition global PortalBtn suppression is transition-scoped state. It must survive `clear_task_target_state()` because transition recovery intentionally clears stale task targets before the new main-task call/path comes back. Reset it only from full runner reset, otherwise a return portal can be clicked immediately after map load.
- Global PortalBtn is not allowed to click every visible `PortalBtn`. It must pass main-task relevance checks first: by default the current path target must be near the player and the current goal must not be a far cross-map objective. Task-local overrides are `global_portal_max_target_distance`, `global_portal_max_goal_distance`, and `global_portal_allow_far_goal`; only use them for tasks whose real main-task portal is known to appear while the goal is far.
- `force_task_call_after_transition:*` is also transition-scoped reacquire state. If a forced main-task call fails to return a real `task_path`, ordinary retry clicks must keep rejecting `task_pos` until a fresh path is adopted or the guarded retry window expires; otherwise stale `task_pos` can be accepted as `task_reached` and the runner will loop in NPC/dialogue handling instead of calling the next route.
- During startup reacquire, stale boss `task_pos` is not enough. If startup detects a far boss `task_pos`, the refresh branch must ignore `task_pos`, click the main task button, and briefly reject `task_pos` while waiting for a real `task_path`.
- `revive_reentry` is death-only. It may run after `resume_after_revive` sets `state.revive_reentry_pending`; it must not auto-arm on ordinary startup, ordinary task follow, or because a portal button happens to be visible.
- For death reentry that needs task pathing, model it as an action option: call task first, follow the task path toward the portal, then click PortalBtn from the reentry action. Do not replace normal startup call-task behavior.
- 主线任务是最高优先级。启动脚本、过图后、任务/detail/objective 更新后，都必须能回到“call 当前主线任务 -> 获取新 task_path/task_pos -> 正常寻路”的主流程；任何藏宝地、PortalBtn、复活回门、route action 都不能长期抢占主线。
- 启动脚本后的第一件业务动作必须是 call 当前主线任务。所有 config/action/objective/treasure/boss_kite/route action 的接管都只能发生在首次主线 call 之后；如果首次 call 后暂时只有 `task_pos`、没有 `task_path`，必须继续等待/重试主线 call，不能让配置立即按终点逻辑执行。
- 启动期不能把 `GetMainTaskPos` 返回的 `task_pos` 直接当作已到终点处理。即使距离是 0，也要先 call 当前主线任务并等待新 `task_path`；否则会卡在旧任务终点反复交互。
- 任务/detail/objective 已经变化时，不要继续使用旧 `task_pos`、旧 `task_path`、旧 boss_kite 目标或旧 route action 状态。先清旧目标，再 call 主线任务；等新路径有效后再继续。
- 点击 PortalBtn、世界地图 SendBtn、LiftBtn、MapTrapBtn 这类可能改变地图/楼层/任务状态的 action 后，必须按“状态切换”处理：等待结算/加载，拒绝旧地图 `task_pos`，重新 call 当前主线任务。不要把旧终点当成新任务目标。
- 普通 `follow_task` 连续寻路不能被单个任务修复影响。不要为了修某个卡点增加全局 sleep、全局 hold、全局 MoveTo 节流或全局交互重试；需要等待时必须放在具体 action/config 上。
- 普通任务平滑寻路结构是 runner 发布从当前最近点到终点的剩余任务路径段到 `AvePointLevelingNavWorker.lua` 的 `path_route` 模式，worker 按路径点独立推进 `MoveTo`；UI/NPC/Portal/固定鼠标点击等必须停步的阶段用 `hold_navigation` 暂停 worker，不要把普通移动改回主循环直发或高频单目标重发。
- 终点跑打、藏宝地、复活回入口都是可插拔接管逻辑，只能在匹配到自己的 task/detail/map/坐标条件时接管；任务刷新或 detail 变化后必须释放接管，让主线继续。
- F7/detail/task 匹配只能使用当前 UI/API 实际读到的信息。不要为了让某个配置匹配而硬推断 detail；如果 UI 暂时读不到 detail，用 task+map+坐标做局部 fallback。
- 如果新行为是通用/可复用能力，先判断是否应该放进 `AvePointLevelingActions.lua` 做 action builder；任务专属坐标和开关留在 config，不要塞进 runner 分支。
- 修改 runner 执行顺序时，必须逐项检查：启动 call 主线、死亡/复活 pending、task_entry_action、藏宝地接管、route point action、global PortalBtn、boss_kite、战后捡物。修一个阶段不能改变另一个阶段的默认顺序。
- 高风险边界层改动原则：启动期必须先成功 call 主线才允许 objective/follow/跑打接管；PortalBtn、ExitPortal、世界地图 SendBtn、map transition、LiftBtn 这类状态切换完成后必须强制重新 call 主线，并在等待窗口内拒绝旧 `task_pos`。

## 10.2 Current Fix Notes

- 2026-04-22: Fourth treasure `treasure_fourth_entry_5643_-530_v1` (`藏宝地：隐世金阁`) is now enabled for entry / route capture, following the other working treasure baselines. Outside entry is `(5642.69,-530.40,503)`, target level is `46`, and the entry should follow the same two-step chain as the other treasures: doorway `MapTrapBtn`, then world-map `SendBtn`. Keep the current route action `abyss_below_fourth_treasure_entry_5643_-530` as entrance-only guidance for `深渊以下 / 继续追寻莱安的踪迹`; once inside the treasure `entry_trigger`, the treasure module should take over before normal route actions. Boss / restart / exit / landing data is still incomplete, so later work should focus on exact entry locator, reliable outside/inside map names, inside route capture, boss anchor / kite points, restart portal, exit portal, restart_landing, and exit_landing.
- 2026-04-22: Fourth treasure entry `treasure_fourth_entry_map_trap_placeholder` is intermittently not enumerable even while the character is already standing at `(5642.69,-530.40,503)`. Two runs showed successful `MapTrapBtn` clicks around client `(711,733)` / `(698,727)`, but another run looped on `Nearby button not found`. Current local mitigation is task-local only: update the hint to the measured `(698.20,727.44)` area, widen the hint distance, and allow an entry-step `fallback_interact` (`D`) only for this treasure doorway. Do not generalize this to all treasure entries or global portal handling.
- 2026-04-22: The fourth treasure doorway `fallback_interact` is now hard-scoped in runtime code as well as config: it only applies when `cfg.key=treasure_fourth_entry_5643_-530` and `step.key=treasure_fourth_entry_map_trap_placeholder`. Keep it that way; do not widen this branch to other treasure entries or any mainline task interaction.
- 2026-04-22: Latest logs proved the fourth treasure still had a false-enter problem: after clicking the doorway, `entering -> panel_query_detected -> acquire_path` captured the outside `深渊以下` mainline path instead of a real inside-treasure route. The bad persisted route started at `3050,7050,503`, which is the outside mainline goal. Current mitigation is local to this treasure only: reject any acquired route whose first point falls near `3050,7050,503`, and clear the bad persisted route/resume snapshot. Do not generalize this reject-point rule to other treasures or any mainline routing logic.
- 2026-04-22: To align the fourth treasure with the other treasure state machines, keep its entry/enter/acquire flow config-driven: add `inside_map_patterns={"隐世金阁"}` and set `enter_detect_task_panel_query=false` only for this treasure, because its side-task panel is already visible outside the dungeon. If later F7 confirms a different real inside map name, update `inside_map_patterns`; do not re-enable panel-query enter detection globally.
- 2026-04-22: Latest live logs confirmed the fourth treasure was still missing the second entry step: it clicked the doorway and immediately finished `entry_chain_completed`, so no `SendBtn` probe ever happened. Keep this treasure aligned with the other three by preserving a two-step entry chain (`MapTrapBtn -> SendBtn`) and clear any saved `resume=pending_entry` snapshot before retesting, otherwise the next run may resume into stale half-entry state.
- 2026-04-22: Latest live logs then confirmed a second fourth-treasure gap: after `SendBtn` clicked successfully, the character landed near `(-150,-200,56)` and stayed in `entering`, so it never reached the normal `acquire_path` phase that clicks the treasure side task and captures/saves the inside route. Current local fix is to treat that measured post-send landing as `inside_landing` for this treasure only. Do not again assume that restoring `SendBtn` alone is enough.
- 2026-04-22: Repeat-error guard for the fourth treasure: before changing its flow, verify three checkpoints against the other working treasures and latest logs in order: `entry_steps` must still be two-step (`MapTrapBtn -> SendBtn`), `entering` must have a real inside signal (`inside_map_patterns` or `inside_landing`), and `acquire_path` must remain the stage that calls the treasure task panel to fetch and persist the route. Do not collapse any of these stages based on assumption.
- 2026-04-22: Fourth treasure boss-room data now has measured anchors: boss center `10670.88,18175.38,-664`, kite points `11711.53,19285.31,-664` / `9807.94,19361.28,-664` / `10670.88,18175.38,-664`, and restart portal trigger `11010,19761,-664`. Current restart button matching intentionally reuses the existing `求生之欲/MapTrapBtn` baseline with `fallback_interact=false`; do not widen this until real F8/F10 restart-button logs or exit-portal data are available.
- 2026-04-22: Fourth treasure now also has measured exit-loop anchors: exit portal trigger `10152,19705,-664`, `restart_landing` reuses the measured first inside landing `(-150,-200,56)`, and `exit_landing` reuses the original outside entrance `(5642.69,-530.40,503)`. Keep these tied to the fourth treasure only; button locator baselines for restart/exit are still placeholders until real F8/F10 logs are captured.
- 2026-04-22: User later provided F7 portal data `EPortal @ 10150,19750,-664` for the fourth treasure boss room. This matches the previously supplied exit-door stand point `10152,19705,-664`, not the restart door `11010,19761,-664`, so treat it as the true exit portal and as a valid boss-death / portal-ready signal for this treasure. Prefer the F7 EPortal coordinate over the nearby F6 player stand point when configuring enum-portal detection.
- 2026-04-22: `abyss_below_continue_ryan_trace_route_3920_-6148` is a task-local `recorded_route_point` for `深渊以下 / 继续追寻莱安的踪迹`: when near `(3920,-6148,503)`, it walks `(3468,-5451,503)` -> `(2615,-4557,503)` -> `(1849,-4458.14,503)` and then reacquires the main task. This one intentionally does not require destination match because the live main-task destination is far away `(3050,7050,503)` while the bad path segment is local.
- 2026-04-22: `abyss_below_open_gate_gather_6828_-6416` is a task-local `ROUTE_POINT_ACTIONS.objective_button_flow_point` for `深渊以下 / 找到打开大门的方式`: when the main task destination matches the door endpoint, it moves to `(6827.58,-6415.66,505)` and clicks only the local `FightInteractiveView...GatherBtn`, then forces a main-task reacquire. Its `retry_ms=60000` is intentional so the same vanished GatherBtn cannot re-arm while the new objective path/detail is still refreshing. Keep this local; do not add GatherBtn to global endpoint handling.
- 2026-04-22: `深渊以下 / 找到打开大门的方式` now has a second task-local precise gather point near `(2496.23,7983.64,503)`. Keep it as a local `ROUTE_POINT_ACTIONS.objective_button_flow_point` with local `FightInteractiveView...GatherBtn` lookup and about `2.2s` settle time for the gather cast bar before `force_task_call_after_transition` reacquires the next main-task step. This point is intentionally `allow_without_task_target=true` because live logs can stall with repeated main-task clicks and no `task_path` while already standing near the gather spot. Do not move this behavior into global GatherBtn handling, and do not shorten the settle time unless live logs prove the cast bar is fully completed earlier.
- 2026-04-22: Live logs showed `深渊以下 / 找到打开大门的方式` can stall in `wait_task_path_after_button` with repeated main-task clicks and no `task_path`, so the precise gather point near `(2496.23,7983.64,503)` must be allowed to run without `task_target/destination` after the startup / forced-reacquire protection window expires. Keep this behavior opt-in through `allow_without_task_target=true`; do not reopen ordinary route actions during `wait_task_path_after_button`, and do not bypass the initial startup main-task call window globally.
- 2026-04-22: Tighten the above guard further: `allow_without_task_target=true` alone is not enough to let a route action run during `wait_task_path_after_button`. The runtime branch in that stage must also require a second explicit marker such as `allow_wait_task_path_recover=true`, and only the current `深渊以下 / 找到打开大门的方式` precise gather point carries that marker. This keeps the recovery local to this task and avoids widening the main-flow wait branch for other `allow_without_task_target` actions.
- 2026-04-22: `abyss_below_trace_ryan_anchor_3223_6136` is a task-local single-point `recorded_route_point` for `深渊以下` after the door gather step, scoped to the Ryan-trace details (`继续追寻莱安的踪迹` / `追寻莱安的踪迹`). It first walks to `(3223,6136,503)` and then reacquires the main task path. Keep it local to these details; it is a bad-path anchor, not a global follow-task fix, and it should not use the wait-task-path recovery branch.
- 2026-04-22: `深渊以下 / 击败莱安幻影` now uses a task-local `boss_kite` objective with three measured kite points `(-1549.53,25388.33,503)` / `(-116.72,24092.46,503)` / `(-2038.89,24231.98,503)`. This task can enter a no-`task_path` state (`GetMainTaskPath returned no usable points`) while the boss fight is already active, so its objective explicitly opts into `allow_no_task_target_force_kite=true`. Runtime support for this must stay opt-in and task-local; do not let ordinary boss tasks or generic no-target states auto-arm combat from nearby monsters.
- 2026-04-22: Follow-up fix for `深渊以下 / 击败莱安幻影`: wiring `allow_no_task_target_force_kite=true` only inside `maybe_handle_forced_kite_monster()` is not enough. The leveling loop must also explicitly call that path from both `wait_task_path_after_button` and `wait_task` when there is still no `task_target`; otherwise the task sits in repeated main-task reacquire and never enters boss kite. Keep this bridge opt-in and gated by the task objective flag only.
- 2026-04-22: `深渊以下 / 进入升华秘殿，追击基冈` now has a task-local two-point `recorded_route_point` override through `(-603,24916,503) -> (980,25022,503)`. This route is allowed to start even from `wait_task_path_after_button` with no `task_target`, but its completion must not widen generic recorded-route behavior: only actions that explicitly set `wait_task_refresh_before_reacquire_ms` may clear stale task targets and hold a short no-reacquire window so the task can refresh on its own before normal main-task reacquire resumes.
- 2026-04-22: `深渊以下 / 与校长德里克对话` now has a task-local single-point `recorded_route_point` anchor at `(-1599,23620,503)`. It is only meant to nudge the runner to that measured point and then release back to normal main-task reacquire; do not add wait-task-refresh behavior or no-target recovery flags to this anchor unless later logs prove this task also enters a `no task_target` state.
- 2026-04-22: `成神之日 / 击败吸收了灾烬和火种力量的基冈` now uses a task-local `boss_kite` objective with four measured kite points `(155.47,2093.02,1281)` / `(1450.77,3179.78,1281)` / `(-452.57,4007.07,1281)` / `(-800.64,2554.34,1281)`. This boss can also enter a no-`task_path` state right after the task refresh, so it explicitly opts into `allow_no_task_target_force_kite=true`. Keep this behavior task-local; do not turn generic no-target states into auto-combat for other tasks.
- 2026-04-22: `成神之日 / 进入升华秘殿，阻止基冈` now uses a task-local `task_objective_button -> world_map_send` chain: click the local `TransportBtn`, wait about `3000ms`, human-mouse click the measured client ratio `(0.428472, 0.496667)`, then locate `WorldMapDetail...SendBtn` and continue through the existing task-entry transition refresh. Runtime support for this is explicit opt-in via `objective.arm_task_entry_action_after_click=true`; keep it local to this task and do not change the default global `TransportBtn` behavior.
- 2026-04-22: `遗忘秘殿 / 击败特殊实验体基尔` 使用任务局部 `boss_kite` 三点跑打 `(9059.86,-248.59,88)`、`(10935.20,-409.86,88)`、`(10051.53,1707.82,88)`，在主线寻路接近终点时切入并持续到任务结束；只匹配该 detail 或 `特殊实验体基尔` 标题漂移，不要扩到整个 `遗忘秘殿`。该任务额外开启 `allow_nearby_text_task_change_exit`，只有同一个新任务标题连续软确认后才释放 sticky 跑打，用于处理 Boss 死后任务面板已刷到 `深渊以下` 但房间仍有杂怪的情况；不要把这个开关默认加到所有 boss_kite。
- 2026-04-22: `深渊以下 / 进入觉醒秘殿深处` 终点后不是直接刷新下一步；当前 task cfg 的 `objective.followup_route_action_key` 会 arm 局部 `recorded_route_point`，依次走 `(4340,-6246,491.31)`、`(3072.59,-6604.02,503)`、`(2037.36,-5959.17,503)`、`(2980.27,-5939.52,503)`，录制路线走完后再重新 call 主线。保持这个行为局部约束，不要改普通 `follow_task` 或全局 route action 扫描。
- 2026-04-21: `another_magic_academy_forbidden_guard_boss_room` has a death-only `revive_reentry` anchor at `(18306.24,8595.45,307.57)`. It should directly move to that door point, click `PortalBtn` or fallback `D`, then reacquire the main task; do not reuse it for startup, normal follow, or generic visible portal handling.
- 2026-04-21: `another_magic_academy_forbidden_guard_boss_room` now uses three endpoint kite points `(19680.23,8403.38,607)`, `(20886.94,7818.64,605)`, `(20750.76,9283.95,605)` with seamless `boss_kite` until the task changes/finishes. Keep this constrained to `另一个魔法学院 / 击败禁区守卫 / 守卫军领袖·阿尔克斯`; do not broaden it to other academy tasks.
- 2026-04-21: `world_map_send` entry actions now support optional `map_open_wait_jitter_ms`; the runtime samples it once when the action is armed and logs `map_wait/jitter_max`. Keep this as a task-local timing option for map selection panels, not a global main-task retry delay.
- 2026-04-21: `蹈火之人 / 前往圣德兰魔法学院` uses a detail-scoped `world_map_send`: after the main task click it waits about 1.9-2.5s, clicks client ratio `(0.503472,0.495556)` to select the academy map, then locates `WorldMapDetail...SendBtn`. This task also sets `defer_revive_during_map_entry=true` because the map panel can briefly report `hp=0` while position is still available; keep that guard task-local and do not apply it to normal combat deaths or the later Romel boss config under the same task title.
- 2026-04-22: `world_map_send` center selection can opt into visible cursor movement with `center_use_human_mouse=true`; keep `center_mouse_mode` as the low-level input mode (`api/driver/background`, usually `api`). This only routes the center map point through the existing `fixed_client_click` / `human_mouse` path; `SendBtn` lookup still uses locator matching, and main task call / normal navigation must stay unchanged. Because this click is synchronous, center settle timing must be based on the post-click `now_ms`, not the tick time from before mouse movement.
- 2026-04-22: `长夜终尽 / 前往余烬之息` no longer uses a `selection_step` map-item locator. This task now uses a task-local `world_map_send` center click at ratio `(0.503472, 0.498889)` after the map opens, then locates `WorldMapDetail...SendBtn`. Keep this direct map click local to this detail; do not replace other working `selection_step` map-send tasks with the same center click.
- 2026-04-22: Live logs show `前往余烬之息` currently appears under the visible title `圣诫之末`, not only `长夜终尽`. The task-local `world_map_send` config for this detail must match both titles, otherwise runtime cannot resolve `entry_action` and the center mouse click will never arm even though the map panel opens after the main task call.
- 2026-04-22: `前往余烬之息` must stay config-only. It has an extra direct `TASK_NAME_CONFIGS["圣诫之末"]` world_map_send config with only the title constraint, because live logs can clear the detail text before task-entry handling. Do not point this alias at the detail-scoped config with `constraint_mode="all"`; that prevents the map click from arming when detail is empty. If this title needs special main-task handling later, keep it on a task-local control-click locator path; do not add screen-click fallback or generic main-task-click prelock logic.
- 2026-04-22: `遗忘秘殿` 下 detail 包含 `拯救` 的三名平民链式目标，不能只在主线寻路终点扫 `GatherBtn`，因为终点不够精准。当前用三个任务局部 `ROUTE_POINT_ACTIONS.objective_button_flow_point`：主线终点进入对应精确坐标附近后，先 `MoveTo` 到 `(10110,-17208,323)` / `(10363,-11180,323)` / `(13911.89,-14194.49,323)`，再点局部 `GatherBtn`，点击后 `force_task_call_after_transition=true` 重新 call 主线拿下一名平民路径。不要把这种行为放进全局 GatherBtn，也不要恢复成终点直接扫按钮。

- 2026-04-21: `反目 / 穿越学城庭院` 在 `学城庭院` 的 `11228.66,15430.23,16` 附近有任务路径坏点；当前通过任务局部 `ROUTE_POINT_ACTIONS.recorded_route_point` 先走到 `11996,15205,16`，再重新 call 主线任务。该 action 触发半径要收窄且冷却较长，避免新主线路径经过原 trigger 附近时二次接管；不要把这个单点纠偏改成全局 follow/stall 逻辑。
- 2026-04-21: `逃离内城区 / 深入学者街巷` 在 `-7343,3287,1304` 附近补了任务局部 `ROUTE_POINT_ACTIONS.recorded_route_point`；当前要连续走两段录制点：先到 `-7248,4270,1299.12`，再到 `-11863.98,4063.33,1004.00`，最后才重新 call 主线任务。它和 `反目 / 穿越学城庭院` 一样属于单点坏寻路纠偏，只允许在该 task/detail 下接管，不要抽成全局寻路修复。
- 2026-04-21: `逃离内城区 / 击败副官哈斯` 终点当前走任务局部 `boss_kite` 四点循环跑打，点位是 `(-17411.62,6564.26,1004)`、`(-17556.39,5225.58,1004)`、`(-16261.53,4982.27,1004)`、`(-16227.50,6226.11,1004)`；只允许匹配这个 detail，不要把这组跑打点放大到整个 `逃离内城区` 主线。
- 2026-04-21: `叹息之墙 / 击败驻守城墙的学城守卫` 终点当前走任务局部 `boss_kite` 三点循环跑打，点位是 `(41722.66,16692.50,5363)`、`(41752.31,18232.59,5363)`、`(40715.92,17264.01,5369)`；只允许匹配这个 detail，不要把这组三点跑打扩到整个 `叹息之墙` 主线。
- 2026-04-21: `叹息之墙 / 穿越意志高墙` 在 `意志高墙` 地图当前补了任务局部 `ROUTE_POINT_ACTIONS.recorded_route_point` 入口锚点；命中后先走到 `-5776,2400,523`，再重新 call 主线任务。这个锚点带 `allow_without_task_target = true`，只允许在该 task/detail/map 下接管，不要把它扩成全局“无 task_path 时自动跑锚点”逻辑。
- 2026-04-21: `叹息之墙 / 继续前进，穿越意志高墙` 当前五点 `boss_kite` 不再等任务终点，触发距离已放宽到会在 `18077.17,-2129.24,403` 附近提前切入跑打；跑打点仍然是 `(18648.82,-3124.62,403)`、`(17522.53,-2107.33,408)`、`(17838.49,-1372.60,404)`、`(19137.89,-1257.22,403)`、`(19428.58,-2196.27,403)`。它和同 detail 的 `wall_of_sighs_manual_route_5104_-1737` 还是前后两段局部接管，不要互相替换成单一全局逻辑。
- 2026-04-21: `另一个魔法学院 / 冲破防线，抵达禁区入口` 在 `-813,-1035,5` 附近补了任务局部 `ROUTE_POINT_ACTIONS.recorded_route_point`；命中后依次走 `(-813.34,-1034.82,5)`、`(-666.89,1804.62,5)`、`(-690,3710,305)`、`(-1024.87,5459.95,305)`，最后重新 call 主线任务。它只服务这个 task/detail 的坏路段，不要改成全局寻路修复。
- 2026-04-21: `recorded_route_point` 走完录制点并成功重新 call 主线后，会清掉自己的 active route 状态，让普通 `wait_task_path_after_button -> GetMainTaskPath -> task_path` 流程接管新路径；如果录制点后要接世界地图 Send 等入口动作，继续使用 `arm_task_entry_action_after_click=true` 的专门分支。
- 2026-04-21: `AvePointLevelingNavWorker.lua` 的 MoveTo 反馈日志现在会带 `worker_mode / route_index / route_distance / route_point / original_index`；worker 成功发 MoveTo 时先写目标字段再写 `last_issue_at`，避免主线程读到“新 issue 时间 + 旧目标坐标”的误导日志。
- 2026-04-21: 普通主线重取路径时不要把 `state.nav_worker_path_route_version` 重置回 0；它必须在同一个 worker 生命周期内单调递增，否则新主线路径可能和旧 path_route 同版本，worker 会继续跑旧的 route_points。worker 从 `target` 模式切回 `path_route` 时也要刷新本地 `last_route_mode`。
- 2026-04-21: 主线任务按钮点击不能让 `支线/藏宝地` 污染查询链。`click_main_task_button` 会优先从任务面板按 `kind=主线` 直接点主线条目，过滤 `支线/藏宝地` 查询，并把锚点 fallback 收窄，避免 panel 查询 miss 后误点下面的支线任务。`_G.AVEPOINT_LAST_TASK_NAME` 也不能缓存支线/藏宝地；主线标题会剥掉 `主线` 前缀后缓存真实任务名。
- 2026-04-21: `反目 / 击败拦路的副官` 终点当前走任务局部 `boss_kite` 三点循环跑打，锚点在 `-580,-620,16` 附近；它只应该匹配这个 detail，不要把三点跑打放大到整个 `反目` 主线，也不要改成全局 follow/combat 策略。
- 2026-04-21: Holy Fire has multiple details under the same visible mainline title. `圣洁之火` dialogue flow must stay constrained to its dialogue detail, while `击败拦路的觉醒者` / `帮助马德兰击败觉醒者首领` uses a separate `boss_kite` objective key. Do not collapse these back into a broad `圣洁之火` config, otherwise terminal title drift can trigger `task_changed` before the endpoint monsters are dead.
- 2026-04-21: `圣洁之火 / 尝试和学城守卫交谈` has both a pre-jump `dialogue_flow` and a task-local `post_dialogue_flow`. FunctionBtn opens the dialogue, the fixed mouse click runs at client `(735,349)` with `skip_dialogue_jump=true`, then normal JumpBtn resumes. Keep this task-local; do not add the fixed click to global button rules.
- 2026-04-21: NPC 对话现在有一层局部 `npc_dialogue_combat_retry` 状态。它只在 `interact_with_npc` 成功按下 `D` 后 armed；若随后进入 `task_combat`，`mark_task_combat_seen` 会清掉当前 pending dialogue，但保留 retry 状态。战斗收尾在 `maybe_handle_task_combat_completion`：普通任务 NPC 会回到正常 `find_current_task_npc -> interact_with_npc` 评估，`route_point_action.npc_dialogue_point` 则会按原 action key 重新 arm 手工 NPC 对话。不要把这层 retry 改成全局刷新下一任务，也不要在普通 `clear_pending_interaction()` 路径里保留旧 retry 状态。

- 2026-04-20：`scripts/AvePointLeveling.lua` 里的 `fetch_locator_button_target` / `click_locator_button_target` 同时服务 `dialogue_flow` 和 `post_dialogue_flow`。Lua 只按词法作用域解析本地变量，如果 handler 定义在 helper 实现之前，必须先前置声明这两个 helper；不要把它们改回“先调用、后 local function 声明”的形式，否则任务对话后置固定坐标点击会报 `attempt to call a nil value`，导致鼠标移动点击不执行。
- 2026-04-20：`post_dialogue_flow` 的固定坐标点击会走拟人鼠标，点击函数可能同步耗时数秒；后续 `settle_ms`、`task_update_wait_until`、`schedule_task_refresh_after_transition` 必须按点击完成后的 `now_ms(ctx)` 计算，不能沿用点击开始前的 tick 时间，否则会在点击刚结束时立刻刷新/点击主线面板，打断后续对话或任务详情 UI。
- 2026-04-20：`JumpBtn` 点击后如果成功 armed `post_dialogue_flow`，要延后/清理前一阶段 `FunctionBtn` 留下的 `require_task_button_refresh`，让 post flow 自己决定什么时候重新 call 主线；否则会在固定坐标点击后马上回点任务面板，表现为“对话刚关就又被主线刷新打断”。
- 2026-04-20：`与莫琳娜交谈` 当前配置要求 NPC 交互按钮点开对话后先走固定坐标鼠标点击，然后再回到正常 `JumpBtn`；`JumpBtn` 之后不能再次执行该鼠标点击。该行为通过任务局部 `post_dialogue_flow.arm_after_objective_button=true` + `skip_dialogue_jump=true` 控制，runner 在 `JumpBtn` 后不会再次 arm 这类 flow，不要改成全局跳过或全局重复 `JumpBtn`。
- 2026-04-20：`陷落圣城 / 开启第一座圣光塔` 是链式塔交互任务：每次 call 主线到塔附近后点击 `FightInteractiveView.MapTrapBtn`，点击后必须 `force_task_call_after_transition` 重新 call 主线拿下一座塔路径。该行为放在该任务局部 `TASK_NAME_CONFIGS.objective.button_steps` 和已有 `fallen_city_holy_tower_*` route point action 上，不要把 `MapTrapBtn` 加进全局 `TASK_OBJECTIVE_BUTTON_STEPS`。
- 2026-04-27 note: mainline auto-call is back on the old `LaunchGUI` flow. Keep `click_main_task_button()` on `click_task_panel_entry(query) -> control_click(addr)` first, then the old anchor-button fallback. Do not reintroduce startup forced main-task reacquire, F12 TaskBtn locator priority, or panel-kind direct click into the mainline runner unless that whole chain is revalidated.
