local Store = {}

local CONFIG_KEY = "maple.accounts"

local DEFAULT_SKILL_RELEASE = {
    skill_use_method = "press_key",
    skill_key = "Shift",
    skill_key_code = 0x10,
    skill_input_mode = "foreground",
    quickslot_use_trusted = false,
    fallback_to_basic_attack = true
}

local function backend()
    return Store.backend or config
end

local function default_root()
    return {
        max_parallel = 1,
        auto_start_after_login = false,
        auto_relogin_on_disconnect = false,
        auto_relogin_cooldown_seconds = 30,
        auto_relogin_max_attempts = 0,
        poll_interval = 2.0,
        game_path = "",
        launcher_path = "",
        captcha_key = "",
        profiles = {
            default = DEFAULT_SKILL_RELEASE
        },
        items = {}
    }
end

local function normalize_key_code(value, default_value)
    if type(value) == "string" then
        return tonumber(value) or tonumber(value:match("^0[xX](%x+)$"), 16) or default_value
    end
    return tonumber(value) or default_value
end

local function normalize_skill_release(settings, defaults)
    settings = type(settings) == "table" and settings or {}
    defaults = defaults or DEFAULT_SKILL_RELEASE
    return {
        skill_use_method = tostring(settings.skill_use_method or settings.method or defaults.skill_use_method),
        skill_key = tostring(settings.skill_key or settings.key_name or defaults.skill_key),
        skill_key_code = normalize_key_code(settings.skill_key_code or settings.key_code, defaults.skill_key_code),
        skill_input_mode = tostring(settings.skill_input_mode or settings.input_mode or defaults.skill_input_mode),
        skill_hold_ms = math.max(0, tonumber(settings.skill_hold_ms or settings.hold_ms or defaults.skill_hold_ms or 0) or 0),
        quickslot_use_trusted = settings.quickslot_use_trusted == true,
        fallback_to_basic_attack = settings.fallback_to_basic_attack ~= false
    }
end

local function copy_optional_skill_release(target, source)
    source = type(source) == "table" and source or {}
    if source.skill_use_method ~= nil then target.skill_use_method = tostring(source.skill_use_method) end
    if source.method ~= nil then target.skill_use_method = tostring(source.method) end
    if source.skill_key ~= nil then target.skill_key = tostring(source.skill_key) end
    if source.key_name ~= nil then target.skill_key = tostring(source.key_name) end
    if source.skill_key_code ~= nil then target.skill_key_code = normalize_key_code(source.skill_key_code, target.skill_key_code) end
    if source.key_code ~= nil then target.skill_key_code = normalize_key_code(source.key_code, target.skill_key_code) end
    if source.skill_input_mode ~= nil then target.skill_input_mode = tostring(source.skill_input_mode) end
    if source.input_mode ~= nil then target.skill_input_mode = tostring(source.input_mode) end
    if source.skill_hold_ms ~= nil then target.skill_hold_ms = math.max(0, tonumber(source.skill_hold_ms) or 0) end
    if source.hold_ms ~= nil then target.skill_hold_ms = math.max(0, tonumber(source.hold_ms) or 0) end
    if source.quickslot_use_trusted ~= nil then target.quickslot_use_trusted = source.quickslot_use_trusted == true end
    if source.fallback_to_basic_attack ~= nil then target.fallback_to_basic_attack = source.fallback_to_basic_attack ~= false end
    return target
end

local function normalize_profiles(profiles)
    profiles = type(profiles) == "table" and profiles or {}
    profiles.default = normalize_skill_release(profiles.default, DEFAULT_SKILL_RELEASE)
    for name, profile in pairs(profiles) do
        profiles[name] = normalize_skill_release(profile, profiles.default)
    end
    return profiles
end

