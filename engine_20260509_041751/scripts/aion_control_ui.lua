--[[
    Aion automation control UI draft.

    This file is intentionally UI/config only. It does not start combat,
    gathering, movement, buying, teleporting, or any other live action.

    Hotkeys:
      F7       show/hide window
      F8       start/stop
      F9       run safe API probe
      F10      pause/resume
      Ctrl+F12 exit
]]

local ok_core, core = pcall(require, "aion.core")
local ok_probe, probe = pcall(require, "aion.probe")
local ok_entity, entity = pcall(require, "aion.entity")
local ok_inventory, inventory = pcall(require, "aion.inventory")
local ok_quest, quest = pcall(require, "aion.quest")
local ok_combat, combat = pcall(require, "aion.combat")
local ok_map, map = pcall(require, "aion.map")

local runtime = {
    running = false,
    paused = false,
    status = "已停止",
    active_mode = "none",
    last_event = "",
    last_probe = "未运行",
    frame = 0,
    ui_visible = true,
    audit = {
        started_at = 0,
        last_sample_at = 0,
        elapsed_seconds = 0,
        samples = 0,
        kills_est = 0,
        gather_est = 0,
        material_gain = 0,
        exp_gain = 0,
        kinah_gain = 0,
        seen_loot = {},
        last_inventory_counts = nil,
        last_level = nil,
        last_exp = nil,
        last_max_exp = nil,
        last_kinah = nil,
        last_error = "",
        current = {
            hp = 0,
            max_hp = 0,
            mp = 0,
            max_mp = 0,
            level = 0,
            map = "",
            entities = 0,
            inventory = 0,
            quests = 0,
            target_id = 0,
        },
    },
}

local cfg = {
    profile_name = "默认方案",
    primary_mode = 1,
    priority_mode = 1,

    combat = {
        enabled = true,
        mode = 1,
        target_policy = 1,
        radius = 35,
        min_level = 1,
        max_level = 99,
        prefer_quest_targets = true,
        avoid_elite = true,
        keep_auto_battle = false,
        target_names = "",
        blacklist_names = "",
    },

    gather = {
        enabled = false,
        mode = 1,
        radius = 30,
        gather_herb = true,
        gather_ore = true,
        gather_resource = true,
        gather_after_combat = true,
        resource_names = "",
        blacklist_names = "",
    },

    route = {
        active_tab = 1,
        selected_route = 1,
        loop = true,
        reverse_on_end = false,
        stop_on_death = true,
        record_interval = 1.5,
        waypoint_radius = 3,
        route_name = "主路径",
        revive_route_name = "复活路径",
        vendor_route_name = "补给路径",
        route_points = "",
        revive_points = "",
        vendor_points = "",
    },

    leveling = {
        enabled = true,
        mode = 1,
        start_level = 1,
        target_level = 50,
        prefer_quest = true,
        allow_grind = true,
        allow_gather = false,
        learn_skills = true,
        equip_upgrades = true,
    },

    crafting = {
        enabled = false,
        profession = 1,
        item_name = "",
        craft_count = 10,
        stop_when_missing_material = true,
        reserve_kinah = 1000,
        material_rules = "",
    },

    supply = {
        hp_percent = 35,
        mp_percent = 25,
        bag_full_percent = 85,
        min_kinah = 0,
        buy_hp_potion = 50,
        buy_mp_potion = 50,
        vendor_name = "",
        keep_items = "",
        sell_rules = "",
    },

    safety = {
        max_failures = 5,
        max_stuck_seconds = 20,
        max_deaths = 3,
        stop_on_unknown_map = true,
        stop_on_api_fail = true,
        circuit_breaker = true,
    },

    audit = {
        enabled = true,
        sample_interval = 2.0,
        show_details = false,
        reset_on_start = true,
        material_keywords = "材料\n粉末\n精气\n精髓\n矿\n草\n재료\n광석\n약초\n정수\n가루",
    },
}

local primary_modes = {
    "练级",
    "打怪",
    "采集",
    "制作",
    "路径测试",
    "调试",
}

local priority_modes = {
    "优先打怪",
    "优先采集",
    "只打怪",
    "只采集",
    "任务优先",
    "安全优先",
}

local combat_modes = {
    "原地打怪",
    "路径打怪",
    "任务目标打怪",
    "定点循环打怪",
}

local combat_target_policies = {
    "最近目标",
    "任务目标",
    "低血量目标",
    "威胁目标",
    "指定名字",
}

local gather_modes = {
    "原地采集",
    "路径采集",
    "战后采集",
    "只采任务资源",
}

local leveling_modes = {
    "任务练级",
    "刷怪练级",
    "采集练级",
    "混合练级",
}

