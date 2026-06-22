local Result = require("maple.core.result")
local MockEnvironment = require("maple.environment.mock_environment")
local MapleApi = require("maple.environment.maple_api")
local Normalize = require("maple.environment.normalizers")

local MapleEnvironment = {}
MapleEnvironment.__index = MapleEnvironment

local function result_value(result)
    return result and result.data and result.data.value
end

local function result_values(result)
    return result and result.data and result.data.values or {}
end

local function diagnostic(result)
    return result and result.data and result.data.diagnostic or result and result.data
end

local function failed_snapshot(result)
    return {
        api_ok = false,
        api_error = result and result.reason or "api_failure",
        source = diagnostic(result)
    }
end

local function quickslot_slot_for_skill(bb, skill_id)
    if not bb or not bb.skill or not skill_id then return nil end
    local wanted = tostring(skill_id)
    for _, slot in ipairs(bb.skill.quickslots or {}) do
        if tostring(slot.id or "") == wanted or tostring(slot.numeric_id or "") == wanted then
            return slot.slot
        end
    end
    return nil
end

local DEFAULT_SKILL_RELEASE = {
    method = "press_key",
    key_name = "Shift",
    key_code = 0x10,
    input_mode = "foreground",
    quickslot_use_trusted = false,
    fallback_to_basic_attack = true
}

local function sleep_ms(ms)
    if sys and sys.sleep then sys.sleep(tonumber(ms) or 0) end
end

local function key_code_number(value, default_value)
    if type(value) == "string" then
        return tonumber(value) or tonumber(value:match("^0[xX](%x+)$"), 16) or default_value
    end
    return tonumber(value) or default_value
end

local function merge_skill_release(target, source)
    if type(source) ~= "table" then return target end
    if source.method ~= nil or source.skill_use_method ~= nil then
        target.method = tostring(source.method or source.skill_use_method)
    end
    if source.key_name ~= nil or source.skill_key ~= nil then
        target.key_name = tostring(source.key_name or source.skill_key)
    end
    if source.key_code ~= nil or source.skill_key_code ~= nil then
        target.key_code = key_code_number(source.key_code or source.skill_key_code, target.key_code)
    end
    if source.input_mode ~= nil or source.skill_input_mode ~= nil then
        target.input_mode = tostring(source.input_mode or source.skill_input_mode)
    end
    if source.hold_ms ~= nil or source.skill_hold_ms ~= nil then
        target.hold_ms = tonumber(source.hold_ms or source.skill_hold_ms) or 0
    end
    if source.key_mode ~= nil then target.key_mode = source.key_mode end
    if source.quickslot_use_trusted ~= nil then target.quickslot_use_trusted = source.quickslot_use_trusted == true end
    if source.fallback_to_basic_attack ~= nil then target.fallback_to_basic_attack = source.fallback_to_basic_attack ~= false end
    return target
end

local function safe_call(fn)
    local ok, result = pcall(fn)
    if not ok then return false, tostring(result) end
    return result ~= false, result
end

function MapleEnvironment.new(opts)
    opts = opts or {}
    local api = opts.api or MapleApi.new({
        data_module = opts.data_module,
        module_name = opts.module_name or "data",
        logger = opts.logger,
        account_index = opts.account_index
    })
    return setmetatable({
        capabilities = {
            can_bind_client = true,
            can_login = false,
            can_navigate = false,
            can_interact = true,
            can_manage_inventory = true,
            can_evaluate_equipment = false,
            can_learn_skill = false,
            can_execute_combat = true,
            real_client = true
        },
        adapter_name = "maple_environment",
        api = api,
        mock = MockEnvironment.new(opts.world),
        connected = false,
        pid = nil,
        hwnd = opts.hwnd,
        target_name = opts.target_name or "msw.exe",
        license_key = opts.license_key,
        key_api = opts.key_api or keybd,
        wnd_api = opts.wnd_api or wnd,
        proc_api = opts.proc_api or proc,
        input_mode = opts.input_mode or "foreground",
        key_mode = opts.key_mode or "api",
        skill_release = opts.skill_release,
        allow_mock_fallback = opts.allow_mock_fallback ~= false
    }, MapleEnvironment)
