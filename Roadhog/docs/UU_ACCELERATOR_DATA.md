# UU 加速器状态与入口实测记录

记录日期：2026-10-23（Asia/Shanghai）。用途：后续自动登录的 UU 启动入口和加速状态衔接。完整登录流程入口见 [LOGIN_UI_DATA.md](LOGIN_UI_DATA.md)。

**脚本1已实测跑通：停止加速 → 停止确认框 → 游戏列表 → 第一款“永恒之塔” → 加载 → 加速详情页。** 加载完成后，“启动游戏”和“停止加速”重新出现，计时从 `00:00:09` 增长至 `00:01:04`。本轮没有点击“启动游戏”，没有写目标内存；发送了 KMBox 鼠标移动和点击。最终恢复加速并释放设备连接。

这里确认的是 UU 当前显示的业务状态；尚未解码后台 VPN 状态枚举，也没有单独验证网络加速效果。功能尚未接入正式自动登录。

## 证据和适用范围

持久证据位于 [evidence/uu-acceleration-2026-10-23](evidence/uu-acceleration-2026-10-23)，[index.json](evidence/uu-acceleration-2026-10-23/index.json) 保存原路径、大小和逐文件 SHA256，读取记录不再依赖 `.tmp` 留存。

- 本地对照文件：`C:\Program Files (x86)\Netease\UU\5251`，版本 `6.20.1.63`。`uu.exe` 为业务进程，`ui.dll` 为 DuiLib UI，`skin.dll` 包含皮肤和语言资源。
- `uu.exe` SHA256：`967c86c19ccbb05a151e522288389e0f842c21c52a94924ca495d846ea3e76fb`。
- `ui.dll` SHA256：`1939022846a612be7802794c0c6f5d5d2ee38146b4ec47d48cdcc9f16f9bb72c`。
- 目标 UU 的若干关键 UI 导出 RVA 和机器码与本地文件核对一致；没有对目标整个模块做 SHA256，因此不能称为远程完整文件哈希匹配。
- 当次目标 `uu.exe` PID `1572`、主窗口 HWND `0x30328`、manager `0x5FFAF34`、root `0x5E9F0D0`。这些是历史会话数据，不能作为永久地址。

归档分为 `live/` 运行观察、`static/` 静态及目标机器码证据、`resources/` 相关皮肤、`probe-source/` 诊断探针源码快照。原始静态记录保留了当时尚未完成的事项，以本文汇总的后续实测结论为准。

## 状态和选择规则

| 状态 | 本次读取到的依据 | 后续动作依据 |
| --- | --- | --- |
| 已加速详情页 | `child_game_acced` 可见；同页 `game_name` 为目标游戏；`launch_game`、`stop_acc` 可见可用；`acced_time` 增长 | 可定位“启动游戏”；尚未实测启动游戏动作 |
| 停止确认框 | 独立 `COMMON_ALERT_BOX`；`tip_desc=%{1547}`；`Ok` 显示“停止”；主窗口被禁用 | 核实提示内容后点击该弹窗的 `Ok` |
| 已停止、回到首页 | `child_home_page` 可见、详情页隐藏；第一卡片 `game_name=永恒之塔` | 定位第一卡片及其 `start_acc` |
| 正在加载加速 | 同一第一卡片的 `accing_shade` 和 `acc_progress` 可见 | 等待业务状态改变，不重复点击 |
| 加速恢复 | 详情页和两个按钮再次出现，目标游戏名称正确，计时重新增长 | 完成本次循环 |

不能仅用 `uu.exe` 存在判断加速状态；也不能把一次读取失败当成未加速或未出现弹窗。

首页第一卡片层级：

```text
child_home_page → layout_home_page → layout_top_area
  → layout_game_card → game_card_0
```

需要核对该卡片子树中的 `game_name`。`start_acc`、`game_name`、`stop_acc` 在其他卡片中也存在，不能全树找到同名控件就点击。此次鼠标移到第一卡片后实际命中其 `start_acc`，不需要额外点击“进入游戏详情”按钮。

11:47:35.984 点击后，11:47:36.027～11:47:41.936 连续30次完整采样读取到了首页卡片加载层。此版本的本次加载发生在卡片上，`child_game_accing` 独立页面未出现；静态 XML 中存在该页面不代表实际会使用。

## 停止确认弹窗

