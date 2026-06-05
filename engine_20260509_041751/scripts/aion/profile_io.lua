local M = {}

local function sorted_keys(tbl)
    local keys = {}
    for key in pairs(tbl or {}) do
        keys[#keys + 1] = key
    end
    table.sort(keys, function(a, b)
        local ta = type(a)
        local tb = type(b)
        if ta ~= tb then
            return ta < tb
        end
        return tostring(a) < tostring(b)
    end)
    return keys
end

local function is_identifier(key)
    return type(key) == "string" and string.match(key, "^[A-Za-z_][A-Za-z0-9_]*$") ~= nil
end

local function escape_string(value)
    return string.format("%q", tostring(value))
end

function M.clone(value, seen)
    if type(value) ~= "table" then
        return value
    end

    seen = seen or {}
    if seen[value] then
        return seen[value]
    end

    local out = {}
    seen[value] = out
    for key, item in pairs(value) do
        out[M.clone(key, seen)] = M.clone(item, seen)
    end
    return out
end

function M.merge(dst, src)
    if type(dst) ~= "table" or type(src) ~= "table" then
        return dst
    end

    for key, value in pairs(src) do
        if type(value) == "table" and type(dst[key]) == "table" then
            M.merge(dst[key], value)
        else
            dst[key] = M.clone(value)
        end
    end
    return dst
end

function M.serialize(value, indent, seen)
    indent = indent or 0
    seen = seen or {}

    local t = type(value)
    if t == "nil" then
        return "nil"
    elseif t == "number" or t == "boolean" then
        return tostring(value)
    elseif t == "string" then
        return escape_string(value)
    elseif t ~= "table" then
        return escape_string(tostring(value))
    end

    if seen[value] then
        error("cannot serialize recursive table")
    end
    seen[value] = true

    local pad = string.rep(" ", indent)
    local child_pad = string.rep(" ", indent + 4)
    local lines = { "{" }

    for _, key in ipairs(sorted_keys(value)) do
        local encoded_key = nil
        if is_identifier(key) then
            encoded_key = key
        else
            encoded_key = "[" .. M.serialize(key, 0, seen) .. "]"
        end

        lines[#lines + 1] = child_pad .. encoded_key .. " = "
            .. M.serialize(value[key], indent + 4, seen) .. ","
    end

    lines[#lines + 1] = pad .. "}"
    seen[value] = nil
    return table.concat(lines, "\n")
end

local function parent_dir(path)
    local normalized = tostring(path or ""):gsub("\\", "/")
    local dir = normalized:match("^(.*)/[^/]+$")
    if dir == "" then
        return nil
    end
    return dir
end

local function ensure_parent_dir(path)
    local dir = parent_dir(path)
    if not dir then
        return
    end

    local safe_dir = dir:gsub("\"", "")
    if safe_dir == "" then
        return
    end
    os.execute('mkdir "' .. safe_dir .. '" >nul 2>nul')
end

function M.write(path, value)
    ensure_parent_dir(path)

    local file, err = io.open(path, "w")
    if not file then
        return false, err or "open failed"
    end

    file:write("return ")
    file:write(M.serialize(value))
    file:write("\n")
    file:close()
    return true, nil
end

function M.read(path)
    local chunk, load_err = loadfile(path)
    if not chunk then
        return false, nil, load_err or "loadfile failed"
    end

    local ok, value = pcall(chunk)
    if not ok then
        return false, nil, tostring(value)
    end
    if type(value) ~= "table" then
        return false, nil, "profile file must return a table"
    end

    return true, value, nil
end

function M.package(kind, payload)
    return {
        schema_version = "1.0.0",
        generator = "aion_control_ui",
        exported_at = os.date("%Y-%m-%d %H:%M:%S"),
        kind = kind,
        payload = payload or {},
    }
end

function M.writePackage(path, kind, payload)
    return M.write(path, M.package(kind, payload))
end

function M.readPackage(path, expectedKind)
    local ok, package, err = M.read(path)
    if not ok then
        return false, nil, err
    end
    if expectedKind and package.kind ~= expectedKind then
        return false, nil, string.format(
            "kind mismatch: expected %s got %s",
            tostring(expectedKind),
            tostring(package.kind)
        )
    end
    return true, package, nil
end

return M
