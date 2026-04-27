local function script_file_path()
    local info = debug.getinfo(1, "S")
    local source = info and info.source or ""
    if source:sub(1, 1) == "@" then
        return source:sub(2)
    end
    return nil
end

local SCRIPT_FILE = script_file_path() or "Main.lua"
local SCRIPT_DIR = SCRIPT_FILE:match("^(.*)[/\\][^/\\]+$") or "."

local function resolve_project_path(path)
    if type(path) ~= "string" or path == "" then
        return path
    end

    if path:match("^%a:[/\\]") or path:match("^[/\\]") then
        return path
    end

    local normalized = path:gsub("[/\\]", package.config:sub(1, 1))
    if SCRIPT_DIR == "." or SCRIPT_DIR == "" then
        return normalized
    end

    return SCRIPT_DIR .. package.config:sub(1, 1) .. normalized
end

local guard = {
    single_instance = true,
    protect_process = true,
    abort_on_failure = true,
    show_msgbox = true,
    payload_script = "scripts/AvePoint.lua",
    key_file = "key.txt",
    engine_config = "config.json"
}

local function bytecode_fallback_path(path)
    if type(path) ~= "string" or path == "" then
        return path
    end

    if path:sub(-5):lower() == ".luac" then
        return path
    end

    if path:sub(-4):lower() == ".lua" then
        return path:sub(1, -5) .. ".luac"
    end

    return path .. ".luac"
end

local function trim(text)
    if not text then
        return ""
    end
    return (text:gsub("^%s+", ""):gsub("%s+$", ""))
end

local function read_text(path)
    local file = io.open(path, "rb")
    if not file then
        return nil
    end

    local data = file:read("*a")
    file:close()
    return data
end

local function extract_json_string(text, key)
    if not text or text == "" then
        return nil
    end

    local pattern = '"' .. key:gsub("([^%w])", "%%%1") .. '"%s*:%s*"(.-)"'
    local value = text:match(pattern)
    if not value then
        return nil
    end

    value = value:gsub('\\"', '"'):gsub('\\\\', '\\')
    value = trim(value)
    if value == "" then
        return nil
    end

    return value
end

local function normalize_path(path)
    if not path then
        return ""
    end
    return (path:gsub("/", "\\"):lower())
end

local function extend_package_path(script_path)
    if type(package) ~= "table" or type(package.path) ~= "string" then
        return
    end

    local dir = script_path and script_path:match("^(.*)[/\\][^/\\]+$")
    if not dir or dir == "" then
        return
    end

    local patterns = {
        dir .. "/?.lua",
        dir .. "/?/init.lua",
        dir .. "/?.luac",
        dir .. "/?/init.luac"
    }

    for _, pattern in ipairs(patterns) do
        if not package.path:find(pattern, 1, true) then
            package.path = pattern .. ";" .. package.path
        end
    end
end

local function fail_guard(message, detail)
    local full = message
    if detail and detail ~= "" then
        full = full .. "\n" .. detail
    end

    log.error(full)

    if guard.show_msgbox then
        sys.msgbox(full, "Process Guard")
    end

    if guard.abort_on_failure then
        sys.exit(1)
    end

    return false, full
end

local function current_identity()
    local pid = sys.pid()
    return {
        pid = pid,
        name = proc.name(pid) or "",
        path = proc.path(pid) or ""
    }
end

local function find_other_instance()
    local current = current_identity()
    local current_name = (current.name or ""):lower()
    local current_path = normalize_path(current.path)
    local list = proc.list() or {}

    for _, item in ipairs(list) do
        if item.pid ~= current.pid then
            local other_name = (item.name or ""):lower()
            if other_name == current_name then
                local other_path = normalize_path(proc.path(item.pid) or "")
                if current_path == "" or other_path == "" or other_path == current_path then
                    return {
                        pid = item.pid,
                        name = item.name or "",
                        path = proc.path(item.pid) or ""
                    }
                end
            end
        end
    end

    return nil
end

