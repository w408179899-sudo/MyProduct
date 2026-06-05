local core = require("aion.core")
local entity = require("aion.entity")
local inventory = require("aion.inventory")
local ui = require("aion.ui")
local npc = require("aion.npc")
local shop = require("aion.shop")
local quest = require("aion.quest")
local map = require("aion.map")
local combat = require("aion.combat")
local nav = require("aion.nav")
local account = require("aion.account")
local channel = require("aion.channel")
local remote = require("aion.remote")
local loot = require("aion.loot")
local security = require("aion.security")
local data = core.data

local M = {}

local function log_msg(level, msg)
    if log and type(log[level]) == "function" then
        log[level](msg)
    elseif print then
        print(msg)
    end
end

local function pass(detail)
    return { status = "PASS", detail = detail or "" }
end

local function warn(detail)
    return { status = "WARN", detail = detail or "" }
end

local function fail(detail)
    return { status = "FAIL", detail = detail or "" }
end

local function count(list)
    if type(list) ~= "table" then
        return 0
    end
    local n = 0
    for _ in ipairs(list) do
        n = n + 1
    end
    return n
end

local function format_bool(value)
    return value and "true" or "false"
end

local function checkCall(fn, formatter, nilText)
    local ok, value, err = fn()
    if not ok then
        return fail(err)
    end
    if value == nil then
        return warn(nilText or "nil")
    end
    if formatter then
        return pass(formatter(value))
    end
    return pass(tostring(value))
end

