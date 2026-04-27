local function loadfile_with_bytecode_fallback(path, label)
    local candidates = { path }
    if type(path) == "string" and path ~= "" then
        if path:sub(-4):lower() == ".lua" then
            candidates[#candidates + 1] = path:sub(1, -5) .. ".luac"
        elseif path:sub(-5):lower() ~= ".luac" then
            candidates[#candidates + 1] = path .. ".luac"
        end
    end

    local last_err = nil
    for _, candidate in ipairs(candidates) do
        local chunk, err = loadfile(candidate)
        if chunk then
            return chunk
        end
        last_err = err
    end

    error(string.format("load %s failed: %s", tostring(label or path), tostring(last_err)))
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

local function load_data_module()
    if type(_G.data) == "table" then
        return _G.data
    end

    local ok, mod = pcall(require, "data")
    if ok and type(mod) == "table" then
        _G.data = mod
        return mod
    end

    ok, mod = pcall(require, "scripts.data")
    if ok and type(mod) == "table" then
        _G.data = mod
        return mod
    end

    local chunk = loadfile_with_bytecode_fallback("scripts/data.lua", "data")
    local result = chunk()
    if type(result) == "table" then
        _G.data = result
        return result
    end

    if type(_G.data) == "table" then
        return _G.data
    end

    error("data module is not available")
end

local PROCESS_NAME = "torchlight_infinite.exe"
local MODE = "api"
local BUTTON_NAME = "UIButton Transient.GameEngine.CoreGameInstance.UIMysticMapItem_C.WidgetTree.ClickButton"
local DISTANCE_MIN = 141.7
local DISTANCE_MAX = 141.8

local nav = load_nav_module()

local function format_addr_hex(value)
    local number_value = tonumber(value) or 0
    local integer_value = math.floor(number_value)
    return string.format("0x%X", integer_value)
end

local function Getloacalpos(x, y, x1, y1)
    local xx = x - x1
    local yy = y - y1
    local distance = math.sqrt(xx * xx + yy * yy)
    return distance
end

local function Getdistance(Buttonaddr, verbose)
    local controls = data.EnumCText and data.EnumCText() or nil
    local control_count = controls and #controls or 0
    local nearest_ctl = nil
    local nearest_distance = nil

    if verbose then
        print(string.format(
            "Getdistance start | button_addr=%s button_x=%f button_y=%f texts=%d",
            format_addr_hex(Buttonaddr and Buttonaddr.addr),
            tonumber(Buttonaddr and Buttonaddr.x) or 0,
            tonumber(Buttonaddr and Buttonaddr.y) or 0,
            control_count
        ))
    end

    for idxtext, ctl in ipairs(controls or {}) do
        local distance = Getloacalpos(Buttonaddr.x, Buttonaddr.y, ctl.x, ctl.y)
        if nearest_distance == nil or distance < nearest_distance then
            nearest_ctl = ctl
            nearest_distance = distance
        end

        if verbose then
            print(string.format(
                "Getdistance scan | text_index=%d text_addr=%s text=%s x=%f y=%f distance=%f",
                idxtext,
                format_addr_hex(ctl.addr),
                tostring(ctl.text or ""),
                tonumber(ctl.x) or 0,
                tonumber(ctl.y) or 0,
                tonumber(distance) or 0
            ))
        end

        if distance > DISTANCE_MIN and distance < DISTANCE_MAX then
            if verbose then
                print(string.format(
                    "Getdistance hit | text_index=%d text_addr=%s text=%s distance=%f",
                    idxtext,
                    format_addr_hex(ctl.addr),
                    tostring(ctl.text or ""),
                    tonumber(distance) or 0
                ))
            end
            return ctl, distance
        end
    end

    if verbose then
        if nearest_ctl ~= nil then
            print(string.format(
                "Getdistance miss | nearest_text_addr=%s nearest_text=%s nearest_distance=%f",
                format_addr_hex(nearest_ctl.addr),
                tostring(nearest_ctl.text or ""),
                tonumber(nearest_distance) or 0
            ))
        else
            print("Getdistance miss | nearest_text=<none>")
        end
    end
end

local init_ok, init_err = nav.init(PROCESS_NAME, MODE)
if not init_ok then
    error("Torch API init failed: " .. tostring(init_err))
end

print(string.format(
    "Torch API initialized | process=%s mode=%s pid=%s",
    PROCESS_NAME,
    MODE,
    tostring(nav.pid or "")
))

local data = load_data_module()
local Buttons = data.EnumCButton and data.EnumCButton() or nil

if Buttons and #Buttons > 0 then
    print(string.format("找到按钮数量: %d", #Buttons))

    local exact_name_count = 0
    local printed_count = 0

    for idx, btn in ipairs(Buttons) do
        print(string.format(
            "button scan | index=%d addr=%s name=%s x=%f y=%f exact_match=%s",
            idx,
            format_addr_hex(btn.addr),
            tostring(btn.name or ""),
            tonumber(btn.x) or 0,
            tonumber(btn.y) or 0,
            tostring(btn.name == BUTTON_NAME)
        ))

        if btn.name == BUTTON_NAME then
            exact_name_count = exact_name_count + 1
            local bunttoninfo, distance = Getdistance(btn, true)
            if bunttoninfo ~= 0 and bunttoninfo ~= nil then
                printed_count = printed_count + 1
                print(string.format(
                    "[%d] addr=%s name=%s x=%f y=%f bunttoninfo=%s text=%s distance=%f",
                    idx,
                    format_addr_hex(btn.addr),
                    tostring(btn.name or ""),
                    tonumber(btn.x) or 0,
                    tonumber(btn.y) or 0,
                    format_addr_hex(bunttoninfo.addr),
                    tostring(bunttoninfo.text or ""),
                    tonumber(distance) or 0
                ))
            else
                print(string.format(
                    "button no distance hit | index=%d addr=%s name=%s",
                    idx,
                    format_addr_hex(btn.addr),
                    tostring(btn.name or "")
                ))
            end
        end
    end

    print(string.format("exact_name_count=%d", exact_name_count))
    print(string.format("printed_count=%d", printed_count))
else
    print("buttons_found=0")
end
