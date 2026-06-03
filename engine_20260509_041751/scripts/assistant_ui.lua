--[[
    游戏辅助界面
    
    包含两个标签页：
    1. 主界面 - 组队设置、刷金模式、服务器选择
    2. 配置界面 - 购买数量、恢复设置、出售保留
    
    使用方法:
    1. 编辑器模式: AetherRunner.exe --gui，然后运行此脚本
    2. 命令行模式: AetherRunner.exe scripts/assistant_ui.lua
]]

--============================================================================
-- 状态变量
--============================================================================

local cfg = {
    -- 组队设置
    team_role = 1,              -- 1=队长, 2=队员
    captain_pickup = true,      -- 队长拾取
    captain_fight = true,       -- 队长打怪
    member_pickup = false,      -- 队员拾取
    member_fight = true,        -- 队员打怪

    -- 队员名字 (队长填写，最多7个，逗号或换行分隔)
    member_names = "",

    -- 跟随队长
    captain_name = "",

    -- 服务器
    server = 1,                 -- 1=台服, 2=韩服

    -- 刷金模式
    gold_mode = 1,              -- 1=定点刷金, 2=随机飞刷金, 3=地监刷金, 4=定制刷金
    fixed_coords = "",          -- 定点刷金坐标
    custom_script_index = 1,    -- 定制刷金脚本选择
    dungeon_index = 1,          -- 地监选择

    -- 配置 - 购买数量
    buy_hp_potion = 100,        -- 血药
    buy_arrow = 1000,           -- 箭
    buy_scroll = 10,            -- 回城卷轴
    buy_meat = 50,              -- 肉
    buy_speed_potion = 10,      -- 加速药水

    -- 配置 - 恢复设置
    hp_recover_pct = 50,        -- 血量恢复 %
    weight_clear_pct = 80,      -- 清包负重 %
    food_recover_pct = 30,      -- 饱食度恢复 %
    use_world_tree = false,     -- 无血药世界之树恢复

    -- 配置 - 出售保留
    keep_items = "",            -- 保留物品名字
}

-- 地监列表
local dungeon_list = {
    "眠龙地监——说话之岛",
    "银骑士洞——说话之岛",
}

-- 定制刷金脚本列表
local custom_script_list = {
    "脚本1 - 示例",
    "脚本2 - 示例",
    "脚本3 - 示例",
}

-- 运行状态
local running = false
local frame_count = 0

--============================================================================
-- 辅助函数
--============================================================================

local function help_marker(desc)
    imgui.same_line()
    imgui.text_disabled("(?)")
    if imgui.is_item_hovered() then
        imgui.begin_tooltip()
        imgui.text(desc)
        imgui.end_tooltip()
    end
end

--============================================================================
-- 主界面
--============================================================================

