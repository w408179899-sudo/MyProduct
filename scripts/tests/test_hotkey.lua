--[[
    hotkey 模块测试 - 热键监听功能验证 (纯轮询模式)
    
    API (重构后, 无 Lua 回调):
    - hotkey.start(interval?)    启动监听线程
    - hotkey.stop()              停止监听线程
    - hotkey.is_running()        检查监听状态
    - hotkey.is_pressed(vk)      查询按键是否按下
    - hotkey.get_pressed()       获取所有按下的键
    - hotkey.set_interval(ms)    设置轮询间隔
    
    注: register/unregister/unregister_all/unregister_key 已移除
        Lua 脚本自行管理热键逻辑 (通过 is_pressed 轮询)
]]
local T = require("tests.test_framework")

local function run()
    T.reset()
    T.log("\n=== hotkey 模块测试 (纯轮询) ===")

    T.test("hotkey模块存在", function()
        T.assert_type(hotkey, "table")
    end)

    T.test("hotkey API: 函数存在", function()
        T.assert_type(hotkey.start, "function")
        T.assert_type(hotkey.stop, "function")
        T.assert_type(hotkey.is_running, "function")
        T.assert_type(hotkey.is_pressed, "function")
        T.assert_type(hotkey.get_pressed, "function")
        T.assert_type(hotkey.set_interval, "function")
    end)

    T.test("hotkey API: 回调接口已移除", function()
        -- 这些函数在重构后不再存在
        T.assert_nil(hotkey.register, "register 应已移除")
        T.assert_nil(hotkey.unregister, "unregister 应已移除")
        T.assert_nil(hotkey.unregister_key, "unregister_key 应已移除")
        T.assert_nil(hotkey.unregister_all, "unregister_all 应已移除")
    end)

    T.test("hotkey.start/stop: 启停监听", function()
        hotkey.stop()
        T.assert_false(hotkey.is_running())
        local ok = hotkey.start()
        T.assert_true(ok, "首次启动应成功")
        T.assert_true(hotkey.is_running())
        hotkey.stop()
        T.assert_false(hotkey.is_running())
    end)

    T.test("hotkey.start: 重复启动返回false", function()
        hotkey.start()
        T.assert_true(hotkey.is_running())
        -- 重复启动应返回 false (已在运行)
        local ok = hotkey.start()
        T.assert_false(ok, "重复启动应返回false")
        T.assert_true(hotkey.is_running(), "仍应在运行")
        hotkey.stop()
    end)

    T.test("hotkey.start: 自定义间隔", function()
        local ok = hotkey.start(20) -- 20ms 间隔
        T.assert_true(ok)
        T.assert_true(hotkey.is_running())
        hotkey.stop()
    end)

    T.test("hotkey.is_pressed: 查询按键状态", function()
        hotkey.start()
        sys.sleep(50) -- 等待轮询线程启动
        -- F13 (0x7C) 正常情况下不会被按下
        T.assert_false(hotkey.is_pressed(0x7C), "F13应未按下")
        -- 检查多个不常用键
        T.assert_false(hotkey.is_pressed(0x7D), "F14应未按下")
        T.assert_false(hotkey.is_pressed(0x7E), "F15应未按下")
        hotkey.stop()
    end)

    T.test("hotkey.get_pressed: 获取当前所有按下的键", function()
        hotkey.start()
        sys.sleep(50) -- 等待轮询线程启动
        local keys = hotkey.get_pressed()
        T.assert_type(keys, "table")
        T.log("  当前按下的键数: " .. #keys)
        -- 列出按下的键 (如果有)
        if #keys > 0 then
            local vks = {}
            for _, vk in ipairs(keys) do
                table.insert(vks, string.format("0x%02X", vk))
            end
            T.log("  按下的键: " .. table.concat(vks, ", "))
        end
        hotkey.stop()
    end)

    T.test("hotkey.set_interval: 设置轮询间隔", function()
        hotkey.set_interval(10)
        -- 不应报错
    end)

    T.test("hotkey.set_interval: 非法间隔应报错", function()
        local ok, err = pcall(hotkey.set_interval, 0)
        T.assert_false(ok, "间隔0应报错")
        local ok2, err2 = pcall(hotkey.set_interval, -1)
        T.assert_false(ok2, "负间隔应报错")
    end)

    T.test("hotkey.stop: 重复停止不应报错", function()
        hotkey.stop()
        hotkey.stop() -- 重复停止
        T.assert_false(hotkey.is_running())
    end)

    return T.report("hotkey")
end

return { run = run }
