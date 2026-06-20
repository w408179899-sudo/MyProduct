-- ============================================================================
-- entity_ui.lua  CSZF 数据探测 ImGui 界面
-- ============================================================================
local data = require 'data'

local ok, pid = data.connect()
if not ok then log.error(tostring(pid)); return end
log.info('游戏连接成功, PID=' .. pid)

-- ============================================================================
-- 主题
-- ============================================================================
local function apply_theme()
    imgui.set_style_colors({
        [imgui.Col_WindowBg]          = { r = 0.06, g = 0.06, b = 0.08, a = 1.0 },
        [imgui.Col_TitleBg]           = { r = 0.10, g = 0.10, b = 0.14, a = 1.0 },
        [imgui.Col_TitleBgActive]     = { r = 0.14, g = 0.14, b = 0.20, a = 1.0 },
        [imgui.Col_FrameBg]           = { r = 0.05, g = 0.05, b = 0.07, a = 1.0 },
        [imgui.Col_FrameBgHovered]    = { r = 0.12, g = 0.12, b = 0.16, a = 1.0 },
        [imgui.Col_FrameBgActive]     = { r = 0.16, g = 0.16, b = 0.22, a = 1.0 },
        [imgui.Col_Button]            = { r = 0.12, g = 0.12, b = 0.16, a = 1.0 },
        [imgui.Col_ButtonHovered]     = { r = 0.20, g = 0.18, b = 0.28, a = 1.0 },
        [imgui.Col_ButtonActive]      = { r = 0.28, g = 0.24, b = 0.40, a = 1.0 },
        [imgui.Col_Tab]               = { r = 0.08, g = 0.08, b = 0.12, a = 1.0 },
        [imgui.Col_TabSelected]       = { r = 0.16, g = 0.16, b = 0.22, a = 1.0 },
        [imgui.Col_TabSelectedOverline]= { r = 0.55, g = 0.40, b = 0.20, a = 1.0 },
        [imgui.Col_Header]            = { r = 0.12, g = 0.12, b = 0.16, a = 1.0 },
        [imgui.Col_HeaderHovered]     = { r = 0.55, g = 0.40, b = 0.20, a = 0.30 },
        [imgui.Col_TableHeaderBg]     = { r = 0.10, g = 0.10, b = 0.14, a = 1.0 },
        [imgui.Col_TableRowBgAlt]     = { r = 1, g = 1, b = 1, a = 0.02 },
        [imgui.Col_CheckMark]         = { r = 0.82, g = 0.52, b = 0.18, a = 1.0 },
        [imgui.Col_SliderGrab]        = { r = 0.58, g = 0.37, b = 0.13, a = 1.0 },
        [imgui.Col_SliderGrabActive]  = { r = 0.82, g = 0.52, b = 0.18, a = 1.0 },
    })
    imgui.set_style({
        window_rounding = 8, frame_rounding = 6, tab_rounding = 5,
        grab_rounding = 6, scrollbar_rounding = 10,
        window_padding = { x = 12, y = 10 },
        frame_padding  = { x = 8, y = 5 },
        item_spacing   = { x = 8, y = 6 },
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
local action_log = {}

local qs_set_slot = '1'
local qs_set_id = ''
local qs_set_type = 'item'
local qs_use_slot = '1'
local qs_action_index = 0
local qs_actions = { 'press', 'hold', 'release' }

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
    imgui.text(string.format('怪物:%d  掉落:%d  传送门:%d  NPC:%d',
        nb.mobCount or 0, nb.dropCount or 0, nb.portalCount or 0, nb.npcCount or 0))
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
-- 主窗口
-- ============================================================================
local function on_render()
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
