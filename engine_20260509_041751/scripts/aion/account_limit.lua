local M = {}

function M.limitFromCard(card)
    local text = tostring(card or "")
    local digits = string.match(text, "^YY(%d%d)")
    local limit = tonumber(digits)
    if limit and limit > 0 then
        return limit
    end
    return nil
end

function M.currentLimit(config_module)
    local cfg = config_module or config
    if not cfg or type(cfg.get) ~= "function" then
        return nil
    end

    local ok, card = pcall(cfg.get, "savedUserCard", "")
    if not ok then
        card = ""
    end
    return M.limitFromCard(card)
end

function M.canAdd(items, config_module)
    local limit = M.currentLimit(config_module)
    if not limit then
        return true, nil, 0
    end

    local count = 0
    if type(items) == "table" then
        count = #items
    end

    return count < limit, limit, count
end

function M.filterLoginItems(items, config_module, source_index_fn)
    local limit = M.currentLimit(config_module)
    if not limit or type(items) ~= "table" then
        return items, {}, limit
    end

    local allowed = {}
    local blocked = {}
    for loop_index, account in ipairs(items) do
        local source_index = loop_index
        if type(source_index_fn) == "function" then
            source_index = tonumber(source_index_fn(loop_index, account)) or loop_index
        elseif type(account) == "table" then
            source_index = tonumber(account.__index) or loop_index
        end

        if source_index <= limit then
            allowed[#allowed + 1] = account
        else
            blocked[#blocked + 1] = {
                account = account,
                source_index = source_index,
            }
        end
    end

    return allowed, blocked, limit
end

return M
