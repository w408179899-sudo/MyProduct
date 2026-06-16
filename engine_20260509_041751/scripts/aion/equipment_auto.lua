local M = {}

M.QUALITY_WHITE = 1
M.QUALITY_GREEN = 2

local function number(value)
    return tonumber(value) or 0
end

function M.itemName(item)
    if type(item) ~= "table" then
        return ""
    end
    return tostring(item.text or item.name or "")
end

function M.itemLevel(item)
    return number(type(item) == "table" and item.item_level)
end

function M.isEquipped(item)
    return number(type(item) == "table" and item.slot) ~= 0
end

function M.isWearable(item)
    if type(item) ~= "table" then
        return false, "not-table"
    end
    if number(item.id) <= 0 then
        return false, "missing-id"
    end
    if number(item.equip_pos) <= 0 then
        return false, "not-equipment"
    end
    if M.isEquipped(item) then
        return false, "already-equipped"
    end
    return true, "ok"
end

function M.isLowQualityEquipment(item)
    local ok, reason = M.isWearable(item)
    if not ok then
        return false, reason
    end

    local quality = number(item.quality)
    if quality == M.QUALITY_WHITE or quality == M.QUALITY_GREEN then
        return true, "ok"
    end
    if quality <= 0 then
        return false, "quality-unknown"
    end
    return false, "quality-too-high"
end

function M.equipQualityRank(item)
    return number(type(item) == "table" and item.quality)
end

function M.itemSummary(item)
    item = type(item) == "table" and item or {}
    return "id=" .. tostring(item.id or "") ..
        " name=" .. M.itemName(item) ..
        " cat=" .. tostring(item.cat_name or item.cat or "") ..
        " slot=" .. tostring(item.slot or "") ..
        " slot_name=" .. tostring(item.slot_name or "") ..
        " equip_pos=" .. tostring(item.equip_pos or "") ..
        " equip_pos_name=" .. tostring(item.equip_pos_name or "") ..
        " quality=" .. tostring(item.quality or "") ..
        " level=" .. tostring(item.item_level or "") ..
        " count=" .. tostring(item.count or "")
end

function M.indexEquipped(items)
    local out = {}
    for _, item in ipairs(items or {}) do
        local slot = number(item and item.slot)
        if slot ~= 0 then
            local current = out[slot]
            if not current or M.itemLevel(item) > M.itemLevel(current) then
                out[slot] = item
            end
        end
    end
    return out
end

function M.evaluateCandidate(item, equippedBySlot)
    local ok, reason = M.isWearable(item)
    if not ok then
        return nil, reason
    end

    local equipPos = number(item.equip_pos)
    local current = equippedBySlot and equippedBySlot[equipPos] or nil
    local itemLevel = M.itemLevel(item)
    if type(current) ~= "table" then
        return {
            action = "equip",
            reason = "empty-slot",
            item = item,
            current = nil,
            equip_pos = equipPos,
            item_level = itemLevel,
            current_level = 0,
            level_delta = itemLevel,
        }, "ok"
    end

    local currentLevel = M.itemLevel(current)
    if itemLevel > currentLevel then
        return {
            action = "replace",
            reason = "level-upgrade",
            item = item,
            current = current,
            equip_pos = equipPos,
            item_level = itemLevel,
            current_level = currentLevel,
            level_delta = itemLevel - currentLevel,
        }, "ok"
    end

    return nil, "level-not-higher"
end

local function betterCandidate(a, b)
    if not b then
        return true
    end
    local ar = a.reason == "empty-slot" and 1 or 2
    local br = b.reason == "empty-slot" and 1 or 2
    if ar ~= br then
        return ar < br
    end

    local aq = M.equipQualityRank(a.item)
    local bq = M.equipQualityRank(b.item)
    if aq ~= bq then
        return aq > bq
    end
    if number(a.item_level) ~= number(b.item_level) then
        return number(a.item_level) > number(b.item_level)
    end
    if number(a.level_delta) ~= number(b.level_delta) then
        return number(a.level_delta) > number(b.level_delta)
    end
    return number(a.item and a.item.id) < number(b.item and b.item.id)
