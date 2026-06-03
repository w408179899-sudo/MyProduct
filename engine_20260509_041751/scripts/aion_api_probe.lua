-- Press F9 to run safe AionData wrapper probes.
-- Press Ctrl+F12 to exit this probe runner.

local probe = require("aion.probe")

local HOTKEY_PROBE = 0x78      -- F9
local HOTKEY_CTRL = 0x11       -- Ctrl
local HOTKEY_EXIT = 0x7B       -- F12
local RUN_ON_START = false

local function log_info(msg)
    if log and log.info then
        log.info(msg)
    elseif print then
        print(msg)
    end
end

local function sleep(ms)
    if sys and sys.sleep then
        sys.sleep(ms)
    end
end

log_info("Aion API probe runner loaded")
log_info("Press F9 to run probes; press Ctrl+F12 to exit")

if RUN_ON_START then
    probe.run()
end

hotkey.start(10)

local running = false
while true do
    if hotkey.is_pressed(HOTKEY_CTRL) and hotkey.is_pressed(HOTKEY_EXIT) then
        log_info("Aion API probe runner exiting")
        break
    end

    if hotkey.is_pressed(HOTKEY_PROBE) and not running then
        running = true
        probe.run()
        sleep(500)
        running = false
    end

    sleep(50)
end

hotkey.stop()