本次出现确认框。它应作为条件分支处理，不能假定每次必出或永远不出。

| 项目 | 值 |
| --- | --- |
| 窗口标题 / 类名 | `COMMON_ALERT_BOX` / `Confirm` |
| 正文控件 | `tip_desc`，实测原始文本 `%{1547}` |
| 对应正文 | 停止加速可能导致游戏重连，请确保已退出游戏或不在对局中。 |
| 确认控件 | **`Ok`**，大小写准确；语言资源 `%{2766}` 为“停止” |
| 取消 / 关闭 | `Cancel` / `close` |
| 不再提醒我 | `option_value`，本次未修改 |

当前确认类的 manager 位于 CWindowWnd 对象 `+0x30`；普通 WindowImplBase 的 `+0x38` 不能套到这个弹窗。通过同一 HWND、manager 的 HWND、root 的反向 manager 指针核对对象关系。主窗口 `WS_DISABLED` 只能证明它被禁用，不能单独证明弹窗内容；要继续读取独立窗口及其正文。

## 坐标与鼠标实测

以下均为 **2026-10-23 本次窗口位置下的屏幕坐标**。主窗口客户端原点 `(12,20)`，大小 `1000×688`，DPI 为100%。

| 控件 | 客户区矩形 | 屏幕中心 |
| --- | --- | --- |
| 详情页 `launch_game` | `[40,328,150,364]` | `(107,366)` |
| 详情页 `stop_acc` | `[166,328,276,364]` | `(233,366)` |
| 首页 `game_card_0` | `[40,94,258,454]` | `(161,294)` |
| 弹窗 `Ok` | `[72,140,192,176]`，弹窗原点 `(312,229)` | `(444,387)` |

坐标可动态取得：解析当前窗口客户端屏幕原点，加上控件当前客户端矩形中心。窗口移动、DPI 变化或进程重启后要重新解析，不能把表中的数字作为固定点击位置。

首次 KMBox 移动距离偏小，点击没有命中按钮；本次探针通过 UU 最后处理的鼠标点作小幅修正，并确认 hover 指针命中目标按钮后点击。发送成功和业务状态已改变分别验证。

已验证的 UI 读取字段（针对本次版本；指针为32位）：

| 对象 | 字段 |
| --- | --- |
| manager | HWND `+0xC`；root `+0x60`；DPI对象 `+0x318` |
| 控件 | manager `+0x14C`；parent `+0x150`；名称 CDuiString `+0x1D8`；RECT `+0x260`；原始文本 CDuiString `+0x2E4` |
| 控件状态 | 可见 `+0x2B0 && +0x2B1`；可用 `+0x2B2`；需要合并祖先状态 |
| 容器 | 子项数组指针 `+0x76C`；数量 `+0x770`；容量 `+0x774` |
| 鼠标诊断 | manager 的最后客户端鼠标点 `+0x80/+0x84`；hover控件 `+0x68`；focus `+0x64`；按下控件 `+0x6C` |
| 按钮诊断 | CButtonUI 状态 `+0x8AC`；hover 位 `0x8`，按下/捕获位 `0x50` |

鼠标诊断字段是最后处理的消息，可能陈旧；单独读取旧值不能证明窗口当前在前台或操作成功。

当前目标的窗口坐标链：`user32!gSharedInfo → USER handle entry → 所属线程 TEB 的 Win32ClientInfo → 桌面映射 → 窗口对象`。验证句柄类型、代数、桌面 identity、对象 HWND 后读取客户端屏幕矩形；此次排除了特殊窗口/RTL 分支，且 DPI 与根控件尺寸一致。具体结构和机器码证据见 [remote-window-mapping.json](evidence/uu-acceleration-2026-10-23/static/remote-window-mapping.json) 及 `remote-user32-*.json`，属于当前目标 Windows 的验证结果，不是所有 Windows 版本通用偏移。

## 原始证据索引与剩余工作

