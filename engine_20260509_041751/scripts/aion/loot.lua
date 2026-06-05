local core = require("aion.core")
local ui = require("aion.ui")
local data = core.data

local M = {}

function M.pickup(lootObj)
    return core.first("AionData.LootPickup", data.LootPickup, lootObj)
end

function M.pickupDialog(dialogName)
    local names = {}
    if dialogName and dialogName ~= "" then
        names[#names + 1] = dialogName
    else
        names[#names + 1] = "loot_dialog"
        names[#names + 1] = "dlg_loot"
    end

    local last_err = nil
    local last_value = nil
    for _, name in ipairs(names) do
        local direct_ok, direct_value, direct_err = M.pickup(name)
        if direct_ok and direct_value == true then
            return direct_ok, direct_value, direct_err
        end
        last_err = direct_err or last_err
        last_value = direct_value

        local ok, dlg, err = ui.find(name)
        if ok and dlg and dlg.addr and dlg.addr ~= 0 then
            local addr_ok, addr_value, addr_err = M.pickup(dlg.addr)
            if addr_ok and addr_value == true then
                return addr_ok, addr_value, addr_err
            end
            last_err = addr_err or last_err
            last_value = addr_value
        else
            last_err = err or last_err or ("loot dialog not found: " .. tostring(name))
        end
    end

    return false, last_value, last_err
end

return M
