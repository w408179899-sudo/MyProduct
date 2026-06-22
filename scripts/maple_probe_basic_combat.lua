local cwd = sys and sys.get_cwd and sys.get_cwd() or "."
package.path = cwd .. "/scripts/?.lua;" .. cwd .. "/scripts/?/init.lua;" .. package.path

local Probe = require("maple.probes.api_probe")

return Probe.basic_combat({
    account_index = tonumber(account_index) or 0,
    target_name = probe_target_name or "msw.exe",
    license_key = probe_license_key,
    run_seconds = tonumber(probe_run_seconds) or 20,
    max_ticks = tonumber(probe_max_ticks) or 80,
    key_name = probe_key_name or "Shift",
    key_code = tonumber(probe_key_code) or 0x10,
    input_mode = probe_input_mode or "foreground",
    key_mode = probe_key_mode,
    hold_ms = tonumber(probe_hold_ms) or 0,
    baseline_attack_range_x = tonumber(probe_attack_range_x),
    baseline_attack_range_y = tonumber(probe_attack_range_y),
    baseline_stop_range_x = tonumber(probe_stop_range_x),
    baseline_pursuit_y_tolerance = tonumber(probe_pursuit_y_tolerance),
    baseline_move_ms = tonumber(probe_move_ms),
    baseline_attack_wait_ms = tonumber(probe_attack_wait_ms),
    baseline_pick_wait_ms = tonumber(probe_pick_wait_ms)
})
