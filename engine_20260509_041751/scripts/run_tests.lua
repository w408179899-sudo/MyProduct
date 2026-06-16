--[[
    AetherEngine 总测试入口
    整合所有模块测试，统一运行并生成报告
    
    用法:
        run_tests.lua                    -- 运行所有测试
        run_tests.lua sys proc           -- 只运行 sys 和 proc 模块测试
        run_tests.lua driver             -- 只运行 driver 模块测试
        run_tests.lua --list             -- 列出所有可用的测试模块
]]

-- 设置模块搜索路径
local script_dir = debug.getinfo(1, "S").source:match("@(.+[\\/])")
if script_dir then
    package.path = script_dir .. "?.lua;" .. package.path
end

-- 加载测试框架
local T = require("tests.test_framework")

-- 所有可用的测试模块
local all_test_modules = {
    -- 基础模块
    sys = "tests.test_sys",
    log = "tests.test_log",
    proc = "tests.test_proc",
    task = "tests.test_task",
    -- 输入模块
    keybd = "tests.test_keybd",
    mouse = "tests.test_mouse",
    wnd = "tests.test_wnd",
    -- 图像识别
    vision = "tests.test_vision",
    ocr = "tests.test_ocr",
    -- 网络与验证
    http = "tests.test_http",
    -- auth = "tests.test_auth",
    -- 工具模块
    resource = "tests.test_resource",
    crypto = "tests.test_crypto",
    hotkey = "tests.test_hotkey",
    path = "tests.test_path",
    ffi = "tests.test_ffi",
    asm = "tests.test_asm",
    disasm = "tests.test_disasm",
    config = "tests.test_config",
    encoding = "tests.test_encoding",
    -- 网格地图
    grid = "tests.test_grid",
    -- 驱动与轨迹
    driver = "tests.test_driver",
    trajectory = "tests.test_trajectory",
    aion_core = "tests.test_aion_core",
    aion_target_dump = "tests.test_aion_target_dump",
    aion_quest_snapshot = "tests.test_aion_quest_snapshot",
    aion_task_recorder = "tests.test_aion_task_recorder",
    aion_main_quest_resume = "tests.test_aion_main_quest_resume",
    aion_main_quest_20590 = "tests.test_aion_main_quest_20590",
    aion_main_quest_20610 = "tests.test_aion_main_quest_20610",
    aion_main_quest_20611 = "tests.test_aion_main_quest_20611",
    aion_main_quest_order_gate = "tests.test_aion_main_quest_order_gate",
    aion_main_quest_teleport_guard = "tests.test_aion_main_quest_teleport_guard",
    aion_leveling_combat_gate = "tests.test_aion_leveling_combat_gate",
    aion_equipment_auto = "tests.test_aion_equipment_auto",
    aion_leveling_skill_auto = "tests.test_aion_leveling_skill_auto",
    aion_leveling_skill_auto_quickbar = "tests.test_aion_leveling_skill_auto_quickbar",
    aion_login_autostart = "tests.test_aion_login_autostart",
    aion_account_runtime_guard = "tests.test_aion_account_runtime_guard",
    aion_account_limit = "tests.test_aion_account_limit",
    aion_account_profile = "tests.test_aion_account_profile",
    aion_login_flow_create_character = "tests.test_aion_login_flow_create_character",
    aion_post_kill_loot = "tests.test_aion_post_kill_loot",
    aion_attack_key_burst = "tests.test_aion_attack_key_burst",
    aion_attack_key_repeat = "tests.test_aion_attack_key_repeat",
    aion_floor_recovery = "tests.test_aion_floor_recovery",
    aion_loot = "tests.test_aion_loot"
}

