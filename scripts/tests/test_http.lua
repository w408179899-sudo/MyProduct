--[[
    http 模块测试 - HTTP请求功能验证
    覆盖: URL编解码/GET/POST/下载/超时设置
    自动检测网络可用性，无网络时跳过网络相关测试
]]
local T = require("tests.test_framework")

-- 检测网络是否可用 (尝试连接 httpbin.org)
local function check_network()
    local r = http.get("http://httpbin.org/status/200")
    return r and r.status == 200
end

local function run()
    T.reset()
    T.log("\n=== http 模块测试 ===")

    -- URL编解码
    T.test("http.url_encode: URL编码", function()
        T.assert_eq(http.url_encode("hello world"), "hello%20world")
        local enc = http.url_encode("key=value&foo=bar")
        T.assert_true(enc:find("%%3D") ~= nil and enc:find("%%26") ~= nil)
    end)

    T.test("http.url_decode: URL解码", function()
        T.assert_eq(http.url_decode("hello%20world"), "hello world")
        T.assert_eq(http.url_decode("key%3Dvalue%26foo%3Dbar"), "key=value&foo=bar")
    end)

    T.test("http.set_timeout: 设置超时", function()
        http.set_timeout(30) -- 不应报错
    end)

    T.test("http.set_timeout: 设置为0不崩溃", function()
        http.set_timeout(0) -- 不应报错
        http.set_timeout(30) -- 恢复
    end)

    -- 真实网络测试 (自动检测网络可用性)
    local has_network = check_network()
    if has_network then
        T.log("  [网络可用] 运行网络测试")
        
        T.test("http.get: GET httpbin.org", function()
            local r = http.get("http://httpbin.org/get")
            T.assert_eq(r.status, 200)
            T.assert_type(r.body, "string"); T.assert_gt(#r.body, 0)
            T.log("  响应长度: " .. #r.body)
        end)

        T.test("http.post: POST httpbin.org", function()
            local r = http.post("http://httpbin.org/post", '{"name":"test"}', "application/json")
            T.assert_eq(r.status, 200)
            T.assert_true(r.body:find("test") ~= nil)
        end)

        T.test("http.get: 自定义Headers", function()
            local r = http.get("http://httpbin.org/headers", {
                ["X-Custom"] = "test-value", ["User-Agent"] = "AetherEngine/1.0"
            })
            -- 网络超时或失败时跳过验证
            if r.status == 200 then
                T.assert_true(r.body:find("test-value") ~= nil or r.body:find("X-Custom") ~= nil)
            else
                T.log("  网络请求失败，跳过Headers验证")
            end
        end)
    else
        T.log("  [网络不可用] 跳过网络测试")
    end

    -- http.download 测试
    T.test("http.download: 函数存在", function()
        T.assert_type(http.download, "function")
    end)

    if has_network then
        T.test("http.download: 下载小文件", function()
            local tmp = sys.tmpdir()
            local filepath = tmp .. "/aether_http_test.txt"
            local result = http.download("http://httpbin.org/robots.txt", filepath)
            T.assert_type(result, "table")
            if result.success then
                T.assert_type(result.size, "number")
                T.assert_gt(result.size, 0, "下载大小应大于0")
                -- 验证文件已创建
                local f = io.open(filepath, "r")
                if f then
                    local content = f:read("*a")
                    f:close()
                    T.assert_gt(#content, 0, "下载内容不应为空")
                    T.log("  下载大小: " .. result.size .. " bytes")
                    os.remove(filepath)
                end
            else
                T.log("  下载失败: " .. tostring(result.error))
            end
        end)
    end

    return T.report("http")
end

return { run = run }
