--[[
    Post-login enter-game flow:

    1. Bind AionData to the detected Aion.bin pid.
    2. Accept the user agreement dialog when it appears.
    3. Wait for the server selection scene/API and log every visible server key.
    4. Submit the configured server key, retrying once to match manual double-click behavior.
    5. Wait for the character selection scene and log every character entry.
    6. Select the configured character name when it exists.
    7. Create a character from configured race/job when the configured name is
       missing, using a generated 10-letter English-style name on duplicate.
    8. Submit the second password if the dialog appears.
    9. Confirm GetCharacter() returns an in-game character before reporting ready.
]]

local ok_core, core = pcall(require, "aion.core")
local ok_account_api, account_api = pcall(require, "aion.account")
local ok_security, security = pcall(require, "aion.security")
local ok_ui, ui_api = pcall(require, "aion.ui")
local ok_buttons, ui_buttons = pcall(require, "aion_ui_buttons")

local M = {}

local SCENE_USER_AGREEMENT = 0x8
local SCENE_USER_AGREEMENT_CONFIRM = 0x9
local SCENE_SERVER_SELECT = 0xA
local SCENE_CHARACTER_SELECT = 0xC

local GENERATED_CHARACTER_NAMES = {
    "Silverleaf",
    "Amberstone",
    "Brightwind",
    "Stormlight",
    "Frostguard",
    "Shadowfall",
    "Nightbloom",
    "Stonehaven",
    "Moonwalker",
    "Starfinder",
    "Sunbreaker",
    "Riverstone",
    "Goldenvale",
    "Ironcastle",
    "Swiftarrow",
    "Crystalbay",
    "Clearwater",
    "Greenfield",
    "Highlander",
    "Blueforest",
    "Wildflower",
    "Winterfall",
    "Summerwind",
    "Springvale",
    "Meadowlark",
    "Forestglen",
    "Morningdew",
    "Silentpath",
    "Cloudriver",
    "Brightwood",
}

local function trim_text(value)
    local text = tostring(value or "")
    text = string.gsub(text, "^%s+", "")
    text = string.gsub(text, "%s+$", "")
    return text
end

local function random_int(ctx, min_value, max_value)
    min_value = tonumber(min_value) or 0
    max_value = tonumber(max_value) or min_value
    if max_value < min_value then
        max_value = min_value
    end

    if ctx and type(ctx.random) == "function" then
        local ok, value = pcall(ctx.random, min_value, max_value)
        value = ok and tonumber(value) or nil
        if value ~= nil then
            value = math.floor(value)
            if value < min_value then
                value = min_value
            elseif value > max_value then
                value = max_value
            end
            return value
        end
    end

    return math.random(min_value, max_value)
end

local function now_ms(ctx)
    if ctx and type(ctx.now_ms) == "function" then
        return ctx.now_ms()
    end
    if sys and type(sys.time) == "function" then
        return sys.time()
    end
    return os.time() * 1000
end

local function sleep(ctx, ms)
    if ctx and type(ctx.sleep) == "function" then
        ctx.sleep(ms)
    elseif sys and type(sys.sleep) == "function" then
        sys.sleep(ms)
    end
end

local function set_progress(ctx, value)
    if ctx and type(ctx.set_progress) == "function" then
        ctx.set_progress(value)
    elseif task and type(task.set_progress) == "function" then
        task.set_progress(value)
    end
end

local function set_character(ctx, char)
    if ctx and type(ctx.set_character) == "function" then
        ctx.set_character(ctx.index, char)
    end
end

local function flow_log(ctx, level, event, data)
    if not log then
        return
    end

    local fn = log[level]
    if type(fn) ~= "function" then
        fn = log.info
    end
    if type(fn) ~= "function" then
        return
    end

    local text = "[AionLoginFlow] index=" .. tostring(ctx and ctx.index or 0) .. " event=" .. tostring(event or "")
    if data ~= nil and tostring(data) ~= "" then
        text = text .. " " .. tostring(data)
    end
    fn(text)
end

local function flow_info(ctx, event, data)
    flow_log(ctx, "info", event, data)
end

local function flow_warn(ctx, event, data)
    flow_log(ctx, "warn", event, data)
end

local function set_flow_status(ctx, status, message)
    if ctx and type(ctx.set_status) == "function" then
        ctx.set_status(ctx.index, status, message)
    end
    flow_info(ctx, "status", tostring(status or "") .. " message=" .. tostring(message or ""))
end

local function default_flow_config()
    return {
        init_timeout_seconds = 120,
        server_timeout_seconds = 90,
        character_timeout_seconds = 90,
        enter_game_timeout_seconds = 120,
        poll_interval_ms = 1000,
        agreement_timeout_seconds = 12,
        agreement_retry_interval_ms = 1000,
        server_submit_attempts = 2,
        server_submit_interval_ms = 450,
        create_character_recheck_timeout_seconds = 20,
        create_character_recheck_interval_ms = 1000,
        create_character_max_attempts = 4,
    }
end

local function normalize_flow_config(flow)
    local out = default_flow_config()
    if type(flow) == "table" then
        for key, value in pairs(flow) do
            if out[key] ~= nil then
                out[key] = tonumber(value) or out[key]
            end
        end
    end
    out.init_timeout_seconds = math.max(30, out.init_timeout_seconds)
    out.server_timeout_seconds = math.max(5, out.server_timeout_seconds)
    out.character_timeout_seconds = math.max(5, out.character_timeout_seconds)
    out.enter_game_timeout_seconds = math.max(10, out.enter_game_timeout_seconds)
    out.poll_interval_ms = math.max(250, out.poll_interval_ms)
    out.agreement_timeout_seconds = math.max(0, out.agreement_timeout_seconds)
    out.agreement_retry_interval_ms = math.max(250, out.agreement_retry_interval_ms)
    out.server_submit_attempts = math.max(1, math.min(4, math.floor(out.server_submit_attempts)))
    out.server_submit_interval_ms = math.max(150, out.server_submit_interval_ms)
    out.create_character_recheck_timeout_seconds = math.max(3, out.create_character_recheck_timeout_seconds)
    out.create_character_recheck_interval_ms = math.max(250, out.create_character_recheck_interval_ms)
    out.create_character_max_attempts = math.max(1, math.min(8, math.floor(out.create_character_max_attempts)))
    return out
end