local tests = {
    {
        name = "core.ensureInit",
        run = function()
            local ok, err = core.ensureInit(M.current_pid)
            if not ok then
                return fail(err)
            end
            return pass("initialized")
        end,
    },
    {
        name = "core.getState",
        run = function()
            return checkCall(core.getState, function(state)
                return string.format("pid=%s hwnd=%s inited=%s", tostring(state.pid), tostring(state.hwnd), format_bool(state.inited))
            end)
        end,
    },
    {
        name = "core.getScene",
        run = function()
            return checkCall(core.getScene, function(scene)
                return string.format("idx=%s name=%s", tostring(scene.index), tostring(scene.name))
            end)
        end,
    },
    {
        name = "account.serverList",
        run = function()
            return checkCall(account.serverList, function(list)
                return "count=" .. tostring(count(list))
            end)
        end,
    },
    {
        name = "account.currentServerId",
        run = function()
            return checkCall(account.currentServerId, function(id)
                return "server_id=" .. tostring(id)
            end)
        end,
    },
    {
        name = "account.characterList",
        run = function()
            return checkCall(account.characterList, function(list)
                return "count=" .. tostring(count(list))
            end)
        end,
    },
    {
        name = "core.getCharacter",
        run = function()
            return checkCall(core.getCharacter, function(char)
                return string.format("name=%s level=%s hp=%s/%s pos=%.1f,%.1f,%.1f",
                    tostring(char.name), tostring(char.level), tostring(char.hp), tostring(char.mhp),
                    char.x or 0, char.y or 0, char.z or 0)
            end, "not in game scene")
        end,
    },
    {
        name = "map.current",
        run = function()
            return checkCall(map.current, function(cur)
                return string.format("index=%s region=%s name=%s", tostring(cur.index), tostring(cur.region), tostring(cur.name_en))
            end, "current map unavailable")
        end,
    },
    {
        name = "map.bigMapId",
        run = function()
            return checkCall(map.bigMapId, function(id)
                return "big_map_id=" .. tostring(id)
            end)
        end,
    },
    {
        name = "map.nodes",
        run = function()
            local ok, bigId, err = map.bigMapId()
            if not ok then
                return fail(err)
            end
            local listOk, list, listErr = map.nodes(bigId ~= 0 and bigId or nil)
            if not listOk then
                return fail(listErr)
            end
            return pass("count=" .. tostring(count(list)))
        end,
    },
    {
        name = "map.bigMapTeleports",
        run = function()
            local eOk, elyos, eErr = map.bigMapTeleportsForRace(0)
            if not eOk then
                return fail("elyos: " .. tostring(eErr))
            end
            local aOk, asmodian, aErr = map.bigMapTeleportsForRace(1)
            if not aOk then
                return fail("asmodian: " .. tostring(aErr))
            end
            return pass(string.format("elyos=%d asmodian=%d", count(elyos), count(asmodian)))
        end,
    },
    {
        name = "channel.info",
        run = function()
            local ok, info, err = channel.info()
            if not ok then
                return fail(err)
            end
            if not info then
                return warn("not available")
            end
            return pass(string.format("current=%s count=%s",
                tostring(info.current or info.current_channel or info.index),
                tostring(info.count or info.channel_count or "")))
        end,
    },
    {
        name = "entity.list",
        run = function()
            return checkCall(entity.list, function(list)
                return "count=" .. tostring(count(list))
            end)
        end,
    },
    {
        name = "entity.nearestLootable",
        run = function()
            local ok, target, err = entity.nearestLootable()
            if not ok then
                return fail(err)
            end
            if not target then
                return warn("none")
            end
            return pass(string.format("name=%s distance=%.1f", tostring(target.name), target.distance or 0))
        end,
    },
    {
        name = "inventory.list",
        run = function()
            return checkCall(inventory.list, function(list)
                return "count=" .. tostring(count(list))
            end)
        end,
    },
    {
        name = "inventory.kinah",
        run = function()
            return checkCall(inventory.kinah, function(value)
                return "kinah=" .. tostring(value)
            end)
        end,
    },
    {
        name = "combat.skillList",
        run = function()
            return checkCall(combat.skillList, function(list)
                return "count=" .. tostring(count(list))
            end)
        end,
    },
    {
        name = "combat.buffList",
        run = function()
            return checkCall(combat.buffList, function(list)
                return "count=" .. tostring(count(list))
            end)
        end,
    },
    {
        name = "combat.autoActiveSkills",
        run = function()
            return checkCall(combat.autoActiveSkills, function(list)
                return "count=" .. tostring(count(list))
            end)
        end,
    },
    {
        name = "combat.autoPassiveSkills",
        run = function()
            return checkCall(combat.autoPassiveSkills, function(list)
                return "count=" .. tostring(count(list))
            end)
        end,
    },
    {
        name = "combat.skillType.sample",
        run = function()
            local ok, list, err = combat.skillList()
            if not ok then
                return warn(err)
            end
            local first = list and list[1]
            if not first then
                return warn("no learned skill")
            end
            local typeOk, value, typeErr = combat.skillType(first.id)
            if not typeOk then
                return fail(typeErr)
            end
            return pass(string.format("id=%s type=%s", tostring(first.id), tostring(value)))
        end,
    },
    {
        name = "combat.currentTarget",
        run = function()
            local ok, target, err = combat.currentTarget()
            if not ok then
                return fail(err)
            end
            if not target then
                return warn("none")
            end
            return pass("id=" .. tostring(target.id))
        end,
    },
    {
        name = "combat.autoBattleStatus",
        run = function()
            return checkCall(combat.autoBattleStatus, function(status)
                return string.format("state=%s gather=%s arrange=%s priority=%s",
                    tostring(status.state_name or status.state),
                    format_bool(status.gather_search),
                    format_bool(status.auto_arrange),
                    tostring(status.target_priority_name or status.target_priority))
            end)
        end,
    },
    {
        name = "combat.isAutoBattleOn",
        run = function()
            return checkCall(combat.isAutoBattleOn, function(value)
                return "enabled=" .. format_bool(value)
            end)
        end,
    },
    {
        name = "quest.list",
        run = function()
            return checkCall(quest.list, function(list)
                return "count=" .. tostring(count(list))
            end)
        end,
    },
    {
        name = "quest.completed",
        run = function()
            return checkCall(quest.completed, function(list)
                return "count=" .. tostring(count(list))
            end)
        end,
    },
    {
        name = "quest.achievementList",
        run = function()
            return checkCall(quest.achievementList, function(list)
                return "count=" .. tostring(count(list))
            end)
        end,
    },
    {
        name = "ui.list",
        run = function()
            return checkCall(function()
                return ui.list(false)
            end, function(list)
                return "count=" .. tostring(count(list))
            end)
        end,
    },
    {
        name = "ui.find.dlg_dialog",
        run = function()
            local ok, dlg, err = ui.find("dlg_dialog")
            if not ok then
                return fail(err)
            end
            if not dlg then
                return warn("not found")
            end
            return pass(string.format("addr=%s visible=%s", tostring(dlg.addr), format_bool(dlg.visible)))
        end,
    },
    {
        name = "ui.children.dlg_dialog",
        run = function()
            return checkCall(function()
                return ui.children("dlg_dialog", 4)
            end, function(list)
                return "count=" .. tostring(count(list))
            end)
        end,
    },
    {
        name = "npc.dialog",
        run = function()
            local ok, info, err = npc.dialog()
            if not ok then
                return fail(err)
            end
            if not info then
                return warn("no open dialog")
            end
            return pass(string.format("npc_dialog_id=%s type=%s quest=%s",
                tostring(info.npc_dialog_id), tostring(info.type_text), tostring(info.quest_id)))
        end,
    },
    {
        name = "shop.list",
        run = function()
            return checkCall(shop.list, function(list)
                return "count=" .. tostring(count(list))
            end)
        end,
    },
    {
        name = "shop.staticList",
        run = function()
            local eOk, elyos, eErr = shop.staticListForRace(0)
            if not eOk then
                return fail("elyos: " .. tostring(eErr))
            end
            local aOk, asmodian, aErr = shop.staticListForRace(1)
            if not aOk then
                return fail("asmodian: " .. tostring(aErr))
            end
            return pass(string.format("elyos=%d asmodian=%d", count(elyos), count(asmodian)))
        end,
    },
    {
        name = "security.secondPwdDialog",
        run = function()
            return checkCall(security.secondPwdDialog, function(info)
                return string.format("addr=%s title=%s", tostring(info.addr), tostring(info.title))
            end)
        end,
    },
    {
        name = "security.selectBoxCandidates",
        run = function()
            return checkCall(security.selectBoxCandidates, function(list)
                return "count=" .. tostring(count(list))
            end)
        end,
    },
    {
        name = "nav.load",
        run = function()
            local ok, _, err = nav.load()
            if not ok then
                return warn(err)
            end
            return pass(nav.defaultMap)
        end,
    },
    {
        name = "nav.findRoute.self",
        run = function()
            local ok, pos, err = core.getPosition()
            if not ok then
                return warn(err)
            end
            local routeOk, route, routeErr = nav.findRoute(pos, pos, 0)
            if not routeOk then
                return fail(routeErr)
            end
            if not route then
                return warn("route nil")
            end
            return pass("count=" .. tostring(count(route)))
        end,
    },
    {
        name = "actionWrappers.callable",
        run = function()
            local required = {
                nav.moveTo,
                nav.navigateTo,
                npc.interactByName,
                npc.sendDialog,
                shop.buyByName,
                inventory.useByName,
                inventory.equipByName,
                inventory.decomposeByCategory,
                quest.openSubmit,
                quest.questTeleportId,
                quest.questTeleport,
                quest.taskTeleport,
                map.nodeTeleport,
                map.bigMapTeleport,
                combat.selectTarget,
                combat.autoBattleOn,
                combat.autoBattleOff,
                combat.skillType,
                combat.rebuildSkillTypeMap,
                account.selectServer,
                account.selectCharacter,
                account.createCharacter,
                channel.switch,
                remote.pressKey,
                remote.placeQuickbar,
                remote.returnCharacter,
                loot.pickup,
                security.inputSecondPwd,
                security.claimSelectBox,
                security.genOtp,
            }
            for i, fn in ipairs(required) do
                if type(fn) ~= "function" then
                    return fail("missing action wrapper #" .. tostring(i))
                end
            end
            return pass("all callable")
        end,
    },
}

