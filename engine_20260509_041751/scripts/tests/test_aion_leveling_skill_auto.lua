local T = require("tests.test_framework")

local function clear_modules()
    package.loaded["aion.leveling_skill_auto"] = nil
end

local function load_module()
    clear_modules()
    return require("aion.leveling_skill_auto")
end

local function skill(id, name, type_id, level, extra)
    local item = {
        id = id,
        name = name,
        type = type_id,
        level = level or 1,
    }
    for key, value in pairs(extra or {}) do
        item[key] = value
    end
    return item
end

local function mock_combat(args)
    args = args or {}
    local calls = {
        toggles = {},
        is_auto = {},
    }
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
            calls.is_auto[#calls.is_auto + 1] = id
            if args.is_auto_error and args.is_auto_error[id] then
                return false, nil, args.is_auto_error[id]
            end
            if args.is_auto and args.is_auto[id] ~= nil then
                return true, args.is_auto[id], nil
            end
            return true, true, nil
        end,
        skillAutoToggle = function(id, kind)
            calls.toggles[#calls.toggles + 1] = { id = id, kind = kind }
            if args.toggle_error and args.toggle_error[id] then
                return false, nil, args.toggle_error[id]
            end
            return true, true, nil
        end,
    }
    return combat, calls
end

local function run()
    T.reset()
    T.log("\n=== aion leveling skill auto tests ===")

    T.test("startup sync is pending for first valid character level", function()
        local mod = load_module()
        local state = mod.newRuntime()
        local pending, reason = mod.detectPending(state, { level = 10 }, { startup_sync = true })

        T.assert_eq(pending, true)
        T.assert_eq(reason, "startup")
        T.assert_eq(state.pending_level, 10)
        T.assert_eq(state.pending_reason, "startup")
    end)

    T.test("successful startup marks startup done and prevents duplicate same-level sync", function()
        local mod = load_module()
        local state = mod.newRuntime()
        mod.detectPending(state, { level = 10 }, { startup_sync = true })
        mod.finishAttempt(state, true, { status = "success" }, 100)

        T.assert_eq(state.startup_sync_done, true)
        T.assert_eq(state.last_processed_level, 10)
        T.assert_eq(state.pending_level, 0)

        local pending = mod.detectPending(state, { level = 10 }, { startup_sync = true })
        T.assert_eq(pending, false)
    end)

    T.test("level up triggers one pending sync after startup", function()
        local mod = load_module()
        local state = mod.newRuntime()
        mod.detectPending(state, { level = 10 }, { startup_sync = true })
        mod.finishAttempt(state, true, { status = "success" }, 100)

        local pending, reason = mod.detectPending(state, { level = 11 }, { startup_sync = true })
        T.assert_eq(pending, true)
        T.assert_eq(reason, "level-up")
        T.assert_eq(state.pending_level, 11)
    end)

    T.test("failed sync keeps pending level for retry", function()
        local mod = load_module()
        local state = mod.newRuntime()
        mod.detectPending(state, { level = 10 }, { startup_sync = true })
        mod.finishAttempt(state, false, { status = "failed", errors = { "x" } }, 100)

        T.assert_eq(state.startup_sync_done, false)
        T.assert_eq(state.pending_level, 10)
        T.assert_contains(state.last_error, "x")
    end)

    T.test("retry cooldown blocks repeated attempts", function()
        local mod = load_module()
        local state = mod.newRuntime()
        state.pending_level = 10
        state.last_attempt_at = 100

        local ready, reason = mod.canAttempt(state, 101, 3)
        T.assert_eq(ready, false)
        T.assert_eq(reason, "cooldown")

        ready = mod.canAttempt(state, 104, 3)
        T.assert_eq(ready, true)
    end)

    T.test("plan only adds missing active auto-capable skills", function()
        local mod = load_module()
        local skills = {
            skill(101, "Slash", 2),
            skill(102, "Already", 2),
            skill(103, "Passive", 8),
            skill(104, "ManualOnly", 2),
            skill(105, "IgnoredName", 2),
        }

        local plan = mod.planAutoActiveSkills(skills, { 102 }, {
            ignore_names = "IgnoredName",
            is_skill_auto = function(id)
                return true, id ~= 104, nil
            end,
        })

        T.assert_eq(#plan.to_add, 1)
        T.assert_eq(plan.to_add[1].id, 101)
        T.assert_eq(plan.stats.already, 1)
        T.assert_eq(plan.stats.ignored, 1)
        T.assert_eq(plan.stats.not_auto, 1)
    end)

    T.test("plan keeps only highest level skill per skill group", function()
        local mod = load_module()
        local skills = {
            skill(101, "Slash I", 2, 1),
            skill(102, "Slash II", 2, 2),
            skill(103, "Strike", 2, 1),
        }

        local plan = mod.planAutoActiveSkills(skills, {}, {
            is_skill_auto = function()
                return true, true, nil
            end,
        })

        T.assert_eq(#plan.to_add, 2)
        T.assert_eq(plan.to_add[1].id, 102)
        T.assert_eq(plan.to_add[2].id, 103)
        T.assert_eq(plan.stats.duplicate_group, 1)
    end)

    T.test("plan prefers highest level skill from explicit skill group field", function()
        local mod = load_module()
        local skills = {
            skill(201, "Old Slash", 2, 1, { skill_group_id = 9001 }),
            skill(202, "New Slash", 2, 4, { skill_group_id = 9001 }),
            skill(203, "Strike", 2, 1, { skill_group_id = 9002 }),
        }

        local plan = mod.planAutoActiveSkills(skills, {}, {
            is_skill_auto = function()
                return true, true, nil
            end,
        })

        T.assert_eq(#plan.to_add, 2)
        T.assert_eq(plan.to_add[1].id, 202)
        T.assert_eq(plan.to_add[2].id, 203)
        T.assert_eq(plan.stats.duplicate_group, 1)
    end)

    T.test("plan uses required level to choose latest skill when rank level is absent", function()
        local mod = load_module()
        local skills = {
            skill(301, "Old Chain", 2, 0, { group_id = 7001, required_level = 5 }),
            skill(302, "New Chain", 2, 0, { group_id = 7001, required_level = 13 }),
        }

        local plan = mod.planAutoActiveSkills(skills, {}, {
            is_skill_auto = function()
                return true, true, nil
            end,
        })

        T.assert_eq(#plan.to_add, 1)
        T.assert_eq(plan.to_add[1].id, 302)
        T.assert_eq(plan.stats.duplicate_group, 1)
    end)

    T.test("plan groups rank suffix before trailing action tag", function()
        local mod = load_module()
        local skills = {
            skill(311, "Drain I (Action)", 2, 1),
            skill(312, "Drain II (Action)", 2, 2),
            skill(313, "Drain III (Action)", 2, 3),
        }

        local plan = mod.planAutoActiveSkills(skills, {}, {
            is_skill_auto = function()
                return true, true, nil
            end,
        })

        T.assert_eq(#plan.to_add, 1)
        T.assert_eq(plan.to_add[1].id, 313)
        T.assert_eq(plan.stats.duplicate_group, 2)
    end)

    T.test("plan keeps active and status skills separate even with same group name", function()
        local mod = load_module()
        local skills = {
            skill(321, "Shared Aura I", 2, 1),
            skill(322, "Shared Aura I", 8, 1),
        }

        local plan = mod.planAutoActiveSkills(skills, {}, {
            require_active_type = false,
            is_skill_auto = function()
                return true, true, nil
            end,
        })

        T.assert_eq(#plan.to_add, 2)
        T.assert_eq(plan.to_add[1].id, 321)
        T.assert_eq(plan.to_add[2].id, 322)
        T.assert_eq(plan.stats.duplicate_group, 0)
    end)

    T.test("sync returns debug lines when requested", function()
        local mod = load_module()
        local combat = mock_combat({
            skills = {
                skill(401, "Old Slash", 2, 1, { skill_group_id = 9101 }),
                skill(402, "New Slash", 2, 3, { skill_group_id = 9101 }),
            },
            auto_active = {},
        })

        local ok, result = mod.syncAutoActiveSkills(combat, {
            reason = "level-up",
            level = 12,
            quickbar_required = false,
            debug = true,
        })

        T.assert_eq(ok, true)
        T.assert_eq(result.to_add_count, 1)
        T.assert_eq(result.toggled[1].id, 402)
        T.assert_eq(type(result.debug.lines), "table")
        T.assert_gt(#result.debug.lines, 0, "expected debug lines")
        T.assert_contains(table.concat(result.debug.lines, "\n"), "group-replace")
        T.assert_contains(table.concat(result.debug.lines, "\n"), "toggle success")
    end)

    T.test("sync toggles missing active and status skills", function()
        local mod = load_module()
        local combat, calls = mock_combat({
            skills = {
                skill(101, "Slash", 2),
                skill(102, "Already", 2),
                skill(103, "BuffLike", 8),
            },
            auto_active = { 102 },
        })

        local ok, result = mod.syncAutoActiveSkills(combat, {
            reason = "startup",
            level = 10,
            quickbar_required = false,
            require_active_type = false,
        })
        T.assert_eq(ok, true)
        T.assert_eq(result.added_count, 2)
        T.assert_eq(result.to_add_count, 2)
        T.assert_eq(#calls.toggles, 2)
        T.assert_eq(calls.toggles[1].id, 101)
        T.assert_eq(calls.toggles[2].id, 103)
    end)

    T.test("sync does not toggle status skill already in auto buff list", function()
        local mod = load_module()
        local combat, calls = mock_combat({
            skills = {
                skill(101, "Slash", 2),
                skill(103, "BuffLike", 8),
            },
            auto_active = {},
            auto_buff = { 103 },
        })

        local ok, result = mod.syncAutoActiveSkills(combat, {
            reason = "startup",
            level = 10,
            quickbar_required = false,
            require_active_type = false,
        })
        T.assert_eq(ok, true)
        T.assert_eq(result.current_auto_buff_count, 1)
        T.assert_eq(result.added_count, 1)
        T.assert_eq(result.to_add_count, 1)
        T.assert_eq(#calls.toggles, 1)
        T.assert_eq(calls.toggles[1].id, 101)
    end)

    T.test("sync returns failure when toggle fails", function()
        local mod = load_module()
        local combat = mock_combat({
            skills = { skill(101, "Slash", 2) },
            auto_active = {},
            toggle_error = { [101] = "toggle failed" },
        })

        local ok, result = mod.syncAutoActiveSkills(combat, {
            reason = "level-up",
            level = 11,
            quickbar_required = false,
        })
        T.assert_eq(ok, false)
        T.assert_eq(result.failed_count, 1)
        T.assert_contains(table.concat(result.errors, "; "), "toggle failed")
    end)

    T.test("sync returns failure when auto capability check fails", function()
        local mod = load_module()
        local combat = mock_combat({
            skills = { skill(101, "Slash", 2) },
            auto_active = {},
            is_auto_error = { [101] = "check failed" },
        })

        local ok, result = mod.syncAutoActiveSkills(combat, {
            reason = "level-up",
            level = 11,
            quickbar_required = false,
        })
        T.assert_eq(ok, false)
        T.assert_eq(result.stats.check_failed, 1)
        T.assert_contains(table.concat(result.errors, "; "), "check failed")
    end)

    clear_modules()
    return T.report("aion_leveling_skill_auto")
end

return { run = run }