local function draw_main_tab()
    local changed, val
    local COL_RIGHT = 260   -- 右侧列起始位置
    local INPUT_W = 230     -- 左侧输入框宽度

    -- ======================== 左侧: 组队设置 ========================
    imgui.begin_group()

    imgui.text("组队设置:")
    imgui.separator()
    imgui.spacing()

    -- 队长行: 角色 + 拾取 + 打怪
    if imgui.radio_button("队长##role", cfg.team_role == 1) then
        cfg.team_role = 1
    end
    imgui.same_line(80)
    changed, val = imgui.checkbox("拾取##cap", cfg.captain_pickup)
    if changed then cfg.captain_pickup = val end
    imgui.same_line(160)
    changed, val = imgui.checkbox("打怪##cap", cfg.captain_fight)
    if changed then cfg.captain_fight = val end

    -- 队员行
    if imgui.radio_button("队员##role", cfg.team_role == 2) then
        cfg.team_role = 2
    end
    imgui.same_line(80)
    changed, val = imgui.checkbox("拾取##mem", cfg.member_pickup)
    if changed then cfg.member_pickup = val end
    imgui.same_line(160)
    changed, val = imgui.checkbox("打怪##mem", cfg.member_fight)
    if changed then cfg.member_fight = val end

    imgui.spacing()

    -- 队员名字
    imgui.text("队员名字:")
    help_marker("选队长需要在这里填队员名字，最多7个\n队员无需填名字  注: 需同区服")
    imgui.set_next_item_width(INPUT_W)
    changed, val = imgui.input_text_multiline("##member_names", cfg.member_names, INPUT_W, 80)
    if changed then cfg.member_names = val end

    -- 跟随队长
    imgui.text("跟随队长:")
    help_marker("队员跟随队长，需要填队长名字\n队员无需填名字")
    imgui.set_next_item_width(INPUT_W)
    changed, val = imgui.input_text("##captain_name", cfg.captain_name)
    if changed then cfg.captain_name = val end

    imgui.spacing()

    -- 服务器选择
    if imgui.radio_button("台服", cfg.server == 1) then
        cfg.server = 1
    end
    imgui.same_line(120)
    if imgui.radio_button("韩服", cfg.server == 2) then
        cfg.server = 2
    end

    imgui.end_group()

    -- ======================== 右侧: 刷金模式 ========================
    imgui.same_line(COL_RIGHT)
    imgui.begin_group()

    imgui.text("刷金模式:")
    imgui.separator()
    imgui.spacing()

    -- 1-5级后定点刷金
    if imgui.radio_button("定点刷金", cfg.gold_mode == 1) then
        cfg.gold_mode = 1
    end
    imgui.text("填坐标 (每行一组):")
    imgui.set_next_item_width(220)
    changed, val = imgui.input_text_multiline("##fixed_coords", cfg.fixed_coords, 220, 60)
    if changed then cfg.fixed_coords = val end

    -- 1-5级后随机飞刷金
    if imgui.radio_button("随机飞刷金", cfg.gold_mode == 2) then
        cfg.gold_mode = 2
    end

    imgui.spacing()

    -- 地监刷金 + 下拉框
    if imgui.radio_button("地监刷金##mode", cfg.gold_mode == 3) then
        cfg.gold_mode = 3
    end
    help_marker("注意: 地监最好组队前往")
    imgui.set_next_item_width(220)
    changed, val = imgui.combo("##dungeon_sel", cfg.dungeon_index, dungeon_list)
    if changed then cfg.dungeon_index = val end

    imgui.spacing()

    -- 定制刷金 + 脚本下拉框
    if imgui.radio_button("定制刷金##mode", cfg.gold_mode == 4) then
        cfg.gold_mode = 4
    end
    imgui.set_next_item_width(220)
    changed, val = imgui.combo("##custom_script", cfg.custom_script_index, custom_script_list)
    if changed then cfg.custom_script_index = val end

    imgui.end_group()
end

--============================================================================
-- 配置界面
--============================================================================