local professions = {
    "炼金",
    "料理",
    "武器",
    "防具",
    "裁缝",
    "手工",
}

local route_names = {
    "主路径",
    "复活路径",
    "补给路径",
    "采集路径",
    "练级路径",
}

local function log_info(msg)
    if log and log.info then
        log.info(msg)
    elseif print then
        print(msg)
    end
end

local function log_warn(msg)
    if log and log.warn then
        log.warn(msg)
    else
        log_info(msg)
    end
end

local function now_seconds()
    return os.clock()
end

local function count_array(list)
    if type(list) ~= "table" then
        return 0
    end

    local n = 0
    for _ in ipairs(list) do
        n = n + 1
    end
    return n
end

local function count_lines(text)
    local n = 0
    for line in string.gmatch(text or "", "[^\r\n]+") do
        if line ~= "" then
            n = n + 1
        end
    end
    return n
end

local function audit_reset()
    local a = runtime.audit
    a.started_at = now_seconds()
    a.last_sample_at = 0
    a.elapsed_seconds = 0
    a.samples = 0
    a.kills_est = 0
    a.gather_est = 0
    a.material_gain = 0
    a.exp_gain = 0
    a.kinah_gain = 0
    a.seen_loot = {}
    a.seen_loot_ready = false
    a.last_inventory_counts = nil
    a.last_level = nil
    a.last_exp = nil
    a.last_max_exp = nil
    a.last_kinah = nil
    a.last_error = ""
end

local function audit_rate(value)
    local hours = runtime.audit.elapsed_seconds / 3600
    if hours <= 0 then
        return 0
    end
    return value / hours
end

local function audit_loot_key(e)
    return tostring(e.obj or e.IEntity or e.id or "") .. ":" .. tostring(e.name or "")
end

local function audit_is_material_item(item)
    local text = tostring(item.text or item.name or "") .. " " .. tostring(item.cat_name or "")
    for keyword in string.gmatch(cfg.audit.material_keywords or "", "[^\r\n]+") do
        if keyword ~= "" and string.find(text, keyword, 1, true) then
            return true
        end
    end
    return false
end

local function audit_inventory_counts(items)
    local counts = {}
    for _, item in ipairs(items or {}) do
        if audit_is_material_item(item) then
            local key = tostring(item.text or item.name or item.id or "")
            counts[key] = (counts[key] or 0) + (item.count or 1)
        end
    end
    return counts
end

local function audit_positive_delta(prev, cur)
    if not prev then
        return 0
    end

    local delta = 0
    for key, value in pairs(cur) do
        local old = prev[key] or 0
        if value > old then
            delta = delta + value - old
        end
    end
    return delta
end

local function audit_sample()
    if not cfg.audit.enabled then
        return
    end

    local a = runtime.audit
    local now = now_seconds()
    if a.last_sample_at > 0 and now - a.last_sample_at < cfg.audit.sample_interval then
        return
    end
    a.last_sample_at = now
    a.samples = a.samples + 1

    local count_gains = runtime.running and not runtime.paused
    if a.started_at <= 0 then
        a.started_at = now
    end
    if count_gains then
        a.elapsed_seconds = now - a.started_at
    end

    if ok_core and core then
        local ok, char, err = core.getCharacter()
        if ok and char then
            a.current.hp = char.hp or 0
            a.current.max_hp = char.mhp or char.max_hp or 0
            a.current.mp = char.mp or 0
            a.current.max_mp = char.mmp or char.max_mp or 0
            a.current.level = char.level or 0

            if count_gains and a.last_exp ~= nil then
                local exp_delta = 0
                if (char.level or 0) == (a.last_level or 0) then
                    exp_delta = (char.exp or 0) - (a.last_exp or 0)
                elseif (char.level or 0) > (a.last_level or 0) then
                    exp_delta = math.max(0, (a.last_max_exp or 0) - (a.last_exp or 0)) + (char.exp or 0)
                end
                if exp_delta > 0 then
                    a.exp_gain = a.exp_gain + exp_delta
                end
            end

            a.last_level = char.level or 0
            a.last_exp = char.exp or 0
            a.last_max_exp = char.max_exp or 0
        elseif err then
            a.last_error = tostring(err)
        end
    end

    if ok_map and map then
        local map_ok, cur_map = map.current()
        if map_ok and cur_map then
            a.current.map = cur_map.region or cur_map.name_cn or cur_map.name_en or ""
        end
    end

    if ok_entity and entity then
        local list_ok, list, list_err = entity.list()
        if list_ok and list then
            a.current.entities = count_array(list)
            local new_loot = 0
            for _, e in ipairs(list) do
                if (e.lootable or 0) ~= 0 then
                    local key = audit_loot_key(e)
                    if a.seen_loot_ready and not a.seen_loot[key] and count_gains then
                        new_loot = new_loot + 1
                    end
                    a.seen_loot[key] = true
                end
            end
            if not a.seen_loot_ready then
                a.seen_loot_ready = true
            end
            a.kills_est = a.kills_est + new_loot
        elseif list_err then
            a.last_error = tostring(list_err)
        end
    end

    if ok_inventory and inventory then
        local inv_ok, items, inv_err = inventory.list()
        if inv_ok and items then
            a.current.inventory = count_array(items)
            local cur_counts = audit_inventory_counts(items)
            local gain = count_gains and audit_positive_delta(a.last_inventory_counts, cur_counts) or 0
            if gain > 0 then
                a.material_gain = a.material_gain + gain
                a.gather_est = a.gather_est + gain
            end
            a.last_inventory_counts = cur_counts
        elseif inv_err then
            a.last_error = tostring(inv_err)
        end

        local kinah_ok, kinah = inventory.kinah()
        if kinah_ok and kinah then
            if count_gains and a.last_kinah ~= nil then
                a.kinah_gain = a.kinah_gain + (kinah - a.last_kinah)
            end
            a.last_kinah = kinah
        end
    end

    if ok_quest and quest then
        local quest_ok, quests = quest.list()
        if quest_ok and quests then
            a.current.quests = count_array(quests)
        end
    end

    if ok_combat and combat then
        local target_ok, target = combat.currentTarget()
        if target_ok and target then
            a.current.target_id = target.id or 0
        else
            a.current.target_id = 0
        end
    end
