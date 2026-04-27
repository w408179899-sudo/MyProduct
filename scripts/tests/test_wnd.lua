--[[
    wnd 模块测试 - 窗口管理功能验证
    覆盖: 窗口查找/信息获取/状态查询/操作/消息等
    使用记事本进程进行窗口操作测试
    
    API 列表 (来自 LuaExports.cpp):
    查找:
    - wnd.find(class, title)              精确匹配查找
    - wnd.find_ex(class, title)           模糊匹配查找
    - wnd.find_by_pid(pid, class?, title?) 按PID查找
    - wnd.get_foreground()                获取前台窗口
    
    信息:
    - wnd.get_pid(hwnd)                   获取窗口进程ID
    - wnd.get_tid(hwnd)                   获取窗口线程ID
    - wnd.get_title(hwnd)                 获取窗口标题
    - wnd.set_title(hwnd, title)          设置窗口标题
    - wnd.class_name(hwnd)                获取窗口类名
    - wnd.wnd_rect(hwnd)                  获取窗口矩形
    - wnd.client_rect(hwnd)               获取客户区矩形
    
    位置/大小:
    - wnd.set_pos(hwnd, x, y)             设置窗口位置
    - wnd.set_size(hwnd, w, h)            设置窗口大小
    - wnd.move(hwnd, x, y, w, h)          移动并调整大小
    
    显示状态:
    - wnd.show(hwnd, cmd?)                显示/隐藏窗口
    - wnd.minimize(hwnd)                  最小化窗口
    - wnd.maximize(hwnd)                  最大化窗口
    - wnd.restore(hwnd)                   还原窗口
    - wnd.close(hwnd)                     关闭窗口
    - wnd.set_foreground(hwnd)            设置前台窗口
    - wnd.set_topmost(hwnd, topmost)      设置/取消置顶
    - wnd.enable(hwnd, enable)            启用/禁用窗口
    
    状态检查:
    - wnd.is_visible(hwnd)                检查是否可见
    - wnd.is_minimized(hwnd)              检查是否最小化
    - wnd.is_maximized(hwnd)              检查是否最大化
    - wnd.is_enabled(hwnd)                检查是否启用
    
    消息:
    - wnd.send_message(hwnd, msg, wp, lp) 同步发送消息
    - wnd.post_message(hwnd, msg, wp, lp) 异步发送消息
]]
local T = require("tests.test_framework")

local pid, hwnd = nil, nil

local function startNotepad()
    T.log("[wnd] 启动 notepad.exe")
    pid = proc.create("notepad.exe")
    if pid and pid > 0 then
        sys.sleep(500)
        for _ = 1, 20 do
            hwnd = wnd.find("Notepad", nil)
            if hwnd then break end
            sys.sleep(100)
        end
        if hwnd then T.log(string.format("[wnd] PID=%d, HWND=0x%X", pid, hwnd)); return true end
    end
    return false
end

local function cleanup()
    if pid then proc.kill(pid); pid = nil end
    hwnd = nil
    T.log("[wnd] 清理完成")
end