local function draw_config_tab()
    local changed, val
    local LABEL_W = 140     -- 标签宽度
    local INPUT_W = 100     -- 输入框宽度
    local COL2 = 280        -- 第二列起始
    local COL3 = 530        -- 第三列起始

    -- ======================== 购买数量 ========================
    imgui.text("购买数量设置")
    imgui.separator()
    imgui.spacing()

    -- 行1: 血药 / 箭 / 回城卷轴
    imgui.text("血药购买数量:")
    imgui.same_line(LABEL_W)
    imgui.set_next_item_width(INPUT_W)
    changed, val = imgui.input_int("##buy_hp", cfg.buy_hp_potion)
    if changed then cfg.buy_hp_potion = val end

    imgui.same_line(COL2)
    imgui.text("箭购买数量:")
    imgui.same_line(COL2 + 100)
    imgui.set_next_item_width(INPUT_W)
    changed, val = imgui.input_int("##buy_arrow", cfg.buy_arrow)
    if changed then cfg.buy_arrow = val end

    imgui.same_line(COL3)
    imgui.text("回城卷轴:")
    imgui.same_line(COL3 + 80)
    imgui.set_next_item_width(INPUT_W)
    changed, val = imgui.input_int("##buy_scroll", cfg.buy_scroll)
    if changed then cfg.buy_scroll = val end

    -- 行2: 肉 / 加速药水
    imgui.text("肉购买数量:")
    imgui.same_line(LABEL_W)
    imgui.set_next_item_width(INPUT_W)
    changed, val = imgui.input_int("##buy_meat", cfg.buy_meat)
    if changed then cfg.buy_meat = val end

    imgui.same_line(COL2)
    imgui.text("加速药水数量:")
    imgui.same_line(COL2 + 100)
    imgui.set_next_item_width(INPUT_W)
    changed, val = imgui.input_int("##buy_speed", cfg.buy_speed_potion)
    if changed then cfg.buy_speed_potion = val end

    imgui.spacing()
    imgui.spacing()

    -- ======================== 恢复设置 ========================
    imgui.text("恢复设置")
    imgui.separator()
    imgui.spacing()

    -- 行1: 血量 / 清包 / 世界之树
    imgui.text("血量恢复:")
    imgui.same_line(100)
    imgui.set_next_item_width(80)
    changed, val = imgui.input_int("##hp_pct", cfg.hp_recover_pct)
    if changed then cfg.hp_recover_pct = val end
    imgui.same_line()
    imgui.text("%%")

    imgui.same_line(COL2)
    imgui.text("清包负重:")
    imgui.same_line(COL2 + 80)
    imgui.set_next_item_width(80)
    changed, val = imgui.input_int("##weight_pct", cfg.weight_clear_pct)
    if changed then cfg.weight_clear_pct = val end
    imgui.same_line()
    imgui.text("%%")

    imgui.same_line(COL3)
    changed, val = imgui.checkbox("无血药世界之树恢复", cfg.use_world_tree)
    if changed then cfg.use_world_tree = val end

    -- 行2: 饱食度
    imgui.text("饱食度恢复:")
    imgui.same_line(100)
    imgui.set_next_item_width(80)
    changed, val = imgui.input_int("##food_pct", cfg.food_recover_pct)
    if changed then cfg.food_recover_pct = val end
    imgui.same_line()
    imgui.text("%%")

    imgui.spacing()
    imgui.spacing()

    -- ======================== 出售保留 ========================
    imgui.text("出售保留")
    imgui.separator()
    imgui.spacing()

    imgui.text("出售保留 (填物品名字):")
    help_marker("每行填写一个需要保留的物品名字")
    changed, val = imgui.input_text_multiline("##keep_items", cfg.keep_items, 400, 150)
    if changed then cfg.keep_items = val end
end

--============================================================================
-- 主窗口
--============================================================================

local function draw_main_window()
    imgui.set_next_window_size(650, 480, imgui.Cond_FirstUseEver)
    imgui.set_next_window_pos(100, 100, imgui.Cond_FirstUseEver)

    if imgui.begin_window("游戏辅助", imgui.WindowFlags_NoCollapse) then
        -- 标签页
        if imgui.begin_tab_bar("##main_tabs") then
            if imgui.begin_tab_item("主界面") then
                imgui.spacing()
                draw_main_tab()
                imgui.end_tab_item()
            end

            if imgui.begin_tab_item("配置") then
                imgui.spacing()
                draw_config_tab()
                imgui.end_tab_item()
            end

            imgui.end_tab_bar()
        end

        imgui.separator()
        imgui.spacing()

        -- 底部操作按钮
        local bw, bh = 120, 30
        if not running then
            if imgui.button("开始运行", bw, bh) then
                running = true
                log.info("辅助已启动")
            end
        else
            if imgui.button("停止运行", bw, bh) then
                running = false
                log.info("辅助已停止")
            end
        end
        imgui.same_line()
        if running then
            imgui.text_colored(0.2, 1.0, 0.2, 1.0, "运行中...")
        else
            imgui.text_disabled("已停止")
        end
    end
    imgui.end_window()
end

--============================================================================
-- 渲染回调
--============================================================================

local function on_render()
    frame_count = frame_count + 1
    draw_main_window()
end

--============================================================================
-- 主入口
--============================================================================

log.info("游戏辅助界面启动")

imgui.on_render(on_render)

if imgui.is_initialized() then
    log.info("编辑器模式: 渲染回调已注册")
else
    log.info("命令行模式: 初始化 ImGui")

    if not imgui.init() then
        log.error("ImGui 初始化失败")
        return
    end

    log.info("按 ESC 退出")
    imgui.run()
    log.info("辅助界面已关闭")
end

hotkey.start()
while true do
    if hotkey.is_pressed(0x11) and hotkey.is_pressed(0x7B) then
        log.info("Ctrl+F12 退出")
        hotkey.stop()
        break
    end
    sys.sleep(10)
end
