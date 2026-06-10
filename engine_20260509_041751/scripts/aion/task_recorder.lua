local M = {}

local function trim(text)
    return tostring(text or ""):gsub("^%s+", ""):gsub("%s+$", "")
end

local function clean_text(text)
    text = tostring(text or "")
    text = text:gsub("%c", " ")
    text = text:gsub("%[INFO%].*$", "")
    text = text:gsub("%s+", " ")
    return trim(text)
end

local function clip_text(text, limit)
    text = clean_text(text)
    limit = tonumber(limit) or 0
    if limit > 0 and #text > limit then
        return text:sub(1, limit) .. "...(truncated)"
    end
    return text
end

local function value(value)
    if value == nil then
        return ""
    end
    return clean_text(value)
end

local function number(value, fallback)
    local n = tonumber(value)
    if n == nil then
        return fallback or 0
    end
    return n
end

local function format_number(raw, digits)
    local n = tonumber(raw)
    if n == nil then
        return clean_text(raw)
    end
    digits = tonumber(digits) or 2
    if digits <= 0 then
        return string.format("%.0f", n)
    end
    return string.format("%." .. tostring(digits) .. "f", n)
end

local function append(lines, line, limit)
    lines[#lines + 1] = clip_text(line or "", limit or 360)
end

local function child_obj(child)
    if type(child) ~= "table" then
        return 0
    end
    return tonumber(child.obj or child.addr) or 0
end

local function child_name(child)
    local name = clean_text(child and child.name or "")
    if name == "" then
        return "(no-name)"
    end
    return name
end

local function child_line(index, child)
    return string.format(
        "child[%02d] depth=%s obj=%s name=%s visible=%s x=%s y=%s",
        tonumber(index) or 0,
        value(child and child.depth),
        value(child_obj(child)),
        child_name(child),
        tostring(child and child.visible == true),
        format_number(child and child.x, 2),
        format_number(child and child.y, 2))
end

local function child_matches_x(child, target_x, tolerance)
    if type(child) ~= "table" or child.visible ~= true or child_obj(child) <= 0 then
        return false
    end
    local x = tonumber(child.x)
    if x == nil then
        return false
    end
    return math.abs(x - target_x) <= tolerance
end

local function find_child_by_x(children, target_x, tolerance)
    for index, child in ipairs(children or {}) do
        if child_matches_x(child, target_x, tolerance) then
            return child, index
        end
    end
    return nil, 0
end

local function find_child_by_name(children, name)
    name = tostring(name or "")
    for index, child in ipairs(children or {}) do
        if type(child) == "table"
            and child.visible == true
            and child_obj(child) > 0
            and tostring(child.name or "") == name then
            return child, index
        end
    end
    return nil, 0
end

local function stable_snapshot_line(line)
    line = clean_text(line)
    line = line:gsub(" tab_name=[^%s]*", "")
    line = line:gsub(" status_name=[^%s]*", "")
    line = line:gsub(" current_status=[^%s]*", "")
    line = line:gsub(" lv_text=[^%s]*", "")
    line = line:gsub(" name_cn=[^%s]*", "")
    line = line:gsub(" region=[^%s]*", "")
    line = line:gsub(" current_name=.+$", "")
    line = line:gsub(" name=.+$", "")
    return line
end

local function stable_target_line(line)
    line = clean_text(line)
    line = line:gsub(" name=[^%s]*", "")
    line = line:gsub(" type_name=[^%s]*", "")
    return line
end

local function first_capture(lines, pattern)
    for _, raw in ipairs(type(lines) == "table" and lines or {}) do
        local matched = clean_text(raw):match(pattern)
        if matched and matched ~= "" then
            return matched
        end
    end
    return ""
end

local function task_key(snapshot, target, dialog, action_hint)
    local snapshot_lines = type(snapshot.lines) == "table" and snapshot.lines or {}
    local target_lines = type(target.lines) == "table" and target.lines or {}
    local dialog_state = type(dialog) == "table" and "open" or "closed"
    local dialog_type = type(dialog) == "table" and value(dialog.type_text) or ""
    local content_id = type(dialog) == "table" and value(dialog.dialog_content_id) or ""
    local quest_id = first_capture(snapshot_lines, "current_id=([^%s]+)")
    local quest_step = first_capture(snapshot_lines, "current_step=([^%s]+)")

    return "task_key quest_id=" .. value(quest_id) ..
        " step=" .. value(quest_step) ..
        " map_id=" .. value(first_capture(snapshot_lines, "big_map_id=([^%s]+)")) ..
        " char_pos=" .. value(first_capture(snapshot_lines, "pos=([^%s]+)")) ..
        " target_interact_id=" .. value(first_capture(target_lines, "interact_id=([^%s]+)")) ..
        " target_dist=" .. value(first_capture(target_lines, "dist=([^%s]+)")) ..
        " target_pos=" .. value(first_capture(target_lines, "pos=([^%s]+)")) ..
        " dialog=" .. dialog_state ..
        " dialog_type=" .. value(dialog_type) ..
        " content_id=" .. value(content_id) ..
        " action_hint=" .. value(action_hint)
end

function M.dialogSummary(dialog)
    if type(dialog) ~= "table" then
        return "dialog=closed"
    end
    return string.format(
        "dialog=open npc_dialog_id=%s content_id=%s quest_id=%s type=%s next=%s has_next=%s",
        value(dialog.npc_dialog_id),
        value(dialog.dialog_content_id),
        value(dialog.quest_id),
        value(dialog.type_text),
        value(dialog.next_text or dialog.next_dialog_id or dialog.next),
        value(dialog.has_next))
end

function M.actionHint(dialog, children, target_result, opts)
    opts = type(opts) == "table" and opts or {}
    local target_x = number(opts.dialog_click_x, 25)
    local tolerance = number(opts.dialog_click_x_tolerance, 2)

    if type(dialog) == "table" then
        local ok_child, ok_index = find_child_by_name(children, "ok")
        if ok_child then
            return "dialog_click_ok child_index=" .. tostring(ok_index)
        end

        local accept_child, accept_index = find_child_by_name(children, "accept")
        if accept_child then
            return "dialog_click_accept child_index=" .. tostring(accept_index)
        end

        local x_child, x_index = find_child_by_x(children, target_x, tolerance)
        if x_child then
            return "dialog_click_x child_index=" .. tostring(x_index) .. " x=" .. tostring(target_x)
        end

        return "dialog_unknown_dump"
    end

    target_result = type(target_result) == "table" and target_result or {}
    if tostring(target_result.status or "") == "target_matched" then
        return "target_interact_or_move"
    end
    return "snapshot_only"
end

function M.build(args)
    args = type(args) == "table" and args or {}
    local snapshot = type(args.snapshot) == "table" and args.snapshot or {}
    local target = type(args.target) == "table" and args.target or {}
    local dialog = args.dialog
    local dialog_error = trim(args.dialog_error or "")
    local children = type(args.dialog_children) == "table" and args.dialog_children or {}
    local opts = type(args.opts) == "table" and args.opts or {}
    local lines = {}
    local action_hint = M.actionHint(dialog, children, target, opts)

    append(lines, "record version=1 source=F11")
    append(lines, "summary snapshot_status=" .. value(snapshot.status) ..
        " snapshot=" .. value(snapshot.summary) ..
        " target_status=" .. value(target.status) ..
        " target=" .. value(target.summary) ..
        " " .. M.dialogSummary(dialog) ..
        " action_hint=" .. action_hint, 520)
    append(lines, task_key(snapshot, target, dialog, action_hint), 520)

    append(lines, "")
    append(lines, "snapshot status=" .. value(snapshot.status) .. " summary=" .. value(snapshot.summary))
    for _, line in ipairs(type(snapshot.lines) == "table" and snapshot.lines or {}) do
        append(lines, "snapshot." .. stable_snapshot_line(line), 260)
    end

    append(lines, "")
    append(lines, "target status=" .. value(target.status) .. " summary=" .. value(target.summary))
    for _, line in ipairs(type(target.lines) == "table" and target.lines or {}) do
        append(lines, "target." .. stable_target_line(line), 260)
    end

    append(lines, "")
    append(lines, M.dialogSummary(dialog))
    if dialog_error ~= "" then
        append(lines, "dialog.read_error=" .. dialog_error)
    end
    if type(dialog) == "table" then
        append(lines, "dialog.content_text=" .. value(dialog.content_text))
        append(lines, "dialog.next_text=" .. value(dialog.next_text or dialog.next_dialog_id or dialog.next))
    end

    local child_limit = math.max(0, number(opts.dialog_child_limit, 40))
    append(lines, "dialog.children count=" .. tostring(#children) .. " logged=" .. tostring(math.min(#children, child_limit)))
    for index, child in ipairs(children) do
        if index > child_limit then
            break
        end
        append(lines, "dialog." .. child_line(index, child))
    end

    append(lines, "")
    append(lines, "copy_hint use this F11 block to build task actions: talk_npc, move_to, kill_mob, loot_item, interact_object, wait_progress")

    return {
        status = "ok",
        summary = action_hint,
        lines = lines,
    }
end

return M
