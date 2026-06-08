local T = require("tests.test_framework")
local repeat_key = require("aion.attack_key_repeat")

local function run()
    T.reset()
    T.log("\n=== aion attack-key repeat tests ===")

    T.test("default interval is one second", function()
        local settings = repeat_key.from_config({})
        T.assert_eq(settings.interval_ms, 1000)
    end)

    T.test("configured interval is normalized", function()
        local settings = repeat_key.from_config({
            attack_key_repeat_interval_ms = 1200.8,
        })
        T.assert_eq(settings.interval_ms, 1200)
    end)

    T.test("unsafe interval values are clamped", function()
        local low = repeat_key.from_config({
            attack_key_repeat_interval_ms = 10,
        })
        T.assert_eq(low.interval_ms, 250)

        local high = repeat_key.from_config({
            attack_key_repeat_interval_ms = 9000,
        })
        T.assert_eq(high.interval_ms, 3000)
    end)

    T.test("new target presses immediately", function()
        local ok, reason = repeat_key.should_press({
            now = 10,
            target_obj = 101,
            last_attack_key_obj = 100,
            last_attack_key_at = 9.5,
            interval_ms = 1000,
        })
        T.assert_true(ok)
        T.assert_eq(reason, "new-target")
    end)

    T.test("same target waits until interval expires", function()
        local wait_ok, wait_reason = repeat_key.should_press({
            now = 10.5,
            target_obj = 101,
            last_attack_key_obj = 101,
            last_attack_key_at = 10.0,
            interval_ms = 1000,
        })
        T.assert_false(wait_ok)
        T.assert_eq(wait_reason, "waiting")

        local press_ok, press_reason = repeat_key.should_press({
            now = 11.0,
            target_obj = 101,
            last_attack_key_obj = 101,
            last_attack_key_at = 10.0,
            interval_ms = 1000,
        })
        T.assert_true(press_ok)
        T.assert_eq(press_reason, "interval")
    end)

    T.test("invalid target never presses", function()
        local ok, reason = repeat_key.should_press({
            now = 11.0,
            target_obj = 0,
            last_attack_key_obj = 0,
            last_attack_key_at = 10.0,
            interval_ms = 1000,
        })
        T.assert_false(ok)
        T.assert_eq(reason, "invalid-target")
    end)

    return T.report("aion_attack_key_repeat")
end

return { run = run }
