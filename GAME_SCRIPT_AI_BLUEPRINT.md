# 游戏脚本 AI 实现蓝图

这份文档不是某一款游戏的需求说明，而是一份“新游戏项目接入模板”。

目标只有一个：

- 新游戏来了，把这份文档直接喂给 AI，它能按统一架构先把脚手架搭起来；
- 后续只需要补该游戏的进程名、数据采集方式、按钮定位规则、任务配置和地图数据；
- 尽量避免每次都从零写一套一次性的脚本。

---

## 1. 这份蓝图解决什么问题

当前大多数游戏脚本做不快，不是因为单个功能难，而是因为每次都把这些层重新混在一起：

- 进程附加
- 数据采集
- 输入执行
- 主循环
- 寻路
- 任务逻辑
- 特殊地图/Boss/副本逻辑
- 日志和诊断

正确做法不是“赶紧把功能写出来”，而是先把这些层拆清楚，然后让 AI 在稳定边界里生成代码。

一句话总结：

**要做的是平台化脚本架构，不是一次性脚本。**

---

## 2. 从当前项目抽出来的稳定架构

当前项目里，最值得复用的不是具体任务配置，而是下面这套分层。

### 2.1 入口层必须很薄

参考当前项目：

- `AvePointStandalone.lua`
- `scripts/AvePoint.lua`

入口层只做三件事：

1. 定位项目根目录
2. 扩展 `package.path`
3. 加载真正业务入口

入口层不要承载业务逻辑，不要写任务逻辑，不要写地图逻辑。

### 2.2 共享层负责运行环境和通用常量

参考当前项目：

- `scripts/avepoint/shared.lua`
- `scripts/avepoint/hotkey.lua`

共享层负责：

- 热键定义
- 运行模式切换
- 通用延迟/重试参数
- 通用 UI 点击预设
- 全局上下文注入
- 调试入口

这层是平台层，不是任务层。

### 2.3 主 Runner 只做状态机和调度

参考当前项目：

- `scripts/AvePointLeveling.lua`

主 Runner 负责：

- `start(ctx)`
- `update(now, ctx)`
- 状态机切换
- 主线任务刷新
- 导航调度
- 战斗/交互/死亡/卡住恢复
- 调用低层 API

主 Runner 不应该塞满某个任务的例外判断。

原则：

- 通用流程写在 Runner
- 任务例外写到配置

### 2.4 配置层承载 80% 的业务差异

参考当前项目：

- `scripts/AvePointLevelingConfig.lua`

配置层应该承载：

- 按任务名/任务详情匹配的逻辑
- 按地图匹配的逻辑
- 按坐标触发的逻辑
- 按按钮类型匹配的逻辑
- 特殊副本/藏宝地/Boss 房逻辑
- 对话后补按钮流
- 任务终点跑打
- 复活后二次进门

原则：

- 能靠配置解决，就不要改主流程
- 任务局部生效，不要污染全局

### 2.5 Builder 层负责把配置写法标准化

参考当前项目：

- `scripts/AvePointLevelingActions.lua`

Builder 层的价值：

- 降低 AI 生成配置时的自由度
- 让配置结构统一
- 避免同一种行为出现多种 schema

典型 builder：

- `make_boss_kite_task`
- `make_world_map_send_task`
- `make_dialogue_locator_flow_task`
- `make_objective_point`
- `make_revive_reentry`

### 2.6 Policy 层负责匹配，不负责执行

参考当前项目：

- `scripts/AvePointLevelingPolicy.lua`

Policy 层只做：

- 文本归一化
- task/detail/map 命中
- objective ready distance 计算
- 低优先级 UI 是否该处理

不要把执行动作塞到 Policy 里。

### 2.7 Worker 层负责耗时路线/异步任务

参考当前项目：

- `AvePointLevelingNavWorker.lua`

所有可能阻塞主循环的东西，都要尽量外移：

- 长路径逐点跟随
- 路线循环跑打
- 路线记录回放
- 异步地图内刷怪巡航

原则：

- 主 Runner 决策
- Worker 执行耗时路线

### 2.8 特殊模式必须是独立子状态机

参考当前项目：

- `scripts/AvePointLevelingTreasure.lua`

藏宝地/副本/刷图这种逻辑，不应散落在主循环 if/else 里。

要做成：

- 独立 runtime
- 独立配置
- 独立持久化
- 独立恢复机制

这样后续接新游戏时，副本系统也能直接照搬。

### 2.9 诊断热键必须是第一天就存在

参考当前项目已有：

- F6：玩家坐标
- F7：坐标 + 门 + 任务 + 地图
- F8：鼠标相对坐标
- F9：Data API 诊断

新游戏脚本第一版就必须自带这些诊断能力，否则后续所有定位都靠猜。

---

## 3. 新游戏项目的标准目录结构

