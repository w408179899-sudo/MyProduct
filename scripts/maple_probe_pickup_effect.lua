local cwd = sys and sys.get_cwd and sys.get_cwd() or "."
package.path = cwd .. "/scripts/?.lua;" .. cwd .. "/scripts/?/init.lua;" .. package.path

local Probe = require("maple.probes.api_probe")

return Probe.pickup_effect({
    account_index = tonumber(account_index) or 0,
    target_name = probe_target_name or "msw.exe",
    license_key = probe_license_key,
    repeat_count = tonumber(probe_repeat_count) or 3,
    wait_ms = tonumber(probe_wait_ms) or 450,
    pickup_api_enabled = probe_pickup_api_enabled ~= false,
    pickup_key_enabled = probe_pickup_key_enabled ~= false,
    pickup_key_name = probe_pickup_key_name or probe_key_name or "Z",
    pickup_key_code = tonumber(probe_pickup_key_code or probe_key_code) or 0x5A,
    pickup_key_hold_ms = tonumber(probe_pickup_key_hold_ms or probe_hold_ms) or 80,
    input_mode = probe_input_mode or "foreground",
    key_mode = probe_key_mode
})