end

function MapleEnvironment:fallback(method, result)
    if not self.allow_mock_fallback then return failed_snapshot(result) end
    local snapshot = self.mock[method](self.mock)
    snapshot.api_ok = false
    snapshot.api_error = result and result.reason or "api_failure"
    snapshot.source = diagnostic(result)
    return snapshot
end

function MapleEnvironment:get_actor_state(bb)
    local result = self.api:call("player_info", bb)
    if result.ok then return Normalize.actor(result_value(result), diagnostic(result)) end
    return self:fallback("get_actor_state", result)
end

function MapleEnvironment:get_inventory_state(bb)
    local result = self.api:call("list_inventory", bb)
    if result.ok then return Normalize.inventory(result_value(result), diagnostic(result)) end
    return self:fallback("get_inventory_state", result)
end

function MapleEnvironment:get_quest_state()
    return self.mock:get_quest_state()
end

function MapleEnvironment:get_equipment_state()
    return self.mock:get_equipment_state()
end

function MapleEnvironment:get_skill_state(bb)
    local skills = self.api:call("list_skills", bb)
    local quickslots = self.api:call("list_quickslot", bb)
    if skills.ok or quickslots.ok then
        return Normalize.skill(
            skills.ok and result_value(skills) or {},
            quickslots.ok and result_value(quickslots) or {},
            diagnostic(skills),
            diagnostic(quickslots)
        )
    end
    return self:fallback("get_skill_state", skills)
end

function MapleEnvironment:get_world_state(bb)
    local result = self.api:call("list_nearby", bb)
    if result.ok then return Normalize.world(result_value(result), diagnostic(result)) end
    return self:fallback("get_world_state", result)
end

function MapleEnvironment:bind_client(action, bb)
    local params = action.params or {}
    local result = self.api:call("connect", bb, params.target_name or self.target_name, params.license_key or self.license_key)
    if not result.ok then return result end
    local values = result_values(result)
    if values[1] == false then return Result.failure("connect_failed", { values = values }) end
    self.connected = true
    self.pid = values[2]
    return Result.success({ pid = self.pid, values = values, diagnostic = diagnostic(result) })
end

function MapleEnvironment:basic_attack(action, bb)
    local result = self.api:call("do_attack", bb)
    if not result.ok then return result end
    return Result.success({ raw = result_value(result), diagnostic = diagnostic(result) })
end

function MapleEnvironment:use_quickslot(action, bb)
    local params = action.params or {}
    local result = self.api:call("quickslot_use", bb, params.slot, params.action or "press")
    if not result.ok then return result end
    return Result.success({ slot = params.slot, action = params.action or "press", raw = result_value(result), diagnostic = diagnostic(result) })
end

function MapleEnvironment:resolve_window(params)
    params = params or {}
    if params.hwnd then return params.hwnd end
    if self.hwnd then return self.hwnd end
    local pid = params.pid or self.pid
    if self.proc_api and self.proc_api.window and pid then
        local ok, hwnd = pcall(self.proc_api.window, pid)
        if ok and hwnd and hwnd ~= 0 then
            self.hwnd = hwnd
            return hwnd
        end
    end
    if self.wnd_api and self.wnd_api.find_by_pid and pid then
        local ok, hwnd = pcall(self.wnd_api.find_by_pid, pid)
        if ok and hwnd and hwnd ~= 0 then
            self.hwnd = hwnd
            return hwnd
        end
    end
    return nil
end

function MapleEnvironment:apply_key_mode(mode)
    if mode == nil or mode == "" then return Result.success({ skipped = true }) end
    if not self.key_api or type(self.key_api.set_mode) ~= "function" then
        return Result.success({ skipped = true, reason = "set_mode_unavailable" })
    end
    local ok, result = pcall(self.key_api.set_mode, mode)
    if not ok then return Result.failure("set_mode_failed", { error = tostring(result), mode = mode }) end
    return Result.success({ mode = mode, raw = result })