local function run()
    T.reset()
    T.log("\n=== wnd 模块测试 ===")

    T.test("准备: 创建记事本进程", function()
        T.assert_true(startNotepad(), "启动失败")
    end)

    --------------------------
    -- 窗口查找
    --------------------------
    T.log("\n--- 窗口查找 ---")
    
    T.test("wnd.find: 按类名查找", function()
        local h = wnd.find("Notepad", nil)
        if h then
            T.assert_gt(h, 0)
            hwnd = h
            T.log(string.format("  找到: 0x%X", h))
        end
    end)

    T.test("wnd.find: 按标题查找", function()
        local h = wnd.find(nil, "无标题")
        if h then
            T.assert_gt(h, 0)
            T.log(string.format("  按标题找到: 0x%X", h))
        end
    end)

    T.test("wnd.find_ex: 模糊匹配类名", function()
        local h = wnd.find_ex("Note", nil)
        if h then
            T.assert_gt(h, 0)
            T.log(string.format("  模糊匹配: 0x%X", h))
        end
    end)

    T.test("wnd.find_ex: 模糊匹配标题", function()
        local h = wnd.find_ex(nil, "标题")
        if h then T.log(string.format("  模糊标题: 0x%X", h)) end
    end)

    T.test("wnd.find_by_pid: 按PID查找", function()
        if not pid then return end
        local h = wnd.find_by_pid(pid)
        if h then
            T.assert_gt(h, 0)
            T.log(string.format("  PID %d -> 0x%X", pid, h))
        end
    end)

    T.test("wnd.find_by_pid: 带类名过滤", function()
        if not pid then return end
        local h = wnd.find_by_pid(pid, "Notepad", nil)
        if h then T.assert_gt(h, 0) end
    end)

    T.test("wnd.get_foreground: 获取前台窗口", function()
        local h = wnd.get_foreground()
        T.assert_type(h, "number")
        T.log(string.format("  前台窗口: 0x%X", h or 0))
    end)

    --------------------------
    -- 窗口信息
    --------------------------
    T.log("\n--- 窗口信息 ---")
    
    T.test("wnd.get_title: 获取标题", function()
        if not hwnd then return end
        local t = wnd.get_title(hwnd)
        T.assert_type(t, "string")
        T.log("  标题: " .. t)
    end)

    T.test("wnd.class_name: 获取类名", function()
        if not hwnd then return end
        local c = wnd.class_name(hwnd)
        T.assert_type(c, "string")
        T.assert_eq(c, "Notepad")
        T.log("  类名: " .. c)
    end)

    T.test("wnd.wnd_rect: 获取窗口矩形", function()
        if not hwnd then return end
        local x, y, w, h = wnd.wnd_rect(hwnd)
        T.assert_type(x, "number")
        T.assert_type(y, "number")
        T.assert_type(w, "number")
        T.assert_type(h, "number")
        T.assert_gt(w, 0)
        T.assert_gt(h, 0)
        T.log(string.format("  窗口矩形: (%d,%d) %dx%d", x, y, w, h))
    end)

    T.test("wnd.client_rect: 获取客户区矩形", function()
        if not hwnd then return end
        local x, y, w, h = wnd.client_rect(hwnd)
        T.assert_type(x, "number")
        T.assert_type(y, "number")
        T.assert_type(w, "number")
        T.assert_type(h, "number")
        T.log(string.format("  客户区: (%d,%d) %dx%d", x, y, w, h))
    end)

    T.test("wnd.get_pid: 获取进程ID", function()
        if not hwnd then return end
        local p = wnd.get_pid(hwnd)
        T.assert_type(p, "number")
        T.assert_gt(p, 0)
        T.assert_eq(p, pid)
        T.log("  进程ID: " .. p)
    end)

    T.test("wnd.get_tid: 获取线程ID", function()
        if not hwnd then return end
        local t = wnd.get_tid(hwnd)
        T.assert_type(t, "number")
        T.assert_gt(t, 0)
        T.log("  线程ID: " .. t)
    end)

    --------------------------
    -- 窗口状态检查
    --------------------------
    T.log("\n--- 状态检查 ---")
    
    T.test("wnd.is_visible: 检查可见性", function()
        if not hwnd then return end
        local visible = wnd.is_visible(hwnd)
        T.assert_type(visible, "boolean")
        T.assert_true(visible, "窗口应可见")
        T.log("  可见: " .. tostring(visible))
    end)

    T.test("wnd.is_minimized: 检查最小化", function()
        if not hwnd then return end
        local minimized = wnd.is_minimized(hwnd)
        T.assert_type(minimized, "boolean")
        T.assert_false(minimized, "窗口不应最小化")
    end)

    T.test("wnd.is_maximized: 检查最大化", function()
        if not hwnd then return end
        local maximized = wnd.is_maximized(hwnd)
        T.assert_type(maximized, "boolean")
    end)

    T.test("wnd.is_enabled: 检查启用状态", function()
        if not hwnd then return end
        local enabled = wnd.is_enabled(hwnd)
        T.assert_type(enabled, "boolean")
        T.assert_true(enabled, "窗口应启用")
    end)

    --------------------------
    -- 窗口位置/大小操作
    --------------------------
    T.log("\n--- 位置/大小 ---")
    
    T.test("wnd.set_pos: 设置窗口位置", function()
        if not hwnd then return end
        local ok = wnd.set_pos(hwnd, 100, 100)
        T.assert_type(ok, "boolean")
        T.assert_true(ok)
        sys.sleep(50)
        local x, y = wnd.wnd_rect(hwnd)
        T.assert_eq(x, 100)
        T.assert_eq(y, 100)
        T.log("  位置已设为 (100, 100)")
    end)

    T.test("wnd.set_size: 设置窗口大小", function()
        if not hwnd then return end
        local ok = wnd.set_size(hwnd, 800, 600)
        T.assert_type(ok, "boolean")
        T.assert_true(ok)
        sys.sleep(50)
        local _, _, w, h = wnd.wnd_rect(hwnd)
        T.assert_eq(w, 800)
        T.assert_eq(h, 600)
        T.log("  大小已设为 800x600")
    end)

    T.test("wnd.move: 移动并调整大小", function()
        if not hwnd then return end
        local ok = wnd.move(hwnd, 200, 150, 640, 480)
        T.assert_type(ok, "boolean")
        T.assert_true(ok)
        sys.sleep(50)
        local x, y, w, h = wnd.wnd_rect(hwnd)
        T.assert_eq(x, 200)
        T.assert_eq(y, 150)
        T.log(string.format("  移动到 (%d,%d) %dx%d", x, y, w, h))
    end)

    --------------------------
    -- 窗口显示状态操作
    --------------------------
    T.log("\n--- 显示操作 ---")
    
    T.test("wnd.minimize: 最小化窗口", function()
        if not hwnd then return end
        local ok = wnd.minimize(hwnd)
        T.assert_type(ok, "boolean")
        sys.sleep(100)
        T.assert_true(wnd.is_minimized(hwnd))
        T.log("  已最小化")
    end)

    T.test("wnd.restore: 还原窗口", function()
        if not hwnd then return end
        local ok = wnd.restore(hwnd)
        T.assert_type(ok, "boolean")
        sys.sleep(100)
        T.assert_false(wnd.is_minimized(hwnd))
        T.log("  已还原")
    end)

    T.test("wnd.maximize: 最大化窗口", function()
        if not hwnd then return end
        local ok = wnd.maximize(hwnd)
        T.assert_type(ok, "boolean")
        sys.sleep(100)
        T.assert_true(wnd.is_maximized(hwnd))
        T.log("  已最大化")
    end)

    T.test("wnd.restore: 从最大化还原", function()
        if not hwnd then return end
        wnd.restore(hwnd)
        sys.sleep(100)
        T.assert_false(wnd.is_maximized(hwnd))
        T.log("  已从最大化还原")
    end)

    T.test("wnd.show: 显示窗口 (SW_SHOW=5)", function()
        if not hwnd then return end
        local ok = wnd.show(hwnd, 5)
        T.assert_type(ok, "boolean")
    end)

    T.test("wnd.show: 隐藏窗口 (SW_HIDE=0)", function()
        if not hwnd then return end
        local ok = wnd.show(hwnd, 0)
        T.assert_type(ok, "boolean")
        sys.sleep(50)
        -- 恢复显示
        wnd.show(hwnd, 5)
        sys.sleep(50)
    end)

    --------------------------
    -- 窗口属性操作
    --------------------------
    T.log("\n--- 属性操作 ---")
    
    T.test("wnd.set_title: 设置窗口标题", function()
        if not hwnd then return end
        local ok = wnd.set_title(hwnd, "Test Window Title")
        T.assert_type(ok, "boolean")
        T.assert_true(ok)
        sys.sleep(50)
        local title = wnd.get_title(hwnd)
        T.assert_eq(title, "Test Window Title")
        T.log("  标题已修改")
    end)

    T.test("wnd.set_topmost: 设置置顶", function()
        if not hwnd then return end
        local ok = wnd.set_topmost(hwnd, true)
        T.assert_type(ok, "boolean")
        T.log("  已置顶: " .. tostring(ok))
    end)

    T.test("wnd.set_topmost: 取消置顶", function()
        if not hwnd then return end
        local ok = wnd.set_topmost(hwnd, false)
        T.assert_type(ok, "boolean")
        T.log("  已取消置顶: " .. tostring(ok))
    end)

    T.test("wnd.enable: 禁用窗口", function()
        if not hwnd then return end
        wnd.enable(hwnd, false)
        sys.sleep(50)
        T.assert_false(wnd.is_enabled(hwnd))
        T.log("  已禁用")
    end)

    T.test("wnd.enable: 启用窗口", function()
        if not hwnd then return end
        wnd.enable(hwnd, true)
        sys.sleep(50)
        T.assert_true(wnd.is_enabled(hwnd))
        T.log("  已启用")
    end)

    T.test("wnd.set_foreground: 设置前台窗口", function()
        if not hwnd then return end
        local ok = wnd.set_foreground(hwnd)
        T.assert_type(ok, "boolean")
        T.log("  设置前台: " .. tostring(ok))
    end)

    --------------------------
    -- 消息发送
    --------------------------
    T.log("\n--- 消息发送 ---")
    
    -- Windows 消息常量
    local WM_SETTEXT = 0x000C
    local WM_GETTEXT = 0x000D
    local WM_CLOSE   = 0x0010
    local WM_NULL    = 0x0000
    
    T.test("wnd.send_message: 同步发送消息", function()
        if not hwnd then return end
        local result = wnd.send_message(hwnd, WM_NULL, 0, 0)
        T.assert_type(result, "number")
        T.log("  WM_NULL 返回: " .. result)
    end)

    T.test("wnd.post_message: 异步发送消息", function()
        if not hwnd then return end
        local ok = wnd.post_message(hwnd, WM_NULL, 0, 0)
        T.assert_type(ok, "boolean")
        T.assert_true(ok)
        T.log("  WM_NULL 投递: " .. tostring(ok))
    end)

    --------------------------
    -- 边界测试
    --------------------------
    T.log("\n--- 边界测试 ---")
    
    T.test("wnd.find: 不存在的窗口返回nil", function()
        local h = wnd.find("NonExistentClass12345", "NonExistentTitle12345")
        T.assert_nil(h)
    end)

    T.test("wnd.get_title: 无效句柄", function()
        local title = wnd.get_title(0)
        -- 无效句柄应返回空字符串或nil
        if title then
            T.assert_type(title, "string")
        end
    end)

    T.test("wnd.get_pid: 无效句柄返回nil", function()
        local p = wnd.get_pid(0)
        T.assert_nil(p)
    end)

    return T.report("wnd")
end

local function safeRun()
    T.log("[wnd] ========== 开始测试 ==========")
    local ok, result = pcall(run)
    cleanup()
    T.log("[wnd] ========== 测试结束 ==========")
    if not ok then error(result) end
    return result
end

return { run = safeRun }
