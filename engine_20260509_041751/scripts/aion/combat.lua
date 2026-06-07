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

function M.autoBuffSkills()
    local ok, list, err = core.first("AionData.GetAutoBuffSkills", data.GetAutoBuffSkills)
    if not ok then
        return false, nil, err
    end
    return true, list or {}, nil
end

-- API 3.5 renamed auto passive skills to auto buff skills.
function M.autoPassiveSkills()
    return M.autoBuffSkills()
end

function M.skillType(skillId)
    return core.first("AionData.GetSkillType", data.GetSkillType, skillId)
end

function M.isSkillAuto(skillId)
    return core.first("AionData.IsSkillAuto", data.IsSkillAuto, skillId)
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

function M.skillAutoToggle(skillId, kind)
    return core.first("AionData.SkillAutoToggle", data.SkillAutoToggle, skillId, kind or M.KIND_SKILL)
end

return M
