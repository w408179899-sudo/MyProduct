local T = require("tests.test_framework")

local function clear_modules()
    package.loaded["aion.leveling_skill_auto"] = nil
end

local function load_module()
    clear_modules()
    return require("aion.leveling_skill_auto")
end

local function skill(id, name, type_id, level)
    return { id = id, name = name, type = type_id, level = level or 1 }
end

local function mock_combat(args)
    args = args or {}
    local calls = { toggles = {} }
    local combat = {
        KIND_SKILL = 0x15,
        skillList = function()
            return true, args.skills or {}, nil
        end,
        autoActiveSkills = function()
            return true, args.auto_active or {}, nil
        end,
        autoBuffSkills = function()
            return true, args.auto_buff or {}, nil
        end,
        isSkillAuto = function(id)
            if args.is_auto and args.is_auto[id] ~= nil then
                return true, args.is_auto[id], nil
            end
            return true, true, nil
        end,
        skillAutoToggle = function(id, kind)
            calls.toggles[#calls.toggles + 1] = { id = id, kind = kind }
            return true, true, nil
        end,
    }
    return combat, calls
end

local function mock_quickbar(args)
    args = args or {}
    local calls = {}
    local quickbar = {
        placeQuickbar = function(bar_index, slot_index, kind, id)
            calls[#calls + 1] = {
                bar_index = bar_index,
                slot_index = slot_index,
                kind = kind,
                id = id,
            }
            if args.error and args.error[id] then
                return false, nil, args.error[id]
            end
            return true, true, nil
        end,
    }
    return quickbar, calls
end

local function default_quickbar_opts(quickbar, qstate)
    return {
        quickbar = quickbar,
        quickbar_state = qstate or {},
        quickbar_bar_index = 1,
        quickbar_start_slot = 0,
        quickbar_slot_count = 12,
    }
end

local function run()
    T.reset()
    T.log("\n=== aion leveling skill auto quickbar tests ===")

    T.test("places missing skill on second row before auto toggle", function()
        local mod = load_module()
        local combat, calls = mock_combat({
            skills = { skill(101, "Slash", 2) },
            auto_active = {},
        })
        local quickbar, qcalls = mock_quickbar()
        local opts = default_quickbar_opts(quickbar)
        opts.reason = "startup"
        opts.level = 10

        local ok, result = mod.syncAutoActiveSkills(combat, opts)

        T.assert_eq(ok, true)
        T.assert_eq(result.quickbar_placed_count, 1)
        T.assert_eq(#qcalls, 1)
        T.assert_eq(qcalls[1].bar_index, 1)
        T.assert_eq(qcalls[1].slot_index, 0)
        T.assert_eq(#calls.toggles, 1)
        T.assert_eq(calls.toggles[1].id, 101)
    end)

    T.test("placement failure blocks auto toggle", function()
        local mod = load_module()
        local combat, calls = mock_combat({
            skills = { skill(101, "Slash", 2) },
            auto_active = {},
        })
        local quickbar = mock_quickbar({ error = { [101] = "place failed" } })
        local opts = default_quickbar_opts(quickbar)
        opts.reason = "startup"
        opts.level = 10

        local ok, result = mod.syncAutoActiveSkills(combat, opts)

        T.assert_eq(ok, false)
        T.assert_eq(result.quickbar_failed_count, 1)
        T.assert_eq(#calls.toggles, 0)
        T.assert_contains(table.concat(result.errors, "; "), "place failed")
    end)

    T.test("quickbar api is not required when no new skill needs adding", function()
        local mod = load_module()
        local combat = mock_combat({
            skills = { skill(101, "Already", 2) },
            auto_active = { 101 },
        })

        local ok, result = mod.syncAutoActiveSkills(combat, {
            reason = "startup",
            level = 10,
            quickbar = nil,
            quickbar_state = {},
        })

        T.assert_eq(ok, true)
        T.assert_eq(result.to_add_count, 0)
        T.assert_eq(result.added_count, 0)
    end)

    T.test("missing quickbar api fails when a new skill must be added", function()
        local mod = load_module()
        local combat = mock_combat({
            skills = { skill(101, "Slash", 2) },
            auto_active = {},
        })

        local ok, result = mod.syncAutoActiveSkills(combat, {
            reason = "startup",
            level = 10,
            quickbar = nil,
            quickbar_state = {},
        })

        T.assert_eq(ok, false)
        T.assert_eq(result.quickbar_failed_count, 1)
        T.assert_contains(table.concat(result.errors, "; "), "quickbar placement required")
    end)

    T.test("quickbar state skips occupied slots and records new slots", function()
        local mod = load_module()
        local combat = mock_combat({
            skills = { skill(101, "Slash", 2), skill(102, "Strike", 2) },
            auto_active = {},
        })
        local quickbar, qcalls = mock_quickbar()
        local qstate = {
            occupied_slots = { ["1:0"] = true },
            placed_by_id = {},
            next_slot = 0,
        }
        local opts = default_quickbar_opts(quickbar, qstate)
        opts.reason = "startup"
        opts.level = 10

        local ok, result = mod.syncAutoActiveSkills(combat, opts)

        T.assert_eq(ok, true)
        T.assert_eq(result.quickbar_placed_count, 2)
        T.assert_eq(#qcalls, 2)
        T.assert_eq(qcalls[1].slot_index, 1)
        T.assert_eq(qcalls[2].slot_index, 2)
        T.assert_eq(qstate.occupied_slots["1:1"], true)
        T.assert_eq(qstate.occupied_slots["1:2"], true)
    end)

    T.test("same skill group is placed only once", function()
        local mod = load_module()
        local combat, calls = mock_combat({
            skills = {
                skill(101, "Slash I", 2, 1),
                skill(102, "Slash II", 2, 2),
            },
            auto_active = {},
        })
        local quickbar, qcalls = mock_quickbar()
        local opts = default_quickbar_opts(quickbar)
        opts.reason = "startup"
        opts.level = 10

        local ok, result = mod.syncAutoActiveSkills(combat, opts)

        T.assert_eq(ok, true)
        T.assert_eq(result.to_add_count, 1)
        T.assert_eq(result.quickbar_placed_count, 1)
        T.assert_eq(#qcalls, 1)
        T.assert_eq(qcalls[1].id, 102)
        T.assert_eq(#calls.toggles, 1)
        T.assert_eq(calls.toggles[1].id, 102)
    end)

    clear_modules()
    return T.report("aion_leveling_skill_auto_quickbar")
end

return { run = run }
