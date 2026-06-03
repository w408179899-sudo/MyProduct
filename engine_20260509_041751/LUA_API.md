# AetherEngine Lua API 文档

> **版本**: 3.2.0 | **更新**: 2026-05-05 | **来源**: 基于 LuaExports.h/cpp + LuaApi_*.cpp 完整导出

---

## 模块索引

| 分类 | 模块 | 说明 |
|------|------|------|
| **基础** | [`sys`](#sys---系统模块) [`log`](#log---日志模块) [`task`](#task---任务模块) [`config`](#config---配置模块) | 系统信息、日志、多任务、JSON配置 |
| **进程与内存** | [`proc`](#proc---进程模块) [`driver`](#driver---驱动模块) | 进程管理、内存读写、AOB扫描、驱动级操作 |
| **输入控制** | [`keybd`](#keybd---键盘模块) [`mouse`](#mouse---鼠标模块) [`hotkey`](#hotkey---热键模块) [`trajectory`](#trajectory---轨迹模块) | 键盘/鼠标(前台+后台)、热键监听、拟人轨迹 |
| **窗口与视觉** | [`wnd`](#wnd---窗口模块) [`vision`](#vision---视觉模块) [`ocr`](#ocr---文字识别模块) | 窗口查找/操作、截图/找图/找色、中文OCR |
| **网络与资源** | [`http`](#http---网络模块) [`resource`](#resource---资源模块) [`auth`](#auth---认证模块) | HTTP请求、远程资源管理、网络验证 |
| **加密与编码** | [`crypto`](#crypto---加密模块) [`encoding`](#encoding---编码模块) | 哈希/AES/RC4/编解码、字符串编码转换 |
| **地图寻路** | [`path`](#path---路点寻路模块) [`grid`](#grid---网格地图模块) | 路点A*寻路、二值化网格地图寻路 |
| **底层工具** | [`asm`](#asm---汇编模块) [`disasm`](#disasm---反汇编模块) [`ffi`](#ffi---外部函数接口) | JIT汇编、反汇编、系统API调用 |
| **界面** | [`imgui`](#imgui---界面模块) | ImGui 即时模式 UI |

---

## sys - 系统模块

### 版本与平台

| 函数 | 返回 | 说明 |
|------|------|------|
| `sys.version()` | string | 引擎版本号 `x.y.z` |
| `sys.platform()` | string | `"windows"` / `"android"` / `"ios"` |
| `sys.arch()` | string | `"x64"` / `"x86"` / `"arm64"` / `"arm"` |
| `sys.info()` | table | `{version, platform, arch, bits}` |
| `sys.hwid()` | string | 硬件机器码 (32 字符小写 hex, 128-bit) |

```lua
-- 获取系统信息
log.info("版本: " .. sys.version())
log.info("平台: " .. sys.platform())
log.info("架构: " .. sys.arch())
local info = sys.info()
log.info("机器码: " .. sys.hwid())
```

### 硬件标识 (HWID)

`sys.hwid()` 基于 7 个稳定硬件因子通过 SHA-256 聚合得到, 输出 32 字符小写十六进制。
因子按固定顺序拼接, 单个因子缺失不会导致整体错位; 有效因子少于 2 个时返回空串。

| 等级 | 因子 | 变化条件 | 典型来源 |
|------|------|---------|---------|
| S | BIOS UUID / 系统 SN / 主板 SN | 换主板 | SMBIOS Type 0/1/2 |
| A | MachineGuid / Machine SID | 重装 Windows | 注册表 / LSA |
| D | 系统盘 SN | 换系统盘 | `IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS` + `IOCTL_STORAGE_QUERY_PROPERTY` |
| H | TPM 2.0 EK 公钥哈希 | 虚拟机克隆 / 换 TPM | TPM `TPM2_ReadPublic` |

### 进程与资源

| 函数 | 返回 | 说明 |
|------|------|------|
| `sys.pid()` | int | 当前进程ID |
| `sys.tid()` | int | 当前线程ID |
| `sys.cpu_count()` | int | CPU核心数 |
| `sys.memory_info()` | table | `{total, available, used, percent}` (MB) |

```lua
-- 获取进程和资源信息
log.info("当前PID: " .. sys.pid())
log.info("CPU核心数: " .. sys.cpu_count())
local mem = sys.memory_info()
log.info(string.format("内存: %dMB / %dMB (%.1f%%)", mem.used, mem.total, mem.percent))
```

### 时间

| 函数 | 返回 | 说明 |
|------|------|------|
| `sys.time()` | int | 毫秒时间戳 |
| `sys.tick()` | int | 微秒级高精度时间戳 |
| `sys.sleep(ms)` | - | 高精度休眠 |

```lua
-- 时间操作
local start = sys.tick()
sys.sleep(100)  -- 休眠 100ms
local elapsed = sys.tick() - start
log.info(string.format("耗时: %.2f ms", elapsed / 1000))
```

### 目录与环境

| 函数 | 返回 | 说明 |
|------|------|------|
| `sys.get_cwd()` | string | 当前工作目录 |
| `sys.set_cwd(path)` | bool | 设置工作目录 |
| `sys.tmpdir()` | string | 临时目录 |
| `sys.homedir()` | string | 用户主目录 |
| `sys.username()` | string | 当前用户名 |
| `sys.get_env(name)` | string | 获取环境变量 |
| `sys.set_env(name, val)` | bool | 设置环境变量 |

### 跨脚本共享数据

| 函数 | 返回 | 说明 |
|------|------|------|
| `sys.set_share(key, val)` | - | 设置共享数据 (int/float/string/bool) |
| `sys.get_share(key)` | any/nil | 获取共享数据 |

### 显示与输入设置

| 函数 | 返回 | 说明 |
|------|------|------|
| `sys.dpi()` | table | `{scale, x, y}` |
| `sys.screen_size()` | w, h | 屏幕尺寸 |
| `sys.get_code_page()` | int | 系统当前 ANSI 代码页 (如 936=GBK, 949=EUC-KR, 932=Shift-JIS) |
| `sys.winver()` | string | Windows 版本号 (如 `"10.0.19045.5011"`)，非 Windows 返回 `"unsupported"` |
| `sys.get_ppid()` | integer | 获取当前进程的父进程 PID (失败返回 0) |

> 鼠标加速度/速度设置已迁移到 [`mouse`](#mouse---鼠标模块) 模块 (`mouse.get_accel/set_accel/get_speed/set_speed`)

### 工具函数

| 函数 | 返回 | 说明 |
|------|------|------|
| `sys.msgbox(text, title?)` | bool | 弹窗提示 |
| `sys.exit(code?)` | - | 退出引擎 (code默认0) |
| `sys.suicide()` | - | 自毁：损毁调用栈后跳转到地址0，进程立即崩溃且无法被调试器捕获有效上下文 |
| `sys.exec(cmd)` | code, out | 执行命令 |
| `sys.debug()` | - | 启动 LuaPanda 调试器 (VSCode断点调试) |
| `sys.get_clipboard()` | string | 获取剪贴板 |
| `sys.set_clipboard(text)` | bool | 设置剪贴板 |
| `sys.auth_info()` | table/nil | 获取验证到期信息，返回 `{expire_time, remaining_days}` 或 `nil`（未登录） |
| `sys.mmap_pe(data, call?)` | table/nil, err | 内存映射加载 PE 文件（不落地加载 DLL） |
| `sys.free_pe(base)` | bool | 释放内存映射的 PE |

**`sys.mmap_pe` 详细说明：**

- **data** (`string`): PE 文件的原始二进制内容（通过 `io.open("xxx.dll","rb")` 读取）
- **call** (`bool`, 可选): 是否调用 `DllMain(DLL_PROCESS_ATTACH)`，默认 `false`
- **返回**: `{ base = 加载基址(int), size = 映像大小(int), exports = { 函数名 = 地址, ... } }` 或 `nil, error`

```lua
-- 内存加载 DLL 并调用 DllMain
local f = io.open("TestDll.dll", "rb")
local data = f:read("*a")
f:close()

local pe, err = sys.mmap_pe(data, true)  -- true = 调用 DllMain
if not pe then
    log.error("加载失败: " .. err)
    return
end

log.info(string.format("base=0x%X size=%d", pe.base, pe.size))

-- 遍历导出函数 (可通过 ffi 调用)
for name, addr in pairs(pe.exports) do
    log.info(string.format("  %s @ 0x%X", name, addr))
end

-- 用完释放
sys.free_pe(pe.base)
```

---

## log - 日志模块

| 函数 | 说明 |
|------|------|
| `log.trace(msg)` | 追踪日志 |
| `log.debug(msg)` | 调试日志 |
| `log.info(msg)` | 信息日志 |
| `log.warn(msg)` | 警告日志 |
| `log.error(msg)` | 错误日志 |
| `log.print(...)` | 类似 print，支持多参数，输出到日志 |

---

## task - 任务模块

### 任务信息

| 函数 | 返回 | 说明 |
|------|------|------|
| `task.id()` | int | 当前任务ID |
| `task.count()` | int | 任务总数 |
| `task.list()` | table | `[{id, name, status, progress}]` |
| `task.status(id)` | string | `"pending"/"running"/"paused"/"completed"/"cancelled"` |
| `task.info(id)` | table/nil | 任务详情 `{id,name,status,state,progress,priority,elapsed,error}` |

### 任务控制

| 函数 | 返回 | 说明 |
|------|------|------|
| `task.create(script, opts?)` | id | 创建任务 |
| `task.run(script, opts?)` | id | 创建并立即运行 |
| `task.start(id)` | bool | 启动 |
| `task.stop(id)` | bool | 停止 |
| `task.pause(id)` | bool | 暂停 |
| `task.resume(id)` | bool | 恢复 |
| `task.wait(id, ms?)` | bool | 等待完成 (ms默认-1无限等待) |
| `task.wait_all(ms?)` | - | 等待所有 |
| `task.stop_all()` | - | 停止所有 |
| `task.cleanup()` | int | 清理已完成 (返回清理数量) |
| `task.set_progress(0-1)` | - | 设置进度 (任务内) |
| `task.on_stop(fn)` | - | 注册停止回调 |

**`opts` 参数表：**

| 字段 | 类型 | 说明 |
|------|------|------|
| `name` | string | 任务名称（可选） |
| `priority` | string | 优先级 `"low"/"normal"/"high"`（可选） |
| `auto_start` | bool | 仅 `create` 有效，是否自动启动（可选） |
| *其他字段* | string | **自动注入为新任务的全局变量**（key=变量名, value=字符串值） |

> **跨任务传参**: opts 中除 `name`/`priority`/`auto_start` 外的所有字符串键值对，会作为全局变量注入到新任务的 Lua VM 中。

```lua
-- 多任务管理示例
local id = task.run([[
    -- 任务内代码
    for i = 1, 10 do
        task.set_progress(i / 10)
        sys.sleep(100)
    end
    log.info("任务完成")
]], {name = "MyTask", priority = "Normal"})

-- 通过文件路径运行任务 (路径 ≤260字符 且不含换行符时视为文件路径)
local id2 = task.run("scripts/worker.lua", {name = "Worker", priority = "high"})

-- 等待任务完成 (5秒超时)
if task.wait(id, 5000) then
    log.info("任务成功完成")
else
    log.info("任务超时")
    task.stop(id)
end

-- 暂停和恢复
task.pause(id)
sys.sleep(1000)
task.resume(id)

-- 获取任务信息
local info = task.info(id)
if info then
    log.info("名称: " .. info.name)       -- "MyTask"
    log.info("状态: " .. info.status)     -- "running"/"completed"/...
    log.info("进度: " .. info.progress)   -- 0.0 ~ 1.0
end

-- 通过 opts 传递参数给子任务 (自动注入为全局变量)
local id3 = task.run("scripts/worker.lua", {
    name = "DataWorker",
    target_url = "http://example.com",  -- 子任务中可直接用 target_url 全局变量
    max_retry = "3"                     -- 注意: 值必须是字符串
})

-- create + start 两步创建
local id4 = task.create("sys.sleep(100)\n", {name = "Deferred"})
-- 此时状态为 "pending"
task.start(id4)  -- 手动启动

-- 清理已完成的任务
task.cleanup()
```

---

## proc - 进程模块

> **注意**: proc 模块直接使用 PID 操作，无需 open/close。通过 `proc.list()` 或系统工具获取目标 PID 后直接调用各 API。

### 模式管理

| 函数 | 返回 | 说明 |
|------|------|------|
| `proc.set_mode(m)` | bool | 设置模式 `"api"/"driver"` |
| `proc.get_mode()` | string | 获取当前模式 |

```lua
-- 切换操作模式
if proc.set_mode("api") then
    log.info("已切换到 API 模式")
end
log.info("当前模式: " .. proc.get_mode())
```

### 进程查询

| 函数 | 返回 | 说明 |
|------|------|------|
| `proc.list()` | table | `[{pid, name}]` |
| `proc.exists(pid/name)` | bool | 检查存在 |
| `proc.is_alive(pid)` | bool | 检查存活 |
| `proc.name(pid)` | string | 进程名 |
| `proc.path(pid)` | string | 完整路径 |
| `proc.is_64bit(pid)` | bool | 是否64位 |
| `proc.priority(pid, level?)` | int/bool | 获取/设置优先级 (0=Idle,1=BelowNormal,2=Normal,3=AboveNormal,4=High,5=Realtime) |
| `proc.module(pid, name?)` | base, size | 模块信息 |
| `proc.modules(pid)` | table | `[{name, base, size}]` |
| `proc.threads(pid)` | table | `[{tid, priority}]` |
| `proc.window(pid)` | hwnd/nil | 主窗口 |
| `proc.memory(pid)` | table | `{working_set}` |

### 进程控制

| 函数 | 返回 | 说明 |
|------|------|------|
| `proc.create(cmd, workdir?, show?)` | pid | 创建进程 (workdir=工作目录, show=是否显示窗口,默认true) |
| `proc.kill(pid, exitCode?)` | bool | 终止 (exitCode 默认0) |
| `proc.suspend(pid)` | bool | 挂起 |
| `proc.resume(pid)` | bool | 恢复 |
| `proc.wait(pid, ms?)` | bool | 等待退出 (ms 默认无限等待) |
| `proc.call(pid, addr, ...)` | int\|nil, err? | 远程调用：向主线程插入 APC 执行目标函数，返回 RAX，最多 8 个参数 |

**`proc.call` 详细说明：**

- 通过 `NtGetNextThread` 获取目标进程主线程（第一个线程）
- 在目标进程分配 RWX 内存，写入 x64 shellcode stub + 参数数据
- 通过 `NtQueueApcThreadEx2` 特殊用户态 APC（Win11+）在主线程上下文中执行
- 等待最多 5 秒，完成后自动释放远程内存；超时不释放（call 可能仍在执行）
- 参数按 x64 调用约定传递：前 4 个 → RCX, RDX, R8, R9，后 4 个 → 栈
- 成功返回函数返回值 (RAX)，失败返回 nil + 错误信息

```lua
-- 无参调用
local ret = proc.call(pid, func_addr)

-- 带参数调用 (最多 8 个)
local ret = proc.call(pid, func_addr, arg1, arg2)

-- 示例: 调用目标进程中的函数并获取返回值
local base = proc.module(pid, "game.dll")
local func = base + 0x12340
local ret, err = proc.call(pid, func, 0x1, 0x2, 0x3)
if ret then
    log.info("return value: " .. string.format("0x%X", ret))
else
    log.error("call failed: " .. err)
end
```

> **`proc.call` vs `driver.init_call`/`driver.exec_call` vs `driver.init_call2`/`driver.exec_call2` 对比：**
>
> | | `proc.call` | `driver.init_call` + `exec_call` | `driver.init_call2` + `exec_call2` |
> |---|---|---|---|
> | **系统要求** | Win11+ | 需要加载驱动 | 需要加载驱动 |
> | **窗口句柄** | 不需要 | 需手动传入 hwnd | 自动获取 |
> | **返回值** | 函数返回值 | 函数返回值 | 函数返回值 |
> | **适用场景** | 通用远程调用，不需驱动 | 需要驱动支持的场景 | 驱动支持 + 无需窗口句柄 |

### 内存读写

| 函数 | 返回 | 函数 | 返回 |
|------|------|------|------|
| `proc.read_u8(pid,addr)` | int | `proc.write_u8(pid,addr,v)` | bool |
| `proc.read_u16(pid,addr)` | int | `proc.write_u16(pid,addr,v)` | bool |
| `proc.read_u32(pid,addr)` | int | `proc.write_u32(pid,addr,v)` | bool |
| `proc.read_u64(pid,addr)` | int | `proc.write_u64(pid,addr,v)` | bool |
| `proc.read_float(pid,addr)` | num | `proc.write_float(pid,addr,v)` | bool |
| `proc.read_double(pid,addr)` | num | `proc.write_double(pid,addr,v)` | bool |
| `proc.read_bytes(pid,addr,len)` | str | `proc.write_bytes(pid,addr,data)` | bool |
| `proc.read_string(pid,addr,max?)` | str | | |

### 内存分配/释放

| 函数 | 返回 | 说明 |
|------|------|------|
| `proc.alloc(pid, size, prot?)` | addr \| nil, err | 在目标进程分配内存，prot 默认 `"rw-"` |
| `proc.free(pid, addr)` | bool, err? | 释放目标进程中的内存 |
| `proc.protect(pid, addr, size, prot)` | old_prot \| nil, err | 修改内存页保护属性，返回旧保护属性字符串 |

prot 支持的格式：`"rwx"` / `"rw-"` / `"r-x"` / `"r--"` / `"--x"` / `"---"`

```lua
-- 分配可执行内存
local addr = proc.alloc(pid, 4096, "rwx")
if addr then
    proc.write_bytes(pid, addr, shellcode)
    proc.call(pid, addr)
    proc.free(pid, addr)
end

-- 分配数据内存 (默认 rw-)
local buf = proc.alloc(pid, 256)

-- 修改内存保护属性
local old = proc.protect(pid, addr, 4096, "rwx")  -- 改为可读写执行
proc.write_bytes(pid, addr, patch)
proc.protect(pid, addr, 4096, old)                 -- 恢复原保护属性
```

```lua
-- 内存读写示例 (直接使用 PID，无需 open/close)
local procs = proc.list()
local pid = nil
for _, p in ipairs(procs) do
    if p.name == "game.exe" then pid = p.pid; break end
end

if pid then
    local base, size = proc.module(pid)
    
    -- 读取不同类型的数据
    local byte_val = proc.read_u8(pid, base)
    local dword_val = proc.read_u32(pid, base + 0x100)
    local str_val = proc.read_string(pid, base + 0x200, 32)
    
    -- 写入数据
    proc.write_u32(pid, base + 0x100, 0x12345678)
end
```

### 地址表达式

| 函数 | 返回 | 说明 |
|------|------|------|
| `proc.eval_addr(pid, expr)` | int/nil | 解析地址表达式，返回最终地址 |
| `proc.read_vec3(pid, expr)` | x,y,z/nil | 读取连续 3 个 float (Vector3) |

表达式支持多级指针链 + 模块名:
```
[[[0x140001800] + 0x1000] + 0x200] + 0x28
[[[GameAssembly.dll + 0x18968] + 0x300] + 0x280] + 0x28
```

```lua
-- 解析地址表达式
local addr = proc.eval_addr(pid, "[[[GameAssembly.dll + 0x18968] + 0x300] + 0x280] + 0x28")
if addr then
    local hp = proc.read_float(pid, addr)
end

-- 读取角色坐标 (FVector/Vector3: 3个连续float)
local x, y, z = proc.read_vec3(pid, "[[[GameAssembly.dll + 0x18968] + 0x300] + 0x280] + 0x28")
if x then
    log.info(string.format("坐标: %.1f, %.1f, %.1f", x, y, z))
end
```

### 内存扫描

| 函数 | 返回 | 说明 |
|------|------|------|
| `proc.scan(pid, pattern, start?, size?)` | addr/nil | AOB 特征码扫描 |

**`proc.scan` 详细说明：**

- **pattern** (`string`): 十六进制字节模式，空格分隔，`??` 为通配符
- **start** (`int`, 可选): 扫描起始地址，默认为主模块基址
- **size** (`int`, 可选): 扫描范围大小，默认为主模块大小
- **返回**: 第一个匹配地址，未找到返回 `nil`

```lua
-- AOB 扫描示例
local pid = 12345
local base, size = proc.module(pid)

-- 扫描特征码 (?? 为通配符，匹配任意字节)
local addr = proc.scan(pid, "48 8B ?? 90", base, size)
if addr then
    log.info(string.format("找到地址: 0x%X", addr))
end

-- 不指定范围时默认扫描主模块
local addr2 = proc.scan(pid, "55 8B EC ?? ?? 83 EC")
```

---

## wnd - 窗口模块

### 窗口查找

| 函数 | 返回 | 说明 |
|------|------|------|
| `wnd.find(class?, title?)` | hwnd/nil | 精确匹配 (class优先) |
| `wnd.find_ex(class?, title?)` | hwnd/nil | 模糊匹配 |
| `wnd.find_by_pid(pid, class?, title?)` | hwnd/nil | 按PID查找 |
| `wnd.get_foreground()` | hwnd | 前台窗口 |

### 窗口信息

| 函数 | 返回 | 说明 |
|------|------|------|
| `wnd.get_title(hwnd)` | string | 标题 |
| `wnd.class_name(hwnd)` | string | 类名 |
| `wnd.get_pid(hwnd)` | int/nil | 进程ID |
| `wnd.get_tid(hwnd)` | int/nil | 线程ID |
| `wnd.wnd_rect(hwnd)` | x,y,w,h | 窗口矩形 |
| `wnd.client_rect(hwnd)` | x,y,w,h | 客户区矩形 |

### 窗口状态

| 函数 | 返回 | 说明 |
|------|------|------|
| `wnd.is_visible(hwnd)` | bool | 可见 |
| `wnd.is_minimized(hwnd)` | bool | 最小化 |
| `wnd.is_maximized(hwnd)` | bool | 最大化 |
| `wnd.is_enabled(hwnd)` | bool | 启用 |

### 窗口操作

| 函数 | 返回 | 说明 |
|------|------|------|
| `wnd.set_title(hwnd, t)` | bool | 设置标题 |
| `wnd.set_pos(hwnd, x, y)` | bool | 设置位置 |
| `wnd.set_size(hwnd, w, h)` | bool | 设置大小 |
| `wnd.move(hwnd, x,y,w,h)` | bool | 移动+调整大小 |
| `wnd.show(hwnd, cmd)` | bool | 显示/隐藏 (0隐藏,5显示) |
| `wnd.minimize(hwnd)` | bool | 最小化 |
| `wnd.maximize(hwnd)` | bool | 最大化 |
| `wnd.restore(hwnd)` | bool | 还原 |
| `wnd.close(hwnd)` | bool | 关闭 |
| `wnd.set_foreground(hwnd)` | bool | 设为前台 |
| `wnd.set_topmost(hwnd, b)` | bool | 置顶 |
| `wnd.enable(hwnd, b)` | bool | 启用/禁用 |
| `wnd.send_message(hwnd,msg,w,l)` | int | 同步消息 |
| `wnd.post_message(hwnd,msg,w,l)` | bool | 异步消息 |

---

## keybd - 键盘模块

### 前台输入

| 函数 | 返回 | 说明 |
|------|------|------|
| `keybd.set_mode(m)` | bool | 设置模式 `"api"/"driver"/"background"` |
| `keybd.get_mode()` | string | 获取当前模式 |
| `keybd.down(vk)` | bool | 按下 |
| `keybd.up(vk)` | bool | 释放 |
| `keybd.click(vk, delay?)` | bool | 点击 |
| `keybd.type(text, delay?)` | bool | 输入文本 |
| `keybd.combo(keys, delay?)` | bool | 组合键 `{0x11,0x41}`=Ctrl+A |

```lua
-- 前台键盘输入示例
keybd.set_mode("api")  -- 设置为前台模式

-- 单个按键
keybd.click(0x41)  -- 点击 A 键
keybd.click(0x0D)  -- 点击 Enter

-- 输入文本
keybd.type("Hello World", 50)  -- 每个字符间隔 50ms

-- 组合键
keybd.combo({0x11, 0x41})  -- Ctrl+A
keybd.combo({0x11, 0x43})  -- Ctrl+C
```

### 后台输入

| 函数 | 返回 | 说明 |
|------|------|------|
| `keybd.set_window(hwnd/title)` | bool | 设置目标窗口 |
| `keybd.post_key(hwnd, vk, down)` | bool | 按键 |
| `keybd.post_click(hwnd, vk)` | bool | 点击 |
| `keybd.post_type(hwnd, text)` | bool | 文本 |
| `keybd.post_combo(hwnd, keys)` | bool | 组合键 |

```lua
-- 后台键盘输入示例
local hwnd = wnd.find("Notepad")  -- 查找记事本窗口
if hwnd then
    keybd.set_window(hwnd)
    keybd.post_type(hwnd, "Hello from background")
    keybd.post_click(hwnd, 0x0D)  -- 后台发送 Enter
end
```

> hwnd 可以是句柄(int)或窗口标题(string)

**常用键码**: `0x08`Backspace `0x09`Tab `0x0D`Enter `0x10`Shift `0x11`Ctrl `0x12`Alt `0x1B`Esc `0x20`Space `0x30-39`0-9 `0x41-5A`A-Z `0x70-7B`F1-F12

---

## mouse - 鼠标模块

### 前台输入

| 函数 | 返回 | 说明 |
|------|------|------|
| `mouse.set_mode(m)` | bool | 设置模式 `"api"/"driver"/"background"` |
| `mouse.get_mode()` | string | 获取当前模式 |
| `mouse.set_trajectory(m)` | bool | 设置轨迹模式 `"none"/"robot"/"fast"/"average"/"granny"/"precise"` |
| `mouse.get_trajectory()` | string | 获取当前轨迹模式 |
| `mouse.position()` | x, y | 当前位置 |
| `mouse.move(dx, dy)` | bool | 相对移动 (默认) |
| `mouse.move_to(x, y)` | bool | 移动到屏幕绝对坐标 (使用 TrajectoryGenerator 拟人轨迹或 smoothstep) |
| `mouse.down(btn?)` | bool | 按下 `"left"/"right"/"middle"` |
| `mouse.up(btn?)` | bool | 释放 |
| `mouse.click(btn?, delay?)` | bool | 点击 |
| `mouse.double_click(btn?, delay?)` | bool | 双击 |
| `mouse.wheel(delta)` | bool | 滚轮 (正上负下) |
| `mouse.drag(x1,y1,x2,y2, duration?)` | bool | 拖拽 |

**轨迹模式说明** (`set_trajectory`):

| 模式 | 说明 |
|------|------|
| `"none"` | 默认: smoothstep 缓动, 无噪声 |
| `"robot"` | 直线匀速, 无噪声无过冲 |
| `"fast"` | 快速玩家: 快速精准, 低噪声 |
| `"average"` | 普通用户: 中速自然, 适中偏移和过冲 |
| `"granny"` | 老年人: 慢速, 高噪声, 多过冲 |
| `"precise"` | 精准: 适中速度, 低噪声, 无过冲 |

```lua
-- 前台鼠标输入示例
mouse.set_mode("api")

-- 设置拟人轨迹模式 (影响 move_to 的移动曲线)
mouse.set_trajectory("average")  -- 普通用户轨迹

-- 获取当前位置
local x, y = mouse.position()
log.info(string.format("鼠标位置: %d, %d", x, y))

-- 移动鼠标
mouse.move(10, 10)       -- 相对移动
mouse.move_to(500, 300)  -- 移动到绝对坐标 (自动使用轨迹算法+相对移动)

-- 点击操作
mouse.click("left")         -- 左键点击
mouse.double_click("left")  -- 双击
mouse.click("right")        -- 右键点击

-- 拖拽
mouse.drag(100, 100, 200, 200, 500)  -- 从 (100,100) 拖到 (200,200)，耗时 500ms

-- 滚轮
mouse.wheel(5)   -- 向上滚动
mouse.wheel(-5)  -- 向下滚动

-- 恢复默认轨迹
mouse.set_trajectory("none")
```

### 后台输入 (线程安全)

所有 `post_*` 方法通过参数传入 `hwnd`, 内部无共享状态, 支持多线程并行调用。
`hwnd` 参数支持整数句柄或字符串窗口标题 (自动缓存 2 秒, 避免频繁 FindWindow)。

| 函数 | 返回 | 说明 |
|------|------|------|
| `mouse.set_window(hwnd/title)` | bool | 设置目标窗口 (仅 background 模式前台方法用) |
| `mouse.post_move(hwnd, x, y)` | bool | 后台移动到客户区坐标 |
| `mouse.post_down(hwnd, btn?)` | bool | 后台按下 |
| `mouse.post_up(hwnd, btn?)` | bool | 后台释放 |
| `mouse.post_click(hwnd, x?, y?, btn?, delay?)` | bool | 后台点击 |
| `mouse.post_wheel(hwnd, delta)` | bool | 后台滚轮 |

### 鼠标系统设置

| 函数 | 返回 | 说明 |
|------|------|------|
| `mouse.get_accel()` | table | `{threshold1, threshold2, acceleration}` |
| `mouse.set_accel(t1,t2,a)` | bool | 设置鼠标加速度 |
| `mouse.get_speed()` | int | 鼠标速度 (1-20) |
| `mouse.set_speed(s)` | bool | 设置鼠标速度 |

```lua
-- 后台鼠标输入示例 (线程安全, 支持多 task 并行)
local hwnd = wnd.find("Game")
if hwnd then
    -- 后台点击窗口坐标 (客户区)
    mouse.post_click(hwnd, 400, 300, "left")
    
    -- 后台滚轮
    mouse.post_wheel(hwnd, 3)
    
    -- 用窗口标题也可以 (自动缓存 hwnd, 2秒 TTL)
    mouse.post_click("Game", 400, 300)
end
```

---

## vision - 视觉模块

### 截图

| 函数 | 返回 | 说明 |
|------|------|------|
| `vision.set_mode(mode)` | - | 设置截图模式 |
| `vision.get_mode()` | string | 获取截图模式 |
| `vision.capture()` | Image | 全屏 |
| `vision.capture(x,y,w,h)` | Image | 区域 |
| `vision.capture_window(hwnd, clientOnly?)` | Image | 截取指定窗口 (clientOnly=true 仅客户区) |

### 图像操作

| 函数 | 返回 | 说明 |
|------|------|------|
| `vision.load(path)` | Image/nil | 加载文件 |
| `vision.load_memory(data)` | Image/nil | 加载内存 |
| `vision.save(img, path)` | bool | 保存 (png/jpg/bmp) |
| `vision.free(img)` | - | 释放 |
| `vision.crop(img, x,y,w,h)` | Image | 裁剪 |
| `vision.resize(img, w, h)` | Image | 缩放 |
| `vision.to_gray(img)` | Image | 灰度 |
| `vision.to_binary(img, th)` | Image | 二值化 |
| `vision.compare(img1, img2)` | number | 相似度 (0-1) |

### 找图找色

| 函数 | 返回 | 说明 |
|------|------|------|
| `vision.pixel(img, x, y)` | int | 像素颜色 (ARGB) |
| `vision.find_color(img, color, tol?)` | x,y/nil | 找色 |
| `vision.find_all_colors(img, color, tol?, max?)` | table | 找所有颜色 |
| `vision.find(img, tpl, thresh?)` | x,y,score/nil | 找图 |
| `vision.find_all(img, tpl, thresh?, max?)` | table | 找所有 |
| `vision.find_multi_color(img, first_color, offsets)` | x,y/nil | 多点找色 |

**`vision.find_multi_color` 详细说明：**

多点找色：先找到满足 `first_color` 的像素，再验证相对偏移位置的颜色是否匹配。

- **first_color** (`table`): `{color = 0xRRGGBB, tolerance = 10}`
- **offsets** (`table`): 偏移点数组 `{{x=dx, y=dy, color=0xRRGGBB, tolerance=10}, ...}`

```lua
-- 多点找色示例: 找红色按钮 (中心红色 + 右侧白色文字 + 下方灰色边框)
local img = vision.capture()
local x, y = vision.find_multi_color(img,
    {color = 0xFF0000, tolerance = 15},       -- 第一个点: 红色
    {
        {x = 20, y = 0,  color = 0xFFFFFF, tolerance = 10},  -- 右偏20: 白色
        {x = 0,  y = 15, color = 0x808080, tolerance = 20},  -- 下偏15: 灰色
    }
)
if x then
    log.info(string.format("找到按钮: %d, %d", x, y))
    mouse.click_at(x, y)
end
vision.free(img)
```

```lua
-- 视觉识别示例
local img = vision.capture(0, 0, 1920, 1080)  -- 截图
if img:valid() then
    -- 找色 (红色: 0xFF0000)
    local x, y = vision.find_color(img, 0xFF0000, 10)
    if x then
        log.info(string.format("找到红色: %d, %d", x, y))
        mouse.move_to(x, y)
        mouse.click("left")
    end
    
    -- 找所有匹配的颜色
    local colors = vision.find_all_colors(img, 0xFF0000, 10, 100)
    for i, pos in ipairs(colors) do
        log.info(string.format("颜色 %d: %d, %d", i, pos.x, pos.y))
    end
    
    vision.free(img)
end
```

### Image方法

`img:valid()` `img:width()` `img:height()` `img:pixel(x,y)` `img:save(path)`

---

## ocr - 文字识别模块

| 函数 | 返回 | 说明 |
|------|------|------|
| `ocr.init(path/config)` | bool | 初始化OCR引擎 |
| `ocr.is_initialized()` | bool | 检查初始化状态 |
| `ocr.recognize(img)` | table | 识别文字 `[{text,score,box,detect_time,recognize_time}]` |
| `ocr.release()` | - | 释放OCR引擎 |

**`ocr.init` 配置参数：**

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `models_dir` | string | `"./models"` | NCNN 模型文件目录 |
| `padding` | int | 50 | 文本检测填充 |
| `max_side_len` | int | 1024 | 图像最大边长 (越大越慢越精) |
| `box_score_thresh` | float | 0.6 | 文本框置信度阈值 |
| `do_angle` | bool | true | 是否检测文字方向 |

> 也可直接传字符串作为模型目录: `ocr.init("./models")`

**`ocr.recognize` 返回字段：**

| 字段 | 说明 |
|------|------|
| `text` | 识别的文字 |
| `score` | 识别置信度 (0-1) |
| `box` | 文本框坐标 `{x1,y1,x2,y2,x3,y3,x4,y4}` (四角) |
| `detect_time` | 检测耗时 (ms) |
| `recognize_time` | 识别耗时 (ms) |

```lua
ocr.init({models_dir = "./models", max_side_len = 1024, box_score_thresh = 0.5})

local img = vision.capture(0, 0, 800, 600)
local results = ocr.recognize(img)
for _, r in ipairs(results) do
    log.info(string.format("[%.2f] %s", r.score, r.text))
end

vision.free(img)
ocr.release()
```

---

## http - 网络模块

| 函数 | 返回 | 说明 |
|------|------|------|
| `http.get(url, headers?)` | table | GET请求 `{status, body, headers, error}` |
| `http.post(url, body, content_type?, headers?)` | table | POST请求 |
| `http.download(url, path, headers?)` | table | 下载文件 `{success, size, error}` |
| `http.set_timeout(sec)` | - | 设置超时时间 |
| `http.url_encode(str)` | string | URL编码 |
| `http.url_decode(str)` | string | URL解码 |

```lua
-- HTTP 请求示例
http.set_timeout(10)  -- 设置 10 秒超时

-- GET 请求
local resp = http.get("https://api.example.com/data")
if resp.status == 200 then
    log.info("响应: " .. resp.body)
else
    log.error("错误: " .. (resp.error or "Unknown"))
end

-- POST 请求
local data = "name=test&value=123"
local resp = http.post("https://api.example.com/submit", data, "application/x-www-form-urlencoded")
log.info("状态码: " .. resp.status)

-- 下载文件
local result = http.download("https://example.com/file.zip", "./download.zip")
if result.success then
    log.info("下载成功: " .. result.size .. " 字节")
end

-- URL 编码
local encoded = http.url_encode("hello world & special=chars")
log.info("编码: " .. encoded)
```

---

## hotkey - 热键模块 (纯轮询)

全局单例，多任务安全。Lua 脚本通过 `is_pressed` 自行管理热键逻辑，无回调。

| 函数 | 返回 | 说明 |
|------|------|------|
| `hotkey.start(interval?)` | bool | 启动监听线程 (默认10ms) |
| `hotkey.stop()` | - | 停止监听线程 |
| `hotkey.is_running()` | bool | 是否运行 |
| `hotkey.is_pressed(vk)` | bool | 按键是否按下 |
| `hotkey.get_pressed()` | table | 当前所有按下的虚拟键码 |
| `hotkey.set_interval(ms)` | - | 设置轮询间隔 (>0) |

```lua
-- 热键轮询示例
hotkey.start(10)  -- 启动监听，10ms 间隔

-- 在循环中检查按键状态 (纯轮询，无回调)
while true do
    if hotkey.is_pressed(0x70) then  -- F1
        log.info("F1 被按下")
        mouse.click("left")
    end
    
    if hotkey.is_pressed(0x11) and hotkey.is_pressed(0x53) then  -- Ctrl+S
        log.info("Ctrl+S 被按下")
    end
    
    -- 获取所有按下的键
    local pressed = hotkey.get_pressed()
    if #pressed > 0 then
        for _, vk in ipairs(pressed) do
            log.info("按键: " .. string.format("0x%02X", vk))
        end
    end
    
    sys.sleep(50)
end

hotkey.stop()
```

---

## crypto - 加密模块

### 哈希

| 函数 | 返回 | 说明 |
|------|------|------|
| `crypto.md5(data)` | string(32) | MD5哈希 (十六进制) |
| `crypto.sha1(data)` | string(40) | SHA1哈希 (十六进制) |
| `crypto.sha256(data)` | string(64) | SHA256哈希 (十六进制) |
| `crypto.xxhash64(data, seed?)` | string(16) | XXHash64 (高性能非加密哈希) |

```lua
-- 哈希示例
local data = "Hello World"
log.info("MD5: " .. crypto.md5(data))
log.info("SHA1: " .. crypto.sha1(data))
log.info("SHA256: " .. crypto.sha256(data))
```

### 编码

| 函数 | 返回 | 说明 |
|------|------|------|
| `crypto.base64_encode(data)` | string | Base64编码 |
| `crypto.base64_decode(data)` | string | Base64解码 |
| `crypto.hex_encode(data)` | string | Hex编码 |
| `crypto.hex_decode(data)` | string | Hex解码 |

```lua
-- 编码示例
local data = "Hello"
local b64 = crypto.base64_encode(data)
log.info("Base64: " .. b64)
log.info("解码: " .. crypto.base64_decode(b64))

local hex = crypto.hex_encode(data)
log.info("Hex: " .. hex)
```

### 加密

| 函数 | 返回 | 说明 |
|------|------|------|
| `crypto.rc4(data, key)` | string | RC4 (对称) |
| `crypto.aes_encrypt(data, key16)` | string | AES加密 |
| `crypto.aes_decrypt(data, key16)` | string | AES解密 |
| `crypto.chacha20(data, key32, nonce12)` | string | ChaCha20 |
| `crypto.xxtea_encrypt(data, key)` | string | XXTEA加密 |
| `crypto.xxtea_decrypt(data, key)` | string | XXTEA解密 |
| `crypto.random(len, charset?)` | string | 随机字符串 |

```lua
-- 加密示例
local data = "Secret Message"
local key = "1234567890123456"  -- 16字节 AES key

-- AES 加密
local encrypted = crypto.aes_encrypt(data, key)
local decrypted = crypto.aes_decrypt(encrypted, key)
log.info("原文: " .. data)
log.info("解密: " .. decrypted)

-- RC4 加密
local rc4_encrypted = crypto.rc4(data, key)
local rc4_decrypted = crypto.rc4(rc4_encrypted, key)
log.info("RC4解密: " .. rc4_decrypted)

-- 生成随机字符串
local random_str = crypto.random(16)
log.info("随机: " .. random_str)
```

---

## auth - 认证模块

每个任务通过 `auth.new(config)` 创建独立的验证客户端实例 (userdata)，由 Lua GC 自动释放。

### 模块函数

| 函数 | 返回 | 说明 |
|------|------|------|
| `auth.new(config)` | AuthClient | 创建独立验证客户端 (userdata) |
| `auth.machine_code()` | string | 获取机器码 (无状态) |

### AuthClient 对象方法

| 方法 | 返回 | 说明 |
|------|------|------|
| `client:login(card)` | bool, result | 登录 |
| `client:is_logged_in()` | bool | 登录状态 |
| `client:start_heartbeat()` | - | 启动心跳 |
| `client:stop_heartbeat()` | - | 停止心跳 |
| `client:get_notice()` | string\|nil | 获取公告 |
| `client:get_variable(name)` | string\|nil | 获取远程变量 |
| `client:unbind()` | bool | 解绑 |
| `client:get_token()` | string | 获取 Token |

**配置表**:
```lua
{
    host, app_id, app_key,
    encrypt_algorithm = "aes"/"rc4"/"none"/"des"/"base64",
    encode_method = "base64"/"hex",
    signature_method = "md5"/"sha1",
    aes_key, aes_iv, rc4_key, des_key,
    encrypt_param_name, encrypt_param_value, encrypt_response,
    heartbeat_interval,
    verify_signature, verify_safe_code, verify_time_diff
}
```

```lua
-- 网络验证完整示例
local client = auth.new({
    host = "http://auth.example.com",
    app_id = "7",
    app_key = "your_app_key",
    encrypt_algorithm = "aes",
    encode_method = "base64",
    signature_method = "md5",
    aes_key = "1234567890123456",
    aes_iv = "1234567890123456"
})

-- 获取机器码 (无状态，不依赖客户端)
log.info("机器码: " .. auth.machine_code())

-- 登录
local success, result = client:login("CARD_NUMBER")
if success then
    log.info("登录成功, Token: " .. client:get_token())
    client:start_heartbeat()  -- 启动心跳保活
    
    local notice = client:get_notice()       -- 获取公告
    local var = client:get_variable("url")   -- 获取远程变量
else
    log.error("登录失败: " .. (result and result.message or "unknown"))
end
-- client 离开作用域后由 GC 自动释放 (停止心跳 + 释放资源)
```

---

## config - 配置模块

JSON 配置文件读写，支持嵌套 key、自动类型转换。配置文件路径固定为 `./script_config.json`，不允许 Lua 修改。

| 函数 | 返回 | 说明 |
|------|------|------|
| `config.load()` | bool, err | 加载配置文件 |
| `config.save()` | bool, err | 保存配置文件 |
| `config.get(key, default?)` | any | 获取配置项 (支持嵌套 key) |
| `config.set(key, value)` | bool | 设置配置项 |
| `config.delete(key)` | bool | 删除配置项 |
| `config.exists(key)` | bool | 检查配置项是否存在 |
| `config.keys()` | table | 获取所有顶级 key |
| `config.clear()` | bool | 清空配置 |
| `config.get_all()` | table | 获取全部配置 |
| `config.set_all(table)` | bool | 从 table 设置全部配置 |

```lua
-- 配置文件读写示例
config.load()  -- 加载配置

-- 读写基本类型
config.set("username", "admin")
config.set("max_retry", 3)
config.set("enabled", true)

-- 嵌套 key (自动创建中间对象)
config.set("window.width", 800)
config.set("window.height", 600)

-- 读取配置 (支持默认值)
local width = config.get("window.width", 1024)
local name = config.get("username", "guest")

-- 检查和删除
if config.exists("window.width") then
    config.delete("window.width")
end

-- 获取所有配置
local all = config.get_all()
for k, v in pairs(all) do
    log.info(k .. " = " .. tostring(v))
end

-- 从 table 批量设置
config.set_all({
    name = "MyApp",
    version = "1.0.0",
    settings = { debug = true }
})

-- 保存到文件
config.save()
```

---

## path - 路点寻路模块

基于 WaypointMap 全局单例，使用 .wmap 地图文件 + A* 算法。
地图文件由工具菜单中的「路点地图编辑器」录制生成。

| 函数 | 返回 | 说明 |
|------|------|------|
| `path.load(file)` | bool | 加载 .wmap 地图文件到全局单例 |
| `path.find(x1,y1,z1, x2,y2,z2, maxRange?)` | table/nil | 根据起止坐标寻路，返回 `[{x,y,z,id,label}, ...]` |

- **maxRange**: 可选，起终点匹配最近节点的最大距离。<=0 不限制 (默认)，>0 时超出范围返回 nil

```lua
-- 加载地图
path.load("map/world.wmap")

-- 从坐标 (100,200,0) 到 (500,600,0) 寻路
local route = path.find(100, 200, 0, 500, 600, 0)
if route then
    for i, pt in ipairs(route) do
        log.info(string.format("#%d -> (%.0f, %.0f, %.0f)", pt.id, pt.x, pt.y, pt.z))
    end
end

-- 限制匹配范围: 起终点必须在 500 距离内有路点
local route2 = path.find(100, 200, 0, 500, 600, 0, 500)
```

---

## grid - 网格地图模块

二值化网格地图寻路，适用于大规模游戏地图 (如 576×512)。1 bit/cell 紧凑存储，8方向 A* 寻路。
支持 .gmap 二进制文件和 CSV 文本格式，通过「网格地图编辑器」可视化编辑。

### 加载与管理

| 函数 | 返回 | 说明 |
|------|------|------|
| `grid.load(name, path)` | bool | 加载地图 (.gmap/.csv/.txt 自动识别) |
| `grid.load_string(name, csv)` | bool | 从 CSV 字符串加载 |
| `grid.save(name, path)` | bool | 保存 (.gmap 或 .csv，按扩展名) |
| `grid.reload(name, path)` | bool | 重新加载地图文件 |
| `grid.unload(name)` | bool | 卸载地图 |
| `grid.unload_all()` | - | 卸载所有 |
| `grid.list()` | table | 已加载地图名称列表 |
| `grid.info(name)` | table/nil | `{width, height, cell_size, walkable_count}` |

### 查询与编辑

| 函数 | 返回 | 说明 |
|------|------|------|
| `grid.is_walkable(name, x, y)` | bool | 格子是否可通行 |
| `grid.set_cell(name, x, y, obstacle)` | - | 设置格子状态 (true=障碍) |

### 寻路

| 函数 | 返回 | 说明 |
|------|------|------|
| `grid.find_path(name, x1,y1, x2,y2, allowDiagonal?)` | table/nil | A* 寻路，输入/输出均为游戏坐标，返回 `[{x,y}, ...]`。`allowDiagonal` 默认 true (8方向)，false 为仅4方向 |

### 坐标映射

| 函数 | 返回 | 说明 |
|------|------|------|
| `grid.set_origin(name, x, y, z?)` | - | 设置地图原点 (游戏坐标, 对应 CSV 左上角) |
| `grid.set_cell_size(name, size)` | - | 设置比例尺 (1格=多少游戏单位) |
| `grid.set_axis(name, flipX, flipY)` | - | 设置轴方向 (true=反向) |
| `grid.get_origin(name)` | x, y | 获取地图原点坐标 |

```lua
-- 加载 CSV 地图 (0=通行, 1=障碍)
grid.load("world", "map/map-0.txt")

-- 配置坐标映射
grid.set_origin("world", 32256, 32768, 0)  -- CSV(0,0) = 游戏(32256,32768)
grid.set_cell_size("world", 1)              -- 1格 = 1游戏单位

-- 游戏坐标寻路 (输入输出均为游戏坐标)
local path = grid.find_path("world", 32456, 32968, 32656, 33068)
if path then
    for _, pt in ipairs(path) do
        log.info(string.format("(%.0f, %.0f)", pt.x, pt.y))
    end
end

-- 获取原点 (想用网格坐标时可先备份原点然后设为0,0)
local ox, oy = grid.get_origin("world")

-- 保存为紧凑二进制 (含 origin/cellSize 配置)
grid.save("world", "map/map-0.gmap")
```

---

## trajectory - 轨迹模块

| 函数 | 返回 | 说明 |
|------|------|------|
| `trajectory.generate(x1,y1,x2,y2,nature?)` | table | 生成轨迹点 `[{x,y,time,pressure}]` |
| `trajectory.generate_overshoot(x1,y1,x2,y2,nature?)` | table | 带过冲轨迹 |
| `trajectory.preset(name)` | table | 预设配置 |
| `trajectory.set_seed(n)` | - | 设置随机种子 |

**预设**: `"robot"` `"fast_gamer"` `"granny"` `"touch_swipe"` `"touch_swipe_fast"` `"touch_drag"` `"joystick_smooth"` `"joystick_aim"` `"precise"` `"joystick_fast"` `"joystick_return"`

**自定义 nature 参数表：**

| 字段 | 类型 | 说明 |
|------|------|------|
| `min_speed` | number | 最小速度 (px/s) |
| `max_speed` | number | 最大速度 (px/s) |
| `reaction_time` | number | 反应时间 (ms) |
| `deviation` | number | 偏移系数 (越大越弯曲) |
| `deviation_spread` | number | 偏移分布范围 |
| `noise` | number | 噪声强度 |
| `noise_frequency` | number | 噪声频率 |
| `overshoot` | bool | 是否启用过冲 |
| `overshoot_probability` | number | 过冲概率 (0-1) |
| `overshoot_distance` | number | 过冲距离 |
| `max_overshoots` | int | 最大过冲次数 |
| `flow` | string | 速度曲线: `"constant"` `"ease_in"` `"ease_out"` `"ease_in_out"` `"sinusoidal"` `"random"` |
| `input_type` | string | 输入类型: `"mouse"` `"touch"` `"joystick"` |
| `deadzone` | number | 摇杆死区 |
| `return_to_center` | bool | 摇杆是否回中 |
| `pressure_start/end/max` | number | 触控压力参数 |

```lua
-- 使用预设
local points = trajectory.generate(0, 0, 500, 300, trajectory.preset("fast_gamer"))

-- 自定义参数
local points2 = trajectory.generate(0, 0, 500, 300, {
    min_speed = 300, max_speed = 800,
    deviation = 1.5, noise = 0.3,
    overshoot = true, overshoot_probability = 0.4,
    flow = "ease_in_out"
})

-- 轨迹点格式: {x, y, time, pressure}
for _, pt in ipairs(points) do
    mouse.move(pt.x, pt.y)
    sys.sleep(pt.time)
end
```

---

## resource - 资源模块

远程资源管理（下载/上传/查询/更新检查）+ ZIP 压缩/解压（基于 minizip-ng，支持 AES-256 加密）。模块只负责传输原始数据，不会自动加解密。

### 初始化与配置

| 函数 | 返回 | 说明 |
|------|------|------|
| `resource.init(cache_dir?)` | - | 初始化资源管理器 (默认 `"./cache"`) |
| `resource.set_auth(owner, key, pass?)` | - | 设置服务器认证参数 |

### 服务端 API

| 函数 | 返回 | 说明 |
|------|------|------|
| `resource.download(path)` | data\|nil, err | 下载资源，返回原始数据；自动缓存并校验 XXHash64 |
| `resource.query(path)` | info\|nil, err | 查询资源元数据 `{path, file_name, size, xxhash, upload_time, description}` |
| `resource.upload(path, data, desc?, pass?)` | bool, err? | 上传资源到服务器 |

### 更新检查

| 函数 | 返回 | 说明 |
|------|------|------|
| `resource.check_update(paths)` | table | 对比本地缓存文件 XXHash64 与服务器，返回需更新的路径列表。**必须**传入路径 table |

### 压缩/解压

| 函数 | 返回 | 说明 |
|------|------|------|
| `resource.zip(src, zip_path, password?)` | bool, err? | 压缩文件或目录为 ZIP。支持可选 AES-256 加密 |
| `resource.unzip(zip_path, dest_dir, password?)` | bool, err? | 解压 ZIP 到目标目录。支持可选密码解密 |

### 缓存管理

| 函数 | 返回 | 说明 |
|------|------|------|
| `resource.clear_cache()` | - | 清空所有本地缓存 |

```lua
-- 完整使用示例
resource.init("./cache")
resource.set_auth("admin", "my_key")

-- 检查更新 (必须显式指定路径列表)
local updates = resource.check_update({"scripts/main.luac", "scripts/utils.luac"})
for _, p in ipairs(updates) do
    resource.download(p)
end

-- 下载资源
local data, err = resource.download("scripts/main.luac")
if data then
    log.info("大小: " .. #data)
end

-- 查询元数据
local info = resource.query("scripts/main.luac")
if info then
    log.info("文件: " .. info.file_name .. " 哈希: " .. info.xxhash)
end

-- 上传
resource.upload("configs/data.lua", "return {version='1.0'}", "配置文件")

-- ZIP 压缩/解压
resource.zip("./data", "./backup.zip")                -- 压缩目录
resource.zip("./secret.txt", "./enc.zip", "pass123")  -- 加密压缩
resource.unzip("./backup.zip", "./output")             -- 解压
resource.unzip("./enc.zip", "./output", "pass123")     -- 解密解压
```

---

## asm - 汇编模块

| 函数 | 返回 | 说明 |
|------|------|------|
| `asm.arch()` | string | 当前架构 |
| `asm.archs()` | table | 支持的架构列表 |
| `asm.compile(code, arch?)` | CodeBlock, err | 编译汇编 |
| `asm.emit(bytes)` | CodeBlock, err | 从字节数组生成 |

**CodeBlock 对象方法：**

| 方法 | 返回 | 说明 |
|------|------|------|
| `code:valid()` | bool | 是否有效 |
| `code:size()` | int | 字节码大小 |
| `code:ptr()` | lightuserdata | 可执行内存指针（用于 ffi.cast） |
| `code:hex()` | string | 十六进制字节串 |

> CodeBlock 持有可执行内存（VirtualAlloc + PAGE_EXECUTE_READWRITE），由 Lua GC 自动释放。

**`asm.emit(bytes)` 说明：** 直接从字节数组 `{0x48, 0x89, ...}` 创建可执行 CodeBlock，不经过汇编编译。

```lua
-- 编译汇编并通过 ffi 调用
local code, err = asm.compile("mov eax, 0x2a; ret", "x64")
if code then
    log.info("字节码: " .. code:hex())  -- "B8 2A 00 00 00 C3"
    log.info("大小: " .. code:size())   -- 6
    
    local ffi = require("cffi")
    ffi.cdef[[typedef int (*IntFn)(void);]]
    local fn = ffi.cast("IntFn", code:ptr())
    local result = fn()  -- 返回 42
    log.info("结果: " .. result)
end

-- 从字节数组直接创建可执行代码
local code2 = asm.emit({0xB8, 0x2A, 0x00, 0x00, 0x00, 0xC3})  -- mov eax, 42; ret

-- 查看支持的架构
log.info("当前架构: " .. asm.arch())      -- "x64"
log.info("支持: " .. table.concat(asm.archs(), ", "))  -- "x86, x64, arm, arm64, thumb"
```

---

## ffi - 外部函数接口

基于 **cffi-lua** 实现（兼容 LuaJIT ffi 语法），用于调用系统 API、外部 DLL 和任意内存地址的函数。

**核心 API：**

| 函数 | 说明 |
|------|------|
| `ffi.cdef[[...]]` | 声明 C 类型和函数原型 |
| `ffi.cast(type, value)` | 类型转换（**整数地址 → 函数指针**） |
| `ffi.load(lib)` | 加载动态库，返回命名空间对象 |
| `ffi.C` | 默认 C 库命名空间 |
| `ffi.new(type, ...)` | 分配 C 类型实例 |
| `ffi.sizeof(type)` | 获取类型大小 |
| `ffi.string(ptr, len?)` | C 指针转 Lua 字符串 |

**预注册类型：** 引擎启动时自动注册 `i8/i16/i32/i64/u8/u16/u32/u64/f32/f64` 以及 Windows 类型 `HANDLE/HWND/HMODULE/DWORD/BOOL/LPCSTR`。

### 加载 DLL 调用导出函数

```lua
local ffi = require("cffi")

ffi.cdef[[
    typedef unsigned long DWORD;
    DWORD GetTickCount(void);
    DWORD GetCurrentProcessId(void);
    int MessageBoxA(void* hwnd, const char* text, const char* caption, unsigned int type);
]]

-- 方式1: ffi.load 加载指定 DLL
local kernel32 = ffi.load("kernel32")
local tick = kernel32.GetTickCount()
local pid = kernel32.GetCurrentProcessId()

-- 方式2: ffi.C 调用默认 C 库 (user32 等已链接的)
ffi.C.MessageBoxA(nil, "Hello", "AetherEngine", 0)
```

### 给定地址直接调用函数

通过 `ffi.cast` 将整数地址转为函数指针后直接调用：

```lua
local ffi = require("cffi")

-- 1. 声明函数类型
ffi.cdef[[
    typedef int (*AddFunc)(int a, int b);
    typedef void (*VoidFunc)(void);
    typedef bool (*InitFunc)(const char* config);
]]

-- 2. 将整数地址 cast 为函数指针
local addr = 0x7FF612340000  -- 已知函数地址
local fn = ffi.cast("AddFunc", addr)
local result = fn(10, 20)  -- 直接调用, 返回 30
```

### 配合 sys.mmap_pe 调用 DLL 导出函数

```lua
local ffi = require("cffi")

-- 内存加载 DLL
local f = io.open("MyPlugin.dll", "rb")
local pe = sys.mmap_pe(f:read("*a"), true)
f:close()

-- 声明导出函数签名
ffi.cdef[[
    typedef int (*PluginInit)(const char* config);
    typedef const char* (*PluginVersion)(void);
]]

-- 通过 exports 表获取地址并 cast 调用
if pe and pe.exports.PluginInit then
    local init_fn = ffi.cast("PluginInit", pe.exports.PluginInit)
    local ret = init_fn("debug=true")
    log.info("PluginInit 返回: " .. ret)
end

if pe and pe.exports.PluginVersion then
    local ver_fn = ffi.cast("PluginVersion", pe.exports.PluginVersion)
    local ver = ffi.string(ver_fn())
    log.info("版本: " .. ver)
end

-- 用完释放
sys.free_pe(pe.base)
```

### 配合 asm 模块调用生成的机器码

```lua
local ffi = require("cffi")
ffi.cdef[[ typedef int (*IntFn)(void); ]]

local code = asm.compile("mov eax, 42; ret", "x64")
local fn = ffi.cast("IntFn", code:ptr())  -- ptr() 返回 lightuserdata
log.info("结果: " .. fn())  -- 42
```

### 结构体与指针

```lua
local ffi = require("cffi")
ffi.cdef[[
    typedef struct { float x, y, z; } Vec3;
]]

-- 分配并使用结构体
local pos = ffi.new("Vec3", {100.0, 200.0, 0.0})
log.info(string.format("坐标: %.1f, %.1f, %.1f", pos.x, pos.y, pos.z))

-- 获取结构体大小
log.info("Vec3 大小: " .. ffi.sizeof("Vec3"))  -- 12
```

---

## imgui - 界面模块

> **颜色参数**: 所有 `color` 参数支持两种格式：
> - 整数 `0xAARRGGBB` (如 `0xFFFF0000` = 红色)
> - 四个分量 `r, g, b, a` (0-255，此时占4个参数位)
>
> 辅助函数: `imgui.color(r,g,b,a)` → `0xAARRGGBB`，`imgui.color_rgba(0xRRGGBBAA)` → `0xAARRGGBB`

### 窗口

| 函数 | 返回 | 说明 |
|------|------|------|
| `imgui.begin_window(name, flags?)` | bool | 开始窗口 (无关闭按钮) |
| `imgui.begin_window(name, true, flags?)` | visible, open | 有关闭按钮, open=false 表示用户点了关闭 |
| `imgui.end_window()` | - | 结束窗口 |

> **注意**: `open=false` 时窗口不会自动消失，**脚本必须自行记录状态**并在后续帧停止调用 `begin_window`：
> ```lua
> local wnd_open = true
> -- 在渲染回调中:
> if wnd_open then
>     local visible, open = imgui.begin_window("窗口", true)
>     wnd_open = open  -- 记住关闭状态
>     if visible then
>         -- 绘制内容
>     end
>     imgui.end_window()
> end
> ```
| `imgui.begin_child(id, w?, h?, border?, flags?)` | bool | 子区域 |
| `imgui.end_child()` | - | 结束子区域 |

### 绘制 (屏幕前景层)

| 函数 | 说明 |
|------|------|
| `imgui.add_line(x1, y1, x2, y2, color, thickness?)` | 画线 (thickness 默认 1) |
| `imgui.add_rect(x1, y1, x2, y2, color, rounding?, thickness?)` | 矩形边框 |
| `imgui.add_rect_filled(x1, y1, x2, y2, color, rounding?)` | 填充矩形 |
| `imgui.add_circle(x, y, radius, color, segments?, thickness?)` | 圆形边框 |
| `imgui.add_circle_filled(x, y, radius, color, segments?)` | 填充圆形 |
| `imgui.add_triangle(x1,y1, x2,y2, x3,y3, color, thickness?)` | 三角形边框 |
| `imgui.add_triangle_filled(x1,y1, x2,y2, x3,y3, color)` | 填充三角形 |
| `imgui.add_text(x, y, text, color)` | 绘制文本 |

### 控件

| 函数 | 返回 | 说明 |
|------|------|------|
| `imgui.text(s)` | - | 文本 |
| `imgui.text_colored(color, s)` | - | 彩色文本 |
| `imgui.text_wrapped(s)` | - | 自动换行文本 |
| `imgui.text_disabled(s)` | - | 灰色文本 |
| `imgui.label_text(label, s)` | - | 标签+文本 |
| `imgui.bullet_text(s)` | - | 带圆点的文本 |
| `imgui.bullet()` | - | 圆点 |
| `imgui.button(s, w?, h?)` | bool | 按钮 |
| `imgui.small_button(s)` | bool | 小按钮 |
| `imgui.invisible_button(id, w, h)` | bool | 透明按钮 |
| `imgui.arrow_button(id, dir)` | bool | 箭头按钮 (dir: 0左/1右/2上/3下) |
| `imgui.checkbox(s, v)` | v, changed | 复选框 |
| `imgui.radio_button(s, v, val)` | v, changed | 单选框 |
| `imgui.input_text(s, v, size?)` | v, changed | 单行输入 |
| `imgui.input_text_multiline(s, v, w?, h?)` | v, changed | 多行输入 |
| `imgui.input_int(s, v, step?)` | v, changed | 整数输入 |
| `imgui.input_float(s, v, step?)` | v, changed | 浮点输入 |
| `imgui.slider_int(s, v, min, max)` | v, changed | 整数滑块 |
| `imgui.slider_float(s, v, min, max)` | v, changed | 浮点滑块 |
| `imgui.drag_int(s, v, speed?, min?, max?)` | v, changed | 整数拖拽 |
| `imgui.drag_float(s, v, speed?, min?, max?)` | v, changed | 浮点拖拽 |
| `imgui.combo(s, idx, items)` | idx, changed | 下拉框 |
| `imgui.list_box(s, idx, items, height?)` | idx, changed | 列表框 |
| `imgui.selectable(s, selected?, flags?)` | bool | 可选项 |
| `imgui.progress_bar(frac, w?, h?, text?)` | - | 进度条 |
| `imgui.color_edit3(s, r, g, b)` | r,g,b, changed | RGB颜色编辑 |
| `imgui.color_edit4(s, r, g, b, a)` | r,g,b,a, changed | RGBA颜色编辑 |
| `imgui.color_button(id, color, flags?, w?, h?)` | bool | 颜色按钮 |

### 布局

| 函数 | 说明 |
|------|------|
| `imgui.same_line(offset?, spacing?)` | 同行 |
| `imgui.new_line()` | 新行 |
| `imgui.separator()` | 分隔线 |
| `imgui.spacing()` | 间距 |
| `imgui.indent(w?)` | 增加缩进 |
| `imgui.unindent(w?)` | 减少缩进 |
| `imgui.dummy(w, h)` | 占位空白 |
| `imgui.set_cursor_pos(x, y)` | 设置光标位置 |
| `imgui.get_cursor_pos()` → x, y | 获取光标位置 |
| `imgui.get_content_region_avail()` → w, h | 获取可用区域大小 |
| `imgui.calc_text_size(text)` → w, h | 计算文本尺寸 |

### 控件宽度

| 函数 | 说明 |
|------|------|
| `imgui.set_next_item_width(w)` | 设置下个控件宽度 (正=像素, 负=距右边距) |
| `imgui.push_item_width(w)` | 压入宽度 (影响后续所有控件) |
| `imgui.pop_item_width()` | 弹出宽度 |
| `imgui.calc_item_width()` → w | 获取当前控件宽度 |

### 树节点

| 函数 | 返回 | 说明 |
|------|------|------|
| `imgui.tree_node(label)` | bool | 树节点 (展开时返回 true, 需调用 tree_pop) |
| `imgui.tree_node_ex(label, flags?)` | bool | 树节点 (带标志) |
| `imgui.tree_pop()` | - | 结束树节点 |
| `imgui.collapsing_header(label, flags?)` | bool | 折叠标题 |
| `imgui.set_next_item_open(open, cond?)` | - | 设置下个树节点展开状态 |

### 标签页

| 函数 | 返回 | 说明 |
|------|------|------|
| `imgui.begin_tab_bar(id, flags?)` | bool | 开始标签栏 |
| `imgui.end_tab_bar()` | - | 结束标签栏 |
| `imgui.begin_tab_item(label, flags?)` | bool | 开始标签项 |
| `imgui.end_tab_item()` | - | 结束标签项 |

### 菜单

| 函数 | 返回 | 说明 |
|------|------|------|
| `imgui.begin_menu_bar()` | bool | 窗口菜单栏 |
| `imgui.end_menu_bar()` | - | |
| `imgui.begin_main_menu_bar()` | bool | 主菜单栏 |
| `imgui.end_main_menu_bar()` | - | |
| `imgui.begin_menu(label, enabled?)` | bool | 菜单 |
| `imgui.end_menu()` | - | |
| `imgui.menu_item(label, shortcut?, selected?)` | clicked | 菜单项 |

### 表格

| 函数 | 返回 | 说明 |
|------|------|------|
| `imgui.begin_table(id, cols, flags?)` | bool | 开始表格 |
| `imgui.end_table()` | - | 结束表格 |
| `imgui.table_setup_column(name, flags?, width?)` | - | 设置列 |
| `imgui.table_headers_row()` | - | 表头行 |
| `imgui.table_next_row(flags?, height?)` | - | 下一行 |
| `imgui.table_next_column()` | bool | 下一列 |
| `imgui.table_set_column_index(idx)` | bool | 跳转到指定列 |

### 弹窗与提示

| 函数 | 返回 | 说明 |
|------|------|------|
| `imgui.open_popup(id)` | - | 打开弹窗 |
| `imgui.begin_popup(id)` | bool | 开始弹窗 |
| `imgui.end_popup()` | - | 结束弹窗 |
| `imgui.close_popup()` | - | 关闭当前弹窗 |
| `imgui.begin_tooltip()` | - | 开始提示框 |
| `imgui.end_tooltip()` | - | 结束提示框 |
| `imgui.set_tooltip(text)` | - | 快速设置提示 |

### 样式 (临时修改)

| 函数 | 说明 |
|------|------|
| `imgui.push_style_color(idx, color)` | 压入颜色 (idx 为 `Col_*` 常量) |
| `imgui.pop_style_color(count?)` | 弹出颜色 |
| `imgui.push_style_var(idx, val [, val2])` | 压入样式变量 (idx 为 `StyleVar_*` 常量) |
| `imgui.pop_style_var(count?)` | 弹出样式变量 |

### 主题/皮肤

#### 样式属性 (rounding/padding/spacing 等)

| 函数 | 返回 | 说明 |
|------|------|------|
| `imgui.get_style()` | table | 获取当前完整样式属性表 |
| `imgui.set_style(table)` | - | 从表设置样式 (只更新表中存在的字段) |

**`get_style()` 返回表结构:**

| 字段 (float) | 对应 ImGuiStyle | 字段 (ImVec2 → {x,y}) | 对应 ImGuiStyle |
|------|------|------|------|
| `alpha` | Alpha | `window_padding` | WindowPadding |
| `disabled_alpha` | DisabledAlpha | `window_min_size` | WindowMinSize |
| `window_rounding` | WindowRounding | `window_title_align` | WindowTitleAlign |
| `window_border_size` | WindowBorderSize | `frame_padding` | FramePadding |
| `child_rounding` | ChildRounding | `item_spacing` | ItemSpacing |
| `child_border_size` | ChildBorderSize | `item_inner_spacing` | ItemInnerSpacing |
| `popup_rounding` | PopupRounding | `cell_padding` | CellPadding |
| `popup_border_size` | PopupBorderSize | `button_text_align` | ButtonTextAlign |
| `frame_rounding` | FrameRounding | `selectable_text_align` | SelectableTextAlign |
| `frame_border_size` | FrameBorderSize | `separator_text_align` | SeparatorTextAlign |
| `indent_spacing` | IndentSpacing | `separator_text_padding` | SeparatorTextPadding |
| `scrollbar_size` | ScrollbarSize | | |
| `scrollbar_rounding` | ScrollbarRounding | | |
| `grab_min_size` | GrabMinSize | | |
| `grab_rounding` | GrabRounding | | |
| `tab_rounding` | TabRounding | | |
| `tab_border_size` | TabBorderSize | | |
| `tab_bar_border_size` | TabBarBorderSize | | |
| `tab_bar_overline_size` | TabBarOverlineSize | | |
| `separator_text_border_size` | SeparatorTextBorderSize | | |
| `docking_separator_size` | DockingSeparatorSize | | |

另有 `window_menu_button_position` (int, `Dir_*` 常量)。

#### 颜色操作

| 函数 | 返回 | 说明 |
|------|------|------|
| `imgui.get_style_color(idx)` | r, g, b, a | 获取单个颜色 (0~1 浮点, idx 为 `Col_*` 常量) |
| `imgui.set_style_color(idx, r, g, b, a?)` | - | 设置单个颜色 (a 默认 1.0) |
| `imgui.get_style_colors()` | table | 获取全部颜色 `{[idx] = {r,g,b,a}}` |
| `imgui.set_style_colors(table)` | - | 批量设置颜色 `{[idx] = {r,g,b,a}}` |

#### 预设主题

| 函数 | 说明 |
|------|------|
| `imgui.style_colors_dark()` | 应用 ImGui 暗色主题 |
| `imgui.style_colors_light()` | 应用 ImGui 亮色主题 |
| `imgui.style_colors_classic()` | 应用 ImGui 经典主题 |

#### 颜色常量 (`Col_*`)

`Col_Text`, `Col_TextDisabled`, `Col_WindowBg`, `Col_ChildBg`, `Col_PopupBg`, `Col_Border`, `Col_BorderShadow`, `Col_FrameBg`, `Col_FrameBgHovered`, `Col_FrameBgActive`, `Col_TitleBg`, `Col_TitleBgActive`, `Col_TitleBgCollapsed`, `Col_MenuBarBg`, `Col_ScrollbarBg`, `Col_ScrollbarGrab`, `Col_ScrollbarGrabHovered`, `Col_ScrollbarGrabActive`, `Col_CheckMark`, `Col_SliderGrab`, `Col_SliderGrabActive`, `Col_Button`, `Col_ButtonHovered`, `Col_ButtonActive`, `Col_Header`, `Col_HeaderHovered`, `Col_HeaderActive`, `Col_Separator`, `Col_SeparatorHovered`, `Col_SeparatorActive`, `Col_ResizeGrip`, `Col_ResizeGripHovered`, `Col_ResizeGripActive`, `Col_Tab`, `Col_TabHovered`, `Col_TabSelected`, `Col_TabSelectedOverline`, `Col_TabDimmed`, `Col_TabDimmedSelected`, `Col_TabDimmedSelectedOverline`, `Col_DockingPreview`, `Col_DockingEmptyBg`, `Col_PlotLines`, `Col_PlotLinesHovered`, `Col_PlotHistogram`, `Col_PlotHistogramHovered`, `Col_TableHeaderBg`, `Col_TableBorderStrong`, `Col_TableBorderLight`, `Col_TableRowBg`, `Col_TableRowBgAlt`, `Col_TextLink`, `Col_TextSelectedBg`, `Col_DragDropTarget`, `Col_NavCursor`, `Col_NavWindowingHighlight`, `Col_NavWindowingDimBg`, `Col_ModalWindowDimBg`, `Col_COUNT`

#### 样式变量常量 (`StyleVar_*`)

`StyleVar_Alpha`, `StyleVar_DisabledAlpha`, `StyleVar_WindowPadding`, `StyleVar_WindowRounding`, `StyleVar_WindowBorderSize`, `StyleVar_WindowMinSize`, `StyleVar_WindowTitleAlign`, `StyleVar_ChildRounding`, `StyleVar_ChildBorderSize`, `StyleVar_PopupRounding`, `StyleVar_PopupBorderSize`, `StyleVar_FramePadding`, `StyleVar_FrameRounding`, `StyleVar_FrameBorderSize`, `StyleVar_ItemSpacing`, `StyleVar_ItemInnerSpacing`, `StyleVar_IndentSpacing`, `StyleVar_CellPadding`, `StyleVar_ScrollbarSize`, `StyleVar_ScrollbarRounding`, `StyleVar_GrabMinSize`, `StyleVar_GrabRounding`, `StyleVar_TabRounding`, `StyleVar_TabBorderSize`, `StyleVar_TabBarBorderSize`, `StyleVar_TabBarOverlineSize`, `StyleVar_ButtonTextAlign`, `StyleVar_SelectableTextAlign`, `StyleVar_SeparatorTextBorderSize`, `StyleVar_SeparatorTextAlign`, `StyleVar_SeparatorTextPadding`, `StyleVar_DockingSeparatorSize`

#### 主题示例

```lua
-- 1. 应用暗色基础 + 自定义强调色
imgui.style_colors_dark()
imgui.set_style_color(imgui.Col_Button, 0.2, 0.5, 0.8, 1.0)
imgui.set_style_color(imgui.Col_ButtonHovered, 0.3, 0.6, 0.9, 1.0)
imgui.set_style({ window_rounding = 8, frame_rounding = 4, grab_rounding = 4 })

-- 2. 批量设置颜色表
imgui.set_style_colors({
    [imgui.Col_WindowBg] = { r = 0.1, g = 0.1, b = 0.15, a = 1.0 },
    [imgui.Col_Button]   = { r = 0.2, g = 0.4, b = 0.6, a = 1.0 },
})

-- 3. 完整主题导出/导入
local saved_style  = imgui.get_style()
local saved_colors = imgui.get_style_colors()
-- ... 切换主题 / 修改后恢复 ...
imgui.set_style(saved_style)
imgui.set_style_colors(saved_colors)
```

### 输入状态

| 函数 | 返回 | 说明 |
|------|------|------|
| `imgui.is_mouse_clicked(btn?)` | bool | 鼠标按下 (0左/1右/2中) |
| `imgui.is_mouse_down(btn?)` | bool | 鼠标持续按下 |
| `imgui.is_mouse_double_clicked(btn?)` | bool | 鼠标双击 |
| `imgui.get_mouse_pos()` | x, y | 鼠标位置 |
| `imgui.is_key_pressed(key)` | bool | 按键按下 |
| `imgui.is_key_down(key)` | bool | 按键持续按下 |

### 项目状态

| 函数 | 返回 | 说明 |
|------|------|------|
| `imgui.is_item_hovered()` | bool | 上个控件被悬停 |
| `imgui.is_item_active()` | bool | 上个控件激活 |
| `imgui.is_item_clicked(btn?)` | bool | 上个控件被点击 |
| `imgui.is_item_focused()` | bool | 上个控件获得焦点 |
| `imgui.is_item_visible()` | bool | 上个控件可见 |
| `imgui.is_item_edited()` | bool | 上个控件被编辑 |
| `imgui.get_item_rect_min()` | x, y | 上个控件矩形左上角 |
| `imgui.get_item_rect_max()` | x, y | 上个控件矩形右下角 |
| `imgui.get_item_rect_size()` | w, h | 上个控件尺寸 |

### 窗口设置与状态

| 函数 | 返回 | 说明 |
|------|------|------|
| `imgui.set_next_window_pos(x, y, cond?)` | - | 设置下个窗口位置 |
| `imgui.set_next_window_size(w, h, cond?)` | - | 设置下个窗口大小 |
| `imgui.set_next_window_focus()` | - | 设置下个窗口获得焦点 |
| `imgui.set_next_window_bg_alpha(alpha)` | - | 设置下个窗口背景透明度 |
| `imgui.set_window_font_scale(scale)` | - | 设置窗口字体缩放 |
| `imgui.is_window_focused(flags?)` | bool | 窗口是否获得焦点 |
| `imgui.is_window_hovered(flags?)` | bool | 窗口是否被悬停 |
| `imgui.get_window_size()` | w, h | 窗口大小 |
| `imgui.get_window_pos()` | x, y | 窗口位置 |
| `imgui.get_screen_size()` | w, h | 屏幕尺寸 |
| `imgui.get_frame_count()` | int | 帧计数 |
| `imgui.get_delta_time()` | float | 帧间隔(秒) |

### ID与分组

| 函数 | 说明 |
|------|------|
| `imgui.push_id(id)` | 压入ID (字符串或整数) |
| `imgui.pop_id()` | 弹出ID |
| `imgui.begin_group()` | 开始分组 |
| `imgui.end_group()` | 结束分组 |
| `imgui.begin_disabled(disabled?)` | 开始禁用区域 |
| `imgui.end_disabled()` | 结束禁用区域 |
| `imgui.set_keybd_focus(offset?)` | 设置键盘焦点 |
| `imgui.set_item_default_focus()` | 设置默认焦点项 |

### 生命周期 (独立模式)

| 函数 | 说明 |
|------|------|
| `imgui.init(title?)` | 初始化 ImGui (Lua 脚本独立创建窗口时使用) |
| `imgui.shutdown()` | 关闭 |
| `imgui.begin_frame()` → bool | 开始帧 |
| `imgui.end_frame()` | 结束帧 |
| `imgui.run(callback)` | 主循环 (每帧调用 callback) |
| `imgui.is_initialized()` → bool | 是否已初始化 |
| `imgui.poll_events()` → bool | 处理系统事件 |

### 回调 (编辑器模式)

| 函数 | 说明 |
|------|------|
| `imgui.on_render(callback)` | 注册渲染回调 (编辑器模式, 每帧调用) |
| `imgui.clear_render_callback()` | 清除渲染回调 |
| `imgui.is_editor_mode()` → bool | 是否为编辑器模式 |

---

## driver - 驱动模块

> **进程白名单**: 所有涉及目标进程 PID 的驱动接口 (读写/注入/远程调用/进程保护) 均受 `auth_config.lua` 中 `process_names` 白名单限制。未配置白名单时不限制。

### 驱动加载

| 函数 | 返回 | 说明 |
|------|------|------|
| `driver.load(license)` | bool | 加载驱动 (需要卡密授权) |
| `driver.is_loaded()` | bool | 是否已加载 |

### 原始内存操作

| 函数 | 返回 | 说明 |
|------|------|------|
| `driver.read_memory(pid, addr, size)` | data/nil | 驱动级读内存 (原始字节) |
| `driver.write_memory(pid, addr, data)` | bool | 驱动级写内存 (原始字节) |

### 类型化内存读写

| 函数 | 返回 | 函数 | 返回 |
|------|------|------|------|
| `driver.read_u8(pid,addr)` | int/nil | `driver.write_u8(pid,addr,v)` | bool |
| `driver.read_u16(pid,addr)` | int/nil | `driver.write_u16(pid,addr,v)` | bool |
| `driver.read_u32(pid,addr)` | int/nil | `driver.write_u32(pid,addr,v)` | bool |
| `driver.read_u64(pid,addr)` | int/nil | `driver.write_u64(pid,addr,v)` | bool |
| `driver.read_float(pid,addr)` | num/nil | `driver.write_float(pid,addr,v)` | bool |
| `driver.read_double(pid,addr)` | num/nil | `driver.write_double(pid,addr,v)` | bool |

### 进程/模块操作

| 函数 | 返回 | 说明 |
|------|------|------|
| `driver.get_module(pid, name)` | table/nil | 模块信息 `{name, base, size}` |
| `driver.inject_module(pid, path)` | bool | 通过路径注入模块 (内部读取文件后注入) |
| `driver.inject_module_ex(pid, data)` | bool | 通过内存缓冲区注入模块 |
| `driver.protect_process(pid)` | bool | 保护进程 |

### 远程调用

| 函数 | 返回 | 说明 |
|------|------|------|
| `driver.init_call(pid, hwnd?)` | shared_buf | 初始化远程调用，返回共享缓冲区地址 |
| `driver.exec_call(pid, func, ...)` | ret, err | 执行远程调用 (最多16个u64参数) |
| `driver.init_call2(pid)` | deployee | 初始化远程调用，自动获取窗口 |
| `driver.exec_call2(pid, func, ...)` | ret, err | 执行远程调用 (最多16个u64参数) |

**远程调用说明：**

1. `init_call` / `exec_call`：需要提供窗口句柄，返回共享缓冲区基址
2. `init_call2` / `exec_call2`：自动获取窗口，无需手动传窗口句柄
3. 两种方式都支持最多 16 个 u64 参数（指针/整数均可）
4. 字符串参数需先 `write_memory` 写入目标进程内存，再把地址作为参数传入

```lua
-- 方式1: 远程调用
local shared_buf = driver.init_call(pid, hwnd)
if shared_buf then
    driver.write_memory(pid, shared_buf + 0x100, "Title\0")
    driver.write_memory(pid, shared_buf + 0x200, "Text\0")
    local ret = driver.exec_call(pid, remoteFuncAddr, hwnd, shared_buf + 0x200, shared_buf + 0x100, 0)
end

-- 方式2: 远程调用 (无需窗口句柄)
local deployee = driver.init_call2(pid)
if deployee then
    local ret = driver.exec_call2(pid, MessageBoxW, 0, text, title, 0x40)
end
```

### 驱动级鼠标

| 函数 | 返回 | 说明 |
|------|------|------|
| `driver.mouse_input(dx, dy, flags)` | bool | 原始鼠标输入 (底层) |
| `driver.mouse_move(dx, dy, abs?)` | bool | 移动鼠标 (abs 默认 true) |
| `driver.mouse_down(btn?)` | bool | 按下 `"left"/"right"/"middle"` |
| `driver.mouse_up(btn?)` | bool | 释放 |
| `driver.mouse_click(btn?)` | bool | 点击 (down + 100ms + up) |
| `driver.mouse_wheel(delta)` | bool | 滚轮 |

### 驱动级键盘

| 函数 | 返回 | 说明 |
|------|------|------|
| `driver.keybd_input(scan, down, up)` | bool | 原始键盘输入 (底层) |
| `driver.keybd_down(vk)` | bool | 按下按键 |
| `driver.keybd_up(vk)` | bool | 释放按键 |
| `driver.keybd_click(vk)` | bool | 点击 (down + up) |

```lua
-- 驱动模块示例
if driver.load("YOUR_LICENSE_KEY") then
    log.info("驱动加载成功")
    
    -- 驱动级类型化内存读写
    local pid = 12345  -- 直接使用 PID
    local base, size = proc.module(pid)
    
    local hp = driver.read_float(pid, base + 0x100)
    if hp then
        log.info("HP: " .. hp)
        driver.write_float(pid, base + 0x100, 999.0)
    end
    
    -- 驱动级原始内存读写
    local data = driver.read_memory(pid, base, 32)
    driver.write_memory(pid, base + 0x200, "\x90\x90\x90\x90")
    
    -- 获取模块信息
    local mod = driver.get_module(pid, "kernel32.dll")
    if mod then
        log.info(string.format("模块: %s @ 0x%X", mod.name, mod.base))
    end
    
    -- 驱动级输入 (绕过反作弊)
    driver.mouse_move(10, 10)       -- 相对移动
    driver.mouse_click("left")      -- 左键点击
    driver.keybd_click(0x41)         -- 按下 A 键
    driver.mouse_wheel(3)            -- 向上滚轮
else
    log.error("驱动加载失败")
end
```

---

## disasm - 反汇编模块

基于 Capstone 引擎的反汇编模块，支持 x86、x64、ARM、Thumb、ARM64 架构。

### 模块函数

| 函数 | 返回 | 说明 |
|------|------|------|
| `disasm.disassemble(bytes, addr [, arch [, max_count]])` | table \| nil, error | 一次性反汇编 |
| `disasm.open([arch])` | Disasm \| nil, error | 创建可复用的反汇编器句柄 |

- **bytes**: `string` (原始字节) 或 `table` (`{0x48, 0x89, ...}`)
- **addr**: 起始地址 (integer)
- **arch**: `"x86"`, `"x64"` (默认), `"arm"`, `"thumb"`, `"arm64"`
- **max_count**: 最大指令数 (0 = 不限制)

### 句柄对象方法

| 方法 | 返回 | 说明 |
|------|------|------|
| `d:disasm(bytes, addr [, max_count])` | table \| nil, error | 反汇编 |
| `d:arch()` | string | 当前架构名 |
| `d:is_open()` | boolean | 是否已初始化 |
| `d:close()` | - | 关闭句柄 (也由 `__gc` 自动释放) |

### 指令表字段

返回的每条指令包含以下字段:

| 字段 | 类型 | 说明 |
|------|------|------|
| `address` | int | 指令地址 |
| `size` | int | 指令长度 |
| `mnemonic` | string | 助记符 (`mov`, `call`, ...) |
| `operands` | string | 操作数文本 |
| `text` | string | 完整文本 (`"mov rax, rbx"`) |
| `bytes` | string | 原始字节 (二进制) |
| `hex` | string | 字节十六进制 (`"48 89 D8"`) |
| `is_call` | bool | 是否为 call |
| `is_jump` | bool | 是否为 jmp/jcc |
| `is_ret` | bool | 是否为 ret |
| `has_rip_rel` | bool | 是否含 RIP 相对寻址 |
| `imm` | int | 第一个立即数 |
| `disp` | int | 第一个内存位移 |
| `rip_disp` | int | RIP 相对位移 |
| `ops` | table | x86 操作数详情 (type/reg/imm/mem) |

```lua
-- 一次性反汇编: 从内存读取并反汇编
local pid = proc.find("target.exe")
local base = proc.module(pid)
local code = proc.read(pid, base, 64)  -- 读 64 字节
local insns, err = disasm.disassemble(code, base)
if insns then
    for _, insn in ipairs(insns) do
        log.info(string.format("%016X  %-12s %s", insn.address, insn.hex, insn.text))
    end
end

-- 复用句柄 (高性能, 避免重复初始化)
local d = disasm.open("x64")
local insns1 = d:disasm(code1, addr1)
local insns2 = d:disasm(code2, addr2, 10)  -- 最多 10 条
d:close()

-- 直接传入字节表
local insns = disasm.disassemble({0x48, 0x89, 0xD8, 0xC3}, 0x1000, "x64")
-- insns[1].text = "mov rax, rbx"
-- insns[2].text = "ret"

-- 查找 call 指令
for _, insn in ipairs(insns) do
    if insn.is_call then
        log.info(string.format("CALL at %X -> target imm: %X", insn.address, insn.imm))
    end
end
```

---

## encoding - 编码模块

窄字节字符串编码转换，基于 Windows CodePage 实现。支持 UTF-8、GBK、Big5、EUC-KR、Shift-JIS 等编码间互转。

### 通用转换

| 函数 | 返回 | 说明 |
|------|------|------|
| `encoding.convert(str, from, to)` | string \| nil, error | 通用编码转换 |
| `encoding.codepage(name)` | int \| nil | 查询编码名对应的 CodePage 数值 |

**支持的编码名**: `"utf8"`, `"gbk"`, `"gb2312"`, `"gb18030"`, `"big5"`, `"euckr"`, `"shiftjis"`, `"sjis"`, `"ascii"`, `"latin1"`, `"ansi"`, 或直接传入数字 codepage (如 `"936"`)

### 便捷函数

| 函数 | 返回 | 说明 |
|------|------|------|
| `encoding.utf8_to_gbk(str)` | string \| nil, error | UTF-8 → GBK (简体中文) |
| `encoding.gbk_to_utf8(str)` | string \| nil, error | GBK → UTF-8 |
| `encoding.utf8_to_big5(str)` | string \| nil, error | UTF-8 → Big5 (繁体中文) |
| `encoding.big5_to_utf8(str)` | string \| nil, error | Big5 → UTF-8 |
| `encoding.utf8_to_euckr(str)` | string \| nil, error | UTF-8 → EUC-KR (韩文) |
| `encoding.euckr_to_utf8(str)` | string \| nil, error | EUC-KR → UTF-8 |
| `encoding.utf8_to_shiftjis(str)` | string \| nil, error | UTF-8 → Shift-JIS (日文) |
| `encoding.shiftjis_to_utf8(str)` | string \| nil, error | Shift-JIS → UTF-8 |
| `encoding.utf8_to_ansi(str)` | string \| nil, error | UTF-8 → 系统默认 ANSI |
| `encoding.ansi_to_utf8(str)` | string \| nil, error | 系统默认 ANSI → UTF-8 |
| `encoding.utf8_to_local(str)` | string \| nil, error | UTF-8 → 系统本地编码 (同 utf8_to_ansi，命名更明确) |
| `encoding.local_to_utf8(str)` | string \| nil, error | 系统本地编码 → UTF-8 (同 ansi_to_utf8) |
| `encoding.adaptive_to_local(str)` | string, type | 自动检测 UTF-8/ANSI 并转本地编码，type=`"utf8"`/`"ansi"`/`"ascii"`/`"empty"` |

```lua
-- 通用转换: GBK 简体 → Big5 繁体
local big5_str = encoding.convert(gbk_str, "gbk", "big5")

-- 便捷函数: UTF-8 → GBK
local gbk = encoding.utf8_to_gbk("你好世界")

-- 读取 GBK 编码文件并转为 UTF-8
local f = io.open("gbk_file.txt", "rb")
local raw = f:read("*a")
f:close()
local utf8_text = encoding.gbk_to_utf8(raw)
log.info(utf8_text)

-- 错误处理
local result, err = encoding.convert(str, "utf8", "unknown")
if not result then
    log.error("转换失败: " .. err)
end

-- 查询 codepage 数值
local cp = encoding.codepage("gbk")  -- 936
```

---

## 完整 API 清单

**23 个核心模块** (共 293+ 个函数 + imgui/ffi):

| 模块 | 函数数 | 主要功能 |
|------|--------|----------|
| `sys` | 32 | 系统信息、环境、剪贴板、PE加载、代码页 |
| `log` | 6 | 日志输出 (trace/debug/info/warn/error/print) |
| `proc` | 37 | 进程管理、内存读写、AOB扫描、地址表达式 |
| `task` | 17 | 多任务管理、暂停/恢复/停止 |
| `keybd` | 12 | 键盘输入 (前台/后台) |
| `mouse` | 23 | 鼠标输入 (前台/后台/轨迹/系统设置) |
| `wnd` | 28 | 窗口查找、操作、消息 |
| `vision` | 19 | 截图、找图、找色、图像处理 |
| `ocr` | 4 | 中文文字识别 (NCNN) |
| `http` | 6 | HTTP请求、下载、URL编码 |
| `auth` | 2+8 | 网络验证 (2模块函数 + 8对象方法) |
| `crypto` | 15 | 哈希、编码、加密 |
| `hotkey` | 6 | 全局热键监听 (纯轮询) |
| `driver` | 32 | 驱动级读写、类型化读写、注入、远程调用、鼠标/键盘输入 |
| `trajectory` | 4 | 拟人鼠标轨迹生成 |
| `resource` | 9 | 资源下载/上传/查询/更新检查/ZIP压缩解压 |
| `config` | 10 | JSON配置文件读写 |
| `disasm` | 2+4 | 反汇编 (Capstone, 2模块函数 + 4对象方法) |
| `encoding` | 15 | 字符串编码转换 (UTF-8/GBK/Big5/EUC-KR/Shift-JIS/Local/Adaptive) |
| `path` | 2 | 路点地图寻路 (A* + wmap + maxRange) |
| `asm` | 4 | JIT汇编编译 |
| `ffi` | ∞ | cffi-lua 外部函数接口 |
| `imgui` | ∞ | ImGui 界面绑定 |

---

*文档已基于 LuaApi_*.cpp 注册表完整对齐 (2026-03-28)*
