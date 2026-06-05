local core = require("aion.core")
local ui = require("aion.ui")
local data = core.data

local M = {}

function M.pickup(lootObj)
    return core.first("AionData.LootPickup", data.LootPickup, lootObj)
end

function M.pickupDialog(dialogName)
    dialogName = dialogName or "dlg_loot"
    local direct_ok, direct_value, direct_err = M.pickup(dialogName)
    if direct_ok and direct_value == true then
        return direct_ok, direct_value, direct_err
    end

    local ok, dlg, err = ui.find(dialogName)
    if not ok then
        return false, nil, direct_err or err
    end
    if not dlg or not dlg.addr or dlg.addr == 0 then
        return false, nil, direct_err or ("loot dialog not found: " .. tostring(dialogName))
    end

    local addr_ok, addr_value, addr_err = M.pickup(dlg.addr)
    if addr_ok and addr_value == true then
        return addr_ok, addr_value, addr_err
    end
    return false, addr_value, addr_err or direct_err
end

return M