- [停止按钮及落点复核](evidence/uu-acceleration-2026-10-23/live/cycle-stop-confirmed.json)。
- [独立确认窗](evidence/uu-acceleration-2026-10-23/live/cycle-modal-windows.json)、[确认正文](evidence/uu-acceleration-2026-10-23/live/cycle-modal-text.json)、[确认后返回首页](evidence/uu-acceleration-2026-10-23/live/cycle-stop-ok.json)。
- [首页第一款游戏](evidence/uu-acceleration-2026-10-23/live/cycle-home-ui.json)、[点击及30次加载采样](evidence/uu-acceleration-2026-10-23/live/cycle-start-first-card.json)。
- [恢复详情页](evidence/uu-acceleration-2026-10-23/live/cycle-final.json)、[计时继续增长](evidence/uu-acceleration-2026-10-23/live/cycle-final-verify.json)。

完整树诊断存在部分读取失败，未匹配的 CTextUI 导出也单独记录；它的返回地址对应本地 CListUI，具体原因仍待确认。没有把这个错误地址当作 CTextUI 使用。关键动作及状态使用完整且复核身份/几何的 FocusedCapture；正式读取器仍须落实进程、模块、设备会话隔离，以及 provider 内的读取验证和可信快照。

待实现：自动发现与重启后重新绑定、版本和缩放变化、超时/错误/确认框分支、UU 到启动器的实际启动确认及后续账号登录衔接。本次循环用于验证状态和入口，不代表已决定每次登录都先停止再加速；完整登录与重试规则仍按后续设计。

`probe-source/` 文件加 `.txt` 避免进入主项目编译。它们保留原 `.tmp` 路径、硬件绑定保护和诊断入口；读取历史证据不需运行探针，恢复实机探针时需调整路径并重新验证绑定。`click` 模式会产生真实鼠标输入。归档这些记录不代表功能已接入生产或完成部署。

## 2026-10-25：断线后的重新加速

脚本2（角色 `Tone`、配置读取设备 `fpga://devindex=1`）实机出现独立弹窗，正文是 **“加速断开，发现 40002 错误”**。弹窗出现时主窗口可能最小化或禁用，前台也可能是 Explorer；应先识别这个弹窗，直接点击其“重新加速”，由 UU 恢复主窗口及加速，然后继续登录。不要先双击桌面图标、关闭弹窗或点击“快速反馈”。

| 字段 | 本次确认 |
| --- | --- |
| 资源布局 | `acc_error_confirm.xml`；另一个静态变体 `acc_error_confirm2.xml` 使用 `error_code_desc2` |
| 窗口 | 无标题；客户端 `360×416`，本次屏幕原点 `(332,156)` |
| 正文 | `error_code_desc`，原始文本直接包含错误原因与错误码 |
| 主按钮 | `Ok`，原始文本 `%{1451}`，语言表为“重新加速” |
| 次按钮 | `Cancel`，原始文本 `%{1452}`，语言表为“快速反馈”，不是取消操作 |
| 帮助入口 | `error_code=%{1450}`；第二种布局改为 `error_code_desc2` |
| 本次主按钮 | 客户区 `[52,275,172,311]`；屏幕中心 `(444,449)`，仅为证据，生产按当前窗口重新计算 |

完整、经过生产读取器验证的弹窗树保存在 [测试夹具](../../Roadhog.Tests/Fixtures/Login/uu-reconnect-40002.json)。不能仅凭 `Ok` 或“重新加速”几个字识别：停止加速确认框、Steam绑定页面不属于这个布局。按钮未启用、正文尚空、坐标不完整、存在多个匹配弹窗时均不点击。

实现入口是 `LoginNativeReader.ProjectUuReconnectWindows/ProjectUuReconnect`。弹窗优先于打开UU、检查前台及各登录进程页面；点击仍经过同一个快照发布、重新绑定、移动后复核和前台验证过程。同一个仍然有效的弹窗可按现有3秒控制重试间隔重试。普通“启动游戏后UU自动最小化”的等待规则保留：只有已确认的重加速操作及后续无游戏/通行证/启动器进程的UU页面，才清除旧启动限制并允许重新启动游戏；已有后续客户端时接续原流程。

### 本次发现的读取兼容问题

