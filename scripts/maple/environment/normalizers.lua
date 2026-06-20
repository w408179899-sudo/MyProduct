local Normalizers = {}

local function as_number(value, fallback)
    local n = tonumber(value)
    if n == nil then return fallback end
    return n
end

local function as_string(value, fallback)
    if value == nil then return fallback end
    return tostring(value)
end

local function as_boolean(value, fallback)
    if value == nil then return fallback end
    if value == true or value == "true" or value == "1" or value == 1 then return true end
    if value == false or value == "false" or value == "0" or value == 0 then return false end
    return fallback
end

local function position(x, y, z)
    return {
        x = as_number(x, 0),
        y = as_number(y, 0),
        z = as_number(z, 0)
    }
end

local function rounded(value)
    local n = as_number(value, 0)
    if n >= 0 then return math.floor(n + 0.5) end
    return math.ceil(n - 0.5)
end

local function mob_id(mob, index)
    return table.concat({
        "mob",
        as_string(mob.MobId, "unknown"),
        as_string(mob.Name, "unknown"),
        tostring(rounded(mob.x)),
        tostring(rounded(mob.y)),
        tostring(index or 0)
    }, ":")
end

local function drop_id(drop, index)
    return table.concat({
        "drop",
        as_string(drop.ItemId, "unknown"),
        as_string(drop.Name, "unknown"),
        tostring(rounded(drop.x)),
        tostring(rounded(drop.y)),
        tostring(index or 0)
    }, ":")
end

local function source(diagnostic)
    return {
        api_name = diagnostic and diagnostic.api_name,
        ok = diagnostic and diagnostic.ok == true,
        elapsed_ms = diagnostic and diagnostic.elapsed_ms or 0,
        result_count = diagnostic and diagnostic.result_count or 0
    }
end

function Normalizers.number(value, fallback)
    return as_number(value, fallback)
end

function Normalizers.boolean(value, fallback)
    return as_boolean(value, fallback)
end

function Normalizers.actor(raw, diagnostic)
    raw = raw or {}
    local hp = as_number(raw.Hp, 0)
    local max_hp = as_number(raw.MaxHp, 0)
    local map_id = as_string(raw.MapId, nil)
    return {
        level = as_number(raw.Level, 1),
        hp = hp,
        max_hp = max_hp,
        mp = as_number(raw.Mp, 0),
        max_mp = as_number(raw.MaxMp, 0),
        job = raw.Job,
        nickname = raw.Nickname,
        gender = raw.Gender,
        char_id = raw.CharId,
        ap = as_number(raw.AP, 0),
        entity = raw.Entity,
        is_dead = max_hp > 0 and hp <= 0,
        is_in_combat = false,
        invincible = as_boolean(raw.Invincible, false),
        position = position(raw.X, raw.Y, raw.Z),
        current_map = map_id,
        map_id = map_id,
        map_name = raw.MapName,
        movement = {
            walk_speed = as_number(raw.WalkSpeed, 0),
            gravity = as_number(raw.Gravity, 0)
        },
        source = source(diagnostic)
    }
end

function Normalizers.inventory(raw, diagnostic)
    raw = raw or {}
    local items = {}
    for i, item in ipairs(raw.items or {}) do
        items[#items + 1] = {
            id = as_string(item.CUID or item.Code or i, tostring(i)),
            code = item.Code,
            count = as_number(item.Count, 0),
            type = item.type,
            index = item.index,
            name = item.name,
            item_type = item.itemType,
            item_type_name = item.itemTypeName,
            equip_info = item.equipInfo
        }
    end
    return {
        meso = as_number(raw.meso, 0),
        used_slots = #items,
        max_slots = as_number(raw.max_slots, 0),
        items = items,
        is_full = false,
        has_required_items = false,
        source = source(diagnostic)
    }
end

