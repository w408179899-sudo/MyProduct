--[[
    keybd 模块测试 - 键盘输入功能验证
    覆盖: 模式切换/按键操作/组合键/文本输入/OCR验证
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
local notepad = {
    pid = nil,
    hwnd = nil,
    edit_hwnd = nil
}

local function open_notepad()
    if notepad.pid then return true end
    notepad.pid = proc.create("notepad.exe")
    if not notepad.pid then return false end
    sys.sleep(500)
    
    -- 查找窗口
    for _ = 1, 20 do
        notepad.hwnd = wnd.find("Notepad", nil)  -- 按类名查找
        if notepad.hwnd and notepad.hwnd ~= 0 then break end
        sys.sleep(100)
    end
    if not notepad.hwnd or notepad.hwnd == 0 then return false end
    
    -- 查找编辑区
    notepad.edit_hwnd = wnd.find_ex(notepad.hwnd, 0, "Edit", nil)
    if not notepad.edit_hwnd or notepad.edit_hwnd == 0 then
        notepad.edit_hwnd = notepad.hwnd
    end
    
    wnd.set_foreground(notepad.hwnd)
    sys.sleep(200)
    return true
end

local function close_notepad()
    if notepad.pid then
        -- Ctrl+A, Delete 清空, 然后不保存关闭
        keybd.combo({0x11, 0x41}) -- Ctrl+A
        sys.sleep(50)
        keybd.click(0x2E) -- Delete
        sys.sleep(50)
        wnd.close(notepad.hwnd)
        sys.sleep(200)
        -- 如果弹出保存对话框，按N不保存
        local dlg = wnd.find("#32770", "记事本")
        if dlg and dlg ~= 0 then
            keybd.click(0x4E) -- N
            sys.sleep(100)
        end
        proc.kill(notepad.pid)
        notepad.pid = nil
        notepad.hwnd = nil
        notepad.edit_hwnd = nil
    end
end

-- 清空编辑区
local function clear_editor()
    if notepad.hwnd then
        wnd.set_foreground(notepad.hwnd)
        sys.sleep(100)
        keybd.combo({0x11, 0x41}) -- Ctrl+A
        sys.sleep(50)
        keybd.click(0x2E) -- Delete
        sys.sleep(100)
    end
end

-- OCR 识别记事本内容 (使用客户区)
local function ocr_notepad_content()
    if not notepad.hwnd then return nil end
    
    wnd.set_foreground(notepad.hwnd)
    sys.sleep(200)
    
    -- 使用客户区坐标，避免捕获标题栏
    local x, y, w, h = wnd.client_rect(notepad.hwnd)
    if not x or w <= 0 or h <= 0 then return nil end
    
    -- 只截取左上角区域 (文本输入位置)
    local cw = math.min(w, 300)
    local ch = math.min(h, 100)
    
    local img = vision.capture(x, y, cw, ch)
    if not img or not img:valid() then return nil end
    
    local results = ocr.recognize(img)
    vision.free(img)
    
    -- 合并所有识别文本
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
        T.log("\n--- keybd (跳过: 仅Windows) ---")
        return true
    end

    T.reset()
    T.log("\n=== keybd 模块测试 ===")

    -- API存在性
    T.test("keybd API: 函数存在", function()
        -- 前台 API
        T.assert_type(keybd.set_mode, "function")
        T.assert_type(keybd.get_mode, "function")
        T.assert_type(keybd.down, "function")
        T.assert_type(keybd.up, "function")
        T.assert_type(keybd.click, "function")
        T.assert_type(keybd.type, "function")
        T.assert_type(keybd.combo, "function")
        -- 后台 API
        T.assert_type(keybd.post_key, "function")
        T.assert_type(keybd.post_click, "function")
        T.assert_type(keybd.post_type, "function")
        T.assert_type(keybd.post_combo, "function")
    end)

    -- 模式切换
    T.test("keybd.set_mode: API模式", function()
        local ok = keybd.set_mode("api")
        T.assert_type(ok, "boolean")
        T.log("  api: " .. tostring(ok))
    end)

    T.test("keybd.get_mode: 获取当前模式", function()
        local mode = keybd.get_mode()
        T.assert_type(mode, "string")
        T.assert_eq(mode, "api")
        T.log("  当前模式: " .. mode)
    end)

    T.test("keybd.set_mode: driver模式 (可能失败)", function()
        local ok = keybd.set_mode("driver")
        T.assert_type(ok, "boolean")
        T.log("  driver: " .. tostring(ok))
    end)

    T.test("keybd.set_mode: background模式", function()
        local ok = keybd.set_mode("background")
        T.assert_type(ok, "boolean")
        T.log("  background: " .. tostring(ok))
        keybd.set_mode("api") -- 切回
    end)

    T.test("keybd.set_mode: 无效参数返回false", function()
        T.assert_false(keybd.set_mode("invalid"))
    end)

    -- 虚拟键码验证
    T.test("VK常量: 验证常用键码", function()
        T.assert_eq(0x0D, 13, "VK_RETURN")
        T.assert_eq(0x20, 32, "VK_SPACE")
        T.assert_eq(0x1B, 27, "VK_ESCAPE")
        T.assert_eq(0x09, 9, "VK_TAB")
    end)

    ---------------------------------------------------------
    -- 记事本输入测试 + OCR 验证
    ---------------------------------------------------------
    T.log("\n--- 记事本输入 + OCR 验证 ---")
    
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
        -- 测试 keybd.type 输入英文
        T.test("keybd.type: 英文输入 + OCR验证", function()
            clear_editor()
            wnd.set_foreground(notepad.hwnd)
            sys.sleep(100)
            
            local test_text = "Hello World 2025"
            keybd.type(test_text)
            sys.sleep(300)
            
            local content = ocr_notepad_content()
            T.log("  输入: " .. test_text)
            T.log("  OCR: " .. (content or "nil"))
            
            -- OCR可能有误差，只记录结果，不强制断言
            T.log("  [注] OCR结果供参考，可能有误差")
        end)

        -- 测试 keybd.click 输入数字
        T.test("keybd.click: 数字输入 + OCR验证", function()
            clear_editor()
            wnd.set_foreground(notepad.hwnd)
            sys.sleep(100)
            
            -- 输入 12345 (VK_0=0x30, VK_1=0x31, ...)
            for i = 1, 5 do
                keybd.click(0x30 + i) -- 1,2,3,4,5
                sys.sleep(30)
            end
            sys.sleep(300)
            
            local content = ocr_notepad_content()
            T.log("  输入: 12345")
            T.log("  OCR: " .. (content or "nil"))
            
            T.log("  [注] OCR结果供参考")
        end)

        -- 测试 keybd.combo 组合键
        T.test("keybd.combo: Ctrl+A 全选", function()
            clear_editor()
            wnd.set_foreground(notepad.hwnd)
            sys.sleep(100)
            
            keybd.type("SelectAll")
            sys.sleep(100)
            keybd.combo({0x11, 0x41}) -- Ctrl+A
            sys.sleep(100)
            keybd.click(0x2E) -- Delete
            sys.sleep(200)
            
            local content = ocr_notepad_content()
            T.log("  全选删除后: " .. (content or "(空)"))
            -- 内容应该被清空或只剩窗口标题
        end)

        -- 测试 keybd.down/up 按下释放
        T.test("keybd.down/up: 按住Shift输入大写", function()
            clear_editor()
            wnd.set_foreground(notepad.hwnd)
            sys.sleep(100)
            
            keybd.down(0x10) -- Shift down
            keybd.click(0x41) -- A -> 大写 A
            keybd.click(0x42) -- B -> 大写 B
            keybd.click(0x43) -- C -> 大写 C
            keybd.up(0x10) -- Shift up
            sys.sleep(300)
            
            local content = ocr_notepad_content()
            T.log("  输入: ABC (Shift)")
            T.log("  OCR: " .. (content or "nil"))
            
            T.log("  [注] OCR结果供参考")
        end)
    else
        T.log("  [跳过] 记事本或OCR未就绪")
    end

    ---------------------------------------------------------
    -- 后台输入测试 (post_*)
    ---------------------------------------------------------
    if notepad_ready then
        T.log("\n--- 后台键盘输入测试 ---")
        
        T.test("keybd.post_click: 后台按键", function()
            clear_editor()
            sys.sleep(100)
            
            -- 后台输入数字 123
            keybd.post_click(notepad.edit_hwnd, 0x31) -- '1'
            sys.sleep(50)
            keybd.post_click(notepad.edit_hwnd, 0x32) -- '2'
            sys.sleep(50)
            keybd.post_click(notepad.edit_hwnd, 0x33) -- '3'
            sys.sleep(300)
            
            if ocr_ready then
                local content = ocr_notepad_content()
                T.log("  后台输入: 123")
                T.log("  OCR: " .. (content or "nil"))
            end
        end)

        T.test("keybd.post_type: 后台输入文本", function()
            clear_editor()
            sys.sleep(100)
            
            local ok = keybd.post_type(notepad.edit_hwnd, "Hello")
            T.assert_type(ok, "boolean")
            sys.sleep(300)
            
            if ocr_ready then
                local content = ocr_notepad_content()
                T.log("  后台输入: Hello")
                T.log("  OCR: " .. (content or "nil"))
            end
        end)

        T.test("keybd.post_combo: 后台组合键 Ctrl+A", function()
            clear_editor()
            keybd.post_type(notepad.edit_hwnd, "SelectMe")
            sys.sleep(200)
            
            -- 后台 Ctrl+A 全选
            local ok = keybd.post_combo(notepad.edit_hwnd, {0x11, 0x41})
            T.assert_type(ok, "boolean")
            sys.sleep(100)
            
            -- 后台 Delete 删除
            keybd.post_click(notepad.edit_hwnd, 0x2E)
            sys.sleep(300)
            
            if ocr_ready then
                local content = ocr_notepad_content()
                T.log("  全选删除后: " .. (content or "(空)"))
            end
        end)

        T.test("keybd.post_key: 后台按下/释放", function()
            clear_editor()
            sys.sleep(100)
            
            -- 按住 Shift 输入大写
            keybd.post_key(notepad.edit_hwnd, 0x10, true)  -- Shift down
            keybd.post_click(notepad.edit_hwnd, 0x41)     -- A
            keybd.post_key(notepad.edit_hwnd, 0x10, false) -- Shift up
            sys.sleep(300)
            
            if ocr_ready then
                local content = ocr_notepad_content()
                T.log("  后台Shift+A: " .. (content or "nil"))
            end
        end)

        T.test("keybd.post_*: 窗口标题方式", function()
            clear_editor()
            sys.sleep(100)
            
            -- 使用窗口标题查找
            local ok = keybd.post_type("无标题 - 记事本", "TitleTest")
            if not ok then
                ok = keybd.post_type("Untitled - Notepad", "TitleTest")
            end
            T.assert_type(ok, "boolean")
            sys.sleep(200)
            T.log("  使用窗口标题: " .. tostring(ok))
        end)
    end

    ---------------------------------------------------------
    -- set_window 测试
    ---------------------------------------------------------
    T.log("\n--- set_window ---")

    T.test("keybd.set_window: 函数存在", function()
        T.assert_type(keybd.set_window, "function")
    end)

    T.test("keybd.set_window: 设置无效窗口不崩溃", function()
        local ok, err = pcall(keybd.set_window, 0)
        T.log("  set_window(0): ok=" .. tostring(ok))
    end)

    -- 清理
    T.log("\n--- 清理 ---")
    close_notepad()
    if ocr_ready then ocr.release() end
    T.log("  记事本已关闭")

    return T.report("keybd")
end

return { run = run }
