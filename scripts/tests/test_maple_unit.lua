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