end

local function sleep(ms)
    if sys and sys.sleep then
        sys.sleep(ms)
    end
end

local function set_event(text)
    runtime.last_event = text
    log_info("[AionControlUI] " .. text)
end

local function start_bot()
    if cfg.audit.reset_on_start then
        audit_reset()
    end
    runtime.running = true
    runtime.paused = false
    runtime.status = "运行中"
    runtime.active_mode = primary_modes[cfg.primary_mode] or "unknown"
    set_event("启动: " .. runtime.active_mode)
    runtime.ui_visible = false
end

local function stop_bot()
    runtime.running = false
    runtime.paused = false
    runtime.status = "已停止"
    runtime.active_mode = "none"
    runtime.ui_visible = true
    set_event("停止")
end

local function toggle_start_stop()
    if runtime.running then
        stop_bot()
    else
        start_bot()
    end
end

local function toggle_pause()
    if not runtime.running then
        set_event("未运行，无法暂停")
        return
    end

    runtime.paused = not runtime.paused
    runtime.status = runtime.paused and "已暂停" or "运行中"
    set_event(runtime.paused and "暂停" or "继续")
end

local function toggle_ui_visible()
    runtime.ui_visible = not runtime.ui_visible
    set_event(runtime.ui_visible and "显示窗口" or "隐藏窗口")
end

local function save_config()
    if not config then
        log_warn("[AionControlUI] config module unavailable")
        return
    end

    config.load()
    config.set("aion_control.profile_name", cfg.profile_name)
    config.set("aion_control.primary_mode", cfg.primary_mode)
    config.set("aion_control.priority_mode", cfg.priority_mode)
    config.set("aion_control.combat_radius", cfg.combat.radius)
    config.set("aion_control.gather_radius", cfg.gather.radius)
    config.set("aion_control.route_name", cfg.route.route_name)
    config.set("aion_control.hp_percent", cfg.supply.hp_percent)
    config.set("aion_control.mp_percent", cfg.supply.mp_percent)
    config.set("aion_control.bag_full_percent", cfg.supply.bag_full_percent)
    config.set("aion_control.audit_enabled", cfg.audit.enabled)
    config.set("aion_control.audit_sample_interval", cfg.audit.sample_interval)
    config.set("aion_control.audit_material_keywords", cfg.audit.material_keywords)
    config.save()
    set_event("配置已保存到 script_config.json")
end

local function load_config()
    if not config then
        log_warn("[AionControlUI] config module unavailable")
        return
    end

    config.load()
    cfg.profile_name = config.get("aion_control.profile_name", cfg.profile_name)
    cfg.primary_mode = config.get("aion_control.primary_mode", cfg.primary_mode)
    cfg.priority_mode = config.get("aion_control.priority_mode", cfg.priority_mode)
    cfg.combat.radius = config.get("aion_control.combat_radius", cfg.combat.radius)
    cfg.gather.radius = config.get("aion_control.gather_radius", cfg.gather.radius)
    cfg.route.route_name = config.get("aion_control.route_name", cfg.route.route_name)
    cfg.supply.hp_percent = config.get("aion_control.hp_percent", cfg.supply.hp_percent)
    cfg.supply.mp_percent = config.get("aion_control.mp_percent", cfg.supply.mp_percent)
    cfg.supply.bag_full_percent = config.get("aion_control.bag_full_percent", cfg.supply.bag_full_percent)
    cfg.audit.enabled = config.get("aion_control.audit_enabled", cfg.audit.enabled)
    cfg.audit.sample_interval = config.get("aion_control.audit_sample_interval", cfg.audit.sample_interval)
    cfg.audit.material_keywords = config.get("aion_control.audit_material_keywords", cfg.audit.material_keywords)
    set_event("配置已加载")
