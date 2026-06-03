--[[
    grid 模块测试 - 二值化网格地图寻路
    覆盖: 加载CSV/保存gmap/查询/寻路/坐标映射等
]]
local T = require("tests.test_framework")

local script_dir = debug.getinfo(1, "S").source:match("@(.+[\\/])") or ""
-- 项目根目录 (scripts/tests/ 上两级)
local project_root = script_dir:match("(.+[\\/])scripts[\\/]tests[\\/]") or ""

local function run()
    T.reset()
    T.log("\n=== grid 模块测试 ===")

    --------------------------
    -- API 存在性测试
    --------------------------
    T.log("\n--- API 存在性 ---")

    T.test("grid.load: 函数存在", function()
        T.assert_type(grid.load, "function")
    end)

    T.test("grid.load_string: 函数存在", function()
        T.assert_type(grid.load_string, "function")
    end)

    T.test("grid.find_path: 函数存在", function()
        T.assert_type(grid.find_path, "function")
    end)

    T.test("grid.info: 函数存在", function()
        T.assert_type(grid.info, "function")
    end)

    T.test("grid.is_walkable: 函数存在", function()
        T.assert_type(grid.is_walkable, "function")
    end)

    T.test("grid.save: 函数存在", function()
        T.assert_type(grid.save, "function")
    end)

    T.test("grid.list: 函数存在", function()
        T.assert_type(grid.list, "function")
    end)

    --------------------------
    -- CSV 字符串加载测试
    --------------------------
    T.log("\n--- CSV 字符串加载 ---")

    T.test("grid.load_string: 小地图", function()
        local csv = "0,0,0,1,1\n0,0,0,1,1\n0,0,0,0,0\n1,1,0,0,0\n1,1,0,0,0"
        local ok = grid.load_string("test_small", csv)
        T.assert_true(ok, "load_string 应成功")
    end)

    T.test("grid.info: 查看小地图信息", function()
        local info = grid.info("test_small")
        T.assert_not_nil(info, "info 不应为 nil")
        T.assert_eq(info.width, 5, "宽度应为 5")
        T.assert_eq(info.height, 5, "高度应为 5")
        T.log(string.format("  %dx%d, walkable=%d", info.width, info.height, info.walkable_count))
    end)

    T.test("grid.is_walkable: 通行格", function()
        T.assert_true(grid.is_walkable("test_small", 0, 0))
        T.assert_true(grid.is_walkable("test_small", 2, 2))
    end)

    T.test("grid.is_walkable: 障碍格", function()
        T.assert_false(grid.is_walkable("test_small", 3, 0))
        T.assert_false(grid.is_walkable("test_small", 0, 3))  -- (0,3) 是障碍
    end)

    T.test("grid.is_walkable: 越界", function()
        T.assert_false(grid.is_walkable("test_small", -1, 0))
        T.assert_false(grid.is_walkable("test_small", 5, 0))
    end)

    --------------------------
    -- 寻路测试 (小地图)
    --------------------------
    T.log("\n--- 寻路 (小地图) ---")

    T.test("grid.find_path: 直线路径", function()
        local path = grid.find_path("test_small", 0, 0, 2, 0)
        T.assert_not_nil(path, "应找到路径")
        T.assert_gt(#path, 0, "路径不应为空")
        T.assert_eq(path[1].x, 0, "起点 x")
        T.assert_eq(path[1].y, 0, "起点 y")
        T.assert_eq(path[#path].x, 2, "终点 x")
        T.assert_eq(path[#path].y, 0, "终点 y")
        T.log(string.format("  路径长度: %d 步", #path))
    end)

    T.test("grid.find_path: 绕过障碍", function()
        local path = grid.find_path("test_small", 0, 0, 4, 4)
        T.assert_not_nil(path, "应找到路径")
        T.assert_gt(#path, 2, "绕障碍路径应 > 2 步")
        T.log(string.format("  路径长度: %d 步", #path))
    end)

    T.test("grid.find_path: 同一点", function()
        local path = grid.find_path("test_small", 0, 0, 0, 0)
        T.assert_not_nil(path)
        T.assert_eq(#path, 1)
    end)

    T.test("grid.find_path: 不可达", function()
        -- 创建一个分隔的地图
        local csv = "0,1,0\n0,1,0\n0,1,0"
        grid.load_string("test_split", csv)
        local path = grid.find_path("test_split", 0, 0, 2, 0)
        T.assert_nil(path, "分隔地图应无路径")
    end)

    T.test("grid.find_path: 起点是障碍", function()
        local path = grid.find_path("test_small", 3, 0, 0, 0)
        T.assert_nil(path, "起点障碍应返回 nil")
    end)

    --------------------------
    -- 保存/加载 .gmap 测试
    --------------------------
    T.log("\n--- 保存/加载 .gmap ---")

    T.test("grid.save: 保存为 .gmap", function()
        local ok = grid.save("test_small", "test_grid_output.gmap")
        T.assert_true(ok, "保存应成功")
    end)

    T.test("grid.load: 从 .gmap 加载", function()
        local ok = grid.load("test_reload", "test_grid_output.gmap")
        T.assert_true(ok, "加载 .gmap 应成功")

        local info = grid.info("test_reload")
        T.assert_not_nil(info)
        T.assert_eq(info.width, 5)
        T.assert_eq(info.height, 5)
        T.log(string.format("  重新加载: %dx%d", info.width, info.height))
    end)

    --------------------------
    -- 真实大地图测试 (map-4.gmap)
    --------------------------
    T.log("\n--- 真实大地图 (map-4.gmap) ---")

    local gmap_path = project_root .. "map/map-4.gmap"
    T.test("grid.load: 加载 .gmap 大地图", function()
        local ok = grid.load("map4", gmap_path)
        T.assert_true(ok, "加载 .gmap 应成功")

        local info = grid.info("map4")
        T.assert_not_nil(info)
        T.assert_gt(info.width, 0)
        T.assert_gt(info.height, 0)
        T.log(string.format("  地图: %dx%d, 可通行: %d (%.1f%%)",
            info.width, info.height, info.walkable_count,
            info.walkable_count / (info.width * info.height) * 100))
    end)

    T.test("grid.find_path: 游戏坐标寻路", function()
        local info = grid.info("map4")
        if not info then
            T.log("  [跳过] 地图未加载")
            return
        end

        -- 设置原点
        grid.set_origin("map4", 32512, 32128, 0)
        grid.set_cell_size("map4", 1)

        -- 直接使用游戏坐标寻路
        local t1 = sys.tick()
        local path = grid.find_path("map4", 33048, 32322, 32611, 32727)
        local t2 = sys.tick()
        local elapsed_ms = (t2 - t1) / 1000.0

        T.assert_not_nil(path, "应找到路径")
        T.assert_gt(#path, 0, "路径不应为空")
        -- 返回的是游戏坐标
        T.assert_type(path[1].x, "number")
        T.assert_type(path[1].y, "number")
        T.log(string.format("  路径: %d 步, 耗时: %.2f ms, 首点(%.0f,%.0f)",
            #path, elapsed_ms, path[1].x, path[1].y))
        T.assert_true(elapsed_ms < 100, "寻路应在 100ms 内完成")
    end)

    T.test("grid.get_origin: 获取原点", function()
        local ox, oy = grid.get_origin("map4")
        T.assert_not_nil(ox)
        T.assert_eq(ox, 32512, "原点X")
        T.assert_eq(oy, 32128, "原点Y")
        T.log(string.format("  原点: (%.0f, %.0f)", ox, oy))
    end)

    --------------------------
    -- 管理功能测试
    --------------------------
    T.log("\n--- 管理功能 ---")

    T.test("grid.list: 获取已加载地图", function()
        local list = grid.list()
        T.assert_type(list, "table")
        T.assert_gt(#list, 0)
        T.log("  已加载: " .. table.concat(list, ", "))
    end)

    T.test("grid.unload: 卸载地图", function()
        local ok = grid.unload("test_small")
        T.assert_true(ok)
        T.assert_nil(grid.info("test_small"))
    end)

    T.test("grid.unload_all: 卸载所有", function()
        grid.unload_all()
        local list = grid.list()
        T.assert_eq(#list, 0)
    end)

    return T.report("grid")
end

return { run = run }
