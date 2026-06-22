local cwd = sys and sys.get_cwd and sys.get_cwd() or "."
package.path = cwd .. "/scripts/?.lua;" .. cwd .. "/scripts/?/init.lua;" .. package.path

local Probe = require("maple.probes.api_probe")

return Probe.key_effect({
    account_index = tonumber(account_index) or 0,
    target_name = probe_target_name or "msw.exe",
    license_key = probe_license_key,
    input_mode = probe_input_mode or "foreground",
    key_mode = probe_key_mode,
    key_code = tonumber(probe_key_code) or 0x10,
    hold_ms = tonumber(probe_hold_ms) or 0,
    wait_ms = tonumber(probe_wait_ms) or 900
})