end

local function run_probe()
    if not ok_probe or not probe then
        runtime.last_probe = "probe 模块不可用"
        log_warn("[AionControlUI] aion.probe unavailable")
        return
    end

    local _, summary = probe.run()
    runtime.last_probe = string.format("pass=%d warn=%d fail=%d",
        summary.PASS or 0, summary.WARN or 0, summary.FAIL or 0)
    set_event("API 探针完成: " .. runtime.last_probe)
end

local function capture_position_text()
    if not ok_core or not core then
        return nil, "aion.core 不可用"
    end

    local ok, pos, err = core.getPosition()
    if not ok then
        return nil, err or "坐标读取失败"
    end

    return string.format("%.3f, %.3f, %.3f", pos.x or 0, pos.y or 0, pos.z or 0), nil
end

local function append_point(field)
    local text, err = capture_position_text()
    if not text then
        set_event("添加坐标失败: " .. tostring(err))
        return
    end

    cfg.route[field] = cfg.route[field] == "" and text or cfg.route[field] .. "\n" .. text
    set_event("已添加坐标: " .. text)
end

local function clear_route(field)
    cfg.route[field] = ""
    set_event("已清空路径: " .. field)
end

local function help_marker(text)
    imgui.same_line()
    imgui.text_disabled("(?)")
    if imgui.is_item_hovered() then
        imgui.begin_tooltip()
        imgui.text(text)
        imgui.end_tooltip()
    end
end

local function draw_header()
    imgui.text("Aion 控制台")
    imgui.same_line(180)
    imgui.text("状态: " .. runtime.status)
    imgui.same_line(340)
    imgui.text("模式: " .. tostring(runtime.active_mode))

    imgui.separator()

    local bw, bh = 90, 28
    if not runtime.running then
        if imgui.button("启动", bw, bh) then
            start_bot()
        end
    else
        if imgui.button("停止", bw, bh) then
            stop_bot()
        end
    end

    imgui.same_line()
    if imgui.button(runtime.paused and "继续" or "暂停", bw, bh) then
        toggle_pause()
    end

    imgui.same_line()
    if imgui.button("API探针", bw, bh) then
        run_probe()
    end

    imgui.same_line()
    if imgui.button("保存配置", bw, bh) then
        save_config()
    end

    imgui.same_line()
    if imgui.button("加载配置", bw, bh) then
        load_config()
    end

    imgui.same_line()
    if imgui.button("隐藏窗口", bw, bh) then
        toggle_ui_visible()
    end

    imgui.separator()
end

local function draw_overview_tab()
    local changed, val

    imgui.text("方案")
    imgui.set_next_item_width(220)
    changed, val = imgui.input_text("方案名", cfg.profile_name)
    if changed then cfg.profile_name = val end

    imgui.set_next_item_width(220)
    changed, val = imgui.combo("主模式", cfg.primary_mode, primary_modes)
    if changed then cfg.primary_mode = val end

    imgui.set_next_item_width(220)
    changed, val = imgui.combo("优先级", cfg.priority_mode, priority_modes)
    if changed then cfg.priority_mode = val end

    imgui.spacing()
    imgui.text("运行摘要")
    imgui.separator()
    imgui.text("运行状态: " .. runtime.status)
    imgui.text("当前模式: " .. tostring(runtime.active_mode))
    imgui.text("最后事件: " .. tostring(runtime.last_event))
    imgui.text("最近探针: " .. tostring(runtime.last_probe))

    imgui.spacing()
    imgui.text("说明")
    imgui.separator()
    imgui.text_wrapped("这份 UI 目前只保存配置和输出状态，不直接执行真实动作。后续建议接入 Planner、Behavior Tree、ActionQueue 和 Executor。")
end

