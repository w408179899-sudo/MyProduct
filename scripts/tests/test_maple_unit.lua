local T = require("tests.test_framework")

local Config = require("maple.config")
local Blackboard = require("maple.blackboard")
local Logger = require("maple.systems.logger")
local Planner = require("maple.planner.planner")
local Executor = require("maple.systems.executor")
local MockEnvironment = require("maple.environment.mock_environment")
local Sequence = require("maple.bt.sequence")
local Selector = require("maple.bt.selector")
local PrioritySelector = require("maple.bt.priority_selector")
local Timeout = require("maple.bt.timeout")
local Retry = require("maple.bt.retry")
local BT = require("maple.bt.constants")
local Safety = require("maple.systems.safety")
local Snapshot = require("maple.systems.snapshot")
local Replay = require("maple.systems.replay")
local InventoryManager = require("maple.managers.inventory_manager")
local EquipmentManager = require("maple.managers.equipment_manager")
local SkillManager = require("maple.managers.skill_manager")
local QuestManager = require("maple.managers.quest_manager")
local ObjectiveResolver = require("maple.managers.objective_resolver")
local CombatManager = require("maple.managers.combat_manager")
local CombatResolver = require("maple.combat.resolver")
local Perception = require("maple.systems.perception")
local MapleApi = require("maple.environment.maple_api")
local MapleEnvironment = require("maple.environment.maple_environment")
local Normalize = require("maple.environment.normalizers")
local Probe = require("maple.probes.api_probe")
local ApiSample = require("maple.probes.fixtures.api_sample")

