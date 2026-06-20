local cwd = sys and sys.get_cwd and sys.get_cwd() or "."
package.path = cwd .. "/scripts/?.lua;" .. cwd .. "/scripts/?/init.lua;" .. package.path

local Probe = require("maple.probes.api_probe")

return Probe.actions({
    account_index = tonumber(account_index) or 0,
    target_name = probe_target_name or "msw.exe",
    license_key = probe_license_key,
    quickslot_slot = tonumber(probe_quickslot_slot) or 1,
    move_ms = tonumber(probe_move_ms) or 300
})
