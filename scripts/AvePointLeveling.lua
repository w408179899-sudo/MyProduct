local function read_bundle_part(path)
    local file, open_err = io.open(path, "rb")
    if not file then
        error("failed to open AvePointLeveling bundle part " .. tostring(path) .. ": " .. tostring(open_err), 2)
    end
    local content = file:read("*a")
    file:close()
    return content
end

local part_paths = {
    "scripts/avepoint/leveling_bundle/AvePointLeveling.part01.chunk",
    "scripts/avepoint/leveling_bundle/AvePointLeveling.part02.chunk",
    "scripts/avepoint/leveling_bundle/AvePointLeveling.part03.chunk"
}

local parts = {}
for index, path in ipairs(part_paths) do
    parts[index] = read_bundle_part(path)
end

local source = table.concat(parts, "\n")
local load_chunk = loadstring or load
local chunk, load_err = load_chunk(source, "@scripts/AvePointLeveling.bundle.lua")
if not chunk then
    error("failed to load AvePointLeveling bundle: " .. tostring(load_err), 2)
end

return chunk()