function M.run(opts)
    opts = opts or {}
    local requested_pid = tonumber(opts.pid) or 0
    M.current_pid = requested_pid > 0 and requested_pid or nil
    local results = {}
    local summary = { PASS = 0, WARN = 0, FAIL = 0 }

    log_msg("info", "=== Aion API probe start ===" ..
        (M.current_pid and (" pid=" .. tostring(M.current_pid)) or " pid=auto"))
    for _, test in ipairs(tests) do
        local started = os.clock()
        local ok, result = pcall(test.run)
        local elapsed = math.floor((os.clock() - started) * 1000)

        if not ok then
            result = fail(tostring(result))
        end
        result.name = test.name
        result.elapsed = elapsed
        results[#results + 1] = result

        summary[result.status] = (summary[result.status] or 0) + 1
        local line = string.format("[API-PROBE] %-4s %-28s %4dms %s",
            result.status, test.name, elapsed, result.detail or "")

        if result.status == "FAIL" then
            log_msg("error", line)
        elseif result.status == "WARN" then
            log_msg("warn", line)
        else
            log_msg("info", line)
        end
    end

    local total = #results
    log_msg("info", string.format("=== Aion API probe done: total=%d pass=%d warn=%d fail=%d ===",
        total, summary.PASS or 0, summary.WARN or 0, summary.FAIL or 0))

    M.current_pid = nil
    return results, summary
end

return M
