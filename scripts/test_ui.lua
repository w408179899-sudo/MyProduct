-- ============================================================================
-- entity_ui.lua  CSZF 数据探测 ImGui 界面
-- ============================================================================
local data = require 'data'

local ok, pid = data.connect()
if not ok then log.error(tostring(pid)); return end
log.info('游戏连接成功, PID=' .. pid)

-- 激活游戏窗口 (技能施放需要游戏窗口处于前台)
local function activate_game_window()
    local id = task.run([[
        local jcid=tonumber(pid)
        local hwnd = wnd.find_by_pid(jcid)
        if hwnd then
            wnd.set_foreground(hwnd)
        sys.sleep(50)
        end
    ]], {name = "MyTask", priority = "Normal", pid = tostring(pid)})

end

-- ============================================================================
-- 主题
-- ============================================================================
local function apply_theme()
    imgui.style_colors_light()
    local colors = {}
    local function color(key, value)
        if key ~= nil then colors[key] = value end
    end
    color(imgui.Col_WindowBg,            { r = 0.94, g = 0.94, b = 0.94, a = 1.0 })
    color(imgui.Col_ChildBg,             { r = 0.94, g = 0.94, b = 0.94, a = 1.0 })
    color(imgui.Col_PopupBg,             { r = 0.98, g = 0.98, b = 0.98, a = 1.0 })
    color(imgui.Col_TitleBg,             { r = 0.80, g = 0.80, b = 0.80, a = 1.0 })
    color(imgui.Col_TitleBgActive,       { r = 0.80, g = 0.80, b = 0.80, a = 1.0 })
    color(imgui.Col_FrameBg,             { r = 1.00, g = 1.00, b = 1.00, a = 1.0 })
    color(imgui.Col_FrameBgHovered,      { r = 0.89, g = 0.95, b = 1.00, a = 1.0 })
    color(imgui.Col_FrameBgActive,       { r = 0.80, g = 0.90, b = 1.00, a = 1.0 })
    color(imgui.Col_Button,              { r = 0.64, g = 0.80, b = 0.96, a = 1.0 })
    color(imgui.Col_ButtonHovered,       { r = 0.54, g = 0.74, b = 0.95, a = 1.0 })
    color(imgui.Col_ButtonActive,        { r = 0.42, g = 0.66, b = 0.90, a = 1.0 })
    color(imgui.Col_Tab,                 { r = 0.80, g = 0.88, b = 0.97, a = 1.0 })
    color(imgui.Col_TabSelected,         { r = 0.68, g = 0.82, b = 0.96, a = 1.0 })
    color(imgui.Col_TabSelectedOverline, { r = 0.24, g = 0.54, b = 0.88, a = 1.0 })
    color(imgui.Col_Header,              { r = 0.75, g = 0.86, b = 0.97, a = 1.0 })
    color(imgui.Col_HeaderHovered,       { r = 0.66, g = 0.80, b = 0.95, a = 1.0 })
    color(imgui.Col_HeaderActive,        { r = 0.58, g = 0.74, b = 0.92, a = 1.0 })
    color(imgui.Col_TableHeaderBg,       { r = 0.74, g = 0.84, b = 0.95, a = 1.0 })
    color(imgui.Col_TableRowBgAlt,       { r = 0.88, g = 0.88, b = 0.88, a = 0.45 })
    color(imgui.Col_CheckMark,           { r = 0.18, g = 0.50, b = 0.86, a = 1.0 })
    color(imgui.Col_SliderGrab,          { r = 0.42, g = 0.66, b = 0.90, a = 1.0 })
    color(imgui.Col_SliderGrabActive,    { r = 0.24, g = 0.54, b = 0.88, a = 1.0 })
    imgui.set_style_colors(colors)
    imgui.set_style({
        window_rounding = 2, frame_rounding = 4, tab_rounding = 3,
        grab_rounding = 4, scrollbar_rounding = 6,
        window_padding = { x = 8, y = 8 },
        frame_padding  = { x = 7, y = 4 },
        item_spacing   = { x = 7, y = 5 },
    })
end

-- ============================================================================
-- 数据缓存
-- ============================================================================
local D = {
    player = nil, inventory = nil, skills = nil,
    portals = nil, nearby = nil, characters = nil,
    quickslot = nil,
}
local D_time = {}
local auto_refresh = false
local auto_interval = 3.0
local window_open = true

-- Call 测试状态
local call_cmd_index = 0
local call_arg = ''
local call_result = ''
local call_result_ok = nil
local call_commands = {
    'get_player_info', 'list_inventory', 'list_skills', 'list_buffs',
    'list_mobs', 'list_portals', 'list_nearby', 'list_characters',
    'do_attack', 'use_item', 'equip_item', 'pick_all',
    'use_portal', 'walk', 'stat_point', 'switch_channel', 'enter_char',
}
local call_history = {}

-- 动作测试状态
local action_walk_dir = 1  -- 1=右 -1=左 0=停
local action_item_id = ''
local action_equip_id = ''
local action_enter_char_id = ''
local action_float = false
local action_invincible = false
local action_hp_lock = false
local action_no_knockback = false
local dump_admin_move = false
local action_log = {}
local action_maintain_next = 0
local action_maintain_last = ''
local action_maintain_error = ''
local action_maintain_active = false

local dump_tp_map = ''
local dump_tp_x = ''
local dump_tp_y = ''
local dump_tp_portal = 'sp'
local dump_tp_force = true
local dump_npc_code = ''
local dump_npc_action = 'talk'
local dump_shop_key = ''
local dump_dialog_button = 'ok'
local dump_dialog_select_value = '0'
local dump_dialog_select_index = '0'
local dump_dialog_kind_index = 0
local dump_dialog_kinds = { 'all', 'dialogue', 'etc', 'quest' }
local dump_dialog_kind_names = { '自动', '普通对话', '其他对话', '任务对话' }
local dump_last = {
    title = '', text = '', items = {}, npcs = {}, options = {},
    state = {}, teleport = {}, panels = {}, raw_lines = {},
}

local qs_set_slot = '1'
local qs_set_id = ''
local qs_set_type = 'item'
local qs_use_slot = '1'
local qs_action_index = 0
local qs_actions = { 'press', 'hold', 'release' }

-- 寻路数据
local pf_data = nil
local pf_time = 0
local pf_ground = nil
local pf_ground_range = '-20|20|1'

local function action_exec(name, fn)
    local t = os.clock()
    local ok, ret = pcall(fn)
    local elapsed = os.clock() - t
    action_log[#action_log + 1] = {
        name = name, ok = ok, ret = tostring(ret or ''), time = t, elapsed = elapsed
    }
    if #action_log > 100 then table.remove(action_log, 1) end
    log.info(string.format('[%s] %.3fs => %s: %s', name, elapsed, ok and 'OK' or 'FAIL', tostring(ret or '')))
end

local function maintain_actions()
    local now = os.clock()
    if now < action_maintain_next then return end
    local fast = action_invincible or action_hp_lock or action_no_knockback or action_float or action_maintain_active
    action_maintain_next = now + (fast and 0.02 or 0.25)
    local ok, ret = pcall(function() return data.action_maintain() end)
    if ok then
        action_maintain_last = tostring(ret or '')
        action_maintain_error = ''
        if ret == nil or action_maintain_last == '' then
            action_maintain_active = false
            return
        end
        local s = action_maintain_last
        if s:find('invincible=true', 1, true) then action_invincible = true end
        if s:find('hp_lock=true', 1, true) then action_hp_lock = true end
        if s:find('no_knockback=true', 1, true) then action_no_knockback = true end
        if s:find('float=true', 1, true) then action_float = true end
        if s:find('invincible=false', 1, true) then action_invincible = false end
        if s:find('hp_lock=false', 1, true) then action_hp_lock = false end
        if s:find('no_knockback=false', 1, true) then action_no_knockback = false end
        if s:find('float=false', 1, true) then action_float = false end
        action_maintain_active = not s:find('no enabled action', 1, true)
    else
        action_maintain_error = tostring(ret or '')
    end
end

