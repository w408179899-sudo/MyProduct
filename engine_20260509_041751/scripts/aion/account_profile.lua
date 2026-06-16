local profile_io = require("aion.profile_io")

local M = {}

M.kind = "aion_account_profile"
M.default_root = "profiles/accounts"

M.private_top_level_keys = {
    "profile_name",
    "primary_mode",
    "priority_mode",
}

M.private_domain_keys = {
    "combat",
    "gather",
    "skills",
    "character",
    "route",
    "leveling",
    "npc_dialog",
    "crafting",
    "supply",
    "safety",
    "audit",
    "transfer",
    "test",
}

M.shared_route_keys = {
    "route_name",
    "revive_route_name",
    "vendor_route_name",
    "gather_route_name",
    "leveling_route_name",
    "route_points",
    "revive_points",
    "vendor_points",
    "gather_points",
    "leveling_points",
    "saved_routes",
}

local shared_route_lookup = {}
for _, key in ipairs(M.shared_route_keys) do
    shared_route_lookup[key] = true
end

local function clone(value)
    return profile_io.clone(value)
end

local function merge(dst, src)
    return profile_io.merge(dst, src)
end

local function trim(value)
    local text = tostring(value or "")
    text = text:gsub("^%s+", "")
    text = text:gsub("%s+$", "")
    return text
end

local function safe_slug(value)
    local text = trim(value):lower()
    text = text:gsub("[^%w_%-%.]+", "_")
    text = text:gsub("_+", "_")
    text = text:gsub("^_+", "")
    text = text:gsub("_+$", "")
    if text == "" then
        text = "account"
    end
    return text
end

local function pick_source_name(account, index)
    account = account or {}
    local candidates = {
        account.label,
        account.account,
        account.target and account.target.character_name,
        account.character and account.character.character_name,
        tostring(index or 0),
    }
    for _, value in ipairs(candidates) do
        local text = trim(value)
        if text ~= "" and text ~= "0" then
            return text
        end
    end
    return "account"
end

function M.ensureProfileKey(account, index)
    if type(account) ~= "table" then
        return nil
    end
    local key = trim(account.profile_key)
    if key == "" then
        key = string.format("acc_%03d_%s", tonumber(index) or 0, safe_slug(pick_source_name(account, index)))
        account.profile_key = key
    end
    return key
end

function M.profilePath(account, index, root)
    local key = M.ensureProfileKey(account, index)
    if not key then
        return nil
    end
    root = trim(root)
    if root == "" then
        root = M.default_root
    end
    return root .. "/" .. key .. ".lua"
end

function M.sharedRouteFromConfig(config)
    local route = config and config.route or {}
    local out = {}
    for _, key in ipairs(M.shared_route_keys) do
        out[key] = clone(route[key])
    end
    return out
end

function M.applySharedRoute(config, shared_route)
    config.route = config.route or {}
    for _, key in ipairs(M.shared_route_keys) do
        if shared_route and shared_route[key] ~= nil then
            config.route[key] = clone(shared_route[key])
        end
    end
    return config
end

function M.privateFromConfig(config)
    local profile = {}
    if type(config) ~= "table" then
        return profile
    end

    for _, key in ipairs(M.private_top_level_keys) do
        profile[key] = clone(config[key])
    end

    for _, key in ipairs(M.private_domain_keys) do
        if key == "route" then
            local route = {}
            for route_key, value in pairs(config.route or {}) do
                if not shared_route_lookup[route_key] then
                    route[route_key] = clone(value)
                end
            end
            profile.route = route
        elseif config[key] ~= nil then
            profile[key] = clone(config[key])
        end
    end

    return profile
end

function M.buildEffectiveConfig(default_config, private_profile, shared_route, account)
    local effective = clone(default_config or {})
    effective.accounts = nil
    merge(effective, private_profile or {})
    M.applySharedRoute(effective, shared_route or M.sharedRouteFromConfig(default_config or {}))

    if account and account.target then
        effective.target = clone(account.target)
    end

    return effective
end

function M.splitEffectiveConfig(effective_config)
    return M.privateFromConfig(effective_config), M.sharedRouteFromConfig(effective_config)
end

function M.load(path)
    local ok, package, err = profile_io.readPackage(path, M.kind)
    if not ok then
        return false, nil, err
    end
    return true, package.payload or {}, nil
end

function M.save(path, private_profile)
    return profile_io.writePackage(path, M.kind, private_profile or {})
end

function M.mergeSharedRouteIntoConfig(config, shared_route)
    config.route = config.route or {}
    for _, key in ipairs(M.shared_route_keys) do
        if shared_route and shared_route[key] ~= nil then
            config.route[key] = clone(shared_route[key])
        end
    end
    return config
end

return M