- HWND在窗口对象内可能出现多次；第一个看似根指针的值实际仍是HWND。窗口枚举现遍历候选，用 manager/HWND/root反向关系找到正确对象后才决定完整性。已确认禁用或最小化的主窗口不要求读取其非活动控件树，避免阻塞独立弹窗或已开始加载的游戏。
- 已完整读出隐藏状态的子树不参与当前可操作界面，避免隐藏页面中已换出的节点阻塞可见按钮；可见子树仍需完整读取、校验子项及反向关系。
- 目标UU的部分RTTI页不能通过DMA读取。`UuControlTypes`内嵌本机已归档版本的类型资料，但必须同时校验目标PE身份和vtable全部9个槽位（COL加8个函数指针）；不要求另带第三方DLL。脚本2的旧版时间戳不同，因此这条路径正确拒绝复用新版资料。
- 旧版通过只读解析完整的 `GetClass`（槽5）和 `GetInterface`（槽6）方法及实际字符串证明类型。只支持完整16/42/52/110字节模板，校验父调用、有界递归及循环、`Control`终点、`IContainer/Container`分支；不执行目标函数。未知方法继续保留原RTTI读取，读短不能被解释为叶节点。
- 实机的CRT比较函数机器码已完整核对，另有Webview2控件使用不区分大小写比较。`LoginPeExports`通过PE名称→ordinal→函数表认证 `wcscmp/_wcsicmp`，不依赖此次返回了相邻函数的VMM导出便捷接口。原始方法证据及故障回放在 [方法夹具](../../Roadhog.Tests/Fixtures/Login/uu-control-methods.json)。

这些处理均在本地登录adapter内部。业务继续只消费正式 `login_scene`；读取失败保留同会话快照，实际输入需要新的完整读取，进程/模块会话变化作废旧目标。

### 实测与回归结果

2026-10-25 脚本2实测（北京时间）：06:00:05识别并点击重加速；06:00:09进入加速等待；06:00:17确认永恒之塔已加速并启动游戏；06:00:35点击开始游戏；06:00:53提交已保存的账号密码；06:02:34同意游戏协议；06:02:39进入服务器；06:02:45确认种族创建限制；06:02:51选择Tone；06:03:00勾选入场协议；06:03:07确认；**06:03:23正式快照为InWorld/Tone，游戏PID9748**。输入与设备租约均已释放，未启动挂机或操作其他账号。本次没有遇到验证码。

过程中多次线程表、前台身份或加载期窗口数据不完整，均由原快照/输入恢复边界等待并重读，没有退出流程，也没有重复打开UU或重复提交启动游戏。运行记录见 [实测日志](evidence/uu-reconnect-2026-10-25/login.log) 和 [完成结果](evidence/uu-reconnect-2026-10-25/login-result.json)。

Release构建0警告、0错误；仅运行 `--test-filter=auto-login`，41项通过，未跑全量。覆盖实际弹窗、非目标弹窗、主窗口禁用/最小化、无效manager候选、旧版方法回放、PE名称/ordinal映射、读短/奇数指针/循环、重加速与已有启动/登录/入场限制、取消、以及原有登录回归。新类型数据已内嵌在Roadhog.dll，无需拷贝UU的DLL。

## 2026-10-25：在线启动不依赖 UU 窗口表

脚本4在06:08:55、06:09:35、06:10:03启动后均停在“检查目标电脑的游戏与登录状态”，持续记录 `Incomplete window inventory read 0, length 24`。当时客户端DLL与06:08:47的本地Debug产物一致，并非之前的Release交付包。只读探针确认角色“初次见面”（Aion PID588）处于游戏状态15、实体和位置有效、没有可见退出提示；UU PID3700的 `user32!gSharedInfo` 便捷查询返回0，目标导出目录页 `0x7647EC40` 本身也读不到。原先先读UU窗口表的顺序把完整的在线事实阻塞了。

现由同一个provider按以下顺序采集：完整的游戏内角色且没有可见游戏消息 → UU重新加速弹窗 → 原有各登录阶段。只有第一项完整成立才短路；可见退出提示仍阻止接受残留角色信息，需要登录时的UU读失败也不能伪装成弹窗不存在。窗口导出地址无效时，读取器在解引用前拒绝并记录目标PID，交由原快照边界重试；没有猜测导出地址或跳过实际需要操作的弹窗。

新增回归覆盖脚本4冷启动时UU窗口表不可读、退出消息与重加速优先级、角色/地图/消息数据不完整、快照保留、无效导出地址不被读取。仅运行 `--test-filter=auto-login`，45项通过，未运行全量。正式 `VmmAutoLoginSession.ReadAsync` 在脚本4的只读验证得到 `InWorld / 初次见面 / PID588`，未发送输入、未启动挂机；设备租约已释放。结果与构建记录见 [在线启动证据](evidence/login-online-priority-2026-10-25/validation.json)。运行客户端未自动替换或重启。

