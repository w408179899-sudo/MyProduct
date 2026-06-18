--[[
    单机多开挂机控制 UI
    
    管理多个游戏账号的挂机脚本控制面板
    支持账号列表展示、启停控制、脚本选择、账号导入导出
    
    使用方法:
    1. 编辑器模式: AetherRunner.exe --gui，然后运行此脚本
    2. 命令行模式: AetherRunner.exe scripts/test_ui_multiplay.lua
]]

--============================================================================
-- 配置
--============================================================================

local SCRIPTS = {"主线", "刷金", "日常", "副本", "挂机升级", "采集"}
local SAVE_FILE = "accounts.txt"

--============================================================================
-- 状态
--============================================================================

local KEY_LEFT_SHIFT  = 641   -- ImGuiKey_LeftShift (v1.91)
local KEY_RIGHT_SHIFT = 642   -- ImGuiKey_RightShift

local state = {
    accounts = {},          -- 账号列表
    selected = {},          -- 选中状态 (bool数组)
    select_all = false,     -- 全选
    last_click = 0,         -- 上次点击索引 (Shift多选用)
    editing = false,        -- 编辑模式
    script_index = 1,       -- 当前脚本选择
    log = {},               -- 操作日志
    max_log = 30,
}

--============================================================================
-- 账号结构
--============================================================================

local function new_account(account, password, server, char_name, level, gold, status, script)
    return {
        account   = account or "",
        password  = password or "",
        server    = server or "",
        char_name = char_name or "",
        level     = level or "0",
        gold      = gold or "0",
        status    = status or "未启动",
        script    = script or "主线",
    }
end

--============================================================================
-- 辅助函数
--============================================================================

local function add_log(msg)
    table.insert(state.log, 1, os.date("[%H:%M:%S] ") .. msg)
    if #state.log > state.max_log then
        table.remove(state.log)
    end
end

