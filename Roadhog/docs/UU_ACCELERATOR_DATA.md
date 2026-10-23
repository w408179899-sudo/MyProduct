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
