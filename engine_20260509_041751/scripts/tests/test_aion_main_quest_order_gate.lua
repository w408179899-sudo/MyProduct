local T = require("tests.test_framework")

local function clear_modules()
    package.loaded["aion.main_quest_order_gate"] = nil
end

local function load_gate()
    clear_modules()
    return require("aion.main_quest_order_gate")
end

local function quest(id, status)
    return { id = id, status_code = status, req_count = 0 }
end

local function run()
    T.reset()
    T.log("\n=== aion main quest order gate tests ===")

    T.test("runs 20590 while reward dialog is open", function()
        local gate = load_gate()
        local stage, reason = gate.choose({}, {
            quest(20610, 4),
        }, true)

        T.assert_eq(stage, "20590")
        T.assert_eq(reason, "quest20590-reward-dialog")
    end)

    T.test("runs 20590 while current 20590 is active", function()
        local gate = load_gate()
        local stage, reason = gate.choose({}, {
            quest(20590, 3),
        }, false)

        T.assert_eq(stage, "20590")
        T.assert_eq(reason, "quest20590-current")
    end)

    T.test("runs 20610 while current 20610 is unfinished", function()
        local gate = load_gate()
        local stage, reason = gate.choose({
            completed_20590_reward = true,
        }, {
            quest(20610, 4),
        }, false)

        T.assert_eq(stage, "20610")
        T.assert_eq(reason, "quest20610-current")
    end)

    T.test("does not rerun 20610 when only later quests are current", function()
        local gate = load_gate()
        local stage, reason = gate.choose({
            completed_20590_reward = true,
            completed_20610_reward = false,
        }, {
            quest(20621, 4),
            quest(20622, 6),
        }, false)

        T.assert_eq(stage, "20611")
        T.assert_eq(reason, "main-chain")
    end)

    T.test("runs 20611 after old tasks are complete", function()
        local gate = load_gate()
        local stage, reason = gate.choose({
            completed_20590_reward = true,
            completed_20610_reward = true,
        }, {
            quest(20611, 3),
        }, false)

        T.assert_eq(stage, "20611")
        T.assert_eq(reason, "main-chain")
    end)

    clear_modules()
    return T.report("aion_main_quest_order_gate")
end

return { run = run }
