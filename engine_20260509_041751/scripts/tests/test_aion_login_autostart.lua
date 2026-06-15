local T = require("tests.test_framework")
local autostart = require("aion.login_autostart")

local function base_ctx()
    return {
        cfg = {
            accounts = { auto_start_after_login = true },
            primary_mode = 1,
            target = {},
            combat = { mode = 1 },
            route = { route_points = "" },
        },
        account = {
            login = { status = "ready" },
            target = { pid = 5852 },
            runtime = { status = "idle", task_id = 0 },
        },
        runtime = {
            running = false,
            accounts = {},
        },
    }
end

local function run()
    T.reset()
    T.log("\n=== aion login autostart tests ===")

    T.test("disabled setting does not start", function()
        local ctx = base_ctx()
        ctx.cfg.accounts.auto_start_after_login = false
        local decision = autostart.decide(ctx)
        T.assert_eq(decision.action, "none")
        T.assert_eq(decision.reason, "disabled")
    end)

    T.test("not-ready login status does not start", function()
        local ctx = base_ctx()
        ctx.account.login.status = "waiting_enter_game"
        local decision = autostart.decide(ctx)
        T.assert_eq(decision.action, "none")
        T.assert_eq(decision.reason, "login-not-ready")
    end)

    T.test("ready without pid is blocked", function()
        local ctx = base_ctx()
        ctx.account.target.pid = 0
        local decision = autostart.decide(ctx)
        T.assert_eq(decision.action, "block")
        T.assert_eq(decision.reason, "pid-missing")
    end)

    T.test("combat stationary mode starts from current combat config", function()
        local ctx = base_ctx()
        ctx.cfg.combat.mode = 1
        local decision = autostart.decide(ctx)
        T.assert_eq(decision.action, "start")
        T.assert_eq(decision.reason, "login-ready")
        T.assert_eq(decision.pid, 5852)
    end)

    T.test("combat patrol mode requires route points", function()
        local ctx = base_ctx()
        ctx.cfg.combat.mode = 2
        ctx.cfg.route.route_points = ""
        local decision = autostart.decide(ctx)
        T.assert_eq(decision.action, "block")
        T.assert_eq(decision.reason, "combat-route-empty")
    end)

    T.test("combat patrol mode starts when route points exist", function()
        local ctx = base_ctx()
        ctx.cfg.combat.mode = 2
        ctx.cfg.route.route_points = "1, 2, 3\n4, 5, 6"
        local decision = autostart.decide(ctx)
        T.assert_eq(decision.action, "start")
    end)

    T.test("already running runtime does not start again", function()
        local ctx = base_ctx()
        ctx.runtime.running = true
        local decision = autostart.decide(ctx)
        T.assert_eq(decision.action, "none")
        T.assert_eq(decision.reason, "runtime-running")
    end)

    T.test("pending script does not queue another start", function()
        local ctx = base_ctx()
        ctx.runtime.accounts.pending_script = { action = "start", index = 1 }
        local decision = autostart.decide(ctx)
        T.assert_eq(decision.action, "none")
        T.assert_eq(decision.reason, "script-pending")
    end)

    T.test("settings window open does not queue another start", function()
        local ctx = base_ctx()
        ctx.runtime.accounts.settings_window_visible = true
        local decision = autostart.decide(ctx)
        T.assert_eq(decision.action, "none")
        T.assert_eq(decision.reason, "settings-window-open")
    end)

    T.test("pending script queue does not queue another start", function()
        local ctx = base_ctx()
        ctx.runtime.accounts.pending_scripts = {
            { action = "start", index = 1 },
        }
        local decision = autostart.decide(ctx)
        T.assert_eq(decision.action, "none")
        T.assert_eq(decision.reason, "script-pending")
    end)

    T.test("active account runtime does not queue another start", function()
        local ctx = base_ctx()
        ctx.account.runtime.status = "queued_start"
        local decision = autostart.decide(ctx)
        T.assert_eq(decision.action, "none")
        T.assert_eq(decision.reason, "account-runtime-active")
    end)

    T.test("manual stop does not auto start again", function()
        local ctx = base_ctx()
        ctx.account.runtime.manual_stop = true
        local decision = autostart.decide(ctx)
        T.assert_eq(decision.action, "none")
        T.assert_eq(decision.reason, "manual-stop")
    end)

    return T.report("aion_login_autostart")
end

return { run = run }
