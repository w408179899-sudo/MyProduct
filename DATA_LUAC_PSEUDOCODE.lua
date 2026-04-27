-- Reconstructed from scripts/data.luac on 2026-04-24.
-- This is a best-effort pseudo-source, not a byte-identical decompile.
-- It preserves the exported API, confirmed offsets, and main control flow.

local offset = {
    UWorldBase = 134120232,
    ObjectBase = 132769320,
    FnameBase = 132666768,
    GameMgr = 109044160,
    UTutorialManager = 109046440,
    UIGamepadEventMgr = 109046472,
    loading = 133465060,
    Controlsoffset = 132769784,
    moveto = 16560368,
    click = 8266064,
    move_EMoveCtrl = 1240,
}

local M = {
    pid = 0,
    GameBase = 0,
    g_base = 0,
}

local LICENSE_KEY = "LUA5D744CAA2A7A601FB14514E3D93B9D5A"

local function is_valid_ptr(ptr)
    return ptr ~= nil and ptr ~= 0 and ptr > 0x10000
end

local function ensure_driver()
    if driver.is_loaded() then
        return true
    end

    local ok, err = driver.load(LICENSE_KEY)
    if not ok then
        return false, err or "unknown error"
    end

    return true
end

function M.InitGameinfo(pid, mode)
    M.pid = pid
    if not M.pid then
        log.error("invalid PID")
        return false, "invalid PID"
    end

    local ok, err = ensure_driver()
    if ok then
        if M.g_base == nil or M.g_base == 0 then
            M.g_base = driver.init_call(pid)
            if proc.get_mode() ~= mode then
                proc.set_mode(mode)
            end
        end
        print("driver load success")
    else
        print("driver load failed:", err)
        return false
    end

    if M.GameBase == 0 then
        local module_base, module_err = proc.module(pid, "torchlight_infinite.exe")
        if not module_base then
            log.error("failed to get main module base: " .. (module_err or "unknown error"))
            return false, module_err or "unknown error"
        end

        M.GameBase = module_base
        log.info(string.format("module base: 0x%X", M.GameBase))
        return true
    end
end

local function readWstring(addr, len)
    if not M.pid or not addr then
        return ""
    end

    len = len or 128
    local raw = driver.read_memory(M.pid, addr, len * 2)
    if not raw then
        return ""
    end

    local out = {}
    for i = 1, #raw - 1, 2 do
        local lo = raw:byte(i)
        local hi = raw:byte(i + 1)
        if lo == 0 and hi == 0 then
            break
        end

        local code = lo + hi * 256
        if code < 0x80 then
            table.insert(out, string.char(code))
        elseif code < 0x800 then
            table.insert(out, string.char(math.floor(code / 64) + 0xC0, (code % 64) + 0x80))
        else
            table.insert(
                out,
                string.char(
                    math.floor(code / 4096) + 0xE0,
                    (math.floor(code / 64) % 64) + 0x80,
                    (code % 64) + 0x80
                )
            )
        end
    end

    return table.concat(out)
end

function M.GetByName(name_id)
    if name_id == 0 or name_id == nil then
        return ""
    end

    local pool = M.GameBase + offset.FnameBase
    local block = proc.read_u64(M.pid, pool + ((name_id >> 16) * 8))
    local entry = block + ((name_id & 0xFFFF) * 2)
    local header = proc.read_u16(M.pid, entry)
    if not header or header == 0 then
        return ""
    end

    local size = header >> 6
    return proc.read_string(M.pid, entry + 2, size)
end

function M.GetFullName(obj)
    local outer_name = ""
    local outer = proc.read_u64(M.pid, obj + 32)

    while is_valid_ptr(outer) do
        local outer_name_id = proc.read_u32(M.pid, outer + 24)
        outer_name = M.GetByName(outer_name_id) .. "." .. outer_name
        outer = proc.read_u64(M.pid, outer + 32)
    end

    local class_ptr = proc.read_u64(M.pid, obj + 16)
    outer_name = outer_name:gsub(".*()/", "")

    local class_name = ""
    if is_valid_ptr(class_ptr) then
        class_name = M.GetByName(proc.read_u32(M.pid, class_ptr + 24))
    end

    local self_name = M.GetByName(proc.read_u32(M.pid, obj + 24))
    return string.format("%s %s%s", class_name, outer_name, self_name)
