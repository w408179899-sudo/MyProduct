local core = require("aion.core")
local data = core.data

local M = {}

function M.list()
    local ok, list, err = core.first("AionData.GetShopItems", data.GetShopItems)
    if not ok then
        return false, nil, err
    end
    return true, list or {}, nil
end

function M.staticList(race)
    if type(data.GetShopItemsStatic) == "function" then
        return core.first("AionData.GetShopItemsStatic", data.GetShopItemsStatic, race)
    end

    local ok, char, err = core.getCharacter()
    if not ok then
        return false, nil, err
    end
    local key = char and char.race == 1 and "asmodian" or "elyos"
    return true, data.SHOP_ITEMS and data.SHOP_ITEMS[key] or {}, nil
end

function M.findByName(name)
    local ok, list, err = M.list()
    if not ok then
        return false, nil, err
    end

    for _, item in ipairs(list) do
        if item.name == name then
            return true, item, nil
        end
    end
    return true, nil, nil
end

function M.price(priceBase)
    return core.first("AionData.GetShopItemPrice", data.GetShopItemPrice, priceBase)
end

function M.buy(interactId, itemId, subId, count)
    return core.first("AionData.BuyShopItem", data.BuyShopItem, interactId, itemId, subId, count)
end

function M.buyByName(name, count)
    local ok, item, err = M.findByName(name)
    if not ok then
        return false, nil, err
    end
    if not item then
        return false, nil, "shop item not found: " .. tostring(name)
    end
    return M.buy(item.interact_id, item.id, item.sub_id, count or 1)
end

return M