新游戏项目建议直接按下面这套骨架起：

```text
ProjectRoot/
  config.json
  GameStandalone.lua
  scripts/
    Game.lua
    game/
      shared.lua
      hotkey.lua
      guard.lua
      route.lua
      interact.lua
      diagnostics.lua
    GameRunner.lua
    GameConfig.lua
    GameActions.lua
    GamePolicy.lua
    GameDungeon.lua
    GameNavWorker.lua
    game_state.lua
  logs/
  release/
  LEVELING_HANDOFF.md
  GAME_SCRIPT_AI_BLUEPRINT.md
```

### 文件职责

- `GameStandalone.lua`
  - 入口薄层
- `scripts/Game.lua`
  - 模块总装
- `scripts/game/shared.lua`
  - 通用常量、热键、配置加载
- `scripts/game/hotkey.lua`
  - 调试热键、启动停止热键
- `scripts/GameRunner.lua`
  - 主状态机
- `scripts/GameConfig.lua`
  - 任务、地图、按钮、房间、Boss 配置
- `scripts/GameActions.lua`
  - 配置 builder
- `scripts/GamePolicy.lua`
  - 文本匹配与策略判断
- `scripts/GameDungeon.lua`
  - 副本/刷图/藏宝地子状态机
- `scripts/GameNavWorker.lua`
  - 异步路线执行
- `scripts/game_state.lua`
  - 持久化 runtime

---

## 4. 新游戏项目必须先抽象的 7 个接口

AI 不应该直接开始写任务逻辑，必须先补齐底层接口。

### 4.1 Process / Attach

必须统一成接口：

- `init(process_name, mode)`
- `reset()`
- `pid()`
- `is_initialized()`

### 4.2 Data API

至少要有：

- `player_info()`
- `player_pos()`
- `is_main_interface()`
- `is_loading()`
- `get_main_task_pos()`
- `get_main_task_path()`
- `enum_monsters()`
- `enum_npcs()`
- `enum_portals()`
- `enum_ground_items()`
- `enum_interactive_items()`
- `enum_ui()`

### 4.3 Input API

至少要有：

- `move_to(x, y, z)`
- `key_press(vk)`
- `control_click(addr)`
- `window_click_client(x, y)`
- `human_mouse_click(x, y, opts)`

### 4.4 UI Locator API

至少要有：

- `find_button_near_point()`
- `click_task_panel_entry()`
- `get_task_panel_info()`
- `get_current_selected_button()`

### 4.5 Route API

至少要有：

- `build_route(from, to)`
- `nearest_route_index()`
- `sync_task_path_target()`
- `record_route()`

### 4.6 Diagnostics API

至少要有：

- `print_player_pos()`
- `print_portals_and_task()`
- `print_cursor_client_pos()`
- `test_data_api()`

### 4.7 Persistence API

至少要有：

- `load_state(key)`
- `save_state(key, value)`
- `clear_state(key)`

---

## 5. AI 写代码时必须遵守的核心原则

这部分是最关键的。

### 5.1 主流程只写稳定公共逻辑

不要把某个任务的按钮点击、Boss 例外、地图绕路硬塞进主循环。

### 5.2 任务差异尽量配置化

优先级：

1. 改 `GameConfig.lua`
2. 不够时改 `GameActions.lua`
3. 再不够时改 `GamePolicy.lua`
4. 最后才改 `GameRunner.lua`

### 5.3 所有耗时动作都要非阻塞

不要把这些写成长阻塞：

- 长路径逐点移动
- 等地图切换
- 等对话窗口
- 等 Boss 清场

正确做法：

- 主循环只推进状态
- 动作返回 `running / success / failure` 风格结果
- 必要时丢给 worker

### 5.4 所有定位都要先有诊断再有业务

顺序必须是：

1. 先加 F6/F7/F8/F9 级别的诊断
2. 再根据诊断数据写业务逻辑

不要让 AI 直接猜 UI、猜坐标、猜按钮。

### 5.5 所有特殊逻辑都要局部生效

如果某个行为只属于：

- 一个任务
- 一个 detail
- 一个地图
- 一个房间

那它就只能局部生效。

### 5.6 必须自带足够日志

日志至少覆盖：

- 当前任务/详情更新
- 当前地图更新
- 主线 call 开始/成功/失败
- task path/pos 更新
- 路线 worker 启停
- 目标点触发
- 按钮点击结果
- 对话开始/结束
- 死亡/复活/重进门
- 副本接管/释放
- 卡住判定和恢复

### 5.7 编译产物和源码必须分离

不要让 AI 去改：

- `release/`
- `.luac`
- zip 打包文件

只改源码层。

---

## 6. 市面上成熟游戏脚本平台的共性

下面这些不是照抄实现，而是要吸收它们的结构特点。

