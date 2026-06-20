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
        target_name = opts.target_name or "msw.exe",
        license_key = opts.license_key,
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
    return Result.success({ pid = values[2], values = values, diagnostic = diagnostic(result) })
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
    local slot = params.quickslot_slot or proposal.quickslot_slot or quickslot_slot_for_skill(bb, params.skill_id or proposal.skill_id)
    if slot then
        return self:use_quickslot({ params = { slot = slot, action = "press" } }, bb)
    end
    return self:basic_attack({ params = {} }, bb)
end

function MapleEnvironment:perform_action(action, bb)
    if action.name == "BindClient" then return self:bind_client(action, bb) end
    if action.name == "BasicAttack" then return self:basic_attack(action, bb) end
    if action.name == "UseQuickslot" then return self:use_quickslot(action, bb) end
    if action.name == "SetWalkDirection" then return self:set_walk_direction(action, bb) end
    if action.name == "StopMove" then return self:set_walk_direction({ params = { direction = 0, vertical = 0 } }, bb) end
    if action.name == "PickAllDrops" then return self:pick_all_drops(action, bb) end
    if action.name == "UseItem" then return self:use_item(action, bb) end
    if action.name == "EquipItem" then return self:equip_item(action, bb) end
    if action.name == "ExecuteCombatDecision" then return self:execute_combat_decision(action, bb) end
    return self.mock:perform_action(action, bb)
end

return MapleEnvironment