local function fake_data()
    local data = { calls = {} }
    function data.connect(target_name, license_key)
        data.calls[#data.calls + 1] = { name = "connect", target_name = target_name, license_key = license_key }
        return true, 1234
    end
    function data.player_info()
        data.calls[#data.calls + 1] = { name = "player_info" }
        return {
            Hp = "1500", Mp = "800", Level = "30", MaxHp = "2000", MaxMp = "1000",
            Nickname = "hero", CharId = "c1", X = "320.5", Y = "-180.0",
            WalkSpeed = "2.0", Gravity = "-1.0", Invincible = "false",
            MapId = "100020000", MapName = "Training"
        }
    end
    function data.list_inventory()
        data.calls[#data.calls + 1] = { name = "list_inventory" }
        return { meso = "99", items = { { Code = 2000002, Count = "3", name = "Potion" } } }
    end
    function data.list_skills()
        data.calls[#data.calls + 1] = { name = "list_skills" }
        return { point = "2", used = "1", skills = { { Code = 1001004, CurrentLevel = "3", name = "Slash" } } }
    end
    function data.list_quickslot()
        data.calls[#data.calls + 1] = { name = "list_quickslot" }
        return { { slot = 1, key = "Shift", cat = "Skill", id = "1001004" } }
    end
    function data.list_nearby()
        data.calls[#data.calls + 1] = { name = "list_nearby" }
        return {
            mobCount = 1,
            dropCount = 1,
            portalCount = 1,
            npcCount = 1,
            mobs = { { Name = "Snail", MobId = 100101, Level = "5", x = "100.0", y = "200.0", Hp = "50", MaxHp = "50" } },
            drops = { { Name = "Red Potion", ItemId = 2000003, OwnerCID = "mine", Free = false, x = "105.0", y = "201.0" } },
            portals = { { Name = "sp", PortalType = 1, DestMap = "100", DestPortal = "in", x = "0", y = "0" } },
            npcs = { { Name = "Guide", NpcCode = 9000000, x = "10", y = "20" } }
        }
    end
    function data.do_attack()
        data.calls[#data.calls + 1] = { name = "do_attack" }
        return "ok:attack"
    end
    function data.quickslot_use(slot, action)
        data.calls[#data.calls + 1] = { name = "quickslot_use", slot = slot, action = action }
        return "ok:quickslot"
    end
    function data.walk(direction, vertical)
        data.calls[#data.calls + 1] = { name = "walk", direction = direction, vertical = vertical }
        return "ok:walk"
    end
    function data.pick_all()
        data.calls[#data.calls + 1] = { name = "pick_all" }
        return "ok: picked=1"
    end
    function data.use_item(item_code)
        data.calls[#data.calls + 1] = { name = "use_item", item_code = item_code }
        return "ok:use_item"
    end
    function data.equip_item(item_code)
        data.calls[#data.calls + 1] = { name = "equip_item", item_code = item_code }
        return "ok:equip_item"
    end
    return data
end

local function test_logger()
    return Logger.new("test", { level = "debug", print_to_console = false, keep_records = 50 })
end

local function run()
    T.reset()
    T.log("\n=== maple unit tests ===")

    T.test("planner selects recovery before other goals", function()
        local bb = Blackboard.new({ account = { enabled = true } })
        bb.runtime.last_error = "boom"
        bb.inventory.is_full = true
        Planner.new(Config, test_logger()):update(bb)
        T.assert_eq(bb.task.active_goal, "recovery")
    end)

    T.test("planner selects inventory and prevents low-priority thrash", function()
        local bb = Blackboard.new({ account = { enabled = true } })
        bb.runtime.tick = 1
        bb.inventory.is_full = true
        local planner = Planner.new(Config, test_logger())
        planner:update(bb)
        T.assert_eq(bb.task.active_goal, "inventory")
        bb.inventory.is_full = false
        bb.runtime.tick = 2
        planner:update(bb)
        T.assert_eq(bb.task.active_goal, "inventory")
    end)

    T.test("behavior tree sequence and selector statuses", function()
        local success = { tick = function() return BT.SUCCESS end }
        local failure = { tick = function() return BT.FAILURE end }
        T.assert_eq(Sequence.new("seq", { success, failure }):tick({}), BT.FAILURE)
        T.assert_eq(Selector.new("sel", { failure, success }):tick({}), BT.SUCCESS)
    end)

    T.test("priority selector respects priority", function()
        local picked = ""
        local low = { priority = 1, tick = function() picked = "low"; return BT.SUCCESS end }
        local high = { priority = 10, tick = function() picked = "high"; return BT.SUCCESS end }
        T.assert_eq(PrioritySelector.new("prio", { low, high }):tick({}), BT.SUCCESS)
        T.assert_eq(picked, "high")
    end)

    T.test("timeout and retry decorators are bounded", function()
        local bb = Blackboard.new()
        local running = { tick = function() return BT.RUNNING end }
        local timeout = Timeout.new("timeout", running, 0, test_logger())
        bb.runtime.tick = 2
        T.assert_eq(timeout:tick(bb), BT.FAILURE)

        local retry = Retry.new("retry", { tick = function() return BT.FAILURE end }, 1, test_logger())
        T.assert_eq(retry:tick(bb), BT.RUNNING)
        T.assert_eq(retry:tick(bb), BT.FAILURE)
    end)

    T.test("executor rejects unknown and malformed actions", function()
        local bb = Blackboard.new()
        local executor = Executor.new(MockEnvironment.new(), Config, test_logger())
        local action, err = executor:queue_action(bb, "NoSuchAction", {})
        T.assert_nil(action)
        T.assert_eq(err, "unknown_action")
        action, err = executor:queue_action(bb, "NavigateTo", {})
        T.assert_nil(action)
        T.assert_contains(err, "missing_param")
    end)

    T.test("executor records successful action lifecycle", function()
        local bb = Blackboard.new()
        local executor = Executor.new(MockEnvironment.new(), Config, test_logger())
        local action = executor:queue_action(bb, "Wait", { seconds = 1 })
        T.assert_not_nil(action)
        executor:flush(bb)
        T.assert_eq(bb.metrics.action_success_count, 1)
        T.assert_eq(bb.task.failure_count, 0)
    end)

    T.test("executor accepts atomic Maple action specs", function()
        local bb = Blackboard.new()
        local executor = Executor.new(MockEnvironment.new(), Config, test_logger())
        T.assert_not_nil(executor:queue_action(bb, "BasicAttack", {}))
        T.assert_not_nil(executor:queue_action(bb, "UseQuickslot", { slot = 1 }))
        T.assert_not_nil(executor:queue_action(bb, "SetWalkDirection", { direction = 1 }))
        T.assert_not_nil(executor:queue_action(bb, "StopMove", {}))
        T.assert_not_nil(executor:queue_action(bb, "PickAllDrops", {}))
        T.assert_not_nil(executor:queue_action(bb, "UseItem", { item_code = 2000002 }))
        T.assert_not_nil(executor:queue_action(bb, "EquipItem", { item_code = 1002000 }))
    end)

    T.test("managers keep business logic atomic", function()
        local bb = Blackboard.new()
        bb.inventory.used_slots = 95
        bb.inventory.max_slots = 100
        bb.inventory.items = { { name = "a" }, { name = "b", keep = true } }
        T.assert_true(InventoryManager.is_full(bb))
        T.assert_eq(#InventoryManager.get_sellable_items(bb), 1)

        T.assert_true(EquipmentManager.should_replace({ attack = 1 }, { attack = 10 }, 5))
        bb.skill.available = { { id = "s1", required_level = 1, priority = 5 } }
        T.assert_eq(SkillManager.get_learnable_skills(bb)[1].id, "s1")

        bb.quest.current_quest_id = "q1"
        bb.quest.active.q1 = { objectives = { { type = "wait" } } }
        T.assert_true(QuestManager.is_objective_complete(QuestManager.get_current_objective(bb), bb))
        T.assert_eq(ObjectiveResolver.resolve({ type = "wait", seconds = 2 }).action, "Wait")
    end)

    T.test("combat resolver returns plain immediate proposal", function()
        local proposal = CombatResolver.resolve({
            mode = "immediate",
            actor_position = { x = 0, y = 0, z = 0 },
            targets = {
                { id = "far", x = 200, y = 0, z = 0 },
                { id = "near", x = 20, y = 0, z = 0 }
            },
            cfg = { default_skill_id = "basic_attack", max_candidate_targets = 8 }
        })
        T.assert_eq(proposal.mode, "immediate")
        T.assert_eq(proposal.action, "cast_skill")
        T.assert_eq(proposal.intent, "cast_skill")
        T.assert_eq(proposal.target_id, "near")
        T.assert_not_nil(proposal.params)
        T.assert_eq(proposal.params.target_id, "near")
    end)

    T.test("normalizers convert raw Maple API snapshots", function()
        local actor = Normalize.actor({
            Hp = "10", MaxHp = "20", Mp = "5", MaxMp = "10", Level = "3",
            X = "1.5", Y = "-2.5", Invincible = "true", MapId = "100"
        })
        T.assert_eq(actor.hp, 10)
        T.assert_eq(actor.position.x, 1.5)
        T.assert_true(actor.invincible)
        T.assert_eq(actor.current_map, "100")

        local world = Normalize.world({
            mobs = { { Name = "Snail", MobId = 1, x = "10", y = "20", Hp = "7", MaxHp = "9" } },
            drops = { { Name = "Coin", ItemId = 2, OwnerCID = "mine", Free = false, x = "11", y = "21" } }
        })
        T.assert_eq(#world.nearby_targets, 1)
        T.assert_eq(world.nearby_targets[1].hp, 7)
        T.assert_eq(#world.nearby_resources, 1)
        T.assert_true(world.nearby_resources[1].can_pick)

        local skill = Normalize.skill(
            { point = "2", used = "1", skills = { { Code = 1001004, CurrentLevel = "3", name = "Slash" } } },
            { { slot = 1, key = "Shift", cat = "Skill", id = "1001004" } }
        )
        T.assert_eq(skill.learned["1001004"].current_level, 3)
        T.assert_eq(skill.quickslots[1].numeric_id, 1001004)
    end)

    T.test("fixture sample normalizes into combat ready snapshots", function()
        local actor = Normalize.actor(ApiSample.player_info)
        local world = Normalize.world(ApiSample.list_nearby)
        local inventory = Normalize.inventory(ApiSample.list_inventory)
        local skill = Normalize.skill(ApiSample.list_skills, ApiSample.list_quickslot)
        T.assert_eq(actor.map_id, "100020000")
        T.assert_eq(world.nearby_targets[1].type_id, 100101)
        T.assert_true(world.nearby_resources[1].can_pick)
        T.assert_eq(inventory.items[1].count, 10)
        T.assert_eq(skill.quickslots[1].slot, 1)
    end)

    T.test("maple api wrapper records diagnostics", function()
        local bb = Blackboard.new({ account_index = 7 })
        local data = { ping = function() return { items = { 1, 2 } } end }
        local api = MapleApi.new({ data_module = data, logger = test_logger(), account_index = 7 })
        local ok = api:call("ping", bb)
        T.assert_true(ok.ok)
        T.assert_eq(ok.data.diagnostic.api_name, "ping")
        T.assert_eq(bb.metrics.api_call_count, 1)
        T.assert_eq(bb.debug.last_api_call.api_name, "ping")
        local missing = api:call("missing", bb)
        T.assert_false(missing.ok)
        T.assert_eq(bb.metrics.api_error_count, 1)
    end)

    T.test("maple environment reads fake data module snapshots", function()
        local env = MapleEnvironment.new({ data_module = fake_data(), logger = test_logger(), account_index = 1, allow_mock_fallback = false })
        local bb = Blackboard.new({ account_index = 1 })
        local actor = env:get_actor_state(bb)
        local world = env:get_world_state(bb)
        local inventory = env:get_inventory_state(bb)
        local skill = env:get_skill_state(bb)
        T.assert_eq(actor.nickname, "hero")
        T.assert_eq(world.nearby_targets[1].name, "Snail")
        T.assert_eq(world.nearby_resources[1].can_pick, true)
        T.assert_eq(inventory.items[1].count, 3)
        T.assert_eq(skill.quickslots[1].slot, 1)
        T.assert_gte(bb.metrics.api_call_count, 5)
    end)

    T.test("maple environment executes atomic action bricks", function()
        local data = fake_data()
        local env = MapleEnvironment.new({ data_module = data, logger = test_logger(), account_index = 1, allow_mock_fallback = false })
        local bb = Blackboard.new({ account_index = 1 })
        local executor = Executor.new(env, Config, test_logger())
        executor:queue_action(bb, "BindClient", { account_index = 1 })
        executor:queue_action(bb, "SetWalkDirection", { direction = 1 })
        executor:queue_action(bb, "StopMove", {})
        executor:queue_action(bb, "PickAllDrops", {})
        executor:flush(bb)
        executor:flush(bb)
        executor:flush(bb)
        executor:flush(bb)
        T.assert_eq(data.calls[1].name, "connect")
        T.assert_eq(data.calls[2].name, "walk")
        T.assert_eq(data.calls[3].name, "walk")
        T.assert_eq(data.calls[4].name, "pick_all")
        T.assert_eq(bb.metrics.action_success_count, 4)
    end)

    T.test("maple environment maps combat proposal to quickslot or attack", function()
        local data = fake_data()
        local env = MapleEnvironment.new({ data_module = data, logger = test_logger(), account_index = 1, allow_mock_fallback = false })
        local bb = Blackboard.new({ account_index = 1 })
        bb.skill.quickslots = { { slot = 1, id = "1001004", numeric_id = 1001004 } }
        local executor = Executor.new(env, Config, test_logger())
        executor:queue_action(bb, "ExecuteCombatDecision", {
            proposal = { action = "cast_skill", executable = true, skill_id = "1001004", params = { skill_id = "1001004" } }
        })
        executor:queue_action(bb, "ExecuteCombatDecision", {
            proposal = { action = "cast_skill", executable = true, skill_id = "basic_attack", params = { skill_id = "basic_attack" } }
        })
        executor:flush(bb)
        executor:flush(bb)
        T.assert_eq(data.calls[1].name, "quickslot_use")
        T.assert_eq(data.calls[1].slot, 1)
        T.assert_eq(data.calls[2].name, "do_attack")
    end)

    T.test("readonly probe works with fake data module", function()
        local lines = {}
        local result = Probe.readonly({
            data_module = fake_data(),
            output = function(message) lines[#lines + 1] = message end
        })
        T.assert_true(result.ok)
        T.assert_eq(result.normalized.actor.nickname, "hero")
        T.assert_eq(result.normalized.world.nearby_targets[1].name, "Snail")
        T.assert_gte(#lines, 5)
    end)

    T.test("action probe executes expected action bricks with fake data module", function()
        local data = fake_data()
        local result = Probe.actions({
            data_module = data,
            quickslot_slot = 1,
            move_ms = 0,
            output = function() end
        })
        T.assert_true(result.ok)
        T.assert_eq(data.calls[1].name, "connect")
        T.assert_eq(data.calls[2].name, "do_attack")
        T.assert_eq(data.calls[3].name, "quickslot_use")
        T.assert_eq(data.calls[4].name, "walk")
        T.assert_eq(data.calls[5].name, "walk")
        T.assert_eq(data.calls[6].name, "walk")
        T.assert_eq(data.calls[7].name, "walk")
        T.assert_eq(data.calls[8].name, "pick_all")
    end)

    T.test("combat resolver trims predictive candidates", function()
        local proposal = CombatResolver.resolve({
            mode = "predictive",
            actor_position = { x = 0, y = 0, z = 0 },
            targets = {
                { id = "near", x = 40, y = 0, z = 0 },
                { id = "far1", x = 400, y = 0, z = 0 },
                { id = "far2", x = 500, y = 0, z = 0 }
            },
            cfg = {
                default_skill_id = "basic_attack",
                default_skill_range_x = 120,
                default_skill_range_y = 50,
                prediction_horizon_seconds = 1,
                prediction_step_seconds = 0.25,
                default_skill_windup_seconds = 0.5,
                max_candidate_targets = 1
            }
        })
        T.assert_eq(proposal.mode, "predictive")
        T.assert_eq(proposal.candidate_count, 1)
        T.assert_eq(proposal.action, "cast_skill")
    end)

    T.test("combat resolver returns fallback proposal on budget overrun", function()
        local proposal = CombatResolver.resolve({
            mode = "predictive",
            actor_position = { x = 0, y = 0, z = 0 },
            targets = { { id = "m1", x = 40, y = 0, z = 0 } },
            cfg = { max_candidate_targets = 8 },
            budget_ms = 0.0001,
            started_at = (os.clock and os.clock() or 0) - 1
        })
        T.assert_true(proposal.fallback_requested)
        T.assert_eq(proposal.fallback_reason, "budget_exceeded")
    end)

    T.test("combat manager supports immediate tick logic", function()
        local bb = Blackboard.new({ account = { enabled = true, combat_logic_mode = "immediate" } })
        bb.actor.position = { x = 0, y = 0, z = 0 }
        bb.world.nearby_targets = {
            { id = "far", x = 200, y = 0, z = 0 },
            { id = "near", x = 20, y = 0, z = 0 }
        }
        local decision = CombatManager.decide(bb)
        T.assert_eq(decision.mode, "immediate")
        T.assert_eq(decision.action, "cast_skill")
        T.assert_eq(decision.target_id, "near")
    end)

    T.test("combat manager supports predictive tick logic", function()
        local bb = Blackboard.new({ account = { enabled = true, combat_logic_mode = "predictive" } })
        bb.actor.position = { x = 0, y = 0, z = 0 }
        bb.world.nearby_targets = {
            { id = "moving_in", x = 200, y = 0, z = 0, vx = -100, vy = 0 },
            { id = "already_in", x = 40, y = 0, z = 0, vx = 0, vy = 0 }
        }
        local decision = CombatManager.decide(bb)
        T.assert_eq(decision.mode, "predictive")
        T.assert_eq(decision.action, "cast_skill")
        T.assert_gte(decision.score, 1)
        T.assert_not_nil(decision.hit_time)
    end)

    T.test("combat manager smart switch prefers predictive logic", function()
        local bb = Blackboard.new({
            account = {
                enabled = true,
                smart_combat_enabled = true,
                combat_logic_mode = "immediate"
            }
        })
        bb.actor.position = { x = 0, y = 0, z = 0 }
        bb.world.nearby_targets = {
            { id = "m1", x = 40, y = 0, z = 0, vx = 0, vy = 0 }
        }
        local decision = CombatManager.decide(bb)
        T.assert_eq(decision.mode, "predictive")
    end)

    T.test("combat manager disabled smart switch forces immediate logic", function()
        local bb = Blackboard.new({
            account = {
                enabled = true,
                smart_combat_enabled = false,
                combat_logic_mode = "predictive"
            }
        })
        bb.actor.position = { x = 0, y = 0, z = 0 }
        bb.world.nearby_targets = {
            { id = "m1", x = 40, y = 0, z = 0, vx = 0, vy = 0 }
        }
        local decision = CombatManager.decide(bb)
        T.assert_eq(decision.mode, "immediate")
    end)

    T.test("combat manager degrades predictive budget overrun to immediate proposal", function()
        local old_budget = Config.combat.predictive_budget_ms
        Config.combat.predictive_budget_ms = 0.0001
        local bb = Blackboard.new({ account = { enabled = true, combat_logic_mode = "predictive" } })
        bb.actor.position = { x = 0, y = 0, z = 0 }
        bb.world.nearby_targets = {
            { id = "m1", x = 40, y = 0, z = 0, vx = 0, vy = 0 }
        }
        local proposal = CombatManager.decide(bb)
        Config.combat.predictive_budget_ms = old_budget
        T.assert_eq(proposal.mode, "immediate")
        T.assert_true(proposal.degraded)
        T.assert_eq(proposal.fallback_reason, "budget_exceeded")
    end)

    T.test("perception refreshes heavy domains by interval", function()
        local counts = { actor = 0, world = 0, inventory = 0 }
        local env = MockEnvironment.new()
        function env:get_actor_state() counts.actor = counts.actor + 1; return { position = { x = counts.actor, y = 0, z = 0 } } end
        function env:get_world_state() counts.world = counts.world + 1; return { nearby_targets = {} } end
        function env:get_inventory_state() counts.inventory = counts.inventory + 1; return { used_slots = counts.inventory, max_slots = 100, items = {} } end
        local bb = Blackboard.new()
        local perception = Perception.new(env, test_logger(), {
            actor_interval_ticks = 1,
            world_interval_ticks = 1,
            inventory_interval_ticks = 3,
            quest_interval_ticks = 99,
            equipment_interval_ticks = 99,
            skill_interval_ticks = 99
        })
        bb.runtime.tick = 1
        perception:update(bb)
        bb.runtime.tick = 2
        perception:update(bb)
        bb.runtime.tick = 3
        perception:update(bb)
        bb.runtime.tick = 4
        perception:update(bb)
        T.assert_eq(counts.actor, 4)
        T.assert_eq(counts.world, 4)
        T.assert_eq(counts.inventory, 2)
    end)

    T.test("safety opens circuit breaker", function()
        local bb = Blackboard.new()
        bb.task.failure_count = Config.limits.max_failures
        Safety.new(Config, test_logger()):check(bb)
        T.assert_true(bb.safety.circuit_breaker_open)
        T.assert_eq(bb.safety.last_trigger, "too_many_failures")
    end)

    T.test("logger, snapshot, replay record observable state", function()
        local bb = Blackboard.new()
        local logger = test_logger()
        logger:info("goal_changed", { to = "idle" }, bb)
        T.assert_eq(#logger.records, 1)
        local snapshot = Snapshot.new(Config, logger)
        bb.runtime.tick = Config.snapshot.interval_ticks
        local item = snapshot:maybe_save(bb)
        T.assert_not_nil(item)
        T.assert_eq(Replay.from_snapshot(item).tick, item.tick)
    end)

    return T.report("maple_unit")
end

return { run = run }