function Normalizers.quickslots(raw, diagnostic)
    raw = raw or {}
    local list = {}
    for _, slot in ipairs(raw) do
        list[#list + 1] = {
            slot = as_number(slot.slot, 0),
            key = slot.key,
            category = slot.cat,
            id = slot.id,
            numeric_id = as_number(slot.id, nil)
        }
    end
    return {
        list = list,
        source = source(diagnostic)
    }
end

function Normalizers.skill(raw, quickslot_raw, diagnostic, quickslot_diagnostic)
    raw = raw or {}
    local learned = {}
    local available = {}
    for _, skill in ipairs(raw.skills or {}) do
        local id = as_string(skill.Code, nil)
        local item = {
            id = id,
            code = skill.Code,
            name = skill.name,
            tier = skill.tier,
            index = skill.index,
            current_level = as_number(skill.CurrentLevel, 0)
        }
        available[#available + 1] = item
        if id then learned[id] = item end
    end
    local quickslots = Normalizers.quickslots(quickslot_raw or {}, quickslot_diagnostic)
    return {
        point = as_number(raw.point, 0),
        used = as_number(raw.used, 0),
        learned = learned,
        available = available,
        quickslots = quickslots.list,
        should_learn = false,
        trainer_known = false,
        source = source(diagnostic),
        quickslot_source = quickslots.source
    }
end

function Normalizers.world(raw, diagnostic)
    raw = raw or {}
    local targets = {}
    for i, mob in ipairs(raw.mobs or {}) do
        local pos = position(mob.x, mob.y, mob.z)
        targets[#targets + 1] = {
            id = mob.InstanceId or mob_id(mob, i),
            type_id = mob.MobId,
            name = mob.Name,
            level = as_number(mob.Level, 0),
            x = pos.x,
            y = pos.y,
            z = pos.z,
            vx = 0,
            vy = 0,
            vz = 0,
            has_velocity = false,
            position = pos,
            hp = as_number(mob.Hp, 0),
            max_hp = as_number(mob.MaxHp, 0),
            source_index = i
        }
    end

    local resources = {}
    for i, drop in ipairs(raw.drops or {}) do
        local pos = position(drop.x, drop.y, drop.z)
        local mine = drop.OwnerCID == "mine"
        local free = as_boolean(drop.Free, false)
        resources[#resources + 1] = {
            id = drop.DropId or drop_id(drop, i),
            item_id = drop.ItemId,
            name = drop.Name,
            owner_cid = drop.OwnerCID,
            dropper_type = drop.DropperType,
            free = free,
            can_pick = mine or free,
            x = pos.x,
            y = pos.y,
            z = pos.z,
            position = pos,
            source_index = i
        }
    end

    local portals = {}
    for i, portal in ipairs(raw.portals or {}) do
        local pos = position(portal.x, portal.y, portal.z)
        portals[#portals + 1] = {
            id = as_string(portal.Name or i, tostring(i)),
            name = portal.Name,
            portal_type = portal.PortalType,
            dest_map = portal.DestMap,
            dest_portal = portal.DestPortal,
            x = pos.x,
            y = pos.y,
            z = pos.z,
            position = pos
        }
    end

    local npcs = {}
    for i, npc in ipairs(raw.npcs or {}) do
        local pos = position(npc.x, npc.y, npc.z)
        npcs[#npcs + 1] = {
            id = as_string(npc.NpcCode or npc.Name or i, tostring(i)),
            code = npc.NpcCode,
            name = npc.Name,
            x = pos.x,
            y = pos.y,
            z = pos.z,
            position = pos
        }
    end

    return {
        nearby_npcs = npcs,
        nearby_targets = targets,
        nearby_resources = resources,
        nearby_portals = portals,
        selected_entity = nil,
        counts = {
            mob = as_number(raw.mobCount, #targets),
            drop = as_number(raw.dropCount, #resources),
            portal = as_number(raw.portalCount, #portals),
            npc = as_number(raw.npcCount, #npcs)
        },
        source = source(diagnostic)
    }
end

return Normalizers
