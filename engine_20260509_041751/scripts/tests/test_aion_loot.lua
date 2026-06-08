local T = require("tests.test_framework")

local function distance3(a, b)
    local dx = (a.x or 0) - (b.x or 0)
    local dy = (a.y or 0) - (b.y or 0)
    local dz = (a.z or 0) - (b.z or 0)
    return math.sqrt(dx * dx + dy * dy + dz * dz)
end

local function clear_aion_modules()
    package.loaded["aion.loot"] = nil
    package.loaded["aion.core"] = nil
    package.loaded["aion.entity"] = nil
    package.loaded["aion.nav"] = nil
    package.loaded["aion.npc"] = nil
    package.loaded["aion.combat"] = nil
    package.loaded["aion.remote"] = nil
    package.loaded["aion.ui"] = nil
end

local function index_of(actions, prefix)
    for i, action in ipairs(actions) do
        if tostring(action):find(prefix, 1, true) == 1 then
            return i
        end
    end
    return nil
end

local function install_loot_mocks(state)
    clear_aion_modules()
    state = state or {}
    state.actions = state.actions or {}
    state.time_ms = tonumber(state.time_ms) or 0
    state.dialog_open = state.dialog_open == true
    state.close_obj = state.close_obj or 101
    state.takeall_obj = state.takeall_obj or 102
    state.dialog_obj = state.dialog_obj or 201
    state.press_keys = 0
    state.interact_ids = 0
    state.close_clicks = 0
    state.takeall_clicks = 0
    state.lootpickups = 0

    local function record(action)
        state.actions[#state.actions + 1] = action
    end

    local core = {
        data = {},
        first = function(name, fn, ...)
            if type(fn) ~= "function" then
                return false, nil, tostring(name) .. " missing"
            end
            local ok, value = pcall(fn, ...)
            if not ok then
                return false, nil, tostring(value)
            end
            return true, value, nil
        end,
        getCharacter = function()
            return true, state.char or { x = 0, y = 0, z = 0 }, nil
        end,
        distance3 = distance3,
        sleep = function(ms)
            state.time_ms = state.time_ms + (tonumber(ms) or 0)
            record("sleep:" .. tostring(ms))
        end,
        nowMs = function()
            return state.time_ms
        end,
    }

    core.data.LootPickup = function(loot_obj)
        state.lootpickups = state.lootpickups + 1
        record("lootpickup:" .. tostring(loot_obj))
        if state.lootpickup_closes ~= false then
            state.dialog_open = false
        end
        return true
    end

    package.loaded["aion.core"] = core
    package.loaded["aion.entity"] = {
        list = function()
            return true, state.entity_list or {}, nil
        end,
    }
    package.loaded["aion.nav"] = {
        moveTo = function(x, y, z)
            record(string.format("move:%.1f,%.1f,%.1f", tonumber(x) or 0, tonumber(y) or 0, tonumber(z) or 0))
            state.char = { x = x, y = y, z = z }
            return true, true, nil
        end,
    }
    package.loaded["aion.npc"] = {
        interactId = function(interact_id)
            state.interact_ids = state.interact_ids + 1
            record("interact:" .. tostring(interact_id))
            state.dialog_open = true
            return true, true, nil
        end,
    }
    package.loaded["aion.combat"] = {
        selectTarget = function(obj)
            record("select:" .. tostring(obj))
            return true, true, nil
        end,
    }
    package.loaded["aion.remote"] = {
        pressKey = function(keycode)
            state.press_keys = state.press_keys + 1
            record("pressKey:" .. tostring(keycode))
            state.dialog_open = true
            return true, true, nil
        end,
    }
    package.loaded["aion.ui"] = {
        find = function(name)
            if state.dialog_open and (name == "loot_dialog" or name == "dlg_loot") then
                return true, { obj = state.dialog_obj, visible = true }, nil
            end
            return true, { obj = 0, visible = false }, nil
        end,
        children = function(name)
            if not state.dialog_open or (name ~= "loot_dialog" and name ~= "dlg_loot") then
                return true, {}, nil
            end
            return true, {
                { obj = state.takeall_obj, name = "takeall_button", visible = true, x = 52, y = 215 },
                { obj = state.close_obj, name = "cancel", visible = true, x = 157, y = 5 },
            }, nil
        end,
        click = function(obj)
            if obj == state.close_obj then
                state.close_clicks = state.close_clicks + 1
                record("click_cancel:" .. tostring(obj))
                if state.close_keeps_dialog ~= true then
                    state.dialog_open = false
                end
                return true, true, nil
            end
            if obj == state.takeall_obj then
                state.takeall_clicks = state.takeall_clicks + 1
                record("click_takeall:" .. tostring(obj))
                state.dialog_open = false
                return true, true, nil
            end
            return false, false, "unknown click obj"
        end,
    }

    local loot = require("aion.loot")
    return loot, state
end

local function default_target()
    return {
        obj = 5001,
        interact_id = 7001,
        type = 2,
        lootable = 1,
        name = "corpse",
        x = 0,
        y = 0,
        z = 0,
    }
end

local function run()
    T.reset()
    T.log("\n=== aion loot tests ===")

    T.test("stale active dialog is closed before opening target", function()
        local loot, state = install_loot_mocks({ dialog_open = true })
        local ok, result, err = loot.pickupTarget(default_target(), {
            char = { x = 0, y = 0, z = 0 },
            interactRange = 4,
            keycode = 67,
            waitTimeoutMs = 300,
            intervalMs = 50,
            sleep = function(ms)
                state.time_ms = state.time_ms + (tonumber(ms) or 0)
                state.actions[#state.actions + 1] = "sleep_opt:" .. tostring(ms)
            end,
            now_ms = function()
                return state.time_ms
            end,
        })

        T.assert_true(ok, err)
        T.assert_eq(result.status, "picked")
        T.assert_eq(state.close_clicks, 1)
        T.assert_eq(state.interact_ids, 1)
        T.assert_eq(state.press_keys, 0)
        T.assert_eq(state.lootpickups, 1)

        local close_index = index_of(state.actions, "click_cancel:")
        local interact_index = index_of(state.actions, "interact:")
        local pickup_index = index_of(state.actions, "lootpickup:")
        T.assert_not_nil(close_index, "close action should run")
        T.assert_not_nil(interact_index, "interact should run")
        T.assert_not_nil(pickup_index, "LootPickup should run after opening")
        T.assert_true(close_index < interact_index, "stale dialog close should be tried before open")
        T.assert_true(interact_index < pickup_index, "target must open before pickup")
    end)

    T.test("unclosable stale dialog still opens target", function()
        local loot, state = install_loot_mocks({ dialog_open = true, close_keeps_dialog = true })
        local ok, result, err = loot.pickupTarget(default_target(), {
            char = { x = 0, y = 0, z = 0 },
            interactRange = 4,
            keycode = 67,
            waitTimeoutMs = 300,
            intervalMs = 50,
            closeDelayMs = 20,
            sleep = function(ms)
                state.time_ms = state.time_ms + (tonumber(ms) or 0)
                state.actions[#state.actions + 1] = "sleep_opt:" .. tostring(ms)
            end,
            now_ms = function()
                return state.time_ms
            end,
        })

        T.assert_true(ok, err)
        T.assert_eq(result.status, "picked")
        T.assert_true(state.close_clicks >= 1, "close should be attempted")
        T.assert_eq(state.interact_ids, 1)
        T.assert_eq(state.lootpickups, 1)

        local close_index = index_of(state.actions, "click_cancel:")
        local interact_index = index_of(state.actions, "interact:")
        local pickup_index = index_of(state.actions, "lootpickup:")
        T.assert_not_nil(close_index, "close action should run")
        T.assert_not_nil(interact_index, "interact should still run")
        T.assert_not_nil(pickup_index, "LootPickup should run")
        T.assert_true(close_index < interact_index, "interact should happen after close attempt")
        T.assert_true(interact_index < pickup_index, "pickup should happen after interact")
    end)

    T.test("explicit active-dialog pickup keeps old behavior", function()
        local loot, state = install_loot_mocks({ dialog_open = true })
        local ok, result, err = loot.pickupTarget(default_target(), {
            char = { x = 0, y = 0, z = 0 },
            interactRange = 4,
            pickupActiveBeforeOpen = true,
            sleep = function(ms)
                state.time_ms = state.time_ms + (tonumber(ms) or 0)
            end,
            now_ms = function()
                return state.time_ms
            end,
        })

        T.assert_true(ok, err)
        T.assert_eq(result.status, "picked")
        T.assert_eq(state.press_keys, 0)
        T.assert_eq(state.close_clicks, 0)
        T.assert_eq(state.lootpickups, 1)
    end)

    T.test("pickupTargetByKey moves into range and opens with key only", function()
        local loot, state = install_loot_mocks({})
        local target = default_target()
        target.x = 10
        target.y = 0
        target.z = 0

        local ok, result, err = loot.pickupTargetByKey(target, {
            char = { x = 0, y = 0, z = 0 },
            interactRange = 4,
            keycode = 67,
            waitTimeoutMs = 300,
            intervalMs = 50,
            moveSettleMs = 20,
            sleep = function(ms)
                state.time_ms = state.time_ms + (tonumber(ms) or 0)
                state.actions[#state.actions + 1] = "sleep_opt:" .. tostring(ms)
            end,
            now_ms = function()
                return state.time_ms
            end,
        })

        T.assert_true(ok, err)
        T.assert_eq(result.status, "picked")
        T.assert_eq(result.open_mode, "key")
        T.assert_eq(state.interact_ids, 0)
        T.assert_eq(state.press_keys, 1)
        T.assert_eq(state.lootpickups, 1)

        local move_index = index_of(state.actions, "move:")
        local select_index = index_of(state.actions, "select:")
        local key_index = index_of(state.actions, "pressKey:")
        local pickup_index = index_of(state.actions, "lootpickup:")
        T.assert_not_nil(move_index, "move should run")
        T.assert_not_nil(select_index, "target should be selected")
        T.assert_not_nil(key_index, "key should open corpse")
        T.assert_not_nil(pickup_index, "LootPickup should run")
        T.assert_true(move_index < key_index, "move should happen before key")
        T.assert_true(select_index < key_index, "target select should happen before key")
        T.assert_true(key_index < pickup_index, "pickup should happen after key open")
    end)

    T.test("findNearestLootable chooses nearest lootable monster", function()
        local loot = install_loot_mocks({})
        local ok, found, err, meta = loot.findNearestLootable({
            char = { x = 0, y = 0, z = 0 },
            radius = 20,
            list = {
                { obj = 1, type = 2, lootable = 0, x = 1, y = 0, z = 0 },
                { obj = 2, type = 1, lootable = 1, x = 1, y = 0, z = 0 },
                { obj = 3, type = 2, lootable = 1, x = 8, y = 0, z = 0 },
                { obj = 4, type = 2, lootable = 1, x = 3, y = 0, z = 0 },
            },
        })

        T.assert_true(ok, err)
        T.assert_eq(found.obj, 4)
        T.assert_eq(meta.checked, 4)
        T.assert_eq(meta.accepted, 2)
    end)

    T.test("findNearestLootable skips ignored corpse keys", function()
        local loot = install_loot_mocks({})
        local ok, found, err = loot.findNearestLootable({
            char = { x = 0, y = 0, z = 0 },
            list = {
                { obj = 10, type = 2, lootable = 1, x = 1, y = 0, z = 0 },
                { obj = 11, type = 2, lootable = 1, x = 4, y = 0, z = 0 },
            },
            ignored = {
                [10] = true,
            },
        })

        T.assert_true(ok, err)
        T.assert_eq(found.obj, 11)
    end)

    return T.report("aion_loot")
end

return { run = run }
