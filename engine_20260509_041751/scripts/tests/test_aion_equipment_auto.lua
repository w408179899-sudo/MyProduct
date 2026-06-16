local T = require("tests.test_framework")

local function clear_modules()
    package.loaded["aion.equipment_auto"] = nil
end

local function load_module()
    clear_modules()
    return require("aion.equipment_auto")
end

local function item(id, name, equip_pos, slot, level, quality)
    return {
        id = id,
        text = name,
        equip_pos = equip_pos,
        equip_pos_name = "slot-" .. tostring(equip_pos or 0),
        slot = slot or 0,
        slot_name = slot and slot ~= 0 and ("slot-" .. tostring(slot)) or "未穿戴",
        item_level = level or 0,
        quality = quality or 1,
        count = 1,
    }
end

local function mock_inventory(before, after)
    local calls = {
        equip = {},
        decompose = {},
        list_count = 0,
    }
    local api = {
        list = function()
            calls.list_count = calls.list_count + 1
            if calls.list_count >= 2 and after then
                return true, after, nil
            end
            return true, before, nil
        end,
        equipItem = function(itemId, equipPos, unequip)
            calls.equip[#calls.equip + 1] = {
                item_id = itemId,
                equip_pos = equipPos,
                unequip = unequip,
            }
            return true, true, nil
        end,
    }
    return api, calls
end

local function clone_item(src)
    local out = {}
    for key, value in pairs(src) do
        out[key] = value
    end
    return out
end

local function clone_items(items)
    local out = {}
    for index, src in ipairs(items or {}) do
        out[index] = clone_item(src)
    end
    return out
end

