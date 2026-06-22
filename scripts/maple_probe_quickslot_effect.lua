local cwd = sys and sys.get_cwd and sys.get_cwd() or "."
package.path = cwd .. "/scripts/?.lua;" .. cwd .. "/scripts/?/init.lua;" .. package.path

local Probe = require("maple.probes.api_probe")

return Probe.quickslot_effect({
    account_index = tonumber(account_index) or 0,
    target_name = probe_target_name or "msw.exe",
    license_key = probe_license_key,
    quickslot_slot = tonumber(probe_quickslot_slot) or 1,
    quickslot_action = probe_quickslot_action or "press",
    wait_ms = tonumber(probe_wait_ms) or 900
})
