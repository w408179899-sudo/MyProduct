function loadfile_with_bytecode_fallback(path, label)
    local candidates = { path }
    if type(path) == "string" and path ~= "" then
        if path:sub(-4):lower() == ".lua" then
            candidates[#candidates + 1] = path:sub(1, -5) .. ".luac"
        elseif path:sub(-5):lower() ~= ".luac" then
            candidates[#candidates + 1] = path .. ".luac"
        end
    end

    local last_err = nil
    local errors = {}
    for _, candidate in ipairs(candidates) do
        local chunk, err = loadfile(candidate)
        if chunk then
            return chunk
        end
        last_err = err
        errors[#errors + 1] = string.format("%s: %s", tostring(candidate), tostring(err))
    end

    local detail = #errors > 0 and table.concat(errors, "\n") or tostring(last_err)
    error(string.format("load %s failed:\n%s", tostring(label or path), detail))
end

local function load_nav_module()
    local ok, mod = pcall(require, "torch_nav")
    if ok then
        return mod
    end

    ok, mod = pcall(require, "scripts.torch_nav")
    if ok then
        return mod
    end

    local chunk = loadfile_with_bytecode_fallback("scripts/torch_nav.lua", "torch_nav")
    return chunk()
end

nav = load_nav_module()

local function load_human_mouse_module()
    local ok, mod = pcall(require, "human_mouse_v2")
    if ok then
        return mod
    end

    ok, mod = pcall(require, "scripts.human_mouse_v2")
    if ok then
        return mod
    end

    local ok_chunk, chunk = pcall(loadfile_with_bytecode_fallback, "scripts/human_mouse_v2.lua", "human_mouse_v2")
    if ok_chunk and type(chunk) == "function" then
        local ok_mod, loaded = pcall(chunk)
        if ok_mod and type(loaded) == "table" then
            return loaded
        end
    end

    ok, mod = pcall(require, "human_mouse")
    if ok then
        return mod
    end

    ok, mod = pcall(require, "scripts.human_mouse")
    if ok then
        return mod
    end

    local chunk = loadfile_with_bytecode_fallback("scripts/human_mouse.lua", "human_mouse")
    return chunk()
end

human_mouse = load_human_mouse_module()

if type(human_mouse) == "table" and type(human_mouse.configure) == "function" then
    pcall(human_mouse.configure, {
        profile = "careful",
        profile_overrides = {
            report_rate_hz = 36,
            overshoot_probability = 0.18,
            target_jitter_gain = 0.84,
            fatigue_noise_gain = 0.72,
            fatigue_speed_penalty = 0.30
        }
    })
end

local function script_file_path()
    local info = debug.getinfo(1, "S")
    local source = info and info.source or ""
    if source:sub(1, 1) == "@" then
        return source:sub(2)
    end
    return nil
end

SCRIPT_FILE = script_file_path() or "scripts/AvePoint.lua"
SCRIPT_DIR = SCRIPT_FILE:match("^(.*)[/\\][^/\\]+$") or "."
PROJECT_ROOT = SCRIPT_DIR:match("^(.*)[/\\]scripts$") or "."

HOTKEY_OWNER_LOCK_FILE = "avepoint_hotkey_owner.lock"
HOTKEY_OWNER_HEARTBEAT_MS = 1000

function resolve_project_path(path)
    if type(path) ~= "string" or path == "" then
        return path
    end

    if path:match("^%a:[/\\]") or path:match("^[/\\]") then
        return path
    end

    local normalized = path:gsub("[/\\]", package.config:sub(1, 1))
    if PROJECT_ROOT == "." or PROJECT_ROOT == "" then
        return normalized
    end

    return PROJECT_ROOT .. package.config:sub(1, 1) .. normalized
end

function valid_image(img)
    if not img then
        return false
    end

    if type(img.valid) == "function" then
        return img:valid()
    end

    return true
end

function free_image(img)
    if img and type(vision) == "table" and type(vision.free) == "function" then
        vision.free(img)
    end
end

local function load_avepoint_module(relative_path)
    local chunk = loadfile_with_bytecode_fallback(relative_path, relative_path)
    local ok, result = pcall(chunk)
    if not ok then
        error(string.format("load AvePoint module failed: %s | err=%s", tostring(relative_path), tostring(result)))
    end
    return result
end

local modules = {
    "scripts/avepoint/shared.lua",
    "scripts/avepoint/guard.lua",
    "scripts/avepoint/route.lua",
    "scripts/avepoint/interact.lua",
    "scripts/avepoint/hotkey.lua"
}

for _, module_path in ipairs(modules) do
    load_avepoint_module(module_path)
end

if type(main) ~= "function" then
    error("AvePoint main entry is not available after module load")
end

main()