local function get_selected_indices()
    local indices = {}
    for i, sel in ipairs(state.selected) do
        if sel then indices[#indices + 1] = i end
    end
    return indices
end

local function get_status_color(status)
    if status == "运行中" then
        return 0.0, 0.85, 0.0, 1.0
    elseif status == "卡点" then
        return 1.0, 0.2, 0.2, 1.0
    elseif status == "暂停" then
        return 1.0, 0.8, 0.0, 1.0
    else
        return 0.6, 0.6, 0.6, 1.0
    end
end

--============================================================================
-- 账号导入导出
--============================================================================

local function export_accounts()
    local lines = {}
    lines[1] = "账号|密码|区服|角色名|等级|金币|状态|脚本"
    for _, acc in ipairs(state.accounts) do
        lines[#lines + 1] = table.concat({
            acc.account, acc.password, acc.server,
            acc.char_name, acc.level, acc.gold, acc.status, acc.script
        }, "|")
    end
    local f = io.open(SAVE_FILE, "w")
    if f then
        f:write(table.concat(lines, "\n"))
        f:close()
        add_log("已导出 " .. #state.accounts .. " 个账号到 " .. SAVE_FILE)
    else
        add_log("[错误] 无法写入文件: " .. SAVE_FILE)
    end
end

local function import_accounts()
    local f = io.open(SAVE_FILE, "r")
    if not f then
        add_log("[错误] 文件不存在: " .. SAVE_FILE)
        return
    end
    local content = f:read("*a")
    f:close()
    
    state.accounts = {}
    state.selected = {}
    local first = true
    for line in content:gmatch("[^\n]+") do
        if first then
            first = false  -- 跳过表头
        else
            local parts = {}
            for part in line:gmatch("[^|]+") do
                parts[#parts + 1] = part
            end
            if #parts >= 2 then
                state.accounts[#state.accounts + 1] = new_account(
                    parts[1], parts[2], parts[3], parts[4],
                    parts[5], parts[6], parts[7], parts[8]
                )
                state.selected[#state.selected + 1] = false
            end
        end
    end
    add_log("已导入 " .. #state.accounts .. " 个账号")
end

--============================================================================
-- 控制操作
--============================================================================

local function start_account(idx)
    local acc = state.accounts[idx]
    if acc then
        acc.status = "运行中"
        add_log("启动: " .. acc.account .. " [" .. acc.script .. "]")
    end
end

local function stop_account(idx)
    local acc = state.accounts[idx]
    if acc then
        acc.status = "暂停"
        add_log("暂停: " .. acc.account)
    end
end

local function toggle_account(idx)
    local acc = state.accounts[idx]
    if acc then
        if acc.status == "运行中" then
            stop_account(idx)
        else
            start_account(idx)
        end
    end
end

--============================================================================
-- UI 绘制
--============================================================================

local TABLE_FLAGS = imgui.TableFlags_Borders
    + imgui.TableFlags_RowBg
    + imgui.TableFlags_Resizable
    + imgui.TableFlags_ScrollY

local COL_FIXED = imgui.TableColumnFlags_WidthFixed or 0

local function draw_account_table()
    -- 全选
    local chg, val = imgui.checkbox("全选", state.select_all)
    if chg then
        state.select_all = val
        for i = 1, #state.selected do
            state.selected[i] = val
        end
    end
    imgui.same_line()
    imgui.text("  账号数: " .. #state.accounts)
    
    -- 表格 (固定高度 400px，超出滚动)
    if imgui.begin_table("accounts_tbl", 9, TABLE_FLAGS, 0, 400) then
        imgui.table_setup_column("",       COL_FIXED, 30)
        imgui.table_setup_column("账号",    0, 90)
        imgui.table_setup_column("密码",    0, 70)
        imgui.table_setup_column("区服",    0, 60)
        imgui.table_setup_column("角色名",  0, 80)
        imgui.table_setup_column("等级",    COL_FIXED, 40)
        imgui.table_setup_column("金币",    0, 60)
        imgui.table_setup_column("状态",    COL_FIXED, 60)
        imgui.table_setup_column("脚本",    0, 70)
        imgui.table_headers_row()
        
        for i, acc in ipairs(state.accounts) do
            imgui.push_id(i)
            imgui.table_next_row()
            
            -- 选中框 (支持 Shift 多选)
            imgui.table_next_column()
            local sel_chg, sel_val = imgui.checkbox("##sel", state.selected[i] or false)
            if sel_chg then
                local shift_held = imgui.is_key_down(KEY_LEFT_SHIFT) or imgui.is_key_down(KEY_RIGHT_SHIFT)
                if shift_held and state.last_click > 0 and state.last_click ~= i then
                    local lo = math.min(state.last_click, i)
                    local hi = math.max(state.last_click, i)
                    for j = lo, hi do
                        state.selected[j] = sel_val
                    end
                else
                    state.selected[i] = sel_val
                end
                state.last_click = i
            end
            
            -- 账号
            imgui.table_next_column()
            if state.editing then
                imgui.set_next_item_width(-1)
                local c, v = imgui.input_text("##acc", acc.account, 64)
                if c then acc.account = v end
            else
                imgui.text(acc.account)
            end
            
            -- 密码
            imgui.table_next_column()
            if state.editing then
                imgui.set_next_item_width(-1)
                local c, v = imgui.input_text("##pwd", acc.password, 64)
                if c then acc.password = v end
            else
                imgui.text(string.rep("*", math.min(#acc.password, 6)))
            end
            
            -- 区服
            imgui.table_next_column()
            if state.editing then
                imgui.set_next_item_width(-1)
                local c, v = imgui.input_text("##srv", acc.server, 32)
                if c then acc.server = v end
            else
                imgui.text(acc.server)
            end
            
            -- 角色名
            imgui.table_next_column()
            if state.editing then
                imgui.set_next_item_width(-1)
                local c, v = imgui.input_text("##name", acc.char_name, 64)
                if c then acc.char_name = v end
            else
                imgui.text(acc.char_name)
            end
            
            -- 等级 (只读)
            imgui.table_next_column()
            imgui.text(acc.level)
            
            -- 金币 (只读)
            imgui.table_next_column()
            imgui.text(acc.gold)
            
            -- 状态 (只读，颜色)
            imgui.table_next_column()
            local r, g, b, a = get_status_color(acc.status)
            imgui.text_colored(r, g, b, a, acc.status)
            
            -- 脚本 (只读，由下方按钮统一设置)
            imgui.table_next_column()
            imgui.text(acc.script)
            
            imgui.pop_id()
        end
        
        imgui.end_table()
    end
end

local function draw_controls()
    imgui.separator()
    imgui.spacing()
    
    local indices = get_selected_indices()
    
    -- 脚本选择 + 启停
    imgui.set_next_item_width(90)
    local combo_chg, combo_val = imgui.combo("##script", state.script_index, SCRIPTS)
    if combo_chg then state.script_index = combo_val end
    imgui.same_line()
    if imgui.button("应用脚本", 70, 26) then
        local script_name = SCRIPTS[state.script_index]
        for _, idx in ipairs(indices) do
            state.accounts[idx].script = script_name
        end
        add_log("设置 " .. #indices .. " 个账号脚本为: " .. script_name)
    end
    imgui.same_line()
    if imgui.button("开始", 50, 26) then
        if #indices > 0 then
            for _, idx in ipairs(indices) do
                start_account(idx)
            end
            add_log("启动 " .. #indices .. " 个账号")
        else
            add_log("未选中任何账号")
        end
    end
    imgui.same_line()
    if imgui.button("停止", 50, 26) then
        if #indices > 0 then
            for _, idx in ipairs(indices) do
                stop_account(idx)
            end
            add_log("停止 " .. #indices .. " 个账号")
        else
            add_log("未选中任何账号")
        end
    end
    imgui.same_line()
    imgui.text("  ")
    imgui.same_line()
    if imgui.button("账号导入", 70, 26) then
        import_accounts()
    end
    imgui.same_line()
    if imgui.button("账号导出", 70, 26) then
        export_accounts()
    end
    imgui.same_line()
    if imgui.button("+添加", 50, 26) then
        state.accounts[#state.accounts + 1] = new_account("新账号", "123456", "1服", "", "1", "0", "未启动", SCRIPTS[state.script_index])
        state.selected[#state.selected + 1] = false
        add_log("添加新账号")
    end
    imgui.same_line()
    if imgui.button("-删除", 50, 26) then
        local new_accounts = {}
        local new_selected = {}
        local removed = 0
        for i, acc in ipairs(state.accounts) do
            if not state.selected[i] then
                new_accounts[#new_accounts + 1] = acc
                new_selected[#new_selected + 1] = false
            else
                removed = removed + 1
            end
        end
        state.accounts = new_accounts
        state.selected = new_selected
        state.select_all = false
        add_log("删除 " .. removed .. " 个账号")
    end
    imgui.same_line()
    if not state.editing then
        if imgui.button("编辑", 50, 26) then
            state.editing = true
            add_log("进入编辑模式")
        end
    else
        if imgui.button("保存", 50, 26) then
            state.editing = false
            add_log("已保存")
        end
    end
end

local function draw_log_panel()
    imgui.spacing()
    imgui.separator()
    imgui.text("操作日志")
    imgui.same_line()
    if imgui.button("清空##log", 40, 0) then state.log = {} end
    
    imgui.begin_child("log_area", 0, 100, true)
    for _, line in ipairs(state.log) do
        imgui.text(line)
    end
    imgui.end_child()
end

--============================================================================
-- 主渲染
--============================================================================

local function on_render()
    imgui.set_next_window_size(1000, 650, imgui.Cond_FirstUseEver)
    
    if imgui.begin_window("MultiPlay Control") then
        draw_account_table()
        draw_controls()
        draw_log_panel()
    end
    imgui.end_window()
end

--============================================================================
-- 初始化示例数据
--============================================================================

local function init_demo_data()
    state.accounts = {
        new_account("player001", "abc123",  "1服", "战神",   "99", "88888",  "运行中", "主线"),
        new_account("player002", "def456",  "1服", "法爷",   "85", "52000",  "运行中", "刷金"),
        new_account("player003", "ghi789",  "2服", "刺客X",  "72", "31500",  "卡点",   "主线"),
        new_account("player004", "jkl012",  "1服", "奶妈",   "60", "15000",  "未启动", "日常"),
        new_account("player005", "mno345",  "3服", "弓手",   "45", "8200",   "暂停",   "挂机升级"),
    }
    state.selected = {}
    for i = 1, #state.accounts do
        state.selected[i] = false
    end
end

--============================================================================
-- 主入口
--============================================================================

init_demo_data()
add_log("多开控制面板启动")

imgui.on_render(on_render)

if imgui.is_initialized() then
    log.info("编辑器模式: 渲染回调已注册")
else
    log.info("命令行模式: 初始化 ImGui")
    if not imgui.init() then
        log.error("ImGui 初始化失败")
        return
    end
    imgui.run()
end

hotkey.start()
while true do
    if hotkey.is_pressed(0x11) and hotkey.is_pressed(0x7B) then
        log.info("Ctrl+F12 被按下")
        hotkey.stop()
        break
    end
    sys.sleep(10)
end
