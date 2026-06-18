local Result = {}

function Result.success(data)
    return { ok = true, status = "success", data = data }
end

function Result.failure(reason, data)
    return { ok = false, status = "failure", reason = reason or "failed", data = data }
end

function Result.running(data)
    return { ok = nil, status = "running", data = data }
end

return Result
