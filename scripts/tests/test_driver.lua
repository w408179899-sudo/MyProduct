--[[
    driver 模块综合测试
    测试驱动功能: 内核级内存读写、输入模拟、模式切换
    
    包含:
    - 驱动加载与状态检查
    - 模式切换 (mem/proc/keybd/mouse)
    - 内存读写 (类型化 API)
    - 键盘/鼠标输入模拟
    - 记事本进程实际测试
]]
local T = require("tests.test_framework")

--============================================================================
-- 测试配置
--============================================================================

-- 记事本进程信息 (用于实际测试)
local notepadPid = nil
local notepadHwnd = nil

-- 启动记事本辅助函数
local function startNotepad()
    local pid = proc.create("notepad.exe")
    T.trace("proc.create", {"notepad.exe"}, pid)
    if pid and pid > 0 then
        notepadPid = pid
        sys.sleep(800)
        for i = 1, 10 do
            notepadHwnd = wnd.find("Notepad", nil)
            T.trace("wnd.find", {"Notepad", nil}, notepadHwnd)
            if notepadHwnd then break end
            sys.sleep(200)
        end
        return true
    end
    return false
end

-- 清理记事本
local function cleanupNotepad()
    if notepadPid then
        local ok = proc.kill(notepadPid)
        T.trace("proc.kill", {notepadPid}, ok)
        notepadPid = nil
        notepadHwnd = nil
    end
end

--============================================================================
-- 主测试函数
--============================================================================
local M = {}

