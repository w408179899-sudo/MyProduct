--[[
    remote 模块测试 - 中控客户端功能验证
    覆盖: connect/disconnect/is_connected/define_fields/report_status/report/poll
    注意: 需要中控服务器才能测试连接功能，离线测试仅验证 API 可调用性
]]
local T = require("tests.test_framework")

local function run()
    T.reset()
    T.log("\n=== remote 模块测试 ===")

    T.test("remote 模块存在", function()
        T.assert_true(remote ~= nil)
        T.assert_true(type(remote) == "table")
    end)

    T.test("remote.is_connected: 未连接时返回false", function()
        T.assert_eq(remote.is_connected(), false)
    end)

    T.test("remote.poll: 未连接时不阻塞，返回空数组", function()
        local cmds = remote.poll()
        T.assert_true(type(cmds) == "table")
        T.assert_eq(#cmds, 0)
    end)

    -- 未连接时调用不崩溃
    T.test("remote.define_fields: 未连接时不崩溃", function()
        remote.define_fields("角色名|等级|状态")
    end)

    T.test("remote.report_status: 未连接时不崩溃", function()
        remote.report_status("测试角色|99|运行中")
    end)

    T.test("remote.report: 未连接时不崩溃", function()
        remote.report("gold", "12345")
    end)

    -- 连接测试 (需要服务器)
    local test_server = os.getenv("REMOTE_TEST_SERVER")
    if test_server then
        T.log("  检测到 REMOTE_TEST_SERVER=" .. test_server .. "，执行连接测试")

        T.test("remote.connect: 连接中控服务器 (阻塞5秒超时)", function()
            local ok = remote.connect(test_server, sys.hwid())
            T.assert_true(ok)
        end)

        T.test("remote.is_connected: 连接后返回true", function()
            T.assert_eq(remote.is_connected(), true)
        end)

        T.test("remote.define_fields: 定义字段", function()
            remote.define_fields("角色名|等级|状态|金币")
        end)

        T.test("remote.report_status: 上报状态 (同时作为心跳)", function()
            remote.report_status("测试角色|99|运行中|99999")
        end)

        T.test("remote.report: 上报自定义数据", function()
            remote.report("test_key", "test_value")
        end)

        T.test("remote.poll: 连接中轮询不阻塞", function()
            local cmds = remote.poll()
            T.assert_true(type(cmds) == "table")
        end)

        T.test("remote.disconnect: 断开连接", function()
            remote.disconnect()
            T.assert_eq(remote.is_connected(), false)
        end)
    else
        T.log("  [跳过] 未设置 REMOTE_TEST_SERVER 环境变量，跳过连接测试")
        T.log("  设置方式: set REMOTE_TEST_SERVER=ws://你的中控服务器:端口/ws")
    end

    -- 连接失败测试 (阻塞5秒超时后返回false)
    T.test("remote.connect: 连接不存在的服务器返回false", function()
        local ok = remote.connect("ws://127.0.0.1:19999", sys.hwid())
        T.assert_eq(ok, false)
        T.assert_eq(remote.is_connected(), false)
        remote.disconnect()
    end)

    T.summary()
end

run()