end

function MapleEnvironment:press_foreground_key(params)
    local key_code = params.key_code
    local hold_ms = tonumber(params.hold_ms) or 0
    local hwnd = self:resolve_window(params)
    if params.focus_window ~= false and self.wnd_api and self.wnd_api.set_foreground and hwnd then
        pcall(self.wnd_api.set_foreground, hwnd)
        sleep_ms(80)
    end

    if hold_ms > 0 and self.key_api.down and self.key_api.up then
        local down_ok, down_result = safe_call(function() return self.key_api.down(key_code) end)
        sleep_ms(hold_ms)
        local up_ok, up_result = safe_call(function() return self.key_api.up(key_code) end)
        if down_ok and up_ok then
            return Result.success({ method = "keybd.down_up", key_code = key_code, hwnd = hwnd, down = down_result, up = up_result })
        end
        return Result.failure("key_down_up_failed", { key_code = key_code, hwnd = hwnd, down = down_result, up = up_result })
    end

    if not self.key_api.click then return Result.failure("key_click_unavailable", { key_code = key_code, hwnd = hwnd }) end
    local ok, raw = safe_call(function() return self.key_api.click(key_code) end)
    if not ok then return Result.failure("key_click_failed", { key_code = key_code, hwnd = hwnd, raw = raw }) end
    return Result.success({ method = "keybd.click", key_code = key_code, hwnd = hwnd, raw = raw })
end

function MapleEnvironment:press_background_key(params)
    local key_code = params.key_code
    local hold_ms = tonumber(params.hold_ms) or 0
    local hwnd = self:resolve_window(params)
    if not hwnd then return Result.failure("window_not_found", { pid = params.pid or self.pid }) end

    if hold_ms > 0 and self.key_api.post_key then
        local down_ok, down_result = safe_call(function() return self.key_api.post_key(hwnd, key_code, true) end)
        sleep_ms(hold_ms)
        local up_ok, up_result = safe_call(function() return self.key_api.post_key(hwnd, key_code, false) end)
        if down_ok and up_ok then
            return Result.success({ method = "keybd.post_key", key_code = key_code, hwnd = hwnd, down = down_result, up = up_result })
        end
        return Result.failure("post_key_failed", { key_code = key_code, hwnd = hwnd, down = down_result, up = up_result })
    end

    if not self.key_api.post_click then return Result.failure("post_click_unavailable", { key_code = key_code, hwnd = hwnd }) end
    local ok, raw = safe_call(function() return self.key_api.post_click(hwnd, key_code) end)
    if not ok then return Result.failure("post_click_failed", { key_code = key_code, hwnd = hwnd, raw = raw }) end
    return Result.success({ method = "keybd.post_click", key_code = key_code, hwnd = hwnd, raw = raw })
end

function MapleEnvironment:press_key(action, bb)
    local params = action.params or {}
    local key_code = key_code_number(params.key_code, nil)
    if not key_code then return Result.failure("missing_key_code", { params = params }) end
    if not self.key_api then return Result.failure("key_api_unavailable", { key_code = key_code }) end

    local mode = params.input_mode or self.input_mode or "foreground"
    local key_mode = params.key_mode or self.key_mode
    if mode ~= "background" and (key_mode == nil or key_mode == "") then key_mode = "api" end
    local mode_result = self:apply_key_mode(key_mode)
    if not mode_result.ok then return mode_result end

    local call_params = {
        key_code = key_code,
        key_name = params.key_name,
        input_mode = mode,
        key_mode = key_mode,
        hold_ms = tonumber(params.hold_ms) or 0,
        hwnd = params.hwnd,
        pid = params.pid,
        focus_window = params.focus_window
    }
    local result
    if mode == "background" then
        result = self:press_background_key(call_params)
    else
        result = self:press_foreground_key(call_params)
    end
    if result.ok and result.data then
        result.data.key_name = params.key_name
        result.data.input_mode = mode
        result.data.key_mode = key_mode
        result.data.pid = params.pid or self.pid
    end
    return result