end

function M.GetObjectPtr(index)
    local object_base = M.GameBase + offset.ObjectBase
    local chunk_index = index >> 16
    local item_index = index & 0xFFFF
    local chunk_array = proc.read_u64(M.pid, object_base)
    local chunk = proc.read_u64(M.pid, chunk_array + (chunk_index * 8))
    return proc.read_u64(M.pid, chunk + (item_index * 24))
end

local function get_control_array()
    local base = M.GameBase + offset.Controlsoffset
    local list = proc.read_u64(M.pid, base + 280)
    local count = proc.read_u32(M.pid, base + 288)
    return list, count or 0
end

local function get_control_class_name(entry)
    local class_obj = proc.read_u64(M.pid, proc.read_u64(M.pid, entry))
    local class_id = proc.read_u32(M.pid, class_obj + 24)
    return M.GetByName(class_id)
end

local function get_widget_pos(widget)
    local geom = proc.read_u64(M.pid, widget + 224)
    if not is_valid_ptr(geom) then
        return nil, nil, nil
    end

    return geom, proc.read_float(M.pid, geom + 212), proc.read_float(M.pid, geom + 216)
end

local function accepted_widget_state(flag)
    -- Exact branch polarity in bytecode is awkward to read.
    -- These values are the ones explicitly tested by the original chunk.
    return flag == 17 or flag == 25
end

function M.IsOuterVisible(addr)
    local outer = proc.read_u64(M.pid, addr + 32)
    local depth = 0
    while is_valid_ptr(outer) and depth < 8 do
        local flag = proc.read_u8(M.pid, outer + 195)
        if flag == 1 or flag == 2 then
            return false
        end
        outer = proc.read_u64(M.pid, outer + 32)
        depth = depth + 1
    end
    return true
end

function M.EnumCButton()
    local control_list, control_count = get_control_array()
    local results = {}

    for i = 0, control_count - 1 do
        local slot = control_list + (i * 32)
        local entry = proc.read_u64(M.pid, slot + 16)
        if is_valid_ptr(entry) and get_control_class_name(entry) == "Default__UIButton" then
            local child_base = proc.read_u64(M.pid, entry)
            local child_count = proc.read_u32(M.pid, entry + 8)
            for child_index = 0, (child_count or 0) - 1 do
                local child = proc.read_u64(M.pid, child_base + (child_index * 16))
                local geom, x, y = get_widget_pos(child)
                if is_valid_ptr(geom) then
                    local state_root = proc.read_u64(M.pid, child + 232)
                    local state_flag = nil
                    if is_valid_ptr(state_root) then
                        state_flag = proc.read_u8(M.pid, state_root + 12)
                    end

                    -- The original bytecode checks several discrete values here.
                    local state_ok =
                        state_flag == 1 or state_flag == 3 or state_flag == 6 or state_flag == 9

                    local widget_state = proc.read_u8(M.pid, geom + 440)
                    if state_ok and accepted_widget_state(widget_state) then
                        local name = M.GetFullName(child)
                        local text = ""
                        local text_root = proc.read_u64(M.pid, child + 1832)
                        if is_valid_ptr(text_root) then
                            local text_holder = proc.read_u64(M.pid, text_root + 392)
                            if is_valid_ptr(text_holder) then
                                text = readWstring(proc.read_u64(M.pid, text_holder + 56))
                            end
                        end

                        table.insert(results, {
                            addr = child or 0,
                            name = name or "",
                            text = text or "",
                            x = x or 0,
                            y = y or 0,
                        })
                    end
                end
            end
        end
    end

    return results
end

