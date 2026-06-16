local M = {}

M.SIT_KEYCODE = 188 -- VK_OEM_COMMA: "," key, right of M on the keyboard.
M.STAND_KEYCODE = 88
M.DEFAULT_MAX_SECONDS = 30

local function number_or(value, fallback)
    local n = tonumber(value)
    if n == nil then
        return fallback
    end
    return n
end

local function clamp_int(value, fallback, min_value, max_value)
    local n = math.floor(number_or(value, fallback))
    if n < min_value then
        return min_value
    end
    if n > max_value then
        return max_value
    end
    return n
end

local function clamp_key(value, fallback)
    return clamp_int(value, fallback, 1, 255)
end

local function bool_or(value, fallback)
    if value == nil then
        return fallback
    end
    return value == true
end

function M.from_config(supply)
    supply = type(supply) == "table" and supply or {}
    local cfg = supply.floor_recovery or supply.floorRecovery or {}
    if type(cfg) ~= "table" then
        cfg = {}
    end

    local start_percent = clamp_int(
        cfg.start_percent or cfg.mp_below_percent or cfg.below_percent or cfg.low_percent,
        15,
        1,
        100)
    local recover_percent = clamp_int(
        cfg.recover_percent or cfg.mp_recover_percent or cfg.high_percent or cfg.until_percent,
        90,
        1,
        100)
    if recover_percent <= start_percent then
        recover_percent = math.min(100, start_percent + 1)
    end

    return {
        enabled = bool_or(cfg.enabled, false),
        start_percent = start_percent,
        recover_percent = recover_percent,
        sit_keycode = M.SIT_KEYCODE,
        stand_keycode = clamp_key(cfg.stand_keycode or cfg.stop_keycode or cfg.stand_key or M.STAND_KEYCODE, M.STAND_KEYCODE),
        cancel_on_damage = bool_or(cfg.cancel_on_damage, true),
        max_seconds = clamp_int(cfg.max_seconds or cfg.max_recovery_seconds or cfg.timeout_seconds, M.DEFAULT_MAX_SECONDS, 1, 3600),
    }
end

function M.resource_percent(current, maximum)
    local cur = tonumber(current)
    local max = tonumber(maximum)
    if cur == nil or max == nil or max <= 0 then
        return nil
    end
    return math.max(0, math.min(100, (cur / max) * 100))
end

function M.character_mp_values(char)
    char = type(char) == "table" and char or {}
    return
        char.mp or char.MP or char.cur_mp or char.current_mp,
        char.mmp or char.MMP or char.max_mp or char.maxMP
end

function M.character_mp_percent(char)
    local current, maximum = M.character_mp_values(char)
    return M.resource_percent(current, maximum)
end

function M.character_hp(char)
    char = type(char) == "table" and char or {}
    return tonumber(char.hp or char.HP or char.cur_hp or char.current_hp)
end

function M.decide(args)
    args = type(args) == "table" and args or {}
    local settings = args.settings or M.from_config(args.supply)
    local state = type(args.state) == "table" and args.state or {}
    local active = state.active == true
    local pending_after_loot = args.after_loot_pending == true
    local mp_percent = tonumber(args.mp_percent)
    if mp_percent == nil then
        mp_percent = M.character_mp_percent(args.char)
    end
    local mp_current, mp_max = M.character_mp_values(args.char)
    local hp = tonumber(args.hp)
    if hp == nil then
        hp = M.character_hp(args.char)
    end
    local now = tonumber(args.now or args.now_seconds)
    local started_at = tonumber(state.started_at)
    local elapsed_seconds = nil
    if active and now ~= nil and started_at ~= nil and started_at > 0 then
        elapsed_seconds = math.max(0, now - started_at)
    end

    if active then
        if settings.cancel_on_damage ~= false and hp ~= nil then
            local start_hp = tonumber(state.start_hp) or 0
            local last_hp = tonumber(state.last_hp) or 0
            if (last_hp > 0 and hp < last_hp) or (start_hp > 0 and hp < start_hp) then
                return {
                    action = "cancel",
                    reason = "damage",
                    keycode = settings.stand_keycode,
                    mp_percent = mp_percent,
                    mp_current = mp_current,
                    mp_max = mp_max,
                    hp = hp,
                    elapsed_seconds = elapsed_seconds,
                }
            end
        end

        if mp_percent ~= nil and mp_percent >= settings.recover_percent then
            return {
                action = "finish",
                reason = "recovered",
                keycode = settings.stand_keycode,
                mp_percent = mp_percent,
                mp_current = mp_current,
                mp_max = mp_max,
                hp = hp,
                elapsed_seconds = elapsed_seconds,
            }
        end

        local max_seconds = tonumber(settings.max_seconds)
        if max_seconds ~= nil and max_seconds > 0 and elapsed_seconds ~= nil and elapsed_seconds >= max_seconds then
            return {
                action = "timeout",
                reason = "timeout",
                keycode = settings.stand_keycode,
                mp_percent = mp_percent,
                mp_current = mp_current,
                mp_max = mp_max,
                hp = hp,
                elapsed_seconds = elapsed_seconds,
                max_seconds = max_seconds,
            }
        end

        return {
            action = "wait",
            reason = mp_percent == nil and "mp-unavailable" or "recovering",
            mp_percent = mp_percent,
            mp_current = mp_current,
            mp_max = mp_max,
            hp = hp,
            elapsed_seconds = elapsed_seconds,
        }
    end

    if not pending_after_loot then
        return {
            action = "idle",
            reason = "not-after-loot",
            mp_percent = mp_percent,
            mp_current = mp_current,
            mp_max = mp_max,
            hp = hp,
        }
    end

    if settings.enabled ~= true then
        return {
            action = "skip",
            reason = "disabled",
            mp_percent = mp_percent,
            mp_current = mp_current,
            mp_max = mp_max,
            hp = hp,
        }
    end

    if args.in_combat == true or args.loot_pending == true or args.post_kill_pending == true then
        return {
            action = "defer",
            reason = "combat-not-ended",
            mp_percent = mp_percent,
            mp_current = mp_current,
            mp_max = mp_max,
            hp = hp,
        }
    end

    if mp_percent == nil then
        return {
            action = "skip",
            reason = "mp-unavailable",
            mp_percent = nil,
            mp_current = mp_current,
            mp_max = mp_max,
            hp = hp,
        }
    end

    if mp_percent < settings.start_percent then
        return {
            action = "start",
            reason = "mp-low",
            keycode = settings.sit_keycode,
            mp_percent = mp_percent,
            mp_current = mp_current,
            mp_max = mp_max,
            hp = hp,
        }
    end

    return {
        action = "skip",
        reason = "mp-high",
        mp_percent = mp_percent,
        mp_current = mp_current,
        mp_max = mp_max,
        hp = hp,
    }
end

return M