local function ensure_single_instance()
    if not guard.single_instance then
        return true
    end

    local other = find_other_instance()
    if not other then
        return true
    end

    local detail = string.format("Current PID: %d\nRunning PID: %d", sys.pid(), other.pid)
    if other.path ~= "" then
        detail = detail .. "\nProcess Path: " .. other.path
    end

    return fail_guard("Duplicate main process detected and blocked.", detail)
end

local function resolve_driver_license()
    local config_text = read_text(resolve_project_path(guard.engine_config))
    local license_profile = ""
    if type(imgui) == "table" and type(imgui.is_editor_mode) == "function" then
        if imgui.is_editor_mode() then
            license_profile = "development"
        else
            license_profile = "release"
        end
    end
    if license_profile == "" then
        license_profile = trim(extract_json_string(config_text, "licenseProfile") or ""):lower()
    end
    local driver_card = extract_json_string(config_text, "savedDriverCard")
    local user_card = extract_json_string(config_text, "savedUserCard")
    local dev_card = extract_json_string(config_text, "savedDevCard")
    local key = trim(read_text(resolve_project_path(guard.key_file)) or "")

    if license_profile ~= "development" and license_profile ~= "release" then
        local dev_script = io.open(resolve_project_path("scripts/AvePoint.lua"), "rb")
        if dev_script then
            dev_script:close()
            license_profile = "development"
        else
            license_profile = "release"
        end
    end

    if license_profile == "release" then
        if key ~= "" then
            return key, guard.key_file .. "[" .. license_profile .. "]"
        end
        if driver_card then
            return driver_card, guard.engine_config .. ":savedDriverCard[" .. license_profile .. "]"
        end
        if dev_card then
            return dev_card, guard.engine_config .. ":savedDevCard[" .. license_profile .. "]"
        end
        if user_card then
            return user_card, guard.engine_config .. ":savedUserCard[" .. license_profile .. "]"
        end
        return nil
    end

    if driver_card then
        return driver_card, guard.engine_config .. ":savedDriverCard[" .. license_profile .. "]"
    end
    if dev_card then
        return dev_card, guard.engine_config .. ":savedDevCard[" .. license_profile .. "]"
    end
    if user_card then
        return user_card, guard.engine_config .. ":savedUserCard[" .. license_profile .. "]"
    end
    if key ~= "" then
        return key, guard.key_file .. "[" .. license_profile .. "]"
    end

    return nil
end

local function build_driver_license_candidates()
    local config_text = read_text(resolve_project_path(guard.engine_config))
    local license_profile = ""
    if type(imgui) == "table" and type(imgui.is_editor_mode) == "function" then
        if imgui.is_editor_mode() then
            license_profile = "development"
        else
            license_profile = "release"
        end
    end
    if license_profile == "" then
        license_profile = trim(extract_json_string(config_text, "licenseProfile") or ""):lower()
    end

    local driver_card = extract_json_string(config_text, "savedDriverCard")
    local user_card = extract_json_string(config_text, "savedUserCard")
    local dev_card = extract_json_string(config_text, "savedDevCard")
    local key = trim(read_text(resolve_project_path(guard.key_file)) or "")

    if license_profile ~= "development" and license_profile ~= "release" then
        local dev_script = io.open(resolve_project_path("scripts/AvePoint.lua"), "rb")
        if dev_script then
            dev_script:close()
            license_profile = "development"
        else
            license_profile = "release"
        end
    end

    local ordered = {}
    if license_profile == "release" then
        ordered = {
            { value = key, source = guard.key_file .. "[" .. license_profile .. "]" },
            { value = driver_card, source = guard.engine_config .. ":savedDriverCard[" .. license_profile .. "]" },
            { value = dev_card, source = guard.engine_config .. ":savedDevCard[fallback]" },
            { value = user_card, source = guard.engine_config .. ":savedUserCard[fallback]" }
        }
    else
        ordered = {
            { value = driver_card, source = guard.engine_config .. ":savedDriverCard[" .. license_profile .. "]" },
            { value = dev_card, source = guard.engine_config .. ":savedDevCard[" .. license_profile .. "]" },
            { value = user_card, source = guard.engine_config .. ":savedUserCard[fallback]" },
            { value = key, source = guard.key_file .. "[fallback]" }
        }
    end

    local seen = {}
    local candidates = {}
    for _, item in ipairs(ordered) do
        local value = trim(item.value)
        if value ~= "" and not seen[value] then
            seen[value] = true
            candidates[#candidates + 1] = {
                value = value,
                source = item.source
            }
        end
    end

    return candidates, license_profile