local function dynamic_inventory(initial)
    local state = clone_items(initial)
    local calls = {
        equip = {},
        list_count = 0,
    }
    local api = {
        list = function()
            calls.list_count = calls.list_count + 1
            return true, clone_items(state), nil
        end,
        equipItem = function(itemId, equipPos, unequip)
            calls.equip[#calls.equip + 1] = {
                item_id = itemId,
                equip_pos = equipPos,
                unequip = unequip,
            }
            for _, it in ipairs(state) do
                if it.id == itemId then
                    it.slot = equipPos
                    it.slot_name = "slot-" .. tostring(equipPos)
                elseif it.slot == equipPos then
                    it.slot = 0
                    it.slot_name = "未穿戴"
                end
            end
            return true, true, nil
        end,
        decomposeItem = function(itemId)
            calls.decompose[#calls.decompose + 1] = { item_id = itemId }
            for index = #state, 1, -1 do
                if state[index].id == itemId then
                    table.remove(state, index)
                    break
                end
            end
            return true, true, nil
        end,
    }
    return api, calls
end

local function run()
    T.reset()
    T.log("\n=== aion equipment auto tests ===")

    T.test("empty slot equips highest level candidate directly", function()
        local mod = load_module()
        local plan = mod.plan({
            item(101, "Old Shoes", 4, 0, 5),
            item(102, "New Shoes", 4, 0, 9),
        })

        T.assert_not_nil(plan.target)
        T.assert_eq(plan.target.reason, "empty-slot")
        T.assert_eq(plan.target.item.id, 102)
    end)

    T.test("empty slot prefers green equipment before white equipment", function()
        local mod = load_module()
        local plan = mod.plan({
            item(111, "White Shoes", 4, 0, 12, 1),
            item(112, "Green Shoes", 4, 0, 8, 2),
        })

        T.assert_not_nil(plan.target)
        T.assert_eq(plan.target.reason, "empty-slot")
        T.assert_eq(plan.target.item.id, 112)
    end)

    T.test("equipped slot replaces only when bag item level is higher", function()
        local mod = load_module()
        local plan = mod.plan({
            item(201, "Equipped Sword", 1, 1, 8),
            item(202, "Low Sword", 1, 0, 7),
            item(203, "High Sword", 1, 0, 10),
        })

        T.assert_not_nil(plan.target)
        T.assert_eq(plan.target.reason, "level-upgrade")
        T.assert_eq(plan.target.item.id, 203)
        T.assert_eq(plan.target.current.id, 201)
        T.assert_eq(plan.target.level_delta, 2)
    end)

    T.test("replacement prefers green among higher-level equipment", function()
        local mod = load_module()
        local plan = mod.plan({
            item(211, "Equipped Sword", 1, 1, 8, 1),
            item(212, "White Sword", 1, 0, 14, 1),
            item(213, "Green Sword", 1, 0, 9, 2),
        })

        T.assert_not_nil(plan.target)
        T.assert_eq(plan.target.reason, "level-upgrade")
        T.assert_eq(plan.target.item.id, 213)
    end)

    T.test("same level equipment is not treated as replacement", function()
        local mod = load_module()
        local plan = mod.plan({
            item(301, "Equipped Gloves", 5, 5, 10, 1),
            item(302, "Same Level Gloves", 5, 0, 10, 4),
        })

        T.assert_nil(plan.target)
        T.assert_eq(plan.rejected["level-not-higher"], 1)
    end)

    T.test("empty slots are selected before replacements", function()
        local mod = load_module()
        local plan = mod.plan({
            item(401, "Equipped Chest", 3, 3, 5),
            item(402, "Better Chest", 3, 0, 20),
            item(403, "Missing Belt", 13, 0, 8),
        })

        T.assert_not_nil(plan.target)
        T.assert_eq(plan.target.reason, "empty-slot")
        T.assert_eq(plan.target.item.id, 403)
    end)

    T.test("equipBest calls EquipItem with candidate id and equip_pos", function()
        local mod = load_module()
        local before = {
            item(501, "Equipped Helm", 2, 2, 7),
            item(502, "Better Helm", 2, 0, 9),
        }
        local after = {
            item(501, "Equipped Helm", 2, 0, 7),
            item(502, "Better Helm", 2, 2, 9),
        }
        local api, calls = mock_inventory(before, after)

        local ok, result = mod.equipBest(api, { sleep = function() end })
        T.assert_eq(ok, true)
        T.assert_eq(result.status, "equipped")
        T.assert_eq(#calls.equip, 1)
        T.assert_eq(calls.equip[1].item_id, 502)
        T.assert_eq(calls.equip[1].equip_pos, 2)
        T.assert_eq(calls.equip[1].unequip, false)
    end)

    T.test("equipBest does not call EquipItem when no upgrade exists", function()
        local mod = load_module()
        local api, calls = mock_inventory({
            item(601, "Equipped Pants", 12, 12, 12),
            item(602, "Old Pants", 12, 0, 11),
        })

        local ok, result = mod.equipBest(api, { verify_after = false })
        T.assert_eq(ok, true)
        T.assert_eq(result.status, "no-upgrade")
        T.assert_eq(#calls.equip, 0)
    end)

    T.test("equipAll compares many bag items and equips each useful slot", function()
        local mod = load_module()
        local api, calls = dynamic_inventory({
            item(701, "Equipped Sword", 1, 1, 5),
            item(702, "Low Sword", 1, 0, 4),
            item(703, "High Sword", 1, 0, 9),
            item(704, "Best Empty Boots", 4, 0, 8),
            item(705, "Old Empty Boots", 4, 0, 3),
            item(706, "Equipped Gloves", 5, 5, 10),
            item(707, "Same Gloves", 5, 0, 10),
        })

        local ok, result = mod.equipAll(api, {
            sleep = function() end,
            max_actions = 10,
        })

        T.assert_eq(ok, true)
        T.assert_eq(result.status, "complete")
        T.assert_eq(result.equipped_count, 2)
        T.assert_eq(#calls.equip, 2)
        T.assert_eq(calls.equip[1].item_id, 704)
        T.assert_eq(calls.equip[2].item_id, 703)
    end)

    T.test("decomposeLowQuality decomposes only unequipped white and green equipment", function()
        local mod = load_module()
        local api, calls = dynamic_inventory({
            item(801, "Equipped White Sword", 1, 1, 5, 1),
            item(802, "Bag White Sword", 1, 0, 4, 1),
            item(803, "Bag Green Boots", 4, 0, 8, 2),
            item(804, "Bag Blue Gloves", 5, 0, 8, 3),
            item(805, "Potion", 0, 0, 0, 1),
        })

        local ok, result = mod.decomposeLowQuality(api, { sleep = function() end })

        T.assert_eq(ok, true)
        T.assert_eq(result.status, "decomposed")
        T.assert_eq(result.decomposed_count, 2)
        T.assert_eq(#calls.decompose, 2)
        T.assert_eq(calls.decompose[1].item_id, 802)
        T.assert_eq(calls.decompose[2].item_id, 803)
    end)

    T.test("equipment pass then decomposes old and lower white green bag equipment", function()
        local mod = load_module()
        local api, calls = dynamic_inventory({
            item(901, "Equipped White Sword", 1, 1, 5, 1),
            item(902, "Better Green Sword", 1, 0, 9, 2),
            item(903, "Lower White Sword", 1, 0, 4, 1),
            item(904, "White Empty Boots", 4, 0, 8, 1),
            item(905, "Blue Bag Gloves", 5, 0, 8, 3),
        })

        local equip_ok, equip_result = mod.equipAll(api, {
            sleep = function() end,
            max_actions = 10,
        })
        local decompose_ok, decompose_result = mod.decomposeLowQuality(api, { sleep = function() end })

        T.assert_eq(equip_ok, true)
        T.assert_eq(equip_result.equipped_count, 2)
        T.assert_eq(decompose_ok, true)
        T.assert_eq(decompose_result.decomposed_count, 2)
        T.assert_eq(calls.decompose[1].item_id, 903)
        T.assert_eq(calls.decompose[2].item_id, 901)
    end)

    clear_modules()
    return T.report("aion_equipment_auto")
end

return { run = run }
