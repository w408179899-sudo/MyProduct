local M = {
    KIND_SKILL = 0x15,
    DEFAULT_RETRY_SECONDS = 3.0,
}

local function number(value)
    return tonumber(value) or 0
end

local function trim(text)
    return tostring(text or ""):match("^%s*(.-)%s*$") or ""
end

local function skill_id(skill)
    if type(skill) ~= "table" then
        return 0
    end
    return number(skill.id or skill.skill_id or skill.skillId)
end

local function skill_name(skill)
    if type(skill) ~= "table" then
        return ""
    end
    return trim(skill.name or skill.skill_name or skill.skillName or skill.name_ko
        or skill.name_kr or skill.name_cn or skill.name_en)
end

local function skill_type(skill)
    if type(skill) ~= "table" then
        return 0
    end
    return number(skill.type or skill.typ or skill.skill_type or skill.skillType)
end

local function skill_level(skill)
    if type(skill) ~= "table" then
        return 0
    end
    return number(skill.level or skill.lv or skill.skill_level or skill.skillLevel
        or skill.skill_lv or skill.skillLv)
end

local function skill_learn_level(skill)
    if type(skill) ~= "table" then
        return 0
    end
    return number(skill.learn_level or skill.learnLevel
        or skill.required_level or skill.requiredLevel
        or skill.req_level or skill.reqLevel
        or skill.pc_level or skill.pcLevel)
end

local function skill_group_field(skill)
    if type(skill) ~= "table" then
        return ""
    end
    local keys = {
        "group_id",
        "groupId",
        "skill_group_id",
        "skillGroupId",
        "skill_group",
        "skillGroup",
        "base_skill_id",
        "baseSkillId",
        "base_id",
        "baseId",
        "root_skill_id",
        "rootSkillId",
        "root_id",
        "rootId",
        "series_id",
        "seriesId",
        "family_id",
        "familyId",
        "parent_skill_id",
        "parentSkillId",
    }
    for _, key in ipairs(keys) do
        local value = number(skill[key])
        if value > 0 then
            return key .. ":" .. tostring(value)
        end
    end
    return ""
end

local function truncate(text, limit)
    text = tostring(text or "")
    limit = number(limit)
    if limit <= 0 or #text <= limit then
        return text
    end
    return text:sub(1, limit) .. "..."
end

