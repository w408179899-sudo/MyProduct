local Store = {}

local CONFIG_KEY = "maple.accounts"

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
        items = {}
    }
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
    root.items = root.items or {}
    for i, account in ipairs(root.items) do
        root.items[i] = normalize_account(account, i)
    end
    return root
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
