local T = require("tests.test_framework")

local function clear_modules()
    package.loaded["aion.main_quest_teleport_guard"] = nil
end

local function load_guard()
    clear_modules()
    return require("aion.main_quest_teleport_guard")
end

local function run()
    T.reset()
    T.log("\n=== aion main quest teleport guard tests ===")

    T.test("allows quest teleport when no teleport is pending", function()
        local guard = load_guard()
        local block = guard.shouldBlockQuestTeleport({}, 20621, "quest_20621_after_dialog_teleport")

        T.assert_eq(block, false)
    end)

    T.test("allows same pending quest teleport stage", function()
        local guard = load_guard()
        local block = guard.shouldBlockQuestTeleport({
            waiting_teleport = true,
            teleport_quest_id = 20621,
            teleport_stage = "quest_20621_after_dialog_teleport",
        }, 20621, "quest_20621_after_dialog_teleport")

        T.assert_eq(block, false)
    end)

    T.test("blocks older quest teleport while later teleport waits for position change", function()
        local guard = load_guard()
        local block, reason = guard.shouldBlockQuestTeleport({
            waiting_teleport = true,
            teleport_quest_id = 20621,
            teleport_stage = "quest_20621_after_dialog_teleport",
        }, 20610, "quest_20610_task_teleport")

        T.assert_eq(block, true)
        T.assert_eq(reason, "pending_stage=quest_20621_after_dialog_teleport")
    end)

    T.test("blocks same stage with a different pending quest id", function()
        local guard = load_guard()
        local block, reason = guard.shouldBlockQuestTeleport({
            waiting_teleport = true,
            teleport_quest_id = 20621,
            teleport_stage = "shared_stage",
        }, 20610, "shared_stage")

        T.assert_eq(block, true)
        T.assert_eq(reason, "pending_quest_id=20621")
    end)

    clear_modules()
    return T.report("aion_main_quest_teleport_guard")
end

return { run = run }
