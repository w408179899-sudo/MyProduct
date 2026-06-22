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
local CombatRuntime = require("maple.combat.runtime")
local PlatformCombatRuntime = require("maple.combat.platform_runtime")
local PlatformRecorder = require("maple.navigation.platform_recorder")
local PlatformMap = require("maple.navigation.platform_map")
local Perception = require("maple.systems.perception")
local Store = require("maple.account.store")
local MapleApi = require("maple.environment.maple_api")
local MapleEnvironment = require("maple.environment.maple_environment")
local Normalize = require("maple.environment.normalizers")
local Probe = require("maple.probes.api_probe")
local FoundationProbe = require("maple.probes.foundation_probe")
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
        return { slots = { { slot = 1, key = "Shift", cat = "Skill", id = "1001004" } } }
    end
    function data.list_nearby()
        data.calls[#data.calls + 1] = { name = "list_nearby" }
        return {
            mobCount = 1,
            dropCount = 1,
            portalCount = 1,
            npcCount = 1,
            mobs = { { Name = "Snail", MobId = 100101, Level = "5", x = "100.0", y = "200.0", Hp = "50", MaxHp = "50" } },
            drops = { { Id = 1, Name = "Red Potion", ItemId = 2000003, OwnerCID = "hero", Source = "mine", Free = false, x = "105.0", y = "201.0" } },
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
        T.assert_not_nil(executor:queue_action(bb, "PressKey", { key_code = 0x10 }))
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

    T.test("combat runtime presses skill key when target is in range", function()
        local proposal = CombatRuntime.decide({
            actor = { position = { x = 0, y = 0, z = 0 } },
            world = {
                nearby_targets = { { id = "m1", name = "mob", x = 20, y = 0, z = 0 } },
                nearby_resources = {}
            },
            cfg = {
                baseline_attack_range_x = 95,
                baseline_attack_range_y = 45,
                skill_key = "Shift",
                skill_key_code = 0x10,
                skill_input_mode = "foreground"
            },
            state = {}
        })
        T.assert_eq(proposal.action, "PressKey")
        T.assert_eq(proposal.reason, "target_in_attack_box")
        T.assert_eq(proposal.params.key_code, 0x10)
        T.assert_eq(proposal.target.id, "m1")
    end)

    T.test("combat runtime moves toward nearest target when out of range", function()
        local proposal = CombatRuntime.decide({
            actor = { position = { x = 0, y = 0, z = 0 } },
            world = {
                nearby_targets = {
                    { id = "right", name = "mob", x = 200, y = 0, z = 0 },
                    { id = "left", name = "mob", x = -150, y = 0, z = 0 }
                },
                nearby_resources = {}
            },
            cfg = {
                baseline_attack_range_x = 95,
                baseline_attack_range_y = 45,
                baseline_pursuit_y_tolerance = 70
            },
            state = {}
        })
        T.assert_eq(proposal.action, "SetWalkDirection")
        T.assert_eq(proposal.params.direction, -1)
        T.assert_eq(proposal.target.id, "left")
    end)

    T.test("combat runtime attacks before pickup when target still exists", function()
        local proposal = CombatRuntime.decide({
            actor = { position = { x = 0, y = 0, z = 0 } },
            world = {
                nearby_targets = { { id = "m1", name = "mob", x = 20, y = 0, z = 0 } },
                nearby_resources = { { id = "d1", can_pick = true, x = 1, y = 0 } }
            },
            cfg = { baseline_pickup_enabled = true },
            state = { just_attacked = false }
        })
        T.assert_eq(proposal.action, "PressKey")
        T.assert_eq(proposal.reason, "target_in_attack_box")
        T.assert_eq(proposal.target.id, "m1")
    end)

    T.test("combat runtime picks visible drops when no target exists", function()
        local proposal = CombatRuntime.decide({
            actor = { position = { x = 0, y = 0, z = 0 } },
            world = {
                nearby_targets = {},
                nearby_resources = { { id = "d1", can_pick = true, x = 1, y = 0 } }
            },
            cfg = { baseline_pickup_enabled = true },
            state = { just_attacked = false }
        })
        T.assert_eq(proposal.action, "PickAllDrops")
        T.assert_eq(proposal.reason, "pickable_drop_visible_no_target")
    end)

    T.test("combat runtime waits when no target exists", function()
        local proposal = CombatRuntime.decide({
            actor = { position = { x = 0, y = 0, z = 0 } },
            world = { nearby_targets = {}, nearby_resources = {} },
            cfg = { baseline_tick_ms = 250 },
            state = {}
        })
        T.assert_eq(proposal.action, "Wait")
        T.assert_eq(proposal.reason, "no_target")
    end)

    T.test("platform recorder captures points and manual bounds", function()
        local current_ms = 0
        local pos = { x = 0, y = -20, z = 0 }
        local lines = {}
        local recorder = PlatformRecorder.new({
            platform_id = "test_platform",
            sample_ms = 100,
            min_distance = 0,
            now_ms = function() return current_ms end,
            output = function(message) lines[#lines + 1] = message end,
            read_actor = function()
                return {
                    map_id = "101010000",
                    map_name = "Test",
                    position = { x = pos.x, y = pos.y, z = pos.z }
                }
            end
        })

        recorder:start()
        pos.x = -10
        current_ms = 100
        recorder:sample_if_due()
        recorder:mark_left()
        pos.x = 10
        current_ms = 200
        recorder:sample_if_due()
        recorder:mark_right()

        local snapshot = recorder:snapshot()
        T.assert_eq(snapshot.map_id, "101010000")
        T.assert_eq(snapshot.platform.id, "test_platform")
        T.assert_eq(snapshot.platform.left_x, -10)
        T.assert_eq(snapshot.platform.right_x, 10)
        T.assert_gte(#snapshot.platform.points, 5)
        T.assert_gte(#lines, 5)
    end)

    T.test("platform recorder hotkeys control recording state", function()
        local current_ms = 0
        local pos = { x = 1, y = -2, z = 0 }
        local pressed = {}
        local recorder = PlatformRecorder.new({
            sample_ms = 100,
            now_ms = function() return current_ms end,
            output = function() end,
            read_actor = function()
                return {
                    map_id = "map",
                    position = { x = pos.x, y = pos.y, z = pos.z }
                }
            end
        })
        local function poll()
            return recorder:poll_hotkeys(function(key_code) return pressed[key_code] == true end)
        end
        local function tap(key_code)
            pressed[key_code] = true
            poll()
            pressed[key_code] = false
            poll()
        end

        tap(0x78) -- F9 start
        T.assert_true(recorder.recording)
        pos.x = 2
        current_ms = 100
        poll()
        T.assert_gte(#recorder.points, 2)
        tap(0x79) -- F10 pause
        T.assert_false(recorder.recording)
        tap(0x7B) -- F12 clear
        T.assert_eq(#recorder.points, 0)
    end)

    T.test("platform map interpolates uneven platform y", function()
        local map = PlatformMap.normalize({
            map_id = "map",
            platforms = {
                {
                    id = "p1",
                    left_x = -10,
                    right_x = 10,
                    points = {
                        { x = -10, y = -2 },
                        { x = 0, y = -1 },
                        { x = 10, y = -3 }
                    }
                }
            }
        })
        local platform = map.platforms[1]
        T.assert_eq(PlatformMap.y_at(platform, -10), -2)
        T.assert_eq(PlatformMap.y_at(platform, 0), -1)
        T.assert_eq(PlatformMap.y_at(platform, 10), -3)
        T.assert_eq(PlatformMap.y_at(platform, 5), -2)
    end)

    T.test("platform map locates points by x range and y tolerance", function()
        local map = PlatformMap.normalize({
            platforms = {
                {
                    id = "p1",
                    left_x = -5,
                    right_x = 5,
                    points = {
                        { x = -5, y = -10 },
                        { x = 5, y = -10 }
                    }
                }
            }
        })
        local loc = PlatformMap.locate_point(map, { x = 0, y = -9.8 }, { y_tolerance = 0.5 })
        T.assert_not_nil(loc)
        T.assert_eq(loc.platform_id, "p1")
        T.assert_lt(math.abs(loc.y_delta - 0.2), 0.0001)

        local missing = PlatformMap.locate_point(map, { x = 0, y = -8 }, { y_tolerance = 0.5 })
        T.assert_nil(missing)
    end)

    T.test("platform combat faces and presses key when same-platform target is in skill box", function()
        local map = PlatformMap.normalize({
            platforms = {
                {
                    id = "p1",
                    left_x = -10,
                    right_x = 10,
                    safe_margin = 0.5,
                    points = {
                        { x = -10, y = -10 },
                        { x = 10, y = -10 }
                    }
                }
            }
        })
        local proposal = PlatformCombatRuntime.decide({
            map = map,
            actor = { position = { x = 0, y = -10, z = 0 } },
            world = {
                nearby_targets = { { id = "m1", x = 1.2, y = -10, z = 0, vx = 0, vy = 0 } },
                nearby_resources = {}
            },
            cfg = {
                skill_range_x = 2.0,
                skill_range_y = 0.3,
                cast_delay_seconds = 0.7,
                skill_key = "Shift",
                skill_key_code = 0x10
            },
            state = { last_direction = 1 }
        })
        T.assert_eq(proposal.action, "FaceAndPressKey")
        T.assert_eq(proposal.reason, "platform_target_in_skill_box")
        T.assert_eq(proposal.target.id, "m1")
        T.assert_eq(proposal.metrics.direction, 1)
        T.assert_eq(proposal.params.key_code, 0x10)
    end)

    T.test("platform combat moves toward computed stand point when target is out of range", function()
        local map = PlatformMap.normalize({
            platforms = {
                {
                    id = "p1",
                    left_x = -10,
                    right_x = 10,
                    safe_margin = 0.5,
                    points = {
                        { x = -10, y = -10 },
                        { x = 10, y = -10 }
                    }
                }
            }
        })
        local proposal = PlatformCombatRuntime.decide({
            map = map,
            actor = { position = { x = 0, y = -10, z = 0 } },
            world = {
                nearby_targets = { { id = "m1", x = 5, y = -10, z = 0, vx = 0, vy = 0 } },
                nearby_resources = {}
            },
            cfg = {
                skill_range_x = 2.0,
                skill_range_y = 0.3,
                preferred_attack_distance = 1.4,
                arrival_tolerance_x = 0.18
            },
            state = { last_direction = 1 }
        })
        T.assert_eq(proposal.action, "SetWalkDirection")
        T.assert_eq(proposal.reason, "platform_move_to_attack_stand")
        T.assert_eq(proposal.params.direction, 1)
        T.assert_lt(math.abs(proposal.metrics.stand_x - 3.6), 0.0001)
    end)

    T.test("platform combat waits for airborne same-platform target outside skill y range", function()
        local map = PlatformMap.normalize({
            platforms = {
                {
                    id = "p1",
                    left_x = -10,
                    right_x = 10,
                    points = {
                        { x = -10, y = -10 },
                        { x = 10, y = -10 }
                    }
                }
            }
        })
        local proposal = PlatformCombatRuntime.decide({
            map = map,
            actor = { position = { x = 0, y = -10, z = 0 } },
            world = {
                nearby_targets = { { id = "m1", x = 1, y = -9.3, z = 0, vx = 0, vy = 0 } },
                nearby_resources = {}
            },
            cfg = {
                skill_range_x = 2.0,
                skill_range_y = 0.3,
                platform_y_tolerance = 1.0,
                grounded_y_tolerance = 0.2
            },
            state = { last_direction = 1 }
        })
        T.assert_eq(proposal.action, "Wait")
        T.assert_eq(proposal.reason, "platform_target_airborne_wait_land")
        T.assert_eq(proposal.target.id, "m1")
    end)

    T.test("platform combat pickup mode ignores remaining target and picks same-platform drops", function()
        local map = PlatformMap.normalize({
            platforms = {
                {
                    id = "p1",
                    left_x = -10,
                    right_x = 10,
                    points = {
                        { x = -10, y = -10 },
                        { x = 10, y = -10 }
                    }
                }
            }
        })
        local proposal = PlatformCombatRuntime.decide({
            mode = "pickup_only",
            map = map,
            actor = { position = { x = 0, y = -10, z = 0 } },
            world = {
                nearby_targets = { { id = "m1", x = 1, y = -10, z = 0, vx = 0, vy = 0 } },
                nearby_resources = { { id = "d1", name = "drop", x = 0.2, y = -10, z = 0, can_pick = true } }
            },
            cfg = {
                pickup_range_x = 0.45,
                pickup_range_y = 0.5,
                pickup_platform_y_tolerance = 0.8
            },
            state = { last_direction = 1 }
        })
        T.assert_eq(proposal.action, "PickAllDrops")
        T.assert_eq(proposal.reason, "platform_drop_in_pick_range")
        T.assert_eq(proposal.drop.id, "d1")
        T.assert_eq(#proposal.candidates, 1)
        T.assert_eq(#proposal.drop_candidates, 1)
    end)

    T.test("platform combat pickup mode includes same-platform drops even when can_pick is false", function()
        local map = PlatformMap.normalize({
            platforms = {
                {
                    id = "p1",
                    left_x = -10,
                    right_x = 10,
                    points = {
                        { x = -10, y = -10 },
                        { x = 10, y = -10 }
                    }
                }
            }
        })
        local proposal = PlatformCombatRuntime.decide({
            mode = "pickup_only",
            map = map,
            actor = { position = { x = 0, y = -10, z = 0 } },
            world = {
                nearby_targets = {},
                nearby_resources = { { id = "d1", name = "drop", x = 0.2, y = -10, z = 0, can_pick = false } }
            },
            cfg = {
                pickup_include_all_drops = true,
                pickup_range_x = 0.45,
                pickup_range_y = 0.5,
                pickup_platform_y_tolerance = 0.8
            },
            state = { last_direction = 1 }
        })
        T.assert_eq(proposal.action, "PickAllDrops")
        T.assert_eq(proposal.drop.id, "d1")
        T.assert_eq(#proposal.drop_candidates, 1)
    end)

    T.test("platform combat pickup mode does not require raw drop y to match actor y", function()
        local map = PlatformMap.normalize({
            platforms = {
                {
                    id = "p1",
                    left_x = -10,
                    right_x = 10,
                    points = {
                        { x = -10, y = -10 },
                        { x = 10, y = -10 }
                    }
                }
            }
        })
        local proposal = PlatformCombatRuntime.decide({
            mode = "pickup_only",
            map = map,
            actor = { position = { x = 0, y = -10, z = 0 } },
            world = {
                nearby_targets = {},
                nearby_resources = { { id = "d1", name = "drop", x = 0.2, y = -8.8, z = 0, can_pick = true } }
            },
            cfg = {
                pickup_ignore_raw_y = true,
                pickup_range_x = 0.65,
                pickup_range_y = 0.5,
                pickup_platform_y_tolerance = 1.5
            },
            state = { last_direction = 1 }
        })
        T.assert_eq(proposal.action, "PickAllDrops")
        T.assert_eq(proposal.reason, "platform_drop_in_pick_range")
        T.assert_eq(proposal.drop.id, "d1")
        T.assert_eq(#proposal.drop_candidates, 1)
    end)

    T.test("platform combat pickup mode moves toward far same-platform drop", function()
        local map = PlatformMap.normalize({
            platforms = {
                {
                    id = "p1",
                    left_x = -10,
                    right_x = 10,
                    points = {
                        { x = -10, y = -10 },
                        { x = 10, y = -10 }
                    }
                }
            }
        })
        local proposal = PlatformCombatRuntime.decide({
            mode = "pickup_only",
            map = map,
            actor = { position = { x = 0, y = -10, z = 0 } },
            world = {
                nearby_targets = {},
                nearby_resources = { { id = "d1", name = "drop", x = 3.0, y = -9.2, z = 0, can_pick = true } }
            },
            cfg = {
                pickup_ignore_raw_y = true,
                pickup_range_x = 0.65,
                pickup_platform_y_tolerance = 1.5
            },
            state = { last_direction = 1 }
        })
        T.assert_eq(proposal.action, "SetWalkDirection")
        T.assert_eq(proposal.reason, "platform_move_to_drop")
        T.assert_eq(proposal.params.direction, 1)
        T.assert_eq(proposal.params.target_x, 3.0)
        T.assert_eq(proposal.drop.id, "d1")
    end)

    T.test("platform combat pickup mode skips ignored near drop and moves to next drop", function()
        local map = PlatformMap.normalize({
            platforms = {
                {
                    id = "p1",
                    left_x = -10,
                    right_x = 10,
                    points = {
                        { x = -10, y = -10 },
                        { x = 10, y = -10 }
                    }
                }
            }
        })
        local proposal = PlatformCombatRuntime.decide({
            mode = "pickup_only",
            map = map,
            actor = { position = { x = 0, y = -10, z = 0 } },
            world = {
                nearby_targets = {},
                nearby_resources = {
                    { id = "stuck", name = "near", x = 0.2, y = -10, z = 0, can_pick = true },
                    { id = "far", name = "far", x = 3.0, y = -10, z = 0, can_pick = true }
                }
            },
            cfg = {
                pickup_ignore_raw_y = true,
                pickup_range_x = 0.65,
                pickup_platform_y_tolerance = 1.5
            },
            state = {
                last_direction = 1,
                ignored_drops = { stuck = { until_tick = 10, failures = 1 } }
            }
        })
        T.assert_eq(proposal.action, "SetWalkDirection")
        T.assert_eq(proposal.reason, "platform_move_to_drop")
        T.assert_eq(proposal.drop.id, "far")
        T.assert_eq(proposal.params.direction, 1)
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
            mobs = { { Id = 42, Name = "Snail", MobId = 1, x = "10", y = "20", Hp = "7", MaxHp = "9" } },
            drops = { { Id = 7, Name = "Coin", ItemId = 2, OwnerCID = "hero", Source = "mine", Free = false, x = "11", y = "21" } }
        })
        T.assert_eq(#world.nearby_targets, 1)
        T.assert_eq(world.nearby_targets[1].id, "42")
        T.assert_eq(world.nearby_targets[1].instance_id, 42)
        T.assert_eq(world.nearby_targets[1].hp, 7)
        T.assert_eq(#world.nearby_resources, 1)
        T.assert_eq(world.nearby_resources[1].id, 7)
        T.assert_eq(world.nearby_resources[1].drop_source, "mine")
        T.assert_true(world.nearby_resources[1].can_pick)

        local skill = Normalize.skill(
            { point = "2", used = "1", skills = { { Code = 1001004, CurrentLevel = "3", name = "Slash" } } },
            { { slot = 1, key = "Shift", cat = "Skill", id = "1001004" } }
        )
        T.assert_eq(skill.learned["1001004"].current_level, 3)
        T.assert_eq(skill.quickslots[1].numeric_id, 1001004)
    end)

    T.test("normalizers ignore empty drop shell rows but keep meso rows", function()
        local world = Normalize.world({
            dropCount = 4,
            drops = {
                { Id = 1, ItemId = 4000003, Name = "Branch", Source = "mine", Free = false, x = "1", y = "2" },
                { Id = 2, ItemId = 9000000, Name = "", Source = "mine", Free = false, x = "1.2", y = "2" },
                { Id = 3, ItemId = "", Name = "", Source = "other", Free = true, x = "1.4", y = "2" },
                { Id = 4, Source = "other", Free = true, x = "1.6", y = "2" }
            }
        })
        T.assert_eq(world.counts.drop, 4)
        T.assert_eq(#world.nearby_resources, 2)
        T.assert_eq(world.nearby_resources[1].item_id, 4000003)
        T.assert_eq(world.nearby_resources[2].item_id, 9000000)
        T.assert_true(world.nearby_resources[2].can_pick)
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

    T.test("maple environment presses foreground key through PressKey action", function()
        local data = fake_data()
        local calls = {}
        local env = MapleEnvironment.new({
            data_module = data,
            logger = test_logger(),
            account_index = 1,
            allow_mock_fallback = false,
            key_api = {
                click = function(key_code)
                    calls[#calls + 1] = { name = "click", key_code = key_code }
                    return true
                end
            },
            wnd_api = {
                set_foreground = function(hwnd)
                    calls[#calls + 1] = { name = "set_foreground", hwnd = hwnd }
                    return true
                end
            },
            proc_api = {
                window = function(pid)
                    calls[#calls + 1] = { name = "window", pid = pid }
                    return 0x1234
                end
            }
        })
        local bb = Blackboard.new({ account_index = 1 })
        env.pid = 1234
        local result = env:perform_action({ name = "PressKey", params = { key_code = 0x10, key_name = "Shift" } }, bb)
        T.assert_true(result.ok)
        T.assert_eq(result.data.method, "keybd.click")
        T.assert_eq(result.data.hwnd, 0x1234)
        T.assert_eq(calls[1].name, "window")
        T.assert_eq(calls[2].name, "set_foreground")
        T.assert_eq(calls[3].name, "click")
        T.assert_eq(calls[3].key_code, 0x10)
    end)

    T.test("maple environment presses background key through PressKey action", function()
        local data = fake_data()
        local calls = {}
        local env = MapleEnvironment.new({
            data_module = data,
            logger = test_logger(),
            account_index = 1,
            allow_mock_fallback = false,
            key_api = {
                post_click = function(hwnd, key_code)
                    calls[#calls + 1] = { name = "post_click", hwnd = hwnd, key_code = key_code }
                    return true
                end
            },
            proc_api = {
                window = function(pid)
                    calls[#calls + 1] = { name = "window", pid = pid }
                    return 0x2345
                end
            }
        })
        local bb = Blackboard.new({ account_index = 1 })
        env.pid = 2345
        local result = env:perform_action({ name = "PressKey", params = { key_code = 0x10, input_mode = "background" } }, bb)
        T.assert_true(result.ok)
        T.assert_eq(result.data.method, "keybd.post_click")
        T.assert_eq(result.data.hwnd, 0x2345)
        T.assert_eq(calls[1].name, "window")
        T.assert_eq(calls[2].name, "post_click")
        T.assert_eq(calls[2].key_code, 0x10)
    end)

    T.test("store resolves skill release from profile and account overrides", function()
        local root = {
            profiles = {
                default = { skill_key = "Shift", skill_key_code = 0x10 },
                mage = { skill_key = "Home", skill_key_code = 0x24 }
            },
            items = {}
        }
        local account = Store.new_account({ account = "u1", profile = "mage", skill_key = "Insert", skill_key_code = "0x2D" })
        local release = Store.resolve_skill_release(root, account)
        T.assert_eq(release.skill_use_method, "press_key")
        T.assert_eq(release.skill_key, "Insert")
        T.assert_eq(release.skill_key_code, 0x2D)
        T.assert_eq(release.skill_input_mode, "foreground")
        T.assert_false(release.quickslot_use_trusted)
    end)

    T.test("maple environment maps combat proposal to configured skill key by default", function()
        local data = fake_data()
        local calls = {}
        local env = MapleEnvironment.new({
            data_module = data,
            logger = test_logger(),
            account_index = 1,
            allow_mock_fallback = false,
            key_api = {
                click = function(key_code)
                    calls[#calls + 1] = { name = "click", key_code = key_code }
                    return true
                end
            }
        })
        local bb = Blackboard.new({ account_index = 1, account = { enabled = true } })
        local executor = Executor.new(env, Config, test_logger())
        executor:queue_action(bb, "ExecuteCombatDecision", {
            proposal = { action = "cast_skill", executable = true, skill_id = "1001004", params = { skill_id = "1001004" } }
        })
        executor:flush(bb)
        T.assert_eq(calls[1].name, "click")
        T.assert_eq(calls[1].key_code, 0x10)
        T.assert_eq(#data.calls, 0)
    end)

    T.test("maple environment honors account skill key override", function()
        local data = fake_data()
        local calls = {}
        local env = MapleEnvironment.new({
            data_module = data,
            logger = test_logger(),
            account_index = 1,
            allow_mock_fallback = false,
            key_api = {
                click = function(key_code)
                    calls[#calls + 1] = { name = "click", key_code = key_code }
                    return true
                end
            }
        })
        local bb = Blackboard.new({
            account_index = 1,
            account = {
                enabled = true,
                skill_key = "Insert",
                skill_key_code = 0x2D
            }
        })
        local executor = Executor.new(env, Config, test_logger())
        executor:queue_action(bb, "ExecuteCombatDecision", {
            proposal = { action = "cast_skill", executable = true, skill_id = "1001004", params = { skill_id = "1001004" } }
        })
        executor:flush(bb)
        T.assert_eq(calls[1].key_code, 0x2D)
    end)

    T.test("maple environment only uses quickslot when explicitly trusted", function()
        local data = fake_data()
        local env = MapleEnvironment.new({ data_module = data, logger = test_logger(), account_index = 1, allow_mock_fallback = false })
        local bb = Blackboard.new({
            account_index = 1,
            account = {
                skill_use_method = "quickslot",
                quickslot_use_trusted = true
            }
        })
        bb.skill.quickslots = { { slot = 1, id = "1001004", numeric_id = 1001004 } }
        local executor = Executor.new(env, Config, test_logger())
        executor:queue_action(bb, "ExecuteCombatDecision", {
            proposal = { action = "cast_skill", executable = true, skill_id = "1001004", params = { skill_id = "1001004" } }
        })
        executor:flush(bb)
        T.assert_eq(data.calls[1].name, "quickslot_use")
        T.assert_eq(data.calls[1].slot, 1)
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

    T.test("raw snapshot probe prints fake API field samples", function()
        local lines = {}
        local result = Probe.snapshot({
            data_module = fake_data(),
            sample_count = 1,
            output = function(message) lines[#lines + 1] = message end
        })
        T.assert_true(result.ok)
        T.assert_eq(result.raw.list_nearby.mobs[1].Name, "Snail")

        local found_mob_sample = false
        local found_quickslot_sample = false
        for _, line in ipairs(lines) do
            if line:find("raw%.list_nearby%.mobs%[1%]") then found_mob_sample = true end
            if line:find("raw%.list_quickslot%.slots%[1%]") then found_quickslot_sample = true end
        end
        T.assert_true(found_mob_sample)
        T.assert_true(found_quickslot_sample)
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

    T.test("quickslot probe isolates quickslot action with fake data module", function()
        local data = fake_data()
        local lines = {}
        local result = Probe.quickslot({
            data_module = data,
            quickslot_slot = 1,
            repeat_count = 2,
            interval_ms = 0,
            output = function(message) lines[#lines + 1] = message end
        })
        T.assert_true(result.ok)
        T.assert_eq(data.calls[1].name, "connect")
        T.assert_eq(data.calls[2].name, "list_quickslot")
        T.assert_eq(data.calls[3].name, "list_skills")
        T.assert_eq(data.calls[4].name, "quickslot_use")
        T.assert_eq(data.calls[4].slot, 1)
        T.assert_eq(data.calls[5].name, "quickslot_use")
        T.assert_eq(result.selected_slot.key, "Shift")
        T.assert_eq(#data.calls, 5)

        local found_selected = false
        for _, line in ipairs(lines) do
            if line:find("selected quickslot") and line:find("Slash") then found_selected = true end
        end
        T.assert_true(found_selected)
    end)

    T.test("quickslot effect probe prints before after deltas with fake data module", function()
        local data = fake_data()
        local lines = {}
        local result = Probe.quickslot_effect({
            data_module = data,
            quickslot_slot = 1,
            wait_ms = 0,
            output = function(message) lines[#lines + 1] = message end
        })
        T.assert_true(result.ok)
        T.assert_eq(result.selected_slot.key, "Shift")
        T.assert_eq(data.calls[1].name, "connect")
        T.assert_eq(data.calls[2].name, "list_quickslot")
        T.assert_eq(data.calls[3].name, "list_skills")
        T.assert_eq(data.calls[4].name, "player_info")
        T.assert_eq(data.calls[5].name, "list_nearby")
        T.assert_eq(data.calls[6].name, "quickslot_use")
        T.assert_eq(data.calls[7].name, "player_info")
        T.assert_eq(data.calls[8].name, "list_nearby")

        local found_delta = false
        for _, line in ipairs(lines) do
            if line:find("effect delta") then found_delta = true end
        end
        T.assert_true(found_delta)
    end)

    T.test("key effect probe supports foreground keyboard path with fake APIs", function()
        local data = fake_data()
        local calls = {}
        local key_api = {
            click = function(key_code)
                calls[#calls + 1] = { name = "click", key_code = key_code }
                return true
            end
        }
        local wnd_api = {
            set_foreground = function(hwnd)
                calls[#calls + 1] = { name = "set_foreground", hwnd = hwnd }
                return true
            end
        }
        local proc_api = {
            window = function(pid)
                calls[#calls + 1] = { name = "window", pid = pid }
                return 0x1234
            end
        }
        local result = Probe.key_effect({
            data_module = data,
            key_api = key_api,
            wnd_api = wnd_api,
            proc_api = proc_api,
            key_code = 0x10,
            wait_ms = 0,
            output = function() end
        })
        T.assert_true(result.ok)
        T.assert_eq(result.hwnd, 0x1234)
        T.assert_eq(calls[1].name, "window")
        T.assert_eq(calls[2].name, "set_foreground")
        T.assert_eq(calls[3].name, "click")
        T.assert_eq(calls[3].key_code, 0x10)
    end)

    T.test("key effect probe supports background keyboard path with fake APIs", function()
        local data = fake_data()
        local calls = {}
        local key_api = {
            post_click = function(hwnd, key_code)
                calls[#calls + 1] = { name = "post_click", hwnd = hwnd, key_code = key_code }
                return true
            end
        }
        local proc_api = {
            window = function(pid)
                calls[#calls + 1] = { name = "window", pid = pid }
                return 0x2345
            end
        }
        local result = Probe.key_effect({
            data_module = data,
            key_api = key_api,
            proc_api = proc_api,
            input_mode = "background",
            key_code = 0x10,
            wait_ms = 0,
            output = function() end
        })
        T.assert_true(result.ok)
        T.assert_eq(result.hwnd, 0x2345)
        T.assert_eq(calls[1].name, "window")
        T.assert_eq(calls[2].name, "post_click")
        T.assert_eq(calls[2].key_code, 0x10)
    end)

    T.test("pickup effect probe logs raw pick result and drop delta", function()
        local data = fake_data()
        local picked = false
        function data.list_nearby()
            data.calls[#data.calls + 1] = { name = "list_nearby" }
            return {
                mobCount = 0,
                dropCount = picked and 0 or 1,
                portalCount = 0,
                npcCount = 0,
                mobs = {},
                drops = picked and {} or {
                    { Id = 9, Name = "Coin", ItemId = 1, Source = "mine", x = "320.5", y = "-180.0" }
                },
                portals = {},
                npcs = {}
            }
        end
        function data.pick_all()
            data.calls[#data.calls + 1] = { name = "pick_all" }
            picked = true
            return "ok: picked=1 skipped=0"
        end

        local key_calls = {}
        local lines = {}
        local result = Probe.pickup_effect({
            data_module = data,
            key_api = {
                click = function(key_code)
                    key_calls[#key_calls + 1] = key_code
                    return true
                end
            },
            wait_ms = 0,
            output = function(message) lines[#lines + 1] = message end
        })
        T.assert_true(result.ok)
        T.assert_true(picked)
        T.assert_eq(key_calls[1], 0x5A)

        local found_raw = false
        local found_delta = false
        local found_key_result = false
        for _, line in ipairs(lines) do
            if line:find("pickup_effect pick_result", 1, true) and line:find("picked=1", 1, true) then found_raw = true end
            if line:find("pickup_effect key_result", 1, true) and line:find("keybd.click", 1, true) then found_key_result = true end
            if line:find("effect delta", 1, true) and line:find("drops=-1", 1, true) then found_delta = true end
        end
        T.assert_true(found_raw)
        T.assert_true(found_key_result)
        T.assert_true(found_delta)
    end)

    T.test("pickup verify probe compares drop keys and inventory deltas", function()
        local data = fake_data()
        local picked = false
        function data.list_nearby()
            data.calls[#data.calls + 1] = { name = "list_nearby" }
            return {
                mobCount = 0,
                dropCount = picked and 0 or 1,
                portalCount = 0,
                npcCount = 0,
                mobs = {},
                drops = picked and {} or {
                    { Id = 9, Name = "Coin", ItemId = 1, Source = "mine", x = "320.5", y = "-180.0" }
                },
                portals = {},
                npcs = {}
            }
        end
        function data.list_inventory()
            data.calls[#data.calls + 1] = { name = "list_inventory" }
            return {
                meso = picked and "109" or "99",
                items = { { Code = 2000002, Count = "3", name = "Potion" } }
            }
        end
        function data.pick_all()
            data.calls[#data.calls + 1] = { name = "pick_all" }
            picked = true
            return "ok: picked=1 skipped=0"
        end

        local lines = {}
        local result = Probe.pickup_verify({
            data_module = data,
            repeat_count = 1,
            pickup_key_enabled = false,
            verify_waits = { 0 },
            output = function(message) lines[#lines + 1] = message end
        })
        T.assert_true(result.ok)
        T.assert_eq(result.summary.verdict, "drop_keys_cleared")
        T.assert_eq(result.summary.claimed_pick_count, 1)
        T.assert_eq(result.summary.final_drop_count, 0)
        T.assert_eq(result.summary.final_disappeared_count, 1)
        T.assert_eq(result.summary.meso_delta, 10)

        local found_compare = false
        local found_conclusion = false
        for _, line in ipairs(lines) do
            if line:find("pickup_verify compare=after_0ms", 1, true) and line:find("disappeared=1", 1, true) then
                found_compare = true
            end
            if line:find("pickup_verify conclusion verdict=drop_keys_cleared", 1, true) then
                found_conclusion = true
            end
        end
        T.assert_true(found_compare)
        T.assert_true(found_conclusion)
    end)

    T.test("pickup verify probe keeps same drop identity across coordinate jitter", function()
        local data = fake_data()
        local nearby_calls = 0
        function data.list_nearby()
            data.calls[#data.calls + 1] = { name = "list_nearby" }
            nearby_calls = nearby_calls + 1
            local y = nearby_calls > 1 and "-10.900" or "-10.800"
            return {
                mobCount = 0,
                dropCount = 1,
                portalCount = 0,
                npcCount = 0,
                mobs = {},
                drops = {
                    { Id = 12, Name = "Coin", ItemId = 9000000, Source = "mine", x = "-3.5", y = y }
                },
                portals = {},
                npcs = {}
            }
        end
        function data.pick_all()
            data.calls[#data.calls + 1] = { name = "pick_all" }
            return "ok: picked=0 skipped=1"
        end

        local result = Probe.pickup_verify({
            data_module = data,
            repeat_count = 1,
            pickup_key_enabled = false,
            verify_waits = { 0 },
            output = function() end
        })
        T.assert_true(result.ok)
        T.assert_eq(result.summary.final_disappeared_count, 0)
        T.assert_eq(result.summary.final_appeared_count, 0)
        T.assert_eq(result.summary.final_unchanged_count, 1)
    end)

    T.test("basic combat probe runs one logged PressKey tick with fake data", function()
        local data = fake_data()
        function data.list_nearby()
            data.calls[#data.calls + 1] = { name = "list_nearby" }
            return {
                mobCount = 1,
                dropCount = 0,
                portalCount = 0,
                npcCount = 0,
                mobs = {
                    { Id = 1, Name = "Snail", MobId = 100101, x = "330.0", y = "-180.0", Hp = "", MaxHp = "" }
                },
                drops = {},
                portals = {},
                npcs = {}
            }
        end
        local key_calls = {}
        local lines = {}
        local result = Probe.basic_combat({
            data_module = data,
            key_api = {
                click = function(key_code)
                    key_calls[#key_calls + 1] = key_code
                    return true
                end
            },
            run_seconds = 5,
            max_ticks = 1,
            baseline_attack_range_x = 95,
            baseline_attack_range_y = 45,
            baseline_attack_wait_ms = 0,
            output = function(message) lines[#lines + 1] = message end
        })
        T.assert_true(result.ok)
        T.assert_eq(result.ticks, 1)
        T.assert_eq(key_calls[1], 0x10)

        local found_proposal = false
        local found_delta = false
        for _, line in ipairs(lines) do
            if line:find("proposal action=PressKey", 1, true) then found_proposal = true end
            if line:find("after_delta", 1, true) then found_delta = true end
        end
        T.assert_true(found_proposal)
        T.assert_true(found_delta)
    end)

    T.test("platform combat probe stops after platform clear and pickup empty confirmation", function()
        local data = fake_data()
        function data.player_info()
            data.calls[#data.calls + 1] = { name = "player_info" }
            return {
                Hp = "100", Mp = "100", Level = "12", MaxHp = "100", MaxMp = "100",
                Nickname = "hero", CharId = "c1", X = "-3.4", Y = "-10.9",
                WalkSpeed = "1.0", Gravity = "1.0", Invincible = "false",
                MapId = "100050000", MapName = "Manual"
            }
        end
        function data.list_nearby()
            data.calls[#data.calls + 1] = { name = "list_nearby" }
            return {
                mobCount = 1,
                dropCount = 0,
                portalCount = 0,
                npcCount = 0,
                mobs = {
                    { Id = 1, Name = "Slime", MobId = 100, x = "-4.0", y = "-10.9", Hp = "", MaxHp = "" }
                },
                drops = {},
                portals = {},
                npcs = {}
            }
        end

        local lines = {}
        local cwd = sys and sys.get_cwd and sys.get_cwd() or "."
        local result = Probe.platform_combat({
            data_module = data,
            platform_path = cwd .. "/scripts/maple/maps/manual_platform.lua",
            clear_remaining_threshold = 1,
            pickup_empty_confirm_ticks = 3,
            run_seconds = 0,
            max_ticks = 0,
            output = function(message) lines[#lines + 1] = message end
        })
        T.assert_true(result.ok)
        T.assert_eq(result.ticks, 3)

        local found_transition = false
        local found_done = false
        local found_sweep = false
        for _, line in ipairs(lines) do
            if line:find("phase_transition combat->pickup", 1, true) then found_transition = true end
            if line:find("stop reason=platform_clear_pickup_done", 1, true) then found_done = true end
            if line:find("platform_pickup_sweep", 1, true) then found_sweep = true end
        end
        T.assert_true(found_transition)
        T.assert_true(found_done)
        T.assert_false(found_sweep)
    end)

    T.test("platform combat probe default waits for zero platform mobs before pickup", function()
        local data = fake_data()
        local nearby_calls = 0
        function data.player_info()
            data.calls[#data.calls + 1] = { name = "player_info" }
            return {
                Hp = "100", Mp = "100", Level = "12", MaxHp = "100", MaxMp = "100",
                Nickname = "hero", CharId = "c1", X = "-3.4", Y = "-10.9",
                WalkSpeed = "1.0", Gravity = "1.0", Invincible = "false",
                MapId = "100050000", MapName = "Manual"
            }
        end
        function data.list_nearby()
            data.calls[#data.calls + 1] = { name = "list_nearby" }
            nearby_calls = nearby_calls + 1
            local alive = nearby_calls == 1
            return {
                mobCount = alive and 1 or 0,
                dropCount = 0,
                portalCount = 0,
                npcCount = 0,
                mobs = alive and {
                    { Id = 1, Name = "Slime", MobId = 100, x = "-4.0", y = "-10.9", Hp = "", MaxHp = "" }
                } or {},
                drops = {},
                portals = {},
                npcs = {}
            }
        end

        local lines = {}
        local cwd = sys and sys.get_cwd and sys.get_cwd() or "."
        local result = Probe.platform_combat({
            data_module = data,
            key_api = { click = function() return true end },
            platform_path = cwd .. "/scripts/maple/maps/manual_platform.lua",
            pickup_empty_confirm_ticks = 1,
            pickup_sweep_enabled = false,
            attack_wait_ms = 0,
            tick_ms = 0,
            run_seconds = 0,
            max_ticks = 0,
            output = function(message) lines[#lines + 1] = message end
        })
        T.assert_true(result.ok)

        local found_zero_transition = false
        local found_one_transition = false
        for _, line in ipairs(lines) do
            if line:find("phase_transition combat->pickup", 1, true) and line:find("platform_mobs=0", 1, true) then
                found_zero_transition = true
            end
            if line:find("phase_transition combat->pickup", 1, true) and line:find("platform_mobs=1", 1, true) then
                found_one_transition = true
            end
        end
        T.assert_true(found_zero_transition)
        T.assert_false(found_one_transition)
    end)

    T.test("platform combat probe returns from pickup to combat when platform mobs reappear", function()
        local data = fake_data()
        local nearby_calls = 0
        function data.player_info()
            data.calls[#data.calls + 1] = { name = "player_info" }
            return {
                Hp = "100", Mp = "100", Level = "12", MaxHp = "100", MaxMp = "100",
                Nickname = "hero", CharId = "c1", X = "-3.4", Y = "-10.9",
                WalkSpeed = "1.0", Gravity = "1.0", Invincible = "false",
                MapId = "100050000", MapName = "Manual"
            }
        end
        function data.list_nearby()
            data.calls[#data.calls + 1] = { name = "list_nearby" }
            nearby_calls = nearby_calls + 1
            local alive = nearby_calls >= 2
            return {
                mobCount = alive and 1 or 0,
                dropCount = 0,
                portalCount = 0,
                npcCount = 0,
                mobs = alive and {
                    { Id = 1, Name = "Slime", MobId = 100, x = "-4.0", y = "-10.9", Hp = "", MaxHp = "" }
                } or {},
                drops = {},
                portals = {},
                npcs = {}
            }
        end

        local lines = {}
        local cwd = sys and sys.get_cwd and sys.get_cwd() or "."
        local result = Probe.platform_combat({
            data_module = data,
            key_api = { click = function() return true end },
            platform_path = cwd .. "/scripts/maple/maps/manual_platform.lua",
            pickup_empty_confirm_ticks = 3,
            pickup_sweep_enabled = false,
            attack_wait_ms = 0,
            tick_ms = 0,
            run_seconds = 0,
            max_ticks = 2,
            output = function(message) lines[#lines + 1] = message end
        })
        T.assert_true(result.ok)

        local found_back_to_combat = false
        for _, line in ipairs(lines) do
            if line:find("phase_transition pickup->combat", 1, true) then found_back_to_combat = true end
        end
        T.assert_true(found_back_to_combat)
    end)

    T.test("platform combat probe picks platform drops before stopping", function()
        local data = fake_data()
        local picked = false
        function data.player_info()
            data.calls[#data.calls + 1] = { name = "player_info" }
            return {
                Hp = "100", Mp = "100", Level = "12", MaxHp = "100", MaxMp = "100",
                Nickname = "hero", CharId = "c1", X = "-3.4", Y = "-10.9",
                WalkSpeed = "1.0", Gravity = "1.0", Invincible = "false",
                MapId = "100050000", MapName = "Manual"
            }
        end
        function data.list_nearby()
            data.calls[#data.calls + 1] = { name = "list_nearby" }
            return {
                mobCount = 1,
                dropCount = picked and 0 or 1,
                portalCount = 0,
                npcCount = 0,
                mobs = {
                    { Id = 1, Name = "Slime", MobId = 100, x = "-4.0", y = "-10.9", Hp = "", MaxHp = "" }
                },
                drops = picked and {} or {
                    { Id = 9, Name = "Coin", ItemId = 1, Source = "mine", x = "-3.3", y = "-10.9" }
                },
                portals = {},
                npcs = {}
            }
        end
        function data.pick_all()
            data.calls[#data.calls + 1] = { name = "pick_all" }
            picked = true
            return "ok: picked=1"
        end

        local lines = {}
        local cwd = sys and sys.get_cwd and sys.get_cwd() or "."
        local result = Probe.platform_combat({
            data_module = data,
            platform_path = cwd .. "/scripts/maple/maps/manual_platform.lua",
            clear_remaining_threshold = 1,
            pickup_empty_confirm_ticks = 2,
            pickup_sweep_enabled = false,
            run_seconds = 0,
            max_ticks = 0,
            pick_wait_ms = 0,
            output = function(message) lines[#lines + 1] = message end
        })
        T.assert_true(result.ok)
        T.assert_true(picked)

        local found_pick = false
        local found_done = false
        for _, call in ipairs(data.calls) do
            if call.name == "pick_all" then found_pick = true end
        end
        for _, line in ipairs(lines) do
            if line:find("proposal action=PickAllDrops", 1, true) then found_pick = true end
            if line:find("stop reason=platform_clear_pickup_done", 1, true) then found_done = true end
        end
        T.assert_true(found_pick)
        T.assert_true(found_done)
    end)

    T.test("platform combat probe picks nearby drop during combat", function()
        local data = fake_data()
        local picked = false
        function data.player_info()
            data.calls[#data.calls + 1] = { name = "player_info" }
            return {
                Hp = "100", Mp = "100", Level = "12", MaxHp = "100", MaxMp = "100",
                Nickname = "hero", CharId = "c1", X = "-3.4", Y = "-10.9",
                WalkSpeed = "1.0", Gravity = "1.0", Invincible = "false",
                MapId = "100050000", MapName = "Manual"
            }
        end
        function data.list_nearby()
            data.calls[#data.calls + 1] = { name = "list_nearby" }
            return {
                mobCount = 1,
                dropCount = picked and 0 or 1,
                portalCount = 0,
                npcCount = 0,
                mobs = {
                    { Id = 1, Name = "Slime", MobId = 100, x = "-4.0", y = "-10.9", Hp = "", MaxHp = "" }
                },
                drops = picked and {} or {
                    { Id = 9, Name = "Coin", ItemId = 1, Source = "mine", x = "-3.3", y = "-10.9" }
                },
                portals = {},
                npcs = {}
            }
        end
        function data.pick_all()
            data.calls[#data.calls + 1] = { name = "pick_all" }
            picked = true
            return "ok: picked=1"
        end

        local lines = {}
        local cwd = sys and sys.get_cwd and sys.get_cwd() or "."
        local result = Probe.platform_combat({
            data_module = data,
            key_api = { click = function() return true end },
            platform_path = cwd .. "/scripts/maple/maps/manual_platform.lua",
            clear_remaining_threshold = 0,
            pickup_empty_confirm_ticks = 2,
            pickup_sweep_enabled = false,
            max_ticks = 1,
            pick_wait_ms = 0,
            pickup_pick_repeat_ms = 0,
            pickup_key_repeat_ms = 0,
            output = function(message) lines[#lines + 1] = message end
        })
        T.assert_true(result.ok)
        T.assert_true(picked)

        local found_combat_pickup = false
        for _, line in ipairs(lines) do
            if line:find("platform_drop_nearby_during_combat", 1, true) and line:find("PickAllDrops", 1, true) then
                found_combat_pickup = true
            end
        end
        T.assert_true(found_combat_pickup)
    end)

    T.test("platform combat probe uses walk api for pickup movement", function()
        local data = fake_data()
        function data.player_info()
            data.calls[#data.calls + 1] = { name = "player_info" }
            return {
                Hp = "100", Mp = "100", Level = "12", MaxHp = "100", MaxMp = "100",
                Nickname = "hero", CharId = "c1", X = "-3.4", Y = "-10.9",
                WalkSpeed = "1.0", Gravity = "1.0", Invincible = "false",
                MapId = "100050000", MapName = "Manual"
            }
        end
        function data.list_nearby()
            data.calls[#data.calls + 1] = { name = "list_nearby" }
            return {
                mobCount = 0,
                dropCount = 1,
                portalCount = 0,
                npcCount = 0,
                mobs = {},
                drops = {
                    { Id = 9, Name = "Coin", ItemId = 1, Source = "mine", x = "-10.0", y = "-11.8" }
                },
                portals = {},
                npcs = {}
            }
        end

        local lines = {}
        local cwd = sys and sys.get_cwd and sys.get_cwd() or "."
        local result = Probe.platform_combat({
            data_module = data,
            platform_path = cwd .. "/scripts/maple/maps/manual_platform.lua",
            pickup_empty_confirm_ticks = 2,
            pickup_sweep_enabled = false,
            pickup_move_ms = 0,
            pickup_move_method = "walk_api",
            run_seconds = 0,
            max_ticks = 1,
            output = function(message) lines[#lines + 1] = message end
        })
        T.assert_true(result.ok)

        local found_pickup_walk = false
        local found_move_log = false
        for _, call in ipairs(data.calls) do
            if call.name == "walk" and call.direction == -1 then found_pickup_walk = true end
        end
        for _, line in ipairs(lines) do
            if line:find("move_step reason=platform_move_to_drop", 1, true) and line:find("method=walk_api", 1, true) then
                found_move_log = true
            end
        end
        T.assert_true(found_pickup_walk)
        T.assert_true(found_move_log)
    end)

    T.test("platform combat probe ignores stuck pickup drop and moves to next drop", function()
        local data = fake_data()
        local key_calls = {}
        function data.player_info()
            data.calls[#data.calls + 1] = { name = "player_info" }
            return {
                Hp = "100", Mp = "100", Level = "12", MaxHp = "100", MaxMp = "100",
                Nickname = "hero", CharId = "c1", X = "-3.4", Y = "-10.9",
                WalkSpeed = "1.0", Gravity = "1.0", Invincible = "false",
                MapId = "100050000", MapName = "Manual"
            }
        end
        function data.list_nearby()
            data.calls[#data.calls + 1] = { name = "list_nearby" }
            return {
                mobCount = 0,
                dropCount = 2,
                portalCount = 0,
                npcCount = 0,
                mobs = {},
                drops = {
                    { Id = 9, Name = "NearCoin", ItemId = 1, Source = "mine", x = "-3.2", y = "-10.9" },
                    { Id = 10, Name = "FarCoin", ItemId = 1, Source = "mine", x = "-6.0", y = "-11.4" }
                },
                portals = {},
                npcs = {}
            }
        end
        function data.pick_all()
            data.calls[#data.calls + 1] = { name = "pick_all" }
            return "ok: picked=0"
        end

        local lines = {}
        local cwd = sys and sys.get_cwd and sys.get_cwd() or "."
        local result = Probe.platform_combat({
            data_module = data,
            key_api = {
                click = function(key_code)
                    key_calls[#key_calls + 1] = key_code
                    return true
                end
            },
            platform_path = cwd .. "/scripts/maple/maps/manual_platform.lua",
            pickup_empty_confirm_ticks = 2,
            pickup_sweep_enabled = false,
            pickup_drop_fail_threshold = 1,
            pickup_drop_ignore_ticks = 8,
            pickup_pick_repeat = 1,
            pickup_key_repeat = 1,
            pickup_pick_repeat_ms = 0,
            pickup_key_repeat_ms = 0,
            pick_wait_ms = 0,
            pickup_move_ms = 0,
            run_seconds = 0,
            max_ticks = 2,
            output = function(message) lines[#lines + 1] = message end
        })
        T.assert_true(result.ok)

        local found_ignore = false
        local found_move = false
        for _, line in ipairs(lines) do
            if line:find("pickup_drop_ignore id=9", 1, true) then found_ignore = true end
            if line:find("move_step reason=platform_move_to_drop", 1, true) then found_move = true end
        end
        T.assert_true(found_ignore)
        T.assert_true(found_move)
        T.assert_gte(#key_calls, 1)
    end)

    T.test("platform combat probe sweeps platform when API drops are empty", function()
        local data = fake_data()
        local pick_count = 0
        local key_calls = {}
        function data.player_info()
            data.calls[#data.calls + 1] = { name = "player_info" }
            return {
                Hp = "100", Mp = "100", Level = "12", MaxHp = "100", MaxMp = "100",
                Nickname = "hero", CharId = "c1", X = "-3.4", Y = "-10.9",
                WalkSpeed = "1.0", Gravity = "1.0", Invincible = "false",
                MapId = "100050000", MapName = "Manual"
            }
        end
        function data.list_nearby()
            data.calls[#data.calls + 1] = { name = "list_nearby" }
            return {
                mobCount = 1,
                dropCount = 0,
                portalCount = 0,
                npcCount = 0,
                mobs = {
                    { Id = 1, Name = "Slime", MobId = 100, x = "-4.0", y = "-10.9", Hp = "", MaxHp = "" }
                },
                drops = {},
                portals = {},
                npcs = {}
            }
        end
        function data.pick_all()
            data.calls[#data.calls + 1] = { name = "pick_all" }
            pick_count = pick_count + 1
            return "ok: picked=0"
        end

        local lines = {}
        local cwd = sys and sys.get_cwd and sys.get_cwd() or "."
        local result = Probe.platform_combat({
            data_module = data,
            key_api = {
                click = function(key_code)
                    key_calls[#key_calls + 1] = key_code
                    return true
                end
            },
            platform_path = cwd .. "/scripts/maple/maps/manual_platform.lua",
            clear_remaining_threshold = 1,
            pickup_empty_confirm_ticks = 3,
            pickup_sweep_enabled = true,
            max_ticks = 1,
            pick_wait_ms = 0,
            pickup_pick_repeat_ms = 0,
            pickup_key_repeat_ms = 0,
            output = function(message) lines[#lines + 1] = message end
        })
        T.assert_true(result.ok)
        T.assert_eq(result.ticks, 1)
        T.assert_gte(pick_count, 1)
        T.assert_eq(key_calls[1], 0x5A)

        local found_sweep = false
        local found_pickup_key = false
        for _, line in ipairs(lines) do
            if line:find("platform_pickup_sweep_pick", 1, true) then found_sweep = true end
            if line:find("pickup_key_repeat", 1, true) then found_pickup_key = true end
        end
        T.assert_true(found_sweep)
        T.assert_true(found_pickup_key)
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

    T.test("foundation probe executes data-backed list_nearby case", function()
        local data = fake_data()
        local old_require = require
        _G.require = function(name)
            if name == "data" then return data end
            return old_require(name)
        end
        local ok, result = pcall(function()
            return FoundationProbe.run({
                probe_case = "list_nearby",
                probe_target_name = "msw.exe",
                probe_license_key = "test"
            })
        end)
        _G.require = old_require
        T.assert_true(ok)
        T.assert_true(result.ok)
        T.assert_eq(data.calls[1].name, "connect")
        T.assert_eq(data.calls[2].name, "list_nearby")
    end)

    T.test("foundation probe returns safe failure for unknown case", function()
        local data = fake_data()
        local old_require = require
        _G.require = function(name)
            if name == "data" then return data end
            return old_require(name)
        end
        local ok, result = pcall(function()
            return FoundationProbe.run({ probe_case = "missing_case" })
        end)
        _G.require = old_require
        T.assert_true(ok)
        T.assert_false(result.ok)
        T.assert_eq(result.case, "missing_case")
    end)

    return T.report("maple_unit")
end

return { run = run }
