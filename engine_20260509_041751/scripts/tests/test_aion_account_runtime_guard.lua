local T = require("tests.test_framework")
local guard = require("aion.account_runtime_guard")

local function account(status, task_id)
    return {
        runtime = {
            status = status or "idle",
            task_id = task_id or 0,
        },
    }
end

local function running_task_id(active_id)
    return function(id)
        return tonumber(id) == tonumber(active_id)
    end
end

local function share_store()
    local values = {}
    return values,
        function(key, value)
            values[key] = value
        end,
        function(key)
            return values[key]
        end
end

local function run()
    T.reset()
    T.log("\n=== aion account runtime guard tests ===")

    T.test("queued start blocks a second queue for the same account", function()
        local decision = guard.can_queue_start({
            account = account("queued_start", 0),
            index = 2,
            runtime_accounts = {},
        })

        T.assert_false(decision.allowed)
        T.assert_eq(decision.reason, "account-runtime-active")
    end)

    T.test("same account pending start blocks duplicate queue", function()
        local decision = guard.can_queue_start({
            account = account("idle", 0),
            index = 2,
            runtime_accounts = {
                pending_script = { action = "start", index = 2 },
            },
        })

        T.assert_false(decision.allowed)
        T.assert_eq(decision.reason, "start-pending")
    end)

    T.test("pending start for another account does not block this account", function()
        local decision = guard.can_queue_start({
            account = account("idle", 0),
            index = 3,
            runtime_accounts = {
                pending_script = { action = "start", index = 2 },
            },
        })

        T.assert_true(decision.allowed)
    end)

    T.test("begin start allows its own queued start", function()
        local decision = guard.can_begin_start({
            account = account("queued_start", 0),
            index = 2,
            runtime_accounts = {},
        })

        T.assert_true(decision.allowed)
    end)

    T.test("begin start blocks an already starting account", function()
        local decision = guard.can_begin_start({
            account = account("starting", 0),
            index = 2,
            runtime_accounts = {},
        })

        T.assert_false(decision.allowed)
        T.assert_eq(decision.reason, "account-runtime-active")
    end)

    T.test("active task id blocks even when status is stale", function()
        local decision = guard.can_queue_start({
            account = account("idle", 134),
            index = 2,
            runtime_accounts = {},
            is_task_running = running_task_id(134),
        })

        T.assert_false(decision.allowed)
        T.assert_eq(decision.reason, "account-task-active")
    end)

    T.test("flow: double login events create only one start queue", function()
        local runtime_accounts = {}
        local acc = account("idle", 0)

        local first = guard.can_queue_start({
            account = acc,
            index = 2,
            runtime_accounts = runtime_accounts,
        })
        T.assert_true(first.allowed)

        runtime_accounts.pending_script = { action = "start", index = 2 }
        acc.runtime.status = "queued_start"

        local second_click = guard.can_queue_start({
            account = acc,
            index = 2,
            runtime_accounts = runtime_accounts,
        })
        T.assert_false(second_click.allowed)
        T.assert_eq(second_click.reason, "start-pending")

        runtime_accounts.pending_script = nil
        acc.runtime.status = "starting"
        acc.runtime.task_id = 138

        local bridge_tick = guard.can_queue_start({
            account = acc,
            index = 2,
            runtime_accounts = runtime_accounts,
            is_task_running = running_task_id(138),
        })
        T.assert_false(bridge_tick.allowed)
        T.assert_eq(bridge_tick.reason, "account-runtime-active")
    end)

    T.test("stop request publishes account identities", function()
        local values, set_share = share_store()
        local acc = account("running", 0)
        acc.profile_key = "acc_002_test"
        acc.target = { pid = 24148 }

        local result = guard.publish_stop_request({
            account = acc,
            index = 2,
            requested_at = 100,
            source = "test-stop",
            set_share = set_share,
        })

        T.assert_eq(result.published, 3)
        T.assert_eq(values["aion_runtime.stop.index.2.requested_at"], 100)
        T.assert_eq(values["aion_runtime.stop.profile.acc_002_test.requested_at"], 100)
        T.assert_eq(values["aion_runtime.stop.pid.24148.requested_at"], 100)
    end)

    T.test("runner can stop by pid when task id is missing", function()
        local _, set_share, get_share = share_store()
        local acc = account("running", 0)
        acc.profile_key = "acc_002_test"
        acc.target = { pid = 24148 }

        guard.publish_stop_request({
            account = acc,
            index = 2,
            requested_at = 200,
            set_share = set_share,
        })

        local decision = guard.stop_requested({
            account = { target = { pid = 24148 } },
            index = 99,
            pid = 24148,
            started_at = 100,
            get_share = get_share,
        })

        T.assert_true(decision.stop)
        T.assert_eq(decision.identity, "pid.24148")
    end)

    T.test("stale stop request does not stop a newer runner", function()
        local _, set_share, get_share = share_store()
        local acc = account("running", 0)
        acc.target = { pid = 24148 }

        guard.publish_stop_request({
            account = acc,
            index = 2,
            requested_at = 100,
            set_share = set_share,
        })

        local decision = guard.stop_requested({
            account = acc,
            index = 2,
            started_at = 200,
            get_share = get_share,
        })

        T.assert_false(decision.stop)
    end)

    T.test("clear stop request removes stale stop flag", function()
        local values, set_share, get_share = share_store()
        local acc = account("running", 0)
        acc.target = { pid = 24148 }

        guard.publish_stop_request({
            account = acc,
            index = 2,
            requested_at = 100,
            set_share = set_share,
        })
        T.assert_eq(values["aion_runtime.stop.pid.24148.requested_at"], 100)

        guard.clear_stop_request({
            account = acc,
            index = 2,
            set_share = set_share,
        })

        local decision = guard.stop_requested({
            account = acc,
            index = 2,
            started_at = 0,
            get_share = get_share,
        })
        T.assert_false(decision.stop)
    end)

    return T.report("aion_account_runtime_guard")
end

return { run = run }