### 6.1 平台会先提供稳定 Hook + 丰富 API，再谈具体脚本

OSBot 官方对外强调的是“稳定 hook + 丰富脚本 API”，并把脚本开发建立在稳定客户端能力之上，而不是一堆分散的小脚本上。  
来源：

- OSBot 首页：<https://www.osbot.org/>
- OSBot Script API：<https://osbot.org/api/org/osbot/rs07/script/Script.html>

对我们的启发：

- 先做平台接口，再做业务脚本
- 新游戏接入时，优先补 Data/Input/UI 三层 API
- 任务逻辑只是平台上的插件

### 6.2 成熟平台都有固定生命周期

OSBot 的脚本生命周期很清晰：

- `canStart`
- `onStart`
- `onLoop`
- `onPaint`
- `onMessage`
- `onStop`

这说明成熟脚本平台不会把所有事情揉进一个 while true。

对我们的启发：

- 统一 runner 生命周期
- 明确 start / update / stop / diagnostics / persist

### 6.3 热键和上下文限制是平台层，不是业务层

AutoHotkey 官方文档里，热键本身就是一等能力，并且支持上下文限制。  
来源：

- AutoHotkey Hotkeys：<https://doggy8088.github.io/AutoHotkeyDocs/docs/Hotkeys.htm>

对我们的启发：

- 调试热键必须归共享层管理
- 不同模式的热键要能隔离
- 热键不能散落在任务逻辑里

### 6.4 UI 自动化要优先走控件/定位，再退回鼠标

AutoIt 的 `ControlClick` 文档非常典型：能定位控件时，先走控件级点击；不行再退回窗口激活和鼠标。  
来源：

- AutoIt ControlClick：<https://www.autoitscript.com/autoit3/docs/functions/ControlClick.htm>

对我们的启发：

- 点击链路要分层
- 优先 `control_click`
- 次优 locator 命中
- 最后才是相对坐标鼠标点击

### 6.5 复杂流程必须可组合、可复用、可异步

BehaviorTree.CPP 强调三件事：

- 异步动作不能阻塞整个执行流
- 大行为应拆成可组合的子树
- 数据通过 blackboard/ports 传递

来源：

- Async Actions：<https://www.behaviortree.dev/docs/guides/asynchronous_nodes/>
- SubTrees：<https://www.behaviortree.dev/docs/4.0.2/tutorial-basics/tutorial_05_subtrees/>
- Blackboard / Ports：<https://www.behaviortree.dev/docs/tutorial-basics/tutorial_02_basic_ports/>

对我们的启发：

- 主 Runner 不要写阻塞式长逻辑
- 大功能拆成可组合的子状态机/子配置
- 公共 runtime 要有共享状态存储，不要靠全局乱写

---

## 7. 我们要怎样做到“快速做出一款新游戏脚本”

核心不是“AI 代码写得快”，而是“AI 可复用的约束足够强”。

### 7.1 第一步不是做任务，而是做采集面板

新游戏接入第一阶段，只做：

- 进程附加
- 玩家坐标
- 当前地图
- 当前任务文本
- 附近怪/NPC/门/交互物
- UI 文本/按钮枚举
- 鼠标相对坐标
- Data API 诊断

没有这一步，后面全是盲写。

### 7.2 第二步做“最小闭环”

最小闭环必须是：

1. 点主线任务
2. 获取任务路径/终点
3. 执行移动
4. 到点后识别交互按钮
5. 任务刷新

只要这个闭环跑通，后续所有功能都能搭积木。

### 7.3 第三步再做特化模块

顺序建议：

1. 主线跟随
2. 对话 / Gather / Portal / FunctionBtn
3. Boss 房 objective
4. revive_reentry
5. route point action
6. 异步路线 worker
7. 副本/藏宝地
8. 持久化恢复

### 7.4 所有新功能都要落在固定插槽

固定插槽如下：

- 任务级特化：`TASK_NAME_CONFIGS`
- 地图级特化：`MAP_TASK_CONFIGS`
- 坐标级特化：`OBJECTIVE_POINT_CONFIGS`
- 路线级特化：`ROUTE_POINT_ACTIONS`
- 通用按钮库：`TASK_OBJECTIVE_BUTTON_STEPS`
- 副本级特化：`DUNGEON_CONFIGS`

这就是“快速复制新游戏脚本”的关键。

---

## 8. 新游戏接入的数据采集清单

AI 在开始写新游戏项目之前，必须先要求或先实现下面这些采集能力。

### 8.1 基础运行信息

- 进程名
- 窗口类名
- 附加模式（API / driver / memory / OCR）
- 初始化成功日志

### 8.2 玩家核心信息

- 玩家对象地址
- 当前坐标
- 血蓝/死亡状态
- 朝向/移动状态

### 8.3 主线信息