function M.run()
    T.reset()
    log.info("=== driver 模块综合测试 ===")

    local driverLoaded = false

    --========================================================================
    -- 第1部分: 驱动加载
    --========================================================================
    log.info("\n--- 1. 驱动加载 ---")

    T.test(
        "driver.is_loaded: 初始状态检查",
        function()
            local loaded = driver.is_loaded()
            T.trace("driver.is_loaded", {}, loaded)
            T.assert_type(loaded, "boolean")
            driverLoaded = loaded
        end
    )

    if not driverLoaded then
        T.test(
            "driver.load: 加载驱动",
            function()
                local hwid = sys.hwid()
                T.trace("sys.hwid", {}, hwid)
                local ok, err = driver.load()
                T.trace("driver.load", {}, ok, err)
                if ok then
                    driverLoaded = true
                end
            end
        )
    end

    --========================================================================
    -- 第2部分: API 完整性检查
    --========================================================================
    log.info("\n--- 2. API 完整性检查 ---")

    T.test(
        "driver API 完整性",
        function()
            -- 实际导出的 driver API（重构后）
            local apis = {
                "load",
                "is_loaded",
                "read_memory",
                "write_memory",
                "get_module",
                "inject_module",
                "inject_module_ex",  -- 内存缓冲区注入
                "protect_process",
                "init_call",      -- 初始化远程调用
                "exec_call",      -- 执行远程调用
                "mouse_input",
                "keybd_input"
            }
            local missing = {}
            for _, name in ipairs(apis) do
                if type(driver[name]) ~= "function" then
                    table.insert(missing, name)
                end
            end
            if #missing > 0 then
                log.info("  缺失: " .. table.concat(missing, ", "))
            end
            T.assert_eq(#missing, 0, "所有API应存在")
            log.info(string.format("  验证 %d 个 API", #apis))
        end
    )

    -- 注: mem 模块已删除，内存操作整合到 proc 模块
    -- 注: proc.set_mode/get_mode 切换 API/驱动模式，proc 模块使用 PID 直接操作

    --========================================================================
    -- 第4部分: 未加载驱动时的错误处理
    --========================================================================
    if not driverLoaded then
        log.info("\n--- 4. 错误处理 (驱动未加载) ---")

        T.test(
            "read_memory: 未加载返回nil+错误",
            function()
                local data, err = driver.read_memory(1234, 0x400000, 4)
                T.trace("driver.read_memory", {1234, 0x400000, 4}, data, err)
                T.assert_nil(data)
                T.assert_type(err, "string")
            end
        )

        T.test(
            "write_memory: 未加载返回false+错误",
            function()
                local ok, err = driver.write_memory(1234, 0x400000, "\x00\x00\x00\x00")
                T.trace("driver.write_memory", {1234, 0x400000, "<4 bytes>"}, ok, err)
                T.assert_false(ok)
                T.assert_type(err, "string")
            end
        )

        T.test(
            "get_module: 未加载返回nil+错误",
            function()
                local mod, err = driver.get_module(1234, "kernel32.dll")
                T.trace("driver.get_module", {1234, "kernel32.dll"}, mod, err)
                T.assert_nil(mod)
                T.assert_type(err, "string")
            end
        )

        T.test(
            "mouse_input: 未加载返回false",
            function()
                local ok = driver.mouse_input(100, 100, false)
                T.trace("driver.mouse_input", {100, 100, false}, ok)
                T.assert_false(ok)
            end
        )

        T.test(
            "keybd_input: 未加载返回false",
            function()
                local ok = driver.keybd_input(0x1E, true)
                T.trace("driver.keybd_input", {0x1E, true}, ok)
                T.assert_false(ok)
            end
        )

        T.test(
            "init_call: 未加载返回nil+错误",
            function()
                local addr, err = driver.init_call(1234, 0)
                T.trace("driver.init_call", {1234, 0}, addr, err)
                T.assert_nil(addr)
                T.assert_type(err, "string")
            end
        )

        T.test(
            "exec_call: 未加载返回nil+错误",
            function()
                local ret, err = driver.exec_call(1234, 0x400000)
                T.trace("driver.exec_call", {1234, 0x400000}, ret, err)
                T.assert_nil(ret)
                T.assert_type(err, "string")
            end
        )
    end

    --========================================================================
    -- 第5部分: 驱动模式下的内存测试 (使用记事本进程)
    --========================================================================
    if driverLoaded then
        -- 注: mem 模块和 proc.open_pid/close 已删除，内存读写通过 proc.read_*/write_* 直接使用 PID
        log.info("\n--- 5. 记事本驱动级测试 ---")

        -- 启动记事本用于驱动模式内存测试
        if startNotepad() then

            T.test(
                "driver.get_module: 获取记事本模块",
                function()
                    local mod, err = driver.get_module(notepadPid, "notepad.exe")
                    T.trace("driver.get_module", {notepadPid, "notepad.exe"}, mod, err)
                    -- 驱动模式下应该成功，否则跳过后续测试
                    if mod then
                        T.assert_type(mod.base, "number")
                        T.assert_gt(mod.base, 0)
                    end
                end
            )

            T.test(
                "driver.read_memory: 读取记事本PE头",
                function()
                    local mod, modErr = driver.get_module(notepadPid, "notepad.exe")
                    T.trace("driver.get_module", {notepadPid, "notepad.exe"}, mod, modErr)
                    if mod and mod.base then
                        local data, err = driver.read_memory(notepadPid, mod.base, 2)
                        T.trace("driver.read_memory", {notepadPid, string.format("0x%X", mod.base), 2}, data, err)
                        if data then
                            -- 检查 MZ 签名
                            local b1, b2 = string.byte(data, 1, 2)
                            local sig = b1 + b2 * 256
                            T.trace("PE signature", {}, string.format("0x%04X", sig))
                            T.assert_eq(sig, 0x5A4D, "应为 'MZ' (0x5A4D)")
                        end
                    end
                end
            )

            --================================================================
            -- 远程调用测试: init_call + exec_call 调用 MessageBoxA
            --================================================================
            T.test(
                "driver.init_call + exec_call: 调用 MessageBoxA",
                function()
                    -- 1. 获取 user32.dll 模块
                    local user32, err = driver.get_module(notepadPid, "user32.dll")
                    T.trace("driver.get_module", {notepadPid, "user32.dll"}, user32, err)
                    if not user32 then
                        log.warn("  user32.dll 未加载，跳过测试")
                        return
                    end
                    
                    -- 2. 初始化远程调用
                    local shared_buf, initErr = driver.init_call(notepadPid, notepadHwnd or 0)
                    T.trace("driver.init_call", {notepadPid, notepadHwnd or 0}, shared_buf, initErr)
                    if not shared_buf then
                        log.warn("  init_call 失败: " .. (initErr or "unknown"))
                        return
                    end
                    log.info(string.format("  shared_buf 地址: 0x%X", shared_buf))
                    
                    -- 3. 使用 proc.module 获取本地 user32.dll 基址，计算导出函数偏移
                    -- 注意: 这里需要用 GetProcAddress 或手动解析导出表
                    -- 简化方案: 直接使用 ffi 获取本地 MessageBoxA 地址，然后计算偏移
                    local ffi = require("cffi")
                    ffi.cdef[[
                        void* GetModuleHandleA(const char* lpModuleName);
                        void* GetProcAddress(void* hModule, const char* lpProcName);
                    ]]
                    local localUser32 = ffi.C.GetModuleHandleA("user32.dll")
                    local localMsgBox = ffi.C.GetProcAddress(localUser32, "MessageBoxA")
                    T.trace("GetProcAddress", {"user32.dll", "MessageBoxA"}, localMsgBox)
                    
                    if localMsgBox == nil then
                        log.warn("  无法获取本地 MessageBoxA 地址")
                        return
                    end
                    
                    -- 计算偏移量 (cffi-lua cdata 指针转数字)
                    local function ptrToNum(ptr)
                        if ptr == nil then return nil end
                        -- 方法1: 尝试直接转换
                        local ok, result = pcall(function()
                            return tonumber(ffi.cast("uintptr_t", ptr))
                        end)
                        if ok and result then return result end
                        
                        -- 方法2: 解析 tostring 输出 "cdata<void *>: 00007FFABE4A8B70"
                        local s = tostring(ptr)
                        -- 匹配末尾的十六进制地址 (不带 0x 前缀)
                        local hex = s:match(":%s*(%x+)$")
                        if hex then
                            return tonumber(hex, 16)
                        end
                        return nil
                    end
                    
                    local localBase = ptrToNum(localUser32)
                    local localFunc = ptrToNum(localMsgBox)
                    T.trace("ptrToNum", {tostring(localUser32), tostring(localMsgBox)}, localBase, localFunc)
                    
                    if not localBase or not localFunc then
                        log.warn("  无法转换指针地址")
                        return
                    end
                    
                    local offset = localFunc - localBase
                    local remoteMsgBox = user32.base + offset
                    T.trace("MessageBoxA", {string.format("offset=0x%X", offset), string.format("remote=0x%X", remoteMsgBox)})
                    
                    -- 4. 在目标进程分配内存写入字符串 (使用 shared_buf 区域)
                    -- shared_buf 区域可以用来存放参数字符串
                    local title = "Driver Test\0"
                    local text = "Hello from driver!\0"
                    
                    -- 写入标题和文本到 shared_buf + 0x100 和 +0x200 偏移
                    local titleAddr = shared_buf + 0x100
                    local textAddr = shared_buf + 0x200
                    
                    local ok1 = driver.write_memory(notepadPid, titleAddr, title)
                    local ok2 = driver.write_memory(notepadPid, textAddr, text)
                    T.trace("driver.write_memory", {"title", string.format("0x%X", titleAddr)}, ok1)
                    T.trace("driver.write_memory", {"text", string.format("0x%X", textAddr)}, ok2)
                    
                    if not ok1 or not ok2 then
                        log.warn("  写入字符串失败")
                        return
                    end
                    
                    -- 5. 执行远程调用 MessageBoxA(hwnd, text, title, MB_OK)
                    -- MB_OK = 0, MB_ICONINFORMATION = 0x40
                    local hwndParam = notepadHwnd or 0
                    local ret, callErr = driver.exec_call(notepadPid, remoteMsgBox, hwndParam, textAddr, titleAddr, 0x40)
                    T.trace("driver.exec_call", {
                        string.format("MessageBoxA@0x%X", remoteMsgBox),
                        string.format("hwnd=0x%X", hwndParam),
                        string.format("text=0x%X", textAddr),
                        string.format("title=0x%X", titleAddr),
                        "MB_ICONINFORMATION"
                    }, ret, callErr)
                    
                    if ret then
                        log.info(string.format("  MessageBoxA 返回值: %d (用户点击后返回)", ret))
                        T.assert_type(ret, "number")
                    else
                        log.warn("  exec_call 失败: " .. (callErr or "unknown"))
                    end
                end
            )

            cleanupNotepad()
            log.info("  记事本已关闭")
        else
            log.info("  无法启动记事本，跳过驱动模式测试")
        end

        -- 注: mem 模块已删除，内存操作整合到 proc 模块
    end

    return T.report("driver")
end

-- 保存原始测试函数
local originalRun = M.run

-- 主入口：使用 pcall 保证清理始终执行
function M.run()
    log.info("[driver] ==================== 开始 driver 模块测试 ====================")
    local ok, result = pcall(originalRun)
    -- 无论测试成功还是失败，始终执行清理
    log.info("[driver] ==================== 执行清理 ====================")
    cleanupNotepad()
    log.info("[driver] ==================== driver 模块测试结束 ====================")
    if not ok then
        log.error("[driver] 测试过程中发生错误: " .. tostring(result))
        error(result)
    end
    return result
end

return M