local function draw_combat_tab()
    local changed, val

    changed, val = imgui.checkbox("启用打怪", cfg.combat.enabled)
    if changed then cfg.combat.enabled = val end

    imgui.set_next_item_width(220)
    changed, val = imgui.combo("打怪模式", cfg.combat.mode, combat_modes)
    if changed then cfg.combat.mode = val end

    imgui.set_next_item_width(220)
    changed, val = imgui.combo("目标策略", cfg.combat.target_policy, combat_target_policies)
    if changed then cfg.combat.target_policy = val end

    changed, val = imgui.input_int("搜索半径", cfg.combat.radius)
    if changed then cfg.combat.radius = math.max(1, val) end

    changed, val = imgui.input_int("最低等级", cfg.combat.min_level)
    if changed then cfg.combat.min_level = math.max(1, val) end

    changed, val = imgui.input_int("最高等级", cfg.combat.max_level)
    if changed then cfg.combat.max_level = math.max(cfg.combat.min_level, val) end

    changed, val = imgui.checkbox("优先任务目标", cfg.combat.prefer_quest_targets)
    if changed then cfg.combat.prefer_quest_targets = val end

    changed, val = imgui.checkbox("避开精英/高危目标", cfg.combat.avoid_elite)
    if changed then cfg.combat.avoid_elite = val end

    changed, val = imgui.checkbox("保持自动战斗状态", cfg.combat.keep_auto_battle)
    if changed then cfg.combat.keep_auto_battle = val end

    imgui.text("指定目标名")
    imgui.set_next_item_width(420)
    changed, val = imgui.input_text_multiline("##combat_targets", cfg.combat.target_names, 420, 80)
    if changed then cfg.combat.target_names = val end

    imgui.text("黑名单目标名")
    imgui.set_next_item_width(420)
    changed, val = imgui.input_text_multiline("##combat_blacklist", cfg.combat.blacklist_names, 420, 80)
    if changed then cfg.combat.blacklist_names = val end
end

local function draw_gather_tab()
    local changed, val

    changed, val = imgui.checkbox("启用采集", cfg.gather.enabled)
    if changed then cfg.gather.enabled = val end

    imgui.set_next_item_width(220)
    changed, val = imgui.combo("采集模式", cfg.gather.mode, gather_modes)
    if changed then cfg.gather.mode = val end

    changed, val = imgui.input_int("搜索半径##gather", cfg.gather.radius)
    if changed then cfg.gather.radius = math.max(1, val) end

    changed, val = imgui.checkbox("草药", cfg.gather.gather_herb)
    if changed then cfg.gather.gather_herb = val end

    imgui.same_line()
    changed, val = imgui.checkbox("矿物", cfg.gather.gather_ore)
    if changed then cfg.gather.gather_ore = val end

    imgui.same_line()
    changed, val = imgui.checkbox("资源物", cfg.gather.gather_resource)
    if changed then cfg.gather.gather_resource = val end

    changed, val = imgui.checkbox("战斗后顺手采集", cfg.gather.gather_after_combat)
    if changed then cfg.gather.gather_after_combat = val end

    imgui.text("优先资源名")
    imgui.set_next_item_width(420)
    changed, val = imgui.input_text_multiline("##gather_names", cfg.gather.resource_names, 420, 90)
    if changed then cfg.gather.resource_names = val end

    imgui.text("资源黑名单")
    imgui.set_next_item_width(420)
    changed, val = imgui.input_text_multiline("##gather_blacklist", cfg.gather.blacklist_names, 420, 90)
    if changed then cfg.gather.blacklist_names = val end
end

local function draw_route_editor(label, nameField, pointsField)
    local changed, val

    imgui.text(label)
    imgui.set_next_item_width(240)
    changed, val = imgui.input_text("路径名##" .. pointsField, cfg.route[nameField])
    if changed then cfg.route[nameField] = val end

    changed, val = imgui.input_float("录制间隔秒##" .. pointsField, cfg.route.record_interval)
    if changed then cfg.route.record_interval = math.max(0.2, val) end

    changed, val = imgui.input_int("路点到达半径##" .. pointsField, cfg.route.waypoint_radius)
    if changed then cfg.route.waypoint_radius = math.max(1, val) end

    if imgui.button("添加当前坐标##" .. pointsField, 120, 26) then
        append_point(pointsField)
    end

    imgui.same_line()
    if imgui.button("开始录制##" .. pointsField, 100, 26) then
        set_event("请求开始录制: " .. cfg.route[nameField])
    end

    imgui.same_line()
    if imgui.button("停止录制##" .. pointsField, 100, 26) then
        set_event("请求停止录制: " .. cfg.route[nameField])
    end

    imgui.same_line()
    if imgui.button("清空##" .. pointsField, 70, 26) then
        clear_route(pointsField)
    end

    imgui.set_next_item_width(560)
    changed, val = imgui.input_text_multiline("##points_" .. pointsField, cfg.route[pointsField], 560, 220)
    if changed then cfg.route[pointsField] = val end
end