- 当前任务名
- 当前任务详情
- 主线任务按钮位置
- 主线路径点
- 主线终点

### 8.4 场景对象

- 怪物枚举
- NPC 枚举
- 门/传送门枚举
- 掉落物枚举
- 可交互物枚举

### 8.5 UI 信息

- 文本枚举
- 按钮枚举
- 图片枚举
- 当前选中按钮
- 鼠标相对坐标

### 8.6 特殊流程信息

- 地图切换识别
- 对话窗口识别
- 复活按钮识别
- Boss 死亡信号
- 副本入口/出口/重刷门

---

## 9. 给 AI 的实现约束

下面这部分可以直接作为 AI 编码约束。

### 9.1 编码要求

- 入口层必须薄
- 主 Runner 只做通用调度
- 任务差异优先配置化
- 所有耗时动作必须可中断、可重试
- 所有按钮逻辑必须先 locator/control，再退回坐标点击
- 所有关键路径都要有 info/warn 日志
- 所有特殊逻辑必须任务局部生效
- 禁止直接修改编译产物

### 9.2 交付要求

AI 完成首版时，必须同时交付：

1. 可运行入口
2. 主 Runner
3. Config / Actions / Policy 三件套
4. 至少 4 个调试热键
5. 最小闭环主线流程
6. 日志说明
7. handoff 文档

### 9.3 修改优先级

如果新增需求，AI 必须按下面顺序判断落点：

1. 配置就能解决？
2. Builder 需要扩一个 schema？
3. Policy 需要扩一个匹配规则？
4. Runner 是否真的缺少承载点？

只有前 3 个都不够，才允许改 Runner 主流程。

---

## 10. 给 AI 的可直接使用提示词

下面这段可以直接复制给 AI。

```text
你现在要为一个新的游戏项目实现一套可扩展脚本框架，不是一次性脚本。

目标：
1. 先搭建稳定脚手架，再实现最小闭环主线流程。
2. 架构必须复用以下分层：
   - 薄入口：GameStandalone.lua -> scripts/Game.lua
   - shared/hotkey 公共层
   - GameRunner.lua 主状态机
   - GameConfig.lua 配置层
   - GameActions.lua 配置 builder
   - GamePolicy.lua 匹配与策略层
   - GameNavWorker.lua 异步路线 worker
   - GameDungeon.lua 作为副本/刷图子状态机
3. 主 Runner 只写通用流程，不要把某个任务的例外判断写死进主循环。
4. 任务、地图、坐标、按钮、Boss、副本差异，优先配置化。
5. 所有耗时动作必须非阻塞，可重试，可恢复。
6. 所有关键行为都要补足日志，日志要能看出：
   - 当前任务/地图变化
   - 主线 call 开始/成功/失败
   - path/pos 获取结果
   - 路线 worker 启停
   - 交互按钮点击结果
   - 对话/地图切换/死亡/复活/重进门
7. 第一版必须自带调试热键：
   - 打印玩家坐标
   - 打印当前位置+附近门+任务
   - 打印鼠标相对坐标
   - 运行 Data API 自检
8. 第一版先只实现“最小闭环”：
   - 点击主线任务
   - 获取主线路径或终点
   - 跟随移动
   - 到点后识别交互按钮
   - 任务刷新继续
9. 如果新增功能，只按下面顺序扩展：
   - 先改 GameConfig.lua
   - 再改 GameActions.lua
   - 再改 GamePolicy.lua
   - 最后才允许改 GameRunner.lua
10. 禁止修改 release、.luac、zip 等发布产物。

你输出时必须包含：
1. 文件树
2. 每个文件职责
3. 第一版要实现的接口清单
4. 可运行代码骨架
5. 最小闭环主线流程
6. 调试热键
7. handoff 文档
```

---

## 11. 推荐的新游戏首版开发顺序

### Phase 0：建骨架

- 入口
- shared
- hotkey
- runner
- config/actions/policy

### Phase 1：建采集层

- 附加进程
- 玩家坐标
- UI 文本/按钮
- 附近对象枚举

### Phase 2：建最小闭环

- 主线按钮
- 任务路径
- move_to
- 到点交互

### Phase 3：建局部配置能力

- task/detail 约束
- objective point
- route point action
- button step

### Phase 4：建恢复能力

- loading recover
- stall refresh
- death/revive
- task refresh

### Phase 5：建高阶模式

- Boss kite
- 副本
- 路线 worker
- 路线记录回放

---

## 12. 最后结论

如果目标是“快速做出新游戏脚本”，不要问：

- 这个任务怎么写最快？

要问：

- 这个任务差异应该落在哪个固定插槽？

真正高复用的答案是：

**先平台化，再任务化；先做诊断和接口，再做具体功能；先保主流程稳定，再靠配置搭积木。**

