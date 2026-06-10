local T = require("tests.test_framework")

local function clear_modules()
    package.loaded["aion.task_recorder"] = nil
end

local function load_module()
    clear_modules()
    return require("aion.task_recorder")
end

local function run()
    T.reset()
    T.log("\n=== aion task recorder tests ===")

    T.test("builds x25 dialog action hint", function()
        local recorder = load_module()
        local result = recorder.build({
            snapshot = {
                status = "ok",
                summary = "character=Hero level=1 map=Start quests=1 main=1",
                lines = {
                    "character name=Hero level=1 pos=564.00,2785.00,299.50",
                    "map current index=13 big_map_id=220010000 region=Start name_cn=Start",
                    "main_quest.snapshot current_id=20590 current_step=0 current_status=running current_name=BadName",
                },
            },
            target = {
                status = "target_matched",
                summary = "Guard kind=NPC obj=1 id=2",
                lines = { "detail name=Guard kind=NPC type_name=Foo interact_id=2147533452 dist=3.21 pos=560.99,2786.03,299.06" },
            },
            dialog = {
                npc_dialog_id = 2147533452,
                dialog_content_id = 1011,
                quest_id = 20590,
                type_text = "select1",
                next_text = "HACTION_SELECT1_1",
            },
            dialog_children = {
                { obj = 100, name = "background", visible = true, x = 13, y = 113, depth = 1 },
                { obj = 200, name = "", visible = true, x = 24.666666666667, y = 221.13333412011, depth = 2 },
            },
            opts = { dialog_click_x = 25, dialog_click_x_tolerance = 2 },
        })

        T.assert_eq(result.status, "ok")
        T.assert_contains(result.summary, "dialog_click_x")
        local text = table.concat(result.lines, "\n")
        T.assert_contains(text, "record version=1 source=F11")
        T.assert_contains(text, "dialog=open npc_dialog_id=2147533452")
        T.assert_contains(text, "action_hint=dialog_click_x")
        T.assert_contains(text, "task_key quest_id=20590 step=0 map_id=220010000")
        T.assert_contains(text, "target_interact_id=2147533452")
        T.assert_contains(text, "dialog.child[02]")
        T.assert_contains(text, "x=24.67 y=221.13")
        T.assert_false(text:find("current_name=", 1, true) ~= nil, "unstable current_name should be removed")
        T.assert_false(text:find(" region=", 1, true) ~= nil, "unstable region should be removed")
        T.assert_false(text:find("target.detail name=", 1, true) ~= nil, "unstable target name should be removed")
    end)

    T.test("prefers ok button on reward dialog", function()
        local recorder = load_module()
        local result = recorder.build({
            dialog = {
                npc_dialog_id = 2147492916,
                dialog_content_id = 5,
                quest_id = 20590,
                type_text = "select_quest_reward1",
            },
            dialog_children = {
                { obj = 300, name = "", visible = true, x = 25, y = 233, depth = 2 },
                { obj = 301, name = "ok", visible = true, x = 129, y = 419, depth = 1 },
            },
        })

        T.assert_contains(result.summary, "dialog_click_ok")
    end)

    T.test("reports target interaction when no dialog is open", function()
        local recorder = load_module()
        local result = recorder.build({
            target = {
                status = "target_matched",
                summary = "Quest NPC kind=NPC obj=7 id=8",
                lines = { "detail name=Quest NPC interact_id=99" },
            },
        })

        T.assert_eq(result.summary, "target_interact_or_move")
        T.assert_contains(table.concat(result.lines, "\n"), "dialog=closed")
    end)

    T.test("sanitizes control characters from log lines", function()
        local recorder = load_module()
        local result = recorder.build({
            snapshot = {
                status = "ok",
                summary = "character=Hero\r\nlevel=5",
                lines = { "main_quest.snapshot current_id=20610\r\ncurrent_step=0" },
            },
            target = {
                status = "target_matched",
                summary = "NPC\rname",
                lines = { "detail name=NPC\tinteract_id=2147492916" },
            },
        })

        for _, line in ipairs(result.lines) do
            T.assert_false(tostring(line):find("\r", 1, true) ~= nil, "line has CR")
            T.assert_false(tostring(line):find("\n", 1, true) ~= nil, "line has LF")
            T.assert_false(tostring(line):find("\t", 1, true) ~= nil, "line has TAB")
        end
        local text = table.concat(result.lines, "\n")
        T.assert_contains(text, "snapshot=character=Hero level=5")
        T.assert_contains(text, "snapshot.main_quest.snapshot current_id=20610 current_step=0")
    end)

    clear_modules()
    return T.report("aion_task_recorder")
end

return { run = run }