local function draw_route_tab()
    local changed, val

    imgui.set_next_item_width(220)
    changed, val = imgui.combo("路径选择", cfg.route.selected_route, route_names)
    if changed then cfg.route.selected_route = val end

    changed, val = imgui.checkbox("循环路径", cfg.route.loop)
    if changed then cfg.route.loop = val end

    imgui.same_line()
    changed, val = imgui.checkbox("到终点反向", cfg.route.reverse_on_end)
    if changed then cfg.route.reverse_on_end = val end

    imgui.same_line()
    changed, val = imgui.checkbox("死亡停止路径", cfg.route.stop_on_death)
    if changed then cfg.route.stop_on_death = val end

    imgui.spacing()

    if imgui.begin_tab_bar("##route_tabs") then
        if imgui.begin_tab_item("主路径") then
            draw_route_editor("路径打怪/采集用主路径", "route_name", "route_points")
            imgui.end_tab_item()
        end

        if imgui.begin_tab_item("复活路径") then
            draw_route_editor("死亡复活后返回路径", "revive_route_name", "revive_points")
            imgui.end_tab_item()
        end

        if imgui.begin_tab_item("补给路径") then
            draw_route_editor("去商人/仓库/任务点路径", "vendor_route_name", "vendor_points")
            imgui.end_tab_item()
        end

        imgui.end_tab_bar()
    end
end

local function draw_leveling_tab()
    local changed, val

    changed, val = imgui.checkbox("启用练级目标", cfg.leveling.enabled)
    if changed then cfg.leveling.enabled = val end

    imgui.set_next_item_width(220)
    changed, val = imgui.combo("练级方式", cfg.leveling.mode, leveling_modes)
    if changed then cfg.leveling.mode = val end

    changed, val = imgui.input_int("起始等级", cfg.leveling.start_level)
    if changed then cfg.leveling.start_level = math.max(1, val) end

    changed, val = imgui.input_int("目标等级", cfg.leveling.target_level)
    if changed then cfg.leveling.target_level = math.max(cfg.leveling.start_level, val) end

    changed, val = imgui.checkbox("优先任务", cfg.leveling.prefer_quest)
    if changed then cfg.leveling.prefer_quest = val end

    changed, val = imgui.checkbox("允许刷怪补经验", cfg.leveling.allow_grind)
    if changed then cfg.leveling.allow_grind = val end

    changed, val = imgui.checkbox("允许采集补经验", cfg.leveling.allow_gather)
    if changed then cfg.leveling.allow_gather = val end

    changed, val = imgui.checkbox("自动学习技能", cfg.leveling.learn_skills)
    if changed then cfg.leveling.learn_skills = val end

    changed, val = imgui.checkbox("自动评估装备升级", cfg.leveling.equip_upgrades)
    if changed then cfg.leveling.equip_upgrades = val end
end

local function draw_crafting_tab()
    local changed, val

    changed, val = imgui.checkbox("启用制作目标", cfg.crafting.enabled)
    if changed then cfg.crafting.enabled = val end

    imgui.set_next_item_width(220)
    changed, val = imgui.combo("制作专业", cfg.crafting.profession, professions)
    if changed then cfg.crafting.profession = val end

    imgui.set_next_item_width(260)
    changed, val = imgui.input_text("制作物品", cfg.crafting.item_name)
    if changed then cfg.crafting.item_name = val end

    changed, val = imgui.input_int("制作数量", cfg.crafting.craft_count)
    if changed then cfg.crafting.craft_count = math.max(1, val) end

    changed, val = imgui.input_int("保留金币", cfg.crafting.reserve_kinah)
    if changed then cfg.crafting.reserve_kinah = math.max(0, val) end

    changed, val = imgui.checkbox("缺材料时停止", cfg.crafting.stop_when_missing_material)
    if changed then cfg.crafting.stop_when_missing_material = val end

    imgui.text("材料规则")
    imgui.set_next_item_width(480)
    changed, val = imgui.input_text_multiline("##material_rules", cfg.crafting.material_rules, 480, 140)
    if changed then cfg.crafting.material_rules = val end
end

