local T = require("tests.test_framework")

local Bootstrap = require("maple.bootstrap")
local Store = require("maple.account.store")
local Orchestrator = require("maple.account.orchestrator")

local function fake_config()
    local data = {}
    return {
        load = function() return true end,
        save = function() return true end,
        get = function(key, default)
            local value = data[key]
            if value == nil then return default end
            return value
        end,
        set = function(key, value)
            data[key] = value
            return true
        end,
        data = data
    }
end

local function fake_sys()
    local data = {}
    return {
        set_share = function(key, value) data[key] = value end,
        get_share = function(key) return data[key] end,
        data = data
    }
end

local function fake_task()
    local api = { next_id = 100, run_calls = {}, stop_calls = {}, infos = {} }
    function api.run(script, opts)
        api.next_id = api.next_id + 1
        api.run_calls[#api.run_calls + 1] = { script = script, opts = opts, id = api.next_id }
        api.infos[api.next_id] = { id = api.next_id, status = "running", progress = 0.5 }
        return api.next_id
    end
    function api.stop(id)
        api.stop_calls[#api.stop_calls + 1] = id
        api.infos[id] = { id = id, status = "stopped", progress = 1 }
        return true
    end
    function api.info(id)
        return api.infos[id]
    end
    return api
end

local function run()
    T.reset()
    T.log("\n=== maple flow tests ===")

    T.test("agent loop runs one mock tick through planner tree executor", function()
        local system = Bootstrap.new({ account = { enabled = true }, account_index = 1 })
        Bootstrap.tick(system)
        T.assert_eq(system.blackboard.runtime.tick, 1)
        T.assert_eq(system.blackboard.task.active_goal, "quest")
        T.assert_eq(system.blackboard.metrics.action_success_count, 1)
    end)

    T.test("agent loop routes nearby targets to combat decision port", function()
        local system = Bootstrap.new({
            account = { enabled = true, combat_logic_mode = "predictive" },
            account_index = 1,
            world = {
                world = {
                    nearby_targets = {
                        { id = "m1", x = 40, y = 0, z = 0, vx = 0, vy = 0 }
                    }
                }
            }
        })
        Bootstrap.tick(system)
        T.assert_eq(system.blackboard.task.active_goal, "combat")
        T.assert_eq(system.blackboard.metrics.action_success_count, 1)
        T.assert_eq(system.blackboard.combat.last_proposal.mode, "predictive")
        T.assert_eq(system.blackboard.task.last_result.proposal.mode, "predictive")
    end)

    T.test("account store adds and saves durable account records", function()
        local backend = fake_config()
        Store.set_backend(backend)
        local root = Store.load()
        Store.add(root, Store.new_account({ account = "user1", password = "pw", server = "s1" }))
        T.assert_eq(#root.items, 1)
        T.assert_eq(root.items[1].account, "user1")
        T.assert_true(Store.save(root))
        T.assert_eq(#backend.data["maple.accounts"].items, 1)
        Store.set_backend(nil)
    end)

    T.test("account store keeps Aion-style common and audit fields", function()
        local backend = fake_config()
        Store.set_backend(backend)
        local root = Store.load()
        root.auto_relogin_on_disconnect = true
        root.auto_relogin_cooldown_seconds = 15
        root.game_path = "C:/Game"
        Store.add(root, Store.new_account({
            account = "user1",
            task = "main",
            route = "r1",
            smart_combat_enabled = true,
            combat_logic_mode = "predictive"
        }))
        T.assert_true(Store.save(root))

        local loaded = Store.load()
        T.assert_true(loaded.auto_relogin_on_disconnect)
        T.assert_eq(loaded.auto_relogin_cooldown_seconds, 15)
        T.assert_eq(loaded.game_path, "C:/Game")
        T.assert_eq(loaded.items[1].task, "main")
        T.assert_true(loaded.items[1].smart_combat_enabled)
        T.assert_eq(loaded.items[1].combat_logic_mode, "predictive")
        T.assert_type(loaded.items[1].audit, "table")
        Store.set_backend(nil)
    end)

    T.test("orchestrator starts one task per selected account", function()
        local task_api = fake_task()
        local sys_api = fake_sys()
        local orch = Orchestrator.new({ task_api = task_api, sys_api = sys_api })
        local account = Store.new_account({ account = "user1" })
        local ok, id = orch:start_account(account, 1)
        T.assert_true(ok)
        T.assert_eq(account.runtime.task_id, id)
        T.assert_eq(#task_api.run_calls, 1)
        T.assert_eq(task_api.run_calls[1].script, "scripts/maple_account_worker.lua")
        T.assert_eq(task_api.run_calls[1].opts.account_index, "1")
        T.assert_eq(sys_api.data[Store.status_key(1, "status")], "running")
    end)

    T.test("orchestrator stop is isolated to one account", function()
        local task_api = fake_task()
        local sys_api = fake_sys()
        local orch = Orchestrator.new({ task_api = task_api, sys_api = sys_api })
        local a1 = Store.new_account({ account = "user1" })
        local a2 = Store.new_account({ account = "user2" })
        orch:start_account(a1, 1)
        orch:start_account(a2, 2)
        local id2 = a2.runtime.task_id
        orch:stop_account(a1, 1, "test_stop")
        T.assert_nil(a1.runtime.task_id)
        T.assert_eq(a2.runtime.task_id, id2)
        T.assert_eq(#task_api.stop_calls, 1)
        T.assert_eq(sys_api.data[Store.status_key(1, "stop")], true)
        T.assert_eq(sys_api.data[Store.status_key(2, "stop")], false)
    end)

    T.test("start_all respects max_parallel", function()
        local task_api = fake_task()
        local orch = Orchestrator.new({ task_api = task_api, sys_api = fake_sys() })
        local root = {
            max_parallel = 1,
            items = {
                Store.new_account({ account = "user1" }),
                Store.new_account({ account = "user2" })
            }
        }
        local count = orch:start_all(root)
        T.assert_eq(count, 1)
        T.assert_eq(#task_api.run_calls, 1)
        T.assert_not_nil(root.items[1].runtime.task_id)
        T.assert_nil(root.items[2].runtime.task_id)
    end)

    return T.report("maple_flow")
end

return { run = run }
