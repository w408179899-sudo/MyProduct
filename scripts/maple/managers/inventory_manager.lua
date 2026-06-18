local M = {}

function M.is_full(bb)
    local max_slots = tonumber(bb.inventory.max_slots) or 1
    local used = tonumber(bb.inventory.used_slots) or 0
    return used / max_slots >= 0.90
end

function M.classify_item(item)
    if item.keep == true then return "keep" end
    if item.discard == true then return "discard" end
    return "sell"
end

function M.get_sellable_items(bb)
    local out = {}
    for _, item in ipairs(bb.inventory.items or {}) do
        if M.classify_item(item) == "sell" then out[#out + 1] = item end
    end
    return out
end

return M
