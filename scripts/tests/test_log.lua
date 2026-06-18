--[[
    log 模块测试 - 日志输出功能验证
    覆盖: trace/debug/info/warn/error/print (6 个 API)
]]
local T = require("tests.test_framework")

local function run()
    T.reset()
    T.log("\n=== log 模块测试 ===")

    -- API 存在性
    T.test("log 模块存在", function()
        T.assert_type(log, "table")
    end)

    T.test("log API: 函数存在", function()
        T.assert_type(log.trace, "function")
        T.assert_type(log.debug, "function")
        T.assert_type(log.info, "function")
        T.assert_type(log.warn, "function")
        T.assert_type(log.error, "function")
        T.assert_type(log.print, "function")
    end)

    -- 各级别日志调用 (不应报错)
    T.test("log.trace: 追踪日志", function()
        log.trace("[test] trace message")
    end)

    T.test("log.debug: 调试日志", function()
        log.debug("[test] debug message")
    end)

    T.test("log.info: 信息日志", function()
        log.info("[test] info message")
    end)

    T.test("log.warn: 警告日志", function()
        log.warn("[test] warn message")
    end)

    T.test("log.error: 错误日志", function()
        log.error("[test] error message")
    end)

    -- log.print 多参数测试
    T.test("log.print: 单参数", function()
        log.print("single argument")
    end)

    T.test("log.print: 多参数", function()
        log.print("arg1", 123, true, nil, "arg5")
    end)

    T.test("log.print: 空调用", function()
        log.print()
    end)

    -- 特殊字符
    T.test("log.info: 中文日志", function()
        log.info("[test] 中文日志测试 - 你好世界")
    end)

    T.test("log.info: 特殊字符", function()
        log.info("[test] special chars: \t tab \\ backslash")
    end)

    return T.report("log")
end

return { run = run }
