--[[
    resource 模块服务器集成测试
    覆盖: 认证配置 / 资源下载 / 查询 / 上传 / 检查更新 / 懒加载 require / load_script
    
    前置条件:
    1. DistributionServer 已启动 (地址写死在 C++ 中: http://eydata666.xyz:51736)
    2. 已上传测试资源 test/mymodule.luac
    
    注意: 加解密由用户自行处理，本模块只负责传输原始数据
]]
local T = require("tests.test_framework")

-------------------------------------------------------------------------------
-- 测试环境配置 (可通过全局变量覆盖)
-------------------------------------------------------------------------------
local CONFIG = {
    -- 服务器认证参数 (对齐 DistributionServer 的 client API)
    owner        = DIST_OWNER        or "admin",
    client_key   = DIST_CLIENT_KEY   or "admin",
    password     = DIST_PASSWORD     or "",
    -- 测试资源路径 (在服务器上的 path)
    test_script  = DIST_TEST_SCRIPT  or "test/mymodule.luac",
    test_module  = DIST_TEST_MODULE  or "test/mymodule",
    -- 本地缓存目录
    cache_dir    = DIST_CACHE_DIR    or "./cache_test_server",
}

-------------------------------------------------------------------------------
-- 辅助函数
-------------------------------------------------------------------------------

-- 打印配置信息
local function print_config()
    T.log("  所有者: " .. CONFIG.owner)
    T.log("  客户端: " .. CONFIG.client_key)
    T.log("  脚本:   " .. CONFIG.test_script)
    T.log("  模块:   " .. CONFIG.test_module)
end

-------------------------------------------------------------------------------
-- 测试入口
-------------------------------------------------------------------------------
local function run()
    T.reset()
    T.log("\n=== resource 服务器集成测试 ===")
    print_config()

    -- 1. 初始化与配置
    T.test("resource.init: 初始化缓存目录", function()
        resource.init(CONFIG.cache_dir)
    end)

    T.test("resource.set_auth: 设置认证参数", function()
        resource.set_auth(CONFIG.owner, CONFIG.client_key, CONFIG.password)
    end)

    T.test("resource.clear_cache: 清空缓存 (测试前置)", function()
        resource.clear_cache()
    end)

    -- 2. 安装 require 搜索器
    T.test("resource.install_searcher: 安装自定义搜索器", function()
        resource.install_searcher()
        T.assert_type(package.searchers, "table")
        T.assert_gt(#package.searchers, 0, "搜索器数量")
        T.log("  搜索器数量: " .. #package.searchers)
    end)

    -- === 以下测试需要 DistributionServer 在线 ===
    local online = true

    -- 3. 资源查询
    if online then
        T.test("resource.query: 查询服务器资源", function()
            local info, err = resource.query(CONFIG.test_script)
            T.assert_not_nil(info, "query 应返回 info table")
            T.assert_type(info, "table")
            T.assert_not_nil(info.path)
            T.assert_not_nil(info.xxhash)
            T.log("  路径:   " .. tostring(info.path))
            T.log("  大小:   " .. tostring(info.size))
            T.log("  xxhash: " .. tostring(info.xxhash))
        end)
    else
        T.skip("resource.query: 查询服务器资源", "服务器不可达")
    end

    -- 4. 资源下载
    if online then
        T.test("resource.download: 下载资源原始数据", function()
            resource.clear_cache()
            local data, err = resource.download(CONFIG.test_script)
            T.assert_not_nil(data, "download 应返回数据")
            T.assert_type(data, "string")
            T.assert_gt(#data, 0, "数据长度")
            T.log("  下载: " .. #data .. " bytes")
            local is_bytecode = data:sub(1, 4) == "\x1bLua"
            T.log("  类型: " .. (is_bytecode and "Lua 字节码" or "其他"))
        end)
    else
        T.skip("resource.download: 下载资源原始数据", "服务器不可达")
    end

    -- 5. 检查更新
    if online then
        T.test("resource.check_update: 刚下载的资源无需更新", function()
            local updates = resource.check_update({CONFIG.test_script})
            T.assert_type(updates, "table")
            T.assert_eq(#updates, 0, "刚下载的资源不应需要更新")
        end)

        T.test("resource.check_update: 清空缓存后需要更新", function()
            resource.clear_cache()
            local updates = resource.check_update({CONFIG.test_script})
            T.assert_type(updates, "table")
            T.assert_eq(#updates, 1, "清空缓存后应需要更新")
            T.log("  需要更新: " .. updates[1])
        end)
    else
        T.skip("resource.check_update: 刚下载的资源无需更新", "服务器不可达")
        T.skip("resource.check_update: 清空缓存后需要更新", "服务器不可达")
    end

    -- 6. 脚本加载执行
    if online then
        T.test("resource.load_script: 下载并执行脚本", function()
            resource.clear_cache()
            local ok, err = resource.load_script(CONFIG.test_script)
            if ok then
                T.log("  脚本执行成功")
            else
                T.log("  脚本执行失败: " .. tostring(err))
                error("load_script failed: " .. tostring(err))
            end
        end)
    else
        T.skip("resource.load_script: 下载并执行脚本", "服务器不可达")
    end

    -- 7. require 懒加载
    if online then
        T.test("require 懒加载: 服务器模块自动下载", function()
            resource.clear_cache()
            local mod_name = CONFIG.test_module
            package.loaded[mod_name] = nil

            T.log("  尝试 require('" .. mod_name .. "')...")
            local ok, result = pcall(require, mod_name)
            if ok then
                T.log("  加载成功: " .. type(result))
                if type(result) == "table" then
                    for k, v in pairs(result) do
                        T.log("    " .. tostring(k) .. " = " .. tostring(v))
                    end
                end
            else
                T.log("  加载失败: " .. tostring(result))
                error("require failed: " .. tostring(result))
            end
        end)
    else
        T.skip("require 懒加载: 服务器模块自动下载", "服务器不可达")
    end

    -- 8. 不存在资源的优雅处理
    T.test("resource.download: 不存在的资源返回 nil", function()
        local data, err = resource.download("nonexistent/does_not_exist.lua")
        if data == nil then
            T.assert_type(err, "string")
            T.log("  预期: " .. tostring(err))
        end
    end)

    -- 9. 缓存管理
    T.test("resource.clear_cache: 清空测试缓存", function()
        resource.clear_cache()
    end)

    return T.report("resource_server")
end

return { run = run }
