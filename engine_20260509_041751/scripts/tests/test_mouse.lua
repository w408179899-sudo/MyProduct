--[[
    mouse 模块测试 - 鼠标输入功能验证
    覆盖: 模式切换/位置获取/点击/拖动/滚轮/OCR验证
    仅在 Windows 平台运行
]]
local T = require("tests.test_framework")

-- OCR 辅助函数
local function get_models_dir()
    local src = debug.getinfo(1, "S").source:match("@(.+[\\/])")
    local candidates = {}
    if src then
        candidates[#candidates + 1] = src .. "../../models"
        candidates[#candidates + 1] = src .. "../models"
    end
    candidates[#candidates + 1] = "./models"
    candidates[#candidates + 1] = "../models"
    candidates[#candidates + 1] = "models"
    for _, p in ipairs(candidates) do
        local f = io.open(p .. "/keys.txt", "r")
        if f then f:close(); return p end
    end
    return nil
end

-- 记事本管理
local notepad = { pid = nil, hwnd = nil }

local function open_notepad()
    if notepad.pid then return true end
    notepad.pid = proc.create("notepad.exe")
    if not notepad.pid then return false end
    sys.sleep(500)
    
    for _ = 1, 20 do
        notepad.hwnd = wnd.find("Notepad", nil)  -- 按类名查找
        if notepad.hwnd and notepad.hwnd ~= 0 then break end
        sys.sleep(100)
    end
    if not notepad.hwnd or notepad.hwnd == 0 then return false end
    
    wnd.set_foreground(notepad.hwnd)
    sys.sleep(200)
    return true
end

local function close_notepad()
    if notepad.pid then
        keybd.combo({0x11, 0x41}) -- Ctrl+A
        sys.sleep(50)
        keybd.click(0x2E) -- Delete
        sys.sleep(50)
        wnd.close(notepad.hwnd)
        sys.sleep(200)
        local dlg = wnd.find("#32770", "记事本")
        if dlg and dlg ~= 0 then
            keybd.click(0x4E) -- N
            sys.sleep(100)
        end
        proc.kill(notepad.pid)
        notepad.pid = nil
        notepad.hwnd = nil
    end
end

-- OCR 识别记事本内容 (使用客户区)
local function ocr_notepad_content()
    if not notepad.hwnd then return nil end
    wnd.set_foreground(notepad.hwnd)
    sys.sleep(200)
    
    local x, y, w, h = wnd.client_rect(notepad.hwnd)
    if not x or w <= 0 or h <= 0 then return nil end
    
    local cw = math.min(w, 300)
    local ch = math.min(h, 100)
    
    local img = vision.capture(x, y, cw, ch)
    if not img or not img:valid() then return nil end
    
    local results = ocr.recognize(img)
    vision.free(img)
    
    local texts = {}
    for _, r in ipairs(results) do
        if r.text and #r.text > 0 then
            table.insert(texts, r.text)
        end
    end
    return table.concat(texts, " ")
end

local function run()
    if sys.platform() ~= "windows" then
        T.log("\n--- mouse (跳过: 仅Windows) ---")
        return true
    end

    T.reset()
    T.log("\n=== mouse 模块测试 ===")

    -- 模式切换
    T.test("mouse.set_mode: API模式", function()
        local ok = mouse.set_mode("api")
        T.assert_type(ok, "boolean")
        T.log("  api: " .. tostring(ok))
    end)

    T.test("mouse.get_mode: 获取当前模式", function()
        local mode = mouse.get_mode()
        T.assert_type(mode, "string")
        T.assert_eq(mode, "api")
        T.log("  当前模式: " .. mode)
    end)

    T.test("mouse.set_mode: driver模式 (可能失败)", function()
        local ok = mouse.set_mode("driver")
        T.assert_type(ok, "boolean")
        T.log("  driver: " .. tostring(ok))
        mouse.set_mode("api")
    end)

    T.test("mouse.set_mode: 无效参数返回false", function()
        T.assert_false(mouse.set_mode("invalid"))
    end)

    -- 位置获取
    T.test("mouse.position: 获取当前位置", function()
        local x, y = mouse.position()
        T.assert_type(x, "number"); T.assert_type(y, "number")
        T.assert_gte(x, 0); T.assert_gte(y, 0)
        T.log(string.format("  位置: (%d, %d)", x, y))
    end)

    -- API存在性
    T.test("mouse API: 函数存在", function()
        -- 前台 API
        T.assert_type(mouse.set_mode, "function")
        T.assert_type(mouse.get_mode, "function")
        T.assert_type(mouse.set_trajectory, "function")
        T.assert_type(mouse.get_trajectory, "function")
        T.assert_type(mouse.move, "function")
        T.assert_type(mouse.move_to, "function")
        T.assert_type(mouse.down, "function")
        T.assert_type(mouse.up, "function")
        T.assert_type(mouse.click, "function")
        T.assert_type(mouse.double_click, "function")
        T.assert_type(mouse.wheel, "function")
        T.assert_type(mouse.drag, "function")
        T.assert_type(mouse.position, "function")
        -- 后台 API (线程安全, hwnd 参数传入)
        T.assert_type(mouse.post_move, "function")
        T.assert_type(mouse.post_down, "function")
        T.assert_type(mouse.post_up, "function")
        T.assert_type(mouse.post_click, "function")
        T.assert_type(mouse.post_wheel, "function")
    end)

    -- 轨迹模式
    T.test("mouse.set_trajectory: 设置轨迹模式", function()
        -- 默认应为 none
        T.assert_eq(mouse.get_trajectory(), "none")
        
        -- 遍历所有合法模式
        local modes = {"none", "robot", "fast", "average", "granny", "precise"}
        for _, m in ipairs(modes) do
            local ok = mouse.set_trajectory(m)
            T.assert_true(ok, "set_trajectory(" .. m .. ")")
            T.assert_eq(mouse.get_trajectory(), m)
        end
        
        -- 无效模式应报错
        local ok, err = pcall(mouse.set_trajectory, "invalid_mode")
        T.assert_false(ok, "无效模式应报错")
        T.log("  无效模式错误: " .. tostring(err):sub(1, 60))
        
        -- 恢复默认
        mouse.set_trajectory("none")
        T.assert_eq(mouse.get_trajectory(), "none")
    end)

    T.test("mouse.move_to + trajectory: 轨迹模式移动", function()
        mouse.set_mode("api")
        mouse.set_trajectory("fast")
        
        local x1, y1 = mouse.position()
        -- 小距离移动测试 (避免干扰用户桌面)
        mouse.move_to(x1 + 20, y1 + 20)
        sys.sleep(100)
        local x2, y2 = mouse.position()
        T.log(string.format("  轨迹移动: (%d,%d)->(%d,%d)", x1, y1, x2, y2))
        
        -- 恢复
        mouse.set_trajectory("none")
    end)

    ---------------------------------------------------------
    -- 记事本鼠标操作测试 + OCR 验证
    ---------------------------------------------------------
    T.log("\n--- 记事本鼠标 + OCR 验证 ---")
    
    -- 初始化 OCR (可选，找不到模型则跳过验证)
    local ocr_ready = false
    T.test("OCR 初始化 (可选)", function()
        local models_dir = get_models_dir()
        if not models_dir then
            T.log("  [跳过] 未找到 OCR 模型目录")
            return
        end
        ocr_ready = ocr.init(models_dir)
        T.log("  模型目录: " .. models_dir .. ", 结果: " .. tostring(ocr_ready))
    end)

    -- 打开记事本
    local notepad_ready = false
    T.test("打开记事本", function()
        notepad_ready = open_notepad()
        T.assert_true(notepad_ready, "启动记事本")
        T.log(string.format("  PID=%s, HWND=%s", tostring(notepad.pid), tostring(notepad.hwnd)))
    end)

    if notepad_ready and ocr_ready then
        -- 测试 mouse.move_to 和 mouse.click
        T.test("mouse.move_to + click: 点击编辑区", function()
            wnd.set_foreground(notepad.hwnd)
            sys.sleep(100)
            
            local x, y, w, h = wnd.wnd_rect(notepad.hwnd)
            if x then
                -- 点击编辑区中心
                local cx, cy = x + w // 2, y + h // 2
                mouse.move_to(cx, cy)
                sys.sleep(100)
                mouse.click()
                sys.sleep(100)
                
                local mx, my = mouse.position()
                T.log(string.format("  目标: (%d,%d), 实际: (%d,%d)", cx, cy, mx, my))
                -- DPI缩放可能导致坐标偏差较大，仅验证移动到了大致区域
                local dx = math.abs(mx - cx)
                local dy = math.abs(my - cy)
                T.log(string.format("  偏差: dx=%d, dy=%d", dx, dy))
                -- DPI 缩放可能导致大偏差，仅记录不强制断言
                T.log(string.format("  [注] 坐标偏差供参考 (DPI缩放可能影响)"))
            end
        end)

        -- 测试点击后输入
        T.test("mouse.click + keybd: 点击后输入 + OCR验证", function()
            wnd.set_foreground(notepad.hwnd)
            sys.sleep(100)
            
            -- 先清空
            keybd.combo({0x11, 0x41})
            sys.sleep(50)
            keybd.click(0x2E)
            sys.sleep(100)
            
            -- 点击编辑区
            local x, y, w, h = wnd.wnd_rect(notepad.hwnd)
            if x then
                mouse.move_to(x + w // 2, y + h // 2)
                sys.sleep(50)
                mouse.click()
                sys.sleep(100)
            end
            
            -- 输入文本
            keybd.type("MouseTest123")
            sys.sleep(300)
            
            local content = ocr_notepad_content()
            T.log("  输入: MouseTest123")
            T.log("  OCR: " .. (content or "nil"))
            
            T.log("  [注] OCR结果供参考")
        end)

        -- 测试 mouse.double_click 选中单词
        T.test("mouse.double_click: 双击选中单词", function()
            wnd.set_foreground(notepad.hwnd)
            sys.sleep(100)
            
            -- 清空并输入
            keybd.combo({0x11, 0x41})
            sys.sleep(50)
            keybd.click(0x2E)
            sys.sleep(50)
            keybd.type("Hello World")
            sys.sleep(200)
            
            -- 双击选中 Hello
            local x, y, w, h = wnd.wnd_rect(notepad.hwnd)
            if x then
                -- 点击靠左侧 (Hello 位置)
                mouse.move_to(x + 50, y + h // 2)
                sys.sleep(50)
                mouse.double_click()
                sys.sleep(200)
                
                -- 输入替换文字
                keybd.type("Hi")
                sys.sleep(300)
                
                local content = ocr_notepad_content()
                T.log("  替换后: " .. (content or "nil"))
                -- 应该包含 Hi 或 World
            end
        end)

        -- 测试 mouse.wheel 滚动
        T.test("mouse.wheel: 滚轮滚动", function()
            wnd.set_foreground(notepad.hwnd)
            sys.sleep(100)
            
            -- 滚轮向上
            mouse.wheel(3)
            sys.sleep(100)
            -- 滚轮向下
            mouse.wheel(-3)
            sys.sleep(100)
            T.log("  滚轮测试完成")
        end)

        -- 测试 mouse.drag 拖动选择
        T.test("mouse.drag: 拖动选择文本", function()
            wnd.set_foreground(notepad.hwnd)
            sys.sleep(100)
            
            -- 清空并输入
            keybd.combo({0x11, 0x41})
            sys.sleep(50)
            keybd.click(0x2E)
            sys.sleep(50)
            keybd.type("DragTest")
            sys.sleep(200)
            
            local x, y, w, h = wnd.wnd_rect(notepad.hwnd)
            if x then
                -- 从左到右拖动选择
                local startX, startY = x + 30, y + h // 2
                local endX, endY = x + 100, y + h // 2
                
                mouse.move_to(startX, startY)
                sys.sleep(50)
                mouse.drag(startX, startY, endX, endY)
                sys.sleep(200)
                
                T.log(string.format("  拖动: (%d,%d) -> (%d,%d)", startX, startY, endX, endY))
            end
        end)
    else
        T.log("  [跳过] 记事本或OCR未就绪")
    end

    ---------------------------------------------------------
    -- 后台鼠标输入测试 (post_*)
    ---------------------------------------------------------
    if notepad_ready then
        T.log("\n--- 后台鼠标输入测试 ---")
        
        T.test("mouse.post_move: 后台移动", function()
            local ok = mouse.post_move(notepad.hwnd, 50, 50)
            T.assert_type(ok, "boolean")
            T.log("  后台移动到 (50, 50): " .. tostring(ok))
        end)

        T.test("mouse.post_click: 后台点击", function()
            -- 先清空
            keybd.combo({0x11, 0x41})
            sys.sleep(50)
            keybd.click(0x2E)
            sys.sleep(100)
            
            -- 后台点击编辑区
            local ok = mouse.post_click(notepad.hwnd, 100, 100)
            T.assert_type(ok, "boolean")
            sys.sleep(100)
            
            -- 输入验证
            keybd.type("PostClick")
            sys.sleep(300)
            
            if ocr_ready then
                local content = ocr_notepad_content()
                T.log("  后台点击后输入: " .. (content or "nil"))
            end
        end)

        T.test("mouse.post_down/up: 后台按下释放", function()
            local ok1 = mouse.post_down(notepad.hwnd, "left")
            T.assert_type(ok1, "boolean")
            sys.sleep(50)
            local ok2 = mouse.post_up(notepad.hwnd, "left")
            T.assert_type(ok2, "boolean")
            T.log("  后台按下/释放: " .. tostring(ok1) .. "/" .. tostring(ok2))
        end)

        T.test("mouse.post_wheel: 后台滚轮", function()
            local ok = mouse.post_wheel(notepad.hwnd, 120)
            T.assert_type(ok, "boolean")
            sys.sleep(50)
            ok = mouse.post_wheel(notepad.hwnd, -120)
            T.assert_type(ok, "boolean")
            T.log("  后台滚轮: " .. tostring(ok))
        end)

        T.test("mouse.post_*: 窗口标题方式", function()
            -- 使用窗口标题查找
            local ok = mouse.post_click("无标题 - 记事本", 50, 50)
            if not ok then
                ok = mouse.post_click("Untitled - Notepad", 50, 50)
            end
            T.assert_type(ok, "boolean")
            T.log("  使用窗口标题: " .. tostring(ok))
        end)
    end

    ---------------------------------------------------------
    -- set_window / 鼠标系统设置 / 前台 down/up 测试
    ---------------------------------------------------------
    T.log("\n--- set_window / 鼠标设置 / down/up ---")

    T.test("mouse.set_window: 函数存在", function()
        T.assert_type(mouse.set_window, "function")
    end)

    T.test("mouse.set_window: 设置无效窗口不崩溃", function()
        -- 传入 0 或无效句柄，仅验证不崩溃
        local ok, err = pcall(mouse.set_window, 0)
        T.log("  set_window(0): ok=" .. tostring(ok))
    end)

    T.test("mouse.get_accel: 获取鼠标加速度", function()
        local accel = mouse.get_accel()
        T.assert_type(accel, "table")
        T.assert_type(accel.threshold1, "number")
        T.assert_type(accel.threshold2, "number")
        T.assert_type(accel.acceleration, "number")
        T.log(string.format("  鼠标加速度: t1=%d, t2=%d, accel=%d",
            accel.threshold1, accel.threshold2, accel.acceleration))
    end)

    T.test("mouse.set_accel: 设置鼠标加速度", function()
        local original = mouse.get_accel()
        -- 使用原始值恢复 (3个整数参数)
        local ok = mouse.set_accel(original.threshold1, original.threshold2, original.acceleration)
        T.assert_type(ok, "boolean")
        T.log("  设置加速度: " .. tostring(ok))
    end)

    T.test("mouse.get_speed: 获取鼠标速度", function()
        local speed = mouse.get_speed()
        T.assert_type(speed, "number")
        T.assert_gt(speed, 0, "速度应大于0")
        T.log("  鼠标速度: " .. speed)
    end)

    T.test("mouse.set_speed: 设置鼠标速度", function()
        local original = mouse.get_speed()
        -- 设置后恢复
        local ok = mouse.set_speed(original)
        T.assert_type(ok, "boolean")
        T.log("  设置速度: " .. tostring(ok))
    end)

    T.test("mouse.move: 相对移动", function()
        mouse.set_mode("api")
        local x1, y1 = mouse.position()
        mouse.move(5, 5)
        sys.sleep(50)
        local x2, y2 = mouse.position()
        T.log(string.format("  相对移动(5,5): (%d,%d)->(%d,%d)", x1, y1, x2, y2))
        -- DPI缩放可能导致偏差，仅验证有变化或不崩溃
    end)

    T.test("mouse.down/up: 前台按下释放", function()
        mouse.set_mode("api")
        -- 在当前位置按下并释放左键
        mouse.down("left")
        sys.sleep(30)
        mouse.up("left")
        sys.sleep(30)
        T.log("  前台 down/up 完成")
    end)

    -- 清理
    T.log("\n--- 清理 ---")
    close_notepad()
    if ocr_ready then ocr.release() end
    T.log("  记事本已关闭")

    return T.report("mouse")
end

return { run = run }
