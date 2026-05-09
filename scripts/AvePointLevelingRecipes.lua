local M = {}

M.VERSION = 1

local function clone_table(value)
    if type(value) ~= "table" then
        return value
    end
    local out = {}
    for k, v in pairs(value) do
        out[k] = clone_table(v)
    end
    return out
end

local function merge_into(dst, src)
    if type(src) ~= "table" then
        return dst
    end
    for k, v in pairs(src) do
        dst[k] = clone_table(v)
    end
    return dst
end

local function normalize_text(value)
    if value == nil then
        return nil
    end
    local text = tostring(value)
    text = text:gsub("^%s+", "")
    text = text:gsub("%s+$", "")
    if text == "" then
        return nil
    end
    return text
end

local function normalize_steps(steps)
    local out = {}
    if type(steps) ~= "table" then
        return out
    end
    if type(steps[1]) == "table" then
        for _, step in ipairs(steps) do
            out[#out + 1] = clone_table(step)
        end
    else
        out[1] = clone_table(steps)
    end
    return out
end

function M.normalize_step(step)
    if type(step) ~= "table" then
        return nil
    end
    local normalized = clone_table(step)
    normalized.kind = tostring(normalized.kind or "")
    return normalized
end

function M.make_step(kind, opts)
    local step = {
        kind = tostring(kind or "")
    }
    merge_into(step, opts)
    return step
end

function M.make_recipe(opts)
    opts = type(opts) == "table" and opts or {}
    local recipe = {
        mode = "task_recipe",
        version = M.VERSION,
        key = tostring(opts.key or ""),
        enabled = opts.enabled ~= false,
        priority = tonumber(opts.priority) or 0,
        steps = normalize_steps(opts.steps),
        success = type(opts.success) == "table" and clone_table(opts.success) or {
            mode = "task_info_changed",
            vacuum_ms = 5000,
            settle_ms = 1200
        }
    }
    merge_into(recipe, opts)
    recipe.mode = "task_recipe"
    recipe.version = M.VERSION
    recipe.steps = normalize_steps(opts.steps)
    return recipe
end

function M.is_recipe(value)
    return type(value) == "table"
        and tostring(value.mode or "") == "task_recipe"
        and value.enabled ~= false
        and type(value.steps) == "table"
        and #value.steps > 0
end

function M.recipe_type(recipe)
    if type(recipe) ~= "table" then
        return ""
    end
    return tostring(recipe.recipe_type or recipe.type or "")
end

function M.step_at(recipe, index)
    if not M.is_recipe(recipe) then
        return nil
    end
    local step = recipe.steps[math.max(1, tonumber(index) or 1)]
    return M.normalize_step(step)
end

function M.describe_step(step)
    if type(step) ~= "table" then
        return "step=<nil>"
    end
    return string.format(
        "kind=%s key=%s label=%s",
        tostring(step.kind or ""),
        tostring(step.key or ""),
        tostring(step.label or "")
    )
end

function M.current_task_recipe(task_cfg, opts)
    opts = type(opts) == "table" and opts or {}
    local matches = opts.matches_task_constraints
    local function recipe_matches(recipe)
        if not M.is_recipe(recipe) then
            return false
        end
        if type(matches) == "function" then
            return matches(recipe)
        end
        return true
    end

    if type(task_cfg) ~= "table" then
        return nil, nil, "task_cfg_unavailable"
    end

    local recipe = task_cfg.recipe
    if recipe_matches(recipe) then
        return recipe, tostring(recipe.key or task_cfg.key or ""), "task_cfg_recipe"
    end

    if type(task_cfg.recipes) == "table" then
        local best = nil
        for _, item in ipairs(task_cfg.recipes) do
            if recipe_matches(item) then
                if best == nil or (tonumber(item.priority) or 0) > (tonumber(best.priority) or 0) then
                    best = item
                end
            end
        end
        if best ~= nil then
            return best, tostring(best.key or task_cfg.key or ""), "task_cfg_recipes"
        end
    end

    return nil, nil, "no_task_recipe"
end

function M.build_intent_candidate(recipe, matched_source, matched_key, reason)
    if not M.is_recipe(recipe) then
        return nil
    end
    return {
        kind = "task_recipe",
        matched_source = tostring(matched_source or "task_recipe"),
        matched_key = tostring(matched_key or recipe.key or ""),
        config = recipe,
        reason = tostring(reason or "task_recipe_matched")
    }
end

function M.describe_recipe(recipe)
    if type(recipe) ~= "table" then
        return "recipe=<nil>"
    end
    return string.format(
        "key=%s steps=%d success=%s",
        tostring(recipe.key or ""),
        type(recipe.steps) == "table" and #recipe.steps or 0,
        tostring(type(recipe.success) == "table" and recipe.success.mode or "")
    )
end

return M