local function format_scene(scene)
    if type(scene) ~= "table" then
        return "nil"
    end
    return "idx=" .. tostring(scene.index) .. " name=" .. tostring(scene.name or "")
end

local function scene_index(scene)
    if type(scene) ~= "table" then
        return nil
    end
    return tonumber(scene.index)
end

local function log_scene_if_changed(ctx, label, last_text)
    if not ok_core or not core or type(core.getScene) ~= "function" then
        return last_text
    end

    local ok, scene, err = core.getScene()
    local text = ok and format_scene(scene) or ("failed err=" .. tostring(err))
    if text ~= last_text then
        flow_info(ctx, label, text)
        return text
    end
    return last_text
end

local function ensure_modules()
    if not ok_core or not core then
        return false, "aion.core unavailable: " .. tostring(core)
    end
    if not ok_account_api or not account_api then
        return false, "aion.account unavailable: " .. tostring(account_api)
    end
    return true, nil
end

local function current_scene()
    if not ok_core or not core or type(core.getScene) ~= "function" then
        return false, nil, "aion.core.getScene unavailable"
    end
    return core.getScene()
end

local function agreement_dialog_name()
    if ok_buttons and ui_buttons and type(ui_buttons.dialog) == "function" then
        return ui_buttons.dialog("user_agreement")
    end
    if ok_buttons and ui_buttons and type(ui_buttons.user_agreement) == "table" then
        return ui_buttons.user_agreement.dialog
    end
    return "user_agreement_dialog"
end

local function agreement_button_name()
    if ok_buttons and ui_buttons and type(ui_buttons.button) == "function" then
        return ui_buttons.button("user_agreement", "agree")
    end
    if ok_buttons and ui_buttons and type(ui_buttons.user_agreement) == "table" then
        return ui_buttons.user_agreement.agree
    end
    return "agreement_yes"
end

