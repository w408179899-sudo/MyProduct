local ok_core, default_core = pcall(require, "aion.core")
local ok_quest, default_quest = pcall(require, "aion.quest")
local ok_map, default_map = pcall(require, "aion.map")

local M = {
    STATUS_DOING = 3,
    STATUS_DONE = 4,
    STATUS_LEVEL_BLOCKED = 6,
    MISSION_TAB = "\228\189\191\229\145\189",
}

local function value_or_empty(value)
    if value == nil then
        return ""
    end
    return tostring(value)
end

local function format_number(value, digits)
    value = tonumber(value)
    if value == nil then
        return ""
    end
    return string.format("%." .. tostring(tonumber(digits) or 1) .. "f", value)
end

local function count_ipairs(list)
    local count = 0
    if type(list) == "table" then
        for _ in ipairs(list) do
            count = count + 1
        end
    end
    return count
end

local function status_code(quest)
    if type(quest) ~= "table" then
        return nil
    end
    return tonumber(quest.status_code)
end

local function append_line(lines, line)
    lines[#lines + 1] = tostring(line or "")
end

local function read_api(api, fn_name)
    if type(api) ~= "table" or type(api[fn_name]) ~= "function" then
        return false, nil, fn_name .. " unavailable"
    end
    return api[fn_name]()
end

function M.isMainQuest(quest)
    if type(quest) ~= "table" then
        return false
    end
    local tab_name = tostring(quest.tab_name or "")
    return tab_name == M.MISSION_TAB
        or tab_name == "mission"
        or tab_name == "main"
        or quest.is_main == true
end

function M.formatCharacter(char)
    if type(char) ~= "table" then
        return "character none"
    end
    return string.format(
        "character name=%s level=%s race=%s/%s gender=%s/%s job=%s hp=%s/%s mp=%s/%s exp=%s/%s dead=%s pos=%s,%s,%s id=%s obj=%s",
        value_or_empty(char.name),
        value_or_empty(char.level),
        value_or_empty(char.race),
        value_or_empty(char.race_name),
        value_or_empty(char.gender),
        value_or_empty(char.gender_name),
        value_or_empty(char.job),
        value_or_empty(char.hp),
        value_or_empty(char.mhp or char.max_hp),
        value_or_empty(char.mp),
        value_or_empty(char.mmp or char.max_mp),
        value_or_empty(char.exp),
        value_or_empty(char.max_exp),
        tostring(char.dead == true),
        format_number(char.x, 2),
        format_number(char.y, 2),
        format_number(char.z, 2),
        value_or_empty(char.id),
        value_or_empty(char.obj or char.IEntity))
end

function M.formatMap(current_map, big_map_id)
    if type(current_map) ~= "table" then
        return "map current none big_map_id=" .. value_or_empty(big_map_id)
    end
    return string.format(
        "map current index=%s big_map_id=%s region=%s name_en=%s name_cn=%s level=%s",
        value_or_empty(current_map.index),
        value_or_empty(big_map_id),
        value_or_empty(current_map.region),
        value_or_empty(current_map.name_en),
        value_or_empty(current_map.name_cn),
        value_or_empty(current_map.level))
end

function M.formatQuestFields(quest)
    quest = type(quest) == "table" and quest or {}
    return string.format(
        "id=%s tab=%s tab_name=%s status_code=%s status_name=%s req_count=%s seq=%s quest_addr=%s elems=%s lv_text=%s lv_num=%s item=%s/%s exp_reward=%s kinah=%s name=%s",
        value_or_empty(quest.id),
        value_or_empty(quest.tab),
        value_or_empty(quest.tab_name),
        value_or_empty(quest.status_code),
        value_or_empty(quest.status_name or quest.status),
        value_or_empty(quest.req_count),
        value_or_empty(quest.seq),
        value_or_empty(quest.quest),
        value_or_empty(quest.elems),
        value_or_empty(quest.lv_text),
        value_or_empty(quest.lv_num),
        value_or_empty(quest.item_id),
        value_or_empty(quest.item_count),
        value_or_empty(quest.exp_reward),
        value_or_empty(quest.kinah),
        value_or_empty(quest.name))
end

function M.formatQuest(index, quest)
    return string.format("quest[%s] %s", tostring(index), M.formatQuestFields(quest))
end

function M.selectCurrentMainQuest(main_quests)
    local fallback = nil
    for _, quest in ipairs(main_quests or {}) do
        fallback = fallback or quest
        if status_code(quest) == M.STATUS_DOING then
            return quest
        end
    end
    for _, quest in ipairs(main_quests or {}) do
        if status_code(quest) ~= M.STATUS_DONE then
            return quest
        end
    end
    return fallback
end

function M.buildMainQuestSnapshot(quest_list)
    local snapshot = { quests = {}, total = 0, doing = 0, ready = 0, level_blocked = 0 }
    for _, quest in ipairs(quest_list or {}) do
        if M.isMainQuest(quest) then
            snapshot.quests[#snapshot.quests + 1] = quest
            local code = status_code(quest)
            if code == M.STATUS_DOING then
                snapshot.doing = snapshot.doing + 1
            elseif code == M.STATUS_DONE then
                snapshot.ready = snapshot.ready + 1
            elseif code == M.STATUS_LEVEL_BLOCKED then
                snapshot.level_blocked = snapshot.level_blocked + 1
            end
        end
    end
    snapshot.total = #snapshot.quests
    snapshot.current = M.selectCurrentMainQuest(snapshot.quests)
    return snapshot
end

function M.formatMainQuestSnapshot(snapshot)
    snapshot = type(snapshot) == "table" and snapshot or M.buildMainQuestSnapshot({})
    local current = snapshot.current or {}
    local lines = {
        string.format(
            "main_quest.snapshot total=%s doing=%s ready=%s level_blocked=%s current_id=%s current_step=%s current_status=%s current_name=%s",
            value_or_empty(snapshot.total or 0),
            value_or_empty(snapshot.doing or 0),
            value_or_empty(snapshot.ready or 0),
            value_or_empty(snapshot.level_blocked or 0),
            value_or_empty(current.id),
            value_or_empty(current.req_count),
            value_or_empty(current.status_name or current.status_code),
            value_or_empty(current.name))
    }
    for index, quest in ipairs(snapshot.quests or {}) do
        lines[#lines + 1] = string.format("main_quest[%s] %s", tostring(index), M.formatQuestFields(quest))
    end
    return lines
end

function M.read(deps)
    deps = type(deps) == "table" and deps or {}
    local core = deps.core or (ok_core and default_core or nil)
    local quest_api = deps.quest or (ok_quest and default_quest or nil)
    local map_api = deps.map or (ok_map and default_map or nil)
    local lines = {}
    local errors = 0
    local char, current_map, big_map_id, quest_list

    local char_ok, char_value, char_err = read_api(core, "getCharacter")
    if char_ok then
        char = char_value
        append_line(lines, M.formatCharacter(char))
    else
        errors = errors + 1
        append_line(lines, "character err=" .. value_or_empty(char_err))
    end

    local map_ok, map_value, map_err = read_api(map_api, "current")
    if map_ok then
        current_map = map_value
    else
        errors = errors + 1
        append_line(lines, "map current err=" .. value_or_empty(map_err))
    end
    local big_ok, big_value, big_err = read_api(map_api, "bigMapId")
    if big_ok then
        big_map_id = big_value
    else
        errors = errors + 1
        append_line(lines, "map big_map_id err=" .. value_or_empty(big_err))
    end
    if map_ok or big_ok then
        append_line(lines, M.formatMap(current_map, big_map_id))
    end

    local quest_ok, quest_value, quest_err = read_api(quest_api, "list")
    if quest_ok and type(quest_value) == "table" then
        quest_list = quest_value
        append_line(lines, "GetQuestList() count=" .. tostring(count_ipairs(quest_list)))
        for index, quest_item in ipairs(quest_list) do
            append_line(lines, M.formatQuest(index, quest_item))
        end
        local snapshot_lines = M.formatMainQuestSnapshot(M.buildMainQuestSnapshot(quest_list))
        for _, line in ipairs(snapshot_lines) do
            append_line(lines, line)
        end
    else
        errors = errors + 1
        append_line(lines, "GetQuestList() err=" .. value_or_empty(quest_err or "invalid list"))
        append_line(lines, "main_quest.snapshot unavailable")
    end

    local status = errors > 0 and "partial" or "ok"
    local summary = string.format(
        "character=%s level=%s map=%s quests=%s main=%s",
        value_or_empty(char and char.name),
        value_or_empty(char and char.level),
        value_or_empty(current_map and (current_map.region or current_map.name_en)),
        value_or_empty(quest_list and count_ipairs(quest_list) or 0),
        value_or_empty(quest_list and M.buildMainQuestSnapshot(quest_list).total or 0))

    return true, { status = status, summary = summary, lines = lines }, nil
end

return M