local function draw_supply_tab()
    local changed, val

    changed, val = imgui.input_int("回血阈值 %", cfg.supply.hp_percent)
    if changed then cfg.supply.hp_percent = math.max(1, math.min(100, val)) end

    changed, val = imgui.input_int("回蓝阈值 %", cfg.supply.mp_percent)
    if changed then cfg.supply.mp_percent = math.max(1, math.min(100, val)) end

    changed, val = imgui.input_int("清包阈值 %", cfg.supply.bag_full_percent)
    if changed then cfg.supply.bag_full_percent = math.max(1, math.min(100, val)) end

    changed, val = imgui.input_int("最少金币", cfg.supply.min_kinah)
    if changed then cfg.supply.min_kinah = math.max(0, val) end

    changed, val = imgui.input_int("购买血药", cfg.supply.buy_hp_potion)
    if changed then cfg.supply.buy_hp_potion = math.max(0, val) end

    changed, val = imgui.input_int("购买蓝药", cfg.supply.buy_mp_potion)
    if changed then cfg.supply.buy_mp_potion = math.max(0, val) end

    imgui.set_next_item_width(260)
    changed, val = imgui.input_text("商人名字", cfg.supply.vendor_name)
    if changed then cfg.supply.vendor_name = val end

    imgui.text("保留物品")
    imgui.set_next_item_width(420)
    changed, val = imgui.input_text_multiline("##keep_items", cfg.supply.keep_items, 420, 90)
    if changed then cfg.supply.keep_items = val end

    imgui.text("出售规则")
    imgui.set_next_item_width(420)
    changed, val = imgui.input_text_multiline("##sell_rules", cfg.supply.sell_rules, 420, 90)
    if changed then cfg.supply.sell_rules = val end

    imgui.spacing()
    imgui.text("安全")
    imgui.separator()

    changed, val = imgui.input_int("最大失败次数", cfg.safety.max_failures)
    if changed then cfg.safety.max_failures = math.max(1, val) end

    changed, val = imgui.input_int("卡住秒数", cfg.safety.max_stuck_seconds)
    if changed then cfg.safety.max_stuck_seconds = math.max(1, val) end

    changed, val = imgui.input_int("最大死亡次数", cfg.safety.max_deaths)
    if changed then cfg.safety.max_deaths = math.max(0, val) end

    changed, val = imgui.checkbox("未知地图停止", cfg.safety.stop_on_unknown_map)
    if changed then cfg.safety.stop_on_unknown_map = val end

    changed, val = imgui.checkbox("API 失败停止", cfg.safety.stop_on_api_fail)
    if changed then cfg.safety.stop_on_api_fail = val end

    changed, val = imgui.checkbox("启用 Circuit Breaker", cfg.safety.circuit_breaker)
    if changed then cfg.safety.circuit_breaker = val end
end

local function draw_debug_tab()
    imgui.text("调试")
    imgui.separator()

    if imgui.button("运行 API 探针", 140, 28) then
        run_probe()
    end

    imgui.same_line()
    if imgui.button("读取当前坐标", 140, 28) then
        local text, err = capture_position_text()
        set_event(text and ("当前坐标: " .. text) or ("坐标读取失败: " .. tostring(err)))
    end

    imgui.same_line()
    if imgui.button("打印配置摘要", 140, 28) then
        set_event(string.format("mode=%s priority=%s combat=%s gather=%s",
            primary_modes[cfg.primary_mode] or "?",
            priority_modes[cfg.priority_mode] or "?",
            tostring(cfg.combat.enabled),
            tostring(cfg.gather.enabled)))
    end

    imgui.spacing()
    imgui.text("最后事件: " .. tostring(runtime.last_event))
    imgui.text("最近探针: " .. tostring(runtime.last_probe))
    imgui.text("帧: " .. tostring(runtime.frame))

    imgui.spacing()
    imgui.text("热键")
    imgui.separator()
    imgui.text("F7: 呼出/隐藏窗口")
    imgui.text("F8: 启动/停止")
    imgui.text("F9: API 探针")
    imgui.text("F10: 暂停/继续")
    imgui.text("Ctrl+F12: 退出 UI 脚本")
end

local function format_duration(seconds)
    seconds = math.max(0, math.floor(seconds or 0))
    local h = math.floor(seconds / 3600)
    local m = math.floor((seconds % 3600) / 60)
    local s = seconds % 60
    return string.format("%02d:%02d:%02d", h, m, s)
end

