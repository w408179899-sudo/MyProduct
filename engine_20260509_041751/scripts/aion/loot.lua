local core = require("aion.core")
local entity = require("aion.entity")
local nav = require("aion.nav")
local npc = require("aion.npc")
local combat = require("aion.combat")
local remote = require("aion.remote")
local ui = require("aion.ui")
local data = core.data

local M = {}

local default_dialog_names = { "loot_dialog", "dlg_loot" }

local function now_ms(opts)
    if opts and type(opts.now_ms) == "function" then
        return tonumber(opts.now_ms()) or core.nowMs()
    end
    return core.nowMs()
end

local function now_seconds(opts)
    if opts and type(opts.now_seconds) == "function" then
        return tonumber(opts.now_seconds()) or os.time()
    end
    return os.time()
end

local function sleep_ms(opts, ms)
    if opts and type(opts.sleep) == "function" then
        opts.sleep(ms)
        return
    end
    core.sleep(ms)
end

local function emit(opts, event, message)
    if opts and type(opts.log) == "function" then
        pcall(opts.log, event, message)
        return
    end
    if log and type(log.info) == "function" then
        log.info("[AionLoot] " .. tostring(event) .. " " .. tostring(message or ""))
    end
end

local function dialog_names(dialogName)
    if type(dialogName) == "table" then
        return dialogName
    end
    if dialogName and dialogName ~= "" then
        return { dialogName }
    end
    return default_dialog_names
end

local function ui_obj(ctrl)
    if type(ctrl) ~= "table" then
        return tonumber(ctrl) or ctrl
    end
    return ctrl.obj or ctrl.addr
end

local function entity_obj(e)
    return tonumber(e and (e.obj or e.IEntity or e.id)) or 0
end

local function loot_key(e)
    local obj = entity_obj(e)
    if obj > 0 then
        return obj
    end
    return tonumber(e and e.interact_id) or 0
end

local function is_ignored(opts, key)
    local ignored = opts and opts.ignored
    if type(ignored) ~= "table" then
        return false
    end
    local value = ignored[key]
    if value == nil then
        return false
    end
    if value == true then
        return true
    end
    local until_sec = tonumber(value)
    return until_sec ~= nil and now_seconds(opts) < until_sec
end

function M.entityObj(e)
    return entity_obj(e)
end

function M.lootKey(e)
    return loot_key(e)
end

function M.pickup(lootObj)
    return core.first("AionData.LootPickup", data.LootPickup, lootObj)
end

function M.pickupDialog(dialogName)
    local names = dialog_names(dialogName)

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

