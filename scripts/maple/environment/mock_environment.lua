local Result = require("maple.core.result")

local MockEnvironment = {}
MockEnvironment.__index = MockEnvironment

function MockEnvironment.new(world)
    world = world or {}
    return setmetatable({
        capabilities = {
            can_bind_client = true,
            can_login = true,
            can_navigate = true,
            can_interact = true,
            can_manage_inventory = true,
            can_evaluate_equipment = true,
            can_learn_skill = true
        },
        world = world
    }, MockEnvironment)
end

function MockEnvironment:get_actor_state()
    return self.world.actor or {
        level = 1, hp = 100, max_hp = 100, mp = 100, max_mp = 100,
        is_dead = false, is_in_combat = false,
        position = { x = 0, y = 0, z = 0 },
        current_map = "mock_map"
    }
end

function MockEnvironment:get_inventory_state()
    return self.world.inventory or { used_slots = 0, max_slots = 100, items = {}, is_full = false }
end

function MockEnvironment:get_quest_state()
    return self.world.quest or { active = {}, completed = {}, current_quest_id = nil, current_objective_index = 1 }
end

function MockEnvironment:get_equipment_state()
    return self.world.equipment or { current = {}, candidates = {}, upgrade_available = false, durability_low = false }
end

function MockEnvironment:get_skill_state()
    return self.world.skill or { learned = {}, available = {}, should_learn = false, trainer_known = false }
end

function MockEnvironment:get_world_state()
    return self.world.world or { nearby_npcs = {}, nearby_targets = {}, nearby_resources = {}, selected_entity = nil }
end

function MockEnvironment:perform_action(action, bb)
    if action.name == "BindClient" then
        return Result.success({ account_index = action.params.account_index, mock = true })
    elseif action.name == "Login" then
        return Result.success({ account = action.params.account, mock = true })
    elseif action.name == "NavigateTo" then
        bb.actor.position = action.params.destination
        return Result.success({ arrived = true, mock = true })
    elseif action.name == "InteractNpc" then
        return Result.success({ npc_id = action.params.npc_id, mock = true })
    elseif action.name == "ProcessInventoryRules" then
        bb.inventory.is_full = false
        return Result.success({ processed = true })
    elseif action.name == "EvaluateEquipmentCandidates" then
        bb.equipment.upgrade_available = false
        return Result.success({ selected = nil })
    elseif action.name == "LearnSkill" then
        bb.skill.learned[action.params.skill_id] = true
        return Result.success({ skill_id = action.params.skill_id })
    elseif action.name == "Wait" or action.name == "Idle" then
        return Result.success({ waited = action.params.seconds or 0 })
    elseif action.name == "Stop" then
        bb.runtime.running = false
        bb.runtime.stop_requested = true
        return Result.success({ reason = action.params.reason })
    end
    return Result.failure("unsupported_action")
end

return MockEnvironment
