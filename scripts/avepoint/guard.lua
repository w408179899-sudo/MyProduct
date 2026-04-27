function guard_fail(message, detail)
    local full = message
    if detail and detail ~= "" then
        full = full .. "\n" .. detail
    end
    log.error(full)
    return false, full
end

function resolve_driver_license()
    local config_path = resolve_project_path(guard.engine_config)
    local config_text = read_text(config_path)
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

function ensure_driver_loaded()
    if not guard.protect_process then
        return true
    end

    if type(driver) ~= "table" or type(driver.is_loaded) ~= "function" then
        return guard_fail("Driver module is not available in current runtime.")
    end

    if driver.is_loaded() then
        return true
    end

    local candidates, license_profile = resolve_driver_license()
    if #candidates == 0 then
        return guard_fail(
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

    return guard_fail("Driver load failed. Process guard cannot start.", tostring(last_err or "unknown"))
end

function protect_current_process()
    if not guard.protect_process then
        return true
    end

    local ok = ensure_driver_loaded()
    if not ok then
        return false
    end

    if type(driver.protect_process) ~= "function" then
        return guard_fail("driver.protect_process is not available.")
    end

    local pid = sys.pid()
    local protected, err = driver.protect_process(pid)
    if not protected then
        return guard_fail("Failed to enable process protection.", tostring(err or ("pid=" .. pid)))
    end

    log.info("Process protection enabled. PID=" .. pid)
    return true
end