function M.dialogActive(dialogName)
    local details = {}
    for _, name in ipairs(dialog_names(dialogName)) do
        local find_ok, found = ui.find(name)
        if find_ok and type(found) == "table" and found.visible == true then
            return true, "parent=" .. tostring(name)
        end

        local ok, children = ui.children(name, 8)
        if ok and children then
            local visible_count = 0
            for _, child in ipairs(children) do
                if child and child.visible == true then
                    visible_count = visible_count + 1
                end
            end
            details[#details + 1] = tostring(name) .. ":visible_children=" .. tostring(visible_count)
            if visible_count > 0 then
                return true, details[#details]
            end
        else
            details[#details + 1] = tostring(name) .. ":children_unavailable"
        end
    end
    return false, table.concat(details, ",")
end

local function score_loot_all_button(child, parent)
    child = child or {}
    parent = parent or {}
    local obj = ui_obj(child)
    if not obj or tonumber(obj) == 0 or child.visible ~= true then
        return nil
    end

    local name = string.lower(tostring(child.name or ""))
    local score = 0
    local function has(text)
        return name:find(text, 1, true) ~= nil
    end

    if has("all") then score = score + 80 end
    if has("get") or has("pickup") or has("loot") or has("take") or has("receive") or has("acquire") then
        score = score + 50
    end
    if has("button") or has("btn") or has("ok") then
        score = score + 20
    end
    if has("close") or has("cancel") or has("refuse") or has("prev") then
        score = score - 120
    end

    local x = tonumber(child.x) or 0
    local y = tonumber(child.y) or 0
    local px = tonumber(parent.x) or 0
    local py = tonumber(parent.y) or 0
    local global_rect = (px > 0 or py > 0) and x >= px + 35 and x <= px + 170 and y >= py + 220 and y <= py + 300
    local relative_rect = x >= 35 and x <= 170 and y >= 220 and y <= 300
    if global_rect or relative_rect then
        score = score + 65
    end

    local expected_x = px > 0 and px + 95 or 95
    local expected_y = py > 0 and py + 255 or 255
    local dx = x - expected_x
    local dy = y - expected_y
    score = score - math.min(35, math.sqrt(dx * dx + dy * dy) / 12)

    if score <= 15 then
        return nil
    end
    return score
end

function M.clickLootAllButton(opts)
    opts = opts or {}
    local best = nil
    local best_score = nil
    local best_parent = nil
    local best_parent_name = ""
    local last_err = nil

    for _, parent_name in ipairs(dialog_names(opts.dialogName or opts.dialogNames)) do
        local parent = {}
        local find_ok, found = ui.find(parent_name)
        if find_ok and type(found) == "table" then
            parent = found
        end

        local ok, children, err = ui.children(parent_name, 8)
        if not ok then
            last_err = err
            emit(opts, "children_failed", "parent=" .. tostring(parent_name) .. " err=" .. tostring(err))
        else
            for index, child in ipairs(children or {}) do
                local score = score_loot_all_button(child, parent)
                if score and (not best_score or score > best_score) then
                    best = child
                    best_parent = parent
                    best_parent_name = parent_name
                    best_score = score
                end
                if opts.sampleChildren and index <= 12 then
                    emit(opts, "child_sample", string.format(
                        "parent=%s idx=%d obj=%s name=%s visible=%s x=%.0f y=%.0f score=%s",
                        tostring(parent_name),
                        tonumber(index) or 0,
                        tostring(ui_obj(child) or 0),
                        tostring(child and child.name or ""),
                        tostring(child and child.visible == true),
                        tonumber(child and child.x) or 0,
                        tonumber(child and child.y) or 0,
                        tostring(score or "")))
                end
            end
        end
    end

    if not best then
        return false, nil, "loot all button not found; last_err=" .. tostring(last_err)
    end

    local obj = ui_obj(best)
    local ok, clicked, err = ui.click(obj)
    if ok and clicked ~= false then
        local result = {
            status = "picked",
            source = "ClickButton",
            dialog = best_parent_name,
            button = tostring(best.name or ""),
            button_obj = obj,
            score = best_score,
            parent_x = tonumber(best_parent and best_parent.x) or 0,
            parent_y = tonumber(best_parent and best_parent.y) or 0,
        }
        emit(opts, "click_all", string.format("dialog=%s obj=%s name=%s score=%.1f",
            tostring(best_parent_name),
            tostring(obj),
            tostring(best.name or ""),
            tonumber(best_score) or 0))
        return true, result, nil
    end

    return false, nil, "ClickButton failed obj=" .. tostring(obj) .. " err=" .. tostring(err) .. " clicked=" .. tostring(clicked)
end

function M.closeDialog(opts)
    opts = opts or {}
    for _, parent_name in ipairs(dialog_names(opts.dialogName or opts.dialogNames)) do
        local ok, children = ui.children(parent_name, 8)
        if ok and children then
            for _, child in ipairs(children) do
                local name = string.lower(tostring(child and child.name or ""))
                local obj = ui_obj(child)
                if child and child.visible == true and obj and tonumber(obj) ~= 0 and
                    (name == "cancel" or name == "close" or name:find("close", 1, true)) then
                    local click_ok, clicked, err = ui.click(obj)
                    if click_ok and clicked ~= false then
                        emit(opts, "close_dialog", "parent=" .. tostring(parent_name) .. " obj=" .. tostring(obj))
                        return true, { status = "closed", source = "ClickButton", dialog = parent_name }, nil
                    end
                    return false, nil, "close click failed obj=" .. tostring(obj) .. " err=" .. tostring(err)
                end
            end
        end
    end
    return false, nil, "no visible close button"
end

function M.pickupOpenDialog(opts)
    opts = opts or {}

    local ok, picked, err = M.pickupDialog(opts.dialogName or opts.dialogNames)
    emit(opts, "loot_pickup", "picked=" .. tostring(picked) .. " err=" .. tostring(err))
    if ok and picked == true then
        sleep_ms(opts, tonumber(opts.verifyDelayMs) or 120)
        local active, detail = M.dialogActive(opts.dialogName or opts.dialogNames)
        if not active then
            return true, { status = "picked", source = "LootPickup", detail = detail }, nil
        end
        emit(opts, "loot_pickup_uncertain", "LootPickup true but dialog active: " .. tostring(detail))
    end

    if opts.clickFallback ~= false then
        local click_ok, click_result, click_err = M.clickLootAllButton(opts)
        if click_ok then
            return true, click_result, nil
        end
        err = click_err or err
    end

    return false, nil, err or "loot dialog not ready"
end

function M.waitPickupDialog(opts)
    opts = opts or {}
    local timeout = math.max(50, tonumber(opts.timeoutMs) or 1200)
    local interval = math.max(20, tonumber(opts.intervalMs) or 100)
    local started = now_ms(opts)
    local last_err = nil

    while now_ms(opts) - started <= timeout do
        local ok, result, err = M.pickupOpenDialog(opts)
        if ok then
            return true, result, nil
        end
        last_err = err
        sleep_ms(opts, interval)
    end

    return false, nil, last_err or "loot dialog timeout"
end

function M.findNearestLootable(opts)
    opts = opts or {}
    local char = opts.char
    if not char then
        local char_ok, value, char_err = core.getCharacter()
        if not char_ok or not value then
            return false, nil, char_err or "character unavailable"
        end
        char = value
    end

    local list = opts.list
    if not list then
        local list_ok, value, list_err = entity.list()
        if not list_ok then
            return false, nil, list_err
        end
        list = value or {}
    end

    local anchor = opts.anchor or char
    local radius = tonumber(opts.radius)
    local monster_only = opts.monsterOnly ~= false
    local best = nil
    local best_dist = math.huge
    local checked = 0
    local accepted = 0

    for _, item in ipairs(list or {}) do
        checked = checked + 1
        local key = loot_key(item)
        local lootable = (tonumber(item and item.lootable) or 0) ~= 0
        local type_ok = not monster_only or (tonumber(item and item.type) or 0) == 2
        if lootable and type_ok and key > 0 and not is_ignored(opts, key) then
            local anchor_dist = core.distance3(anchor, item)
            if not radius or anchor_dist <= radius then
                accepted = accepted + 1
                local dist = core.distance3(char, item)
                if dist < best_dist then
                    best = item
                    best_dist = dist
                end
            end
        end
    end

    if best then
        best.distance = best_dist
    end

    local meta = { checked = checked, accepted = accepted, char = char }
    if not best then
        return true, nil, nil, meta
    end
    return true, best, nil, meta
end

function M.pickupTarget(target, opts)
    opts = opts or {}
    if type(target) ~= "table" then
        return false, nil, "loot target is nil"
    end

    local char = opts.char
    if not char then
        local char_ok, value, char_err = core.getCharacter()
        if not char_ok or not value then
            return false, nil, char_err or "character unavailable"
        end
        char = value
    end

    local key = loot_key(target)
    if key <= 0 then
        return false, nil, "invalid loot target"
    end

    local dist = core.distance3(char, target)
    local interact_range = math.max(0.5, tonumber(opts.interactRange) or 4)
    local result = {
        status = "target",
        target = target,
        obj = key,
        name = tostring(target.name or ""),
        distance = dist,
        interact_id = tonumber(target.interact_id) or 0,
    }
    emit(opts, "target", string.format("name=%s obj=%s dist=%.1f interact_id=%s",
        result.name,
        tostring(key),
        tonumber(dist) or 0,
        tostring(result.interact_id)))

    if dist > interact_range then
        local move_ok, _, move_err = nav.moveTo(target.x or 0, target.y or 0, target.z or 0)
        if not move_ok then
            result.status = "failed"
            result.error = move_err
            return false, result, move_err
        end
        result.status = "moving"
        emit(opts, "move", string.format("name=%s obj=%s dist=%.1f range=%.1f",
            result.name,
            tostring(key),
            tonumber(dist) or 0,
            tonumber(interact_range) or 0))
        return true, result, nil
    end

    if opts.closeBeforeOpen ~= false then
        M.closeDialog(opts)
        sleep_ms(opts, tonumber(opts.closeDelayMs) or 80)
    end

    if combat and type(combat.selectTarget) == "function" then
        pcall(combat.selectTarget, key)
    end

    local interact_id = tonumber(target.interact_id) or 0
    if interact_id > 0 then
        local interact_ok, interact_value, interact_err = npc.interactId(interact_id)
        if interact_ok and interact_value ~= false then
            emit(opts, "open_interact", "interact_id=" .. tostring(interact_id) .. " obj=" .. tostring(key))
            local picked_ok, picked_result = M.waitPickupDialog({
                dialogName = opts.dialogName,
                dialogNames = opts.dialogNames,
                timeoutMs = tonumber(opts.waitTimeoutMs) or 1200,
                intervalMs = tonumber(opts.intervalMs) or 100,
                verifyDelayMs = opts.verifyDelayMs,
                clickFallback = opts.clickFallback,
                sampleChildren = opts.sampleChildren,
                sleep = opts.sleep,
                now_ms = opts.now_ms,
                log = opts.log,
            })
            if picked_ok then
                picked_result.target = target
                picked_result.obj = key
                picked_result.name = result.name
                return true, picked_result, nil
            end
            emit(opts, "open_interact_no_dialog", "obj=" .. tostring(key))
        else
            emit(opts, "open_interact_failed", "interact_id=" .. tostring(interact_id) .. " err=" .. tostring(interact_err))
        end
    end

    local keycode = tonumber(opts.keycode) or 67
    local key_ok, _, key_err = remote.pressKey(keycode)
    if key_ok then
        emit(opts, "open_key", "keycode=" .. tostring(keycode) .. " obj=" .. tostring(key))
        local picked_ok, picked_result, picked_err = M.waitPickupDialog({
            dialogName = opts.dialogName,
            dialogNames = opts.dialogNames,
            timeoutMs = tonumber(opts.waitTimeoutMs) or 1200,
            intervalMs = tonumber(opts.intervalMs) or 100,
            verifyDelayMs = opts.verifyDelayMs,
            clickFallback = opts.clickFallback,
            sampleChildren = opts.sampleChildren,
            sleep = opts.sleep,
            now_ms = opts.now_ms,
            log = opts.log,
        })
        if picked_ok then
            picked_result.target = target
            picked_result.obj = key
            picked_result.name = result.name
            return true, picked_result, nil
        end
        result.status = "wait_dialog"
        result.error = picked_err
        return true, result, picked_err
    end

    result.status = "failed"
    result.error = key_err
    return false, result, key_err or "loot open failed"
end

function M.pickupNearest(opts)
    opts = opts or {}
    local target = opts.target
    local meta = nil
    if not target then
        local find_ok, found, find_err, find_meta = M.findNearestLootable(opts)
        if not find_ok then
            return false, nil, find_err
        end
        target = found
        meta = find_meta
    end

    if not target then
        return true, {
            status = "no_target",
            checked = meta and meta.checked or 0,
            accepted = meta and meta.accepted or 0,
        }, nil
    end

    local call_opts = {}
    for key, value in pairs(opts) do
        call_opts[key] = value
    end
    call_opts.char = call_opts.char or (meta and meta.char)
    return M.pickupTarget(target, call_opts)
end

M.pickNearest = M.pickupNearest

return M