local function append_unique(list, seen, value)
    value = tostring(value or "")
    if value ~= "" and not seen[value] then
        seen[value] = true
        list[#list + 1] = value
    end
end

local function agreement_checkbox_names()
    local list = {}
    local seen = {}
    if ok_buttons and ui_buttons and type(ui_buttons.button) == "function" then
        append_unique(list, seen, ui_buttons.button("user_agreement", "checkbox"))
        append_unique(list, seen, ui_buttons.button("user_agreement", "check"))
    end
    if ok_buttons and ui_buttons and type(ui_buttons.user_agreement) == "table" then
        append_unique(list, seen, ui_buttons.user_agreement.checkbox)
        append_unique(list, seen, ui_buttons.user_agreement.check)
    end
    append_unique(list, seen, "agreement_check")
    append_unique(list, seen, "agreement_checkbox")
    append_unique(list, seen, "check_agreement")
    append_unique(list, seen, "checkbox_agreement")
    append_unique(list, seen, "agree_check")
    append_unique(list, seen, "check_game_agreement")
    append_unique(list, seen, "game_agreement_check")
    return list
end

local function agreement_button_names()
    local list = {}
    local seen = {}
    append_unique(list, seen, agreement_button_name())
    append_unique(list, seen, "agreement_yes")
    append_unique(list, seen, "agreement_ok")
    append_unique(list, seen, "agree_yes")
    append_unique(list, seen, "btn_agree")
    append_unique(list, seen, "button_agree")
    append_unique(list, seen, "agree")
    append_unique(list, seen, "btn_ok")
    append_unique(list, seen, "button_ok")
    return list
end

local function server_select_dialog_name()
    if ok_buttons and ui_buttons and type(ui_buttons.dialog) == "function" then
        return ui_buttons.dialog("server_select")
    end
    if ok_buttons and ui_buttons and type(ui_buttons.server_select) == "table" then
        return ui_buttons.server_select.dialog
    end
    return "server_select_dialog"
end

local function server_start_button_names()
    local list = {}
    local seen = {}
    if ok_buttons and ui_buttons and type(ui_buttons.button) == "function" then
        append_unique(list, seen, ui_buttons.button("server_select", "start"))
    end
    if ok_buttons and ui_buttons and type(ui_buttons.server_select) == "table" then
        append_unique(list, seen, ui_buttons.server_select.start)
    end
    append_unique(list, seen, "start_button")
    append_unique(list, seen, "server_start_button")
    append_unique(list, seen, "server_select_button")
    append_unique(list, seen, "select_server_button")
    append_unique(list, seen, "btn_start")
    append_unique(list, seen, "button_start")
    return list
end

local function character_select_dialog_name()
    if ok_buttons and ui_buttons and type(ui_buttons.dialog) == "function" then
        return ui_buttons.dialog("character_select")
    end
    if ok_buttons and ui_buttons and type(ui_buttons.character_select) == "table" then
        return ui_buttons.character_select.dialog
    end
    return "select_char_dialog_new"
end

local function character_start_button_names()
    local list = {}
    local seen = {}
    if ok_buttons and ui_buttons and type(ui_buttons.button) == "function" then
        append_unique(list, seen, ui_buttons.button("character_select", "start"))
    end
    if ok_buttons and ui_buttons and type(ui_buttons.character_select) == "table" then
        append_unique(list, seen, ui_buttons.character_select.start)
    end
    append_unique(list, seen, "start_button")
    append_unique(list, seen, "character_start_button")
    append_unique(list, seen, "select_character_button")
    append_unique(list, seen, "btn_start")
    append_unique(list, seen, "button_start")
    return list
end

local function second_password_dialog_name()
    if ok_buttons and ui_buttons and type(ui_buttons.dialog) == "function" then
        return ui_buttons.dialog("second_password")
    end
    if ok_buttons and ui_buttons and type(ui_buttons.second_password) == "table" then
        return ui_buttons.second_password.dialog
    end
    return "second_password_dialog"
end

local function second_password_ok_button_names()
    local list = {}
    local seen = {}
    if ok_buttons and ui_buttons and type(ui_buttons.button) == "function" then
        append_unique(list, seen, ui_buttons.button("second_password", "ok"))
    end
    if ok_buttons and ui_buttons and type(ui_buttons.second_password) == "table" then
        append_unique(list, seen, ui_buttons.second_password.ok)
    end
    append_unique(list, seen, "ok")
    append_unique(list, seen, "btn_ok")
    append_unique(list, seen, "button_ok")
    append_unique(list, seen, "confirm")
    append_unique(list, seen, "btn_confirm")
    return list
end

local function second_password_clear_button_names()
    local list = {}
    local seen = {}
    if ok_buttons and ui_buttons and type(ui_buttons.button) == "function" then
        append_unique(list, seen, ui_buttons.button("second_password", "clear"))
    end
    if ok_buttons and ui_buttons and type(ui_buttons.second_password) == "table" then
        append_unique(list, seen, ui_buttons.second_password.clear)
    end
    append_unique(list, seen, "num_clear")
    return list
end

local function second_password_digit_button_names(digit)
    local list = {}
    local seen = {}
    append_unique(list, seen, "num" .. tostring(digit))
    return list
end

local function ui_find(name)
    if not ok_ui or not ui_api or type(ui_api.find) ~= "function" then
        return false, nil, "aion.ui.find unavailable"
    end
    return ui_api.find(name)
end

local function ui_click(name_or_addr)
    if not ok_ui or not ui_api or type(ui_api.click) ~= "function" then
        return false, nil, "aion.ui.click unavailable"
    end
    return ui_api.click(name_or_addr)
end

local function is_ui_visible(obj)
    if type(obj) ~= "table" then
        return false
    end
    if obj.visible == false then
        return false
    end
    return (tonumber(obj.addr) or tonumber(obj.obj) or 0) > 0 or obj.visible == true
end

local function ui_obj_id(obj)
    if type(obj) ~= "table" then
        return tonumber(obj) or 0
    end
    return tonumber(obj.addr) or tonumber(obj.obj) or tonumber(obj.node) or 0
end

local function ui_control_label(ctrl, index)
    if type(ctrl) ~= "table" then
        return tostring(ctrl)
    end
    return string.format(
        "#%s obj=%s addr=%s node=%s name=%s visible=%s x=%s y=%s layer=%s depth=%s parent=%s",
        tostring(index or ""),
        tostring(ctrl.obj or ""),
        tostring(ctrl.addr or ""),
        tostring(ctrl.node or ""),
        tostring(ctrl.name or ""),
        tostring(ctrl.visible),
        tostring(ctrl.x or ""),
        tostring(ctrl.y or ""),
        tostring(ctrl.layer or ""),
        tostring(ctrl.depth or ""),
        tostring(ctrl.parent_name or ""))
end

local function agreement_name_interesting(name)
    name = string.lower(tostring(name or ""))
    if name == "" then
        return false
    end
    return string.find(name, "agreement", 1, true)
        or string.find(name, "agree", 1, true)
        or string.find(name, "user", 1, true)
        or string.find(name, "check", 1, true)
        or string.find(name, "html", 1, true)
        or string.find(name, "yes", 1, true)
        or string.find(name, "ok", 1, true)
        or string.find(name, "btn", 1, true)
        or string.find(name, "button", 1, true)
end

local function dump_agreement_ui(ctx, dialog_name)
    if ctx.agreement_ui_dumped == true then
        return
    end
    ctx.agreement_ui_dumped = true

    if not ok_ui or not ui_api then
        flow_warn(ctx, "agreement_ui_dump_unavailable", tostring(ui_api))
        return
    end

    if type(ui_api.children) == "function" then
        local child_ok, children, child_err = ui_api.children(dialog_name, 10)
        if child_ok then
            children = children or {}
            flow_info(ctx, "agreement_children", "parent=" .. tostring(dialog_name) .. " count=" .. tostring(#children))
            for i, child in ipairs(children) do
                if i > 80 then
                    flow_warn(ctx, "agreement_children_truncated", "count=" .. tostring(#children))
                    break
                end
                flow_info(ctx, "agreement_child", ui_control_label(child, i))
            end
        else
            flow_warn(ctx, "agreement_children_failed", "parent=" .. tostring(dialog_name) .. " err=" .. tostring(child_err))
        end
    end

    if type(ui_api.list) == "function" then
        local list_ok, list, list_err = ui_api.list(true)
        if list_ok then
            local logged = 0
            for i, ctrl in ipairs(list or {}) do
                if agreement_name_interesting(ctrl and ctrl.name) then
                    logged = logged + 1
                    flow_info(ctx, "agreement_ui_candidate", ui_control_label(ctrl, i))
                    if logged >= 80 then
                        flow_warn(ctx, "agreement_ui_candidates_truncated", "logged=" .. tostring(logged))
                        break
                    end
                end
            end
            flow_info(ctx, "agreement_ui_candidates", "logged=" .. tostring(logged))
        else
            flow_warn(ctx, "agreement_ui_list_failed", tostring(list_err))
        end
    end
end

local function find_named_child(ctx, event, parent_name, name)
    if not ok_ui or not ui_api or type(ui_api.children) ~= "function" then
        return false, nil, "aion.ui.children unavailable"
    end

    local child_ok, children, child_err = ui_api.children(parent_name, 10)
    if not child_ok then
        return false, nil, tostring(child_err or "GetUIChildren failed")
    end

    for i, ctrl in ipairs(children or {}) do
        if tostring(ctrl and ctrl.name or "") == tostring(name or "") then
            flow_info(ctx, event .. "_child_find", string.format(
                "name=%s parent=%s index=%s obj=%s visible=%s",
                tostring(name),
                tostring(parent_name),
                tostring(i),
                tostring(ui_obj_id(ctrl)),
                tostring(type(ctrl) == "table" and ctrl.visible or "")))
            return true, ctrl, nil
        end
    end

    return false, nil, "not found in children"
end

local function find_named_list_control(ctx, event, name)
    if not ok_ui or not ui_api or type(ui_api.list) ~= "function" then
        return false, nil, "aion.ui.list unavailable"
    end

    local list_ok, list, list_err = ui_api.list(true)
    if not list_ok then
        return false, nil, tostring(list_err or "GetUIList failed")
    end

    for i, ctrl in ipairs(list or {}) do
        if tostring(ctrl and ctrl.name or "") == tostring(name or "") then
            flow_info(ctx, event .. "_list_find", string.format(
                "name=%s index=%s obj=%s visible=%s",
                tostring(name),
                tostring(i),
                tostring(ui_obj_id(ctrl)),
                tostring(type(ctrl) == "table" and ctrl.visible or "")))
            return true, ctrl, nil
        end
    end

    return false, nil, "not found in list"
end

local function find_named_click_target(ctx, event, name, parent_name)
    local find_ok, obj, find_err = ui_find(name)
    local obj_id = ui_obj_id(obj)
    flow_info(ctx, event .. "_find", string.format(
        "name=%s ok=%s obj=%s visible=%s err=%s",
        tostring(name),
        tostring(find_ok),
        tostring(obj_id),
        tostring(type(obj) == "table" and obj.visible or ""),
        tostring(find_err or "")))

    if find_ok and obj_id > 0 and is_ui_visible(obj) then
        return true, obj, "find", nil
    end

    parent_name = tostring(parent_name or "")
    if parent_name == "" then
        parent_name = agreement_dialog_name()
    end

    local child_ok, child, child_err = find_named_child(ctx, event, parent_name, name)
    if child_ok and ui_obj_id(child) > 0 and is_ui_visible(child) then
        return true, child, "children", nil
    end

    local list_ok, listed, list_err = find_named_list_control(ctx, event, name)
    if list_ok and ui_obj_id(listed) > 0 and is_ui_visible(listed) then
        return true, listed, "list", nil
    end

    return false, nil, nil, tostring(find_err or child_err or list_err or "not found")
end

local function click_named_candidates(ctx, event, names, required, parent_name)
    local last_err = nil
    for _, name in ipairs(names or {}) do
        local target_ok, obj, source, target_err = find_named_click_target(ctx, event, name, parent_name)
        if target_ok then
            local click_target = ui_obj_id(obj)
            local click_ok, clicked, click_err = ui_click(click_target)
            flow_info(ctx, event .. "_click", string.format(
                "name=%s source=%s target=%s ok=%s clicked=%s err=%s",
                tostring(name),
                tostring(source or ""),
                tostring(click_target),
                tostring(click_ok),
                tostring(clicked),
                tostring(click_err or "")))
            if click_ok and clicked ~= false then
                return true, name
            end
            last_err = tostring(click_err or clicked)
        else
            last_err = tostring(target_err or "not found")
        end
    end

    if required then
        return false, last_err or "no clickable candidate"
    end
    return true, nil
end

local function try_accept_user_agreement(ctx)
    local scene_ok, scene = current_scene()
    local idx = scene_ok and scene_index(scene) or nil
    local in_agreement_scene = idx == SCENE_USER_AGREEMENT or idx == SCENE_USER_AGREEMENT_CONFIRM
    local dialog_name = agreement_dialog_name()
    local button_name = agreement_button_name()

    if not ok_ui or not ui_api then
        if in_agreement_scene then
            return false, "agreement scene visible but aion.ui unavailable: " .. tostring(ui_api)
        end
        return true, false
    end

    local find_ok, dialog, find_err = ui_find(dialog_name)
    local dialog_visible = find_ok and is_ui_visible(dialog)
    if not in_agreement_scene and not dialog_visible then
        return true, false
    end

    dump_agreement_ui(ctx, dialog_name)

    flow_info(ctx, "agreement_detected", string.format(
        "scene=%s dialog=%s dialog_addr=%s button=%s",
        scene_ok and format_scene(scene) or "unknown",
        tostring(dialog_name),
        tostring(ui_obj_id(dialog)),
        tostring(button_name)))

    if ctx.agreement_checkbox_clicked ~= true then
        local check_ok, check_result = click_named_candidates(ctx, "agreement_checkbox", agreement_checkbox_names(), false)
        if not check_ok then
            flow_warn(ctx, "agreement_checkbox_failed", tostring(check_result))
        elseif check_result then
            ctx.agreement_checkbox_clicked = true
            sleep(ctx, 250)
        else
            flow_warn(ctx, "agreement_checkbox_not_found", "continue with agree button")
        end
    end

    local agree_ok, agree_result = click_named_candidates(ctx, "agreement_button", agreement_button_names(), true)
    if not agree_ok then
        return false, "ClickButton failed for agreement agree candidates err=" .. tostring(agree_result or find_err)
    end

    return true, true
end

local function accept_user_agreement_if_present(ctx, timeout_seconds, interval_ms)
    local deadline = now_ms(ctx) + ((tonumber(timeout_seconds) or 0) * 1000)
    local clicked_once = false
    local last_scene = nil

    while true do
        last_scene = log_scene_if_changed(ctx, "agreement_wait_scene", last_scene)
        local ok, clicked_or_absent = try_accept_user_agreement(ctx)
        if not ok then
            return false, clicked_or_absent
        end
        if clicked_or_absent then
            clicked_once = true
            sleep(ctx, interval_ms or 1000)
        else
            if clicked_once then
                flow_info(ctx, "agreement_done", "dialog no longer visible")
            else
                flow_info(ctx, "agreement_not_visible", "continue")
            end
            return true, clicked_once
        end

        if now_ms(ctx) > deadline then
            if clicked_once then
                return true, true
            end
            return false, "timeout checking user agreement"
        end
    end
end

function M.acceptAgreement(ctx, timeout_seconds, interval_ms)
    return accept_user_agreement_if_present(ctx or {}, timeout_seconds, interval_ms)
end

local function wait_for_game_init(ctx, pid, timeout_seconds)
    if not ok_core or not core or type(core.ensureInit) ~= "function" then
        return false, "aion.core.ensureInit unavailable"
    end

    local timeout = tonumber(timeout_seconds) or 120
    local deadline = now_ms(ctx) + (timeout * 1000)
    local last_err = nil
    flow_info(ctx, "core_init_wait", "pid=" .. tostring(pid) .. " timeout=" .. tostring(timeout))
    while now_ms(ctx) <= deadline do
        local ok, err = core.ensureInit(pid)
        if ok then
            flow_info(ctx, "core_init", "ok pid=" .. tostring(pid))
            return true, nil
        end

        local err_text = tostring(err or "unknown")
        if err_text ~= last_err then
            flow_warn(ctx, "core_init_pending", "pid=" .. tostring(pid) .. " err=" .. err_text)
            last_err = err_text
        end
        if string.find(err_text, "already initialized", 1, true) then
            return false, err_text
        end
        sleep(ctx, 1000)
    end

    return false, "InitGameinfo timeout: " .. tostring(last_err or "unknown")
end

local function log_server_list(ctx, list)
    list = list or {}
    flow_info(ctx, "server_list", "count=" .. tostring(#list))
    for i, server in ipairs(list) do
        flow_info(ctx, "server_item", string.format(
            "#%d key=%s server_id=%s addr=%s",
            i,
            tostring(server.key),
            tostring(server.server_id),
            tostring(server.addr)))
    end
end

local function log_character_list(ctx, list)
    list = list or {}
    flow_info(ctx, "character_list", "count=" .. tostring(#list))
    for i, char in ipairs(list) do
        flow_info(ctx, "character_item", string.format(
            "#%d name=%s id=%s level=%s race=%s race_name=%s job=%s map_id=%s addr=%s",
            i,
            tostring(char.name or ""),
            tostring(char.id or ""),
            tostring(char.level or ""),
            tostring(char.race or ""),
            tostring(char.race_name or ""),
            tostring(char.job or ""),
            tostring(char.map_id or ""),
            tostring(char.addr or "")))
    end
end

local function wait_for_server_list(ctx, timeout_seconds, interval_ms)
    if not ok_account_api or not account_api or type(account_api.serverList) ~= "function" then
        return false, nil, "aion.account.serverList unavailable"
    end

    local deadline = now_ms(ctx) + ((tonumber(timeout_seconds) or 60) * 1000)
    local last_scene = nil
    local last_err = nil
    local last_empty_log = 0
    while now_ms(ctx) <= deadline do
        last_scene = log_scene_if_changed(ctx, "server_wait_scene", last_scene)

        local agreement_ok, agreement_clicked_or_err = try_accept_user_agreement(ctx)
        if not agreement_ok then
            return false, nil, tostring(agreement_clicked_or_err)
        end
        if agreement_clicked_or_err then
            sleep(ctx, interval_ms or 1000)
        end

        local ok, list, err = account_api.serverList()
        if ok and type(list) == "table" and #list > 0 then
            log_server_list(ctx, list)
            return true, list, nil
        end

        if ok then
            if now_ms(ctx) - last_empty_log >= 5000 then
                flow_warn(ctx, "server_list_empty", "waiting for server_select_dialog")
                last_empty_log = now_ms(ctx)
            end
        else
            local err_text = tostring(err or "unknown")
            if err_text ~= last_err then
                flow_warn(ctx, "server_list_failed", err_text)
                last_err = err_text
            end
        end
        sleep(ctx, interval_ms or 1000)
    end

    return false, nil, "timeout waiting for server list"
end

local function resolve_server_key(server_cfg, list)
    server_cfg = type(server_cfg) == "table" and server_cfg or {}
    local configured_key = tonumber(server_cfg.key)
    if configured_key and configured_key >= 0 then
        return configured_key, "configured_key"
    end

    local configured_server_id = tonumber(server_cfg.server_id) or 0
    if configured_server_id > 0 then
        for _, server in ipairs(list or {}) do
            if tonumber(server.server_id) == configured_server_id then
                return tonumber(server.key), "configured_server_id"
            end
        end
    end

    local first = (list or {})[1]
    if type(first) == "table" then
        return tonumber(first.key), "first_available"
    end
    return nil, "none"
end

local function server_key_exists(server_key, list)
    local key = tonumber(server_key)
    if key == nil then
        return false
    end
    for _, server in ipairs(list or {}) do
        if tonumber(server.key) == key then
            return true
        end
    end
    return false
end

local function select_server(ctx, server_list)
    local account = ctx and ctx.account or {}
    local flow_cfg = ctx and ctx.flow_cfg or default_flow_config()
    local server_key, reason = resolve_server_key(account.server, server_list)
    if server_key == nil then
        return false, "no selectable server key"
    end
    if not server_key_exists(server_key, server_list) then
        return false, "configured server key not in current list: key=" .. tostring(server_key)
    end

    local attempts = math.max(1, math.min(4, tonumber(flow_cfg.server_submit_attempts) or 2))
    local interval_ms = math.max(150, tonumber(flow_cfg.server_submit_interval_ms) or 450)

    set_flow_status(ctx, "selecting_server", "key=" .. tostring(server_key) .. " attempts=" .. tostring(attempts))
    flow_info(ctx, "select_server", string.format(
        "key=%s reason=%s configured_server_id=%s",
        tostring(server_key),
        tostring(reason),
        tostring(account.server and account.server.server_id or "")))

    local last_err = nil
    for attempt = 1, attempts do
        local ok, selected, err = account_api.selectServer(server_key)
        flow_info(ctx, "select_server_attempt", string.format(
            "attempt=%d/%d key=%s ok=%s selected=%s err=%s",
            attempt,
            attempts,
            tostring(server_key),
            tostring(ok),
            tostring(selected),
            tostring(err or "")))

        if not ok or selected == false then
            last_err = tostring(err or selected)
        else
            last_err = nil
            local button_ok, button_name_or_err = click_named_candidates(
                ctx,
                "server_start_button",
                server_start_button_names(),
                false,
                server_select_dialog_name())
            if button_ok and button_name_or_err then
                flow_info(ctx, "server_start_submitted", "button=" .. tostring(button_name_or_err))
            elseif not button_ok then
                last_err = tostring(button_name_or_err)
                flow_warn(ctx, "server_start_button_failed", tostring(button_name_or_err))
            else
                last_err = "start_button not found"
                flow_warn(ctx, "server_start_button_not_found", "continue after SelectServer")
            end
        end

        sleep(ctx, interval_ms)

        local scene_ok, scene, scene_err = current_scene()
        flow_info(ctx, "select_server_scene", scene_ok and format_scene(scene) or ("failed err=" .. tostring(scene_err)))
        if scene_ok and scene_index(scene) == SCENE_CHARACTER_SELECT then
            return true, "key=" .. tostring(server_key) .. " attempts=" .. tostring(attempt)
        end
        if scene_ok and scene_index(scene) ~= SCENE_SERVER_SELECT then
            return true, "key=" .. tostring(server_key) .. " scene=" .. tostring(scene.index)
        end
    end

    if last_err then
        return false, "SelectServer failed key=" .. tostring(server_key) .. " err=" .. tostring(last_err)
    end
    return true, "key=" .. tostring(server_key) .. " attempts=" .. tostring(attempts)
end

local function wait_for_character_scene(ctx, timeout_seconds, interval_ms)
    local deadline = now_ms(ctx) + ((tonumber(timeout_seconds) or 60) * 1000)
    local last_scene = nil
    local last_char_err = nil
    while now_ms(ctx) <= deadline do
        if ok_core and core and type(core.getScene) == "function" then
            local ok, scene, err = core.getScene()
            local text = ok and format_scene(scene) or ("failed err=" .. tostring(err))
            if text ~= last_scene then
                flow_info(ctx, "character_wait_scene", text)
                last_scene = text
            end
            if ok and scene_index(scene) == SCENE_CHARACTER_SELECT then
                return true, scene, nil
            end
        end

        if ok_account_api and account_api and type(account_api.characterList) == "function" then
            local ok, list, err = account_api.characterList()
            if ok and type(list) == "table" and #list > 0 then
                return true, { index = SCENE_CHARACTER_SELECT, name = "character_list_available" }, nil
            end
            if not ok then
                local err_text = tostring(err or "unknown")
                if err_text ~= last_char_err then
                    flow_warn(ctx, "character_list_pending", err_text)
                    last_char_err = err_text
                end
            end
        end
        sleep(ctx, interval_ms or 1000)
    end

    return false, nil, "timeout waiting for character select scene"
end

local function read_character_list(ctx, timeout_seconds, interval_ms)
    if not ok_account_api or not account_api or type(account_api.characterList) ~= "function" then
        return false, nil, "aion.account.characterList unavailable"
    end

    local deadline = now_ms(ctx) + ((tonumber(timeout_seconds) or 10) * 1000)
    local last_err = nil
    while now_ms(ctx) <= deadline do
        local ok, list, err = account_api.characterList()
        if ok and type(list) == "table" then
            log_character_list(ctx, list)
            return true, list, nil
        end

        local err_text = tostring(err or "unknown")
        if err_text ~= last_err then
            flow_warn(ctx, "character_list_failed", err_text)
            last_err = err_text
        end
        sleep(ctx, interval_ms or 1000)
    end
    return false, nil, "timeout reading character list: " .. tostring(last_err or "unknown")
end

local function find_character_by_name(list, desired_name)
    desired_name = trim_text(desired_name)
    if desired_name == "" then
        return nil, nil, "desired_name_empty"
    end

    for index, char in ipairs(list or {}) do
        if tostring(char and char.name or "") == desired_name then
            return char, index, "configured_name"
        end
    end

    return nil, nil, "configured_name_missing"
end

local function remember_character_names(seen, list)
    seen = type(seen) == "table" and seen or {}
    for _, char in ipairs(list or {}) do
        local name = trim_text(char and char.name)
        if name ~= "" then
            seen[name] = true
        end
    end
    return seen
end

local function generated_character_name(ctx, seen)
    seen = type(seen) == "table" and seen or {}
    local count = #GENERATED_CHARACTER_NAMES
    if count <= 0 then
        return nil
    end

    local start = random_int(ctx, 1, count)
    for offset = 0, count - 1 do
        local index = ((start + offset - 1) % count) + 1
        local name = GENERATED_CHARACTER_NAMES[index]
        if not seen[name] then
            return name
        end
    end

    return nil
end

local function resolve_create_spec(ctx, account)
    account = type(account) == "table" and account or {}
    local server = type(account.server) == "table" and account.server or {}
    local character = type(account.character) == "table" and account.character or {}
    local race = tonumber(character.race)
    local job = tonumber(character.job)
    local gender = tonumber(character.gender)
    local gender_source = "configured"

    if race ~= 0 and race ~= 1 then
        race = 0
    end
    if job == nil or job <= 0 then
        job = 0x1
    end
    if gender ~= 0 and gender ~= 1 then
        gender = random_int(ctx, 0, 1)
        gender_source = "random"
    end

    return {
        desired_name = trim_text(server.character_name),
        race = race,
        job = job,
        gender = gender,
        gender_source = gender_source,
    }
end

local function wait_for_character_by_name(ctx, name, timeout_seconds, interval_ms)
    local deadline = now_ms(ctx) + ((tonumber(timeout_seconds) or 20) * 1000)
    local last_err = nil
    while now_ms(ctx) <= deadline do
        local ok, list, err = account_api.characterList()
        if ok and type(list) == "table" then
            log_character_list(ctx, list)
            local char, index = find_character_by_name(list, name)
            if char then
                return true, char, list, "index=" .. tostring(index)
            end
        else
            local err_text = tostring(err or "unknown")
            if err_text ~= last_err then
                flow_warn(ctx, "created_character_list_failed", err_text)
                last_err = err_text
            end
        end
        sleep(ctx, interval_ms or 1000)
    end

    return false, nil, nil, "timeout waiting for created character name=" .. tostring(name)
end

local function next_create_name(ctx, spec, seen)
    spec = type(spec) == "table" and spec or {}
    seen = type(seen) == "table" and seen or {}
    local desired_name = trim_text(spec.desired_name)
    if desired_name ~= "" and not seen[desired_name] then
        return desired_name, "configured"
    end

    local generated = generated_character_name(ctx, seen)
    if generated then
        return generated, "generated"
    end

    return nil, "none"
end

local function create_character_from_config(ctx, initial_list)
    if not ok_account_api or not account_api or type(account_api.createCharacter) ~= "function" then
        return false, nil, "aion.account.createCharacter unavailable"
    end

    local account = ctx and ctx.account or {}
    local flow_cfg = ctx and ctx.flow_cfg or default_flow_config()
    local spec = resolve_create_spec(ctx, account)
    local seen = remember_character_names({}, initial_list)
    local max_attempts = math.max(1, tonumber(flow_cfg.create_character_max_attempts) or 4)
    local last_err = nil

    for attempt = 1, max_attempts do
        local name, name_source = next_create_name(ctx, spec, seen)
        if not name then
            return false, nil, "no available generated character name"
        end
        seen[name] = true

        set_flow_status(ctx, "creating_character", "name=" .. tostring(name) .. " attempt=" .. tostring(attempt))
        flow_info(ctx, "create_character_attempt", string.format(
            "attempt=%s/%s name=%s source=%s gender=%s gender_source=%s race=%s job=%s",
            tostring(attempt),
            tostring(max_attempts),
            tostring(name),
            tostring(name_source),
            tostring(spec.gender),
            tostring(spec.gender_source),
            tostring(spec.race),
            tostring(spec.job)))

        local ok, created, err = account_api.createCharacter(name, spec.gender, spec.race, spec.job)
        if ok and created ~= false then
            local found_ok, char, latest_list, found_msg = wait_for_character_by_name(
                ctx,
                name,
                flow_cfg.create_character_recheck_timeout_seconds,
                flow_cfg.create_character_recheck_interval_ms)
            remember_character_names(seen, latest_list)
            if found_ok and char then
                local _, char_index = find_character_by_name(latest_list, char.name or name)
                if type(account.server) == "table" then
                    account.server.character_name = tostring(char.name or name)
                end
                flow_info(ctx, "create_character_success", string.format(
                    "name=%s id=%s level=%s %s",
                    tostring(char.name or name),
                    tostring(char.id or ""),
                    tostring(char.level or ""),
                    tostring(found_msg or "")))
                return true, { char = char, index = char_index }, nil
            end
            last_err = tostring(found_msg or "created character not found")
            flow_warn(ctx, "create_character_recheck_failed", last_err)
        else
            last_err = tostring(err or created or "CreateCharacter returned false")
            flow_warn(ctx, "create_character_failed", string.format(
                "name=%s attempt=%s err=%s",
                tostring(name),
                tostring(attempt),
                tostring(last_err)))
        end
    end

    return false, nil, "CreateCharacter failed after attempts=" .. tostring(max_attempts) .. " err=" .. tostring(last_err or "unknown")
end

local function select_character_entry(ctx, char, char_index, reason)
    if not char then
        return false, "character entry is missing"
    end
    char_index = tonumber(char_index) or 0
    if char_index <= 0 then
        return false, "character index is missing name=" .. tostring(char.name or "")
    end

    set_flow_status(ctx, "selecting_character", "name=" .. tostring(char.name or "") .. " index=" .. tostring(char_index))
    flow_info(ctx, "select_character", string.format(
        "index=%s reason=%s name=%s id=%s level=%s addr=%s",
        tostring(char_index),
        tostring(reason),
        tostring(char.name or ""),
        tostring(char.id or ""),
        tostring(char.level or ""),
        tostring(char.addr or "")))

    local ok, selected, err = account_api.selectCharacter(char_index)
    if not ok or selected == false then
        return false, "SelectCharacter failed name=" .. tostring(char.name or "") .. " err=" .. tostring(err or selected)
    end

    local start_ok, start_name_or_err = click_named_candidates(
        ctx,
        "character_start_button",
        character_start_button_names(),
        true,
        character_select_dialog_name())
    if not start_ok then
        return false, "character start button failed: " .. tostring(start_name_or_err)
    end
    flow_info(ctx, "character_start_submitted", "button=" .. tostring(start_name_or_err))
    return true, char
end

local function select_or_create_character(ctx, list)
    local account = ctx and ctx.account or {}
    list = list or {}
    local desired_name = trim_text(account.server and account.server.character_name)
    local char, char_index, reason = find_character_by_name(list, desired_name)
    if char then
        return select_character_entry(ctx, char, char_index, reason)
    end

    if desired_name ~= "" then
        flow_warn(ctx, "configured_character_missing_create", string.format(
            "configured=%s existing_count=%s",
            tostring(desired_name),
            tostring(#list)))
    elseif #list > 0 then
        flow_warn(ctx, "character_name_empty_create", "existing_count=" .. tostring(#list))
    else
        flow_info(ctx, "character_list_empty_create", "creating configured race/job")
    end

    local create_ok, created_char, create_err = create_character_from_config(ctx, list)
    if not create_ok then
        return false, tostring(create_err)
    end

    local created_entry = type(created_char) == "table" and created_char or {}
    return select_character_entry(ctx, created_entry.char, created_entry.index, "created_character")
end

local function current_ingame_character()
    if not ok_core or not core or type(core.getCharacter) ~= "function" then
        return false, nil, "aion.core.getCharacter unavailable"
    end
    local ok, char, err = core.getCharacter()
    if ok and type(char) == "table" and tostring(char.name or "") ~= "" then
        return true, char, nil
    end
    if ok then
        return true, nil, nil
    end
    return false, nil, err
end

local function read_second_password_dialog()
    if not ok_security or not security or type(security.secondPwdDialog) ~= "function" then
        return false, nil, "aion.security.secondPwdDialog unavailable"
    end
    local ok, dialog, err = security.secondPwdDialog()
    if not ok then
        return false, nil, err
    end
    if type(dialog) == "table" and (tonumber(dialog.addr) or 0) > 0 then
        return true, dialog, nil
    end
    return true, nil, nil
end

local function second_password_dialog_visible()
    if ok_ui and ui_api and type(ui_api.find) == "function" then
        local find_ok, dialog = ui_api.find(second_password_dialog_name())
        if find_ok and is_ui_visible(dialog) then
            return true
        end
    end

    local ok, dialog = read_second_password_dialog()
    return ok and type(dialog) == "table"
end

local function click_second_password_digits(ctx, pwd)
    local parent = second_password_dialog_name()
    click_named_candidates(ctx, "second_password_clear", second_password_clear_button_names(), false, parent)

    for pos = 1, #pwd do
        local digit = string.sub(pwd, pos, pos)
        if not string.match(digit, "^%d$") then
            return false, "second password contains non-digit at position " .. tostring(pos)
        end

        local digit_ok, digit_name_or_err = click_named_candidates(
            ctx,
            "second_password_digit_" .. digit,
            second_password_digit_button_names(digit),
            true,
            parent)
        if not digit_ok then
            return false, "second password digit failed digit=" .. tostring(digit) .. " err=" .. tostring(digit_name_or_err)
        end
        sleep(ctx, 120)
    end

    return true, nil
end

local function submit_second_password_ok(ctx)
    local ok_clicked, button_name_or_err = click_named_candidates(
        ctx,
        "second_password_ok",
        second_password_ok_button_names(),
        false,
        second_password_dialog_name())
    if ok_clicked and button_name_or_err then
        flow_info(ctx, "second_password_ok_submitted", "button=" .. tostring(button_name_or_err))
        return true, nil
    end

    if second_password_dialog_visible() then
        return false, "second password ok button not found: " .. tostring(button_name_or_err)
    end

    flow_info(ctx, "second_password_ok_absent", "dialog no longer visible")
    return true, nil
end

local function input_second_password(ctx, dialog)
    local account = ctx and ctx.account or {}
    local pwd = trim_text(account.second_password)
    flow_info(ctx, "second_password", string.format(
        "dialog_addr=%s title=%s password_configured=%s",
        tostring(dialog and dialog.addr or ""),
        tostring(dialog and dialog.title or ""),
        tostring(pwd ~= "")))

    if pwd == "" then
        return false, "second password dialog visible but account.second_password is empty"
    end

    set_flow_status(ctx, "input_second_password", "dialog_addr=" .. tostring(dialog.addr))
    local submitted = false
    if ok_security and security and type(security.inputSecondPwd) == "function" then
        local ok, ret, err = security.inputSecondPwd(dialog.addr, pwd, false)
        if ok and ret ~= false then
            submitted = true
            flow_info(ctx, "second_password_submitted", "source=api ret=" .. tostring(ret))
        else
            flow_warn(ctx, "second_password_api_failed", tostring(err or ret))
        end
    else
        flow_warn(ctx, "second_password_api_unavailable", tostring(security))
    end

    if not submitted then
        local manual_ok, manual_err = click_second_password_digits(ctx, pwd)
        if not manual_ok then
            return false, manual_err
        end
        flow_info(ctx, "second_password_submitted", "source=ui_digits length=" .. tostring(#pwd))
    end

    sleep(ctx, 250)
    local ok_ok, ok_err = submit_second_password_ok(ctx)
    if not ok_ok then
        return false, ok_err
    end
    return true, nil
end

local function wait_for_enter_game(ctx, timeout_seconds, interval_ms)
    local deadline = now_ms(ctx) + ((tonumber(timeout_seconds) or 90) * 1000)
    local last_scene = nil
    local security_warned = false
    local second_password_done = false

    while now_ms(ctx) <= deadline do
        local char_ok, char, char_err = current_ingame_character()
        if char_ok and char then
            flow_info(ctx, "entered_game", string.format(
                "name=%s level=%s race=%s job=%s map_id=%s",
                tostring(char.name or ""),
                tostring(char.level or ""),
                tostring(char.race or ""),
                tostring(char.job or ""),
                tostring(char.map_id or "")))
            return true, char, nil
        end
        if not char_ok and char_err then
            flow_warn(ctx, "get_character_failed", tostring(char_err))
        end

        last_scene = log_scene_if_changed(ctx, "enter_wait_scene", last_scene)

        if ok_security and security and type(security.secondPwdDialog) == "function" then
            local dialog_ok, dialog, dialog_err = read_second_password_dialog()
            if dialog_ok and dialog and not second_password_done then
                local pwd_ok, pwd_err = input_second_password(ctx, dialog)
                if not pwd_ok then
                    return false, nil, pwd_err
                end
                second_password_done = true
            elseif not dialog_ok and not security_warned then
                flow_warn(ctx, "second_password_probe_failed", tostring(dialog_err))
                security_warned = true
            end
        elseif not security_warned then
            flow_warn(ctx, "second_password_probe_unavailable", tostring(security))
            security_warned = true
        end

        sleep(ctx, interval_ms or 1000)
    end

    return false, nil, "timeout waiting for in-game character"
end

M._test = {
    findCharacterByName = find_character_by_name,
    generatedCharacterName = generated_character_name,
    resolveCreateSpec = resolve_create_spec,
}

function M.run(ctx)
    ctx = ctx or {}
    local account = ctx.account or {}
    local accounts_cfg = ctx.accounts_cfg or {}
    local flow_cfg = normalize_flow_config(accounts_cfg.login_flow)
    ctx.flow_cfg = flow_cfg
    local candidate = ctx.candidate or {}
    local pid = tonumber(candidate.pid) or 0
    if pid <= 0 then
        return false, "post-login flow requires game pid"
    end

    local modules_ok, modules_err = ensure_modules()
    if not modules_ok then
        return false, modules_err
    end

    flow_info(ctx, "flow_begin", string.format(
        "pid=%s server_key=%s server_id=%s desired_character=%s",
        tostring(pid),
        tostring(account.server and account.server.key or ""),
        tostring(account.server and account.server.server_id or ""),
        tostring(account.server and account.server.character_name or "")))

    set_flow_status(ctx, "post_login_init", "pid=" .. tostring(pid))
    local init_ok, init_err = wait_for_game_init(ctx, pid, flow_cfg.init_timeout_seconds)
    if not init_ok then
        return false, "AionData init failed: " .. tostring(init_err)
    end
    set_progress(ctx, 0.65)

    set_flow_status(ctx, "checking_agreement", "before server select")
    local agreement_ok, agreement_err = accept_user_agreement_if_present(
        ctx,
        flow_cfg.agreement_timeout_seconds,
        flow_cfg.agreement_retry_interval_ms)
    if not agreement_ok then
        return false, tostring(agreement_err)
    end

    set_flow_status(ctx, "waiting_server_select", "waiting server list")
    local server_ok, server_list, server_err = wait_for_server_list(
        ctx,
        flow_cfg.server_timeout_seconds,
        flow_cfg.poll_interval_ms)
    if not server_ok then
        return false, tostring(server_err)
    end

    local select_ok, select_msg = select_server(ctx, server_list)
    if not select_ok then
        return false, select_msg
    end
    set_progress(ctx, 0.75)

    set_flow_status(ctx, "waiting_character_select", "after server " .. tostring(select_msg))
    local scene_ok, _, scene_err = wait_for_character_scene(
        ctx,
        flow_cfg.character_timeout_seconds,
        flow_cfg.poll_interval_ms)
    if not scene_ok then
        return false, tostring(scene_err)
    end

    local chars_ok, chars, chars_err = read_character_list(ctx, 10, flow_cfg.poll_interval_ms)
    if not chars_ok then
        return false, tostring(chars_err)
    end

    local char_select_ok, selected_char_or_err = select_or_create_character(ctx, chars)
    if not char_select_ok then
        return false, selected_char_or_err
    end
    set_character(ctx, selected_char_or_err)
    set_progress(ctx, 0.85)

    set_flow_status(ctx, "waiting_enter_game", "character=" .. tostring(selected_char_or_err.name or ""))
    local enter_ok, ingame_char, enter_err = wait_for_enter_game(
        ctx,
        flow_cfg.enter_game_timeout_seconds,
        flow_cfg.poll_interval_ms)
    if not enter_ok then
        return false, enter_err
    end

    set_character(ctx, ingame_char)
    return true, "entered_game character=" .. tostring(ingame_char.name or "")
end

return M
