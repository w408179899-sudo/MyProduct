local function script_file_path()
    local info = debug.getinfo(1, "S")
    local source = info and info.source or ""
    if source:sub(1, 1) == "@" then
        return source:sub(2)
    end
    return nil
end

local SCRIPT_FILE = script_file_path() or "AvePointStandalone.lua"
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

local ENTRY_SCRIPT = resolve_project_path("scripts/AvePoint.lua")

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

local function read_text(path)
    local file = io.open(path, "rb")
    if not file then
        return nil
    end

    local data = file:read("*a")
    file:close()
    return data
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

local function main()
    log.info("AvePoint standalone entry starting")
    extend_package_path(ENTRY_SCRIPT)

    local chunk, err = load_script_chunk(ENTRY_SCRIPT)
    if not chunk then
        error("AvePoint standalone load failed: " .. tostring(err or ENTRY_SCRIPT))
    end

    return chunk()
end

main()