local function skill_scalar_fields(skill)
    if type(skill) ~= "table" then
        return ""
    end
    local keys = {}
    for key, value in pairs(skill) do
        local value_type = type(value)
        if value_type == "string" or value_type == "number" or value_type == "boolean" then
            keys[#keys + 1] = tostring(key)
        end
    end
    table.sort(keys)

    local parts = {}
    for _, key in ipairs(keys) do
        parts[#parts + 1] = key .. "=" .. truncate(skill[key], 80)
    end
    return truncate(table.concat(parts, ","), 900)
end

local function roman_rank_value(text)
    text = trim(text)
    local token = text:match("%s+([IVXLCDM]+)$")
        or text:match("%s+([ⅠⅡⅢⅣⅤⅥⅦⅧⅨⅩ]+)$")
        or text:match("%s+(%d+)$")
    if not token then
        return 0
    end
    local unicode = {
        ["Ⅰ"] = 1, ["Ⅱ"] = 2, ["Ⅲ"] = 3, ["Ⅳ"] = 4, ["Ⅴ"] = 5,
        ["Ⅵ"] = 6, ["Ⅶ"] = 7, ["Ⅷ"] = 8, ["Ⅸ"] = 9, ["Ⅹ"] = 10,
    }
    if unicode[token] then
        return unicode[token]
    end
    local numeric = tonumber(token)
    if numeric then
        return numeric
    end
    local values = { I = 1, V = 5, X = 10, L = 50, C = 100, D = 500, M = 1000 }
    local total, previous = 0, 0
    for i = #token, 1, -1 do
        local value = values[token:sub(i, i)] or 0
        if value < previous then
            total = total - value
        else
            total = total + value
            previous = value
        end
    end
    return total
end

local function skill_group_key(skill)
    local prefix = "type:" .. tostring(skill_type(skill)) .. "|"
    local field = skill_group_field(skill)
    if field ~= "" then
        return prefix .. field
    end
    local name = skill_name(skill)
    if name == "" then
        return prefix .. "id:" .. tostring(skill_id(skill))
    end
    repeat
        local updated = name:gsub("%s+%b()$", "")
        updated = updated:gsub("%s+%b[]$", "")
        updated = trim(updated)
        if updated == name then
            break
        end
        name = updated
    until false
    name = name:gsub("%s+[IVXLCDM]+$", "")
    name = name:gsub("%s+[ⅠⅡⅢⅣⅤⅥⅦⅧⅨⅩ]+$", "")
    name = name:gsub("%s+%d+$", "")
    name = name:gsub("%s+", " ")
    return prefix .. string.lower(trim(name))
end

local function skill_debug_text(skill, reason, group)
    return string.format(
        "%s id=%s name=%s type=%s level=%s learn=%s rank=%s group=%s fields={%s}",
        tostring(reason or ""),
        tostring(skill_id(skill)),
        truncate(skill_name(skill), 120),
        tostring(skill_type(skill)),
        tostring(skill_level(skill)),
        tostring(skill_learn_level(skill)),
        tostring(roman_rank_value(skill_name(skill))),
        truncate(group or skill_group_key(skill), 180),
        skill_scalar_fields(skill))
end

local function skill_preferred_over(candidate, current)
    if not current then
        return true
    end
    local c_level, old_level = skill_level(candidate), skill_level(current)
    if c_level ~= old_level then
        return c_level > old_level
    end
    local c_learn, old_learn = skill_learn_level(candidate), skill_learn_level(current)
    if c_learn ~= old_learn then
        return c_learn > old_learn
    end
    local c_rank, old_rank = roman_rank_value(skill_name(candidate)), roman_rank_value(skill_name(current))
    if c_rank ~= old_rank then
        return c_rank > old_rank
    end
    return skill_id(candidate) > skill_id(current)
end

local function append_token_set(set, text)
    text = tostring(text or "")
    for token in text:gmatch("[^\r\n,;]+") do
        token = trim(token)
        if token ~= "" then
            set[token] = true
            set[string.lower(token)] = true
        end
    end
end

local function ignored_token_set(opts)
    local set = {}
    opts = opts or {}
    append_token_set(set, opts.ignore_names)
    append_token_set(set, opts.ignore_ids)
    return set
end

local function list_to_id_set(list)
    local set = {}
    for _, item in ipairs(list or {}) do
        local id = type(item) == "table" and skill_id(item) or number(item)
        if id > 0 then
            set[id] = true
            set[tostring(id)] = true
        end
    end
    return set
end

local function is_ignored(skill, set)
    local id = skill_id(skill)
    local name = skill_name(skill)
    if id > 0 and (set[id] or set[tostring(id)]) then
        return true
    end
    if name ~= "" and (set[name] or set[string.lower(name)]) then
        return true
    end
    return false
end

local function quickbar_key(bar_index, slot_index)
    return tostring(number(bar_index)) .. ":" .. tostring(number(slot_index))
end

local function normalize_quickbar_state(state)
    state = type(state) == "table" and state or {}
    state.occupied_slots = type(state.occupied_slots) == "table" and state.occupied_slots or {}
    state.placed_by_id = type(state.placed_by_id) == "table" and state.placed_by_id or {}
    state.placed_by_group = type(state.placed_by_group) == "table" and state.placed_by_group or {}
    state.next_slots = type(state.next_slots) == "table" and state.next_slots or {}
    state.next_slot = number(state.next_slot)
    return state
end

local function find_quickbar_slot(qstate, opts)
    qstate = normalize_quickbar_state(qstate)
    opts = opts or {}
    local bar_index = number(opts.quickbar_bar_index)
    local start_slot = number(opts.quickbar_start_slot)
    local slot_count = number(opts.quickbar_slot_count)
    local bar_count = number(opts.quickbar_bar_count)
    if slot_count <= 0 then
        slot_count = 12
    end
    if bar_count <= 0 then
        bar_count = 1
    end
    for bar = bar_index, bar_index + bar_count - 1 do
        local next_slot = math.max(start_slot, number(qstate.next_slots[tostring(bar)]))
        if bar == bar_index then
            next_slot = math.max(next_slot, number(qstate.next_slot))
        end
        for slot = next_slot, start_slot + slot_count - 1 do
            local key = quickbar_key(bar, slot)
            if qstate.occupied_slots[key] ~= true then
                return bar, slot, key
            end
        end
    end
    return nil, nil, nil
end

function M.newRuntime()
    return {
        startup_sync_done = false,
        last_seen_level = 0,
        last_processed_level = 0,
        pending_level = 0,
        pending_reason = "",
        last_attempt_at = 0,
        last_success_at = 0,
        last_error = "",
        last_result = "",
        last_summary = {},
        quickbar = {
            next_slot = 0,
            occupied_slots = {},
            placed_by_id = {},
            placed_by_group = {},
            next_slots = {},
        },
    }
end

function M.resetRuntime(state)
    state = type(state) == "table" and state or {}
    local fresh = M.newRuntime()
    for key, value in pairs(fresh) do
        state[key] = value
    end
    return state
end

function M.detectPending(state, char, opts)
    state = type(state) == "table" and state or M.newRuntime()
    opts = opts or {}

    local level = number(char and char.level)
    if level <= 0 then
        return false, "no-level"
    end

    if number(state.last_seen_level) <= 0 then
        state.last_seen_level = level
    end

    if state.startup_sync_done ~= true and opts.startup_sync ~= false then
        state.pending_level = level
        state.pending_reason = "startup"
        return true, "startup"
    end

    if level > number(state.last_seen_level) then
        state.last_seen_level = level
        state.pending_level = level
        state.pending_reason = "level-up"
        return true, "level-up"
    end

    if number(state.pending_level) > number(state.last_processed_level) then
        return true, tostring(state.pending_reason or "pending")
    end

    if level > number(state.last_seen_level) then
        state.last_seen_level = level
    end
    return false, "idle"
end

function M.canAttempt(state, now, retry_seconds)
    state = type(state) == "table" and state or {}
    if number(state.pending_level) <= 0 then
        return false, "no-pending"
    end
    retry_seconds = math.max(0.2, number(retry_seconds) > 0 and number(retry_seconds) or M.DEFAULT_RETRY_SECONDS)
    now = number(now)
    if number(state.last_attempt_at) > 0 and now - number(state.last_attempt_at) < retry_seconds then
        return false, "cooldown"
    end
    return true, "ready"
end

function M.planAutoActiveSkills(skills, current_auto_active, opts)
    opts = opts or {}
    local current = list_to_id_set(current_auto_active)
    local ignored = ignored_token_set(opts)
    local plan = {
        to_add = {},
        errors = {},
        stats = {
            learned = 0,
            active_type = 0,
            already = 0,
            duplicate_group = 0,
            ignored = 0,
            not_auto = 0,
            check_failed = 0,
            invalid = 0,
            candidates = 0,
        },
    }
    if opts.debug == true then
        plan.debug = { lines = {} }
    end

    local function debug_line(text)
        if plan.debug then
            plan.debug.lines[#plan.debug.lines + 1] = tostring(text or "")
        end
    end

    local selected_by_group = {}
    local group_order = {}
    for _, skill in ipairs(skills or {}) do
        plan.stats.learned = plan.stats.learned + 1
        local id = skill_id(skill)
        if id <= 0 then
            plan.stats.invalid = plan.stats.invalid + 1
            debug_line(skill_debug_text(skill, "skip invalid-id", ""))
        elseif opts.require_active_type ~= false and skill_type(skill) ~= 2 then
            -- The leveling caller can include buff/status skills by disabling this gate.
            debug_line(skill_debug_text(skill, "skip type-gate", skill_group_key(skill)))
        else
            plan.stats.active_type = plan.stats.active_type + 1
            local group = skill_group_key(skill)
            if not selected_by_group[group] then
                group_order[#group_order + 1] = group
                selected_by_group[group] = skill
                debug_line(skill_debug_text(skill, "group-select first", group))
            elseif skill_preferred_over(skill, selected_by_group[group]) then
                plan.stats.duplicate_group = plan.stats.duplicate_group + 1
                debug_line("group-replace group=" .. truncate(group, 180) ..
                    " old={" .. skill_debug_text(selected_by_group[group], "old", group) ..
                    "} new={" .. skill_debug_text(skill, "new", group) .. "}")
                selected_by_group[group] = skill
            else
                plan.stats.duplicate_group = plan.stats.duplicate_group + 1
                debug_line("group-drop-lower group=" .. truncate(group, 180) ..
                    " kept={" .. skill_debug_text(selected_by_group[group], "kept", group) ..
                    "} drop={" .. skill_debug_text(skill, "drop", group) .. "}")
            end
        end
    end

    for _, group in ipairs(group_order) do
        local skill = selected_by_group[group]
        local id = skill_id(skill)
        if current[id] or current[tostring(id)] then
            plan.stats.already = plan.stats.already + 1
            debug_line(skill_debug_text(skill, "skip already-auto", group))
        elseif is_ignored(skill, ignored) then
            plan.stats.ignored = plan.stats.ignored + 1
            debug_line(skill_debug_text(skill, "skip ignored", group))
        else
            local allowed = true
            if type(opts.is_skill_auto) == "function" then
                local ok, value, err = opts.is_skill_auto(id)
                allowed = ok == true and value == true
                if ok ~= true then
                    plan.stats.check_failed = plan.stats.check_failed + 1
                    plan.errors[#plan.errors + 1] = "IsSkillAuto failed id=" .. tostring(id) ..
                        " err=" .. tostring(err or "")
                    debug_line(skill_debug_text(skill, "skip auto-check-failed err=" .. tostring(err or ""), group))
                elseif value ~= true then
                    plan.stats.not_auto = plan.stats.not_auto + 1
                    debug_line(skill_debug_text(skill, "skip not-auto-capable", group))
                end
            end
            if allowed then
                plan.stats.candidates = plan.stats.candidates + 1
                plan.to_add[#plan.to_add + 1] = skill
                debug_line(skill_debug_text(skill, "candidate to-add", group))
            end
        end
    end

    return plan
end

function M.syncAutoActiveSkills(combat, opts)
    opts = opts or {}
    local result = {
        status = "failed",
        reason = tostring(opts.reason or ""),
        level = number(opts.level),
        learned_count = 0,
        current_auto_active_count = 0,
        current_auto_buff_count = 0,
        to_add_count = 0,
        added_count = 0,
        failed_count = 0,
        quickbar_required = opts.quickbar_required ~= false,
        quickbar_placed_count = 0,
        quickbar_reused_count = 0,
        quickbar_failed_count = 0,
        errors = {},
        toggled = {},
        quickbar_placed = {},
        stats = {},
        debug = { lines = {} },
    }

    if type(combat) ~= "table" then
        result.errors[#result.errors + 1] = "combat wrapper unavailable"
        return false, result
    end
    if type(combat.skillList) ~= "function"
        or type(combat.autoActiveSkills) ~= "function"
        or type(combat.autoBuffSkills) ~= "function"
        or type(combat.isSkillAuto) ~= "function"
        or type(combat.skillAutoToggle) ~= "function" then
        result.errors[#result.errors + 1] = "combat skill APIs unavailable"
        return false, result
    end

    local skills_ok, skills, skills_err = combat.skillList()
    if not skills_ok then
        result.errors[#result.errors + 1] = "skillList failed: " .. tostring(skills_err or "")
        return false, result
    end
    local active_ok, active, active_err = combat.autoActiveSkills()
    if not active_ok then
        result.errors[#result.errors + 1] = "autoActiveSkills failed: " .. tostring(active_err or "")
        return false, result
    end
    local buff_ok, buff, buff_err = combat.autoBuffSkills()
    if not buff_ok then
        result.errors[#result.errors + 1] = "autoBuffSkills failed: " .. tostring(buff_err or "")
        return false, result
    end

    skills = skills or {}
    active = active or {}
    buff = buff or {}
    result.learned_count = #skills
    result.current_auto_active_count = #active
    result.current_auto_buff_count = #buff

    local current_auto = {}
    for _, item in ipairs(active) do
        current_auto[#current_auto + 1] = item
    end
    for _, item in ipairs(buff) do
        current_auto[#current_auto + 1] = item
    end

    local plan = M.planAutoActiveSkills(skills, current_auto, {
        ignore_names = opts.ignore_names,
        ignore_ids = opts.ignore_ids,
        require_active_type = opts.require_active_type,
        debug = opts.debug == true,
        is_skill_auto = function(id)
            return combat.isSkillAuto(id)
        end,
    })
    result.stats = plan.stats
    result.debug = type(plan.debug) == "table" and plan.debug or { lines = {} }
    local function debug_line(text)
        if result.debug and type(result.debug.lines) == "table" then
            result.debug.lines[#result.debug.lines + 1] = tostring(text or "")
        end
    end
    result.to_add_count = #plan.to_add
    for _, err in ipairs(plan.errors) do
        result.errors[#result.errors + 1] = err
    end

    local kind = opts.kind or combat.KIND_SKILL or M.KIND_SKILL
    local qstate = normalize_quickbar_state(opts.quickbar_state)
    local quickbar = opts.quickbar
    if result.quickbar_required
        and #plan.to_add > 0
        and (type(quickbar) ~= "table" or type(quickbar.placeQuickbar) ~= "function") then
        result.errors[#result.errors + 1] = "quickbar placement required but API unavailable"
        result.quickbar_failed_count = #plan.to_add
        result.status = "partial-failure"
        return false, result
    end

    for _, skill in ipairs(plan.to_add) do
        local id = skill_id(skill)
        local group = skill_group_key(skill)
        local placed_slot = qstate.placed_by_id[tostring(id)] or qstate.placed_by_group[group]
        if result.quickbar_required and not placed_slot then
            local bar, slot, key = find_quickbar_slot(qstate, opts)
            if not slot then
                result.quickbar_failed_count = result.quickbar_failed_count + 1
                result.errors[#result.errors + 1] = "no reserved quickbar slot left id=" .. tostring(id)
                debug_line(skill_debug_text(skill, "quickbar no-slot", group))
            else
                local qok, qvalue, qerr = quickbar.placeQuickbar(
                    bar,
                    slot,
                    kind,
                    id)
                if qok == true and qvalue ~= false then
                    qstate.occupied_slots[key] = true
                    qstate.placed_by_id[tostring(id)] = {
                        bar_index = bar,
                        slot_index = slot,
                    }
                    qstate.placed_by_group[group] = qstate.placed_by_id[tostring(id)]
                    qstate.next_slots[tostring(bar)] = slot + 1
                    if bar == number(opts.quickbar_bar_index) then
                        qstate.next_slot = slot + 1
                    end
                    result.quickbar_placed_count = result.quickbar_placed_count + 1
                    result.quickbar_placed[#result.quickbar_placed + 1] = {
                        id = id,
                        name = skill_name(skill),
                        group = group,
                        bar_index = bar,
                        slot_index = slot,
                    }
                    debug_line(skill_debug_text(skill, "quickbar placed bar=" ..
                        tostring(bar) .. " slot=" .. tostring(slot), group))
                    placed_slot = qstate.placed_by_id[tostring(id)]
                else
                    result.quickbar_failed_count = result.quickbar_failed_count + 1
                    result.errors[#result.errors + 1] = "PlaceQuickbar failed id=" .. tostring(id) ..
                        " bar=" .. tostring(bar) ..
                        " slot=" .. tostring(slot) ..
                        " err=" .. tostring(qerr or qvalue or "")
                    debug_line(skill_debug_text(skill, "quickbar place-failed bar=" ..
                        tostring(bar) .. " slot=" .. tostring(slot) ..
                        " err=" .. tostring(qerr or qvalue or ""), group))
                end
            end
        elseif result.quickbar_required and placed_slot then
            result.quickbar_reused_count = result.quickbar_reused_count + 1
            debug_line(skill_debug_text(skill, "quickbar reused bar=" ..
                tostring(placed_slot.bar_index or "") .. " slot=" .. tostring(placed_slot.slot_index or ""), group))
        end

        if result.quickbar_required and not placed_slot then
            result.failed_count = result.failed_count + 1
        else
        local ok, value, err = combat.skillAutoToggle(id, kind)
        if ok == true and value ~= false then
            result.added_count = result.added_count + 1
            result.toggled[#result.toggled + 1] = {
                id = id,
                name = skill_name(skill),
                group = group,
            }
            debug_line(skill_debug_text(skill, "toggle success", group))
        else
            result.failed_count = result.failed_count + 1
            result.errors[#result.errors + 1] = "SkillAutoToggle failed id=" .. tostring(id) ..
                " err=" .. tostring(err or value or "")
            debug_line(skill_debug_text(skill, "toggle failed err=" .. tostring(err or value or ""), group))
        end
        end
    end

    local failed = result.failed_count > 0
        or result.quickbar_failed_count > 0
        or number(result.stats.check_failed) > 0
    result.status = failed and "partial-failure" or "success"
    return not failed, result
end

function M.finishAttempt(state, ok, result, now)
    state = type(state) == "table" and state or M.newRuntime()
    result = type(result) == "table" and result or {}
    now = number(now)
    local pending_level = number(state.pending_level)
    local reason = tostring(state.pending_reason or "")

    state.last_summary = result
    state.last_result = result.status or (ok and "success" or "failed")
    if ok then
        state.startup_sync_done = true
        state.last_processed_level = math.max(number(state.last_processed_level), pending_level)
        state.last_seen_level = math.max(number(state.last_seen_level), pending_level)
        state.pending_level = 0
        state.pending_reason = ""
        state.last_success_at = now
        state.last_error = ""
    else
        state.pending_level = pending_level
        state.pending_reason = reason ~= "" and reason or "retry"
        state.last_error = table.concat(result.errors or {}, "; ")
    end
    return state
end

function M.formatResult(result)
    result = type(result) == "table" and result or {}
    local stats = type(result.stats) == "table" and result.stats or {}
    return string.format(
        "reason=%s level=%s status=%s learned=%d active=%d buff=%d active_type=%d candidates=%d to_add=%d quickbar(placed=%d reused=%d failed=%d) added=%d failed=%d skipped(already=%d duplicate_group=%d ignored=%d not_auto=%d check_failed=%d invalid=%d)",
        tostring(result.reason or ""),
        tostring(result.level or ""),
        tostring(result.status or ""),
        number(result.learned_count),
        number(result.current_auto_active_count),
        number(result.current_auto_buff_count),
        number(stats.active_type),
        number(stats.candidates),
        number(result.to_add_count),
        number(result.quickbar_placed_count),
        number(result.quickbar_reused_count),
        number(result.quickbar_failed_count),
        number(result.added_count),
        number(result.failed_count),
        number(stats.already),
        number(stats.duplicate_group),
        number(stats.ignored),
        number(stats.not_auto),
        number(stats.check_failed),
        number(stats.invalid))
end

return M
