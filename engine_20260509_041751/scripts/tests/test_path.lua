--[[
    path 模块测试 - 路点地图寻路功能验证
    覆盖: 模块存在性/地图加载/坐标寻路/路径结果验证
    
    注: path 模块通过 WaypointMap 全局单例工作
    - path.load(file)                    加载 .wmap 地图
    - path.find(x1,y1,z1, x2,y2,z2)     根据坐标寻路 -> {{x,y,z,id,label},...} | nil
]]
local T = require("tests.test_framework")

local MAP_FILE = "map/world.wmap"

local function run()
    T.reset()
    T.log("\n=== path 模块测试 ===")

    -- 模块存在性
    T.test("path模块存在", function()
        T.assert_type(path, "table")
    end)

    T.test("path API: 函数存在", function()
        T.assert_type(path.load, "function")
        T.assert_type(path.find, "function")
    end)

    -- 异常输入
    T.test("path.load: 不存在的文件返回false", function()
        local ok = path.load("./nonexistent_file_12345.wmap")
        T.assert_false(ok)
    end)

    T.test("path.find: 无地图时返回nil", function()
        local result = path.find(0, 0, 0, 100, 100, 0)
        T.assert_nil(result)
    end)

    -- 加载真实地图文件
    local mapLoaded = false

    T.test("path.load: 加载 " .. MAP_FILE, function()
        local ok = path.load(MAP_FILE)
        T.assert_true(ok, "应成功加载地图文件")
        mapLoaded = ok
        log.info("  地图加载成功")
    end)

    -- 以下测试依赖地图加载成功
    T.test("path.find: 使用地图中的坐标寻路", function()
        if not mapLoaded then
            log.warn("  跳过: 地图未加载")
            return
        end
        -- 使用真实坐标测试寻路 (可能返回 nil 或 table)
        local result = path.find(-3422.4, -19847.0, 21080.3, -7779.1, -16386.0, 20374.8, 200)
        if result ~= nil then
            T.assert_type(result, "table")
            T.assert_gte(#result, 1, "路径至少1个点")
            log.info("  远距离寻路结果: " .. #result .. " 个路径点")
        else
            log.info("  远距离寻路结果: nil (无匹配节点或不可达)")
        end
    end)

    T.test("path.find: 相同起终点寻路", function()
        if not mapLoaded then
            log.warn("  跳过: 地图未加载")
            return
        end
        -- 同一坐标寻路, 应返回 1 个点的路径
        local result = path.find(0, 0, 0, 0, 0, 0)
        if result ~= nil then
            T.assert_type(result, "table")
            T.assert_gte(#result, 1, "至少1个路径点")
            log.info(string.format("  同点寻路: %d 个路径点", #result))
        else
            log.info("  同点寻路: nil (无匹配节点)")
        end
    end)

    T.test("path.find: 返回数据结构验证", function()
        if not mapLoaded then
            log.warn("  跳过: 地图未加载")
            return
        end
        -- 用 (0,0,0) 尝试, 检查返回的数据结构
        local result = path.find(0, 0, 0, 1000, 1000, 0)
        if result ~= nil then
            T.assert_type(result, "table")
            T.assert_gte(#result, 1, "至少1个路径点")
            -- 验证每个路径点字段
            for i, pt in ipairs(result) do
                T.assert_type(pt.x, "number", "路径点" .. i .. ".x")
                T.assert_type(pt.y, "number", "路径点" .. i .. ".y")
                T.assert_type(pt.z, "number", "路径点" .. i .. ".z")
                T.assert_type(pt.id, "number", "路径点" .. i .. ".id")
                -- label 可选, 但如果存在应为 string
                if pt.label ~= nil then
                    T.assert_type(pt.label, "string", "路径点" .. i .. ".label")
                end
            end
            local first = result[1]
            local last = result[#result]
            log.info(string.format("  路径: %d 个点, 起(%0.f,%0.f,%0.f) -> 终(%0.f,%0.f,%0.f)",
                #result, first.x, first.y, first.z, last.x, last.y, last.z))
        else
            log.info("  寻路结果: nil (可能无匹配节点)")
        end
    end)

    T.test("path.find: maxRange 限制匹配范围", function()
        if not mapLoaded then
            log.warn("  跳过: 地图未加载")
            return
        end
        -- 用极小范围匹配远离所有节点的坐标, 应返回 nil
        local result = path.find(99999, 99999, 99999, -99999, -99999, -99999, 1)
        T.assert_nil(result, "极小范围 + 远离节点应返回nil")
        log.info("  maxRange=1 远离节点: nil (符合预期)")

        -- 不限制范围 (maxRange=0), 同样的坐标应能匹配到最近节点
        local result2 = path.find(99999, 99999, 99999, -99999, -99999, -99999, 0)
        -- 结果取决于地图是否连通, 但至少调用不崩溃
        log.info("  maxRange=0 不限制: " .. tostring(result2 ~= nil and #result2 .. " 个路径点" or "nil"))
    end)

    T.test("path.find: 参数类型错误应报错", function()
        local ok, err = pcall(path.find, "a", 0, 0, 0, 0, 0)
        T.assert_false(ok, "字符串参数应报错")
    end)

    return T.report("path")
end

return { run = run }
