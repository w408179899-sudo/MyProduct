--[[
    test_check_update.lua
    覆盖 resource.check_update / download / query 的完整测试
    
    注意:
    - resource.init() 只在首次调用时加载 hashes.json，后续调用是 no-op
    - 同一进程内 m_hashCache 是内存缓存，磁盘篡改不影响当前会话
    - 跨会话场景 (重启后 hashes.json 是否正确加载) 需要两次运行验证
]]

local passed, failed = 0, 0

local function check(name, condition)
    if condition then
        passed = passed + 1
        log.info("  PASS: " .. name)
    else
        failed = failed + 1
        log.error("  FAIL: " .. name)
    end
end

log.info("========================================")
log.info("  Resource CheckUpdate & Download Tests")
log.info("========================================")

resource.init("./cache")
resource.set_auth("admin", "admin", "")

local test_path = "test/mymodule.luac"

---------------------------------------------------------------
-- Test 1: 清空缓存后, check_update 应报告需要更新
---------------------------------------------------------------
log.info("")
log.info("[Test 1] clear_cache -> check_update (应需要更新)")
resource.clear_cache()
local u1 = resource.check_update({test_path})
check("清空后 check_update 报告需要更新", #u1 == 1)

---------------------------------------------------------------
-- Test 2: 下载文件后, check_update 应报告无需更新
---------------------------------------------------------------
log.info("")
log.info("[Test 2] download -> check_update (应无需更新)")
local data2, err2 = resource.download(test_path)
check("download 返回数据", data2 ~= nil and #data2 > 0)

local u2 = resource.check_update({test_path})
check("下载后 check_update 无需更新", #u2 == 0)

---------------------------------------------------------------
-- Test 3: 连续 check_update 不应改变结果
---------------------------------------------------------------
log.info("")
log.info("[Test 3] 连续两次 check_update (结果应一致)")
local u3a = resource.check_update({test_path})
local u3b = resource.check_update({test_path})
check("两次 check_update 结果一致", #u3a == #u3b)
check("均为无需更新", #u3a == 0 and #u3b == 0)

---------------------------------------------------------------
-- Test 4: hashes.json 正确写入
---------------------------------------------------------------
log.info("")
log.info("[Test 4] hashes.json 验证")
local f4 = io.open("./cache/hashes.json", "r")
local content4 = f4 and f4:read("*a") or ""
if f4 then f4:close() end
check("hashes.json 存在且非空", #content4 > 0)
check("hashes.json 不是 null", content4 ~= "null")
check("hashes.json 包含测试路径", content4:find(test_path, 1, true) ~= nil)
log.info("  hashes.json = " .. content4)

---------------------------------------------------------------
-- Test 5: query 返回的 hash 与缓存一致
---------------------------------------------------------------
log.info("")
log.info("[Test 5] query hash vs download hash 一致性")
local info5 = resource.query(test_path)
check("query 返回结果", info5 ~= nil)
if info5 then
    log.info("  query xxhash:  " .. tostring(info5.xxhash))
    -- hashes.json 里的 hash 应该和 query 一致
    local hash_in_cache = content4:match('"' .. test_path:gsub("[%.%/]", "%%%0") .. '"%s*:%s*"([^"]+)"')
    log.info("  cached xxhash: " .. tostring(hash_in_cache))
    check("query hash == cached hash", info5.xxhash == hash_in_cache)
end

---------------------------------------------------------------
-- Test 6: 删除缓存文件但内存 hash 仍有效
---------------------------------------------------------------
log.info("")
log.info("[Test 6] 删除磁盘缓存文件, 内存 hash 仍有效")
os.remove("./cache/test/mymodule.luac")
local u6 = resource.check_update({test_path})
check("文件删除后 check_update 仍无需更新 (内存缓存)", #u6 == 0)

---------------------------------------------------------------
-- Test 7: 重复下载同一文件
---------------------------------------------------------------
log.info("")
log.info("[Test 7] 重复下载同一文件")
local data7a = resource.download(test_path)
local data7b = resource.download(test_path)
check("两次下载均成功", data7a ~= nil and data7b ~= nil)
check("两次下载数据长度一致", data7a and data7b and #data7a == #data7b)
check("两次下载数据内容一致", data7a == data7b)
local u7 = resource.check_update({test_path})
check("重复下载后 check_update 无需更新", #u7 == 0)

---------------------------------------------------------------
-- Test 8: 下载不存在的资源
---------------------------------------------------------------
log.info("")
log.info("[Test 8] 下载不存在的资源")
local data8, err8 = resource.download("nonexistent/does_not_exist_12345.lua")
check("不存在的资源返回 nil", data8 == nil)
check("返回错误信息", err8 ~= nil)
log.info("  错误: " .. tostring(err8))

---------------------------------------------------------------
-- Test 9: check_update 对不存在的资源
---------------------------------------------------------------
log.info("")
log.info("[Test 9] check_update 对不存在的资源")
local u9 = resource.check_update({"nonexistent/does_not_exist_12345.lua"})
-- query 会失败, 跳过该资源, 返回空列表
check("不存在资源 check_update 返回空", #u9 == 0)

---------------------------------------------------------------
-- Test 10: check_update 无参数 (检查所有已缓存资源)
---------------------------------------------------------------
log.info("")
log.info("[Test 10] check_update 无参数 (检查全部缓存)")
local u10 = resource.check_update()
check("无参数 check_update 返回 table", type(u10) == "table")
log.info("  全部缓存中需更新: " .. #u10 .. " 个")

---------------------------------------------------------------
-- Test 11: query 元数据完整性
---------------------------------------------------------------
log.info("")
log.info("[Test 11] query 返回元数据完整性")
local info11 = resource.query(test_path)
check("query 返回 table", type(info11) == "table")
if info11 then
    check("path 非空", info11.path ~= nil and #info11.path > 0)
    check("file_name 非空", info11.file_name ~= nil and #info11.file_name > 0)
    check("size > 0", info11.size > 0)
    check("xxhash 非空", info11.xxhash ~= nil and #info11.xxhash > 0)
    log.info("  path:      " .. tostring(info11.path))
    log.info("  file_name: " .. tostring(info11.file_name))
    log.info("  size:      " .. tostring(info11.size))
    log.info("  xxhash:    " .. tostring(info11.xxhash))
    log.info("  upload:    " .. tostring(info11.upload_time))
end

---------------------------------------------------------------
-- Test 12: clear_cache 后再下载, 完整周期
---------------------------------------------------------------
log.info("")
log.info("[Test 12] 完整生命周期: clear -> check(需更新) -> download -> check(无需更新)")
resource.clear_cache()
local u12a = resource.check_update({test_path})
check("清空后需要更新", #u12a == 1)

local data12 = resource.download(test_path)
check("下载成功", data12 ~= nil)

local u12b = resource.check_update({test_path})
check("下载后无需更新", #u12b == 0)

---------------------------------------------------------------
-- Test 13: reload_cache 无需重启加载磁盘缓存
---------------------------------------------------------------
log.info("")
log.info("[Test 13] reload_cache: 篡改磁盘 -> reload -> check_update")
-- 当前内存中有正确 hash, 篡改磁盘上的 hashes.json
local f13 = io.open("./cache/hashes.json", "w")
if f13 then
    f13:write('{"test/mymodule.luac": "0000000000000000"}')
    f13:close()
end
-- reload_cache 重新从磁盘加载 (覆盖内存缓存)
resource.reload_cache()
local u13 = resource.check_update({test_path})
check("reload 后检测到伪造 hash 不一致, 需要更新", #u13 == 1)

-- 恢复: 重新下载写入正确 hash
resource.download(test_path)
local u13b = resource.check_update({test_path})
check("重新下载后无需更新", #u13b == 0)

---------------------------------------------------------------
-- Test 14: 并发 CheckUpdate 性能 (多文件)
---------------------------------------------------------------
log.info("")
log.info("[Test 14] 并发 CheckUpdate 性能")
-- 用同一个文件模拟, 主要看耗时
local t0 = os.clock()
local multi_paths = {}
for i = 1, 5 do
    multi_paths[i] = test_path
end
local u14 = resource.check_update(multi_paths)
local elapsed = os.clock() - t0
check("多路径 check_update 返回 table", type(u14) == "table")
log.info(string.format("  5 路径 check_update 耗时: %.3f 秒", elapsed))

---------------------------------------------------------------
-- 汇总 (不清理缓存, 保留供检查)
---------------------------------------------------------------
log.info("")
log.info("========================================")
log.info(string.format("  结果: %d passed, %d failed, %d total", passed, failed, passed + failed))
if failed == 0 then
    log.info("  ALL TESTS PASSED")
else
    log.error("  SOME TESTS FAILED")
end
log.info("========================================")