-- 默认运行顺序
local default_order = {
    "sys", "log", "proc", "task",
    "keybd", "mouse", "wnd",
    "vision", "ocr",
    "http", "auth",
    "resource", "crypto", "hotkey", "path", "ffi", "asm", "disasm",
    "config", "encoding", "grid", "driver", "trajectory",
    "aion_core",
    "aion_target_dump",
    "aion_quest_snapshot",
    "aion_task_recorder",
    "aion_main_quest_resume",
    "aion_main_quest_20590",
    "aion_main_quest_20610",
    "aion_main_quest_20611",
    "aion_main_quest_order_gate",
    "aion_main_quest_teleport_guard",
    "aion_leveling_combat_gate",
    "aion_equipment_auto",
    "aion_leveling_skill_auto",
    "aion_leveling_skill_auto_quickbar",
    "aion_login_autostart",
    "aion_account_runtime_guard",
    "aion_account_limit",
    "aion_account_profile",
    "aion_login_flow_create_character",
    "aion_post_kill_loot", "aion_attack_key_burst", "aion_attack_key_repeat", "aion_floor_recovery", "aion_loot"
}

-- 解析命令行参数
local args = arg or {}
local selected_modules = {}

-- 检查是否请求帮助或列表
for _, a in ipairs(args) do
    if a == "--help" or a == "-h" then
        print("用法: run_tests.lua [模块名...]")
        print("      run_tests.lua --list    列出所有可用模块")
        print("      run_tests.lua --help    显示此帮助")
        print("\n示例:")
        print("      run_tests.lua           运行所有测试")
        print("      run_tests.lua sys proc  只运行 sys 和 proc 测试")
        return true
    elseif a == "--list" or a == "-l" then
        print("可用的测试模块:")
        for _, name in ipairs(default_order) do
            print("  - " .. name)
        end
        return true
    end
end

-- 收集要运行的模块
for _, a in ipairs(args) do
    if not a:match("^%-") then  -- 忽略以 - 开头的选项
        if all_test_modules[a] then
            table.insert(selected_modules, a)
        else
            print("[WARN] 未知的测试模块: " .. a)
        end
    end
end

-- 如果没有指定模块，运行所有测试
if #selected_modules == 0 then
    selected_modules = default_order
end

-- 构建要运行的测试模块列表
local test_modules = {}
for _, name in ipairs(selected_modules) do
    if all_test_modules[name] then
        table.insert(test_modules, all_test_modules[name])
    end
end

print("============================================================")
print("           AetherEngine Lua API Tests")
print("============================================================")
if #selected_modules < #default_order then
    print("  运行模块: " .. table.concat(selected_modules, ", "))
    print("------------------------------------------------------------")
end

-- 运行所有测试
local results = {}
local total_passed = 0
local total_failed = 0
local total_tests = 0

for _, module_name in ipairs(test_modules) do
    local ok, module = pcall(require, module_name)
    if ok and module and module.run then
        local passed = module.run()
        table.insert(
            results,
            {
                name = module_name:match("test_(.+)$") or module_name,
                passed = T.stats.passed,
                failed = T.stats.failed,
                total = T.stats.total,
                success = passed
            }
        )
        total_passed = total_passed + T.stats.passed
        total_failed = total_failed + T.stats.failed
        total_tests = total_tests + T.stats.total
    else
        print("\n[ERROR] Failed to load module: " .. module_name)
        if not ok then
            print("  " .. tostring(module))
        end
    end
end

-- 打印汇总报告
print("\n")
print("============================================================")
print("                    Test Summary")
print("============================================================")

local passed_modules = 0
local failed_modules = 0

for _, result in ipairs(results) do
    local status = result.success and "PASS" or "FAIL"
    local icon = result.success and "[OK]" or "[XX]"
    print(string.format("  %s %-12s: %d/%d tests passed", icon, result.name, result.passed, result.total))

    if result.success then
        passed_modules = passed_modules + 1
    else
        failed_modules = failed_modules + 1
    end
end

print("------------------------------------------------------------")
print(string.format("  Modules: %d passed, %d failed", passed_modules, failed_modules))
print(string.format("  Tests:   %d passed, %d failed, %d total", total_passed, total_failed, total_tests))
print("============================================================")

if total_failed == 0 then
    print("  Result: ALL TESTS PASSED!")
else
    print("  Result: SOME TESTS FAILED!")
end

print("============================================================")

-- 返回结果用于 CI
return total_failed == 0
