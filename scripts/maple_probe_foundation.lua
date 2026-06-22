local cwd = sys and sys.get_cwd and sys.get_cwd() or "."
package.path = cwd .. "/scripts/?.lua;" .. cwd .. "/scripts/?/init.lua;" .. package.path

local FoundationProbe = require("maple.probes.foundation_probe")

return FoundationProbe.run({
    account_index = tonumber(account_index) or 0,
    probe_case = probe_case or "connect_only",
    probe_target_name = probe_target_name or "msw.exe",
    probe_license_key = probe_license_key,
    probe_data_module = probe_data_module or "data",
    probe_ground_range = probe_ground_range or "-20|20|1",
    probe_map_id = probe_map_id,
    probe_x = probe_x,
    probe_y = probe_y,
    probe_portal = probe_portal or "sp",
    probe_force = probe_force ~= false,
    probe_npc_code = probe_npc_code,
    probe_npc_action = probe_npc_action or "talk",
    probe_shop_key = probe_shop_key,
    probe_dialogue_kind = probe_dialogue_kind or "all",
    probe_dialogue_button = probe_dialogue_button or "ok",
    probe_dialogue_value = probe_dialogue_value or "0",
    probe_dialogue_index = probe_dialogue_index or "0"
})
