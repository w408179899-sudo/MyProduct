local cwd = sys and sys.get_cwd and sys.get_cwd() or "."
package.path = cwd .. "/scripts/?.lua;" .. cwd .. "/scripts/?/init.lua;" .. package.path

local Blackboard = require("maple.blackboard")
local Logger = require("maple.systems.logger")
local MapleEnvironment = require("maple.environment.maple_environment")
local PlatformRecorder = require("maple.navigation.platform_recorder")

local function write_line(message)
    message = "[platform_record] " .. tostring(message)
    if log and log.info then log.info(message) else print(message) end
end

local function sleep_ms(ms)
    if sys and sys.sleep then sys.sleep(tonumber(ms) or 0) end
end

local function bool_pressed(key_code)
    return hotkey and hotkey.is_pressed and hotkey.is_pressed(key_code) == true
end

local account_idx = tonumber(account_index) or 0
local save_path = platform_save_path or (cwd .. "/scripts/maple/maps/manual_platform.lua")
local logger = Logger.new("platform_record", {
    level = "debug",
    print_to_console = false,
    keep_records = 100
})
local bb = Blackboard.new({ account_index = account_idx })
local env = MapleEnvironment.new({
    logger = logger,
    account_index = account_idx,
    target_name = probe_target_name or "msw.exe",
    license_key = probe_license_key,
    allow_mock_fallback = false
})

write_line("started")
write_line("hotkeys: F9=start/resume F10=pause F11=save F12=clear F1=left F2=right Ctrl+F12=exit")
write_line("save_path=" .. tostring(save_path))

local connected = env:bind_client({
    params = {
        target_name = probe_target_name or "msw.exe",
        license_key = probe_license_key
    }
}, bb)
if not connected.ok then
    write_line("connect failed reason=" .. tostring(connected.reason))
    return {
        ok = false,
        reason = connected.reason
    }
end
write_line("connect ok pid=" .. tostring(connected.data and connected.data.pid))

local recorder = PlatformRecorder.new({
    save_path = save_path,
    platform_id = platform_id or "manual_1",
    sample_ms = tonumber(platform_sample_ms) or 100,
    min_distance = tonumber(platform_min_distance) or 0.05,
    max_points = tonumber(platform_max_points) or 2000,
    safe_margin = tonumber(platform_safe_margin) or 1,
    output = write_line,
    read_actor = function()
        return env:get_actor_state(bb)
    end
})

if hotkey and hotkey.start then hotkey.start() end

local ok = true
local reason = "user_exit"
while true do
    if bool_pressed(0x11) and bool_pressed(0x7B) then
        reason = "ctrl_f12_exit"
        break
    end

    recorder:poll_hotkeys(bool_pressed)
    sleep_ms(20)
end

if hotkey and hotkey.stop then hotkey.stop() end

write_line("stopped reason=" .. tostring(reason))
return {
    ok = ok,
    reason = reason,
    save_path = recorder.last_save_path or save_path,
    snapshot = recorder:snapshot()
}
