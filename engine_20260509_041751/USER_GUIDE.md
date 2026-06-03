# AetherEngine 开发指南

> **版本**: 3.2.0 | **更新**: 2026-05-05
>
> 本文档面向脚本开发者，介绍 AetherEngine 的架构、能力和开发工作流。
> 完整 API 函数签名请查阅 [LUA_API.md](LUA_API.md)。

---

## 目录

- [快速入门](#快速入门)
- [引擎架构](#引擎架构)
- [引擎全局热键](#引擎全局热键)
- [核心能力概览](#核心能力概览)
- [脚本间数据传递](#脚本间数据传递)
- [打包发布版本](#打包发布版本)
- [配置参考](#配置参考)
- [常用开发模式](#常用开发模式)
- [常见问题](#常见问题)

---

## 快速入门

### 什么是 AetherEngine？

AetherEngine 是一个基于 Lua 5.4 的脚本引擎，提供 **23 个功能模块、293+ 个 API 函数**，覆盖：

- **自动化操作** — 键盘、鼠标（含拟人轨迹）、后台输入
- **图像识别** — 截图、找图、找色、中文 OCR
- **进程操作** — 内存读写、特征码扫描、多级指针、驱动级操作
- **界面开发** — ImGui 即时模式 UI
- **网络与资源** — HTTP 请求、远程资源管理、自动更新
- **多任务** — 独立 Lua VM 并行执行、任务间数据共享
- **安全** — 网络验证（卡密）、AES/RC4 加解密
- **底层工具** — JIT 汇编、反汇编、FFI 调用系统 API

### 第一个脚本

```lua
log.info("Hello, AetherEngine!")
log.info("平台: " .. sys.platform() .. " | 版本: " .. sys.version())
sys.sleep(1000)
log.info("脚本执行完毕！")
```

### 基本概念

- **模块调用**：`模块名.函数名(参数)`，如 `sys.sleep(1000)`
- **返回值**：用变量接收，如 `local ver = sys.version()`
- **错误处理**：失败时通常返回 `nil` 或 `false`，建议检查返回值

---

## 引擎架构

### 两种运行模式

| | 编辑器模式 | 发布模式（默认） |
|------|-----------|---------|
| **启动** | 命令行加 `--gui` | 直接双击 exe |
| **界面** | 完整编辑器（代码编辑、工具菜单） | 仅脚本自己的 ImGui 界面 |
| **脚本启动** | 手动 Ctrl+F10 或菜单 | 验证通过后自动执行 `mainScript` |
| **脚本结束** | 保持运行 | 自动退出进程 |

### 脚本执行模型

```
┌──────────────────────────────────┐
│           AetherEngine           │
│                                  │
│  ┌──────────┐   ┌──────────┐    │
│  │  主脚本   │   │  任务脚本 │    │
│  │ (主 VM)   │   │ (独立 VM) │    │
│  │           │   │           │    │
│  │ imgui UI  │   │ 后台逻辑  │    │
│  └─────┬─────┘   └─────┬─────┘    │
│        │               │          │
│        └───共享数据/配置──┘          │
│                                  │
│  23 个 Lua 模块 (sys/proc/...)   │
└──────────────────────────────────┘
```

- **主脚本**（`mainScript`）：发布模式下验证后自动执行，通常包含 ImGui 界面
- **任务脚本**：通过 `task.run()` 启动，运行在独立的 Lua VM 中，互不干扰
- **数据共享**：多个脚本间通过 `sys.set_share()` / `sys.get_share()` 通信

### 路径解析规则

所有相对路径均以 **exe 所在目录**为基准解析，不受工作目录变化影响。

```
exe 目录/
├── AetherRunner.exe
├── config.json          → "config.json"
├── scripts/
│   └── main.lua         → "scripts/main.lua"
└── map/
    └── world.wmap       → "map/world.wmap"
```

---

## 引擎全局热键

编辑器模式下注册的 4 个系统热键（**任何窗口下**均可使用）：

| 热键 | 功能 | 说明 |
|------|------|------|
| `Ctrl + HOME` | 显示/隐藏 UI | 切换引擎界面可见性，隐藏后脚本仍在运行 |
| `Ctrl + F10` | 启动脚本 | 运行当前编辑器中打开的脚本（需先通过验证） |
| `Ctrl + F11` | 暂停/恢复 | 脚本运行中按下暂停，再按恢复 |
| `Ctrl + F12` | 停止脚本 | 强制终止正在运行的脚本 |

> **发布模式**下全局热键自动取消注册，脚本通过验证后自动启动。

脚本内部可通过 `hotkey` 模块实现自定义按键监听，详见 [LUA_API.md](LUA_API.md) 中的 hotkey 模块。

---

## 核心能力概览

> 以下为各能力的概述和关键示例。完整函数签名请查阅 [LUA_API.md](LUA_API.md)。

### 系统与环境 (`sys`, `log`)

获取系统信息、控制程序流程、日志输出、跨脚本共享数据。

```lua
log.info("版本: " .. sys.version())     -- 日志输出
sys.sleep(1000)                          -- 等待 1 秒
sys.set_share("hp", 100)                 -- 跨脚本共享数据
local hp = sys.get_share("hp")           -- 读取共享数据
local code, out = sys.exec("ipconfig")   -- 执行系统命令
```

### 多任务系统 (`task`)

在独立 Lua VM 中并行运行多个脚本，支持暂停、恢复、停止、进度查询。

```lua
-- 启动一个后台任务（脚本文件或代码字符串均可）
local id = task.run("scripts/worker.lua", {
    name = "打怪任务",
    target = "怪物A",      -- 自定义参数（注入为全局变量）
})

task.wait(id, 10000)       -- 等待最多 10 秒
task.stop(id)              -- 强制停止
```

任务选项表中除 `name`、`priority`、`auto_start` 外的字段，均作为字符串全局变量注入子脚本。

### 进程与内存 (`proc`, `driver`)

查找进程、读写内存、特征码扫描、多级指针解析。`driver` 模块提供驱动级读写，可操作受保护内存。

```lua
-- 查找进程并读取内存
local pid = proc.find("game.exe")
local base = proc.module(pid)
local hp = proc.read_float(pid, base + 0x1A0)

-- 多级指针表达式（自动解引用）
local addr = proc.eval_addr(pid, "[[[base + 0x100] + 0x200] + 0x28]")
local x, y, z = proc.read_vec3(pid, "[[base + 0xDDD] + 0x1C0] + 0x290")

-- 特征码扫描
local found = proc.scan(pid, "48 8B ?? 90", base, size)

-- 驱动级操作（需授权码）
driver.load("LICENSE_KEY")
driver.read_float(pid, addr)
```

两种模式切换：`proc.set_mode("api")` / `proc.set_mode("driver")`

### 窗口管理 (`wnd`)

查找、操作桌面窗口，发送消息。

```lua
local hwnd = wnd.find_ex(nil, "记事本")   -- 模糊匹配标题
wnd.set_foreground(hwnd)                    -- 带到前台
wnd.set_topmost(hwnd, true)                 -- 置顶
local x, y, w, h = wnd.client_rect(hwnd)   -- 获取客户区
```

### 输入模拟 (`keybd`, `mouse`, `trajectory`)

键盘和鼠标的前台/后台/驱动三种模式，鼠标支持拟人轨迹移动。

```lua
-- 前台键鼠
keybd.click(0x41)                    -- 按 A
keybd.combo({0x11, 0x43})            -- Ctrl+C
mouse.set_trajectory("average")      -- 拟人轨迹
mouse.move_to(500, 300)
mouse.click("left")

-- 后台操作（窗口不需要前台）
keybd.post_type(hwnd, "Hello")
mouse.post_click(hwnd, 400, 300, "left")

-- 驱动级（需先 driver.load）
driver.mouse_click("left")
driver.keybd_click(0x41)
```

轨迹模式：`"none"` / `"robot"` / `"fast"` / `"average"` / `"granny"` / `"precise"`

### 图像识别与 OCR (`vision`, `ocr`)

截图、找图、找色、中文文字识别。

```lua
-- 截图 + 找色
local img = vision.capture()
local x, y = vision.find_color(img, 0xFF0000, 10)  -- 找红色

-- 找图（模板匹配）
local tpl = vision.load("button.png")
local x, y, score = vision.find(img, tpl, 0.8)
vision.free(img)
vision.free(tpl)

-- OCR 文字识别
ocr.init({models_dir = "./models"})
local results = ocr.recognize(vision.capture(0, 0, 800, 600))
for _, r in ipairs(results) do
    log.info(r.text .. " (" .. r.score .. ")")
end
```

### 网络与远程资源 (`http`, `resource`)

HTTP 请求、文件下载、远程资源管理、自动更新、ZIP 压缩/解压。

```lua
-- HTTP 请求
local resp = http.get("https://api.example.com/data")
if resp.status == 200 then log.info(resp.body) end

-- 远程资源管理
resource.init("./cache")
resource.set_auth("owner", "key")

-- 检查更新 (需显式指定路径列表)
local updates = resource.check_update({"scripts/main.luac"})
for _, path in ipairs(updates) do resource.download(path) end

-- ZIP 压缩/解压 (基于 minizip-ng, 支持 AES-256 加密)
resource.zip("./data", "./backup.zip")              -- 压缩文件或目录
resource.zip("./data", "./secure.zip", "password")   -- 加密压缩
resource.unzip("./backup.zip", "./output")            -- 解压
resource.unzip("./secure.zip", "./output", "password") -- 解密解压
```

### 界面开发 (`imgui`)

基于 ImGui 的即时模式 UI，支持窗口、按钮、输入框、下拉框、表格等控件。

```lua
local name = "玩家1"
local speed = 1.0
local mode = 1

if imgui.begin("控制面板") then
    name = imgui.input_text("角色名", name)
    speed = imgui.slider_float("速度", speed, 0.1, 5.0)
    mode = imgui.combo("模式", mode, {"普通", "快速", "精确"})

    if imgui.button("开始", 120, 30) then
        log.info("开始！")
    end
    imgui.end_window()
end
```

### 安全与验证 (`auth`, `crypto`)

网络卡密验证、哈希计算、AES/RC4/ChaCha20 加解密、Base64/Hex 编码。

```lua
-- 卡密验证
local client = auth.new({host = "http://auth.example.com", app_id = "7", ...})
local ok = client:login("ABCD-1234-EFGH-5678")
client:start_heartbeat()

-- 加解密
local enc = crypto.aes_encrypt("机密", "1234567890123456")
local dec = crypto.aes_decrypt(enc, "1234567890123456")
log.info("MD5: " .. crypto.md5("Hello"))
```

### 配置管理 (`config`)

JSON 格式脚本配置文件读写（`script_config.json`），支持嵌套键。

```lua
config.load()
local name = config.get("player.name", "默认")
config.set("player.name", "新名字")
config.save()
```

> 注意：`config` 模块操作的是 `script_config.json`，与引擎的 `config.json` 是不同文件。

### 寻路系统 (`path`)

基于预录制地图（`.wmap`）的 A* 寻路，地图通过编辑器的「地图编辑器」录制。

```lua
path.load("map/world.wmap")
local route = path.find(100, 200, 0, 500, 600, 0)
if route then
    for _, pt in ipairs(route) do
        log.info(string.format("(%.0f, %.0f, %.0f)", pt.x, pt.y, pt.z))
    end
end
```

### 底层工具 (`asm`, `disasm`, `ffi`, `encoding`)

- **`asm`** — 运行时汇编编译（Keystone）
- **`disasm`** — 反汇编（Capstone），支持 x86/x64/ARM
- **`ffi`** — cffi-lua，直接调用系统 DLL 函数
- **`encoding`** — 字符编码转换（UTF-8/GBK/Big5/EUC-KR/Shift-JIS）

```lua
-- FFI 调用 Win32 API
local ffi = require("cffi")
ffi.cdef[[ int MessageBoxA(void*, const char*, const char*, unsigned int); ]]
ffi.load("user32").MessageBoxA(nil, "Hello!", "Test", 0)

-- 编码转换
local gbk = encoding.utf8_to_gbk("你好")
local utf8 = encoding.gbk_to_utf8(gbk)
```

---

## 脚本间数据传递

UI 脚本和后台工作脚本通常运行在不同任务中，有三种数据传递方式。

### 方式一：共享数据（推荐）

`sys.set_share()` / `sys.get_share()`，**实时**、**线程安全**，支持 int/float/string/bool。

```lua
-- UI 脚本写入
sys.set_share("target_name", "怪物A")
sys.set_share("attack_range", 50)

-- 工作脚本读取（每次循环获取最新值）
local target = sys.get_share("target_name") or "默认"
local range = sys.get_share("attack_range") or 30
```

### 方式二：配置文件

`config` 模块读写 `script_config.json`，适合**持久化**设置（重启后保留）。

```lua
-- 保存
config.set("settings.target", "怪物A")
config.save()

-- 加载
config.load()
local target = config.get("settings.target", "默认")
```

### 方式三：任务参数

`task.run()` 选项表中的自定义字段注入为子脚本的**全局变量**（仅字符串，启动时一次性传递）。

```lua
-- 启动时传递
task.run("scripts/worker.lua", { target_name = "怪物A", attack_range = "50" })

-- worker.lua 中直接访问
local range = tonumber(attack_range) or 30
```

### 对比

| | 共享数据 | 配置文件 | 任务参数 |
|------|---------|---------|---------|
| **实时性** | ✅ 实时 | ❌ 需 load/save | ❌ 仅启动时 |
| **持久化** | ❌ | ✅ | ❌ |
| **数据类型** | int/float/string/bool | JSON | 仅 string |
| **典型场景** | UI ↔ 后台实时同步 | 用户偏好 | 任务初始参数 |

### 组合使用

```lua
-- UI 主脚本
config.load()
local saved = config.get("last_target", "怪物A")

local function on_changed(target, range)
    sys.set_share("target", target)      -- 实时同步
    config.set("last_target", target)    -- 持久化
    config.save()
end

task.run("scripts/worker.lua", { name = "自动打怪" })
```

---

## 打包发布版本

### 步骤

1. **编辑 `config.json`** — 设置 `mainScript` 为主脚本路径（相对于 exe 目录）：

   ```json
   { "mainScript": "scripts/assistant_ui.lua" }
   ```

2. **放置脚本** — 将 `.lua` / `.luac` 文件放到 exe 同目录下的 `scripts/`

3. **双击 `AetherRunner.exe`** — 弹出验证 → 登录 → 自动执行 → 脚本结束自动退出

### 目录结构

```
发布包/
├── AetherRunner.exe          # 主程序
├── config.json               # 配置（mainScript、验证信息等）
├── scripts/                  # 脚本目录
│   ├── assistant_ui.lua      # 主脚本
│   └── worker.lua            # 工作脚本
├── signatures.json           # 特征码（可选）
├── map/                      # 地图文件（可选）
├── models/                   # OCR 模型（可选）
└── cache/                    # 缓存（自动创建）
```

### 注意事项

- `mainScript` 找不到时程序**直接退出**
- 所有相对路径以 exe 目录为基准，不受 CWD 变化影响
- `config.json` 中需正确配置 `authConfigData`（验证信息）
- 脚本配置（`config` 模块）保存在 `script_config.json`，与引擎 `config.json` 是不同文件

---

## 配置参考

引擎的 `config.json` 控制引擎行为、编辑器状态和工具配置。所有字段均为可选，缺省时使用默认值。

> **注意**：`config.json` 是引擎配置，与脚本的 `script_config.json`（`config` 模块操作）是**不同文件**。

### 核心配置（发布模式必填）

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `mainScript` | string | `""` | 发布模式主脚本路径（相对于 exe 目录），验证通过后自动执行 |
| `authConfigData` | string | `""` | 验证配置（base64 编码的 Lua 编译字节码），执行后返回 AuthConfig 表 |

### 运行时配置（发布模式行为控制）

这些设置**仅影响发布模式**。开发模式（`--gui`）下控制台和日志全开，忽略这些设置。

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `enableConsole` | bool | `false` | 是否创建控制台窗口 |
| `enableWindow` | bool | `false` | 是否创建 ImGui 窗口（`false` 时为无窗口纯热键模式） |
| `enableLogConsole` | bool | `false` | 日志输出到控制台 |
| `enableLogFile` | bool | `false` | 日志输出到文件 |
| `enableLogDebugView` | bool | `false` | 日志输出到 DebugView |
| `logLevel` | string | `"info"` | 日志等级：`trace` / `debug` / `info` / `warn` / `error` / `fatal` |

### 脚本与资源

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `scriptsDir` | string | `"./scripts"` | 脚本搜索目录 |
| `localMode` | bool | `false` | 本地模式（不从远程服务器加载资源） |

### 认证与卡密

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `rememberCard` | bool | `false` | 是否记住卡密（下次启动自动填入） |
| `savedUserCard` | string | `""` | 保存的用户卡密（发布模式） |
| `savedDevCard` | string | `""` | 保存的开发者卡密（编辑器模式） |

> 卡密也可通过 exe 同目录下的 `key.txt` 文件提供（纯文本），`config.json` 为空时自动回退读取。

### 编辑器界面（开发模式自动管理）

以下字段由编辑器自动读写，通常无需手动修改。

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `windowPosX` | float | `100` | 窗口 X 坐标 |
| `windowPosY` | float | `100` | 窗口 Y 坐标 |
| `windowWidth` | float | `1000` | 窗口宽度 |
| `windowHeight` | float | `600` | 窗口高度 |
| `fontSize` | float | `18.0` | 编辑器字体大小 |
| `activeTabIndex` | int | `0` | 当前活动标签页索引 |
| `openedFiles` | array | `[]` | 已打开的文件路径列表 |
| `recentFiles` | array | `[]` | 最近打开的文件列表（最多 10 个） |

### 数据更新器 (`dataUpdater`)

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `processName` | string | `""` | 目标进程名 |
| `useDriver` | bool | `false` | 使用驱动模式读取 |
| `sigFilePath` | string | `"signatures.json"` | 特征码文件路径 |

### 内存查看器 (`memoryViewer`)

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `processName` | string | `""` | 目标进程名 |
| `useDriver` | bool | `false` | 使用驱动模式 |
| `is64bit` | bool | `true` | 目标进程是否 64 位 |
| `splitRatio` | float | `0.55` | 面板分割比 |

### 地图编辑器 (`mapEditor`)

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `pid` | int | `0` | 目标进程 PID |
| `processName` | string | `""` | 进程名（可替代 PID） |
| `expression` | string | `""` | 坐标地址表达式 |
| `useDriver` | bool | `false` | 驱动模式 |
| `isUE5` | bool | `false` | UE5 模式（坐标为 3 个 double） |
| `is2D` | bool | `false` | 2D 模式（Z 坐标恒为 0） |
| `useInteger` | bool | `false` | 整型模式（坐标四舍五入为整数） |
| `mapFilePath` | string | `"map/world.wmap"` | 地图文件路径 |
| `zoomFactor` | float | `0.2` | 缩放系数 |
| `maxZoom` | float | `0.01` | 缩放上限 |
| `autoFit` | bool | `true` | 自动适配视图 |
| `autoFitRange` | float | `0` | 自动适配范围（0 = 全部） |
| `centerOnPlayer` | bool | `true` | 锁定角色居中 |
| `flipX` | bool | `false` | 画布左右翻转 |
| `flipY` | bool | `false` | 画布上下翻转 |
| `recordInterval` | float | `100` | 录制间距 |
| `autoConnect` | bool | `true` | 自动连接录制点 |
| `leftPanelWidth` | float | `340` | 左侧面板宽度 |

### 最小发布配置示例

```json
{
  "mainScript": "scripts/main.lua",
  "authConfigData": "<base64 编码的验证配置字节码>",
  "enableWindow": true
}
```

---

## 常用开发模式

### 热键控制模板

```lua
hotkey.start()
local running = false

while true do
    if hotkey.is_pressed(0x70) then       -- F1 开始/暂停
        running = not running
        log.info(running and "运行" or "暂停")
        sys.sleep(300)
    end
    if hotkey.is_pressed(0x7B) then break end  -- F12 退出

    if running then
        -- 自动化逻辑
    end
    sys.sleep(50)
end
hotkey.stop()
```

### 截图找图点击

```lua
local function find_and_click(tpl_path, threshold)
    local screen = vision.capture()
    local tpl = vision.load(tpl_path)
    if screen:valid() and tpl then
        local x, y = vision.find(screen, tpl, threshold or 0.8)
        vision.free(screen); vision.free(tpl)
        if x then
            mouse.move_to(x, y); mouse.click(); return true
        end
    end
    return false
end
```

### 等待目标出现

```lua
local function wait_for(find_fn, timeout_ms)
    local deadline = sys.time() + timeout_ms
    while sys.time() < deadline do
        local result = find_fn()
        if result then return result end
        sys.sleep(500)
    end
    return nil
end

-- 等待窗口出现（最多 10 秒）
local hwnd = wait_for(function() return wnd.find_ex(nil, "游戏") end, 10000)
```

### 资源自动更新启动

```lua
resource.init("./cache")
resource.set_auth("owner", "key")

local updates = resource.check_update({"scripts/main.luac", "scripts/utils.luac"})
for _, p in ipairs(updates) do resource.download(p) end

dofile("cache/scripts/main.luac")
```

### 多任务并行

```lua
local ids = {}
for i = 1, 3 do
    ids[i] = task.run(string.format([[
        for j = 1, 10 do
            log.info("工人 %d 第 " .. j .. " 步")
            sys.sleep(500)
        end
    ]], i), {name = "工人" .. i})
end
task.wait_all(30000)
task.cleanup()
```

---

## 常见问题

### Q: 脚本没有反应？
1. 检查日志中是否有错误
2. 目标窗口是否正确找到（打印 hwnd）
3. 鼠标/键盘模式是否正确（`api` / `driver` / `background`）
4. 是否缺少 `sys.sleep()` 导致循环过快

### Q: 后台操作没效果？
- 某些全屏程序不接受后台消息
- 尝试驱动模式：`keybd.set_mode("driver")`
- 确认 hwnd 指向正确的窗口/子窗口

### Q: 找图找色不准？
- 调整容差值（太小找不到，太大误匹配）
- 用 `vision.save()` 保存截图检查实际画面
- 分辨率不同时需重新截取模板

### Q: 远程资源下载失败？
- 确保已调用 `resource.init()` + `resource.set_auth()`
- 用 `resource.query()` 验证服务器上的路径是否正确
- `check_update` 需要显式传入路径列表，不再自动扫描缓存

### Q: 如何调试脚本？
1. **日志**：关键位置添加 `log.info()` 输出变量
2. **VSCode 调试**：调用 `sys.debug()` 启动 LuaPanda 断点调试
3. **逐步执行**：每步后加 `sys.sleep()` 和日志观察

### Q: UI 脚本参数怎么传给工作脚本？
三种方式：共享数据（实时）、配置文件（持久化）、任务参数（启动时传递）。详见 [脚本间数据传递](#脚本间数据传递)。

### Q: 怎么打包给用户？
在 `config.json` 中设置 `mainScript` 为你的主脚本路径，双击 exe 即可。详见 [打包发布版本](#打包发布版本)。

---

## 模块速查

**23 个核心模块**（293+ 个函数 + imgui/ffi）：

| 模块 | 功能 | 模块 | 功能 |
|------|------|------|------|
| `sys` | 系统信息、共享数据、PE 加载 | `log` | 日志输出 |
| `task` | 多任务管理 | `proc` | 进程/内存/AOB/指针 |
| `wnd` | 窗口查找与操作 | `keybd` | 键盘（前台/后台） |
| `mouse` | 鼠标（前台/后台/轨迹） | `vision` | 截图/找图/找色 |
| `ocr` | 中文 OCR | `http` | HTTP 请求/下载 |
| `hotkey` | 全局热键监听 | `crypto` | 哈希/编码/加解密 |
| `auth` | 网络卡密验证 | `config` | JSON 配置读写 |
| `resource` | 远程资源管理/ZIP压缩解压 | `path` | A* 寻路 |
| `trajectory` | 拟人鼠标轨迹 | `driver` | 驱动级操作 |
| `asm` | JIT 汇编 | `disasm` | 反汇编 |
| `ffi` | 调用系统 DLL | `encoding` | 编码转换 |
| `imgui` | ImGui 界面 | | |

> 完整函数列表、参数说明和代码示例请查阅 **[LUA_API.md](LUA_API.md)**。

---

> **文档版本**：3.2.0 | 如有疑问请联系管理员
