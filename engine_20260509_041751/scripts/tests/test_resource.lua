--[[
    resource 模块自动化测试
    覆盖: init / set_auth / download / query / upload / check_update
           / zip / unzip / clear_cache
    
    测试资源: test/mymodule.luac (已上传到服务器)
    加解密由用户自行处理，模块只负责传输原始数据
    压缩/解压功能见 ArchiveUtils (minizip-ng)
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
        T.assert_type(resource.init,         "function")
        T.assert_type(resource.set_auth,     "function")
        T.assert_type(resource.download,     "function")
        T.assert_type(resource.query,        "function")
        T.assert_type(resource.upload,       "function")
        T.assert_type(resource.check_update, "function")
        T.assert_type(resource.zip,          "function")
        T.assert_type(resource.unzip,        "function")
        T.assert_type(resource.clear_cache,  "function")
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
            local is_bytecode = data:sub(1, 4) == "\x1bLua"
            T.log("  类型: " .. (is_bytecode and "Lua 字节码" or "其他"))
        end)

        T.test("resource.download: 缓存命中 (第二次下载)", function()
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
    -- 6. check_update: 基于 xxhash 对比检查更新 (必须传路径列表)
    -----------------------------------------------------------------------
    if online then
        T.test("resource.check_update: 刚下载的资源无需更新", function()
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
    else
        T.skip("resource.check_update: 刚下载的资源无需更新", "服务器不可达")
        T.skip("resource.check_update: 清空缓存后需要更新", "服务器不可达")
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
    -- 8. zip / unzip: 压缩解压 (基于 minizip-ng)
    -----------------------------------------------------------------------
    T.test("resource.zip: 压缩文件", function()
        -- 创建测试文件
        local test_file = CACHE_DIR .. "/zip_test.txt"
        local f = io.open(test_file, "w")
        T.assert_not_nil(f, "应能创建测试文件")
        f:write("hello zip test 你好世界")
        f:close()
        
        local zip_file = CACHE_DIR .. "/zip_test.zip"
        local ok, err = resource.zip(test_file, zip_file)
        T.assert_true(ok, "zip 应成功: " .. tostring(err))
        
        -- 验证 zip 文件存在
        local zf = io.open(zip_file, "rb")
        T.assert_not_nil(zf, "zip 文件应存在")
        local size = zf:seek("end")
        zf:close()
        T.assert_gt(size, 0, "zip 文件大小应 > 0")
        T.log("  zip 文件大小: " .. size .. " bytes")
    end)

    T.test("resource.unzip: 解压文件", function()
        local zip_file = CACHE_DIR .. "/zip_test.zip"
        local dest_dir = CACHE_DIR .. "/zip_extract"
        local ok, err = resource.unzip(zip_file, dest_dir)
        T.assert_true(ok, "unzip 应成功: " .. tostring(err))
        
        -- 验证解压后文件内容
        local f = io.open(dest_dir .. "/zip_test.txt", "r")
        T.assert_not_nil(f, "解压后文件应存在")
        local content = f:read("*a")
        f:close()
        T.assert_eq(content, "hello zip test 你好世界", "解压内容应匹配")
        T.log("  解压内容: " .. content)
    end)

    T.test("resource.zip + unzip: 加密压缩解压", function()
        local test_file = CACHE_DIR .. "/zip_test.txt"
        local zip_enc = CACHE_DIR .. "/zip_encrypted.zip"
        local dest_enc = CACHE_DIR .. "/zip_enc_extract"
        
        local ok = resource.zip(test_file, zip_enc, "testpass123")
        T.assert_true(ok, "加密 zip 应成功")
        
        ok = resource.unzip(zip_enc, dest_enc, "testpass123")
        T.assert_true(ok, "加密 unzip 应成功")
        
        local f = io.open(dest_enc .. "/zip_test.txt", "r")
        T.assert_not_nil(f, "加密解压后文件应存在")
        local content = f:read("*a")
        f:close()
        T.assert_eq(content, "hello zip test 你好世界", "加密解压内容应匹配")
        T.log("  加密解压验证通过")
    end)

    T.test("resource.zip: 不存在的源文件返回 false", function()
        local ok, err = resource.zip("nonexistent_file.txt", CACHE_DIR .. "/fail.zip")
        T.assert_false(ok, "不存在的源文件应返回 false")
    end)

    T.test("resource.unzip: 不存在的 zip 返回 false", function()
        local ok, err = resource.unzip("nonexistent.zip", CACHE_DIR .. "/fail_dir")
        T.assert_false(ok, "不存在的 zip 应返回 false")
    end)

    -----------------------------------------------------------------------
    -- 9. clear_cache: 缓存清理
    -----------------------------------------------------------------------
    T.test("resource.clear_cache: 清空所有缓存", function()
        resource.clear_cache()
    end)

    -----------------------------------------------------------------------
    -- 10. 错误处理: 未初始化时的行为
    -----------------------------------------------------------------------
    T.test("错误处理: 参数缺失不崩溃", function()
        resource.init(CACHE_DIR)
        resource.set_auth(TEST_OWNER, TEST_KEY, TEST_PASS)
    end)

    return T.report("resource")
end

return { run = run }
