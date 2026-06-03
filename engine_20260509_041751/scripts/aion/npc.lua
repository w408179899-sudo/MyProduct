local core = require("aion.core")
local entity = require("aion.entity")
local ui = require("aion.ui")
local data = core.data

local M = {
    DIALOG_BUY = -2,
    DIALOG_SELL = -3,
}

function M.interactId(interactId)
    return core.first("AionData.InteractNpc", data.InteractNpc, interactId)
end

function M.interactByName(name)
    local ok, npc, err = entity.nearestNpc(name)
    if not ok then
        return false, nil, err
    end
    if not npc then
        return false, nil, "npc not found: " .. tostring(name)
    end
    return M.interactId(npc.interact_id)
end

function M.dialog()
    local ok, dlg, err = ui.find("dlg_dialog")
    if not ok then
        return false, nil, err
    end
    if not dlg or not dlg.addr or dlg.addr == 0 or dlg.visible == false then
        return true, nil, nil
    end
    return core.first("AionData.GetNpcDialogInfo", data.GetNpcDialogInfo, dlg.addr)
end

function M.waitDialog(timeoutMs)
    return core.waitUntil("dlg_dialog", function()
        local ok, info = M.dialog()
        if ok and info then
            return info
        end
        return nil
    end, timeoutMs or 3000, 100)
end

function M.sendDialog(info, nextDialogId, questId)
    if not info then
        return false, nil, "dialog info is nil"
    end
    return core.first(
        "AionData.SendNpcDialog",
        data.SendNpcDialog,
        info.npc_dialog_id,
        nextDialogId or 0,
        info.dialog_content_id,
        questId or info.quest_id or 0
    )
end

function M.openShopByName(name, mode)
    local ok, _, err = M.interactByName(name)
    if not ok then
        return false, nil, err
    end

    local waitOk, infoOrErr = M.waitDialog(3000)
    if not waitOk then
        return false, nil, infoOrErr
    end

    local nextId = mode == "sell" and M.DIALOG_SELL or M.DIALOG_BUY
    return M.sendDialog(infoOrErr, nextId)
end

return M