local function normalize_account(account, index)
    account = account or {}
    account.key = tostring(account.key or account.account or ("account_" .. tostring(index or 0)))
    account.enabled = account.enabled ~= false
    account.account = tostring(account.account or "")
    account.password = tostring(account.password or "")
    account.second_password = tostring(account.second_password or "")
    account.server = tostring(account.server or "")
    account.character_name = tostring(account.character_name or "")
    account.profile = tostring(account.profile or "default")
    account.task = tostring(account.task or "main")
    account.route = tostring(account.route or "")
    account.note = tostring(account.note or "")
    account.smart_combat_enabled = account.smart_combat_enabled == true
    if account.combat_logic_mode == nil then
        account.combat_logic_mode = account.smart_combat_enabled and "predictive" or "immediate"
    else
        account.combat_logic_mode = tostring(account.combat_logic_mode)
    end
    account.skill_release = account.skill_release
    if account.skill_use_method ~= nil then account.skill_use_method = tostring(account.skill_use_method) end
    if account.skill_key ~= nil then account.skill_key = tostring(account.skill_key) end
    if account.skill_key_code ~= nil then account.skill_key_code = normalize_key_code(account.skill_key_code, DEFAULT_SKILL_RELEASE.skill_key_code) end
    if account.skill_input_mode ~= nil then account.skill_input_mode = tostring(account.skill_input_mode) end
    if account.skill_hold_ms ~= nil then account.skill_hold_ms = math.max(0, tonumber(account.skill_hold_ms) or 0) end
    if account.quickslot_use_trusted ~= nil then account.quickslot_use_trusted = account.quickslot_use_trusted == true end
    if account.fallback_to_basic_attack ~= nil then account.fallback_to_basic_attack = account.fallback_to_basic_attack ~= false end
    account.runtime = account.runtime or {}
    account.audit = account.audit or {}
    return account
end

local function normalize_root(root)
    local defaults = default_root()
    root = type(root) == "table" and root or {}
    for key, value in pairs(defaults) do
        if root[key] == nil then root[key] = value end
    end
    root.max_parallel = math.max(1, tonumber(root.max_parallel) or 1)
    root.auto_start_after_login = root.auto_start_after_login == true
    root.auto_relogin_on_disconnect = root.auto_relogin_on_disconnect == true
    root.auto_relogin_cooldown_seconds = math.max(1, tonumber(root.auto_relogin_cooldown_seconds) or 30)
    root.auto_relogin_max_attempts = math.max(0, tonumber(root.auto_relogin_max_attempts) or 0)
    root.poll_interval = math.max(0.2, tonumber(root.poll_interval) or 2.0)
    root.profiles = normalize_profiles(root.profiles)
    root.items = root.items or {}
    for i, account in ipairs(root.items) do
        root.items[i] = normalize_account(account, i)
    end
    return root
end

function Store.default_skill_release()
    return normalize_skill_release(DEFAULT_SKILL_RELEASE, DEFAULT_SKILL_RELEASE)
end

function Store.resolve_skill_release(root, account)
    root = normalize_root(root)
    account = normalize_account(account or {}, 1)
    local release = normalize_skill_release(root.profiles.default, DEFAULT_SKILL_RELEASE)
    local profile = account.profile or "default"
    if root.profiles and root.profiles[profile] then
        release = normalize_skill_release(root.profiles[profile], release)
    end
    copy_optional_skill_release(release, account.skill_release)
    copy_optional_skill_release(release, account)
    return release
end

function Store.set_backend(b)
    Store.backend = b
end

function Store.load()
    local b = backend()
    if b and b.load then pcall(b.load) end
    return normalize_root(b and b.get and b.get(CONFIG_KEY, nil) or nil)
end

function Store.save(root)
    local b = backend()
    if not b or not b.set then return false, "config_unavailable" end
    root = normalize_root(root)
    b.set(CONFIG_KEY, root)
    if b.save then return b.save() end
    return true
end

function Store.new_account(fields)
    fields = fields or {}
    return normalize_account({
        key = fields.key,
        enabled = fields.enabled ~= false,
        account = fields.account or "",
        password = fields.password or "",
        second_password = fields.second_password or "",
        server = fields.server or "",
        character_name = fields.character_name or "",
        profile = fields.profile or "default",
        task = fields.task or "main",
        route = fields.route or "",
        note = fields.note or "",
        smart_combat_enabled = fields.smart_combat_enabled == true,
        combat_logic_mode = fields.combat_logic_mode,
        skill_release = fields.skill_release,
        skill_use_method = fields.skill_use_method,
        skill_key = fields.skill_key,
        skill_key_code = fields.skill_key_code,
        skill_input_mode = fields.skill_input_mode,
        skill_hold_ms = fields.skill_hold_ms,
        quickslot_use_trusted = fields.quickslot_use_trusted,
        fallback_to_basic_attack = fields.fallback_to_basic_attack,
        runtime = {}
    })
end

function Store.add(root, account)
    root.items = root.items or {}
    account = normalize_account(account, #root.items + 1)
    root.items[#root.items + 1] = account
    return account, #root.items
end

function Store.remove(root, index)
    if not root.items or not root.items[index] then return false end
    table.remove(root.items, index)
    return true
end

function Store.get(root, index)
    return root.items and root.items[index] or nil
end

function Store.status_key(index, name)
    return string.format("maple.account.%d.%s", tonumber(index) or 0, name)
end

return Store
