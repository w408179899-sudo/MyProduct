local cwd = sys and sys.get_cwd and sys.get_cwd() or "."
package.path = cwd .. "/scripts/?.lua;" .. cwd .. "/scripts/?/init.lua;" .. package.path

local Probe = require("maple.probes.api_probe")

return Probe.quickslot({
    account_index = tonumber(account_index) or 0,
    target_name = probe_target_name or "msw.exe",
    license_key = probe_license_key,
    quickslot_slot = 2,
    quickslot_action = probe_quickslot_action or "press",
    repeat_count = tonumber(probe_repeat_count) or 1,
    interval_ms = tonumber(probe_interval_ms) or 250
})
