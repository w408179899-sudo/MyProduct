--[[
    config 模块测试 - 配置文件读写功能
    覆盖: 加载/保存/读取/写入/删除/嵌套key/数组/对象等
    注意: 配置文件路径固定为 ./script_config.json，不允许 Lua 修改
]]
local T = require("tests.test_framework")

local function run()
    T.reset()
    T.log("\n=== config 模块测试 ===")
    
    -- 测试前清理
    config.clear()
    
    -- ==================== 基础功能测试 ====================
    
    T.test("config.load: 函数存在", function()
        T.assert_type(config.load, "function")
    end)
    
    T.test("config.save: 函数存在", function()
        T.assert_type(config.save, "function")
    end)
    
    T.test("config.get: 函数存在", function()
        T.assert_type(config.get, "function")
    end)
    
    T.test("config.set: 函数存在", function()
        T.assert_type(config.set, "function")
    end)
    
    T.test("config.delete: 函数存在", function()
        T.assert_type(config.delete, "function")
    end)
    
    T.test("config.exists: 函数存在", function()
        T.assert_type(config.exists, "function")
    end)
    
    T.test("config.keys: 函数存在", function()
        T.assert_type(config.keys, "function")
    end)
    
    T.test("config.clear: 函数存在", function()
        T.assert_type(config.clear, "function")
    end)
    
    T.test("config.get_all: 函数存在", function()
        T.assert_type(config.get_all, "function")
    end)
    
    T.test("config.set_all: 函数存在", function()
        T.assert_type(config.set_all, "function")
    end)
    
    -- ==================== 基本类型读写测试 ====================
    
    config.clear()
    
    T.test("config.set/get: 字符串类型", function()
        config.set("test_string", "hello world")
        local val = config.get("test_string")
        T.assert_eq(val, "hello world")
    end)
    
    T.test("config.set/get: 整数类型", function()
        config.set("test_int", 12345)
        local val = config.get("test_int")
        T.assert_eq(val, 12345)
    end)
    
    T.test("config.set/get: 浮点数类型", function()
        config.set("test_float", 3.14159)
        local val = config.get("test_float")
        T.assert_true(math.abs(val - 3.14159) < 0.0001)
    end)
    
    T.test("config.set/get: 布尔类型 true", function()
        config.set("test_bool_true", true)
        local val = config.get("test_bool_true")
        T.assert_eq(val, true)
    end)
    
    T.test("config.set/get: 布尔类型 false", function()
        config.set("test_bool_false", false)
        local val = config.get("test_bool_false")
        T.assert_eq(val, false)
    end)
    
    T.test("config.set/get: 数组类型", function()
        config.set("test_array", {1, 2, 3, 4, 5})
        local val = config.get("test_array")
        T.assert_type(val, "table")
        T.assert_eq(#val, 5)
        T.assert_eq(val[1], 1)
        T.assert_eq(val[5], 5)
    end)
    
    T.test("config.set/get: 对象类型", function()
        config.set("test_obj", {name = "test", value = 100})
        local val = config.get("test_obj")
        T.assert_type(val, "table")
        T.assert_eq(val.name, "test")
        T.assert_eq(val.value, 100)
    end)
    
    -- ==================== 嵌套 key 测试 ====================
    
    config.clear()
    
    T.test("config.set/get: 嵌套 key", function()
        config.set("window.width", 800)
        config.set("window.height", 600)
        
        local width = config.get("window.width")
        local height = config.get("window.height")
        
        T.assert_eq(width, 800)
        T.assert_eq(height, 600)
    end)
    
    T.test("config.set/get: 深层嵌套 key", function()
        config.set("a.b.c.d", "deep")
        local val = config.get("a.b.c.d")
        T.assert_eq(val, "deep")
    end)
    
    -- ==================== 默认值测试 ====================
    
    config.clear()
    
    T.test("config.get: 不存在的 key 返回 nil", function()
        local val = config.get("nonexistent_key")
        T.assert_nil(val)
    end)
    
    T.test("config.get: 不存在的 key 使用默认值", function()
        local val = config.get("nonexistent_key", "default_value")
        T.assert_eq(val, "default_value")
    end)
    
    T.test("config.get: 默认值为数字", function()
        local val = config.get("nonexistent_key", 42)
        T.assert_eq(val, 42)
    end)
    
    -- ==================== exists 测试 ====================
    
    config.clear()
    
    T.test("config.exists: key 存在", function()
        config.set("existing_key", "value")
        T.assert_true(config.exists("existing_key"))
    end)
    
    T.test("config.exists: key 不存在", function()
        T.assert_false(config.exists("nonexistent_key"))
    end)
    
    T.test("config.exists: 嵌套 key 存在", function()
        config.set("parent.child", "value")
        T.assert_true(config.exists("parent.child"))
    end)
    
    -- ==================== delete 测试 ====================
    
    config.clear()
    
    T.test("config.delete: 删除存在的 key", function()
        config.set("to_delete", "value")
        T.assert_true(config.exists("to_delete"))
        
        local result = config.delete("to_delete")
        T.assert_true(result)
        T.assert_false(config.exists("to_delete"))
    end)
    
    T.test("config.delete: 删除不存在的 key", function()
        local result = config.delete("nonexistent")
        T.assert_false(result)
    end)
    
    -- ==================== keys 测试 ====================
    
    config.clear()
    
    T.test("config.keys: 获取所有顶级 key", function()
        config.set("key1", "value1")
        config.set("key2", "value2")
        config.set("key3", "value3")
        
        local keys = config.keys()
        T.assert_type(keys, "table")
        T.assert_eq(#keys, 3)
    end)
    
    -- ==================== clear 测试 ====================
    
    T.test("config.clear: 清空配置", function()
        config.set("key1", "value1")
        config.set("key2", "value2")
        
        config.clear()
        
        local keys = config.keys()
        T.assert_eq(#keys, 0)
    end)
    
    -- ==================== get_all/set_all 测试 ====================
    
    config.clear()
    
    T.test("config.get_all: 获取全部配置", function()
        config.set("a", 1)
        config.set("b", 2)
        
        local all = config.get_all()
        T.assert_type(all, "table")
        T.assert_eq(all.a, 1)
        T.assert_eq(all.b, 2)
    end)
    
    T.test("config.set_all: 从 table 设置全部配置", function()
        config.clear()
        local data = {
            name = "test",
            version = 1,
            settings = {
                enabled = true,
                value = 100
            }
        }
        
        config.set_all(data)
        
        T.assert_eq(config.get("name"), "test")
        T.assert_eq(config.get("version"), 1)
        T.assert_eq(config.get("settings.enabled"), true)
        T.assert_eq(config.get("settings.value"), 100)
    end)
    
    -- ==================== save/load 测试 ====================
    
    config.clear()
    
    T.test("config.save/load: 保存并加载配置", function()
        config.set("persistent_key", "persistent_value")
        config.set("persistent_num", 999)
        
        -- 保存到文件
        local save_ok = config.save()
        T.assert_true(save_ok)
        
        -- 清空内存缓存
        config.clear()
        T.assert_nil(config.get("persistent_key"))
        
        -- 从文件加载
        local load_ok = config.load()
        T.assert_true(load_ok)
        
        -- 验证数据恢复
        T.assert_eq(config.get("persistent_key"), "persistent_value")
        T.assert_eq(config.get("persistent_num"), 999)
    end)
    
    -- ==================== 边界情况测试 ====================
    
    config.clear()
    
    T.test("config.set: 空字符串值", function()
        config.set("empty_string", "")
        local val = config.get("empty_string")
        T.assert_eq(val, "")
    end)
    
    T.test("config.set: 零值", function()
        config.set("zero", 0)
        local val = config.get("zero")
        T.assert_eq(val, 0)
    end)
    
    T.test("config.set: 负数", function()
        config.set("negative", -123)
        local val = config.get("negative")
        T.assert_eq(val, -123)
    end)
    
    T.test("config.set: 空数组", function()
        config.set("empty_array", {})
        local val = config.get("empty_array")
        T.assert_type(val, "table")
    end)
    
    T.test("config.set: 混合数组", function()
        config.set("mixed", {"a", 1, true})
        local val = config.get("mixed")
        T.assert_eq(val[1], "a")
        T.assert_eq(val[2], 1)
        T.assert_eq(val[3], true)
    end)
    
    -- 清理配置
    config.clear()
    
    return T.report("config")
end

return { run = run }
