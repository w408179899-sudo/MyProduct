local core = require("aion.core")
local data = core.data

local M = {}

function M.secondPwdDialog()
    return core.first("AionData.GetSecondPwdDialog", data.GetSecondPwdDialog)
end

function M.inputSecondPwd(dialogAddr, pwd, register)
    return core.first("AionData.InputSecondPwd", data.InputSecondPwd, dialogAddr, pwd, register)
end

function M.selectBoxCandidates()
    local ok, list, err = core.first("AionData.GetSelectBoxCandidates", data.GetSelectBoxCandidates)
    if not ok then
        return false, nil, err
    end
    return true, list or {}, nil
end

function M.claimSelectBox(boxId, itemIndex)
    return core.first("AionData.ClaimSelectBox", data.ClaimSelectBox, boxId, itemIndex)
end

function M.genOtp(secret)
    local ok, values, err = core.call("AionData.GenOTP", data.GenOTP, secret)
    if not ok then
        return false, nil, err
    end
    return true, { code = values[1], remain = values[2], err = values[3] }, nil
end

return M
