local core = require("aion.core")
local data = core.data

local M = {
    KIND_ITEM = 0x1,
    KIND_SKILL = 0x15,
}

function M.pressKey(keycode)
    return core.first("AionData.PressKey", data.PressKey, keycode)
end

function M.placeQuickbar(barIndex, slotIndex, kind, id)
    return core.first("AionData.PlaceQuickbar", data.PlaceQuickbar, barIndex, slotIndex, kind, id)
end

function M.returnCharacter()
    return core.first("AionData.ReturnCharacter", data.ReturnCharacter)
end

return M