end

function MapleEnvironment:set_walk_direction(action, bb)
    local params = action.params or {}
    local result = self.api:call("walk", bb, params.direction, params.vertical or 0)
    if not result.ok then return result end
    return Result.success({ direction = params.direction, vertical = params.vertical or 0, raw = result_value(result), diagnostic = diagnostic(result) })
end

function MapleEnvironment:pick_all_drops(action, bb)
    local result = self.api:call("pick_all", bb)
    if not result.ok then return result end
    return Result.success({ raw = result_value(result), diagnostic = diagnostic(result) })
end

function MapleEnvironment:use_item(action, bb)
    local params = action.params or {}
    local result = self.api:call("use_item", bb, params.item_code)
    if not result.ok then return result end
    return Result.success({ item_code = params.item_code, raw = result_value(result), diagnostic = diagnostic(result) })
end

function MapleEnvironment:equip_item(action, bb)
    local params = action.params or {}
    local result = self.api:call("equip_item", bb, params.item_code)
    if not result.ok then return result end
    return Result.success({ item_code = params.item_code, raw = result_value(result), diagnostic = diagnostic(result) })
end

function MapleEnvironment:execute_combat_decision(action, bb)
    local proposal = action.params and action.params.proposal or {}
    if proposal.executable == false then
        return Result.success({ skipped = true, proposal = proposal, reason = proposal.reason })
    end
    if proposal.action ~= "cast_skill" then
        return Result.success({ skipped = true, proposal = proposal, reason = "unsupported_combat_action" })
    end

    local params = proposal.params or {}
    local release = {}
    merge_skill_release(release, DEFAULT_SKILL_RELEASE)
    merge_skill_release(release, self.skill_release)
    if bb and bb.account then
        merge_skill_release(release, bb.account.skill_release)
        merge_skill_release(release, bb.account)
    end
    merge_skill_release(release, proposal.skill_release)
    merge_skill_release(release, params.skill_release)

    local slot = params.quickslot_slot or proposal.quickslot_slot or quickslot_slot_for_skill(bb, params.skill_id or proposal.skill_id)
    if release.method == "quickslot" and release.quickslot_use_trusted == true and slot then
        return self:use_quickslot({ params = { slot = slot, action = "press" } }, bb)
    end
    if release.method == "press_key" then
        local result = self:press_key({
            params = {
                key_code = release.key_code,
                key_name = release.key_name,
                input_mode = release.input_mode,
                key_mode = release.key_mode,
                hold_ms = release.hold_ms
            }
        }, bb)
        if result.ok or release.fallback_to_basic_attack ~= true then return result end
    end
    return self:basic_attack({ params = {} }, bb)
end

function MapleEnvironment:perform_action(action, bb)
    if action.name == "BindClient" then return self:bind_client(action, bb) end
    if action.name == "BasicAttack" then return self:basic_attack(action, bb) end
    if action.name == "UseQuickslot" then return self:use_quickslot(action, bb) end
    if action.name == "PressKey" then return self:press_key(action, bb) end
    if action.name == "SetWalkDirection" then return self:set_walk_direction(action, bb) end
    if action.name == "StopMove" then return self:set_walk_direction({ params = { direction = 0, vertical = 0 } }, bb) end
    if action.name == "PickAllDrops" then return self:pick_all_drops(action, bb) end
    if action.name == "UseItem" then return self:use_item(action, bb) end
    if action.name == "EquipItem" then return self:equip_item(action, bb) end
    if action.name == "ExecuteCombatDecision" then return self:execute_combat_decision(action, bb) end
    return self.mock:perform_action(action, bb)
end

return MapleEnvironment
