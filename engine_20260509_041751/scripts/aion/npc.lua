local core = require("aion.core")
local entity = require("aion.entity")
local ui = require("aion.ui")
local data = core.data

local M = {
    DIALOG_BUY = -2,
    DIALOG_SELL = -3,
}

local function number(value, fallback)
    local n = tonumber(value)
    if n == nil then
        return fallback
    end
    return n
end

local function childObj(child)
    if type(child) ~= "table" then
        return 0
    end
    return tonumber(child.obj or child.addr) or 0
end

local function copyChild(child, index, count)
    local result = {}
    for key, value in pairs(child or {}) do
        result[key] = value
    end
    result.option_index = index
    result.option_count = count
    return result
end

local function isDialogOption(child, targetX, tolerance, minY, maxY)
    if type(child) ~= "table" or child.visible ~= true or childObj(child) <= 0 then
        return false
    end

    local x = tonumber(child.x)
    local y = tonumber(child.y)
    if not x or not y then
        return false
    end

    if math.abs(x - targetX) > tolerance then
        return false
    end
    if minY and y < minY then
        return false
    end
    if maxY and y > maxY then
        return false
    end
    return true
end

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

function M.dialogOptions(opts)
    opts = type(opts) == "table" and opts or {}
    local depth = math.max(1, number(opts.depth or opts.max_depth, 6))
    local targetX = number(opts.click_x or opts.x, 25)
    local tolerance = math.max(0, number(opts.click_x_tolerance or opts.x_tolerance, 2))
    local minY = tonumber(opts.min_y)
    local maxY = tonumber(opts.max_y)

    local ok, children, err = ui.children(opts.parent or "dlg_dialog", depth)
    if not ok then
        return false, nil, err
    end

    local options = {}
    for _, child in ipairs(children or {}) do
        if isDialogOption(child, targetX, tolerance, minY, maxY) then
            options[#options + 1] = child
        end
    end

    table.sort(options, function(a, b)
        local ay = tonumber(a and a.y) or 0
        local by = tonumber(b and b.y) or 0
        if ay == by then
            return (tonumber(a and a.x) or 0) < (tonumber(b and b.x) or 0)
        end
        return ay < by
    end)

    return true, options, nil
end

function M.dialogOption(index, opts)
    local ok, options, err = M.dialogOptions(opts)
    if not ok then
        return false, nil, err
    end
    if #options <= 0 then
        return false, nil, "no clickable dialog options"
    end

    local idx = tonumber(index) or #options
    if idx < 1 or idx > #options then
        return false, nil, string.format("dialog option index %d out of range, count=%d", idx, #options)
    end

    return true, copyChild(options[idx], idx, #options), nil
end

function M.lastDialogOption(opts)
    return M.dialogOption(nil, opts)
end

function M.clickDialogOption(index, opts)
    local ok, option, err = M.dialogOption(index, opts)
    if not ok then
        return false, nil, err
    end

    local clickOk, clicked, clickErr = ui.click(childObj(option))
    if not clickOk or clicked == false then
        return false, option, clickErr or tostring(clicked)
    end

    return true, option, nil
end

function M.clickLastDialogOption(opts)
    return M.clickDialogOption(nil, opts)
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
