# 退出游戏与重新登录

账号列表顶部“重新登录”作用于勾选账号，确认后提交临时请求。`WorkerProcessManager.RequestGameSessionAsync(instanceId, operation)` 是统一入口：

- `GameSessionOperation.Exit`：正常退出游戏，完成后停止账号，不再登录。
- `GameSessionOperation.Relogin`：正常退出游戏，回收旧后台，再调用原有登录和挂机启动链路。

未来定时下线、上线、重登应由独立调度器调用这些命令；定时上线调用原有 `StartAsync`。退出模块不读取时刻表，也不保存调度配置。

## 执行与隔离

`GameSessionRequestMailbox` 每个账号会话只接受一个请求，Stop/Start 不继承旧请求。与清包互斥，重复请求不会叠加。运行中提交不先停止账号；worker 复用 `PrepareCleanupTickAsync` 处理战斗、复活、起身和输入释放，然后由 `GameExitController` 接管退出。

没有退出请求时，不调用退出模块的读写操作。退出期间暂停普通主循环；受伤或死亡时先取消倒计时，确认关闭后再回到准备阶段处理战斗/复活。成功条件只能是目标电脑完整进程枚举确认没有 Aion.bin。角色读取失败、UI消失、经过12秒均不是成功。

点击退出完成后开始12秒观察。超时仍有倒计时则取消后重试。倒计时已经关闭时继续等待进程清理，30秒仍未结束才重新准备，避免在客户端收尾期间重复输入。请求持续到进程消失或用户停止。

完成后 worker 只保持心跳，不继续读取旧角色。管理器根据请求种类停止或重新启动；StopGeneration 和操作身份检查确保之后的用户停止优先于自动重登。停止过程中使用独立、最长5秒的取消清理，尝试关闭已存在的退出倒计时；若游戏已经退出，则不再输入，也不会自动登录。

## 本地适配器

`IGameExitSession` 隔离物理输入与读取；`VmmGameExitSession` 复用当前账号的 VMM 连接和 KMBox。`GameExitSnapshotPublisher` 使用 sealed typed `game_exit_scene` 通道；失败保留同会话官方快照，会话变化时失效。只有本次完整捕获可以授权输入，旧快照不能授权点击。

只读证据来源为用户提供的 `aion202609091125.json` 与 Game.dll（SHA256 `834300c2a85b6427de2d4a4be2931ff2ea59fd70f48f31754f5c2181753bd786`）。退出相关客户端结构仅存在于 Infrastructure 适配器，业务层不读取原始地址。

实机确认：系统菜单根名 `system_menu_dialog`，退出窗口根名 `quit_alert_dialog`，退出按钮 `quit_game`，取消按钮 `cancel`。控件几何、可见状态、启用状态、窗口和前台均在输入前重新验证；不使用固定屏幕坐标。原始客户端倒计时常量约11秒。

## 验证入口

`Roadhog.Tests.exe --test-filter=game-session` 覆盖状态机、provider、worker、真实IPC管理器和账号隔离。登录、账号进程、清包及UI回归分别使用相应过滤器。

构建时将 `RoadhogDesktopScriptRoot` 指向临时目录，以免构建自动替换其他运行中账号的桌面程序。实机验证使用脚本1配置副本、真实硬件绑定与设备租约，其他账号不重启。

实机结果与验证范围见 [2026-10-30 验证记录](GAME_SESSION_VALIDATION_2026-10-30.md)。