local function split_pipe(line)
    local parts = {}
    local s = tostring(line or '')
    local start = 1
    while true do
        local pos = s:find('|', start, true)
        if not pos then
            parts[#parts + 1] = s:sub(start)
            break
        end
        parts[#parts + 1] = s:sub(start, pos - 1)
        start = pos + 1
    end
    return parts
end

local function parse_dump_result(title, text)
    dump_last = {
        title = title or '', text = tostring(text or ''),
        items = {}, npcs = {}, options = {}, state = {},
        teleport = {}, panels = {}, raw_lines = {},
    }
    local current_header = ''
    for line in dump_last.text:gmatch('[^\r\n]+') do
        dump_last.raw_lines[#dump_last.raw_lines + 1] = line
        if line:match('^===') then current_header = line end
        local p = split_pipe(line)
        if p[1] == 'npc' then
            dump_last.npcs[#dump_last.npcs + 1] = {
                label = p[2], code = p[3], name = p[4], canKey = p[5],
                onlyClick = p[6], x = p[7], y = p[8], entity = p[9],
            }
        elseif p[1] == 'shop_item' then
            dump_last.items[#dump_last.items + 1] = {
                source = p[2], kind = p[3], npc = p[4], key = p[5], slot = p[6],
                item = p[7], name = p[8], price = p[9], req = p[10],
                count = p[11], stock = p[12], period = p[13], raw = p[14],
            }
        elseif p[1] == 'dialogue_option' then
            dump_last.options[#dump_last.options + 1] = {
                kind = p[2], parent = p[3], index = p[4], name = p[5],
                value = p[6], text = p[7], enable = p[8], comps = p[9], entity = p[10],
            }
        elseif p[1] == 'dialogue_panel' or p[1] == 'dialogue_control' or p[1] == 'dialogue_queue' or p[1] == 'panel' then
            dump_last.panels[#dump_last.panels + 1] = line
        else
            local k, v = line:match('^([^=|]+)=(.*)$')
            if k then
                if current_header:find('teleport') then dump_last.teleport[k] = v end
                dump_last.state[k] = v
            end
        end
    end
end

local function dump_exec(name, fn, after)
    action_exec(name, function()
        local ret = fn()
        parse_dump_result(name, ret)
        if after then after(ret) end
        return ret
    end)
end

local function safe_call(fn, ...)
    local ok, ret = pcall(fn, ...)
    if ok then return ret end
    log.error('调用失败: ' .. tostring(ret))
    return nil
end

local function refresh(tab)
    if tab == 'all' or tab == 'player' then
        D.player = safe_call(data.player_info); D_time.player = os.clock()
    end
    if tab == 'all' or tab == 'inventory' then
        D.inventory = safe_call(data.list_inventory); D_time.inventory = os.clock()
    end
    if tab == 'all' or tab == 'skills' then
        D.skills = safe_call(data.list_skills); D_time.skills = os.clock()
    end
    if tab == 'all' or tab == 'portals' then
        D.portals = safe_call(data.list_portals); D_time.portals = os.clock()
    end
    if tab == 'all' or tab == 'nearby' then
        D.nearby = safe_call(data.list_nearby); D_time.nearby = os.clock()
    end
    if tab == 'all' or tab == 'characters' then
        D.characters = safe_call(data.list_characters); D_time.characters = os.clock()
    end
    if tab == 'all' or tab == 'quickslot' then
        D.quickslot = safe_call(data.list_quickslot); D_time.quickslot = os.clock()
    end
end

-- ============================================================================
-- 表格渲染辅助
-- ============================================================================
local TBL_FLAGS = nil  -- 延迟初始化

local function get_tbl_flags()
    if not TBL_FLAGS then
        TBL_FLAGS = imgui.TableFlags_Borders + imgui.TableFlags_RowBg + imgui.TableFlags_Resizable
    end
    return TBL_FLAGS
end

local function kv_table(pairs_list)
    if imgui.begin_table('kv', 2, get_tbl_flags()) then
        imgui.table_setup_column('属性', imgui.TableColumnFlags_WidthFixed, 100)
        imgui.table_setup_column('值', imgui.TableColumnFlags_WidthStretch)
        imgui.table_headers_row()
        for _, kv in ipairs(pairs_list) do
            imgui.table_next_row()
            imgui.table_next_column(); imgui.text(kv[1])
            imgui.table_next_column()
            local v = kv[2]
            if kv[3] == 'green' then imgui.text_colored(0.3, 0.9, 0.3, 1.0, tostring(v))
            elseif kv[3] == 'yellow' then imgui.text_colored(0.9, 0.9, 0.3, 1.0, tostring(v))
            elseif kv[3] == 'blue' then imgui.text_colored(0.3, 0.6, 0.9, 1.0, tostring(v))
            else imgui.text(tostring(v)) end
        end
        imgui.end_table()
    end
end

-- ============================================================================
-- 各标签页渲染
-- ============================================================================
local function tab_player()
    if not D.player then imgui.text_disabled('未刷新'); return end
    local p = D.player
    kv_table({
        {'Hp', p.Hp or '?', 'green'},
        {'Mp', p.Mp or '?'},
        {'Level', p.Level or '?'},
        {'Exp', p.Exp or '?'},
        {'Job', p.Job or '?'},
        {'MaxHp', p.MaxHp or '?'},
        {'MaxMp', p.MaxMp or '?'},
        {'Nickname', p.Nickname or '?'},
        {'Gender', p.Gender or '?'},
        {'CharId', p.CharId or '?'},
        {'AP', p.AP or '?'},
        {'X', p.X or '?'},
        {'Y', p.Y or '?'},
        {'WalkSpeed', p.WalkSpeed or '?'},
        {'Gravity', p.Gravity or '?'},
        {'Invincible', p.Invincible or '?'},
        {'MapId', p.MapId or '?', 'blue'},
        {'MapName', p.MapName or '?', 'green'},
        {'Entity', p.Entity or '?'},
    })
end

local function tab_inventory()
    if not D.inventory then imgui.text_disabled('未刷新'); return end
    local inv = D.inventory
    if inv.meso then
        imgui.text_colored(0.9, 0.8, 0.3, 1.0, '金币: ' .. tostring(inv.meso))
        imgui.separator()
    end
    if not inv.items or #inv.items == 0 then imgui.text('背包为空'); return end
    if imgui.begin_table('InvTbl', 7, get_tbl_flags()) then
        imgui.table_setup_column('类型', imgui.TableColumnFlags_WidthFixed, 60)
        imgui.table_setup_column('槽位', imgui.TableColumnFlags_WidthFixed, 50)
        imgui.table_setup_column('物品ID', imgui.TableColumnFlags_WidthFixed, 80)
        imgui.table_setup_column('数量', imgui.TableColumnFlags_WidthFixed, 50)
        imgui.table_setup_column('名称', imgui.TableColumnFlags_WidthStretch)
        imgui.table_setup_column('类别', imgui.TableColumnFlags_WidthFixed, 50)
        imgui.table_setup_column('装备信息', imgui.TableColumnFlags_WidthStretch)
        imgui.table_headers_row()
        for _, it in ipairs(inv.items) do
            imgui.table_next_row()
            imgui.table_next_column(); imgui.text(tostring(it.type or ''))
            imgui.table_next_column(); imgui.text(tostring(it.index or ''))
            imgui.table_next_column(); imgui.text(tostring(it.Code or ''))
            imgui.table_next_column(); imgui.text(tostring(it.Count or ''))
            imgui.table_next_column(); imgui.text(it.name or '?')
            imgui.table_next_column(); imgui.text(it.itemTypeName or '?')
            imgui.table_next_column()
            if it.equipInfo then
                local parts = {}
                for k, v in pairs(it.equipInfo) do parts[#parts+1] = k .. '=' .. tostring(v) end
                imgui.text(table.concat(parts, ', '))
            else
                imgui.text_disabled('-')
            end
        end
        imgui.end_table()
    end
end

local function tab_skills()
    if not D.skills then imgui.text_disabled('未刷新'); return end
    local sk = D.skills
    imgui.text(string.format('技能点: %d  已用: %d', sk.point or 0, sk.used or 0))
    imgui.separator()
    if not sk.skills or #sk.skills == 0 then imgui.text('无技能'); return end
    if imgui.begin_table('SkillTbl', 6, get_tbl_flags()) then
        imgui.table_setup_column('阶层', imgui.TableColumnFlags_WidthFixed, 50)
        imgui.table_setup_column('槽位', imgui.TableColumnFlags_WidthFixed, 50)
        imgui.table_setup_column('技能ID', imgui.TableColumnFlags_WidthFixed, 80)
        imgui.table_setup_column('等级', imgui.TableColumnFlags_WidthFixed, 50)
        imgui.table_setup_column('名称', imgui.TableColumnFlags_WidthStretch)
        imgui.table_setup_column('操作', imgui.TableColumnFlags_WidthFixed, 90)
        imgui.table_headers_row()
        for _, s in ipairs(sk.skills) do
            imgui.table_next_row()
            imgui.table_next_column(); imgui.text(tostring(s.tier or ''))
            imgui.table_next_column(); imgui.text(tostring(s.index or ''))
            imgui.table_next_column(); imgui.text(tostring(s.Code or ''))
            imgui.table_next_column(); imgui.text(tostring(s.CurrentLevel or ''))
            imgui.table_next_column(); imgui.text(s.name or '?')
            imgui.table_next_column()
            if imgui.button('设快捷栏##skill_qs_' .. tostring(s.Code or ''), 80, 0) then
                local id = tonumber(s.Code)
                if id then
                    qs_set_id = tostring(id)
                    qs_set_type = 'skill'
                    action_exec('quickslot_set(' .. qs_set_slot .. ',' .. id .. ',skill)', function() return data.quickslot_set(qs_set_slot, id, 'skill') end)
                    refresh('quickslot')
                end
            end
        end
        imgui.end_table()
    end
end

local function tab_portals()
    if not D.portals then imgui.text_disabled('未刷新'); return end
    if type(D.portals) ~= 'table' then imgui.text(tostring(D.portals)); return end
    if #D.portals == 0 then imgui.text('无传送门'); return end
    if imgui.begin_table('PortalTbl', 7, get_tbl_flags()) then
        imgui.table_setup_column('索引', imgui.TableColumnFlags_WidthFixed, 40)
        imgui.table_setup_column('名称', imgui.TableColumnFlags_WidthFixed, 80)
        imgui.table_setup_column('类型', imgui.TableColumnFlags_WidthFixed, 50)
        imgui.table_setup_column('目标地图', imgui.TableColumnFlags_WidthFixed, 100)
        imgui.table_setup_column('目标门', imgui.TableColumnFlags_WidthFixed, 80)
        imgui.table_setup_column('X', imgui.TableColumnFlags_WidthFixed, 60)
        imgui.table_setup_column('Y', imgui.TableColumnFlags_WidthFixed, 60)
        imgui.table_headers_row()
        for _, p in ipairs(D.portals) do
            imgui.table_next_row()
            imgui.table_next_column(); imgui.text(tostring(p.index or ''))
            imgui.table_next_column(); imgui.text(p.name or '?')
            imgui.table_next_column(); imgui.text(tostring(p.type or ''))
            imgui.table_next_column()
            local dest = tostring(p.destMap or '')
            if dest ~= '' and dest ~= '999999999' then
                imgui.text_colored(0.3, 0.6, 0.9, 1.0, dest)
            else
                imgui.text_disabled(dest)
            end
            imgui.table_next_column(); imgui.text(tostring(p.destName or ''))
            imgui.table_next_column(); imgui.text(tostring(p.x or ''))
            imgui.table_next_column(); imgui.text(tostring(p.y or ''))
        end
        imgui.end_table()
    end
end

local function tab_nearby()
    if not D.nearby then imgui.text_disabled('未刷新'); return end
    local nb = D.nearby
    imgui.text(string.format('怪物:%d  掉落:%d  传送门:%d  NPC:%d  玩家:%d',
        nb.mobCount or 0, nb.dropCount or 0, nb.portalCount or 0, nb.npcCount or 0, nb.playerCount or 0))
    imgui.separator()

    if imgui.begin_tab_bar('NearbySubTabs') then
        -- 怪物
        if imgui.begin_tab_item(string.format('怪物 (%d)##nb_mob', #(nb.mobs or {}))) then
            if imgui.begin_table('NMTbl', 5, get_tbl_flags()) then
                imgui.table_setup_column('名称', imgui.TableColumnFlags_WidthFixed, 100)
                imgui.table_setup_column('ID', imgui.TableColumnFlags_WidthFixed, 70)
                imgui.table_setup_column('等级', imgui.TableColumnFlags_WidthFixed, 50)
                imgui.table_setup_column('坐标', imgui.TableColumnFlags_WidthFixed, 120)
                imgui.table_setup_column('HP', imgui.TableColumnFlags_WidthStretch)
                imgui.table_headers_row()
                for _, m in ipairs(nb.mobs or {}) do
                    imgui.table_next_row()
                    imgui.table_next_column(); imgui.text(m.Name or '?')
                    imgui.table_next_column(); imgui.text(tostring(m.MobId or ''))
                    imgui.table_next_column(); imgui.text(tostring(m.Level or ''))
                    imgui.table_next_column(); imgui.text(string.format('(%.1f, %.1f)', m.x or 0, m.y or 0))
                    imgui.table_next_column()
                    if m.Hp and m.Hp ~= '' then imgui.text(tostring(m.Hp) .. '/' .. tostring(m.MaxHp or ''))
                    else imgui.text_disabled('-') end
                end
                imgui.end_table()
            end
            imgui.end_tab_item()
        end
        -- 掉落物
        if imgui.begin_tab_item(string.format('掉落物 (%d)##nb_drop', #(nb.drops or {}))) then
            if imgui.begin_table('NDTbl', 6, get_tbl_flags()) then
                imgui.table_setup_column('物品', imgui.TableColumnFlags_WidthFixed, 100)
                imgui.table_setup_column('ID', imgui.TableColumnFlags_WidthFixed, 70)
                imgui.table_setup_column('归属', imgui.TableColumnFlags_WidthFixed, 80)
                imgui.table_setup_column('类型', imgui.TableColumnFlags_WidthFixed, 50)
                imgui.table_setup_column('自由', imgui.TableColumnFlags_WidthFixed, 40)
                imgui.table_setup_column('坐标', imgui.TableColumnFlags_WidthStretch)
                imgui.table_headers_row()
                for _, d in ipairs(nb.drops or {}) do
                    imgui.table_next_row()
                    imgui.table_next_column(); imgui.text(d.Name or tostring(d.ItemId or '?'))
                    imgui.table_next_column(); imgui.text(tostring(d.ItemId or ''))
                    imgui.table_next_column()
                    if d.Source == 'mine' then imgui.text_colored(0.3, 0.9, 0.3, 1.0, '我的')
                    else imgui.text(d.OwnerCID or '?') end
                    imgui.table_next_column(); imgui.text(tostring(d.DropperType or ''))
                    imgui.table_next_column()
                    if d.Free then imgui.text_colored(0.9, 0.9, 0.3, 1.0, '是')
                    else imgui.text('否') end
                    imgui.table_next_column(); imgui.text(string.format('(%.1f, %.1f)', d.x or 0, d.y or 0))
                end
                imgui.end_table()
            end
            imgui.end_tab_item()
        end
        -- 传送门
        if imgui.begin_tab_item(string.format('传送门 (%d)##nb_portal', #(nb.portals or {}))) then
            if imgui.begin_table('NPTbl', 5, get_tbl_flags()) then
                imgui.table_setup_column('名称', imgui.TableColumnFlags_WidthFixed, 80)
                imgui.table_setup_column('类型', imgui.TableColumnFlags_WidthFixed, 50)
                imgui.table_setup_column('目标地图', imgui.TableColumnFlags_WidthFixed, 100)
                imgui.table_setup_column('目标门', imgui.TableColumnFlags_WidthFixed, 80)
                imgui.table_setup_column('坐标', imgui.TableColumnFlags_WidthStretch)
                imgui.table_headers_row()
                for _, p in ipairs(nb.portals or {}) do
                    imgui.table_next_row()
                    imgui.table_next_column(); imgui.text(p.Name or '?')
                    imgui.table_next_column(); imgui.text(tostring(p.PortalType or ''))
                    imgui.table_next_column()
                    local dest = tostring(p.DestMap or '')
                    if dest ~= '' and dest ~= '999999999' then
                        imgui.text_colored(0.3, 0.6, 0.9, 1.0, dest)
                    else imgui.text_disabled(dest) end
                    imgui.table_next_column(); imgui.text(tostring(p.DestPortal or ''))
                    imgui.table_next_column(); imgui.text(string.format('(%.1f, %.1f)', p.x or 0, p.y or 0))
                end
                imgui.end_table()
            end
            imgui.end_tab_item()
        end
        -- NPC
        if imgui.begin_tab_item(string.format('NPC (%d)##nb_npc', #(nb.npcs or {}))) then
            if imgui.begin_table('NNTbl', 3, get_tbl_flags()) then
                imgui.table_setup_column('名称', imgui.TableColumnFlags_WidthStretch)
                imgui.table_setup_column('代码', imgui.TableColumnFlags_WidthFixed, 80)
                imgui.table_setup_column('坐标', imgui.TableColumnFlags_WidthFixed, 120)
                imgui.table_headers_row()
                for _, n in ipairs(nb.npcs or {}) do
                    imgui.table_next_row()
                    imgui.table_next_column(); imgui.text(n.Name or '?')
                    imgui.table_next_column(); imgui.text(tostring(n.NpcCode or ''))
                    imgui.table_next_column(); imgui.text(string.format('(%.1f, %.1f)', n.x or 0, n.y or 0))
                end
                imgui.end_table()
            end
            imgui.end_tab_item()
        end
        -- 玩家
        if imgui.begin_tab_item(string.format('玩家 (%d)##nb_player', #(nb.players or {}))) then
            if imgui.begin_table('NPTbl2', 6, get_tbl_flags()) then
                imgui.table_setup_column('名称', imgui.TableColumnFlags_WidthFixed, 100)
                imgui.table_setup_column('ID', imgui.TableColumnFlags_WidthFixed, 80)
                imgui.table_setup_column('等级', imgui.TableColumnFlags_WidthFixed, 50)
                imgui.table_setup_column('职业', imgui.TableColumnFlags_WidthFixed, 50)
                imgui.table_setup_column('HP', imgui.TableColumnFlags_WidthFixed, 80)
                imgui.table_setup_column('坐标', imgui.TableColumnFlags_WidthStretch)
                imgui.table_headers_row()
                for _, p in ipairs(nb.players or {}) do
                    imgui.table_next_row()
                    imgui.table_next_column(); imgui.text_colored(0.3, 0.6, 0.9, 1.0, p.Name or '?')
                    imgui.table_next_column(); imgui.text(tostring(p.Id or ''))
                    imgui.table_next_column(); imgui.text(tostring(p.Level or ''))
                    imgui.table_next_column(); imgui.text(tostring(p.Job or ''))
                    imgui.table_next_column(); imgui.text(tostring(p.Hp or '') .. '/' .. tostring(p.MaxHp or ''))
                    imgui.table_next_column(); imgui.text(string.format('(%.1f, %.1f)', p.x or 0, p.y or 0))
                end
                imgui.end_table()
            end
            imgui.end_tab_item()
        end
        imgui.end_tab_bar()
    end
end

local function tab_characters()
    if not D.characters then imgui.text_disabled('未刷新'); return end
    if type(D.characters) ~= 'table' or #D.characters == 0 then imgui.text('无角色'); return end
    if imgui.begin_table('CharTbl', 4, get_tbl_flags()) then
        imgui.table_setup_column('槽位', imgui.TableColumnFlags_WidthFixed, 50)
        imgui.table_setup_column('名称', imgui.TableColumnFlags_WidthStretch)
        imgui.table_setup_column('等级', imgui.TableColumnFlags_WidthFixed, 50)
        imgui.table_setup_column('职业', imgui.TableColumnFlags_WidthFixed, 60)
        imgui.table_headers_row()
        for _, c in ipairs(D.characters) do
            imgui.table_next_row()
            imgui.table_next_column(); imgui.text(tostring(c.index or ''))
            imgui.table_next_column(); imgui.text(c.name or '?')
            imgui.table_next_column(); imgui.text(tostring(c.level or ''))
            imgui.table_next_column(); imgui.text(tostring(c.job or ''))
        end
        imgui.end_table()
    end
end

-- ============================================================================
-- 快捷栏
-- ============================================================================
local function tab_quickslot()
    if not D.quickslot then imgui.text_disabled('未刷新'); return end
    local qs = D.quickslot
    if not qs.slots or #qs.slots == 0 then imgui.text('无快捷栏数据'); return end

    -- 操作模式选择
    imgui.text('触发模式:')
    imgui.same_line()
    local ch_act, val_act = imgui.combo('##qs_action', qs_action_index, qs_actions)
    if ch_act then qs_action_index = val_act end
    local cur_action = qs_actions[qs_action_index + 1] or 'press'
    imgui.same_line()
    imgui.text_disabled('(press=触发一次 hold=按住 release=停止)')
    imgui.separator()

    -- 快捷栏槽位表
    if imgui.begin_table('QSTbl', 5, get_tbl_flags()) then
        imgui.table_setup_column('槽位', imgui.TableColumnFlags_WidthFixed, 40)
        imgui.table_setup_column('按键', imgui.TableColumnFlags_WidthFixed, 80)
        imgui.table_setup_column('类型', imgui.TableColumnFlags_WidthFixed, 60)
        imgui.table_setup_column('ID', imgui.TableColumnFlags_WidthFixed, 100)
        imgui.table_setup_column('操作', imgui.TableColumnFlags_WidthStretch)
        imgui.table_headers_row()
        for _, s in ipairs(qs.slots) do
            imgui.table_next_row()
            imgui.table_next_column(); imgui.text(tostring(s.slot))
            imgui.table_next_column(); imgui.text(s.key or '?')
            imgui.table_next_column()
            if s.cat == 'Item' then imgui.text_colored(0.3, 0.9, 0.3, 1.0, s.cat)
            elseif s.cat == 'Skill' then imgui.text_colored(0.3, 0.6, 0.9, 1.0, s.cat)
            elseif s.cat == 'Default' then imgui.text_colored(0.9, 0.8, 0.3, 1.0, s.cat)
            else imgui.text_disabled(s.cat) end
            imgui.table_next_column(); imgui.text(s.id ~= '' and s.id or '-')
            imgui.table_next_column()
            if imgui.button('触发##qs_use_' .. s.slot, 50, 0) then
                --ctivate_game_window()
                --sys.sleep(100)
                action_exec('quickslot_use(' .. s.slot .. ',' .. cur_action .. ')', function() return data.quickslot_use(s.slot, cur_action) end)
            end
            imgui.same_line()
            if imgui.button('清空##qs_clr_' .. s.slot, 50, 0) then
                action_exec('quickslot_set(' .. s.slot .. ',0)', function() return data.quickslot_set(s.slot, 0) end)
                refresh('quickslot')
            end
        end
        imgui.end_table()
    end

    imgui.separator()

    -- 设置槽位
    imgui.text('设置快捷栏:')

    imgui.push_item_width(50)
    local ch1, v1 = imgui.input_text('槽位##qs_slot', qs_set_slot)
    if ch1 then qs_set_slot = v1 end
    imgui.pop_item_width()
    imgui.same_line()
    imgui.text_disabled('1-8 或按键名')

    imgui.push_item_width(120)
    local ch2, v2 = imgui.input_text('物品/技能ID##qs_id', qs_set_id)
    if ch2 then qs_set_id = v2 end
    imgui.pop_item_width()
    imgui.same_line()
    imgui.text_disabled('0=清空')

    imgui.text('类型:')
    imgui.same_line()
    if imgui.radio_button('物品##qs_type_item', qs_set_type == 'item') then
        qs_set_type = 'item'
    end
    imgui.same_line()
    if imgui.radio_button('技能##qs_type_skill', qs_set_type == 'skill') then
        qs_set_type = 'skill'
    end
    imgui.same_line()

    if imgui.button('设置快捷栏', 100, 0) then
        local id = tonumber(qs_set_id)
        if id then
            action_exec('quickslot_set(' .. qs_set_slot .. ',' .. id .. ',' .. qs_set_type .. ')', function() return data.quickslot_set(qs_set_slot, id, qs_set_type) end)
            refresh('quickslot')
        end
    end

    imgui.separator()

    -- 批量操作
    if imgui.button('清空全部', 100, 0) then
        action_exec('quickslot_clear', data.quickslot_clear)
        refresh('quickslot')
    end
    imgui.same_line()
    if imgui.button('刷新快捷栏', 100, 0) then
        refresh('quickslot')
    end
    imgui.same_line()
    if imgui.button('系统诊断', 100, 0) then
        action_exec('probe_systems', function() return data.probe_systems() end)
    end
end

local function do_call()
    local cmd = call_commands[call_cmd_index + 1]
    if not cmd then return end
    local ok, ret = pcall(data.call, cmd, call_arg)
    call_result_ok = ok
    if ok then
        call_result = tostring(ret or 'nil')
    else
        call_result = tostring(ret)
    end
    call_history[#call_history + 1] = {
        cmd = cmd, arg = call_arg, ok = ok, result = call_result, time = os.clock()
    }
    if #call_history > 50 then table.remove(call_history, 1) end
end

local function tab_call()
    -- 命令选择
    local items = {}
    for i, c in ipairs(call_commands) do items[i] = c end
    local ch, val = imgui.combo('命令', call_cmd_index, items)
    if ch then call_cmd_index = val end

    imgui.same_line()
    local selected_cmd = call_commands[call_cmd_index + 1] or ''
    imgui.text_disabled(selected_cmd)

    -- 参数输入
    local changed, val2 = imgui.input_text('参数##call_arg', call_arg)
    if changed then call_arg = val2 end

    imgui.same_line()
    if imgui.button('执行', 80, 0) then do_call() end

    imgui.separator()

    -- 参数提示
    local cmd = call_commands[call_cmd_index + 1] or ''
    local hints = {
        use_item = '物品代码 (如 2000002 药水)',
        equip_item = '物品代码 (如 1002000 头盔)',
        use_portal = 'portalName=传送门名称 (如 west00)',
        walk = 'left / right / stop',
        stat_point = 'str/dex/int/luk 或 str=5 dex=3 int=2 luk=1',
        switch_channel = 'instanceId=频道实例ID',
        enter_char = 'charId=角色ID',
    }
    local hint = hints[cmd]
    if hint then
        imgui.text_disabled('参数提示: ' .. hint)
    end

    imgui.separator()

    -- 最近结果
    if call_result_ok ~= nil then
        if call_result_ok then
            imgui.text_colored(0.3, 0.9, 0.3, 1.0, 'OK')
        else
            imgui.text_colored(0.9, 0.3, 0.3, 1.0, 'FAIL')
        end
        imgui.same_line()
        imgui.text(call_result)
        imgui.separator()
    end

    -- 历史记录
    imgui.text_disabled(string.format('历史记录 (%d)', #call_history))
    if imgui.begin_child('call_history', 0, 200) then
        for i = #call_history, 1, -1 do
            local h = call_history[i]
            if h.ok then
                imgui.text_colored(0.3, 0.9, 0.3, 1.0, '[OK]')
            else
                imgui.text_colored(0.9, 0.3, 0.3, 1.0, '[FAIL]')
            end
            imgui.same_line()
            imgui.text(string.format('%s(%s) => %.1fs前', h.cmd, h.arg or '', os.clock() - h.time))
        end
        imgui.end_child()
    end
end

-- ============================================================================
-- 动作测试
-- ============================================================================
local function tab_actions()
    imgui.text('动作测试')
    imgui.separator()

    -- 攻击
    if imgui.button('普通攻击', 100, 0) then
        action_exec('do_attack', data.do_attack)
    end

    -- 拾取
    imgui.same_line()
    if imgui.button('全图拾取', 100, 0) then
        action_exec('pick_all', data.pick_all)
    end

    -- 悬浮
    imgui.same_line()
    local ch, val = imgui.checkbox('悬浮', action_float)
    if ch then
        action_float = val
        action_exec('float(' .. tostring(val) .. ')', function() return data.float(val) end)
    end

    imgui.same_line()
    local ch_inv, val_inv = imgui.checkbox('无敌', action_invincible)
    if ch_inv then
        action_invincible = val_inv
        action_exec('set_invincible(' .. tostring(val_inv) .. ')', function() return data.set_invincible(val_inv) end)
    end
    imgui.same_line()
    if imgui.button('状态探测##action_state', 80, 0) then
        action_exec('probe_action_state', function() return data.probe_action_state() end)
    end

    imgui.separator()

    -- 行走
    imgui.text('行走:')
    imgui.same_line()
    if imgui.radio_button('左', action_walk_dir == -1) then
        action_walk_dir = -1
        action_exec('walk_left', function() return data.walk(-1, 0) end)
    end
    imgui.same_line()
    if imgui.radio_button('右', action_walk_dir == 1) then
        action_walk_dir = 1
        action_exec('walk_right', function() return data.walk(1, 0) end)
    end
    imgui.same_line()
    if imgui.radio_button('停', action_walk_dir == 0) then
        action_walk_dir = 0
        action_exec('walk_stop', function() return data.walk(0, 0) end)
    end

    imgui.separator()

    -- 使用物品
    imgui.text('使用物品:')
    imgui.same_line()
    imgui.push_item_width(120)
    local ch2, val2 = imgui.input_text('##use_item_id', action_item_id)
    if ch2 then action_item_id = val2 end
    imgui.pop_item_width()
    imgui.same_line()
    if imgui.button('使用', 60, 0) then
        local id = tonumber(action_item_id)
        if id then
            action_exec('use_item(' .. id .. ')', function() return data.use_item(id) end)
        end
    end

    -- 穿装备
    imgui.text('穿装备:')
    imgui.same_line()
    imgui.push_item_width(120)
    local ch3, val3 = imgui.input_text('##equip_id', action_equip_id)
    if ch3 then action_equip_id = val3 end
    imgui.pop_item_width()
    imgui.same_line()
    if imgui.button('穿', 60, 0) then
        local id = tonumber(action_equip_id)
        if id then
            action_exec('equip_item(' .. id .. ')', function() return data.equip_item(id) end)
        end
    end

    -- 进入角色
    imgui.text('进入角色:')
    imgui.same_line()
    imgui.push_item_width(120)
    local ch4, val4 = imgui.input_text('##enter_char_id', action_enter_char_id)
    if ch4 then action_enter_char_id = val4 end
    imgui.pop_item_width()
    imgui.same_line()
    if imgui.button('进入', 60, 0) then
        local id = tonumber(action_enter_char_id)
        if id then
            action_exec('enter_char(' .. id .. ')', function() return data.enter_char(id) end)
        end
    end

    imgui.separator()

    -- 快捷栏
    imgui.text('快捷栏:')
    imgui.same_line()
    imgui.push_item_width(50)
    local ch5, v5 = imgui.input_text('##qs_use_slot', qs_use_slot)
    if ch5 then qs_use_slot = v5 end
    imgui.pop_item_width()
    imgui.same_line(); imgui.text('槽位(1-8)')
    imgui.same_line()
    local ch_act2, val_act2 = imgui.combo('##qs_act2', qs_action_index, qs_actions)
    if ch_act2 then qs_action_index = val_act2 end
    local cur_act2 = qs_actions[qs_action_index + 1] or 'press'
    imgui.same_line()
    if imgui.button('触发', 50, 0) then
        --activate_game_window()
        action_exec('quickslot_use(' .. qs_use_slot .. ',' .. cur_act2 .. ')', function() return data.quickslot_use(qs_use_slot, cur_act2) end)
    end
    imgui.same_line()
    if imgui.button('停止', 50, 0) then
        action_exec('quickslot_use(' .. qs_use_slot .. ',release)', function() return data.quickslot_use(qs_use_slot, 'release') end)
    end
    imgui.same_line()
    if imgui.button('清空全部', 60, 0) then
        action_exec('quickslot_clear', data.quickslot_clear)
    end

    imgui.separator()

    -- 动作日志
    imgui.text_disabled(string.format('动作日志 (%d)', #action_log))
    if imgui.begin_child('action_log', 0, 200) then
        for i = #action_log, 1, -1 do
            local h = action_log[i]
            if h.ok then
                imgui.text_colored(0.3, 0.9, 0.3, 1.0, '[OK]')
            else
                imgui.text_colored(0.9, 0.3, 0.3, 1.0, '[FAIL]')
            end
            imgui.same_line()
            imgui.text(string.format('%s %.3fs => %s', h.name, h.elapsed, h.ret))
        end
        imgui.end_child()
    end
end

-- ============================================================================
-- Dump 功能测试
-- ============================================================================
local function dump_manager_kind_from_option(opt)
    local k = tostring(opt and opt.kind or '')
    if k:find('quest') then return 'quest' end
    if k:find('etc') then return 'etc' end
    if k:find('dialogue') then return 'dialogue' end
    return dump_dialog_kinds[dump_dialog_kind_index + 1] or 'all'
end

local function draw_dump_action_log(height)
    imgui.text_disabled(string.format('动作日志 (%d)', #action_log))
    if imgui.begin_child('dump_action_log', 0, height or 180) then
        for i = #action_log, 1, -1 do
            local h = action_log[i]
            if h.ok then
                imgui.text_colored(0.3, 0.9, 0.3, 1.0, '[OK]')
            else
                imgui.text_colored(0.9, 0.3, 0.3, 1.0, '[FAIL]')
            end
            imgui.same_line()
            imgui.text(string.format('%s %.3fs => %s', h.name, h.elapsed, h.ret))
        end
        imgui.end_child()
    end
end

-- ============================================================================
-- 寻路数据标签页
-- ============================================================================
local function tab_pathfind()
    if imgui.button('刷新寻路数据', 120, 0) then
        local t = os.clock()
        pf_data = data.probe_pathfind()
        pf_time = os.clock() - t
        -- 打印原始日志用于调试
        if pf_data and pf_data.raw then
            log.info('[probe_pathfind] raw output:\n' .. pf_data.raw)
        else
            log.info('[probe_pathfind] returned nil')
        end
    end
    imgui.same_line()
    if pf_data then
        imgui.text_disabled(string.format('%.1fs前  耗时%.3fs', os.clock() - pf_time, pf_time))
    else
        imgui.text_disabled('未刷新')
        return
    end

    imgui.separator()

    -- 玩家位置 + 地图信息
    kv_table({
        {'PlayerX', string.format('%.2f', pf_data.player.x), 'green'},
        {'PlayerY', string.format('%.2f', pf_data.player.y), 'green'},
        {'MapName', pf_data.map.name or '?', 'blue'},
        {'IsTown', tostring(pf_data.map.isTown), 'yellow'},
    })

    imgui.separator()
    imgui.text(string.format('传送门 (%d)', #pf_data.portals))
    if #pf_data.portals > 0 then
        if imgui.begin_table('PFTbl', 6, get_tbl_flags()) then
            imgui.table_setup_column('名称', imgui.TableColumnFlags_WidthFixed, 80)
            imgui.table_setup_column('类型', imgui.TableColumnFlags_WidthFixed, 50)
            imgui.table_setup_column('目标地图', imgui.TableColumnFlags_WidthFixed, 100)
            imgui.table_setup_column('目标门', imgui.TableColumnFlags_WidthFixed, 80)
            imgui.table_setup_column('激活', imgui.TableColumnFlags_WidthFixed, 50)
            imgui.table_setup_column('坐标', imgui.TableColumnFlags_WidthStretch)
            imgui.table_headers_row()
            for _, p in ipairs(pf_data.portals) do
                imgui.table_next_row()
                imgui.table_next_column(); imgui.text(p.name or '?')
                imgui.table_next_column(); imgui.text(tostring(p.type or ''))
                imgui.table_next_column()
                local dest = tostring(p.destMap or '')
                if dest ~= '' and dest ~= '999999999' then
                    imgui.text_colored(0.3, 0.6, 0.9, 1.0, dest)
                else imgui.text_disabled(dest) end
                imgui.table_next_column(); imgui.text(p.destPortal or '?')
                imgui.table_next_column()
                if p.active then imgui.text_colored(0.3, 0.9, 0.3, 1.0, '是')
                else imgui.text_disabled('否') end
                imgui.table_next_column(); imgui.text(string.format('(%.1f, %.1f)', p.x, p.y))
            end
            imgui.end_table()
        end
    end

    imgui.separator()
    imgui.text(string.format('可攀爬物/绳子 (%d)', #pf_data.climbables))
    if #pf_data.climbables > 0 then
        if imgui.begin_table('ClimbTbl', 5, get_tbl_flags()) then
            imgui.table_setup_column('X', imgui.TableColumnFlags_WidthFixed, 80)
            imgui.table_setup_column('Y', imgui.TableColumnFlags_WidthFixed, 80)
            imgui.table_setup_column('TopY', imgui.TableColumnFlags_WidthFixed, 80)
            imgui.table_setup_column('BottomY', imgui.TableColumnFlags_WidthFixed, 80)
            imgui.table_setup_column('启用', imgui.TableColumnFlags_WidthStretch)
            imgui.table_headers_row()
            for _, c in ipairs(pf_data.climbables) do
                imgui.table_next_row()
                imgui.table_next_column(); imgui.text(string.format('%.2f', c.x))
                imgui.table_next_column(); imgui.text(string.format('%.2f', c.y))
                imgui.table_next_column(); imgui.text(tostring(c.topY or '?'))
                imgui.table_next_column(); imgui.text(tostring(c.bottomY or '?'))
                imgui.table_next_column(); imgui.text(tostring(c.enable or '?'))
            end
            imgui.end_table()
        end
    else
        imgui.text_disabled('未找到可攀爬物')
    end

    imgui.separator()
    imgui.text('碰撞检测')
    kv_table({
        {'ClimbUp', tostring(pf_data.climbUp or '无')},
        {'ClimbDown', tostring(pf_data.climbDown or '无')},
    })
    if pf_data.climbState then
        kv_table({
            {'攀爬实体', pf_data.climbState.entity or '?'},
            {'攀爬顶部', pf_data.climbState.top or '?'},
            {'攀爬底部', pf_data.climbState.bottom or '?'},
        })
    end

    imgui.separator()
    imgui.text(string.format('Foothold 平台 (%d)', #pf_data.footholds))
    if #pf_data.footholds > 0 then
        if imgui.begin_table('FHTbl', 5, get_tbl_flags()) then
            imgui.table_setup_column('名称', imgui.TableColumnFlags_WidthFixed, 80)
            imgui.table_setup_column('子序号', imgui.TableColumnFlags_WidthFixed, 50)
            imgui.table_setup_column('X', imgui.TableColumnFlags_WidthFixed, 80)
            imgui.table_setup_column('Y', imgui.TableColumnFlags_WidthFixed, 80)
            imgui.table_setup_column('子实体', imgui.TableColumnFlags_WidthStretch)
            imgui.table_headers_row()
            for _, f in ipairs(pf_data.footholds) do
                imgui.table_next_row()
                imgui.table_next_column(); imgui.text(f.name or '?')
                imgui.table_next_column(); imgui.text(tostring(f.index))
                imgui.table_next_column(); imgui.text_colored(0.3, 0.9, 0.3, 1.0, string.format('%.2f', f.x))
                imgui.table_next_column(); imgui.text_colored(0.3, 0.9, 0.3, 1.0, string.format('%.2f', f.y))
                imgui.table_next_column(); imgui.text_disabled(f.sub or '?')
            end
            imgui.end_table()
        end
    end

    imgui.separator()
    imgui.text_disabled(string.format('Foothold 详细信息 (%d)', #pf_data.footholdInfo))
    if #pf_data.footholdInfo > 0 then
        if imgui.begin_table('FHInfoTbl', 3, get_tbl_flags()) then
            imgui.table_setup_column('名称', imgui.TableColumnFlags_WidthFixed, 80)
            imgui.table_setup_column('X', imgui.TableColumnFlags_WidthFixed, 60)
            imgui.table_setup_column('Y', imgui.TableColumnFlags_WidthFixed, 60)
            imgui.table_headers_row()
            for _, fi in ipairs(pf_data.footholdInfo) do
                if type(fi) == 'table' and fi.name then
                    imgui.table_next_row()
                    imgui.table_next_column(); imgui.text(fi.name or '?')
                    imgui.table_next_column(); imgui.text(tostring(fi.x))
                    imgui.table_next_column(); imgui.text(tostring(fi.y))
                end
            end
            imgui.end_table()
        end
    end

    -- Foothold 深度探测结果 (第一个 foothold 的组件/属性/子实体)
    if #pf_data.footholdDeep > 0 then
        imgui.separator()
        imgui.text_disabled('Foothold 深度探测 (第一个):')
        if imgui.begin_child('fh_deep', 0, 200) then
            for _, line in ipairs(pf_data.footholdDeep) do
                if line:find('fh_comp') then
                    imgui.text_colored(0.3, 0.9, 0.3, 1.0, line)
                elseif line:find('fh_prop') or line:find('fh_ent_prop') or line:find('fh_child_prop') then
                    imgui.text_colored(0.9, 0.8, 0.3, 1.0, line)
                elseif line:find('fh_child') then
                    imgui.text_colored(0.3, 0.6, 0.9, 1.0, line)
                else
                    imgui.text(line)
                end
            end
            imgui.end_child()
        end
    end

    -- Rigidbody 地面信息
    local rbKeys = {}
    for k in pairs(pf_data.rbGround) do rbKeys[#rbKeys+1] = k end
    if #rbKeys > 0 then
        imgui.separator()
        imgui.text_disabled('Rigidbody 地面信息:')
        local rbList = {}
        for _, k in ipairs(rbKeys) do rbList[#rbList+1] = {k, pf_data.rbGround[k]} end
        kv_table(rbList)
    end

    imgui.separator()
    imgui.text('最近NPC')
    if pf_data.npc then
        kv_table({
            {'Code', tostring(pf_data.npc.code or '?'), 'blue'},
            {'Name', pf_data.npc.name or '?', 'green'},
            {'CanKey', tostring(pf_data.npc.canKey), 'yellow'},
            {'Pos', string.format('(%.1f, %.1f)', pf_data.npc.x, pf_data.npc.y)},
        })
    else
        imgui.text_disabled('附近无NPC')
    end

    imgui.separator()
    imgui.text('地面探测 (移动玩家扫描平台)')
    imgui.same_line()
    imgui.text_disabled('会临时移动玩家, 完成后自动恢复')
    imgui.push_item_width(200)
    local chg, val = imgui.input_text('范围(起|止|步长)##pf_ground_range', pf_ground_range)
    if chg then pf_ground_range = val end
    imgui.pop_item_width()
    imgui.same_line()
    if imgui.button('开始探测', 80, 0) then
        local sx, ex, st = pf_ground_range:match('^(%S+)|(%S+)|(%S+)')
        sx, ex, st = tonumber(sx) or -20, tonumber(ex) or 20, tonumber(st) or 1
        log.info(string.format('[probe_ground] start scan X=[%.1f, %.1f] step=%.2f', sx, ex, st))
        local t = os.clock()
        local pts, err = data.probe_ground(sx, ex, st)
        local elapsed = os.clock() - t
        if pts then
            pf_ground = pts
            log.info(string.format('[probe_ground] done %d points in %.2fs', #pts, elapsed))
        else
            log.info('[probe_ground] failed: ' .. tostring(err))
        end
    end

    if pf_ground and #pf_ground > 0 then
        imgui.separator()
        imgui.text(string.format('地面采样 (%d 点)', #pf_ground))
        if imgui.begin_table('GroundProbeTbl', 3, get_tbl_flags()) then
            imgui.table_setup_column('X', imgui.TableColumnFlags_WidthFixed, 80)
            imgui.table_setup_column('Y(地面)', imgui.TableColumnFlags_WidthFixed, 80)
            imgui.table_setup_column('可视化', imgui.TableColumnFlags_WidthStretch)
            imgui.table_headers_row()
            -- 找出 Y 范围用于可视化
            local minY, maxY = pf_ground[1].y, pf_ground[1].y
            for _, pt in ipairs(pf_ground) do
                if pt.y < minY then minY = pt.y end
                if pt.y > maxY then maxY = pt.y end
            end
            local yRange = maxY - minY
            if yRange < 0.01 then yRange = 1 end
            for _, pt in ipairs(pf_ground) do
                imgui.table_next_row()
                imgui.table_next_column(); imgui.text(string.format('%.2f', pt.x))
                imgui.table_next_column(); imgui.text_colored(0.3, 0.9, 0.3, 1.0, string.format('%.2f', pt.y))
                imgui.table_next_column()
                -- 简单文本可视化: 用 # 表示高度
                local barLen = math.floor((pt.y - minY) / yRange * 30)
                if barLen < 0 then barLen = 0 end
                if barLen > 30 then barLen = 30 end
                imgui.text_disabled(string.rep('#', barLen))
            end
            imgui.end_table()
        end
    end
end

local function draw_dump_raw(height)
    if dump_last.title == '' then
        imgui.text_disabled('还没有探测结果')
        return
    end
    imgui.text_colored(0.3, 0.6, 0.9, 1.0, '最后结果: ' .. dump_last.title)
    if imgui.begin_child('dump_raw_text', 0, height or 180) then
        for _, line in ipairs(dump_last.raw_lines or {}) do imgui.text(line) end
        imgui.end_child()
    end
end

local function draw_dump_state_table()
    local keys = {}
    for k in pairs(dump_last.state or {}) do keys[#keys + 1] = k end
    table.sort(keys)
    if #keys == 0 then imgui.text_disabled('点击 状态探测 后显示'); return end
    if imgui.begin_table('DumpStateTbl', 2, get_tbl_flags()) then
        imgui.table_setup_column('字段', imgui.TableColumnFlags_WidthFixed, 180)
        imgui.table_setup_column('值', imgui.TableColumnFlags_WidthStretch)
        imgui.table_headers_row()
        for _, k in ipairs(keys) do
            imgui.table_next_row()
            imgui.table_next_column(); imgui.text(k)
            imgui.table_next_column(); imgui.text(tostring(dump_last.state[k]))
        end
        imgui.end_table()
    end
end

local function draw_dump_npc_table()
    if #(dump_last.npcs or {}) == 0 then imgui.text_disabled('点击 刷新NPC列表 后显示'); return end
    if imgui.begin_table('DumpNpcTbl', 7, get_tbl_flags()) then
        imgui.table_setup_column('操作', imgui.TableColumnFlags_WidthFixed, 110)
        imgui.table_setup_column('名称', imgui.TableColumnFlags_WidthStretch)
        imgui.table_setup_column('NPC代码', imgui.TableColumnFlags_WidthFixed, 90)
        imgui.table_setup_column('坐标', imgui.TableColumnFlags_WidthFixed, 120)
        imgui.table_setup_column('可按键', imgui.TableColumnFlags_WidthFixed, 70)
        imgui.table_setup_column('只点击', imgui.TableColumnFlags_WidthFixed, 70)
        imgui.table_setup_column('来源', imgui.TableColumnFlags_WidthFixed, 80)
        imgui.table_headers_row()
        for i, n in ipairs(dump_last.npcs) do
            imgui.table_next_row()
            imgui.table_next_column()
            if imgui.button('填入##npc_fill_' .. i, 45, 0) then dump_npc_code = tostring(n.code or '') end
            imgui.same_line()
            if imgui.button('对话##npc_talk_' .. i, 45, 0) then
                dump_npc_code = tostring(n.code or '')
                dump_exec('npc_chat(' .. dump_npc_code .. ')', function() return data.npc_chat(dump_npc_code) end)
            end
            imgui.table_next_column(); imgui.text(n.name or '')
            imgui.table_next_column(); imgui.text(n.code or '')
            imgui.table_next_column(); imgui.text(string.format('%s, %s', n.x or '', n.y or ''))
            imgui.table_next_column(); imgui.text(n.canKey or '')
            imgui.table_next_column(); imgui.text(n.onlyClick or '')
            imgui.table_next_column(); imgui.text(n.label or '')
        end
        imgui.end_table()
    end
end

local function draw_dump_shop_table()
    if #(dump_last.items or {}) == 0 then imgui.text_disabled('点击 遍历商店 后显示'); return end
    if imgui.begin_table('DumpShopTbl', 10, get_tbl_flags()) then
        imgui.table_setup_column('来源', imgui.TableColumnFlags_WidthFixed, 55)
        imgui.table_setup_column('类型', imgui.TableColumnFlags_WidthFixed, 75)
        imgui.table_setup_column('NPC', imgui.TableColumnFlags_WidthFixed, 80)
        imgui.table_setup_column('Key', imgui.TableColumnFlags_WidthFixed, 70)
        imgui.table_setup_column('槽位', imgui.TableColumnFlags_WidthFixed, 45)
        imgui.table_setup_column('物品ID', imgui.TableColumnFlags_WidthFixed, 85)
        imgui.table_setup_column('名称', imgui.TableColumnFlags_WidthStretch)
        imgui.table_setup_column('价格', imgui.TableColumnFlags_WidthFixed, 70)
        imgui.table_setup_column('需求', imgui.TableColumnFlags_WidthFixed, 80)
        imgui.table_setup_column('数量', imgui.TableColumnFlags_WidthFixed, 55)
        imgui.table_headers_row()
        for _, it in ipairs(dump_last.items) do
            imgui.table_next_row()
            imgui.table_next_column(); imgui.text(it.source or '')
            imgui.table_next_column(); imgui.text(it.kind or '')
            imgui.table_next_column(); imgui.text(it.npc or '')
            imgui.table_next_column(); imgui.text(it.key or '')
            imgui.table_next_column(); imgui.text(it.slot or '')
            imgui.table_next_column(); imgui.text(it.item or '')
            imgui.table_next_column(); imgui.text(it.name or '')
            imgui.table_next_column(); imgui.text(it.price or '')
            imgui.table_next_column(); imgui.text(it.req or '')
            imgui.table_next_column(); imgui.text(it.count or it.stock or '')
        end
        imgui.end_table()
    end
end

local function draw_dump_dialogue_table()
    if #(dump_last.options or {}) == 0 then imgui.text_disabled('点击 刷新对话按钮/选项 后显示'); return end
    if imgui.begin_table('DumpDialogueTbl', 8, get_tbl_flags()) then
        imgui.table_setup_column('操作', imgui.TableColumnFlags_WidthFixed, 60)
        imgui.table_setup_column('类型', imgui.TableColumnFlags_WidthFixed, 100)
        imgui.table_setup_column('序号', imgui.TableColumnFlags_WidthFixed, 45)
        imgui.table_setup_column('按钮/实体', imgui.TableColumnFlags_WidthFixed, 120)
        imgui.table_setup_column('Value', imgui.TableColumnFlags_WidthFixed, 80)
        imgui.table_setup_column('文本', imgui.TableColumnFlags_WidthStretch)
        imgui.table_setup_column('可见', imgui.TableColumnFlags_WidthFixed, 55)
        imgui.table_setup_column('组件', imgui.TableColumnFlags_WidthFixed, 160)
        imgui.table_headers_row()
        for i, opt in ipairs(dump_last.options) do
            imgui.table_next_row()
            imgui.table_next_column()
            if imgui.button('点击##dlg_opt_' .. i, 48, 0) then
                dump_dialog_select_value = tostring(opt.value or '')
                dump_dialog_select_index = tostring(i)
                local k = dump_manager_kind_from_option(opt)
                dump_exec('dialogue_select(' .. dump_dialog_select_value .. ',' .. dump_dialog_select_index .. ',' .. k .. ')', function()
                    return data.dialogue_select(dump_dialog_select_value, dump_dialog_select_index, k)
                end)
            end
            imgui.table_next_column(); imgui.text(opt.kind or '')
            imgui.table_next_column(); imgui.text(opt.index or '')
            imgui.table_next_column(); imgui.text(opt.name or '')
            imgui.table_next_column(); imgui.text(opt.value or '')
            imgui.table_next_column(); imgui.text(opt.text or '')
            imgui.table_next_column(); imgui.text(opt.enable or '')
            imgui.table_next_column(); imgui.text(opt.comps or '')
        end
        imgui.end_table()
    end
end

local function tab_dump_tools()
    imgui.text('Dump 功能测试')
    imgui.same_line()
    imgui.text_disabled('结果会显示在当前页底部和 结果 页签')
    imgui.separator()

    if imgui.begin_tab_bar('DumpSubTabs') then
        if imgui.begin_tab_item('状态##dump_state') then
            if imgui.button('状态探测', 90, 0) then
                dump_exec('probe_action_state', function() return data.probe_action_state() end)
            end
            imgui.same_line()
            if imgui.button('动作自检', 90, 0) then
                dump_exec('action_selftest', function() return data.action_selftest() end)
            end
            imgui.same_line()
            if imgui.button('传送探测', 90, 0) then
                dump_exec('teleport_probe', function() return data.teleport_probe() end, function()
                    if dump_last.teleport.CurrentMapName then dump_tp_map = tostring(dump_last.teleport.CurrentMapName) end
                    local pos = dump_last.teleport.pos
                    if pos then
                        local x, y = tostring(pos):match('([^,]+),([^,]+)')
                        if x then dump_tp_x = x end
                        if y then dump_tp_y = y end
                    end
                end)
            end
            imgui.same_line()
            if imgui.button('NPC探测', 80, 0) then
                dump_exec('npc_probe', function() return data.npc_probe() end)
            end
            imgui.same_line()
            if imgui.button('对话探测', 80, 0) then
                dump_exec('dialogue_probe', function() return data.dialogue_probe() end)
            end

            imgui.separator()
            local ch_inv, v_inv = imgui.checkbox('无敌(标志+锁血兜底)', action_invincible)
            if ch_inv then
                action_invincible = v_inv
                dump_exec('set_invincible(' .. tostring(v_inv) .. ')', function() return data.set_invincible(v_inv) end)
            end
            imgui.same_line()
            local ch_hp, v_hp = imgui.checkbox('锁血', action_hp_lock)
            if ch_hp then
                action_hp_lock = v_hp
                dump_exec('set_hp_lock(' .. tostring(v_hp) .. ')', function() return data.set_hp_lock(v_hp) end)
            end
            imgui.same_line()
            local ch_nk, v_nk = imgui.checkbox('不击退', action_no_knockback)
            if ch_nk then
                action_no_knockback = v_nk
                dump_exec('set_no_knockback(' .. tostring(v_nk) .. ')', function() return data.set_no_knockback(v_nk) end)
            end
            imgui.same_line()
            local ch_float, v_float = imgui.checkbox('悬浮', action_float)
            if ch_float then
                action_float = v_float
                dump_exec('float(' .. tostring(v_float) .. ')', function() return data.float(v_float) end)
            end
            imgui.same_line()
            local ch_admin, v_admin = imgui.checkbox('高机动移动', dump_admin_move)
            if ch_admin then
                dump_admin_move = v_admin
                dump_exec('admin_move(' .. tostring(v_admin) .. ')', function() return data.admin_move(v_admin) end)
            end
            imgui.text_disabled('dump 里的 SetInvincible 是空函数；无敌目前会同时启用锁血兜底。')
            if action_invincible or action_hp_lock or action_no_knockback or action_float then
                if action_maintain_error ~= '' then
                    imgui.text_colored(0.9, 0.3, 0.3, 1.0, '维持错误: ' .. action_maintain_error)
                else
                    imgui.text_disabled('维持状态: ' .. action_maintain_last)
                end
            end
            imgui.separator()
            draw_dump_state_table()
            imgui.end_tab_item()
        end

        if imgui.begin_tab_item('瞬移##dump_teleport') then
            if imgui.button('读取当前地图和坐标', 150, 0) then
                dump_exec('teleport_probe', function() return data.teleport_probe() end, function()
                    if dump_last.teleport.CurrentMapName then dump_tp_map = tostring(dump_last.teleport.CurrentMapName) end
                    local pos = dump_last.teleport.pos
                    if pos then
                        local x, y = tostring(pos):match('([^,]+),([^,]+)')
                        if x then dump_tp_x = x end
                        if y then dump_tp_y = y end
                    end
                end)
            end
            imgui.same_line()
            local ch_force, v_force = imgui.checkbox('强制传送', dump_tp_force)
            if ch_force then dump_tp_force = v_force end
            imgui.text_disabled('地图ID留空表示当前地图；X/Y留空表示当前位置。')

            imgui.push_item_width(140)
            local ch_map, v_map = imgui.input_text('地图ID##dump_tp_map', dump_tp_map)
            if ch_map then dump_tp_map = v_map end
            imgui.pop_item_width()
            imgui.same_line()
            imgui.push_item_width(100)
            local ch_x, v_x = imgui.input_text('X坐标##dump_tp_x', dump_tp_x)
            if ch_x then dump_tp_x = v_x end
            imgui.pop_item_width()
            imgui.same_line()
            imgui.push_item_width(100)
            local ch_y, v_y = imgui.input_text('Y坐标##dump_tp_y', dump_tp_y)
            if ch_y then dump_tp_y = v_y end
            imgui.pop_item_width()

            if imgui.button('传到当前坐标', 120, 0) then
                dump_exec('teleport_to_position', function()
                    return data.teleport_to_position(dump_tp_map, dump_tp_x, dump_tp_y, dump_tp_force)
                end)
            end
            imgui.same_line()
            if imgui.button('回出生点', 90, 0) then
                dump_exec('teleport_to_spawn', function()
                    return data.teleport_to_spawn(dump_tp_map, dump_tp_force)
                end)
            end
            imgui.same_line()
            imgui.push_item_width(110)
            local ch_portal, v_portal = imgui.input_text('传送门名##dump_tp_portal', dump_tp_portal)
            if ch_portal then dump_tp_portal = v_portal end
            imgui.pop_item_width()
            imgui.same_line()
            if imgui.button('传到传送门', 110, 0) then
                dump_exec('teleport_to_portal', function()
                    return data.teleport_to_portal(dump_tp_map, dump_tp_portal, dump_tp_force)
                end)
            end
            imgui.separator()
            draw_dump_state_table()
            imgui.end_tab_item()
        end

        if imgui.begin_tab_item('NPC商店##dump_npc_shop') then
            if imgui.button('刷新NPC列表', 110, 0) then
                dump_exec('npc_probe', function() return data.npc_probe() end)
            end
            imgui.same_line()
            if imgui.button('遍历当前商店面板', 140, 0) then
                dump_exec('shop_panel_probe', function() return data.shop_panel_probe() end)
            end
            imgui.same_line()
            if imgui.button('遍历该NPC商店', 120, 0) then
                dump_exec('shop_probe(' .. dump_npc_code .. ')', function() return data.shop_probe(dump_npc_code, dump_shop_key) end)
            end

            imgui.push_item_width(120)
            local ch_npc, v_npc = imgui.input_text('NPC代码##dump_npc_code', dump_npc_code)
            if ch_npc then dump_npc_code = v_npc end
            imgui.pop_item_width()
            imgui.same_line()
            imgui.push_item_width(100)
            local ch_act, v_act = imgui.input_text('NPC动作##dump_npc_action', dump_npc_action)
            if ch_act then dump_npc_action = v_act end
            imgui.pop_item_width()
            imgui.same_line()
            imgui.push_item_width(100)
            local ch_key, v_key = imgui.input_text('活动商店Key##dump_shop_key', dump_shop_key)
            if ch_key then dump_shop_key = v_key end
            imgui.pop_item_width()

            if imgui.button('最近NPC对话', 110, 0) then
                dump_exec('npc_chat(nearest)', function() return data.npc_chat('') end)
            end
            imgui.same_line()
            if imgui.button('指定NPC对话', 110, 0) then
                dump_exec('npc_chat(' .. dump_npc_code .. ')', function() return data.npc_chat(dump_npc_code) end)
            end
            imgui.same_line()
            if imgui.button('执行NPC动作', 110, 0) then
                dump_exec('npc_special_act', function() return data.npc_special_act(dump_npc_code, dump_npc_action) end)
            end
            imgui.same_line()
            if imgui.button('打开商店', 90, 0) then
                dump_exec('shop_open', function() return data.shop_open(dump_npc_code, dump_shop_key) end)
            end

            imgui.separator()
            imgui.text_disabled(string.format('NPC %d 个，商店物品 %d 个', #(dump_last.npcs or {}), #(dump_last.items or {})))
            draw_dump_npc_table()
            imgui.separator()
            draw_dump_shop_table()
            imgui.end_tab_item()
        end

        if imgui.begin_tab_item('对话##dump_dialogue') then
            if imgui.button('刷新对话按钮/选项', 140, 0) then
                dump_exec('dialogue_options_probe', function() return data.dialogue_options_probe() end)
            end
            imgui.same_line()
            if imgui.button('刷新对话管理器', 120, 0) then
                dump_exec('dialogue_probe', function() return data.dialogue_probe() end)
            end
            imgui.same_line()
            local ch_kind, v_kind = imgui.combo('对话类型##dump_dialog_kind', dump_dialog_kind_index, dump_dialog_kind_names)
            if ch_kind then dump_dialog_kind_index = v_kind end
            imgui.same_line()
            if imgui.button('停止对话##dump_dialog_close', 100, 0) then
                dump_exec('dialogue_close', function() return data.dialogue_close() end)
            end
            local kind = dump_dialog_kinds[dump_dialog_kind_index + 1] or 'all'

            imgui.push_item_width(110)
            local ch_btn, v_btn = imgui.input_text('按钮名##dump_dialog_button', dump_dialog_button)
            if ch_btn then dump_dialog_button = v_btn end
            imgui.pop_item_width()
            imgui.same_line()
            if imgui.button('点击按钮', 80, 0) then
                dump_exec('dialogue_button(' .. dump_dialog_button .. ',' .. kind .. ')', function()
                    return data.dialogue_button(dump_dialog_button, kind)
                end)
            end
            imgui.same_line()
            imgui.push_item_width(100)
            local ch_sel, v_sel = imgui.input_text('选项Value##dump_dialog_select_value', dump_dialog_select_value)
            if ch_sel then dump_dialog_select_value = v_sel end
            imgui.pop_item_width()
            imgui.same_line()
            imgui.push_item_width(80)
            local ch_idx, v_idx = imgui.input_text('序号##dump_dialog_select_index', dump_dialog_select_index)
            if ch_idx then dump_dialog_select_index = v_idx end
            imgui.pop_item_width()
            imgui.same_line()
            if imgui.button('点击选项', 80, 0) then
                dump_exec('dialogue_select', function()
                    return data.dialogue_select(dump_dialog_select_value, dump_dialog_select_index, kind)
                end)
            end

            local quick = {'ok','next','prev','yes','no','accept','reject','complete','func','lost'}
            for i, name in ipairs(quick) do
                if (i - 1) % 5 ~= 0 then imgui.same_line() end
                if imgui.button(name .. '##quick_dialog_' .. name, 80, 0) then
                    dump_dialog_button = name
                    dump_exec('dialogue_button(' .. name .. ')', function() return data.dialogue_button(name, kind) end)
                end
            end

            imgui.separator()
            imgui.text_disabled(string.format('对话选项 %d 个', #(dump_last.options or {})))
            draw_dump_dialogue_table()
            if #(dump_last.panels or {}) > 0 then
                imgui.separator()
                imgui.text_disabled('面板/队列')
                for _, line in ipairs(dump_last.panels) do imgui.text(line) end
            end
            imgui.end_tab_item()
        end

        if imgui.begin_tab_item('结果##dump_result') then
            draw_dump_raw(260)
            imgui.separator()
            draw_dump_action_log(220)
            imgui.end_tab_item()
        end
        imgui.end_tab_bar()
    end
end

-- ============================================================================
-- 主窗口
-- ============================================================================
local function on_render()
    maintain_actions()
    if not window_open then return end

    local sw, sh = imgui.get_screen_size()
    imgui.set_next_window_pos(sw * 0.05, sh * 0.05, imgui.Cond_FirstUseEver)
    imgui.set_next_window_size(sw * 0.45, sh * 0.88, imgui.Cond_FirstUseEver)

    local visible, open = imgui.begin_window('CSZF 数据探测', true)
    if not open then
        window_open = false
        imgui.end_window()
        return
    end
    if not visible then imgui.end_window(); return end

    -- 控制栏
    if imgui.button('刷新全部', 80, 0) then refresh('all') end
    imgui.same_line()
    local ch, val = imgui.checkbox('自动', auto_refresh)
    if ch then auto_refresh = val end
    if auto_refresh then
        imgui.same_line()
        imgui.push_item_width(90)
        local sv, sv2 = imgui.slider_float('##interval', auto_interval, 0.5, 10.0, '%.1fs')
        if sv then auto_interval = sv2 end
        imgui.pop_item_width()
        if os.clock() - (D_time.nearby or 0) > auto_interval then refresh('nearby') end
    end
    imgui.same_line()
    imgui.text_disabled('Ctrl+F12 退出')

    imgui.separator()

    -- 标签页
    if imgui.begin_tab_bar('MainTabs') then
        if imgui.begin_tab_item('角色信息##tab_player') then
            if imgui.button('刷新##btn_player', 60, 0) then refresh('player') end
            imgui.same_line()
            if D_time.player then imgui.text_disabled(string.format('%.0fs前', os.clock() - D_time.player)) end
            imgui.separator()
            tab_player()
            imgui.end_tab_item()
        end
        if imgui.begin_tab_item('背包##tab_inv') then
            if imgui.button('刷新##btn_inv', 60, 0) then refresh('inventory') end
            imgui.same_line()
            if D_time.inventory then imgui.text_disabled(string.format('%.0fs前', os.clock() - D_time.inventory)) end
            imgui.separator()
            tab_inventory()
            imgui.end_tab_item()
        end
        if imgui.begin_tab_item('技能##tab_skill') then
            if imgui.button('刷新##btn_skill', 60, 0) then refresh('skills') end
            imgui.same_line()
            if D_time.skills then imgui.text_disabled(string.format('%.0fs前', os.clock() - D_time.skills)) end
            imgui.separator()
            tab_skills()
            imgui.end_tab_item()
        end
        if imgui.begin_tab_item('传送门##tab_portal') then
            if imgui.button('刷新##btn_portal', 60, 0) then refresh('portals') end
            imgui.same_line()
            if D_time.portals then imgui.text_disabled(string.format('%.0fs前', os.clock() - D_time.portals)) end
            imgui.separator()
            tab_portals()
            imgui.end_tab_item()
        end
        if imgui.begin_tab_item(string.format('周围实体##tab_nearby')) then
            if imgui.button('刷新##btn_nearby', 60, 0) then refresh('nearby') end
            imgui.same_line()
            if D_time.nearby then imgui.text_disabled(string.format('%.0fs前', os.clock() - D_time.nearby)) end
            imgui.separator()
            tab_nearby()
            imgui.end_tab_item()
        end
        if imgui.begin_tab_item('角色列表##tab_char') then
            if imgui.button('刷新##btn_char', 60, 0) then refresh('characters') end
            imgui.same_line()
            if D_time.characters then imgui.text_disabled(string.format('%.0fs前', os.clock() - D_time.characters)) end
            imgui.separator()
            tab_characters()
            imgui.end_tab_item()
        end
        if imgui.begin_tab_item('快捷栏##tab_qs') then
            if imgui.button('刷新##btn_qs', 60, 0) then refresh('quickslot') end
            imgui.same_line()
            if D_time.quickslot then imgui.text_disabled(string.format('%.0fs前', os.clock() - D_time.quickslot)) end
            imgui.separator()
            tab_quickslot()
            imgui.end_tab_item()
        end
        if imgui.begin_tab_item('Call 测试##tab_call') then
            tab_call()
            imgui.end_tab_item()
        end
        if imgui.begin_tab_item('动作测试##tab_actions') then
            tab_actions()
            imgui.end_tab_item()
        end
        if imgui.begin_tab_item('Dump功能##tab_dump_tools') then
            tab_dump_tools()
            imgui.end_tab_item()
        end
        if imgui.begin_tab_item('寻路数据##tab_pathfind') then
            if imgui.button('刷新##btn_pf', 60, 0) then
                local t = os.clock()
                pf_data = data.probe_pathfind()
                pf_time = os.clock() - t
            end
            imgui.same_line()
            if pf_time > 0 then imgui.text_disabled(string.format('%.1fs前', os.clock() - pf_time)) end
            imgui.separator()
            tab_pathfind()
            imgui.end_tab_item()
        end
        imgui.end_tab_bar()
    end

    imgui.end_window()
end

-- ============================================================================
-- 启动
-- ============================================================================
apply_theme()

if imgui.is_initialized() then
    imgui.on_render(on_render)
    log.info('编辑器模式: 数据探测 UI 已注册, 按 Ctrl+F12 退出')
    hotkey.start(10)
    while window_open do
        if hotkey.is_pressed(0x11) and hotkey.is_pressed(0x7B) then
            log.info('Ctrl+F12 退出')
            break
        end
        sys.sleep(20)
    end
    hotkey.stop()
    imgui.clear_render_callback()
    log.info('数据探测 UI 已关闭')
else
    imgui.init('CSZF 数据探测')
    imgui.on_render(on_render)
    log.info('独立模式: 关闭窗口退出')
    imgui.run()
    log.info('数据探测 UI 已关闭')
end