function M.EnumCText()
    local control_list, control_count = get_control_array()
    local results = {}

    for i = 0, control_count - 1 do
        local slot = control_list + (i * 32)
        local entry = proc.read_u64(M.pid, slot + 16)
        if is_valid_ptr(entry) and get_control_class_name(entry) == "Default__UITextBlock" then
            local child_base = proc.read_u64(M.pid, entry)
            local child_count = proc.read_u32(M.pid, entry + 8)
            for child_index = 0, (child_count or 0) - 1 do
                local child = proc.read_u64(M.pid, child_base + (child_index * 16))
                local geom, x, y = get_widget_pos(child)
                if is_valid_ptr(geom) then
                    local widget_state = proc.read_u8(M.pid, geom + 440)
                    local enabled = proc.read_u8(M.pid, geom + 360)
                    if accepted_widget_state(widget_state) and M.IsOuterVisible(child) and enabled == 1 then
                        local name = M.GetFullName(child)
                        local text = ""
                        local text_owner = proc.read_u64(M.pid, child + 312)
                        if is_valid_ptr(text_owner) then
                            local text_ptr = proc.read_u64(M.pid, text_owner + 40)
                            if is_valid_ptr(text_ptr) then
                                text = readWstring(text_ptr)
                            end
                        end

                        if x ~= 0 and y ~= 0 and text ~= "" then
                            table.insert(results, {
                                addr = child or 0,
                                name = name or "",
                                text = text or "",
                                x = x or 0,
                                y = y or 0,
                            })
                        end
                    end
                end
            end
        end
    end

    return results
end

function M.EnumCTextFiltered(points, max_distance)
    max_distance = max_distance or 200
    local texts = M.EnumCText()
    if not points or #points == 0 then
        return texts
    end

    local filtered = {}
    for _, text_entry in ipairs(texts) do
        for _, point in ipairs(points) do
            local dx = text_entry.x - point.x
            local dy = text_entry.y - point.y
            if (dx * dx + dy * dy) <= (max_distance * max_distance) then
                table.insert(filtered, text_entry)
                break
            end
        end
    end

    return filtered
end

function M.EnumCImage()
    local control_list, control_count = get_control_array()
    local results = {}

    for i = 0, control_count - 1 do
        local slot = control_list + (i * 32)
        local entry = proc.read_u64(M.pid, slot + 16)
        if is_valid_ptr(entry) then
            local class_name = get_control_class_name(entry)
            if class_name == "Default__UIImage" then
                local child_base = proc.read_u64(M.pid, entry)
                local child_count = proc.read_u32(M.pid, entry + 8)
                for child_index = 0, (child_count or 0) - 1 do
                    local child = proc.read_u64(M.pid, child_base + (child_index * 16))
                    local geom, x, y = get_widget_pos(child)
                    if is_valid_ptr(geom) then
                        local widget_state = proc.read_u8(M.pid, geom + 440)
                        if accepted_widget_state(widget_state) then
                            table.insert(results, {
                                addr = child or 0,
                                Fullname = M.GetFullName(child) or "",
                                IsAsyncLoad = proc.read_u8(M.pid, child + 544) or 0,
                                x = x or 0,
                                y = y or 0,
                            })
                        end
                    end
                end
            end
        end
    end

    return results
end

local function resolve_nearby_world_array()
    local world = M.GameBase + offset.UWorldBase

    local list_expr = string.format("[[[0x%X]+0x30]+0x98]", world)
    local list_ptr = proc.eval_addr(M.pid, list_expr)
    if not is_valid_ptr(list_ptr) then
        return nil, nil, "nearby world array resolve failed"
    end

    local count_expr = string.format("[[0x%X]+0x30]+0xA0", world)
    local count_ptr = proc.eval_addr(M.pid, count_expr)
    if not is_valid_ptr(count_ptr) then
        return nil, nil, "nearby world count resolve failed"
    end

    return list_ptr, proc.read_u32(M.pid, count_ptr)
end

local function read_actor_pos(actor)
    local scene = proc.read_u64(M.pid, actor + 304)
    if not is_valid_ptr(scene) then
        return nil, nil, nil
    end

    return proc.read_float(M.pid, scene + 264), proc.read_float(M.pid, scene + 268), proc.read_float(M.pid, scene + 272)
end

local function enum_simple_world_entities(expected_class_name)
    local nearby_list, count, err = resolve_nearby_world_array()
    if not nearby_list then
        return nil, err
    end

    local results = {}
    for i = 1, count do
        local actor = proc.read_u64(M.pid, nearby_list + ((i - 1) * 8))
        if is_valid_ptr(actor) then
            local class_name = M.GetByName(proc.read_u32(M.pid, actor + 24))
            if class_name == expected_class_name then
                local entity_id = proc.read_u32(M.pid, actor + 1812)
                local x, y, z = read_actor_pos(actor)
                table.insert(results, {
                    addr = actor,
                    classname = class_name or "",
                    entityId = entity_id or 0,
                    x = x or 0,
                    y = y or 0,
                    z = z or 0,
                })
            end
        end
    end

    return results