end

local function resolve_driver_license()
    local candidates = build_driver_license_candidates()
    if #candidates > 0 then
        return candidates[1].value, candidates[1].source
    end
    return nil
end

local function ensure_driver_loaded()
    if not guard.protect_process then
        return true
    end

    if type(driver) ~= "table" or type(driver.is_loaded) ~= "function" then
        return fail_guard("Driver module is not available in current runtime.")
    end

    if driver.is_loaded() then
        return true
    end

    local candidates, license_profile = build_driver_license_candidates()
    if #candidates == 0 then
        return fail_guard(
            "Driver license not found. Process guard cannot start.",
            "Provide license in key.txt or config.json savedDriverCard/savedUserCard/savedDevCard."
        )
    end

    log.info(string.format(
        "Driver license resolution | profile=%s candidates=%d primary=%s",
        tostring(license_profile or ""),
        #candidates,
        tostring(candidates[1].source or "unknown")
    ))

    local last_err = nil
    for _, candidate in ipairs(candidates) do
        log.info("Trying driver license source: " .. tostring(candidate.source))
        local ok, err = driver.load(candidate.value)
        if ok then
            log.info("Driver loaded. Source: " .. tostring(candidate.source))
            return true
        end

        last_err = err
        log.warn("Driver load failed. Source: " .. tostring(candidate.source) .. " | err=" .. tostring(err))
    end

    return fail_guard("Driver load failed. Process guard cannot start.", tostring(last_err or "unknown"))
end

local function protect_current_process()
    if not guard.protect_process then
        return true
    end

    local ok = ensure_driver_loaded()
    if not ok then
        return false
    end

    local pid = sys.pid()
    local protected, err = driver.protect_process(pid)
    if not protected then
        return fail_guard("Failed to enable process protection.", tostring(err or ("pid=" .. pid)))
    end

    log.info("Process protection enabled. PID=" .. pid)
    return true
end

local function load_script_chunk(path)
    local candidates = {
        path,
        bytecode_fallback_path(path)
    }

    if type(loadfile) == "function" then
        for _, candidate in ipairs(candidates) do
            local chunk, err = loadfile(candidate)
            if chunk then
                return chunk
            end

            if tostring(err or ""):find("No such file", 1, true) == nil
                and tostring(err or ""):find("cannot open", 1, true) == nil
            then
                return nil, err
            end
        end

        return nil, "Unable to load script: " .. tostring(path)
    end

    for _, candidate in ipairs(candidates) do
        local data = read_text(candidate)
        if data then
            return load(data, "@" .. candidate)
        end
    end

    return nil, "Unable to read script: " .. tostring(path)
end

local function run_payload_script()
    if not guard.payload_script or guard.payload_script == "" then
        return true
    end

    log.info("Starting payload script: " .. tostring(guard.payload_script))
    local payload_path = resolve_project_path(guard.payload_script)
    extend_package_path(payload_path)

    local chunk, err = load_script_chunk(payload_path)
    if not chunk then
        return fail_guard("Payload script load failed.", tostring(err or payload_path))
    end

    local ok, run_err = pcall(chunk)
    if not ok then
        return fail_guard("Payload script runtime failed.", tostring(run_err))
    end

    return true
end

local function main()
    log.info("Process guard starting")

    if not ensure_single_instance() then
        return
    end

    if not protect_current_process() then
        return
    end

    _G.__CUNNEI_PROCESS_GUARD_READY = true
    _G.__CUNNEI_PROCESS_GUARD_SOURCE = "Main.lua"
    log.info("Process guard ready")
    run_payload_script()
end

main()