local function draw_audit_panel()
    local a = runtime.audit
    local changed, val

    imgui.separator()
    imgui.text("审计")
    help_marker("当前为估算审计：击杀=新出现可拾取尸体；采集=材料/资源类物品入包增量；经验和金币按角色数据差值计算。")

    imgui.same_line(90)
    changed, val = imgui.checkbox("启用##audit", cfg.audit.enabled)
    if changed then cfg.audit.enabled = val end

    imgui.same_line(170)
    changed, val = imgui.checkbox("启动时重置##audit", cfg.audit.reset_on_start)
    if changed then cfg.audit.reset_on_start = val end

    imgui.same_line(310)
    if imgui.button("重置审计", 90, 24) then
        audit_reset()
        set_event("审计已重置")
    end

    imgui.same_line(410)
    changed, val = imgui.checkbox("详情##audit", cfg.audit.show_details)
    if changed then cfg.audit.show_details = val end

    imgui.same_line(500)
    imgui.set_next_item_width(70)
    changed, val = imgui.input_float("采样秒##audit", cfg.audit.sample_interval)
    if changed then cfg.audit.sample_interval = math.max(0.5, val) end

    imgui.text(string.format("时长 %s  |  击杀估算 %d (%.1f/h)  |  采集/入包 %d (%.1f/h)  |  经验 %d (%.0f/h)  |  金币 %+d (%+.0f/h)",
        format_duration(a.elapsed_seconds),
        a.kills_est,
        audit_rate(a.kills_est),
        a.gather_est,
        audit_rate(a.gather_est),
        a.exp_gain,
        audit_rate(a.exp_gain),
        a.kinah_gain,
        audit_rate(a.kinah_gain)))

    imgui.text(string.format("当前 Lv.%d  HP %d/%d  MP %d/%d  地图 %s  实体 %d  背包 %d  任务 %d  目标 %s",
        a.current.level or 0,
        a.current.hp or 0,
        a.current.max_hp or 0,
        a.current.mp or 0,
        a.current.max_mp or 0,
        tostring(a.current.map or ""),
        a.current.entities or 0,
        a.current.inventory or 0,
        a.current.quests or 0,
        tostring(a.current.target_id or 0)))

    if cfg.audit.show_details then
        imgui.separator()
        imgui.text("审计口径")
        imgui.text("击杀估算: 新出现 lootable 实体，已见过的尸体不重复计数。")
        imgui.text("采集估算: 背包内匹配关键字的物品数量正向增量。")
        imgui.text("路径点: 主路径 " .. tostring(count_lines(cfg.route.route_points)) ..
            " / 复活 " .. tostring(count_lines(cfg.route.revive_points)) ..
            " / 补给 " .. tostring(count_lines(cfg.route.vendor_points)))

        imgui.text("材料关键字")
        imgui.set_next_item_width(520)
        changed, val = imgui.input_text_multiline("##audit_material_keywords", cfg.audit.material_keywords, 520, 80)
        if changed then cfg.audit.material_keywords = val end

        if a.last_error ~= "" then
            imgui.text("最近审计错误: " .. tostring(a.last_error))
        end
    end
end

local function draw_main_window()
    imgui.set_next_window_size(800, 760, imgui.Cond_FirstUseEver)
    imgui.set_next_window_pos(120, 80, imgui.Cond_FirstUseEver)

    if imgui.begin_window("Aion 控制台", imgui.WindowFlags_NoCollapse) then
        draw_header()

        if imgui.begin_tab_bar("##aion_control_tabs") then
            if imgui.begin_tab_item("总览") then
                draw_overview_tab()
                imgui.end_tab_item()
            end

            if imgui.begin_tab_item("打怪") then
                draw_combat_tab()
                imgui.end_tab_item()
            end

            if imgui.begin_tab_item("采集") then
                draw_gather_tab()
                imgui.end_tab_item()
            end

            if imgui.begin_tab_item("路径") then
                draw_route_tab()
                imgui.end_tab_item()
            end

            if imgui.begin_tab_item("练级") then
                draw_leveling_tab()
                imgui.end_tab_item()
            end

            if imgui.begin_tab_item("制作") then
                draw_crafting_tab()
                imgui.end_tab_item()
            end

            if imgui.begin_tab_item("补给/安全") then
                draw_supply_tab()
                imgui.end_tab_item()
            end

            if imgui.begin_tab_item("调试") then
                draw_debug_tab()
                imgui.end_tab_item()
            end

            imgui.end_tab_bar()
        end

        draw_audit_panel()
    end

    imgui.end_window()
end

local function on_render()
    runtime.frame = runtime.frame + 1
    audit_sample()
    if runtime.ui_visible then
        draw_main_window()
    end
end

log_info("Aion 控制台 UI 启动")
load_config()

imgui.on_render(on_render)

if not imgui.is_initialized() then
    if not imgui.init() then
        log_warn("ImGui 初始化失败")
        return
    end
    imgui.run()
end

hotkey.start(10)

local last_f7 = false
local last_f8 = false
local last_f9 = false
local last_f10 = false

while true do
    local ctrl = hotkey.is_pressed(0x11)
    if ctrl and hotkey.is_pressed(0x7B) then
        log_info("Aion 控制台 UI 退出")
        break
    end

    local f7 = hotkey.is_pressed(0x76)
    if f7 and not last_f7 then
        toggle_ui_visible()
    end
    last_f7 = f7

    local f8 = hotkey.is_pressed(0x77)
    if f8 and not last_f8 then
        toggle_start_stop()
    end
    last_f8 = f8

    local f9 = hotkey.is_pressed(0x78)
    if f9 and not last_f9 then
        run_probe()
    end
    last_f9 = f9

    local f10 = hotkey.is_pressed(0x79)
    if f10 and not last_f10 then
        toggle_pause()
    end
    last_f10 = f10

    sleep(50)
end

hotkey.stop()
