local T = require("tests.test_framework")

local mission_tab = string.char(228, 189, 191, 229, 145, 189)

local function clear_modules()
    package.loaded["aion.quest_snapshot"] = nil
    package.loaded["aion.core"] = nil
    package.loaded["aion.quest"] = nil
    package.loaded["aion.map"] = nil
end

local function install_mocks(opts)
    clear_modules()
    opts = opts or {}
    package.loaded["aion.core"] = {
        getCharacter = function()
            if opts.char_err then
                return false, nil, opts.char_err
            end
            return true, opts.char or {
                name = "Silverleaf",
                level = 12,
                race = 1,
                race_name = "Asmodian",
                gender = 0,
                gender_name = "Male",
                job = 2,
                hp = 100,
                mhp = 120,
                mp = 50,
                mmp = 80,
                x = 1,
                y = 2,
                z = 3,
            }, nil
        end,
    }
    package.loaded["aion.map"] = {
        current = function()
            if opts.map_err then
                return false, nil, opts.map_err
            end
            return true, opts.map or { index = 7, region = "Pandemonium", name_en = "LC1", level = 10 }, nil
        end,
        bigMapId = function()
            if opts.big_map_err then
                return false, nil, opts.big_map_err
            end
            return true, opts.big_map_id or 5, nil
        end,
    }
    package.loaded["aion.quest"] = {
        list = function()
            if opts.quest_err then
                return false, nil, opts.quest_err
            end
            return true, opts.quests or {}, nil
        end,
    }
    return require("aion.quest_snapshot")
end

local function joined(result)
    return table.concat(result.lines or {}, "\n")
end

local function run()
    T.reset()
    T.log("\n=== aion quest snapshot tests ===")

    T.test("prints character map and raw quest list", function()
        local snapshot = install_mocks({
            quests = {
                { id = 100, tab_name = mission_tab, status_code = 3, status_name = "doing", req_count = 2, seq = 1, name = "Main A" },
                { id = 200, tab_name = "task", status_code = 4, status_name = "done", req_count = 5, seq = 2, name = "Side B" },
            },
        })

        local ok, result, err = snapshot.read()
        T.assert_true(ok, err)
        T.assert_eq(result.status, "ok")
        local text = joined(result)
        T.assert_contains(text, "character name=Silverleaf level=12")
        T.assert_contains(text, "map current index=7 big_map_id=5 region=Pandemonium")
        T.assert_contains(text, "GetQuestList() count=2")
        T.assert_contains(text, "quest[2] id=200")
    end)

    T.test("builds main quest progress snapshot", function()
        local snapshot = install_mocks({
            quests = {
                { id = 90, tab_name = mission_tab, status_code = 4, status_name = "ready", req_count = 9, name = "Turn In" },
                { id = 100, tab_name = mission_tab, status_code = 3, status_name = "doing", req_count = 2, name = "Current Main" },
                { id = 200, tab_name = "task", status_code = 3, name = "Side" },
            },
        })

        local ok, result, err = snapshot.read()
        T.assert_true(ok, err)
        local text = joined(result)
        T.assert_contains(text, "main_quest.snapshot total=2 doing=1 ready=1 level_blocked=0 current_id=100 current_step=2")
        T.assert_contains(text, "main_quest[1] id=90")
        T.assert_contains(text, "main_quest[2] id=100")
    end)

    T.test("reports empty main quest snapshot", function()
        local snapshot = install_mocks({
            quests = {
                { id = 200, tab_name = "task", status_code = 3, name = "Side" },
            },
        })

        local ok, result, err = snapshot.read()
        T.assert_true(ok, err)
        T.assert_contains(joined(result), "main_quest.snapshot total=0 doing=0 ready=0 level_blocked=0 current_id=")
    end)

    T.test("keeps partial snapshot when quest list fails", function()
        local snapshot = install_mocks({ quest_err = "quest api failed" })
        local ok, result, err = snapshot.read()
        T.assert_true(ok, err)
        T.assert_eq(result.status, "partial")
        local text = joined(result)
        T.assert_contains(text, "character name=Silverleaf")
        T.assert_contains(text, "GetQuestList() err=quest api failed")
        T.assert_contains(text, "main_quest.snapshot unavailable")
    end)

    clear_modules()
    return T.report("aion_quest_snapshot")
end

return { run = run }