## 2026-10-25：离线时 UU 窗口表与隐藏控件的读取恢复

脚本4在06:22重新启动后持续记录 `Window shared-info export is unavailable for PID 3700`。本次用户确认已掉线或在登录页；目标进程清单实际仅有UU，没有Aion、启动器或通行证。因此上面的“已在线优先”修复已生效，但不适用于本次必须打开UU的状态。

`WindowInventory` 现在在目标进程查不到 `gSharedInfo` 时，使用经过验证的同一Windows会话的64位winlogon读取共享USER表。共享头、server计数及句柄表通过该来源读取；目标线程TEB、桌面映射、窗口对象及控件仍通过目标进程读取，不能混用虚拟地址。验证有效进程、实际SessionId、EPROCESS/PEB、user32模块范围、完整64位计数、线程所有权、桌面identity和HWND，并在结束时复核表头和进程/模块身份。不同用户SID不意味着不同桌面会话，不用SID代替SessionId。缺少数据仍由原provider保留快照和重试，输入需要重新完成捕获。

实机A/B同时确认VMM会缓存第一次初始化的会话进程模块视图：先用普通winlogon视图读取USER表，再请求前台符号，会得到空的win32kbase模块。现由 `SessionInputProbe.SessionProcess` 统一使用包含内核的会话视图；同样顺序下win32kbase和 `gptiForeground` 均能解析。对照见 [普通视图先初始化](evidence/login-session-window-table-2026-10-25/session-user-view-first.json) 与 [统一会话视图](evidence/login-session-window-table-2026-10-25/session-kernel-view-first.json)。没有硬编码这次的模块基址或导出地址。

恢复UU后又发现隐藏控件 `0xB03DD30`：整对象请求880字节，仅返回跨页前720字节，但manager/parent和三个可见/可用状态字节完整，Visible为0。原来先读取整对象，再判断隐藏，导致隐藏页也阻塞按钮发现。现在先完整读取身份和可见状态，确认隐藏才跳过后续内容；可见控件仍需完整对象，并复核前后身份/状态一致。缺少可见状态不能当作隐藏。原始片段见 [隐藏控件读短](evidence/login-session-window-table-2026-10-25/hidden-payload-read.json)。

本次Release构建0警告、0错误，仅运行 `--test-filter=auto-login`，49项通过。新增覆盖共享表/目标地址空间隔离、错误会话/进程、64位计数上位、读短和表头变更、隐藏对象内容缺页、可见对象缺页、可见状态变化；保留在线优先、重加速及原登录回归。正式provider实测从 [Desktop/OpenAccelerator](evidence/login-session-window-table-2026-10-25/desktop-scene.json) 进入 [AcceleratorReady/LaunchGame](evidence/login-session-window-table-2026-10-25/accelerator-ready.json)。

继续使用脚本4已保存配置实测：06:42:11启动游戏，06:42:41进入开始游戏操作，06:43:11进入账号密码提交，06:43:53开始加载游戏模块，06:44:55同意协议，06:45:01选服，06:45:09确认种族限制，06:45:15选择“初次见面”，06:45:26勾选入场协议，06:45:32确认，**06:45:49正式快照确认InWorld/初次见面/PID9352**。这次未遇到验证码；窗口切换和游戏加载中的短读、前台暂时无法复核均经现有恢复边界接续。见 [实机日志](evidence/login-session-window-table-2026-10-25/login.log)、[登录结果](evidence/login-session-window-table-2026-10-25/login-result.json)。

独立实测释放输入及设备租约后，点击原客户端脚本4行的启动按钮，06:46:13原客户端确认角色在线并记录 `account.running` / `worker.loop.enter`，账号worker为PID8736。其余五个worker保持原PID及启动时间。见 [挂机恢复记录](evidence/login-session-window-table-2026-10-25/runtime-resumed.json)。本次实机登录使用隔离构建的修复DLL；常驻客户端仍是此前的Debug版本，未在线替换。新DLL与使用说明位于桌面 `Roadhog_离线登录修复_20261025`，退出客户端后替换才会持久启用本次离线读取修复；已有配置和模型无需调整。完整状态及哈希见 [验证记录](evidence/login-session-window-table-2026-10-25/validation.json)。
