local Store = {}

local CONFIG_KEY = "maple.accounts"

local function backend()
    return Store.backend or config
end

local function default_root()
    return {
        max_parallel = 1,
        auto_start_after_login = false,
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
    account.runtime = account.runtime or {}
    return account
end

function Store.set_backend(b)
    Store.backend = b
end

function Store.load()
    local b = backend()
    if b and b.load then pcall(b.load) end
    local root = b and b.get and b.get(CONFIG_KEY, nil) or nil
    if type(root) ~= "table" then root = default_root() end
    root.items = root.items or {}
    for i, account in ipairs(root.items) do
        root.items[i] = normalize_account(account, i)
    end
    return root
end

function Store.save(root)
    local b = backend()
    if not b or not b.set then return false, "config_unavailable" end
    root = root or default_root()
    root.items = root.items or {}
    for i, account in ipairs(root.items) do
        root.items[i] = normalize_account(account, i)
    end
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