end

function M.EnumMonster()
    local nearby_list, count, err = resolve_nearby_world_array()
    if not nearby_list then
        return nil, err
    end

    local results = {}
    for i = 1, count do
        local actor = proc.read_u64(M.pid, nearby_list + ((i - 1) * 8))
        if is_valid_ptr(actor) then
            local class_name = M.GetByName(proc.read_u32(M.pid, actor + 24))
            if class_name == "EMonster" then
                local entity_id = proc.read_u32(M.pid, actor + 1812)
                local x, y, z = read_actor_pos(actor)
                local role = proc.read_u64(M.pid, actor + 1368)
                local cur_hp = 0
                local max_hp = 0
                if is_valid_ptr(role) then
                    cur_hp = proc.read_u32(M.pid, role + 1424) or 0
                    max_hp = proc.read_u32(M.pid, role + 1424 + 24) or 0
                end

                table.insert(results, {
                    addr = actor,
                    classname = class_name or "",
                    entityId = entity_id or 0,
                    curHp = cur_hp or 0,
                    maxHp = max_hp or 0,
                    x = x or 0,
                    y = y or 0,
                    z = z or 0,
                })
            end
        end
    end

    return results
end

function M.EnumGroundItem()
    local nearby_list, count, err = resolve_nearby_world_array()
    if not nearby_list then
        return nil, err
    end

    local results = {}
    for i = 1, count do
        local actor = proc.read_u64(M.pid, nearby_list + ((i - 1) * 8))
        if is_valid_ptr(actor) then
            local class_name = M.GetByName(proc.read_u32(M.pid, actor + 24))
            if class_name == "EGroundItem" then
                local item_runtime = proc.read_u64(M.pid, actor + 800)
                local flags = is_valid_ptr(item_runtime) and proc.read_u8(M.pid, item_runtime + 147) or 0
                if (flags & 1) == 1 then
                    local item_data = proc.read_u64(M.pid, actor + 968)
                    local itemlevel = is_valid_ptr(item_data) and proc.read_u32(M.pid, item_data + 360) or 0
                    local quality = is_valid_ptr(item_data) and proc.read_u32(M.pid, item_data + 372) or 0
                    local name_id = is_valid_ptr(item_data) and proc.read_u32(M.pid, item_data + 376) or 0
                    local _item_name_key = string.format("item_base|name|%d", name_id)
                    local x, y, z = read_actor_pos(actor)

                    table.insert(results, {
                        addr = actor,
                        classname = class_name or "",
                        itemlevel = itemlevel or 0,
                        quality = quality or 0,
                        x = x or 0,
                        y = y or 0,
                        z = z or 0,
                    })
                end
            end
        end
    end

    return results
end

function M.EnumPortal()
    return enum_simple_world_entities("EPortal")
end

function M.EnumNPC()
    return enum_simple_world_entities("EFightNPC")
end

function M.EnumInteractiveItem()
    return enum_simple_world_entities("EInteractiveItem")
end

function M.GetPlayerAddr()
    local expr = string.format("[[[[[[0x%X]+0x210]+0x38]]+0x30]+0x2A0]", M.GameBase + offset.UWorldBase)
    log.info("addr" .. expr)
    local addr = proc.eval_addr(M.pid, expr)
    if not is_valid_ptr(addr) then
        return nil, "player object resolve failed"
    end
    return addr
end

function M.GetPlayerinfo()
    local player = M.GetPlayerAddr()
    if not player then
        return nil, "get player object failed"
    end

    local info = {}
    local transform_root = proc.read_u64(M.pid, player + 304)

    info.entityId = proc.read_u32(M.pid, player + 1836)
    info.eRole = proc.read_u64(M.pid, player + 1376)

    if is_valid_ptr(info.eRole) then
        info.curHp = proc.read_u32(M.pid, info.eRole + 1424 + 0)
        info.maxHp = proc.read_u32(M.pid, info.eRole + 1424 + 24)
        info.Hpseal = proc.read_u32(M.pid, info.eRole + 1480 + 0)
        info.curMp = proc.read_u32(M.pid, info.eRole + 1504 + 0)
        info.maxMp = proc.read_u32(M.pid, info.eRole + 1504 + 24)
        info.Mpseal = proc.read_u32(M.pid, info.eRole + 1552 + 24)
        info.curShield = proc.read_u32(M.pid, info.eRole + 1576 + 0)
        info.maxShield = proc.read_u32(M.pid, info.eRole + 1576 + 24)
    end

    info.x = is_valid_ptr(transform_root) and proc.read_float(M.pid, transform_root + 264) or nil
    info.y = is_valid_ptr(transform_root) and proc.read_float(M.pid, transform_root + 268) or nil
    info.z = is_valid_ptr(transform_root) and proc.read_float(M.pid, transform_root + 272) or nil
    info.angle = is_valid_ptr(transform_root) and proc.read_float(M.pid, transform_root + 308) or nil

    return info
