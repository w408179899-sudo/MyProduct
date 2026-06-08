local T = require("tests.test_framework")
local post_kill_loot = require("aion.post_kill_loot")

local function run()
    T.reset()
    T.log("\n=== aion post-kill loot tests ===")

    T.test("not-lootable stays in post-kill wait window", function()
        local decision = post_kill_loot.decide({
            last_killed_obj = 1001,
            post_kill_until = 13.0,
            now = 11.2,
            reject_reason = "not-lootable",
            seen_entity = { lootable = 0 },
        })
        T.assert_eq(decision.action, "wait")
        T.assert_eq(decision.reason, "not-lootable")
        T.assert_true(decision.remain > 1.7 and decision.remain < 1.9)
    end)

    T.test("not-lootable after old grace still waits until full window", function()
        local decision = post_kill_loot.decide({
            last_killed_obj = 1001,
            post_kill_until = 23.0,
            now = 20.9,
            reject_reason = "not-lootable",
            elapsed = 0.9,
            seen_entity = { lootable = 0 },
        })
        T.assert_eq(decision.action, "wait")
        T.assert_true(decision.remain > 2.0)
    end)

    T.test("loot-ready opens loot before reacquiring target", function()
        local decision = post_kill_loot.decide({
            last_killed_obj = 1001,
            post_kill_until = 13.0,
            now = 10.5,
            loot_target = { obj = 1001, lootable = 1 },
        })
        T.assert_eq(decision.action, "open-loot")
        T.assert_eq(decision.reason, "loot-ready")
    end)

    T.test("expired post-kill window can resume target search", function()
        local decision = post_kill_loot.decide({
            last_killed_obj = 1001,
            post_kill_until = 13.0,
            now = 13.0,
            reject_reason = "not-lootable",
        })
        T.assert_eq(decision.action, "expired")
    end)

    T.test("missing last kill is idle", function()
        local decision = post_kill_loot.decide({
            last_killed_obj = 0,
            post_kill_until = 13.0,
            now = 10.0,
        })
        T.assert_eq(decision.action, "none")
    end)

    return T.report("aion_post_kill_loot")
end

return { run = run }
