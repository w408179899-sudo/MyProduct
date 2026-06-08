local T = require("tests.test_framework")
local floor_recovery = require("aion.floor_recovery")

local function run()
    T.reset()
    T.log("\n=== aion floor-recovery tests ===")

    T.test("defaults are disabled with 15 to 90 thresholds and 8/X keys", function()
        local settings = floor_recovery.from_config({})
        T.assert_false(settings.enabled)
        T.assert_eq(settings.start_percent, 15)
        T.assert_eq(settings.recover_percent, 90)
        T.assert_eq(settings.sit_keycode, 56)
        T.assert_eq(settings.stand_keycode, 88)
        T.assert_true(settings.cancel_on_damage)
    end)

    T.test("configuration normalizes unsafe values", function()
        local settings = floor_recovery.from_config({
            floor_recovery = {
                enabled = true,
                start_percent = 99,
                recover_percent = 20,
                sit_keycode = -5,
                stand_keycode = 999,
                cancel_on_damage = false,
            },
        })
        T.assert_true(settings.enabled)
        T.assert_eq(settings.start_percent, 99)
        T.assert_eq(settings.recover_percent, 100)
        T.assert_eq(settings.sit_keycode, 1)
        T.assert_eq(settings.stand_keycode, 255)
        T.assert_false(settings.cancel_on_damage)
    end)

    T.test("no start before a loot completion marker", function()
        local decision = floor_recovery.decide({
            settings = { enabled = true, start_percent = 15, recover_percent = 90, sit_keycode = 56, stand_keycode = 88 },
            after_loot_pending = false,
            char = { mp = 10, max_mp = 100, hp = 100 },
        })
        T.assert_eq(decision.action, "idle")
        T.assert_eq(decision.reason, "not-after-loot")
    end)

    T.test("disabled setting consumes the after-loot check without sitting", function()
        local decision = floor_recovery.decide({
            settings = { enabled = false, start_percent = 15, recover_percent = 90, sit_keycode = 56, stand_keycode = 88 },
            after_loot_pending = true,
            char = { mp = 10, max_mp = 100, hp = 100 },
        })
        T.assert_eq(decision.action, "skip")
        T.assert_eq(decision.reason, "disabled")
    end)

    T.test("after-loot low MP starts floor recovery", function()
        local decision = floor_recovery.decide({
            settings = { enabled = true, start_percent = 15, recover_percent = 90, sit_keycode = 56, stand_keycode = 88 },
            after_loot_pending = true,
            char = { mp = 14, max_mp = 100, hp = 100 },
        })
        T.assert_eq(decision.action, "start")
        T.assert_eq(decision.reason, "mp-low")
        T.assert_eq(decision.keycode, 56)
    end)

    T.test("character MP percent prefers documented mmp over max_mp alias", function()
        local percent = floor_recovery.character_mp_percent({
            mp = 10,
            max_mp = 10,
            mmp = 100,
        })
        T.assert_eq(percent, 10)
    end)

    T.test("MP equal to low threshold does not start because rule is lower-than", function()
        local decision = floor_recovery.decide({
            settings = { enabled = true, start_percent = 15, recover_percent = 90, sit_keycode = 56, stand_keycode = 88 },
            after_loot_pending = true,
            char = { mp = 15, max_mp = 100, hp = 100 },
        })
        T.assert_eq(decision.action, "skip")
        T.assert_eq(decision.reason, "mp-high")
    end)

    T.test("active recovery waits until recover threshold", function()
        local decision = floor_recovery.decide({
            settings = { enabled = true, start_percent = 15, recover_percent = 90, sit_keycode = 56, stand_keycode = 88 },
            state = { active = true, start_hp = 100, last_hp = 100 },
            char = { mp = 80, max_mp = 100, hp = 100 },
        })
        T.assert_eq(decision.action, "wait")
        T.assert_eq(decision.reason, "recovering")
    end)

    T.test("active recovery finishes at high threshold and sends X", function()
        local decision = floor_recovery.decide({
            settings = { enabled = true, start_percent = 15, recover_percent = 90, sit_keycode = 56, stand_keycode = 88 },
            state = { active = true, start_hp = 100, last_hp = 100 },
            char = { mp = 90, max_mp = 100, hp = 100 },
        })
        T.assert_eq(decision.action, "finish")
        T.assert_eq(decision.reason, "recovered")
        T.assert_eq(decision.keycode, 88)
    end)

    T.test("active recovery cancels on damage and sends X", function()
        local decision = floor_recovery.decide({
            settings = { enabled = true, start_percent = 15, recover_percent = 90, sit_keycode = 56, stand_keycode = 88 },
            state = { active = true, start_hp = 100, last_hp = 100 },
            char = { mp = 30, max_mp = 100, hp = 99 },
        })
        T.assert_eq(decision.action, "cancel")
        T.assert_eq(decision.reason, "damage")
        T.assert_eq(decision.keycode, 88)
    end)

    T.test("start is deferred while combat or loot state is still pending", function()
        local decision = floor_recovery.decide({
            settings = { enabled = true, start_percent = 15, recover_percent = 90, sit_keycode = 56, stand_keycode = 88 },
            after_loot_pending = true,
            in_combat = true,
            char = { mp = 10, max_mp = 100, hp = 100 },
        })
        T.assert_eq(decision.action, "defer")
        T.assert_eq(decision.reason, "combat-not-ended")
    end)

    return T.report("aion_floor_recovery")
end

return { run = run }
