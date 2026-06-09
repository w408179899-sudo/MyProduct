local ok_core, default_core = pcall(require, "aion.core")
local ok_combat, default_combat = pcall(require, "aion.combat")
local ok_entity, default_entity = pcall(require, "aion.entity")

local M = {}

local function value_or_empty(value)
    if value == nil then
        return ""
    end
    return tostring(value)
end

local function number_or_zero(value)
    return tonumber(value) or 0
end

local function format_number(value, digits)
    value = tonumber(value)
    if value == nil then
        return ""
    end
    return string.format("%." .. tostring(tonumber(digits) or 1) .. "f", value)
end

local function object_id(value)
    if type(value) ~= "table" then
        return 0
    end
    return tonumber(value.obj)
        or tonumber(value.IEntity)
        or tonumber(value.addr)
        or 0
end

local function target_id(value)
    if type(value) ~= "table" then
        return 0
    end
    return tonumber(value.id) or 0
end

local function distance3(char, entity, core)
    if type(char) ~= "table" or type(entity) ~= "table" then
        return nil
    end
    if core and type(core.distance3) == "function" then
        local ok, dist = pcall(core.distance3, char, entity)
        if ok and tonumber(dist) then
            return tonumber(dist)
        end
    end
    local dx = number_or_zero(char.x) - number_or_zero(entity.x)
    local dy = number_or_zero(char.y) - number_or_zero(entity.y)
    local dz = number_or_zero(char.z) - number_or_zero(entity.z)
    return math.sqrt(dx * dx + dy * dy + dz * dz)
end

function M.entityObject(entity)
    return object_id(entity)
end

function M.targetObject(target)
    return object_id(target)
end

function M.findEntityForTarget(target, list)
    local target_obj = M.targetObject(target)
    local current_id = target_id(target)

    if target_obj > 0 then
        for _, entity in ipairs(list or {}) do
            if M.entityObject(entity) == target_obj then
                return entity, "obj"
            end
        end
    end

    if current_id > 0 then
        for _, entity in ipairs(list or {}) do
            if target_id(entity) == current_id then
                return entity, "id"
            end
        end
    end

    return nil, nil
end

function M.kind(entity)
    if type(entity) ~= "table" then
        return "unknown"
    end
    if entity.is_self == true then
        return "self"
    end

    local tag = tostring(entity.tag or "")
    if tag ~= "" then
        return tag
    end

    local type_name = tostring(entity.type_name or "")
    if type_name ~= "" then
        return type_name
    end

    local type_value = tonumber(entity.type)
    if type_value == 1 then
        return "item"
    elseif type_value == 2 then
        return "creature"
    elseif type_value ~= nil then
        return "type-" .. tostring(type_value)
    end
    return "unknown"
end

function M.formatResult(target, entity, opts)
    opts = type(opts) == "table" and opts or {}
    local list_count = tonumber(opts.list_count) or 0
    local match_key = tostring(opts.match_key or "")
    local matched = type(entity) == "table"
    local lines = {}

    lines[#lines + 1] = string.format(
        "current obj=%s id=%s matched=%s match=%s entity_count=%s list_err=%s",
        value_or_empty(M.targetObject(target)),
        value_or_empty(target_id(target)),
        tostring(matched),
        match_key,
        tostring(list_count),
        value_or_empty(opts.list_err))

    if not matched then
        lines[#lines + 1] = "detail target not found in GetAroundList"
        return {
            status = "target_unmatched",
            summary = "target unmatched obj=" .. value_or_empty(M.targetObject(target)) .. " id=" .. value_or_empty(target_id(target)),
            lines = lines,
        }
    end

    local dist = distance3(opts.char, entity, opts.core)
    lines[#lines + 1] = string.format(
        "detail name=%s kind=%s tag=%s type=%s type_name=%s ct=%s id=%s obj=%s hp=%s/%s level=%s dist=%s pos=%s,%s,%s lootable=%s dead=%s interact_id=%s rating=%s mutant=%s flags=%s race=%s job=%s",
        value_or_empty(entity.name),
        M.kind(entity),
        value_or_empty(entity.tag),
        value_or_empty(entity.type),
        value_or_empty(entity.type_name),
        value_or_empty(entity.ct),
        value_or_empty(entity.id),
        value_or_empty(M.entityObject(entity)),
        value_or_empty(entity.hp),
        value_or_empty(entity.mhp or entity.max_hp),
        value_or_empty(entity.level),
        format_number(dist, 2),
        format_number(entity.x, 2),
        format_number(entity.y, 2),
        format_number(entity.z, 2),
        value_or_empty(entity.lootable),
        tostring(entity.dead == true),
        value_or_empty(entity.interact_id),
        value_or_empty(entity.rating),
        tostring(entity.is_mutant == true),
        value_or_empty(entity.flags),
        value_or_empty(entity.race),
        value_or_empty(entity.job))

    return {
        status = "target_matched",
        summary = string.format(
            "%s kind=%s obj=%s id=%s",
            value_or_empty(entity.name),
            M.kind(entity),
            value_or_empty(M.entityObject(entity)),
            value_or_empty(entity.id)),
        lines = lines,
    }
end

function M.read(deps)
    deps = type(deps) == "table" and deps or {}
    local core = deps.core or (ok_core and default_core or nil)
    local combat = deps.combat or (ok_combat and default_combat or nil)
    local entity = deps.entity or (ok_entity and default_entity or nil)

    if not combat or type(combat.currentTarget) ~= "function" then
        return false, nil, "aion.combat.currentTarget unavailable"
    end

    local target_ok, target, target_err = combat.currentTarget()
    if not target_ok then
        return false, nil, tostring(target_err or "GetCurrentTarget failed")
    end
    if type(target) ~= "table" or (M.targetObject(target) <= 0 and target_id(target) <= 0) then
        return true, {
            status = "no_target",
            summary = "no selected target",
            lines = { "current none" },
        }, nil
    end

    local char = nil
    if core and type(core.getCharacter) == "function" then
        local char_ok, char_value = core.getCharacter()
        if char_ok then
            char = char_value
        end
    end

    local list = {}
    local list_err = nil
    if entity and type(entity.list) == "function" then
        local list_ok, list_value, err = entity.list()
        if list_ok and type(list_value) == "table" then
            list = list_value
        else
            list_err = tostring(err or "GetAroundList failed")
        end
    else
        list_err = "aion.entity.list unavailable"
    end

    local matched, match_key = M.findEntityForTarget(target, list)
    return true, M.formatResult(target, matched, {
        char = char,
        core = core,
        list_count = #list,
        list_err = list_err,
        match_key = match_key,
    }), nil
end

return M