end

local function candidate_sort(a, b)
    return betterCandidate(a, b)
end

function M.plan(items)
    items = type(items) == "table" and items or {}
    local equippedBySlot = M.indexEquipped(items)
    local candidates = {}
    local rejected = {}
    local target = nil

    for _, item in ipairs(items) do
        local candidate, reason = M.evaluateCandidate(item, equippedBySlot)
        if candidate then
            candidates[#candidates + 1] = candidate
            if betterCandidate(candidate, target) then
                target = candidate
            end
        else
            rejected[reason or "rejected"] = (rejected[reason or "rejected"] or 0) + 1
        end
    end

    table.sort(candidates, candidate_sort)
    return {
        target = target,
        candidates = candidates,
        rejected = rejected,
        equipped_by_slot = equippedBySlot,
        inventory_count = #items,
    }
end

function M.findItemById(items, itemId)
    itemId = number(itemId)
    for _, item in ipairs(items or {}) do
        if number(item and item.id) == itemId then
            return item
        end
    end
    return nil
end

function M.equipBest(inventoryApi, opts)
    opts = opts or {}
    if type(inventoryApi) ~= "table"
        or type(inventoryApi.list) ~= "function"
        or type(inventoryApi.equipItem) ~= "function" then
        return false, { status = "failed", error = "inventory api unavailable" }
    end

    local listOk, items, listErr = inventoryApi.list()
    if not listOk then
        return false, { status = "failed", error = tostring(listErr or "inventory.list failed") }
    end

    local plan = M.plan(items)
    local target = plan.target
    if not target then
        return true, {
            status = "no-upgrade",
            plan = plan,
            lines = M.debugLines(plan),
        }
    end

    local item = target.item
    local callOk, result, callErr = inventoryApi.equipItem(item.id, item.equip_pos, false)
    target.call_ok = callOk
    target.call_result = result
    target.call_error = callErr
    if not callOk or result == false then
        return false, {
            status = "failed",
            error = tostring(callErr or result or "equipItem failed"),
            plan = plan,
            target = target,
            lines = M.debugLines(plan),
        }
    end

    if opts.verify_after == false then
        return true, {
            status = "submitted",
            plan = plan,
            target = target,
            lines = M.debugLines(plan),
        }
    end

    local sleepMs = math.max(0, number(opts.verify_sleep_ms or 700))
    if sleepMs > 0 and type(opts.sleep) == "function" then
        opts.sleep(sleepMs)
    end

    local verifyOk, verifyItems, verifyErr = inventoryApi.list()
    if not verifyOk then
        return false, {
            status = "verify-failed",
            error = tostring(verifyErr or "verify inventory.list failed"),
            plan = plan,
            target = target,
            lines = M.debugLines(plan),
        }
    end

    local after = M.findItemById(verifyItems, item.id)
    target.after = after
    if after and M.isEquipped(after) then
        return true, {
            status = "equipped",
            plan = plan,
            target = target,
            after = after,
            lines = M.debugLines(plan),
        }
    end

    return false, {
        status = "verify-failed",
        error = "target item is still unequipped",
        plan = plan,
        target = target,
        after = after,
        lines = M.debugLines(plan),
    }
end

local function appendLines(dst, src, prefix)
    for _, line in ipairs(src or {}) do
        dst[#dst + 1] = tostring(prefix or "") .. tostring(line or "")
    end
end

function M.equipAll(inventoryApi, opts)
    opts = opts or {}
    if type(inventoryApi) ~= "table"
        or type(inventoryApi.list) ~= "function"
        or type(inventoryApi.equipItem) ~= "function" then
        return false, { status = "failed", error = "inventory api unavailable" }
    end

    local maxActions = math.max(1, number(opts.max_actions or 20))
    local result = {
        status = "no-upgrade",
        equipped_count = 0,
        actions = {},
        lines = {},
    }

    for step = 1, maxActions do
        local listOk, items, listErr = inventoryApi.list()
        if not listOk then
            result.status = "failed"
            result.error = tostring(listErr or "inventory.list failed")
            return false, result
        end

        local plan = M.plan(items)
        appendLines(result.lines, M.debugLines(plan), "step[" .. tostring(step) .. "] ")

        local target = plan.target
        if not target then
            result.status = result.equipped_count > 0 and "complete" or "no-upgrade"
            return true, result
        end

        local item = target.item
        local callOk, callResult, callErr = inventoryApi.equipItem(item.id, item.equip_pos, false)
        target.call_ok = callOk
        target.call_result = callResult
        target.call_error = callErr
        result.lines[#result.lines + 1] = "step[" .. tostring(step) .. "] equip call item_id=" ..
            tostring(item.id or "") ..
            " equip_pos=" .. tostring(item.equip_pos or "") ..
            " reason=" .. tostring(target.reason or "") ..
            " call_ok=" .. tostring(callOk) ..
            " call_result=" .. tostring(callResult) ..
            " call_error=" .. tostring(callErr or "")

        if not callOk or callResult == false then
            result.status = "failed"
            result.error = tostring(callErr or callResult or "equipItem failed")
            result.target = target
            return false, result
        end

        if opts.verify_after == false then
            result.equipped_count = result.equipped_count + 1
            result.actions[#result.actions + 1] = target
        else
            local sleepMs = math.max(0, number(opts.verify_sleep_ms or 700))
            if sleepMs > 0 and type(opts.sleep) == "function" then
                opts.sleep(sleepMs)
            end

            local verifyOk, verifyItems, verifyErr = inventoryApi.list()
            if not verifyOk then
                result.status = "verify-failed"
                result.error = tostring(verifyErr or "verify inventory.list failed")
                result.target = target
                return false, result
            end

            local after = M.findItemById(verifyItems, item.id)
            target.after = after
            if not after or not M.isEquipped(after) then
                result.status = "verify-failed"
                result.error = "target item is still unequipped"
                result.target = target
                result.after = after
                return false, result
            end

            result.equipped_count = result.equipped_count + 1
            result.actions[#result.actions + 1] = target
        end
    end

    result.status = "limit-reached"
    result.error = "equipment pass reached max_actions=" .. tostring(maxActions)
    return false, result
end

function M.decomposePlan(items)
    items = type(items) == "table" and items or {}
    local candidates = {}
    local rejected = {}

    for _, item in ipairs(items) do
        local ok, reason = M.isLowQualityEquipment(item)
        if ok then
            candidates[#candidates + 1] = item
        else
            rejected[reason or "rejected"] = (rejected[reason or "rejected"] or 0) + 1
        end
    end

    table.sort(candidates, function(a, b)
        local aq = number(a.quality)
        local bq = number(b.quality)
        if aq ~= bq then
            return aq < bq
        end
        local al = M.itemLevel(a)
        local bl = M.itemLevel(b)
        if al ~= bl then
            return al < bl
        end
        return number(a.id) < number(b.id)
    end)

    return {
        candidates = candidates,
        rejected = rejected,
        inventory_count = #items,
    }
end

function M.decomposeDebugLines(plan)
    plan = type(plan) == "table" and plan or {}
    local lines = {}
    lines[#lines + 1] = "decompose inventory_count=" .. tostring(plan.inventory_count or 0) ..
        " candidates=" .. tostring(#(plan.candidates or {}))

    local rejectParts = {}
    for reason, count in pairs(plan.rejected or {}) do
        rejectParts[#rejectParts + 1] = tostring(reason) .. "=" .. tostring(count)
    end
    table.sort(rejectParts)
    lines[#lines + 1] = "decompose rejected " .. table.concat(rejectParts, " ")

    local candidates = plan.candidates or {}
    local limit = math.min(#candidates, 20)
    for index = 1, limit do
        lines[#lines + 1] = string.format("decompose candidate[%d] %s",
            index,
            M.itemSummary(candidates[index]))
    end
    return lines
end

function M.decomposeLowQuality(inventoryApi, opts)
    opts = opts or {}
    if type(inventoryApi) ~= "table"
        or type(inventoryApi.list) ~= "function"
        or type(inventoryApi.decomposeItem) ~= "function" then
        return false, { status = "failed", error = "inventory decompose api unavailable" }
    end

    local listOk, items, listErr = inventoryApi.list()
    if not listOk then
        return false, { status = "failed", error = tostring(listErr or "inventory.list failed") }
    end

    local plan = M.decomposePlan(items)
    local result = {
        status = "no-decompose",
        decomposed_count = 0,
        actions = {},
        plan = plan,
        lines = M.decomposeDebugLines(plan),
    }

    if #plan.candidates <= 0 then
        return true, result
    end

    local maxActions = math.max(1, number(opts.max_actions or #plan.candidates))
    local count = math.min(#plan.candidates, maxActions)
    for index = 1, count do
        local item = plan.candidates[index]
        local callOk, callResult, callErr = inventoryApi.decomposeItem(item.id)
        local action = {
            item = item,
            call_ok = callOk,
            call_result = callResult,
            call_error = callErr,
        }
        result.actions[#result.actions + 1] = action
        result.lines[#result.lines + 1] = "decompose call item_id=" .. tostring(item.id or "") ..
            " quality=" .. tostring(item.quality or "") ..
            " call_ok=" .. tostring(callOk) ..
            " call_result=" .. tostring(callResult) ..
            " call_error=" .. tostring(callErr or "")

        if not callOk or callResult == false then
            result.status = "failed"
            result.error = tostring(callErr or callResult or "decomposeItem failed")
            result.target = item
            return false, result
        end

        result.decomposed_count = result.decomposed_count + 1
        local sleepMs = math.max(0, number(opts.decompose_sleep_ms or 150))
        if sleepMs > 0 and type(opts.sleep) == "function" then
            opts.sleep(sleepMs)
        end
    end

    if count < #plan.candidates then
        result.status = "limit-reached"
        result.error = "decompose pass reached max_actions=" .. tostring(maxActions)
        return false, result
    end

    result.status = "decomposed"
    return true, result
end

function M.debugLines(plan)
    plan = type(plan) == "table" and plan or {}
    local lines = {}
    lines[#lines + 1] = "inventory_count=" .. tostring(plan.inventory_count or 0) ..
        " candidates=" .. tostring(#(plan.candidates or {}))

    local rejectParts = {}
    for reason, count in pairs(plan.rejected or {}) do
        rejectParts[#rejectParts + 1] = tostring(reason) .. "=" .. tostring(count)
    end
    table.sort(rejectParts)
    lines[#lines + 1] = "rejected " .. table.concat(rejectParts, " ")

    local candidates = plan.candidates or {}
    local limit = math.min(#candidates, 10)
    for index = 1, limit do
        local c = candidates[index]
        lines[#lines + 1] = string.format(
            "candidate[%d] reason=%s delta=%s current_level=%s item=%s current=%s",
            index,
            tostring(c.reason or ""),
            tostring(c.level_delta or 0),
            tostring(c.current_level or 0),
            M.itemSummary(c.item),
            c.current and M.itemSummary(c.current) or "none")
    end

    if plan.target then
        lines[#lines + 1] = "target reason=" .. tostring(plan.target.reason or "") ..
            " " .. M.itemSummary(plan.target.item)
    else
        lines[#lines + 1] = "target none"
    end
    return lines
end

return M
