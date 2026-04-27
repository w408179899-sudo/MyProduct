--[[
    resource 模块自动化测试
    覆盖: init / set_auth / download / query / upload / check_update
           / load_script / install_searcher / clear_cache
    
    测试资源: test/mymodule.luac (已上传到服务器)
    加解密由用户自行处理，模块只负责传输原始数据
]]
local T = require("tests.test_framework")

-- 测试常量
local TEST_RESOURCE = "test/mymodule.luac"   -- 已上传的测试资源
local TEST_OWNER    = "admin"
local TEST_KEY      = "admin"
local TEST_PASS     = ""
local CACHE_DIR     = "./cache"

local function run()
    T.reset()
    T.log("\n=== resource 模块自动化测试 ===")

    -----------------------------------------------------------------------
    -- 1. API 存在性检查
    -----------------------------------------------------------------------
    T.test("resource API: 所有函数存在", function()
        T.assert_type(resource.init,             "function")
        T.assert_type(resource.set_auth,         "function")
        T.assert_type(resource.download,         "function")
        T.assert_type(resource.query,            "function")
        T.assert_type(resource.upload,           "function")
        T.assert_type(resource.check_update,     "function")
        T.assert_type(resource.load_script,      "function")
        T.assert_type(resource.install_searcher, "function")
        T.assert_type(resource.clear_cache,      "function")
    end)

    -----------------------------------------------------------------------
    -- 2. 初始化与配置
    -----------------------------------------------------------------------
    T.test("resource.init: 初始化缓存目录", function()
        resource.init(CACHE_DIR)
    end)

    T.test("resource.set_auth: 设置认证参数", function()
        resource.set_auth(TEST_OWNER, TEST_KEY, TEST_PASS)
    end)

    T.test("resource.clear_cache: 清空缓存 (测试前置)", function()
        resource.clear_cache()
    end)

    -----------------------------------------------------------------------
    -- 3. 服务器在线检测 (后续在线测试依赖此结果)
    -----------------------------------------------------------------------
    local online = true
    if not online then
        T.log("\n  [!] 服务器不可达，跳过在线测试")
    end

    -----------------------------------------------------------------------
    -- 4. query: 查询资源元数据
    -----------------------------------------------------------------------
    if online then
        T.test("resource.query: 查询已存在资源", function()
            local info, err = resource.query(TEST_RESOURCE)
            T.assert_not_nil(info, "query 应返回 info table")
            T.assert_type(info, "table")
            T.assert_not_nil(info.path,      "info.path 不应为 nil")
            T.assert_not_nil(info.file_name, "info.file_name 不应为 nil")
            T.assert_not_nil(info.xxhash,    "info.xxhash 不应为 nil")
            T.assert_type(info.size, "number")
            T.assert_gt(info.size, 0, "info.size 应 > 0")
            T.log("  path:      " .. tostring(info.path))
            T.log("  file_name: " .. tostring(info.file_name))
            T.log("  size:      " .. tostring(info.size))
            T.log("  xxhash:    " .. tostring(info.xxhash))
            T.log("  upload_time: " .. tostring(info.upload_time))
        end)

        T.test("resource.query: 查询不存在资源返回 nil", function()
            local info, err = resource.query("nonexistent/no_such_file.dat")
            T.assert_nil(info, "不存在资源应返回 nil")
            T.assert_type(err, "string")
            T.log("  预期错误: " .. err)
        end)
    else
        T.skip("resource.query: 查询已存在资源", "服务器不可达")
        T.skip("resource.query: 查询不存在资源返回 nil", "服务器不可达")
    end

    -----------------------------------------------------------------------
    -- 5. download: 下载资源原始数据
    -----------------------------------------------------------------------
    if online then
        T.test("resource.download: 下载已存在资源", function()
            resource.clear_cache()
            local data, err = resource.download(TEST_RESOURCE)
            T.assert_not_nil(data, "download 应返回数据")
            T.assert_type(data, "string")
            T.assert_gt(#data, 0, "数据长度应 > 0")
            T.log("  大小: " .. #data .. " bytes")
            -- 简单检查内容类型
            local is_bytecode = data:sub(1, 4) == "\x1bLua"
            T.log("  类型: " .. (is_bytecode and "Lua 字节码" or "其他"))
        end)

        T.test("resource.download: 缓存命中 (第二次下载)", function()
            -- 第二次下载同一资源，应从缓存/服务器返回相同数据
            local data, err = resource.download(TEST_RESOURCE)
            T.assert_not_nil(data, "第二次下载应返回数据")
            T.assert_gt(#data, 0, "数据长度应 > 0")
            T.log("  大小: " .. #data .. " bytes (再次下载)")
        end)

        T.test("resource.download: 不存在资源返回 nil", function()
            local data, err = resource.download("nonexistent/no_such_file.dat")
            T.assert_nil(data, "不存在资源应返回 nil")
            T.assert_type(err, "string")
            T.log("  预期错误: " .. err)
        end)
    else
        T.skip("resource.download: 下载已存在资源", "服务器不可达")
        T.skip("resource.download: 缓存命中", "服务器不可达")
        T.skip("resource.download: 不存在资源返回 nil", "服务器不可达")
    end

    -----------------------------------------------------------------------
    -- 6. check_update: 基于 xxhash 对比检查更新
    -----------------------------------------------------------------------
    if online then
        T.test("resource.check_update: 刚下载的资源无需更新", function()
            -- 上一步已下载 TEST_RESOURCE，hash 已记录
            local updates = resource.check_update({TEST_RESOURCE})
            T.assert_type(updates, "table")
            T.assert_eq(#updates, 0, "刚下载的资源不应需要更新")
            T.log("  需要更新: " .. #updates .. " 个")
        end)

        T.test("resource.check_update: 清空缓存后需要更新", function()
            resource.clear_cache()
            local updates = resource.check_update({TEST_RESOURCE})
            T.assert_type(updates, "table")
            T.assert_eq(#updates, 1, "清空缓存后应需要更新")
            T.assert_eq(updates[1], TEST_RESOURCE)
            T.log("  需要更新: " .. updates[1])
        end)

        T.test("resource.check_update: 无参数检查所有已缓存资源", function()
            -- 先重新下载一个资源
            resource.download(TEST_RESOURCE)
            local updates = resource.check_update()
            T.assert_type(updates, "table")
            T.log("  已缓存资源中需要更新: " .. #updates .. " 个")
        end)
    else
        T.skip("resource.check_update: 刚下载的资源无需更新", "服务器不可达")
        T.skip("resource.check_update: 清空缓存后需要更新", "服务器不可达")
        T.skip("resource.check_update: 无参数检查所有已缓存资源", "服务器不可达")
    end

    -----------------------------------------------------------------------
    -- 7. upload: 上传资源
    -----------------------------------------------------------------------
    if online then
        T.test("resource.upload: 上传测试资源", function()
            local test_data = "-- upload test from test_resource.lua\nreturn 'hello'"
            local ok, err = resource.upload(
                "test/upload_test.lua",  -- 上传路径
                test_data,               -- 文件内容
                "自动化测试上传"          -- 描述
            )
            T.assert_true(ok, "上传应成功")
            T.log("  上传成功: test/upload_test.lua (" .. #test_data .. " bytes)")
        end)

        T.test("resource.upload + query: 上传后可查询", function()
            local info = resource.query("test/upload_test.lua")
            T.assert_not_nil(info, "上传后应可查询")
            T.assert_eq(info.path, "test/upload_test.lua")
            T.log("  验证: path=" .. tostring(info.path) .. " size=" .. tostring(info.size))
        end)
    else
        T.skip("resource.upload: 上传测试资源", "服务器不可达")
        T.skip("resource.upload + query: 上传后可查询", "服务器不可达")
    end

    -----------------------------------------------------------------------
    -- 8. install_searcher + require 懒加载
    -----------------------------------------------------------------------
    if online then
        T.test("resource.install_searcher: 安装自定义搜索器", function()
            local count_before = #package.searchers
            resource.install_searcher()
            T.assert_eq(#package.searchers, count_before + 1, "搜索器数量应+1")
            T.log("  搜索器数量: " .. #package.searchers)
        end)

        T.test("require 懒加载: 通过搜索器从服务器下载模块", function()
            resource.clear_cache()
            -- 清除已加载模块缓存
            local mod_name = "test/mymodule"
            package.loaded[mod_name] = nil

            T.log("  尝试 require('" .. mod_name .. "')...")
            local ok, result = pcall(require, mod_name)
            if ok then
                T.log("  加载成功, type=" .. type(result))
                if type(result) == "table" then
                    for k, v in pairs(result) do
                        T.log("    " .. tostring(k) .. " = " .. tostring(v))
                    end
                end
            else
                -- require 失败不一定是 bug (可能 .luac 不是合法 Lua 字节码等)
                T.log("  require 失败: " .. tostring(result))
                error("require 懒加载失败: " .. tostring(result))
            end
        end)
    else
        T.skip("resource.install_searcher: 安装自定义搜索器", "服务器不可达")
        T.skip("require 懒加载: 通过搜索器从服务器下载模块", "服务器不可达")
    end

    -----------------------------------------------------------------------
    -- 9. load_script: 下载并直接执行
    -----------------------------------------------------------------------
    if online then
        T.test("resource.load_script: 下载并执行脚本", function()
            resource.clear_cache()
            local ok, err = resource.load_script(TEST_RESOURCE)
            if ok then
                T.log("  执行成功")
            else
                T.log("  执行失败: " .. tostring(err))
                error("load_script 失败: " .. tostring(err))
            end
        end)
    else
        T.skip("resource.load_script: 下载并执行脚本", "服务器不可达")
    end

    -----------------------------------------------------------------------
    -- 10. clear_cache: 缓存清理
    -----------------------------------------------------------------------
    T.test("resource.clear_cache: 清空所有缓存", function()
        resource.clear_cache()
        -- 清空后 check_update 应报告所有资源需要更新
        -- (但由于 hash 记录也被清空，无参调用返回空列表)
        if online then
            local updates = resource.check_update()
            T.assert_type(updates, "table")
            T.assert_eq(#updates, 0, "hash 记录清空后无参 check_update 应返回空")
        end
    end)

    -----------------------------------------------------------------------
    -- 11. 错误处理: 未设置认证时的行为
    -----------------------------------------------------------------------
    T.test("错误处理: 参数缺失不崩溃", function()
        -- 重新初始化但不设置 auth (模拟未配置)
        resource.init(CACHE_DIR)
        -- 这些调用不应导致崩溃 (应返回 nil + 错误信息)
        -- 注: 实际行为取决于 C++ 实现是否允许空 auth 调用
    end)

    -- 恢复认证 (供后续测试使用)
    resource.set_auth(TEST_OWNER, TEST_KEY, TEST_PASS)

    return T.report("resource")
end

return { run = run }
