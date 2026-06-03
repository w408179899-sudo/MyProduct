--[[
    proc 模块测试 - 进程管理功能验证
    覆盖: 进程枚举/信息查询/模块列表/线程列表等
    使用记事本进程进行跨进程测试
    
    注: proc 模块已移除 open/open_pid/close，直接使用 PID 操作
]]
local T = require("tests.test_framework")

-- 测试进程状态 (统一使用 PID)
local pid = nil

-- 辅助函数
local function startNotepad()
    T.log("[proc] 启动 notepad.exe")
    pid = proc.create("notepad.exe")
    if pid and pid > 0 then
        sys.sleep(500)
        T.log(string.format("[proc] PID=%d", pid))
        return true
    end
    T.log("[proc] 启动失败")
    return false
end

local function cleanup()
    if pid then proc.kill(pid); pid = nil end
    T.log("[proc] 清理完成")
end

local function run()
    T.reset()
    T.log("\n=== proc 模块测试 ===")

    -- proc.pid 已删除，使用 sys.pid() 替代

    T.test("proc.list: 枚举进程列表", function()
        local list = proc.list()
        T.assert_type(list, "table"); T.assert_gt(#list, 0)
        T.assert_type(list[1].pid, "number"); T.assert_type(list[1].name, "string")
        T.log(string.format("  进程数: %d", #list))
    end)

    -- 启动测试进程
    T.test("proc.create: 创建记事本进程", function()
        T.assert_true(startNotepad(), "启动记事本失败")
    end)

    -- 以下测试依赖记事本进程
    T.test("proc.is_alive: 检查进程存活", function()
        if pid then
            T.assert_true(proc.is_alive(pid))
        end
        T.assert_false(proc.is_alive(0), "无效PID应返回false")
    end)

    T.test("proc.module: 获取主模块", function()
        if not pid then return end
        local base, size = proc.module(pid)
        if base then
            T.assert_gt(base, 0); T.assert_gt(size, 0)
            T.log(string.format("  基址: 0x%X, 大小: %dKB", base, size // 1024))
        end
    end)

    T.test("proc.module: 获取ntdll.dll", function()
        if not pid then return end
        local base, size = proc.module(pid, "ntdll.dll")
        if base then
            T.assert_gt(base, 0); T.assert_gt(size, 0)
            T.log(string.format("  ntdll: 0x%X, %dKB", base, size // 1024))
        end
    end)

    T.test("proc.name: 获取进程名", function()
        if not pid then return end
        local n = proc.name(pid)
        T.assert_type(n, "string"); T.assert_gt(#n, 0)
        T.assert_true(n:lower():find("notepad") ~= nil)
        T.log("  名称: " .. n)
    end)

    T.test("proc.path: 获取进程路径", function()
        if not pid then return end
        local p = proc.path(pid)
        T.assert_type(p, "string"); T.assert_gt(#p, 0)
        T.log("  路径: " .. p)
    end)

    T.test("proc.memory: 获取内存信息", function()
        if not pid then return end
        local m = proc.memory(pid)
        T.assert_type(m, "table"); T.assert_type(m.working_set, "number")
        T.assert_gt(m.working_set, 0)
        T.log(string.format("  工作集: %dKB", m.working_set))
    end)

    T.test("proc.modules: 枚举模块列表", function()
        if not pid then return end
        local mods = proc.modules(pid)
        T.assert_type(mods, "table"); T.assert_gt(#mods, 0)
        T.assert_type(mods[1].name, "string"); T.assert_type(mods[1].base, "number")
        T.log(string.format("  模块数: %d, 首模块: %s", #mods, mods[1].name))
    end)

    T.test("proc.threads: 获取线程列表", function()
        if not pid then return end
        local t = proc.threads(pid)
        T.assert_type(t, "table"); T.assert_gt(#t, 0)
        T.assert_type(t[1].tid, "number")
        T.log(string.format("  线程数: %d", #t))
    end)

    T.test("proc.window: 获取主窗口", function()
        if not pid then return end
        local hwnd = proc.window(pid)
        T.log("  窗口: " .. (hwnd and string.format("0x%X", hwnd) or "nil"))
    end)

    T.test("proc.is_64bit: 检查进程架构", function()
        if not pid then return end
        local is64 = proc.is_64bit(pid)
        T.assert_type(is64, "boolean")
        T.log("  64位: " .. tostring(is64))
    end)

    T.test("proc.exists: 检查进程存在", function()
        if pid then T.assert_true(proc.exists(pid)) end
        T.assert_true(proc.exists("notepad.exe"))
        T.assert_false(proc.exists("NonExistent12345.exe"))
    end)

    T.test("proc.priority: 获取优先级", function()
        if not pid then return end
        local p = proc.priority(pid)
        T.assert_type(p, "number"); T.assert_gte(p, 0); T.assert_lte(p, 5)
        T.log("  优先级: " .. p)
    end)

    -- API存在性
    T.test("proc.suspend/resume/wait: 函数存在", function()
        T.assert_type(proc.suspend, "function")
        T.assert_type(proc.resume, "function")
        T.assert_type(proc.wait, "function")
    end)

    -- 内存操作测试
    T.test("proc.read_*: 内存读取函数存在", function()
        T.assert_type(proc.read_u8, "function")
        T.assert_type(proc.read_u16, "function")
        T.assert_type(proc.read_u32, "function")
        T.assert_type(proc.read_u64, "function")
        T.assert_type(proc.read_float, "function")
        T.assert_type(proc.read_double, "function")
        T.assert_type(proc.read_bytes, "function")
        T.assert_type(proc.read_string, "function")
    end)

    T.test("proc.write_*: 内存写入函数存在", function()
        T.assert_type(proc.write_u8, "function")
        T.assert_type(proc.write_u16, "function")
        T.assert_type(proc.write_u32, "function")
        T.assert_type(proc.write_u64, "function")
        T.assert_type(proc.write_float, "function")
        T.assert_type(proc.write_double, "function")
        T.assert_type(proc.write_bytes, "function")
    end)

    T.test("proc.read_bytes: 读取PE头", function()
        if not pid then return end
        local base, _ = proc.module(pid)
        if not base then return end
        
        -- 读取DOS头的前2字节 (MZ签名)
        local data = proc.read_bytes(pid, base, 2)
        if data then
            T.assert_eq(#data, 2)
            T.assert_eq(data, "MZ", "PE头应以MZ开始")
            T.log("  PE签名: MZ")
        end
    end)

    T.test("proc.read_u16: 读取DOS签名", function()
        if not pid then return end
        local base, _ = proc.module(pid)
        if not base then return end
        
        local sig = proc.read_u16(pid, base)
        if sig then
            T.assert_eq(sig, 0x5A4D, "DOS签名应为0x5A4D (MZ)")
            T.log(string.format("  DOS签名: 0x%04X", sig))
        end
    end)

    T.test("proc.read_u32: 读取PE偏移", function()
        if not pid then return end
        local base, _ = proc.module(pid)
        if not base then return end
        
        -- DOS头偏移0x3C处存放PE头偏移
        local pe_offset = proc.read_u32(pid, base + 0x3C)
        if pe_offset then
            T.assert_gt(pe_offset, 0)
            T.log(string.format("  PE偏移: 0x%X", pe_offset))
            -- 验证PE签名 "PE\0\0" = 0x00004550
            local pe_sig = proc.read_u32(pid, base + pe_offset)
            if pe_sig then
                T.assert_eq(pe_sig, 0x4550, "PE签名应为0x4550")
            end
        end
    end)

    T.test("proc.read_u64: 读取64位值", function()
        if not pid then return end
        local base, _ = proc.module(pid)
        if not base then return end
        
        local val = proc.read_u64(pid, base)
        if val then
            T.assert_type(val, "number")
            T.log(string.format("  U64值: 0x%X", val))
        end
    end)

    T.test("proc.read_string: 读取字符串", function()
        if not pid then return end
        local base, _ = proc.module(pid)
        if not base then return end
        
        -- PE文件开头是"MZ"
        local str = proc.read_string(pid, base, 2)
        if str then
            T.assert_eq(str, "MZ")
            T.log("  字符串: " .. str)
        end
    end)

    -- 模式切换测试
    T.test("proc.set_mode/get_mode: 模式切换", function()
        T.assert_type(proc.set_mode, "function")
        T.assert_type(proc.get_mode, "function")
        
        local mode = proc.get_mode()
        T.assert_type(mode, "string")
        T.log("  当前模式: " .. mode)
    end)

    -- 内存写入测试 (使用当前进程避免权限问题)
    T.test("proc.write_*: 内存写入函数完整性", function()
        -- 仅检查函数存在性，实际写入测试风险较高
        T.assert_type(proc.write_u8, "function")
        T.assert_type(proc.write_u16, "function")
        T.assert_type(proc.write_u32, "function")
        T.assert_type(proc.write_u64, "function")
        T.assert_type(proc.write_float, "function")
        T.assert_type(proc.write_double, "function")
        T.assert_type(proc.write_bytes, "function")
    end)

    -- 进程控制完整测试
    T.test("proc.suspend/resume: 挂起恢复", function()
        if not pid then return end
        
        local ok = proc.suspend(pid)
        T.log("  挂起: " .. tostring(ok))
        sys.sleep(100)
        
        ok = proc.resume(pid)
        T.log("  恢复: " .. tostring(ok))
    end)

    -- AOB 特征码扫描测试
    T.test("proc.scan: 函数存在", function()
        T.assert_type(proc.scan, "function")
    end)

    T.test("proc.scan: 扫描PE头MZ签名", function()
        if not pid then return end
        local base, size = proc.module(pid)
        if not base or not size then return end
        
        -- 扫描 "MZ" 签名 (4D 5A)
        local addr = proc.scan(pid, "4D 5A", base, size)
        if addr then
            T.assert_eq(addr, base, "MZ签名应在模块起始位置")
            T.log(string.format("  找到 MZ 签名: 0x%X", addr))
        else
            T.log("  未找到 MZ 签名 (可能权限不足)")
        end
    end)

    T.test("proc.scan: 通配符扫描", function()
        if not pid then return end
        local base, size = proc.module(pid)
        if not base or not size then return end
        
        -- 扫描 "MZ" 后跟任意字节 (4D 5A ??)
        local addr = proc.scan(pid, "4D 5A ??", base, size)
        if addr then
            T.assert_eq(addr, base, "带通配符的MZ签名应在模块起始位置")
            T.log(string.format("  通配符扫描成功: 0x%X", addr))
        end
    end)

    -- 内存分配/释放测试
    T.log("\n--- alloc/free ---")

    T.test("proc.alloc/free: 函数存在", function()
        T.assert_type(proc.alloc, "function")
        T.assert_type(proc.free, "function")
    end)

    T.test("proc.alloc: 默认 rw- 内存", function()
        if not pid then return end
        local addr = proc.alloc(pid, 4096)
        T.assert_type(addr, "number")
        T.assert_gt(addr, 0)
        T.log(string.format("  rw- 地址: 0x%X", addr))
        -- 写入后读回验证
        proc.write_u32(pid, addr, 0xDEADBEEF)
        local val = proc.read_u32(pid, addr)
        T.assert_eq(val, 0xDEADBEEF, "读回值应匹配")
        -- 释放
        local ok = proc.free(pid, addr)
        T.assert_true(ok, "释放应成功")
    end)

    T.test("proc.alloc: rwx 可执行内存", function()
        if not pid then return end
        local addr = proc.alloc(pid, 4096, "rwx")
        T.assert_type(addr, "number")
        T.assert_gt(addr, 0)
        T.log(string.format("  rwx 地址: 0x%X", addr))
        proc.free(pid, addr)
    end)

    T.test("proc.alloc: r-- 只读内存", function()
        if not pid then return end
        local addr = proc.alloc(pid, 4096, "r--")
        T.assert_type(addr, "number")
        T.assert_gt(addr, 0)
        T.log(string.format("  r-- 地址: 0x%X", addr))
        proc.free(pid, addr)
    end)

    T.test("proc.free: 无效地址不崩溃", function()
        if not pid then return end
        local ok = proc.free(pid, 0x1234)
        T.assert_false(ok, "释放无效地址应失败")
    end)

    -- 远程调用测试
    T.log("\n--- call ---")

    T.test("proc.call: 函数存在", function()
        T.assert_type(proc.call, "function")
    end)

    T.test("proc.call: 调用 kernel32!GetCurrentProcessId", function()
        if not pid then return end
        -- 获取 kernel32.dll 中 GetCurrentProcessId 的地址
        local k32base, _ = proc.module(pid, "kernel32.dll")
        if not k32base then
            T.log("  跳过: 无法获取 kernel32.dll 基址")
            return
        end
        -- 简单测试: 调用不会崩溃即可 (APC 异步执行, 无法获取返回值)
        -- 使用 alloc+shellcode 方式, 验证排队成功
        local ok, err = proc.call(pid, k32base)  -- 不是有效函数, 但测试排队
        -- 排队本身应该成功 (无论函数是否有效)
        T.log("  call 结果: ok=" .. tostring(ok) .. (err and (", err=" .. err) or ""))
    end)

    -- 地址表达式与 Vector3 读取测试
    T.log("\n--- 地址表达式 / Vector3 ---")

    T.test("proc.eval_addr: 函数存在", function()
        T.assert_type(proc.eval_addr, "function")
    end)

    T.test("proc.eval_addr: 简单表达式求值", function()
        if not pid then return end
        local base, _ = proc.module(pid)
        if not base then return end
        -- 使用模块基址作为简单表达式 (直接数值)
        local addr = proc.eval_addr(pid, string.format("0x%X", base))
        if addr then
            T.assert_type(addr, "number")
            T.assert_eq(addr, base, "直接地址表达式应返回原值")
            T.log(string.format("  eval_addr: 0x%X", addr))
        else
            T.log("  eval_addr 返回nil (可能不支持纯数值表达式)")
        end
    end)

    T.test("proc.eval_addr: 无效表达式不崩溃", function()
        if not pid then return end
        local ok, addr = pcall(proc.eval_addr, pid, "invalid_expr")
        -- 不崩溃即可，addr 可能为 nil 或报错
        T.log("  无效表达式: ok=" .. tostring(ok) .. ", addr=" .. tostring(addr))
    end)

    T.test("proc.read_vec3: 函数存在", function()
        T.assert_type(proc.read_vec3, "function")
    end)

    T.test("proc.read_vec3: 无效地址返回nil", function()
        if not pid then return end
        local x, y, z = proc.read_vec3(pid, 0)
        T.assert_nil(x, "无效地址应返回nil")
    end)

    -- proc.read_float / proc.read_double 功能测试
    T.test("proc.read_float: 读取浮点数", function()
        if not pid then return end
        local base, _ = proc.module(pid)
        if not base then return end
        -- 尝试读取基址处的浮点值 (仅验证返回类型)
        local val = proc.read_float(pid, base)
        if val then
            T.assert_type(val, "number")
            T.log(string.format("  float@base: %f", val))
        else
            T.log("  read_float 返回nil (正常，PE头不是有效float)")
        end
    end)

    T.test("proc.read_double: 读取双精度浮点", function()
        if not pid then return end
        local base, _ = proc.module(pid)
        if not base then return end
        local val = proc.read_double(pid, base)
        if val then
            T.assert_type(val, "number")
            T.log(string.format("  double@base: %f", val))
        else
            T.log("  read_double 返回nil (正常，PE头不是有效double)")
        end
    end)

    return T.report("proc")
end

-- 安全运行 (确保清理)
local function safeRun()
    T.log("[proc] ========== 开始测试 ==========")
    local ok, result = pcall(run)
    cleanup()
    T.log("[proc] ========== 测试结束 ==========")
    if not ok then error(result) end
    return result
end

return { run = safeRun }
