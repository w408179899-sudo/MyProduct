local core = require("aion.core")
local data = core.data

local M = {
    KIND_ITEM = 0x1,
    KIND_SKILL = 0x15,
}

function M.currentTarget()
    return core.first("AionData.GetCurrentTarget", data.GetCurrentTarget)
end

function M.selectTarget(targetObj)
    return core.first("AionData.SelectTarget", data.SelectTarget, targetObj)
end

function M.skillList()
    local ok, list, err = core.first("AionData.GetSkillList", data.GetSkillList)
    if not ok then
        return false, nil, err
    end
    return true, list or {}, nil
end

function M.buffList()
    local ok, list, err = core.first("AionData.GetBuffList", data.GetBuffList)
    if not ok then
        return false, nil, err
    end
    return true, list or {}, nil
end

function M.autoActiveSkills()
    local ok, list, err = core.first("AionData.GetAutoActiveSkills", data.GetAutoActiveSkills)
    if not ok then
        return false, nil, err
    end
    return true, list or {}, nil
end

function M.autoPassiveSkills()
    local ok, list, err = core.first("AionData.GetAutoPassiveSkills", data.GetAutoPassiveSkills)
    if not ok then
        return false, nil, err
    end
    return true, list or {}, nil
end

function M.skillType(skillId)
    return core.first("AionData.GetSkillType", data.GetSkillType, skillId)
end

function M.rebuildSkillTypeMap()
    return core.first("AionData.RebuildSkillTypeMap", data.RebuildSkillTypeMap)
end

function M.autoBattleStatus()
    return core.first("AionData.GetAutoBattleStatus", data.GetAutoBattleStatus)
end

function M.isAutoBattleOn()
    return core.first("AionData.IsAutoBattleOn", data.IsAutoBattleOn)
end

function M.autoBattleOn()
    return core.first("AionData.AutoBattleOn", data.AutoBattleOn)
end

function M.autoBattleOff()
    return core.first("AionData.AutoBattleOff", data.AutoBattleOff)
end

function M.skillAutoOn(skillId, kind)
    return core.first("AionData.SkillAutoOn", data.SkillAutoOn, skillId, kind or M.KIND_SKILL)
end

function M.skillAutoOff(skillId, kind)
    return core.first("AionData.SkillAutoOff", data.SkillAutoOff, skillId, kind or M.KIND_SKILL)
end

return M
