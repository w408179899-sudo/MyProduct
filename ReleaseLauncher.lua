local MAIN_SCRIPT = "Main.luac"
local AVEPOINT_SCRIPT = "scripts/AvePoint.luac"
local MAIN_TASK_NAME = "Main.luac"
local AVEPOINT_TASK_NAME = "AvePoint.luac"
local START_TIMEOUT_MS = 8000
local START_POLL_MS = 100
local AVEPOINT_START_DELAY_MS = 500
local MONITOR_POLL_MS = 500

local main_task_id = nil
local avepoint_task_id = nil

local function task_status(id)
    if not id or type(task) ~= "table" or type(task.status) ~= "function" then
        return nil
    end
    return task.status(id)
end

local function task_error(id)
    if not id or type(task) ~= "table" or type(task.info) ~= "function" then
        return ""
    end

    local info = task.info(id)
    if type(info) ~= "table" then
        return ""
    end

    local err = info.error
    if err == nil or err == "" then
        return ""
    end

    return tostring(err)
end

local function stop_task_if_running(id)
    local status = task_status(id)
    if status == "running" or status == "pending" or status == "paused" then
        task.stop(id)
    end
end

local function build_compiled_loader_script(path)
    return string.format([[
local script_path = %q
local chunk, err = loadfile(script_path)
if not chunk then
    error("load compiled script failed [" .. tostring(script_path) .. "]: " .. tostring(err))
end
return chunk()
]], path)
end

local function start_child(path, task_name)
    local id = task.run(build_compiled_loader_script(path), {
        name = task_name
    })
    if not id then
        return nil, "task.run returned nil"
    end

    return id
end

local function wait_child_running(id, label, timeout_ms)
    local deadline = sys.time() + math.max(0, tonumber(timeout_ms) or 0)
    local last_status = nil

    while sys.time() <= deadline do
        local status = task_status(id)
        if status == "running" then
            return true
        end

        if status ~= last_status then
            last_status = status
            log.info(string.format(
                "Release launcher waiting child | task=%s id=%s status=%s",
                tostring(label),
                tostring(id),
                tostring(status)
            ))
        end

        if status == "completed" or status == "cancelled" or status == nil then
            local err = task_error(id)
            if err ~= "" then
                return false, string.format(
                    "%s stopped during startup | status=%s error=%s",
                    tostring(label),
                    tostring(status),
                    err
                )
            end

            return false, string.format(
                "%s stopped during startup | status=%s",
                tostring(label),
                tostring(status)
            )
        end

        sys.sleep(START_POLL_MS)
    end

    return false, string.format(
        "%s startup timeout | timeout=%dms status=%s",
        tostring(label),
        math.max(0, tonumber(timeout_ms) or 0),
        tostring(task_status(id))
    )
end

local function child_ended_message(label, id)
    local status = task_status(id)
    local err = task_error(id)
    if err ~= "" then
        return string.format(
            "%s ended | id=%s status=%s error=%s",
            tostring(label),
            tostring(id),
            tostring(status),
            err
        )
    end

    return string.format(
        "%s ended | id=%s status=%s",
        tostring(label),
        tostring(id),
        tostring(status)
    )
end

if type(task) == "table" and type(task.on_stop) == "function" then
    task.on_stop(function()
        stop_task_if_running(avepoint_task_id)
        stop_task_if_running(main_task_id)
    end)
end

local function main()
    log.info("Release launcher starting")

    local main_id, main_err = start_child(MAIN_SCRIPT, MAIN_TASK_NAME)
    if not main_id then
        error("Start child failed [Main.luac]: " .. tostring(main_err))
    end
    main_task_id = main_id

    local ok, wait_err = wait_child_running(main_task_id, MAIN_TASK_NAME, START_TIMEOUT_MS)
    if not ok then
        stop_task_if_running(main_task_id)
        error(wait_err)
    end

    sys.sleep(AVEPOINT_START_DELAY_MS)

    local ave_id, ave_err = start_child(AVEPOINT_SCRIPT, AVEPOINT_TASK_NAME)
    if not ave_id then
        stop_task_if_running(main_task_id)
        error("Start child failed [AvePoint.luac]: " .. tostring(ave_err))
    end
    avepoint_task_id = ave_id

    ok, wait_err = wait_child_running(avepoint_task_id, AVEPOINT_TASK_NAME, START_TIMEOUT_MS)
    if not ok then
        stop_task_if_running(avepoint_task_id)
        stop_task_if_running(main_task_id)
        error(wait_err)
    end

    log.info(string.format(
        "Release launcher ready | main_task=%s avepoint_task=%s",
        tostring(main_task_id),
        tostring(avepoint_task_id)
    ))

    while true do
        local main_status = task_status(main_task_id)
        local avepoint_status = task_status(avepoint_task_id)

        local main_alive = main_status == "running" or main_status == "pending" or main_status == "paused"
        local avepoint_alive = avepoint_status == "running" or avepoint_status == "pending" or avepoint_status == "paused"

        if not main_alive then
            log.error(child_ended_message(MAIN_TASK_NAME, main_task_id))
            stop_task_if_running(avepoint_task_id)
            break
        end

        if not avepoint_alive then
            log.error(child_ended_message(AVEPOINT_TASK_NAME, avepoint_task_id))
            stop_task_if_running(main_task_id)
            break
        end

        sys.sleep(MONITOR_POLL_MS)
    end
end

main()
