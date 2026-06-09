local T = require("tests.test_framework")
local burst = require("aion.attack_key_burst")

local function run()
    T.reset()
    T.log("\n=== aion attack-key burst tests ===")

    T.test("defaults are five presses every 200ms", function()
        local settings = burst.from_config({})
        T.assert_eq(settings.count, 5)
        T.assert_eq(settings.interval_ms, 200)
    end)

    T.test("configured values are normalized", function()
        local settings = burst.from_config({
            attack_key_burst_count = 4.9,
            attack_key_burst_interval_ms = 250.8,
        })
        T.assert_eq(settings.count, 4)
        T.assert_eq(settings.interval_ms, 250)
    end)

    T.test("unsafe values are clamped", function()
        local low = burst.from_config({
            attack_key_burst_count = -2,
            attack_key_burst_interval_ms = 10,
        })
        T.assert_eq(low.count, 1)
        T.assert_eq(low.interval_ms, 50)

        local high = burst.from_config({
            attack_key_burst_count = 99,
            attack_key_burst_interval_ms = 5000,
        })
        T.assert_eq(high.count, 10)
        T.assert_eq(high.interval_ms, 1000)
    end)

    return T.report("aion_attack_key_burst")
end

return { run = run }
