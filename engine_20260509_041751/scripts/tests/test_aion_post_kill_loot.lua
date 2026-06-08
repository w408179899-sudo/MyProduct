local T = require("tests.test_framework")
local post_kill_loot = require("aion.post_kill_loot")

local function run()
    T.reset()
    T.log("\n=== aion post-kill loot tests ===")

    T.test("missing last kill is idle", function()
        local decision = post_kill_loot.decide({
            last_killed_obj = 0,
            post_kill_check_at = 10.1,
            now = 10.0,
        })
        T.assert_eq(decision.action, "none")
        T.assert_eq(decision.reason, "no-last-kill")
    end)

    T.test("death check waits only until the configured short delay", function()
        local decision = post_kill_loot.decide({
            last_killed_obj = 1001,
            post_kill_check_at = 10.1,
            now = 10.04,
        })
        T.assert_eq(decision.action, "delay")
        T.assert_eq(decision.reason, "check-delay")
        T.assert_true(decision.remain > 0.05 and decision.remain < 0.07)
    end)

    T.test("lootable corpse opens loot at the check point", function()
        local decision = post_kill_loot.decide({
            last_killed_obj = 1001,
            post_kill_check_at = 10.1,
            now = 10.1,
            loot_target = { obj = 1001, lootable = 1 },
        })
        T.assert_eq(decision.action, "open-loot")
        T.assert_eq(decision.reason, "loot-ready")
        T.assert_eq(decision.remain, 0)
    end)

    T.test("not-lootable corpse is skipped immediately after the check", function()
        local decision = post_kill_loot.decide({
            last_killed_obj = 1001,
            post_kill_check_at = 10.1,
            now = 10.11,
            reject_reason = "not-lootable",
            seen_entity = { obj = 1001, lootable = 0 },
        })
        T.assert_eq(decision.action, "skip")
        T.assert_eq(decision.reason, "not-lootable")
        T.assert_eq(decision.remain, 0)
    end)

    T.test("missing corpse is skipped after the one-shot check", function()
        local decision = post_kill_loot.decide({
            last_killed_obj = 1001,
            post_kill_check_at = 10.1,
            now = 10.11,
            reject_reason = "missing",
        })
        T.assert_eq(decision.action, "skip")
        T.assert_eq(decision.reason, "missing")
    end)

    T.test("legacy post_kill_until is treated as the check point", function()
        local decision = post_kill_loot.decide({
            last_killed_obj = 1001,
            post_kill_until = 10.1,
            now = 10.02,
        })
        T.assert_eq(decision.action, "delay")
    end)

    return T.report("aion_post_kill_loot")
end

return { run = run }
