--[[
    resource 快速诊断测试 (不依赖 test_framework, 全部输出到 log)
]]
local function L(msg) log.info("[RESTEST] " .. msg) end
local function E(msg) log.error("[RESTEST] " .. msg) end

L("=== resource 快速诊断测试 ===")

-- 2. 初始化
L("--- Step 2: 初始化 ---")
resource.init("./cache_diag")
resource.set_auth("admin", "admin", "")
-- resource.clear_cache()

-- 3. Query
L("--- Step 3: 查询服务器资源 ---")
local info, err = resource.query("test/mymodule.luac")
if info then
    L("  query OK: path=" .. tostring(info.path) .. " size=" .. tostring(info.size) .. " xxhash=" .. tostring(info.xxhash))
else
    E("  query FAIL: " .. tostring(err))
    L("=== 测试终止 (服务器不可达) ===")
    return
end

-- 4. Download
L("--- Step 4: 下载资源 ---")
local data, derr = resource.download("test/mymodule.luac")
if data then
    local dl_hash = crypto.xxhash64(data)
    L("  download OK: size=" .. #data .. " lua_xxhash=" .. dl_hash)
    L("  server_xxhash=" .. tostring(info.xxhash))
    if dl_hash == info.xxhash then
        L("  Lua侧 hash 对比: MATCH")
    else
        E("  Lua侧 hash 对比: MISMATCH! lua=" .. dl_hash .. " server=" .. info.xxhash)
    end
else
    E("  download FAIL: " .. tostring(derr))
    L("=== 测试终止 ===")
    return
end

-- 5. CheckUpdate (刚下载, 应该无需更新)
L("--- Step 5: check_update (刚下载, 应无需更新) ---")
local updates = resource.check_update({"test/mymodule.luac"})
L("  needs update: " .. #updates .. " items")
if #updates == 0 then
    L("  PASS: 刚下载的资源无需更新")
else
    E("  FAIL: 刚下载的资源仍需更新! paths=" .. table.concat(updates, ","))
end

-- 6. 清理
-- resource.clear_cache()
L("=== 测试完成 ===")