end

function M.control_click(addr)
    local func = M.GameBase + offset.click
    return driver.exec_call(M.pid, func, addr + 1016, 0)
end

function M.MoveTo(x, y)
    local player = M.GetPlayerAddr()
    if not player then
        return nil, "selfPtr resolve failed"
    end

    local move_ctrl = proc.read_u64(M.pid, player + offset.move_EMoveCtrl)
    proc.write_float(M.pid, M.g_base + 0, x)
    proc.write_float(M.pid, M.g_base + 4, y)

    local func = M.GameBase + offset.moveto
    return driver.exec_call(M.pid, func, move_ctrl, M.g_base)
end

function M.GetChineseName(name)
    -- The bytecode body does not look like a real "name lookup".
    -- It writes globals x/y into g_base and calls moveto with global EMoveCtrl.
    local _key = "item_base|name|" .. name
    proc.write_float(M.pid, M.g_base + 0, x)
    proc.write_float(M.pid, M.g_base + 4, y)
    local func = M.GameBase + offset.moveto
    return driver.exec_call(M.pid, func, EMoveCtrl, M.g_base)
end

function M.IsMainInterface()
    return proc.read_u8(M.pid, M.GameBase + offset.GameMgr + 84) == 1
end

function M.GetCurrentSelected()
    local mgr = proc.read_u64(M.pid, M.GameBase + offset.UIGamepadEventMgr)
    local selected = proc.read_u64(M.pid, mgr + 648)
    if not is_valid_ptr(selected) then
        return nil
    end

    local fullname = M.GetFullName(selected)
    local geom = proc.read_u64(M.pid, selected + 224)
    local x = is_valid_ptr(geom) and proc.read_float(M.pid, geom + 212) or 0
    local y = is_valid_ptr(geom) and proc.read_float(M.pid, geom + 216) or 0

    local text = ""
    local text_root = proc.read_u64(M.pid, selected + 1832)
    if is_valid_ptr(text_root) then
        local text_holder = proc.read_u64(M.pid, text_root + 392)
        if is_valid_ptr(text_holder) then
            text = readWstring(proc.read_u64(M.pid, text_holder + 56))
        end
    end

    return {
        addr = selected,
        Fullname = fullname or "",
        x = x or 0,
        y = y or 0,
        text = text,
    }
end

function M.Isloading()
    return proc.read_u8(M.pid, M.GameBase + offset.loading) == 1
end

function M.GetMainTaskPos()
    local manager = proc.read_u64(M.pid, M.GameBase + offset.UTutorialManager)
    local task = proc.read_u64(M.pid, manager + 216)
    if not is_valid_ptr(task) then
        return nil
    end

    return {
        x = proc.read_float(M.pid, task + 656),
        y = proc.read_float(M.pid, task + 660),
        z = proc.read_float(M.pid, task + 664),
    }
end

function M.GetMainTaskPath()
    local manager = proc.read_u64(M.pid, M.GameBase + offset.UTutorialManager)
    local task = proc.read_u64(M.pid, manager + 216)
    if not is_valid_ptr(task) then
        return nil
    end

    local path_base = proc.read_u64(M.pid, task + 624)
    local count = proc.read_u32(M.pid, task + 632)
    local path = {}

    for i = 1, count do
        local point = path_base + ((i - 1) * 16) + 4
        table.insert(path, {
            x = proc.read_float(M.pid, point + 0),
            y = proc.read_float(M.pid, point + 4),
            z = proc.read_float(M.pid, point + 8),
        })
    end

    return path
end

return M
