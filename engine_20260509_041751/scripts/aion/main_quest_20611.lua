local M = {}
local npc_names = require("aion.npc_names")

M.quest_id = 20611
M.quest_ids = { 20611, 20612, 20613, 20614, 20615, 20620, 20621, 20622 }
M.quest_id_min = 20611
M.quest_id_max = 20699
M.remote_reward_quest_id = 24340
M.remote_reward_quest_ids = { 24340, 24341 }
M.big_map_id = 220010000
M.level_move_stage = "quest_20611_level_move"
M.level_grind_stage = "quest_20611_level_grind"
M.obelisk_stage = "quest_20611_obelisk"
M.indicator_title_stage = "quest_20611_indicator_title"
M.target_link_stage = "quest_20611_target_link"
M.target_teleport_stage = "quest_20611_target_teleport"
M.hotspot_teleport_stage = "quest_20611_hotspot_teleport"
M.hotspot_reward_stage = "quest_20611_hotspot_reward_npc"
M.quest_20612_id = 20612
M.quest_20612_required_level = 11
M.quest_20612_level_grind_stage = "quest_20612_level_grind"
M.quest_20612_start_stage = "quest_20612_start_npc"
M.quest_20612_teleport_stage = "quest_20612_task_teleport"
M.quest_20612_reward_stage = "quest_20612_reward_npc"
M.quest_20613_id = 20613
M.quest_20613_level_grind_stage = "quest_20613_level14_grind"
M.quest_20613_teleport_stage = "quest_20613_task_teleport"
M.quest_20613_start_stage = "quest_20613_start_npc"
M.quest_20613_after_start_teleport_stage = "quest_20613_after_start_teleport"
M.quest_20613_after_start_reward_stage = "quest_20613_after_start_reward_npc"
M.quest_20614_id = 20614
M.quest_20614_level_grind_stage = "quest_20614_level17_grind"
M.quest_20614_teleport_stage = "quest_20614_task_teleport"
M.quest_20614_start_stage = "quest_20614_start_npc"
M.quest_20614_after_start_teleport_stage = "quest_20614_after_start_teleport"
M.quest_20614_reward_stage = "quest_20614_reward_npc"
M.quest_20614_level17_required_level = 17
M.quest_20615_id = 20615
M.quest_20615_level_grind_stage = "quest_20615_level20_grind"
M.quest_20615_teleport_stage = "quest_20615_task_teleport"
M.quest_20615_target_stage = "quest_20615_target_npc"
M.quest_20615_big_map_teleport_stage = "quest_20615_big_map_teleport"
M.quest_20615_after_big_map_teleport_stage = "quest_20615_after_big_map_task_teleport"
M.quest_20615_morheim_npc_stage = "quest_20615_morheim_npc"
M.quest_20615_level20_required_level = 20
M.quest_20620_id = 20620
M.quest_20620_start_stage = "quest_20620_start_npc"
M.quest_20620_teleport_stage = "quest_20620_task_teleport"
M.quest_20620_after_teleport_stage = "quest_20620_after_teleport_npc"
M.quest_20620_socket_stigma_stage = "quest_20620_socket_stigma"
M.quest_20620_after_stigma_teleport_stage = "quest_20620_after_stigma_teleport"
M.quest_20620_after_stigma_npc_stage = "quest_20620_after_stigma_npc"
M.quest_20620_obelisk_stage = "quest_20620_obelisk"
M.quest_20620_after_obelisk_teleport_stage = "quest_20620_after_obelisk_teleport"
M.quest_20620_after_obelisk_npc_stage = "quest_20620_after_obelisk_npc"
M.quest_20621_id = 20621
M.quest_20621_level_grind_stage = "quest_20621_level22_grind"
M.quest_20621_teleport_stage = "quest_20621_task_teleport"
M.quest_20621_after_teleport_npc_stage = "quest_20621_after_teleport_npc"
M.quest_20621_after_dialog_teleport_stage = "quest_20621_after_dialog_teleport"
M.quest_20621_after_dialog_teleport_npc_stage = "quest_20621_after_dialog_teleport_npc"
M.quest_20621_level22_required_level = 22
M.quest_20622_id = 20622
M.quest_20622_level_grind_stage = "quest_20622_level25_grind"
M.quest_20622_level25_required_level = 25
M.level_grind_blue_submit_stage = "level_grind_blue_submit"
M.passive_blue_submit_tab = 1
M.passive_blue_submit_cooldown_seconds = 3
M.post_20612_level14_required_level = 14
M.grind_point = {
    x = 194.491,
    y = 2689.982,
    z = 300.625,
}
M.post_20612_level14_grind_point = {
    x = 1093.552,
    y = 2247.044,
    z = 254.250,
}
M.quest_20621_level22_grind_point = {
    x = 174.508,
    y = 2298.396,
    z = 438.510,
    big_map_id = 220020000,
}
M.quest_20622_level25_route = {
    { x = 412.223, y = 1851.753, z = 442.514 },
    { x = 411.139, y = 1852.281, z = 442.583 },
    { x = 409.310, y = 1853.221, z = 442.743 },
    { x = 407.495, y = 1854.214, z = 442.916 },
    { x = 405.690, y = 1855.201, z = 442.977 },
    { x = 403.876, y = 1856.191, z = 442.810 },
    { x = 401.932, y = 1856.974, z = 442.580 },
    { x = 400.058, y = 1857.722, z = 442.472 },
    { x = 398.162, y = 1858.480, z = 442.250 },
    { x = 396.239, y = 1859.249, z = 441.884 },
    { x = 394.307, y = 1860.022, z = 441.509 },
    { x = 392.340, y = 1860.809, z = 441.250 },
    { x = 390.293, y = 1861.500, z = 440.969 },
    { x = 388.238, y = 1861.915, z = 440.672 },
    { x = 386.236, y = 1862.320, z = 440.340 },
    { x = 384.166, y = 1862.873, z = 439.788 },
    { x = 382.153, y = 1863.491, z = 439.750 },
    { x = 380.193, y = 1864.154, z = 439.978 },
    { x = 378.174, y = 1864.836, z = 440.000 },
    { x = 376.130, y = 1865.311, z = 439.843 },
    { x = 374.077, y = 1865.654, z = 439.874 },
    { x = 372.054, y = 1865.242, z = 440.072 },
    { x = 370.021, y = 1864.596, z = 440.161 },
    { x = 368.049, y = 1863.948, z = 440.125 },
    { x = 366.417, y = 1862.581, z = 440.125 },
    { x = 364.836, y = 1861.243, z = 440.078 },
    { x = 363.201, y = 1859.859, z = 439.932 },
    { x = 361.778, y = 1858.240, z = 439.629 },
    { x = 360.573, y = 1856.631, z = 439.275 },
    { x = 359.325, y = 1854.964, z = 438.911 },
    { x = 358.106, y = 1853.337, z = 438.597 },
    { x = 356.798, y = 1851.749, z = 438.350 },
    { x = 355.485, y = 1850.148, z = 438.218 },
    { x = 354.142, y = 1848.510, z = 438.134 },
    { x = 352.821, y = 1846.899, z = 437.942 },
    { x = 351.488, y = 1845.281, z = 437.660 },
    { x = 350.165, y = 1843.687, z = 437.461 },
    { x = 348.801, y = 1842.047, z = 437.329 },
    { x = 347.460, y = 1840.434, z = 437.337 },
    { x = 346.119, y = 1838.822, z = 437.411 },
    { x = 344.850, y = 1837.296, z = 437.449 },
    { x = 343.480, y = 1835.677, z = 437.585 },
    { x = 342.146, y = 1834.140, z = 438.079 },
    { x = 340.739, y = 1832.521, z = 438.591 },
    { x = 339.343, y = 1830.913, z = 439.022 },
    { x = 337.961, y = 1829.322, z = 439.430 },
    { x = 336.161, y = 1828.329, z = 440.081 },
    { x = 334.423, y = 1827.257, z = 440.615 },
    { x = 332.858, y = 1825.919, z = 440.937 },
    { x = 331.265, y = 1824.555, z = 440.956 },
    { x = 329.597, y = 1823.363, z = 440.939 },
    { x = 327.607, y = 1822.566, z = 441.134 },
    { x = 325.719, y = 1821.810, z = 441.358 },
    { x = 323.736, y = 1821.167, z = 442.084 },
    { x = 321.654, y = 1820.984, z = 442.864 },
    { x = 319.636, y = 1820.976, z = 443.148 },
    { x = 317.538, y = 1821.176, z = 443.274 },
    { x = 315.557, y = 1820.831, z = 443.654 },
    { x = 314.524, y = 1818.899, z = 444.071 },
    { x = 313.319, y = 1817.339, z = 444.773 },
    { x = 311.605, y = 1816.247, z = 446.137 },
    { x = 309.804, y = 1815.302, z = 446.564 },
    { x = 307.922, y = 1814.273, z = 446.761 },
    { x = 306.101, y = 1813.516, z = 447.219 },
    { x = 304.034, y = 1813.184, z = 448.018 },
    { x = 301.990, y = 1812.856, z = 448.572 },
    { x = 299.971, y = 1812.531, z = 448.594 },
    { x = 297.947, y = 1812.199, z = 448.848 },
    { x = 295.931, y = 1811.853, z = 449.263 },
    { x = 293.909, y = 1811.392, z = 449.625 },
    { x = 292.109, y = 1810.489, z = 449.625 },
    { x = 290.472, y = 1809.186, z = 449.822 },
    { x = 288.809, y = 1807.794, z = 450.087 },
    { x = 287.109, y = 1806.514, z = 450.329 },
    { x = 285.462, y = 1805.250, z = 450.648 },
    { x = 284.185, y = 1803.652, z = 450.987 },
    { x = 283.009, y = 1802.176, z = 451.484 },
    { x = 282.731, y = 1800.671, z = 451.631 },
    { x = 282.523, y = 1799.503, z = 451.719 },
}
M.quest_20622_level25_grind_point = M.quest_20622_level25_route[#M.quest_20622_level25_route]
M.quest_20622_level25_grind_point.big_map_id = 220020000
M.quest_20614_level17_route = {
    { x = 940.295, y = 1707.646, z = 259.500 },
    { x = 938.978, y = 1707.956, z = 259.500 },
    { x = 936.973, y = 1708.427, z = 259.473 },
    { x = 934.967, y = 1708.898, z = 259.201 },
    { x = 932.881, y = 1709.387, z = 258.790 },
    { x = 930.881, y = 1709.857, z = 258.708 },
    { x = 928.828, y = 1710.339, z = 258.823 },
    { x = 926.707, y = 1710.765, z = 258.956 },
    { x = 924.636, y = 1710.882, z = 259.085 },
    { x = 922.479, y = 1710.793, z = 259.182 },
    { x = 920.433, y = 1710.689, z = 259.241 },
    { x = 918.341, y = 1710.538, z = 259.354 },
    { x = 916.320, y = 1710.350, z = 259.480 },
    { x = 914.163, y = 1710.151, z = 259.500 },
    { x = 912.082, y = 1709.942, z = 259.620 },
    { x = 909.989, y = 1709.722, z = 259.625 },
    { x = 907.946, y = 1709.507, z = 259.658 },
    { x = 905.890, y = 1709.291, z = 259.746 },
    { x = 904.041, y = 1710.238, z = 259.613 },
    { x = 902.267, y = 1711.250, z = 259.764 },
    { x = 900.470, y = 1712.198, z = 259.873 },
    { x = 898.601, y = 1713.165, z = 260.006 },
    { x = 896.677, y = 1714.174, z = 260.015 },
    { x = 894.818, y = 1715.149, z = 260.072 },
    { x = 892.969, y = 1716.097, z = 260.005 },
    { x = 891.095, y = 1716.972, z = 260.110 },
    { x = 889.168, y = 1717.917, z = 260.421 },
    { x = 887.329, y = 1718.820, z = 260.576 },
    { x = 885.490, y = 1719.722, z = 260.621 },
    { x = 883.592, y = 1720.578, z = 260.618 },
    { x = 881.689, y = 1721.445, z = 260.563 },
    { x = 879.774, y = 1722.343, z = 260.750 },
    { x = 877.937, y = 1723.207, z = 260.750 },
    { x = 876.020, y = 1724.118, z = 260.757 },
    { x = 874.113, y = 1725.022, z = 260.814 },
    { x = 872.228, y = 1725.917, z = 260.971 },
    { x = 870.211, y = 1726.685, z = 260.779 },
    { x = 868.188, y = 1727.418, z = 260.924 },
    { x = 866.262, y = 1728.117, z = 260.898 },
    { x = 864.723, y = 1728.675, z = 260.890 },
    { x = 862.775, y = 1730.176, z = 260.886 },
    { x = 861.189, y = 1731.832, z = 260.990 },
    { x = 859.504, y = 1733.132, z = 261.155 },
    { x = 857.778, y = 1734.469, z = 261.265 },
    { x = 856.067, y = 1735.767, z = 261.240 },
    { x = 854.280, y = 1737.087, z = 261.210 },
    { x = 852.602, y = 1738.306, z = 261.146 },
    { x = 851.740, y = 1738.932, z = 261.266 },
}
M.quest_20614_level17_grind_point = M.quest_20614_level17_route[#M.quest_20614_level17_route]
M.quest_20615_level20_route = {
    { x = 600.922, y = 1485.689, z = 298.540 },
    { x = 602.456, y = 1486.625, z = 297.866 },
    { x = 604.295, y = 1487.749, z = 297.296 },
    { x = 606.092, y = 1488.846, z = 296.425 },
    { x = 607.941, y = 1489.975, z = 295.524 },
    { x = 609.696, y = 1491.047, z = 295.127 },
    { x = 611.505, y = 1492.097, z = 294.718 },
    { x = 613.347, y = 1493.034, z = 294.247 },
    { x = 615.190, y = 1493.917, z = 293.965 },
    { x = 617.044, y = 1494.718, z = 293.332 },
    { x = 618.943, y = 1495.536, z = 292.639 },
    { x = 620.901, y = 1496.380, z = 292.332 },
    { x = 622.898, y = 1497.081, z = 292.250 },
    { x = 624.863, y = 1497.722, z = 292.250 },
    { x = 626.852, y = 1498.371, z = 292.130 },
    { x = 628.863, y = 1499.028, z = 291.974 },
    { x = 630.829, y = 1499.670, z = 291.928 },
    { x = 632.794, y = 1500.312, z = 291.825 },
    { x = 634.771, y = 1500.958, z = 291.702 },
    { x = 636.748, y = 1501.603, z = 291.578 },
    { x = 638.751, y = 1502.257, z = 291.453 },
    { x = 640.708, y = 1502.896, z = 291.331 },
    { x = 642.640, y = 1503.526, z = 291.210 },
    { x = 644.630, y = 1504.176, z = 291.086 },
    { x = 646.594, y = 1504.817, z = 290.948 },
    { x = 648.431, y = 1505.695, z = 290.742 },
    { x = 650.326, y = 1506.717, z = 290.625 },
    { x = 652.170, y = 1507.711, z = 290.625 },
    { x = 654.068, y = 1508.734, z = 290.621 },
    { x = 655.868, y = 1509.705, z = 290.508 },
    { x = 657.724, y = 1510.726, z = 290.506 },
    { x = 659.333, y = 1511.926, z = 290.500 },
    { x = 660.604, y = 1513.667, z = 290.462 },
    { x = 661.675, y = 1515.379, z = 290.309 },
    { x = 662.690, y = 1517.260, z = 290.423 },
    { x = 663.408, y = 1519.159, z = 290.645 },
    { x = 664.040, y = 1521.157, z = 290.967 },
    { x = 664.495, y = 1523.237, z = 291.570 },
    { x = 664.821, y = 1525.330, z = 292.288 },
    { x = 665.079, y = 1527.469, z = 292.681 },
    { x = 665.379, y = 1529.528, z = 292.845 },
    { x = 665.679, y = 1531.586, z = 293.272 },
    { x = 665.987, y = 1533.694, z = 293.692 },
    { x = 666.227, y = 1535.341, z = 294.009 },
}
M.quest_20615_level20_grind_point = M.quest_20615_level20_route[#M.quest_20615_level20_route]
M.npc = {
    name_key = "MQ20611_NPC_001_MISSION",
    name = npc_names.MQ20611_NPC_001_MISSION,
    interact_id = 2147503111,
    x = 586.22,
    y = 2465.17,
    z = 278.58,
}
M.obelisk = {
    name_key = "MQ20611_NPC_002_OBELISK",
    name = npc_names.MQ20611_NPC_002_OBELISK,
    interact_id = 2147505051,
    x = 587.69,
    y = 2467.10,
    z = 278.79,
}
M.target_npc = {
    name_key = "MQ20611_NPC_003_TARGET",
    name = npc_names.MQ20611_NPC_003_TARGET,
    interact_id = 2147520815,
    x = 589.35,
    y = 2450.16,
    z = 278.38,
}
M.quest_20615_target_npc = {
    name_key = "MQ20611_NPC_003_TARGET",
    name = npc_names.MQ20611_NPC_003_TARGET,
    interact_id = 2147520815,
    x = 589.35,
    y = 2450.16,
    z = 278.38,
}
M.quest_20615_big_map_teleport = {
    slot = 0x07,
    price = 1200,
    min_lv = 20,
    name = "Morheim",
    expected_big_map_id = 220020000,
}
M.quest_20615_morheim_npc = {
    name_key = "MQ20615_NPC_001_MORHEIM_AEGIR",
    name = "",
    interact_id = 2147488159,
    x = 224.83,
    y = 2415.82,
    z = 454.11,
    big_map_id = 220020000,
}
M.quest_20620_start_npc = {
    name_key = "MQ20620_NPC_001_START_AEGIR",
    name = "",
    interact_id = 2147488159,
    x = 224.83,
    y = 2415.82,
    z = 454.11,
    big_map_id = 220020000,
}
M.quest_20620_after_teleport_npc = {
    name_key = "MQ20620_NPC_002_AFTER_TELEPORT",
    name = "",
    interact_id = 2147511717,
    x = 234.21,
    y = 2321.90,
    z = 446.32,
    big_map_id = 220020000,
}
M.quest_20620_after_stigma_npc = {
    name_key = "MQ20620_NPC_003_AFTER_STIGMA",
    name = "",
    interact_id = 2147515902,
    x = 269.42,
    y = 2337.65,
    z = 443.74,
    big_map_id = 220020000,
}
M.quest_20620_obelisk = {
    name_key = "MQ20620_NPC_004_OBELISK",
    name = "",
    interact_id = 2147499094,
    x = 268.00,
    y = 2338.62,
    z = 443.75,
    big_map_id = 220020000,
}
M.quest_20620_after_obelisk_npc = {
    name_key = "MQ20620_NPC_005_AFTER_OBELISK",
    name = "",
    interact_id = 2147535533,
    x = 193.00,
    y = 2268.50,
    z = 439.12,
    big_map_id = 220020000,
}
M.quest_20621_after_teleport_npc = {
    name_key = "MQ20621_NPC_001_AFTER_TELEPORT",
    name = "",
    interact_id = 2147535533,
    x = 193.00,
    y = 2268.50,
    z = 439.12,
    big_map_id = 220020000,
}
M.quest_20621_after_dialog_teleport_npc = {
    name_key = "MQ20621_NPC_002_AFTER_DIALOG_TELEPORT",
    name = npc_names.MQ20621_NPC_002_AFTER_DIALOG_TELEPORT or "",
    interact_id = 2147520888,
    x = 414.75,
    y = 1848.00,
    z = 442.53,
    big_map_id = 220020000,
}
M.quest_20620_stigma_keywords = {
    "파멸의 방패",
    "스티그마",
    "Stigma",
    "stigma",
    "烙印",
}
M.hotspot_node = {
    name = "투나프레 호수",
    name_en = "HOTSPOT_DF1_04",
    node_id = 66,
    x = 491.0,
    y = 2301.0,
    z = 300.0,
}
M.hotspot_reward_npc = {
    name_key = "MQ20611_NPC_004_HOTSPOT_REWARD",
    name = npc_names.MQ20611_NPC_004_HOTSPOT_REWARD,
    interact_id = 2147515597,
    x = 493.15,
    y = 2298.88,
    z = 248.42,
}
M.quest_20612_start_point = {
    x = 477.137,
    y = 2304.421,
    z = 250.734,
}
M.quest_20612_start_npc = {
    name_key = "MQ20611_NPC_004_HOTSPOT_REWARD",
    name = npc_names.MQ20611_NPC_004_HOTSPOT_REWARD,
    interact_id = 2147515597,
    x = 493.15,
    y = 2298.88,
    z = 248.42,
}
M.quest_20612_reward_npc = {
    name_key = "MQ20612_NPC_001_REWARD",
    name = npc_names.MQ20612_NPC_001_REWARD or "",
    interact_id = 2147495609,
    x = 1050.70,
    y = 2201.12,
    z = 262.81,
}
M.quest_20613_start_npc = {
    name_key = "MQ20613_NPC_001_START",
    name = npc_names.MQ20613_NPC_001_START or "",
    interact_id = 2147495609,
    x = 1050.70,
    y = 2201.12,
    z = 262.81,
}
M.quest_20613_after_start_reward_npc = {
    name_key = "MQ20613_NPC_002_AFTER_START_REWARD",
    name = npc_names.MQ20613_NPC_002_AFTER_START_REWARD or "",
    interact_id = 2147507242,
    x = 946.25,
    y = 1702.77,
    z = 259.62,
}
M.quest_20614_start_npc = {
    name_key = "MQ20614_NPC_001_START",
    name = "미요우",
    interact_id = 2147507242,
    x = 946.25,
    y = 1702.77,
    z = 259.62,
}
M.quest_20614_reward_npc = {
    name_key = "MQ20614_NPC_002_REWARD",
    name = "드발린",
    interact_id = 2147511075,
    x = 602.85,
    y = 1480.65,
    z = 299.79,
}
M.obelisk_confirm = {
    x = 684,
    y = 437,
    tolerance = 90,
}
M.indicator_title = {
    parent = "quest_indicator_dialog",
    name = "prototype",
    depth = 4,
}
M.indicator_entry_names = {
    "prototype",
    "htmltext",
    "title",
}
M.indicator_teleport = {
    parent = "quest_indicator_dialog",
    name = "teleport",
    depth = 4,
}
M.target_link = {
    parent = "v3_quest_dialog",
    x = 463,
    y = 171,
    tolerance = 45,
    depth = 6,
}
M.dictionary_teleport = {
    parent = "dictionary_dialog",
    name = "teleport_to_npc",
    depth = 6,
}
M.dialog_steps = {
    select_quest = {
        content_id = 10,
        action = "ClickDialogXContinuous",
        reason = "complete quest 20611 mission npc dialog by continuous x-click",
        click_y = 324,
        click_y_tolerance = 8,
    },
    select1 = {
        content_id = 1011,
        action = "ClickDialogXContinuous",
        reason = "complete quest 20611 mission npc dialog by continuous x-click",
    },
    select1_1 = {
        content_id = 1012,
        action = "ClickDialogXContinuous",
        reason = "complete quest 20611 mission npc dialog by continuous x-click",
    },
    select1_1_1 = {
        content_id = 1013,
        action = "ClickDialogXContinuous",
        reason = "complete quest 20611 mission npc dialog by continuous x-click",
    },
    select1_1_1_1 = {
        content_id = 1014,
        action = "ClickDialogXContinuous",
        reason = "complete quest 20611 mission npc dialog by continuous x-click",
    },
}
M.target_dialog_steps = {
    select_quest = {
        content_id = 10,
        action = "ClickDialogXContinuous",
        reason = "accept quest 20611 target npc dialog by continuous x-click",
    },
}
M.hotspot_reward_dialog_steps = {
    select_success = {
        content_id = 10002,
        action = "ClickDialogXContinuous",
        reason = "complete quest 20611 hotspot reward npc dialog by continuous x-click",
    },
}
M.quest_20612_start_dialog_steps = {
    select_quest = {
        content_id = 10,
        action = "ClickDialogXContinuous",
        reason = "accept quest 20612 start npc dialog by continuous x-click",
    },
}
M.quest_20612_reward_dialog_steps = {
    select_success = {
        content_id = 10002,
        action = "ClickDialogXContinuous",
        reason = "complete quest 20612 reward npc dialog by continuous x-click",
    },
}
M.quest_20613_start_dialog_steps = {
    select_quest = {
        content_id = 10,
        action = "ClickDialogXContinuous",
        reason = "accept quest 20613 start npc dialog by continuous x-click",
    },
}
M.quest_20613_after_start_reward_dialog_steps = {
    select_success = {
        content_id = 10002,
        action = "ClickDialogXContinuous",
        reason = "complete quest 20613 after-start reward npc dialog by continuous x-click",
    },
}
M.quest_20614_start_dialog_steps = {
    select_quest = {
        content_id = 10,
        action = "ClickDialogXContinuous",
        reason = "accept quest 20614 start npc dialog by continuous x-click",
    },
}
M.quest_20614_reward_dialog_steps = {
    select_success = {
        content_id = 10002,
        action = "ClickDialogXContinuous",
        reason = "complete quest 20614 reward npc dialog by continuous x-click",
    },
}

local function number(value)
    return tonumber(value) or 0
end

local function dialog_content_id(dialog)
    if type(dialog) ~= "table" then
        return 0
    end
    local value = number(dialog.dialog_content_id)
    if value <= 0 then
        value = number(dialog.content_id)
    end
    return value
end

local function distance3(a, b)
    if type(a) ~= "table" or type(b) ~= "table" then
        return math.huge
    end
    local dx = number(a.x) - number(b.x)
    local dy = number(a.y) - number(b.y)
    local dz = number(a.z) - number(b.z)
    return math.sqrt(dx * dx + dy * dy + dz * dz)
end

local function distance2(a, b)
    if type(a) ~= "table" or type(b) ~= "table" then
        return math.huge
    end
    local dx = number(a.x) - number(b.x)
    local dy = number(a.y) - number(b.y)
    return math.sqrt(dx * dx + dy * dy)
end

local function route_target(route_points, char, waypoint_range)
    route_points = route_points or {}
    waypoint_range = number(waypoint_range)
    if waypoint_range <= 0 then
        waypoint_range = 2.0
    end
    if #route_points <= 0 or type(char) ~= "table" then
        return nil, 0, math.huge
    end

    local nearest_index = 1
    local nearest_dist = math.huge
    for index, point in ipairs(route_points) do
        local dist = distance3(char, point)
        if dist < nearest_dist then
            nearest_index = index
            nearest_dist = dist
        end
    end

    local target_index = nearest_index
    if nearest_dist <= waypoint_range and nearest_index < #route_points then
        target_index = nearest_index + 1
    end
    return route_points[target_index], target_index, nearest_dist
end

local function is_grind_action_name(name)
    return name == "StartStationaryGrind"
        or name == "WaitLevelGrind"
        or name == "WaitQuestComplete"
end

local function action(name, reason, params)
    params = params or {}
    if is_grind_action_name(name) then
        params.requires_combat = true
        params.task_step = "grind"
    end
    return {
        name = name,
        reason = reason or "",
        params = params,
    }
end

local function wait_route_if_active(opts, stage, quest_id, quest)
    local active_stage = tostring(opts and opts.route_following_stage or "")
    if active_stage ~= "" and active_stage == tostring(stage or "") then
        return action("WaitRouteComplete", "wait main quest route complete", {
            quest_id = quest_id,
            quest_step = M.questStep(quest),
            stage = stage,
        })
    end
    return nil
end

local function quest_id(quest)
    return number(quest and quest.id)
end

local function is_earlier_quest(candidate, current)
    if not current then
        return true
    end
    local current_seq = number(current.seq)
    local seq = number(candidate.seq)
    local current_level = number(current.lv_num)
    local level = number(candidate.lv_num)
    if seq > 0 and (current_seq <= 0 or seq < current_seq) then
        return true
    end
    if seq == current_seq and level > 0 and (current_level <= 0 or level < current_level) then
        return true
    end
    if seq == current_seq and level == current_level then
        local id = quest_id(candidate)
        local current_id = quest_id(current)
        return id > 0 and (current_id <= 0 or id < current_id)
    end
    return false
end

local function anchor_from_char(char)
    local anchor = {
        x = number(char and char.x),
        y = number(char and char.y),
        z = number(char and char.z),
    }
    if anchor.x == 0 and anchor.y == 0 and anchor.z == 0 then
        anchor = M.grind_point
    end
    return anchor
end

function M.isRemoteRewardQuestId(id)
    id = number(id)
    if id == number(M.remote_reward_quest_id) then
        return true
    end
    for _, supported_id in ipairs(M.remote_reward_quest_ids or {}) do
        if id == number(supported_id) then
            return true
        end
    end
    return false
end

local function is_supported_quest_id(id)
    id = number(id)
    if id >= M.quest_id_min and id <= M.quest_id_max then
        return true
    end
    if id == M.quest_id then
        return true
    end
    for _, supported_id in ipairs(M.quest_ids or {}) do
        if id == number(supported_id) then
            return true
        end
    end
    return false
end

function M.isReadyPassiveBlueSubmitQuest(quest)
    if type(quest) ~= "table" then
        return false
    end
    local id = quest_id(quest)
    if id <= 0 or is_supported_quest_id(id) or M.isRemoteRewardQuestId(id) then
        return false
    end
    if number(quest.status_code) ~= M.STATUS_DONE then
        return false
    end
    local tab = number(quest.tab)
    local tab_name = tostring(quest.tab_name or "")
    return tab == M.passive_blue_submit_tab
        or tab_name == "任务"
        or tab_name == "制作委托"
end

function M.findReadyPassiveBlueSubmitQuest(quests)
    for _, quest in ipairs(quests or {}) do
        if M.isReadyPassiveBlueSubmitQuest(quest) then
            return quest
        end
    end
    return nil
end

function M.nextLevelGrindBlueSubmitAction(state, runtime, opts)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    local active_stage = tostring(runtime.active_20611_grind_stage or "")
    local level_qid = number(runtime.level_grind_quest_id)
    if runtime.active_20611_grind ~= true
        or not M.isLevelGrindStage(active_stage)
        or level_qid <= 0
        or type(state.dialog) == "table" then
        return nil
    end

    local blue_quest = M.findReadyPassiveBlueSubmitQuest(state.quests)
    if type(blue_quest) ~= "table" then
        return nil
    end

    local qid = quest_id(blue_quest)
    local now = number(opts.now_seconds)
    local cooldown = number(opts.blue_submit_cooldown_seconds)
    if cooldown <= 0 then
        cooldown = M.passive_blue_submit_cooldown_seconds
    end
    if now > 0
        and number(runtime.last_blue_submit_quest_id) == qid
        and now - number(runtime.last_blue_submit_at) < cooldown then
        return nil
    end

    return action("SubmitBlueQuest", "submit completed passive blue quest during level grind", {
        quest_id = qid,
        quest_step = M.questStep(blue_quest),
        status_code = number(blue_quest.status_code),
        tab = number(blue_quest.tab),
        req_count = number(blue_quest.req_count),
        level_grind_quest_id = level_qid,
        stage = M.level_grind_blue_submit_stage,
        grind_stage = active_stage,
    })
end

function M.isLevelGrindStage(stage)
    stage = tostring(stage or "")
    return stage == M.level_grind_stage
        or stage == M.quest_20612_level_grind_stage
        or stage == M.quest_20613_level_grind_stage
        or stage == M.quest_20614_level_grind_stage
        or stage == M.quest_20615_level_grind_stage
        or stage == M.quest_20621_level_grind_stage
        or stage == M.quest_20622_level_grind_stage
end

function M.isGrindStage(stage)
    stage = tostring(stage or "")
    return stage == "quest_20611_grind"
        or M.isLevelGrindStage(stage)
end

function M.distanceToGrindPoint(char)
    return distance3(char, M.grind_point)
end

function M.distanceToNpc(char)
    return distance3(char, M.npc)
end

function M.distanceToObelisk(char)
    return distance3(char, M.obelisk)
end

function M.distanceToTargetNpc(char)
    return distance3(char, M.target_npc)
end

function M.distanceToHotspotRewardNpc(char)
    return distance3(char, M.hotspot_reward_npc)
end

function M.distanceToQuest20612StartPoint(char)
    return distance3(char, M.quest_20612_start_point)
end

function M.distanceToQuest20612StartNpc(char)
    return distance3(char, M.quest_20612_start_npc)
end

function M.distanceToQuest20612RewardNpc(char)
    return distance3(char, M.quest_20612_reward_npc)
end

function M.distanceToQuest20613StartNpc(char)
    return distance3(char, M.quest_20613_start_npc)
end

function M.distanceToQuest20613AfterStartRewardNpc(char)
    return distance3(char, M.quest_20613_after_start_reward_npc)
end

function M.distanceToQuest20614StartNpc(char)
    return distance3(char, M.quest_20614_start_npc)
end

function M.distanceToQuest20614RewardNpc(char)
    return distance3(char, M.quest_20614_reward_npc)
end

function M.distanceToPost20612Level14GrindPoint(char)
    return distance3(char, M.post_20612_level14_grind_point)
end

function M.distanceToQuest20614Level17GrindPoint(char)
    return distance3(char, M.quest_20614_level17_grind_point)
end

function M.distanceToQuest20615Level20GrindPoint(char)
    return distance3(char, M.quest_20615_level20_grind_point)
end

function M.distanceToQuest20621Level22GrindPoint(char)
    return distance3(char, M.quest_20621_level22_grind_point)
end

function M.distanceToQuest20622Level25GrindPoint(char)
    return distance3(char, M.quest_20622_level25_grind_point)
end

function M.distanceToQuest20615TargetNpc(char)
    return distance3(char, M.quest_20615_target_npc)
end

function M.distanceToQuest20615MorheimNpc(char)
    return distance3(char, M.quest_20615_morheim_npc)
end

function M.distanceToQuest20620StartNpc(char)
    return distance3(char, M.quest_20620_start_npc)
end

function M.distanceToQuest20620AfterTeleportNpc(char)
    return distance3(char, M.quest_20620_after_teleport_npc)
end

function M.distanceToQuest20620AfterStigmaNpc(char)
    return distance3(char, M.quest_20620_after_stigma_npc)
end

function M.distanceToQuest20620Obelisk(char)
    return distance3(char, M.quest_20620_obelisk)
end

function M.distanceToQuest20620AfterObeliskNpc(char)
    return distance3(char, M.quest_20620_after_obelisk_npc)
end

function M.distanceToQuest20621AfterTeleportNpc(char)
    return distance3(char, M.quest_20621_after_teleport_npc)
end

function M.distanceToQuest20621AfterDialogTeleportNpc(char)
    return distance3(char, M.quest_20621_after_dialog_teleport_npc)
end

function M.isNearQuest20612RewardNpc(state, opts)
    state = state or {}
    opts = opts or {}
    if type(state.char) ~= "table" then
        return false
    end
    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return false
    end
    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    return M.distanceToQuest20612RewardNpc(state.char) <= range
end

function M.isNearQuest20613StartNpc(state, opts)
    state = state or {}
    opts = opts or {}
    if type(state.char) ~= "table" then
        return false
    end
    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return false
    end
    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    return M.distanceToQuest20613StartNpc(state.char) <= range
end

function M.isNearQuest20613AfterStartRewardNpc(state, opts)
    state = state or {}
    opts = opts or {}
    if type(state.char) ~= "table" then
        return false
    end
    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return false
    end
    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    return M.distanceToQuest20613AfterStartRewardNpc(state.char) <= range
end

function M.isNearQuest20614StartNpc(state, opts)
    state = state or {}
    opts = opts or {}
    if type(state.char) ~= "table" then
        return false
    end
    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return false
    end
    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    return M.distanceToQuest20614StartNpc(state.char) <= range
end

function M.isNearQuest20614RewardNpc(state, opts)
    state = state or {}
    opts = opts or {}
    if type(state.char) ~= "table" then
        return false
    end
    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return false
    end
    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    return M.distanceToQuest20614RewardNpc(state.char) <= range
end

function M.isNearQuest20615TargetNpc(state, opts)
    state = state or {}
    opts = opts or {}
    if type(state.char) ~= "table" then
        return false
    end
    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return false
    end
    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    return M.distanceToQuest20615TargetNpc(state.char) <= range
end

function M.isNearQuest20615MorheimNpc(state, opts)
    state = state or {}
    opts = opts or {}
    if type(state.char) ~= "table" then
        return false
    end
    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.quest_20615_morheim_npc.big_map_id then
        return false
    end
    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    return M.distanceToQuest20615MorheimNpc(state.char) <= range
end

function M.isNearQuest20620StartNpc(state, opts)
    state = state or {}
    opts = opts or {}
    if type(state.char) ~= "table" then
        return false
    end
    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.quest_20620_start_npc.big_map_id then
        return false
    end
    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    return M.distanceToQuest20620StartNpc(state.char) <= range
end

function M.isNearQuest20620AfterTeleportNpc(state, opts)
    state = state or {}
    opts = opts or {}
    if type(state.char) ~= "table" then
        return false
    end
    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.quest_20620_after_teleport_npc.big_map_id then
        return false
    end
    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    return M.distanceToQuest20620AfterTeleportNpc(state.char) <= range
end

function M.isNearQuest20620AfterStigmaNpc(state, opts)
    state = state or {}
    opts = opts or {}
    if type(state.char) ~= "table" then
        return false
    end
    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.quest_20620_after_stigma_npc.big_map_id then
        return false
    end
    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    return M.distanceToQuest20620AfterStigmaNpc(state.char) <= range
end

function M.isNearQuest20620Obelisk(state, opts)
    state = state or {}
    opts = opts or {}
    if type(state.char) ~= "table" then
        return false
    end
    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.quest_20620_obelisk.big_map_id then
        return false
    end
    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    return M.distanceToQuest20620Obelisk(state.char) <= range
end

function M.isNearQuest20620AfterObeliskNpc(state, opts)
    state = state or {}
    opts = opts or {}
    if type(state.char) ~= "table" then
        return false
    end
    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.quest_20620_after_obelisk_npc.big_map_id then
        return false
    end
    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    return M.distanceToQuest20620AfterObeliskNpc(state.char) <= range
end

function M.isNearQuest20621AfterTeleportNpc(state, opts)
    state = state or {}
    opts = opts or {}
    if type(state.char) ~= "table" then
        return false
    end
    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.quest_20621_after_teleport_npc.big_map_id then
        return false
    end
    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    return M.distanceToQuest20621AfterTeleportNpc(state.char) <= range
end

function M.isNearQuest20621AfterDialogTeleportNpc(state, opts)
    state = state or {}
    opts = opts or {}
    if type(state.char) ~= "table" then
        return false
    end
    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.quest_20621_after_dialog_teleport_npc.big_map_id then
        return false
    end
    local range = number(opts.npc_range)
    if range <= 0 then
        range = 6
    end
    return M.distanceToQuest20621AfterDialogTeleportNpc(state.char) <= range
end

function M.questStep(quest)
    return number(quest and quest.req_count)
end

function M.questRequiredLevel(quest)
    return number(quest and quest.lv_num)
end

function M.quest20612RequiredLevel(quest)
    local required = M.questRequiredLevel(quest)
    if required <= 0 then
        required = M.quest_20612_required_level
    end
    return required
end

function M.findQuest(quests)
    local active = nil
    local fallback = nil
    local done = nil
    for _, quest in ipairs(quests or {}) do
        if is_supported_quest_id(quest_id(quest)) then
            if M.isQuestActive(quest) then
                if is_earlier_quest(quest, active) then
                    active = quest
                end
            elseif M.isQuestDone(quest) then
                if is_earlier_quest(quest, done) then
                    done = quest
                end
            else
                if is_earlier_quest(quest, fallback) then
                    fallback = quest
                end
            end
        end
    end
    return active or fallback or done
end

function M.findQuestById(quests, id)
    id = number(id)
    if id <= 0 then
        return nil
    end
    for _, quest in ipairs(quests or {}) do
        if quest_id(quest) == id then
            return quest
        end
    end
    return nil
end

function M.findActiveQuest(quests)
    local best = nil
    for _, quest in ipairs(quests or {}) do
        if M.isQuestActive(quest) and is_earlier_quest(quest, best) then
            best = quest
        end
    end
    return best
end

function M.findLevelBlockedQuest(quests)
    local best = nil
    for _, quest in ipairs(quests or {}) do
        if is_supported_quest_id(quest_id(quest))
            and number(quest.status_code) == 6 then
            if is_earlier_quest(quest, best) then
                best = quest
            end
        end
    end
    return best
end

function M.findRemoteRewardQuest(quests)
    local active = nil
    local ready = nil
    local fallback = nil
    for _, quest in ipairs(quests or {}) do
        if M.isRemoteRewardQuestId(quest_id(quest)) then
            if number(quest.status_code) == 4 then
                ready = ready or quest
            elseif number(quest.status_code) == 3 then
                active = active or quest
            else
                fallback = fallback or quest
            end
        end
    end
    return ready or active or fallback
end

function M.isQuestKnown(quest)
    return type(quest) == "table"
        and is_supported_quest_id(quest_id(quest))
end

function M.isQuestActive(quest)
    return M.isQuestKnown(quest)
        and number(quest.status_code) == 3
end

function M.isQuestDone(quest)
    return M.isQuestKnown(quest)
        and number(quest.status_code) == 4
end

function M.isQuest20620AfterObeliskTeleportSnapshot(quest)
    if quest_id(quest) ~= M.quest_20620_id then
        return false
    end
    local step = M.questStep(quest)
    return (M.isQuestActive(quest) and step > 4)
        or (M.isQuestDone(quest) and step >= 4)
end

function M.isQuestLevelBlocked(quest)
    return M.isQuestKnown(quest)
        and number(quest.status_code) == 6
end

local function sequential_quest_done(runtime, quest)
    runtime = runtime or {}
    local qid = quest_id(quest)
    if qid == M.quest_id then
        return M.isQuestDone(quest)
            and runtime.completed_20611_hotspot_reward == true
    end
    if qid == M.quest_20612_id then
        return M.isQuestDone(quest)
            and runtime.completed_20612_reward_dialog == true
    end
    return M.isQuestDone(quest)
end

function M.findSequentialQuest(quests, runtime)
    local best = nil
    for _, quest in ipairs(quests or {}) do
        if M.isQuestKnown(quest)
            and (M.isQuestActive(quest)
                or M.isQuestLevelBlocked(quest)
                or M.isQuestDone(quest))
            and not sequential_quest_done(runtime, quest) then
            if is_earlier_quest(quest, best) then
                best = quest
            end
        end
    end
    return best
end

function M.isRemoteRewardQuest(quest)
    return type(quest) == "table"
        and M.isRemoteRewardQuestId(quest_id(quest))
end

function M.isRemoteRewardReady(quest)
    return M.isRemoteRewardQuest(quest)
        and number(quest.status_code) == 4
end

function M.isRemoteGrindActive(quest)
    return M.isRemoteRewardQuest(quest)
        and number(quest.status_code) == 3
end

function M.isRemoteRewardDialog(dialog)
    if type(dialog) ~= "table" then
        return false
    end
    return M.isRemoteRewardQuestId(dialog.quest_id)
        and tostring(dialog.type_text or "") == "select_quest_reward_remote"
end

function M.isMissionNpcDialog(dialog)
    return type(dialog) == "table"
        and number(dialog.npc_dialog_id) == M.npc.interact_id
end

function M.isTargetNpcDialog(dialog)
    return type(dialog) == "table"
        and number(dialog.npc_dialog_id) == M.target_npc.interact_id
end

function M.isHotspotRewardNpcDialog(dialog)
    return type(dialog) == "table"
        and number(dialog.npc_dialog_id) == M.hotspot_reward_npc.interact_id
end

function M.isQuest20612StartNpcDialog(dialog)
    return type(dialog) == "table"
        and number(dialog.npc_dialog_id) == M.quest_20612_start_npc.interact_id
end

function M.isQuest20612RewardNpcDialog(dialog)
    return type(dialog) == "table"
        and number(dialog.npc_dialog_id) == M.quest_20612_reward_npc.interact_id
end

function M.isQuest20613StartNpcDialog(dialog)
    if type(dialog) ~= "table"
        or number(dialog.npc_dialog_id) ~= M.quest_20613_start_npc.interact_id then
        return false
    end
    local type_text = tostring(dialog.type_text or "")
    return type_text == "select_quest"
        or dialog_content_id(dialog) == M.quest_20613_start_dialog_steps.select_quest.content_id
end

function M.isQuest20613AfterStartRewardNpcDialog(dialog)
    if type(dialog) ~= "table"
        or number(dialog.npc_dialog_id) ~= M.quest_20613_after_start_reward_npc.interact_id then
        return false
    end
    local dialog_qid = number(dialog.quest_id)
    if dialog_qid > 0 and dialog_qid ~= M.quest_20613_id then
        return false
    end
    local type_text = tostring(dialog.type_text or "")
    return type_text == "select_success"
        or dialog_content_id(dialog) == M.quest_20613_after_start_reward_dialog_steps.select_success.content_id
end

function M.isQuest20614StartNpcDialog(dialog)
    if type(dialog) ~= "table"
        or number(dialog.npc_dialog_id) ~= M.quest_20614_start_npc.interact_id then
        return false
    end
    local dialog_qid = number(dialog.quest_id)
    if dialog_qid > 0 and dialog_qid ~= M.quest_20614_id then
        return false
    end
    local type_text = tostring(dialog.type_text or "")
    return type_text == "select_quest"
        or dialog_content_id(dialog) == M.quest_20614_start_dialog_steps.select_quest.content_id
end

function M.isQuest20614RewardNpcDialog(dialog)
    if type(dialog) ~= "table"
        or number(dialog.npc_dialog_id) ~= M.quest_20614_reward_npc.interact_id then
        return false
    end
    local dialog_qid = number(dialog.quest_id)
    if dialog_qid > 0 and dialog_qid ~= M.quest_20614_id then
        return false
    end
    local type_text = tostring(dialog.type_text or "")
    return type_text == "select_success"
        or dialog_content_id(dialog) == M.quest_20614_reward_dialog_steps.select_success.content_id
end

function M.isQuest20615TargetNpcDialog(dialog)
    if type(dialog) ~= "table"
        or number(dialog.npc_dialog_id) ~= M.quest_20615_target_npc.interact_id then
        return false
    end
    local dialog_qid = number(dialog.quest_id)
    return dialog_qid <= 0 or dialog_qid == M.quest_20615_id
end

function M.isQuest20615MorheimNpcDialog(dialog)
    if type(dialog) ~= "table"
        or number(dialog.npc_dialog_id) ~= M.quest_20615_morheim_npc.interact_id then
        return false
    end
    local dialog_qid = number(dialog.quest_id)
    return dialog_qid <= 0 or dialog_qid == M.quest_20615_id
end

function M.isQuest20620StartNpcDialog(dialog)
    if type(dialog) ~= "table"
        or number(dialog.npc_dialog_id) ~= M.quest_20620_start_npc.interact_id then
        return false
    end
    local dialog_qid = number(dialog.quest_id)
    return dialog_qid <= 0 or dialog_qid == M.quest_20620_id
end

function M.isQuest20620AfterTeleportNpcDialog(dialog)
    if type(dialog) ~= "table"
        or number(dialog.npc_dialog_id) ~= M.quest_20620_after_teleport_npc.interact_id then
        return false
    end
    local dialog_qid = number(dialog.quest_id)
    return dialog_qid <= 0 or dialog_qid == M.quest_20620_id
end

function M.isQuest20620AfterStigmaNpcDialog(dialog)
    if type(dialog) ~= "table"
        or number(dialog.npc_dialog_id) ~= M.quest_20620_after_stigma_npc.interact_id then
        return false
    end
    local dialog_qid = number(dialog.quest_id)
    return dialog_qid <= 0 or dialog_qid == M.quest_20620_id
end

function M.isQuest20620AfterObeliskNpcDialog(dialog)
    if type(dialog) ~= "table"
        or number(dialog.npc_dialog_id) ~= M.quest_20620_after_obelisk_npc.interact_id then
        return false
    end
    local dialog_qid = number(dialog.quest_id)
    return dialog_qid <= 0 or dialog_qid == M.quest_20620_id
end

function M.isQuest20621AfterTeleportNpcDialog(dialog)
    if type(dialog) ~= "table"
        or number(dialog.npc_dialog_id) ~= M.quest_20621_after_teleport_npc.interact_id then
        return false
    end
    local dialog_qid = number(dialog.quest_id)
    return dialog_qid <= 0 or dialog_qid == M.quest_20621_id
end

function M.isQuest20621AfterDialogTeleportNpcDialog(dialog)
    if type(dialog) ~= "table" then
        return false
    end
    local expected_name = tostring(M.quest_20621_after_dialog_teleport_npc.name or "")
    local dialog_name = tostring(dialog.npc_name or dialog.name or "")
    if expected_name ~= "" and dialog_name ~= "" and dialog_name ~= expected_name then
        return false
    end
    local dialog_qid = number(dialog.quest_id)
    if dialog_name ~= "" then
        return dialog_qid <= 0 or dialog_qid == M.quest_20621_id
    end
    return dialog_qid == M.quest_20621_id
end

function M.isObeliskConfirmVisible(state)
    return type(state) == "table"
        and type(state.ui) == "table"
        and state.ui.obelisk_confirm_visible == true
end

function M.teleportDetected(state, runtime, opts)
    opts = opts or {}
    runtime = runtime or {}
    local min_distance = number(opts.teleport_min_distance)
    if min_distance <= 0 then
        min_distance = 20
    end

    local current_big_map = number(state and state.big_map_id)
    local start_big_map = number(runtime.teleport_start_big_map_id)
    if start_big_map > 0 and current_big_map > 0 and start_big_map ~= current_big_map then
        return true, "big_map_changed"
    end

    local start_pos = runtime.teleport_start_pos
    local char = state and state.char
    if type(start_pos) == "table" and type(char) == "table" then
        local dist = distance3(start_pos, char)
        if dist >= min_distance then
            return true, "position_changed"
        end
    end

    return false, "waiting_position_change"
end

function M.nextMissionNpcAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or state.quest or M.findQuestById(state.quests, M.quest_id)

    if runtime.completed_20611_mission_dialog == true then
        return action("Idle", "quest 20611 mission dialog already completed", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = "quest_20611_mission_npc",
        })
    end

    local dialog = state.dialog
    if M.isMissionNpcDialog(dialog) then
        local type_text = tostring(dialog.type_text or "")
        local step = M.dialog_steps[type_text]
        if step then
            return action(step.action, step.reason, {
                quest_id = M.quest_id,
                quest_step = M.questStep(quest),
                expected_content_id = step.content_id,
                content_id = number(dialog.dialog_content_id),
                type_text = type_text,
                click_x = opts.dialog_click_x or 25,
                click_y = step.click_y,
                click_y_tolerance = step.click_y_tolerance,
                interact_id = M.npc.interact_id,
                npc_name = M.npc.name,
                npc_name_key = M.npc.name_key,
                stage = "quest_20611_mission_npc",
            })
        end

        return action("DumpDialog", "unknown quest 20611 mission dialog stage", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            type_text = type_text,
            content_id = number(dialog.dialog_content_id),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.npc.interact_id,
            npc_name = M.npc.name,
            npc_name_key = M.npc.name_key,
            stage = "quest_20611_mission_npc",
        })
    end

    if type(dialog) == "table" then
        return action("DumpDialog", "different npc dialog is already open", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = number(dialog.dialog_content_id),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.npc.interact_id,
            npc_name = M.npc.name,
            npc_name_key = M.npc.name_key,
            stage = "quest_20611_mission_npc",
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_id })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return action("Idle", "quest 20611 mission npc wrong map", {
            quest_id = M.quest_id,
            big_map_id = current_big_map,
            expected_big_map_id = M.big_map_id,
        })
    end

    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    local dist = M.distanceToNpc(char)
    if dist > range then
        return action("NavigateToNpc", "move to quest 20611 mission npc", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = "quest_20611_mission_npc",
            interact_id = M.npc.interact_id,
            npc_name = M.npc.name,
            npc_name_key = M.npc.name_key,
            x = M.npc.x,
            y = M.npc.y,
            z = M.npc.z,
            distance = dist,
            range = range,
        })
    end

    return action("InteractNpc", "open quest 20611 mission npc dialog", {
        quest_id = M.quest_id,
        quest_step = M.questStep(quest),
        stage = "quest_20611_mission_npc",
        interact_id = M.npc.interact_id,
        npc_name = M.npc.name,
        npc_name_key = M.npc.name_key,
    })
end

function M.nextObeliskAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or state.quest or M.findQuestById(state.quests, M.quest_id)

    if runtime.completed_20611_obelisk == true then
        return action("Idle", "quest 20611 obelisk already confirmed", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = M.obelisk_stage,
        })
    end

    if M.isObeliskConfirmVisible(state) or runtime.opened_20611_obelisk == true then
        return action("ClickObeliskConfirm", "confirm quest 20611 obelisk registration", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = M.obelisk_stage,
            npc_name = M.obelisk.name,
            npc_name_key = M.obelisk.name_key,
            confirm_x = M.obelisk_confirm.x,
            confirm_y = M.obelisk_confirm.y,
            confirm_tolerance = M.obelisk_confirm.tolerance,
        })
    end

    if type(state.dialog) == "table" then
        return action("DumpDialog", "different npc dialog is already open before obelisk confirm", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            type_text = tostring(state.dialog.type_text or ""),
            content_id = number(state.dialog.dialog_content_id),
            npc_dialog_id = number(state.dialog.npc_dialog_id),
            interact_id = M.obelisk.interact_id,
            npc_name = M.obelisk.name,
            npc_name_key = M.obelisk.name_key,
            stage = M.obelisk_stage,
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_id })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return action("Idle", "quest 20611 obelisk wrong map", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            big_map_id = current_big_map,
            expected_big_map_id = M.big_map_id,
            stage = M.obelisk_stage,
        })
    end

    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    local dist = M.distanceToObelisk(char)
    if dist > range then
        return action("NavigateToNpc", "move to quest 20611 obelisk", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = M.obelisk_stage,
            interact_id = M.obelisk.interact_id,
            npc_name = M.obelisk.name,
            npc_name_key = M.obelisk.name_key,
            x = M.obelisk.x,
            y = M.obelisk.y,
            z = M.obelisk.z,
            distance = dist,
            range = range,
        })
    end

    return action("InteractNpc", "open quest 20611 obelisk confirm popup", {
        quest_id = M.quest_id,
        quest_step = M.questStep(quest),
        stage = M.obelisk_stage,
        interact_id = M.obelisk.interact_id,
        npc_name = M.obelisk.name,
        npc_name_key = M.obelisk.name_key,
    })
end

function M.nextTargetNpcAction(state, runtime, opts, quest)
    state = state or {}
    opts = opts or {}
    quest = quest or state.quest or M.findQuestById(state.quests, M.quest_id)

    local dialog = state.dialog
    if M.isTargetNpcDialog(dialog) then
        local type_text = tostring(dialog.type_text or "")
        local step = M.target_dialog_steps[type_text]
        if step then
            local expected_content_id = number(step.content_id)
            if expected_content_id <= 0 then
                expected_content_id = number(dialog.dialog_content_id)
            end
            return action(step.action, step.reason, {
                quest_id = M.quest_id,
                quest_step = M.questStep(quest),
                expected_content_id = expected_content_id,
                content_id = number(dialog.dialog_content_id),
                type_text = type_text,
                click_x = opts.dialog_click_x or 25,
                click_y = step.click_y,
                click_y_tolerance = step.click_y_tolerance,
                max_steps = step.max_steps,
                delay_ms = step.delay_ms,
                interact_id = M.target_npc.interact_id,
                npc_name = M.target_npc.name,
                npc_name_key = M.target_npc.name_key,
                stage = "quest_20611_target_npc",
            })
        end

        return action("DumpDialog", "unknown quest 20611 target npc dialog stage", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            type_text = type_text,
            content_id = number(dialog.dialog_content_id),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.target_npc.interact_id,
            npc_name = M.target_npc.name,
            npc_name_key = M.target_npc.name_key,
            stage = "quest_20611_target_npc",
        })
    end

    if type(dialog) == "table" then
        return action("DumpDialog", "different npc dialog is already open before target npc", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = number(dialog.dialog_content_id),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.target_npc.interact_id,
            npc_name = M.target_npc.name,
            npc_name_key = M.target_npc.name_key,
            stage = "quest_20611_target_npc",
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_id })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return action("Idle", "quest 20611 target npc wrong map", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            big_map_id = current_big_map,
            expected_big_map_id = M.big_map_id,
            stage = "quest_20611_target_npc",
        })
    end

    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    local dist = M.distanceToTargetNpc(char)
    if dist > range then
        return action("NavigateToNpc", "move to quest 20611 target npc", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = "quest_20611_target_npc",
            interact_id = M.target_npc.interact_id,
            npc_name = M.target_npc.name,
            npc_name_key = M.target_npc.name_key,
            x = M.target_npc.x,
            y = M.target_npc.y,
            z = M.target_npc.z,
            distance = dist,
            range = range,
        })
    end

    return action("InteractNpc", "open quest 20611 target npc dialog", {
        quest_id = M.quest_id,
        quest_step = M.questStep(quest),
        stage = "quest_20611_target_npc",
        interact_id = M.target_npc.interact_id,
        npc_name = M.target_npc.name,
        npc_name_key = M.target_npc.name_key,
    })
end

function M.nextIndicatorEntryName(runtime)
    local names = M.indicator_entry_names or {}
    local last = tostring(runtime and runtime.clicked_20611_indicator_entry_name or "")
    if last ~= "" then
        for index, name in ipairs(names) do
            if name == last then
                return names[(index % #names) + 1] or M.indicator_title.name
            end
        end
    end
    return M.indicator_title.name
end

function M.openCurrentTrackerAction(quest, reason, runtime)
    local qid = quest_id(quest)
    if qid <= 0 then
        qid = M.quest_id
    end
    local entry_name = M.nextIndicatorEntryName(runtime)
    return action("ClickUiControl", reason or "open current tracked quest detail", {
        quest_id = qid,
        quest_step = M.questStep(quest),
        stage = M.indicator_title_stage,
        parent = M.indicator_title.parent,
        name = entry_name,
        depth = M.indicator_title.depth,
        previous_name = tostring(runtime and runtime.clicked_20611_indicator_entry_name or ""),
    })
end

function M.currentTrackerTeleportAction(quest, runtime, params)
    params = params or {}
    local qid = number(params.quest_id)
    if qid <= 0 then
        qid = quest_id(quest)
    end
    if qid <= 0 then
        qid = M.quest_id
    end
    return action("ClickUiControlWaitTeleport", "current tracker direct teleport after panel did not open", {
        quest_id = qid,
        quest_step = params.quest_step or M.questStep(quest),
        stage = params.stage or M.target_teleport_stage,
        parent = M.indicator_teleport.parent,
        name = M.indicator_teleport.name,
        depth = M.indicator_teleport.depth,
        previous_name = tostring(runtime and runtime.clicked_20611_indicator_entry_name or ""),
        wait_teleport = true,
    })
end

function M.nextCurrentQuestTeleportAction(state, runtime, quest, reason, params)
    state = state or {}
    runtime = runtime or {}
    params = params or {}
    local ui = type(state.ui) == "table" and state.ui or {}
    if ui.quest_panel_visible ~= true
        or runtime.clicked_20611_indicator_title ~= true then
        local last_open_candidate = M.indicator_entry_names[#M.indicator_entry_names]
        if runtime.clicked_20611_indicator_title == true
            and tostring(runtime.clicked_20611_indicator_entry_name or "") == tostring(last_open_candidate or "") then
            return M.currentTrackerTeleportAction(quest, runtime, params)
        end
        return M.openCurrentTrackerAction(quest, "open current tracked quest before teleport", runtime)
    end

    local out = {}
    for key, value in pairs(params) do
        out[key] = value
    end
    local qid = number(out.quest_id)
    if qid <= 0 then
        qid = quest_id(quest)
    end
    if qid <= 0 then
        qid = M.quest_id
    end
    out.quest_id = qid
    out.quest_step = out.quest_step or M.questStep(quest)
    out.open_panel_key = false
    out.require_panel_visible = true
    if out.wait_teleport == nil then
        out.wait_teleport = true
    end
    return action("QuestTeleport", reason or "current quest panel visible; immediate move", out)
end

function M.nextTargetTeleportAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or state.quest or M.findQuestById(state.quests, M.quest_id)

    if runtime.completed_20611_target_teleport == true then
        return action("Idle", "quest 20611 target teleport already completed", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = M.target_teleport_stage,
        })
    end

    return M.nextCurrentQuestTeleportAction(state, runtime, quest, "quest 20611 quest panel visible; immediate move", {
        quest_id = M.quest_id,
        quest_step = M.questStep(quest),
        stage = M.target_teleport_stage,
        wait_teleport = true,
    })
end

function M.nextHotspotTeleportAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or state.quest or M.findQuestById(state.quests, M.quest_id)

    if runtime.completed_20611_hotspot_teleport == true then
        return M.nextHotspotRewardAction(state, runtime, opts, quest)
    end

    local arrival_range = number(opts.hotspot_arrival_range)
    if arrival_range <= 0 then
        arrival_range = 12
    end
    if distance2(state.char, M.hotspot_node) <= arrival_range then
        return M.nextHotspotRewardAction(state, runtime, opts, quest)
    end

    return action("MapNodeTeleportByName", "teleport quest 20611 to hotspot node", {
        quest_id = M.quest_id,
        quest_step = M.questStep(quest),
        stage = M.hotspot_teleport_stage,
        node_name = M.hotspot_node.name,
        node_name_en = M.hotspot_node.name_en,
        node_id = M.hotspot_node.node_id,
        x = M.hotspot_node.x,
        y = M.hotspot_node.y,
        z = M.hotspot_node.z,
        wait_teleport = true,
    })
end

function M.nextTargetStepAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or state.quest or M.findQuestById(state.quests, M.quest_id)

    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    local near_target_npc = type(state.char) == "table"
        and M.distanceToTargetNpc(state.char) <= range
    if M.isTargetNpcDialog(state.dialog) then
        return M.nextTargetNpcAction(state, runtime, opts, quest)
    end
    if runtime.completed_20611_target_dialog == true then
        return M.nextHotspotTeleportAction(state, runtime, opts, quest)
    end
    if near_target_npc or runtime.completed_20611_target_teleport == true then
        return M.nextTargetNpcAction(state, runtime, opts, quest)
    end

    return M.nextTargetTeleportAction(state, runtime, opts, quest)
end

function M.nextHotspotRewardAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or state.quest or M.findQuestById(state.quests, M.quest_id)

    if runtime.completed_20611_hotspot_reward == true then
        return action("Idle", "quest 20611 hotspot reward npc already completed", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = M.hotspot_reward_stage,
        })
    end

    local dialog = state.dialog
    if M.isHotspotRewardNpcDialog(dialog) then
        local type_text = tostring(dialog.type_text or "")
        local step = M.hotspot_reward_dialog_steps[type_text]
        if step then
            return action(step.action, step.reason, {
                quest_id = M.quest_id,
                quest_step = M.questStep(quest),
                expected_content_id = step.content_id,
                content_id = number(dialog.dialog_content_id),
                type_text = type_text,
                click_x = opts.dialog_click_x or 25,
                max_steps = step.max_steps,
                delay_ms = step.delay_ms,
                interact_id = M.hotspot_reward_npc.interact_id,
                npc_name = M.hotspot_reward_npc.name,
                npc_name_key = M.hotspot_reward_npc.name_key,
                stage = M.hotspot_reward_stage,
            })
        end

        return action("DumpDialog", "unknown quest 20611 hotspot reward npc dialog stage", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            type_text = type_text,
            content_id = number(dialog.dialog_content_id),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.hotspot_reward_npc.interact_id,
            npc_name = M.hotspot_reward_npc.name,
            npc_name_key = M.hotspot_reward_npc.name_key,
            stage = M.hotspot_reward_stage,
        })
    end

    if type(dialog) == "table" then
        return action("DumpDialog", "different npc dialog is already open before hotspot reward npc", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = number(dialog.dialog_content_id),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.hotspot_reward_npc.interact_id,
            npc_name = M.hotspot_reward_npc.name,
            npc_name_key = M.hotspot_reward_npc.name_key,
            stage = M.hotspot_reward_stage,
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_id })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return action("Idle", "quest 20611 hotspot reward npc wrong map", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            big_map_id = current_big_map,
            expected_big_map_id = M.big_map_id,
            stage = M.hotspot_reward_stage,
        })
    end

    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    local dist = M.distanceToHotspotRewardNpc(char)
    if dist > range then
        return action("NavigateToNpc", "move to quest 20611 hotspot reward npc", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = M.hotspot_reward_stage,
            interact_id = M.hotspot_reward_npc.interact_id,
            npc_name = M.hotspot_reward_npc.name,
            npc_name_key = M.hotspot_reward_npc.name_key,
            x = M.hotspot_reward_npc.x,
            y = M.hotspot_reward_npc.y,
            z = M.hotspot_reward_npc.z,
            distance = dist,
            range = range,
        })
    end

    return action("InteractNpc", "open quest 20611 hotspot reward npc dialog", {
        quest_id = M.quest_id,
        quest_step = M.questStep(quest),
        stage = M.hotspot_reward_stage,
        interact_id = M.hotspot_reward_npc.interact_id,
        npc_name = M.hotspot_reward_npc.name,
        npc_name_key = M.hotspot_reward_npc.name_key,
        allow_interact_id_fallback = true,
    })
end

function M.nextQuest20612StartAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or state.quest or M.findQuestById(state.quests, M.quest_20612_id)

    if runtime.completed_20612_start_dialog == true then
        return M.nextQuest20612TaskTeleportAction(state, runtime, opts, quest)
    end

    local char = state.char
    if type(char) == "table" then
        local current_big_map = number(state.big_map_id)
        if current_big_map > 0 and current_big_map ~= M.big_map_id then
            return action("Idle", "quest 20612 start npc wrong map", {
                quest_id = M.quest_20612_id,
                quest_step = M.questStep(quest),
                big_map_id = current_big_map,
                expected_big_map_id = M.big_map_id,
                stage = M.quest_20612_start_stage,
            })
        end

        local point_range = number(opts.quest_20612_start_point_range)
        if point_range <= 0 then
            point_range = 3
        end
        local point_dist = M.distanceToQuest20612StartPoint(char)
        if runtime.reached_20612_start_point ~= true and point_dist > point_range then
            return action("NavigateToNpc", "move to quest 20612 start point", {
                quest_id = M.quest_20612_id,
                quest_step = M.questStep(quest),
                stage = M.quest_20612_start_stage,
                interact_id = M.quest_20612_start_npc.interact_id,
                npc_name = M.quest_20612_start_npc.name,
                npc_name_key = M.quest_20612_start_npc.name_key,
                x = M.quest_20612_start_point.x,
                y = M.quest_20612_start_point.y,
                z = M.quest_20612_start_point.z,
                distance = point_dist,
                range = point_range,
            })
        end
    elseif type(state.dialog) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_20612_id })
    end

    local dialog = state.dialog
    if M.isQuest20612StartNpcDialog(dialog) then
        local type_text = tostring(dialog.type_text or "")
        local step = M.quest_20612_start_dialog_steps[type_text]
        if step then
            return action(step.action, step.reason, {
                quest_id = M.quest_20612_id,
                quest_step = M.questStep(quest),
                expected_content_id = step.content_id,
                content_id = number(dialog.dialog_content_id),
                type_text = type_text,
                click_x = opts.dialog_click_x or 25,
                click_y = step.click_y,
                click_y_tolerance = step.click_y_tolerance,
                max_steps = step.max_steps,
                delay_ms = step.delay_ms,
                interact_id = M.quest_20612_start_npc.interact_id,
                npc_name = M.quest_20612_start_npc.name,
                npc_name_key = M.quest_20612_start_npc.name_key,
                stage = M.quest_20612_start_stage,
                mark_20612_start_point_reached = true,
            })
        end

        return action("DumpDialog", "unknown quest 20612 start npc dialog stage", {
            quest_id = M.quest_20612_id,
            quest_step = M.questStep(quest),
            type_text = type_text,
            content_id = number(dialog.dialog_content_id),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20612_start_npc.interact_id,
            npc_name = M.quest_20612_start_npc.name,
            npc_name_key = M.quest_20612_start_npc.name_key,
            stage = M.quest_20612_start_stage,
        })
    end

    if type(dialog) == "table" then
        return action("DumpDialog", "different npc dialog is already open before quest 20612 start npc", {
            quest_id = M.quest_20612_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = number(dialog.dialog_content_id),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20612_start_npc.interact_id,
            npc_name = M.quest_20612_start_npc.name,
            npc_name_key = M.quest_20612_start_npc.name_key,
            stage = M.quest_20612_start_stage,
        })
    end

    if type(char) == "table" then
        local npc_range = number(opts.npc_range)
        if npc_range <= 0 then
            npc_range = 4
        end
        local npc_dist = M.distanceToQuest20612StartNpc(char)
        if npc_dist > npc_range then
            return action("NavigateToNpc", "move from quest 20612 start point to npc", {
                quest_id = M.quest_20612_id,
                quest_step = M.questStep(quest),
                stage = M.quest_20612_start_stage,
                interact_id = M.quest_20612_start_npc.interact_id,
                npc_name = M.quest_20612_start_npc.name,
                npc_name_key = M.quest_20612_start_npc.name_key,
                x = M.quest_20612_start_npc.x,
                y = M.quest_20612_start_npc.y,
                z = M.quest_20612_start_npc.z,
                distance = npc_dist,
                range = npc_range,
                mark_20612_start_point_reached = true,
            })
        end
    end

    return action("InteractNpc", "open quest 20612 start npc dialog", {
        quest_id = M.quest_20612_id,
        quest_step = M.questStep(quest),
        stage = M.quest_20612_start_stage,
        interact_id = M.quest_20612_start_npc.interact_id,
        npc_name = M.quest_20612_start_npc.name,
        npc_name_key = M.quest_20612_start_npc.name_key,
        allow_interact_id_fallback = true,
        mark_20612_start_point_reached = true,
    })
end

function M.nextQuest20612RewardAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20612_id)

    if runtime.completed_20612_reward_dialog == true then
        return action("Idle", "quest 20612 reward npc already completed", {
            quest_id = M.quest_20612_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20612_reward_stage,
        })
    end

    local dialog = state.dialog
    if M.isQuest20612RewardNpcDialog(dialog) then
        local type_text = tostring(dialog.type_text or "")
        local step = M.quest_20612_reward_dialog_steps[type_text]
        if step then
            return action(step.action, step.reason, {
                quest_id = M.quest_20612_id,
                quest_step = M.questStep(quest),
                expected_content_id = step.content_id,
                content_id = number(dialog.dialog_content_id),
                type_text = type_text,
                click_x = opts.dialog_click_x or 25,
                click_y = step.click_y,
                click_y_tolerance = step.click_y_tolerance,
                max_steps = step.max_steps,
                delay_ms = step.delay_ms,
                interact_id = M.quest_20612_reward_npc.interact_id,
                npc_name = M.quest_20612_reward_npc.name,
                npc_name_key = M.quest_20612_reward_npc.name_key,
                stage = M.quest_20612_reward_stage,
            })
        end

        return action("DumpDialog", "unknown quest 20612 reward npc dialog stage", {
            quest_id = M.quest_20612_id,
            quest_step = M.questStep(quest),
            type_text = type_text,
            content_id = number(dialog.dialog_content_id),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20612_reward_npc.interact_id,
            npc_name = M.quest_20612_reward_npc.name,
            npc_name_key = M.quest_20612_reward_npc.name_key,
            stage = M.quest_20612_reward_stage,
        })
    end

    if type(dialog) == "table" then
        return action("DumpDialog", "different npc dialog is already open before quest 20612 reward npc", {
            quest_id = M.quest_20612_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = number(dialog.dialog_content_id),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20612_reward_npc.interact_id,
            npc_name = M.quest_20612_reward_npc.name,
            npc_name_key = M.quest_20612_reward_npc.name_key,
            stage = M.quest_20612_reward_stage,
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_20612_id })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return action("Idle", "quest 20612 reward npc wrong map", {
            quest_id = M.quest_20612_id,
            quest_step = M.questStep(quest),
            big_map_id = current_big_map,
            expected_big_map_id = M.big_map_id,
            stage = M.quest_20612_reward_stage,
        })
    end

    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    local dist = M.distanceToQuest20612RewardNpc(char)
    if dist > range then
        return action("NavigateToNpc", "move to quest 20612 reward npc", {
            quest_id = M.quest_20612_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20612_reward_stage,
            interact_id = M.quest_20612_reward_npc.interact_id,
            npc_name = M.quest_20612_reward_npc.name,
            npc_name_key = M.quest_20612_reward_npc.name_key,
            x = M.quest_20612_reward_npc.x,
            y = M.quest_20612_reward_npc.y,
            z = M.quest_20612_reward_npc.z,
            distance = dist,
            range = range,
        })
    end

    return action("InteractNpc", "open quest 20612 reward npc dialog", {
        quest_id = M.quest_20612_id,
        quest_step = M.questStep(quest),
        stage = M.quest_20612_reward_stage,
        interact_id = M.quest_20612_reward_npc.interact_id,
        npc_name = M.quest_20612_reward_npc.name,
        npc_name_key = M.quest_20612_reward_npc.name_key,
        allow_interact_id_fallback = true,
    })
end

function M.nextQuest20612TaskTeleportAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20612_id)
    local teleport_quest = quest
    if not M.isQuestActive(teleport_quest) then
        local level_quest = state.level_blocked_quest or M.findLevelBlockedQuest(state.quests)
        if M.isQuestLevelBlocked(level_quest)
            and quest_id(level_quest) > M.quest_20612_id then
            teleport_quest = level_quest
        end
    end
    local teleport_qid = quest_id(teleport_quest)
    if teleport_qid <= 0 then
        teleport_qid = M.quest_20612_id
    end

    if runtime.completed_20612_task_teleport == true then
        return action("Idle", "quest 20612 task teleport already completed", {
            quest_id = teleport_qid,
            quest_step = M.questStep(teleport_quest),
            stage = M.quest_20612_teleport_stage,
        })
    end

    return M.nextCurrentQuestTeleportAction(state, runtime, teleport_quest, "post quest 20612 current tracker task teleport", {
        quest_id = teleport_qid,
        quest_step = M.questStep(teleport_quest),
        after_quest_id = M.quest_20612_id,
        stage = M.quest_20612_teleport_stage,
        wait_teleport = true,
    })
end

function M.nextQuest20612LevelGateAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20612_id)

    if type(state.char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_20612_id })
    end

    local required_level = M.quest20612RequiredLevel(quest)
    local char_level = number(state.char and state.char.level)
    if char_level <= 0 then
        return action("ReadState", "character level unavailable", { quest_id = M.quest_20612_id })
    end

    if char_level < required_level then
        local active_stage = tostring(runtime.active_20611_grind_stage or "")
        if runtime.active_20611_grind == true
            and active_stage == M.quest_20612_level_grind_stage
            and number(runtime.level_grind_quest_id) == M.quest_20612_id then
            return action("WaitLevelGrind", "quest 20612 level grind running", {
                quest_id = M.quest_20612_id,
                quest_step = M.questStep(quest),
                required_level = required_level,
                char_level = char_level,
                stage = M.quest_20612_level_grind_stage,
            })
        end

        local anchor = anchor_from_char(state.char)
        return action("StartStationaryGrind", "start quest 20612 level grind", {
            quest_id = M.quest_20612_id,
            quest_step = M.questStep(quest),
            required_level = required_level,
            char_level = char_level,
            until_level = required_level,
            stage = M.quest_20612_level_grind_stage,
            x = anchor.x,
            y = anchor.y,
            z = anchor.z,
        })
    end

    return M.nextQuest20612StartAction(state, runtime, opts, quest)
end

function M.nextQuest20613TaskTeleportAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20613_id)

    if runtime.completed_20613_task_teleport == true then
        return action("Idle", "quest 20613 task teleport completed; wait next instruction", {
            quest_id = M.quest_20613_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20613_teleport_stage,
        })
    end

    if type(state.dialog) == "table" then
        return action("Idle", "quest 20613 task teleport waits for dialog close", {
            quest_id = M.quest_20613_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20613_teleport_stage,
        })
    end

    return M.nextCurrentQuestTeleportAction(state, runtime, quest, "quest 20613 current tracker task teleport", {
        quest_id = M.quest_20613_id,
        quest_step = M.questStep(quest),
        stage = M.quest_20613_teleport_stage,
        wait_teleport = true,
    })
end

function M.nextQuest20614TaskTeleportAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20614_id)

    if M.isQuest20614StartNpcDialog(state.dialog)
        or runtime.completed_20614_task_teleport == true
        or M.isNearQuest20614StartNpc(state, opts) then
        return M.nextQuest20614StartAction(state, runtime, opts, quest)
    end

    if type(state.dialog) == "table" then
        return action("Idle", "quest 20614 task teleport waits for dialog close", {
            quest_id = M.quest_20614_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20614_teleport_stage,
        })
    end

    return M.nextCurrentQuestTeleportAction(state, runtime, quest, "quest 20614 current tracker task teleport", {
        quest_id = M.quest_20614_id,
        quest_step = M.questStep(quest),
        stage = M.quest_20614_teleport_stage,
        wait_teleport = true,
    })
end

function M.nextQuest20615TaskTeleportAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20615_id)

    if runtime.completed_20615_task_teleport == true then
        return M.nextQuest20615TargetNpcAction(state, runtime, opts, quest)
    end

    if type(state.dialog) == "table" then
        return action("Idle", "quest 20615 task teleport waits for dialog close", {
            quest_id = M.quest_20615_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20615_teleport_stage,
        })
    end

    return M.nextCurrentQuestTeleportAction(state, runtime, quest, "quest 20615 current tracker task teleport", {
        quest_id = M.quest_20615_id,
        quest_step = M.questStep(quest),
        stage = M.quest_20615_teleport_stage,
        wait_teleport = true,
    })
end

function M.nextQuest20615TargetNpcAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20615_id)

    if runtime.completed_20615_target_dialog == true then
        return M.nextQuest20615BigMapTeleportAction(state, runtime, opts, quest)
    end

    local dialog = state.dialog
    if M.isQuest20615TargetNpcDialog(dialog) then
        return action("ClickDialogLastContinuousOk", "complete quest 20615 target npc dialog by last-option chain", {
            quest_id = M.quest_20615_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = dialog_content_id(dialog),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20615_target_npc.interact_id,
            npc_name = M.quest_20615_target_npc.name,
            npc_name_key = M.quest_20615_target_npc.name_key,
            click_x = opts.dialog_click_x or 25,
            stage = M.quest_20615_target_stage,
        })
    end

    if type(dialog) == "table" then
        return action("DumpDialog", "different npc dialog is already open before quest 20615 target npc", {
            quest_id = M.quest_20615_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = dialog_content_id(dialog),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20615_target_npc.interact_id,
            npc_name = M.quest_20615_target_npc.name,
            npc_name_key = M.quest_20615_target_npc.name_key,
            stage = M.quest_20615_target_stage,
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_20615_id })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return action("Idle", "quest 20615 target npc wrong map", {
            quest_id = M.quest_20615_id,
            quest_step = M.questStep(quest),
            big_map_id = current_big_map,
            expected_big_map_id = M.big_map_id,
            stage = M.quest_20615_target_stage,
        })
    end

    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    local dist = M.distanceToQuest20615TargetNpc(char)
    if dist > range then
        return action("NavigateToNpc", "move to quest 20615 target npc", {
            quest_id = M.quest_20615_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20615_target_stage,
            interact_id = M.quest_20615_target_npc.interact_id,
            npc_name = M.quest_20615_target_npc.name,
            npc_name_key = M.quest_20615_target_npc.name_key,
            x = M.quest_20615_target_npc.x,
            y = M.quest_20615_target_npc.y,
            z = M.quest_20615_target_npc.z,
            distance = dist,
            range = range,
        })
    end

    return action("InteractNpc", "open quest 20615 target npc dialog", {
        quest_id = M.quest_20615_id,
        quest_step = M.questStep(quest),
        stage = M.quest_20615_target_stage,
        interact_id = M.quest_20615_target_npc.interact_id,
        npc_name = M.quest_20615_target_npc.name,
        npc_name_key = M.quest_20615_target_npc.name_key,
        allow_interact_id_fallback = true,
        after_open_continuous_last = true,
        click_x = opts.dialog_click_x or 25,
    })
end

function M.nextQuest20615BigMapTeleportAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20615_id)

    if runtime.completed_20615_big_map_teleport == true then
        return M.nextQuest20615AfterBigMapTaskTeleportAction(state, runtime, opts, quest)
    end

    if type(state.dialog) == "table" then
        return action("Idle", "quest 20615 big map teleport waits for dialog close", {
            quest_id = M.quest_20615_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20615_big_map_teleport_stage,
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_20615_id })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return action("CompleteBigMapTeleport", "quest 20615 already left alder big map", {
            quest_id = M.quest_20615_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20615_big_map_teleport_stage,
            slot = M.quest_20615_big_map_teleport.slot,
            price = M.quest_20615_big_map_teleport.price,
            start_big_map_id = M.big_map_id,
            big_map_id = current_big_map,
            expected_big_map_id = M.quest_20615_big_map_teleport.expected_big_map_id,
        })
    end

    return action("BigMapTeleport", "quest 20615 teleport to Morheim big map", {
        quest_id = M.quest_20615_id,
        quest_step = M.questStep(quest),
        stage = M.quest_20615_big_map_teleport_stage,
        slot = M.quest_20615_big_map_teleport.slot,
        price = M.quest_20615_big_map_teleport.price,
        min_lv = M.quest_20615_big_map_teleport.min_lv,
        target_name = M.quest_20615_big_map_teleport.name,
        expected_big_map_id = M.quest_20615_big_map_teleport.expected_big_map_id,
        wait_teleport = true,
    })
end

function M.nextQuest20615AfterBigMapTaskTeleportAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20615_id)

    if runtime.completed_20615_after_big_map_task_teleport == true then
        return M.nextQuest20615MorheimNpcAction(state, runtime, opts, quest)
    end

    if runtime.completed_20615_morheim_npc_dialog == true
        or M.isQuest20615MorheimNpcDialog(state.dialog)
        or M.isNearQuest20615MorheimNpc(state, opts) then
        return M.nextQuest20615MorheimNpcAction(state, runtime, opts, quest)
    end

    if type(state.dialog) == "table" then
        return action("Idle", "quest 20615 after big map task teleport waits for dialog close", {
            quest_id = M.quest_20615_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20615_after_big_map_teleport_stage,
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_20615_id })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map == M.big_map_id
        and runtime.completed_20615_big_map_teleport ~= true then
        return M.nextQuest20615BigMapTeleportAction(state, runtime, opts, quest)
    end

    return action("QuestTeleport", "quest 20615 direct task teleport after big map landing", {
        quest_id = M.quest_20615_id,
        quest_step = M.questStep(quest),
        stage = M.quest_20615_after_big_map_teleport_stage,
        wait_teleport = true,
        direct_quest_id_only = true,
        open_panel_key = false,
        require_panel_visible = false,
    })
end

function M.nextQuest20615MorheimNpcAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20615_id)

    if runtime.completed_20615_morheim_npc_dialog == true then
        return action("Idle", "quest 20615 Morheim npc dialog completed; wait next instruction", {
            quest_id = M.quest_20615_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20615_morheim_npc_stage,
        })
    end

    local dialog = state.dialog
    if M.isQuest20615MorheimNpcDialog(dialog) then
        return action("ClickDialogLastContinuousOk", "complete quest 20615 Morheim npc dialog by last-option chain", {
            quest_id = M.quest_20615_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = dialog_content_id(dialog),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20615_morheim_npc.interact_id,
            npc_name = M.quest_20615_morheim_npc.name,
            npc_name_key = M.quest_20615_morheim_npc.name_key,
            click_x = opts.dialog_click_x or 25,
            stage = M.quest_20615_morheim_npc_stage,
        })
    end

    if type(dialog) == "table" then
        return action("DumpDialog", "different npc dialog is already open before quest 20615 Morheim npc", {
            quest_id = M.quest_20615_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = dialog_content_id(dialog),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20615_morheim_npc.interact_id,
            npc_name = M.quest_20615_morheim_npc.name,
            npc_name_key = M.quest_20615_morheim_npc.name_key,
            stage = M.quest_20615_morheim_npc_stage,
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_20615_id })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.quest_20615_morheim_npc.big_map_id then
        return action("Idle", "quest 20615 Morheim npc wrong map", {
            quest_id = M.quest_20615_id,
            quest_step = M.questStep(quest),
            big_map_id = current_big_map,
            expected_big_map_id = M.quest_20615_morheim_npc.big_map_id,
            stage = M.quest_20615_morheim_npc_stage,
        })
    end

    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    local dist = M.distanceToQuest20615MorheimNpc(char)
    if dist > range then
        return action("NavigateToNpc", "move to quest 20615 Morheim npc", {
            quest_id = M.quest_20615_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20615_morheim_npc_stage,
            interact_id = M.quest_20615_morheim_npc.interact_id,
            npc_name = M.quest_20615_morheim_npc.name,
            npc_name_key = M.quest_20615_morheim_npc.name_key,
            x = M.quest_20615_morheim_npc.x,
            y = M.quest_20615_morheim_npc.y,
            z = M.quest_20615_morheim_npc.z,
            distance = dist,
            range = range,
        })
    end

    return action("InteractNpc", "open quest 20615 Morheim npc dialog", {
        quest_id = M.quest_20615_id,
        quest_step = M.questStep(quest),
        stage = M.quest_20615_morheim_npc_stage,
        interact_id = M.quest_20615_morheim_npc.interact_id,
        npc_name = M.quest_20615_morheim_npc.name,
        npc_name_key = M.quest_20615_morheim_npc.name_key,
        allow_interact_id_fallback = true,
        after_open_continuous_last = true,
        click_x = opts.dialog_click_x or 25,
    })
end

function M.nextQuest20620StartNpcAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20620_id)

    if runtime.completed_20620_start_dialog == true then
        return action("Idle", "quest 20620 start npc dialog completed; wait next instruction", {
            quest_id = M.quest_20620_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20620_start_stage,
        })
    end

    local dialog = state.dialog
    if M.isQuest20620StartNpcDialog(dialog) then
        return action("ClickDialogLastContinuousOk", "complete quest 20620 start npc dialog by last-option chain", {
            quest_id = M.quest_20620_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = dialog_content_id(dialog),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20620_start_npc.interact_id,
            npc_name = M.quest_20620_start_npc.name,
            npc_name_key = M.quest_20620_start_npc.name_key,
            click_x = opts.dialog_click_x or 25,
            stage = M.quest_20620_start_stage,
        })
    end

    if type(dialog) == "table" then
        return action("DumpDialog", "different npc dialog is already open before quest 20620 start npc", {
            quest_id = M.quest_20620_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = dialog_content_id(dialog),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20620_start_npc.interact_id,
            npc_name = M.quest_20620_start_npc.name,
            npc_name_key = M.quest_20620_start_npc.name_key,
            stage = M.quest_20620_start_stage,
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_20620_id })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.quest_20620_start_npc.big_map_id then
        return action("Idle", "quest 20620 start npc wrong map", {
            quest_id = M.quest_20620_id,
            quest_step = M.questStep(quest),
            big_map_id = current_big_map,
            expected_big_map_id = M.quest_20620_start_npc.big_map_id,
            stage = M.quest_20620_start_stage,
        })
    end

    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    local dist = M.distanceToQuest20620StartNpc(char)
    if dist > range then
        return action("NavigateToNpc", "move to quest 20620 start npc", {
            quest_id = M.quest_20620_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20620_start_stage,
            interact_id = M.quest_20620_start_npc.interact_id,
            npc_name = M.quest_20620_start_npc.name,
            npc_name_key = M.quest_20620_start_npc.name_key,
            x = M.quest_20620_start_npc.x,
            y = M.quest_20620_start_npc.y,
            z = M.quest_20620_start_npc.z,
            distance = dist,
            range = range,
        })
    end

    return action("InteractNpc", "open quest 20620 start npc dialog", {
        quest_id = M.quest_20620_id,
        quest_step = M.questStep(quest),
        stage = M.quest_20620_start_stage,
        interact_id = M.quest_20620_start_npc.interact_id,
        npc_name = M.quest_20620_start_npc.name,
        npc_name_key = M.quest_20620_start_npc.name_key,
        allow_interact_id_fallback = true,
        after_open_continuous_last = true,
        click_x = opts.dialog_click_x or 25,
    })
end

function M.nextQuest20620TaskTeleportAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20620_id)

    if runtime.completed_20620_after_obelisk_npc_dialog == true then
        return M.nextQuest20620AfterObeliskNpcAction(state, runtime, opts, quest)
    end

    if runtime.completed_20620_after_obelisk_teleport == true then
        return M.nextQuest20620AfterObeliskTeleportAction(state, runtime, opts, quest)
    end

    if M.isQuest20620AfterObeliskTeleportSnapshot(quest) then
        return M.nextQuest20620AfterObeliskTeleportAction(state, runtime, opts, quest)
    end

    if runtime.completed_20620_obelisk == true then
        return M.nextQuest20620ObeliskAction(state, runtime, opts, quest)
    end

    if runtime.completed_20620_after_stigma_npc_dialog == true then
        return M.nextQuest20620AfterStigmaNpcAction(state, runtime, opts, quest)
    end

    if runtime.completed_20620_after_stigma_teleport == true then
        return M.nextQuest20620AfterStigmaTeleportAction(state, runtime, opts, quest)
    end

    if runtime.completed_20620_after_teleport_npc_dialog == true
        or runtime.completed_20620_stigma_socket == true then
        return M.nextQuest20620SocketStigmaAction(state, runtime, opts, quest)
    end

    if runtime.completed_20620_task_teleport == true then
        return M.nextQuest20620AfterTeleportNpcAction(state, runtime, opts, quest)
    end

    if M.isQuest20620AfterTeleportNpcDialog(state.dialog)
        or M.isNearQuest20620AfterTeleportNpc(state, opts) then
        return M.nextQuest20620AfterTeleportNpcAction(state, runtime, opts, quest)
    end

    if type(state.dialog) == "table" then
        return action("Idle", "quest 20620 task teleport waits for dialog close", {
            quest_id = M.quest_20620_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20620_teleport_stage,
        })
    end

    return action("QuestTeleport", "quest 20620 direct task teleport after start dialog", {
        quest_id = M.quest_20620_id,
        quest_step = M.questStep(quest),
        stage = M.quest_20620_teleport_stage,
        wait_teleport = true,
        direct_quest_id_only = true,
        open_panel_key = false,
        require_panel_visible = false,
    })
end

function M.nextQuest20620AfterTeleportNpcAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20620_id)

    if runtime.completed_20620_after_teleport_npc_dialog == true then
        return M.nextQuest20620SocketStigmaAction(state, runtime, opts, quest)
    end

    local dialog = state.dialog
    if M.isQuest20620AfterTeleportNpcDialog(dialog) then
        return action("ClickDialogLastContinuousOk", "complete quest 20620 after-teleport npc dialog by last-option chain", {
            quest_id = M.quest_20620_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = dialog_content_id(dialog),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20620_after_teleport_npc.interact_id,
            npc_name = M.quest_20620_after_teleport_npc.name,
            npc_name_key = M.quest_20620_after_teleport_npc.name_key,
            click_x = opts.dialog_click_x or 25,
            stage = M.quest_20620_after_teleport_stage,
        })
    end

    if type(dialog) == "table" then
        return action("DumpDialog", "different npc dialog is already open before quest 20620 after-teleport npc", {
            quest_id = M.quest_20620_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = dialog_content_id(dialog),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20620_after_teleport_npc.interact_id,
            npc_name = M.quest_20620_after_teleport_npc.name,
            npc_name_key = M.quest_20620_after_teleport_npc.name_key,
            stage = M.quest_20620_after_teleport_stage,
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_20620_id })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.quest_20620_after_teleport_npc.big_map_id then
        return action("Idle", "quest 20620 after-teleport npc wrong map", {
            quest_id = M.quest_20620_id,
            quest_step = M.questStep(quest),
            big_map_id = current_big_map,
            expected_big_map_id = M.quest_20620_after_teleport_npc.big_map_id,
            stage = M.quest_20620_after_teleport_stage,
        })
    end

    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    local dist = M.distanceToQuest20620AfterTeleportNpc(char)
    if dist > range then
        return action("NavigateToNpc", "move to quest 20620 after-teleport npc", {
            quest_id = M.quest_20620_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20620_after_teleport_stage,
            interact_id = M.quest_20620_after_teleport_npc.interact_id,
            npc_name = M.quest_20620_after_teleport_npc.name,
            npc_name_key = M.quest_20620_after_teleport_npc.name_key,
            x = M.quest_20620_after_teleport_npc.x,
            y = M.quest_20620_after_teleport_npc.y,
            z = M.quest_20620_after_teleport_npc.z,
            distance = dist,
            range = range,
        })
    end

    return action("InteractNpc", "open quest 20620 after-teleport npc dialog", {
        quest_id = M.quest_20620_id,
        quest_step = M.questStep(quest),
        stage = M.quest_20620_after_teleport_stage,
        interact_id = M.quest_20620_after_teleport_npc.interact_id,
        npc_name = M.quest_20620_after_teleport_npc.name,
        npc_name_key = M.quest_20620_after_teleport_npc.name_key,
        allow_interact_id_fallback = true,
        after_open_continuous_last = true,
        click_x = opts.dialog_click_x or 25,
    })
end

function M.nextQuest20620SocketStigmaAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20620_id)

    if runtime.completed_20620_after_obelisk_npc_dialog == true then
        return M.nextQuest20620AfterObeliskNpcAction(state, runtime, opts, quest)
    end

    if runtime.completed_20620_after_obelisk_teleport == true then
        return M.nextQuest20620AfterObeliskTeleportAction(state, runtime, opts, quest)
    end

    if M.isQuest20620AfterObeliskTeleportSnapshot(quest) then
        return M.nextQuest20620AfterObeliskTeleportAction(state, runtime, opts, quest)
    end

    if runtime.completed_20620_obelisk == true then
        return M.nextQuest20620ObeliskAction(state, runtime, opts, quest)
    end

    if runtime.completed_20620_after_stigma_npc_dialog == true then
        return M.nextQuest20620AfterStigmaNpcAction(state, runtime, opts, quest)
    end

    if runtime.completed_20620_after_stigma_teleport == true then
        return M.nextQuest20620AfterStigmaTeleportAction(state, runtime, opts, quest)
    end

    if runtime.completed_20620_stigma_socket == true then
        return M.nextQuest20620AfterStigmaTeleportAction(state, runtime, opts, quest)
    end

    return action("UseQuestStigmaStone", "socket quest 20620 stigma stone by background UseItem", {
        quest_id = M.quest_20620_id,
        quest_step = M.questStep(quest),
        stage = M.quest_20620_socket_stigma_stage,
        item_keywords = M.quest_20620_stigma_keywords,
        prefer_keyword = "파멸의 방패",
    })
end

function M.nextQuest20620AfterStigmaTeleportAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20620_id)

    if runtime.completed_20620_after_obelisk_npc_dialog == true then
        return M.nextQuest20620AfterObeliskNpcAction(state, runtime, opts, quest)
    end

    if runtime.completed_20620_after_obelisk_teleport == true then
        return M.nextQuest20620AfterObeliskTeleportAction(state, runtime, opts, quest)
    end

    if M.isQuest20620AfterObeliskTeleportSnapshot(quest) then
        return M.nextQuest20620AfterObeliskTeleportAction(state, runtime, opts, quest)
    end

    if runtime.completed_20620_obelisk == true then
        return M.nextQuest20620ObeliskAction(state, runtime, opts, quest)
    end

    if runtime.completed_20620_after_stigma_npc_dialog == true then
        return M.nextQuest20620AfterStigmaNpcAction(state, runtime, opts, quest)
    end

    if runtime.completed_20620_after_stigma_teleport == true then
        return M.nextQuest20620AfterStigmaNpcAction(state, runtime, opts, quest)
    end

    if M.isQuest20620AfterStigmaNpcDialog(state.dialog)
        or M.isNearQuest20620AfterStigmaNpc(state, opts) then
        return M.nextQuest20620AfterStigmaNpcAction(state, runtime, opts, quest)
    end

    if type(state.dialog) == "table" then
        return action("Idle", "quest 20620 after-stigma teleport waits for dialog close", {
            quest_id = M.quest_20620_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20620_after_stigma_teleport_stage,
        })
    end

    return action("QuestTeleport", "quest 20620 direct task teleport after stigma socket", {
        quest_id = M.quest_20620_id,
        quest_step = M.questStep(quest),
        stage = M.quest_20620_after_stigma_teleport_stage,
        wait_teleport = true,
        direct_quest_id_only = true,
        open_panel_key = false,
        require_panel_visible = false,
    })
end

function M.nextQuest20620AfterStigmaNpcAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20620_id)

    if runtime.completed_20620_after_obelisk_npc_dialog == true then
        return M.nextQuest20620AfterObeliskNpcAction(state, runtime, opts, quest)
    end

    if runtime.completed_20620_after_obelisk_teleport == true then
        return M.nextQuest20620AfterObeliskTeleportAction(state, runtime, opts, quest)
    end

    if M.isQuest20620AfterObeliskTeleportSnapshot(quest) then
        return M.nextQuest20620AfterObeliskTeleportAction(state, runtime, opts, quest)
    end

    if runtime.completed_20620_obelisk == true then
        return M.nextQuest20620ObeliskAction(state, runtime, opts, quest)
    end

    if runtime.completed_20620_after_stigma_npc_dialog == true then
        return M.nextQuest20620ObeliskAction(state, runtime, opts, quest)
    end

    local dialog = state.dialog
    if M.isQuest20620AfterStigmaNpcDialog(dialog) then
        return action("ClickDialogLastContinuousOk", "complete quest 20620 after-stigma npc dialog by last-option chain", {
            quest_id = M.quest_20620_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = dialog_content_id(dialog),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20620_after_stigma_npc.interact_id,
            npc_name = M.quest_20620_after_stigma_npc.name,
            npc_name_key = M.quest_20620_after_stigma_npc.name_key,
            click_x = opts.dialog_click_x or 25,
            stage = M.quest_20620_after_stigma_npc_stage,
        })
    end

    if type(dialog) == "table" then
        return action("DumpDialog", "different npc dialog is already open before quest 20620 after-stigma npc", {
            quest_id = M.quest_20620_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = dialog_content_id(dialog),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20620_after_stigma_npc.interact_id,
            npc_name = M.quest_20620_after_stigma_npc.name,
            npc_name_key = M.quest_20620_after_stigma_npc.name_key,
            stage = M.quest_20620_after_stigma_npc_stage,
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_20620_id })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.quest_20620_after_stigma_npc.big_map_id then
        return action("Idle", "quest 20620 after-stigma npc wrong map", {
            quest_id = M.quest_20620_id,
            quest_step = M.questStep(quest),
            big_map_id = current_big_map,
            expected_big_map_id = M.quest_20620_after_stigma_npc.big_map_id,
            stage = M.quest_20620_after_stigma_npc_stage,
        })
    end

    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    local dist = M.distanceToQuest20620AfterStigmaNpc(char)
    if dist > range then
        return action("NavigateToNpc", "move to quest 20620 after-stigma npc", {
            quest_id = M.quest_20620_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20620_after_stigma_npc_stage,
            interact_id = M.quest_20620_after_stigma_npc.interact_id,
            npc_name = M.quest_20620_after_stigma_npc.name,
            npc_name_key = M.quest_20620_after_stigma_npc.name_key,
            x = M.quest_20620_after_stigma_npc.x,
            y = M.quest_20620_after_stigma_npc.y,
            z = M.quest_20620_after_stigma_npc.z,
            distance = dist,
            range = range,
        })
    end

    return action("InteractNpc", "open quest 20620 after-stigma npc dialog", {
        quest_id = M.quest_20620_id,
        quest_step = M.questStep(quest),
        stage = M.quest_20620_after_stigma_npc_stage,
        interact_id = M.quest_20620_after_stigma_npc.interact_id,
        npc_name = M.quest_20620_after_stigma_npc.name,
        npc_name_key = M.quest_20620_after_stigma_npc.name_key,
        allow_interact_id_fallback = true,
        after_open_continuous_last = true,
        click_x = opts.dialog_click_x or 25,
    })
end

function M.nextQuest20620ObeliskAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20620_id)

    if runtime.completed_20620_after_obelisk_npc_dialog == true then
        return M.nextQuest20620AfterObeliskNpcAction(state, runtime, opts, quest)
    end

    if runtime.completed_20620_after_obelisk_teleport == true then
        return M.nextQuest20620AfterObeliskTeleportAction(state, runtime, opts, quest)
    end

    if runtime.completed_20620_obelisk == true then
        return M.nextQuest20620AfterObeliskTeleportAction(state, runtime, opts, quest)
    end

    if M.isObeliskConfirmVisible(state) or runtime.opened_20620_obelisk == true then
        return action("ClickObeliskConfirm", "confirm quest 20620 obelisk registration", {
            quest_id = M.quest_20620_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20620_obelisk_stage,
            npc_name = M.quest_20620_obelisk.name,
            npc_name_key = M.quest_20620_obelisk.name_key,
            confirm_x = M.obelisk_confirm.x,
            confirm_y = M.obelisk_confirm.y,
            confirm_tolerance = M.obelisk_confirm.tolerance,
        })
    end

    if type(state.dialog) == "table" then
        return action("DumpDialog", "different npc dialog is already open before quest 20620 obelisk confirm", {
            quest_id = M.quest_20620_id,
            quest_step = M.questStep(quest),
            type_text = tostring(state.dialog.type_text or ""),
            content_id = number(state.dialog.dialog_content_id),
            npc_dialog_id = number(state.dialog.npc_dialog_id),
            interact_id = M.quest_20620_obelisk.interact_id,
            npc_name = M.quest_20620_obelisk.name,
            npc_name_key = M.quest_20620_obelisk.name_key,
            stage = M.quest_20620_obelisk_stage,
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_20620_id })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.quest_20620_obelisk.big_map_id then
        return action("Idle", "quest 20620 obelisk wrong map", {
            quest_id = M.quest_20620_id,
            quest_step = M.questStep(quest),
            big_map_id = current_big_map,
            expected_big_map_id = M.quest_20620_obelisk.big_map_id,
            stage = M.quest_20620_obelisk_stage,
        })
    end

    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    local dist = M.distanceToQuest20620Obelisk(char)
    if dist > range then
        return action("NavigateToNpc", "move to quest 20620 obelisk", {
            quest_id = M.quest_20620_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20620_obelisk_stage,
            interact_id = M.quest_20620_obelisk.interact_id,
            npc_name = M.quest_20620_obelisk.name,
            npc_name_key = M.quest_20620_obelisk.name_key,
            x = M.quest_20620_obelisk.x,
            y = M.quest_20620_obelisk.y,
            z = M.quest_20620_obelisk.z,
            distance = dist,
            range = range,
        })
    end

    return action("InteractNpc", "open quest 20620 obelisk confirm popup", {
        quest_id = M.quest_20620_id,
        quest_step = M.questStep(quest),
        stage = M.quest_20620_obelisk_stage,
        interact_id = M.quest_20620_obelisk.interact_id,
        npc_name = M.quest_20620_obelisk.name,
        npc_name_key = M.quest_20620_obelisk.name_key,
        allow_interact_id_fallback = true,
    })
end

function M.nextQuest20620AfterObeliskTeleportAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20620_id)

    if runtime.completed_20620_after_obelisk_npc_dialog == true then
        return M.nextQuest20620AfterObeliskNpcAction(state, runtime, opts, quest)
    end

    if runtime.completed_20620_after_obelisk_teleport == true then
        return M.nextQuest20620AfterObeliskNpcAction(state, runtime, opts, quest)
    end

    if M.isQuest20620AfterObeliskNpcDialog(state.dialog)
        or M.isNearQuest20620AfterObeliskNpc(state, opts) then
        return M.nextQuest20620AfterObeliskNpcAction(state, runtime, opts, quest)
    end

    if type(state.dialog) == "table" then
        return action("Idle", "quest 20620 after-obelisk teleport waits for dialog close", {
            quest_id = M.quest_20620_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20620_after_obelisk_teleport_stage,
        })
    end

    return action("QuestTeleport", "quest 20620 direct task teleport after obelisk binding", {
        quest_id = M.quest_20620_id,
        quest_step = M.questStep(quest),
        stage = M.quest_20620_after_obelisk_teleport_stage,
        wait_teleport = true,
        direct_quest_id_only = true,
        open_panel_key = false,
        require_panel_visible = false,
    })
end

function M.nextQuest20620AfterObeliskNpcAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20620_id)

    if runtime.completed_20620_after_obelisk_npc_dialog == true then
        return action("Idle", "quest 20620 after-obelisk npc dialog completed; wait next instruction", {
            quest_id = M.quest_20620_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20620_after_obelisk_npc_stage,
        })
    end

    local dialog = state.dialog
    if M.isQuest20620AfterObeliskNpcDialog(dialog) then
        return action("ClickDialogLastContinuousOk", "complete quest 20620 after-obelisk npc dialog by last-option chain", {
            quest_id = M.quest_20620_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = dialog_content_id(dialog),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20620_after_obelisk_npc.interact_id,
            npc_name = M.quest_20620_after_obelisk_npc.name,
            npc_name_key = M.quest_20620_after_obelisk_npc.name_key,
            click_x = opts.dialog_click_x or 25,
            stage = M.quest_20620_after_obelisk_npc_stage,
        })
    end

    if type(dialog) == "table" then
        return action("DumpDialog", "different npc dialog is already open before quest 20620 after-obelisk npc", {
            quest_id = M.quest_20620_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = dialog_content_id(dialog),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20620_after_obelisk_npc.interact_id,
            npc_name = M.quest_20620_after_obelisk_npc.name,
            npc_name_key = M.quest_20620_after_obelisk_npc.name_key,
            stage = M.quest_20620_after_obelisk_npc_stage,
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_20620_id })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.quest_20620_after_obelisk_npc.big_map_id then
        return action("Idle", "quest 20620 after-obelisk npc wrong map", {
            quest_id = M.quest_20620_id,
            quest_step = M.questStep(quest),
            big_map_id = current_big_map,
            expected_big_map_id = M.quest_20620_after_obelisk_npc.big_map_id,
            stage = M.quest_20620_after_obelisk_npc_stage,
        })
    end

    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    local dist = M.distanceToQuest20620AfterObeliskNpc(char)
    if dist > range then
        return action("NavigateToNpc", "move to quest 20620 after-obelisk npc", {
            quest_id = M.quest_20620_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20620_after_obelisk_npc_stage,
            interact_id = M.quest_20620_after_obelisk_npc.interact_id,
            npc_name = M.quest_20620_after_obelisk_npc.name,
            npc_name_key = M.quest_20620_after_obelisk_npc.name_key,
            x = M.quest_20620_after_obelisk_npc.x,
            y = M.quest_20620_after_obelisk_npc.y,
            z = M.quest_20620_after_obelisk_npc.z,
            distance = dist,
            range = range,
        })
    end

    return action("InteractNpc", "open quest 20620 after-obelisk npc dialog", {
        quest_id = M.quest_20620_id,
        quest_step = M.questStep(quest),
        stage = M.quest_20620_after_obelisk_npc_stage,
        interact_id = M.quest_20620_after_obelisk_npc.interact_id,
        npc_name = M.quest_20620_after_obelisk_npc.name,
        npc_name_key = M.quest_20620_after_obelisk_npc.name_key,
        allow_interact_id_fallback = true,
        after_open_continuous_last = true,
        click_x = opts.dialog_click_x or 25,
    })
end

function M.nextQuest20614StartAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20614_id)

    if runtime.completed_20614_start_dialog == true then
        return M.nextQuest20614AfterStartTeleportAction(state, runtime, opts, quest)
    end

    local dialog = state.dialog
    if M.isQuest20614StartNpcDialog(dialog) then
        local type_text = tostring(dialog.type_text or "")
        local step = M.quest_20614_start_dialog_steps[type_text]
        if not step and dialog_content_id(dialog) == M.quest_20614_start_dialog_steps.select_quest.content_id then
            step = M.quest_20614_start_dialog_steps.select_quest
            if type_text == "" then
                type_text = "select_quest"
            end
        end
        if step then
            return action(step.action, step.reason, {
                quest_id = M.quest_20614_id,
                quest_step = M.questStep(quest),
                expected_content_id = step.content_id,
                content_id = dialog_content_id(dialog),
                type_text = type_text,
                click_x = opts.dialog_click_x or 25,
                click_y = step.click_y,
                click_y_tolerance = step.click_y_tolerance,
                max_steps = step.max_steps,
                delay_ms = step.delay_ms,
                interact_id = M.quest_20614_start_npc.interact_id,
                npc_name = M.quest_20614_start_npc.name,
                npc_name_key = M.quest_20614_start_npc.name_key,
                stage = M.quest_20614_start_stage,
            })
        end
    end

    if type(dialog) == "table"
        and number(dialog.npc_dialog_id) == M.quest_20614_start_npc.interact_id then
        return action("DumpDialog", "unknown quest 20614 start npc dialog stage", {
            quest_id = M.quest_20614_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = dialog_content_id(dialog),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20614_start_npc.interact_id,
            npc_name = M.quest_20614_start_npc.name,
            npc_name_key = M.quest_20614_start_npc.name_key,
            stage = M.quest_20614_start_stage,
        })
    end

    if type(dialog) == "table" then
        return action("DumpDialog", "different npc dialog is already open before quest 20614 start npc", {
            quest_id = M.quest_20614_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = dialog_content_id(dialog),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20614_start_npc.interact_id,
            npc_name = M.quest_20614_start_npc.name,
            npc_name_key = M.quest_20614_start_npc.name_key,
            stage = M.quest_20614_start_stage,
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_20614_id })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return action("Idle", "quest 20614 start npc wrong map", {
            quest_id = M.quest_20614_id,
            quest_step = M.questStep(quest),
            big_map_id = current_big_map,
            expected_big_map_id = M.big_map_id,
            stage = M.quest_20614_start_stage,
        })
    end

    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    local dist = M.distanceToQuest20614StartNpc(char)
    if dist > range then
        return action("NavigateToNpc", "move to quest 20614 start npc", {
            quest_id = M.quest_20614_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20614_start_stage,
            interact_id = M.quest_20614_start_npc.interact_id,
            npc_name = M.quest_20614_start_npc.name,
            npc_name_key = M.quest_20614_start_npc.name_key,
            x = M.quest_20614_start_npc.x,
            y = M.quest_20614_start_npc.y,
            z = M.quest_20614_start_npc.z,
            distance = dist,
            range = range,
        })
    end

    return action("InteractNpc", "open quest 20614 start npc dialog", {
        quest_id = M.quest_20614_id,
        quest_step = M.questStep(quest),
        stage = M.quest_20614_start_stage,
        interact_id = M.quest_20614_start_npc.interact_id,
        npc_name = M.quest_20614_start_npc.name,
        npc_name_key = M.quest_20614_start_npc.name_key,
        allow_interact_id_fallback = true,
        after_open_continuous_x = true,
        after_open_expected_content_id = M.quest_20614_start_dialog_steps.select_quest.content_id,
        click_x = opts.dialog_click_x or 25,
    })
end

function M.nextQuest20614AfterStartTeleportAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20614_id)

    if M.isQuest20614RewardNpcDialog(state.dialog)
        or runtime.completed_20614_after_start_teleport == true then
        return M.nextQuest20614RewardAction(state, runtime, opts, quest)
    end

    return M.nextCurrentQuestTeleportAction(state, runtime, quest, "quest 20614 after start current tracker task teleport", {
        quest_id = M.quest_20614_id,
        quest_step = M.questStep(quest),
        quest_status = number(quest and quest.status_code),
        quest_req_count = M.questStep(quest),
        stage = M.quest_20614_after_start_teleport_stage,
        source_stage = M.quest_20614_start_stage,
        completed_20614_start_dialog = runtime.completed_20614_start_dialog == true,
        completed_20614_after_start_teleport = runtime.completed_20614_after_start_teleport == true,
        wait_teleport = true,
    })
end

function M.nextQuest20614RewardAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20614_id)
    if type(quest) ~= "table" then
        quest = {
            id = M.quest_20614_id,
            status_code = 4,
            req_count = 0,
        }
    end

    if runtime.completed_20614_reward_dialog == true then
        return action("Idle", "quest 20614 reward npc dialog completed; wait next instruction", {
            quest_id = M.quest_20614_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20614_reward_stage,
        })
    end

    local dialog = state.dialog
    if M.isQuest20614RewardNpcDialog(dialog) then
        local type_text = tostring(dialog.type_text or "")
        local step = M.quest_20614_reward_dialog_steps[type_text]
        if not step and dialog_content_id(dialog) == M.quest_20614_reward_dialog_steps.select_success.content_id then
            step = M.quest_20614_reward_dialog_steps.select_success
            if type_text == "" then
                type_text = "select_success"
            end
        end
        if step then
            return action(step.action, step.reason, {
                quest_id = M.quest_20614_id,
                quest_step = M.questStep(quest),
                expected_content_id = step.content_id,
                content_id = dialog_content_id(dialog),
                type_text = type_text,
                click_x = opts.dialog_click_x or 25,
                click_y = step.click_y,
                click_y_tolerance = step.click_y_tolerance,
                max_steps = step.max_steps,
                delay_ms = step.delay_ms,
                interact_id = M.quest_20614_reward_npc.interact_id,
                npc_name = M.quest_20614_reward_npc.name,
                npc_name_key = M.quest_20614_reward_npc.name_key,
                stage = M.quest_20614_reward_stage,
            })
        end
    end

    if type(dialog) == "table"
        and number(dialog.npc_dialog_id) == M.quest_20614_reward_npc.interact_id then
        return action("DumpDialog", "unknown quest 20614 reward npc dialog stage", {
            quest_id = M.quest_20614_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = dialog_content_id(dialog),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20614_reward_npc.interact_id,
            npc_name = M.quest_20614_reward_npc.name,
            npc_name_key = M.quest_20614_reward_npc.name_key,
            stage = M.quest_20614_reward_stage,
        })
    end

    if type(dialog) == "table" then
        return action("DumpDialog", "different npc dialog is already open before quest 20614 reward npc", {
            quest_id = M.quest_20614_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = dialog_content_id(dialog),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20614_reward_npc.interact_id,
            npc_name = M.quest_20614_reward_npc.name,
            npc_name_key = M.quest_20614_reward_npc.name_key,
            stage = M.quest_20614_reward_stage,
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_20614_id })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return action("Idle", "quest 20614 reward npc wrong map", {
            quest_id = M.quest_20614_id,
            quest_step = M.questStep(quest),
            big_map_id = current_big_map,
            expected_big_map_id = M.big_map_id,
            stage = M.quest_20614_reward_stage,
        })
    end

    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    local dist = M.distanceToQuest20614RewardNpc(char)
    if dist > range then
        return action("NavigateToNpc", "move to quest 20614 reward npc", {
            quest_id = M.quest_20614_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20614_reward_stage,
            interact_id = M.quest_20614_reward_npc.interact_id,
            npc_name = M.quest_20614_reward_npc.name,
            npc_name_key = M.quest_20614_reward_npc.name_key,
            x = M.quest_20614_reward_npc.x,
            y = M.quest_20614_reward_npc.y,
            z = M.quest_20614_reward_npc.z,
            distance = dist,
            range = range,
        })
    end

    return action("InteractNpc", "open quest 20614 reward npc dialog", {
        quest_id = M.quest_20614_id,
        quest_step = M.questStep(quest),
        stage = M.quest_20614_reward_stage,
        interact_id = M.quest_20614_reward_npc.interact_id,
        npc_name = M.quest_20614_reward_npc.name,
        npc_name_key = M.quest_20614_reward_npc.name_key,
        allow_interact_id_fallback = true,
        after_open_continuous_x = true,
        after_open_expected_content_id = M.quest_20614_reward_dialog_steps.select_success.content_id,
        click_x = opts.dialog_click_x or 25,
    })
end

function M.nextQuest20613StartAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20613_id)

    if runtime.completed_20613_start_dialog == true then
        return action("Idle", "quest 20613 start npc dialog completed; wait next instruction", {
            quest_id = M.quest_20613_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20613_start_stage,
        })
    end

    local dialog = state.dialog
    if M.isQuest20613StartNpcDialog(dialog) then
        local type_text = tostring(dialog.type_text or "")
        local step = M.quest_20613_start_dialog_steps[type_text]
        if not step and dialog_content_id(dialog) == M.quest_20613_start_dialog_steps.select_quest.content_id then
            step = M.quest_20613_start_dialog_steps.select_quest
            if type_text == "" then
                type_text = "select_quest"
            end
        end
        if step then
            return action(step.action, step.reason, {
                quest_id = M.quest_20613_id,
                quest_step = M.questStep(quest),
                expected_content_id = step.content_id,
                content_id = dialog_content_id(dialog),
                type_text = type_text,
                click_x = opts.dialog_click_x or 25,
                click_y = step.click_y,
                click_y_tolerance = step.click_y_tolerance,
                max_steps = step.max_steps,
                delay_ms = step.delay_ms,
                interact_id = M.quest_20613_start_npc.interact_id,
                npc_name = M.quest_20613_start_npc.name,
                npc_name_key = M.quest_20613_start_npc.name_key,
                stage = M.quest_20613_start_stage,
            })
        end
    end

    if type(dialog) == "table"
        and number(dialog.npc_dialog_id) == M.quest_20613_start_npc.interact_id then
        return action("DumpDialog", "unknown quest 20613 start npc dialog stage", {
            quest_id = M.quest_20613_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = dialog_content_id(dialog),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20613_start_npc.interact_id,
            npc_name = M.quest_20613_start_npc.name,
            npc_name_key = M.quest_20613_start_npc.name_key,
            stage = M.quest_20613_start_stage,
        })
    end

    if type(dialog) == "table" then
        return action("DumpDialog", "different npc dialog is already open before quest 20613 start npc", {
            quest_id = M.quest_20613_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = dialog_content_id(dialog),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20613_start_npc.interact_id,
            npc_name = M.quest_20613_start_npc.name,
            npc_name_key = M.quest_20613_start_npc.name_key,
            stage = M.quest_20613_start_stage,
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_20613_id })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return action("Idle", "quest 20613 start npc wrong map", {
            quest_id = M.quest_20613_id,
            quest_step = M.questStep(quest),
            big_map_id = current_big_map,
            expected_big_map_id = M.big_map_id,
            stage = M.quest_20613_start_stage,
        })
    end

    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    local dist = M.distanceToQuest20613StartNpc(char)
    if dist > range then
        return action("NavigateToNpc", "move to quest 20613 start npc", {
            quest_id = M.quest_20613_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20613_start_stage,
            interact_id = M.quest_20613_start_npc.interact_id,
            npc_name = M.quest_20613_start_npc.name,
            npc_name_key = M.quest_20613_start_npc.name_key,
            x = M.quest_20613_start_npc.x,
            y = M.quest_20613_start_npc.y,
            z = M.quest_20613_start_npc.z,
            distance = dist,
            range = range,
        })
    end

    return action("InteractNpc", "open quest 20613 start npc dialog", {
        quest_id = M.quest_20613_id,
        quest_step = M.questStep(quest),
        stage = M.quest_20613_start_stage,
        interact_id = M.quest_20613_start_npc.interact_id,
        npc_name = M.quest_20613_start_npc.name,
        npc_name_key = M.quest_20613_start_npc.name_key,
        allow_interact_id_fallback = true,
        after_open_continuous_x = true,
        after_open_expected_content_id = M.quest_20613_start_dialog_steps.select_quest.content_id,
        click_x = opts.dialog_click_x or 25,
    })
end

function M.nextQuest20613AfterStartRewardAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20613_id)
    if type(quest) ~= "table" then
        quest = {
            id = M.quest_20613_id,
            status_code = 4,
            req_count = 0,
        }
    end

    if runtime.completed_20613_after_start_reward_dialog == true then
        return action("Idle", "quest 20613 after-start reward npc dialog completed; wait next instruction", {
            quest_id = M.quest_20613_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20613_after_start_reward_stage,
        })
    end

    local dialog = state.dialog
    if M.isQuest20613AfterStartRewardNpcDialog(dialog) then
        local type_text = tostring(dialog.type_text or "")
        local step = M.quest_20613_after_start_reward_dialog_steps[type_text]
        if not step and dialog_content_id(dialog) == M.quest_20613_after_start_reward_dialog_steps.select_success.content_id then
            step = M.quest_20613_after_start_reward_dialog_steps.select_success
            if type_text == "" then
                type_text = "select_success"
            end
        end
        if step then
            return action(step.action, step.reason, {
                quest_id = M.quest_20613_id,
                quest_step = M.questStep(quest),
                expected_content_id = step.content_id,
                content_id = dialog_content_id(dialog),
                type_text = type_text,
                click_x = opts.dialog_click_x or 25,
                click_y = step.click_y,
                click_y_tolerance = step.click_y_tolerance,
                max_steps = step.max_steps,
                delay_ms = step.delay_ms,
                interact_id = M.quest_20613_after_start_reward_npc.interact_id,
                npc_name = M.quest_20613_after_start_reward_npc.name,
                npc_name_key = M.quest_20613_after_start_reward_npc.name_key,
                stage = M.quest_20613_after_start_reward_stage,
            })
        end
    end

    if type(dialog) == "table"
        and number(dialog.npc_dialog_id) == M.quest_20613_after_start_reward_npc.interact_id then
        return action("DumpDialog", "unknown quest 20613 after-start reward npc dialog stage", {
            quest_id = M.quest_20613_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = dialog_content_id(dialog),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20613_after_start_reward_npc.interact_id,
            npc_name = M.quest_20613_after_start_reward_npc.name,
            npc_name_key = M.quest_20613_after_start_reward_npc.name_key,
            stage = M.quest_20613_after_start_reward_stage,
        })
    end

    if type(dialog) == "table" then
        return action("DumpDialog", "different npc dialog is already open before quest 20613 after-start reward npc", {
            quest_id = M.quest_20613_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = dialog_content_id(dialog),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20613_after_start_reward_npc.interact_id,
            npc_name = M.quest_20613_after_start_reward_npc.name,
            npc_name_key = M.quest_20613_after_start_reward_npc.name_key,
            stage = M.quest_20613_after_start_reward_stage,
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_20613_id })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return action("Idle", "quest 20613 after-start reward npc wrong map", {
            quest_id = M.quest_20613_id,
            quest_step = M.questStep(quest),
            big_map_id = current_big_map,
            expected_big_map_id = M.big_map_id,
            stage = M.quest_20613_after_start_reward_stage,
        })
    end

    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    local dist = M.distanceToQuest20613AfterStartRewardNpc(char)
    if dist > range then
        return action("NavigateToNpc", "move to quest 20613 after-start reward npc", {
            quest_id = M.quest_20613_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20613_after_start_reward_stage,
            interact_id = M.quest_20613_after_start_reward_npc.interact_id,
            npc_name = M.quest_20613_after_start_reward_npc.name,
            npc_name_key = M.quest_20613_after_start_reward_npc.name_key,
            x = M.quest_20613_after_start_reward_npc.x,
            y = M.quest_20613_after_start_reward_npc.y,
            z = M.quest_20613_after_start_reward_npc.z,
            distance = dist,
            range = range,
        })
    end

    return action("InteractNpc", "open quest 20613 after-start reward npc dialog", {
        quest_id = M.quest_20613_id,
        quest_step = M.questStep(quest),
        stage = M.quest_20613_after_start_reward_stage,
        interact_id = M.quest_20613_after_start_reward_npc.interact_id,
        npc_name = M.quest_20613_after_start_reward_npc.name,
        npc_name_key = M.quest_20613_after_start_reward_npc.name_key,
        allow_interact_id_fallback = true,
        after_open_continuous_x = true,
        after_open_expected_content_id = M.quest_20613_after_start_reward_dialog_steps.select_success.content_id,
        click_x = opts.dialog_click_x or 25,
    })
end

function M.nextQuest20613AfterStartTeleportAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20613_id)
    if type(quest) ~= "table" then
        quest = {
            id = M.quest_20613_id,
            status_code = 3,
            req_count = 1,
        }
    end

    if M.isQuest20613AfterStartRewardNpcDialog(state.dialog) then
        return M.nextQuest20613AfterStartRewardAction(state, runtime, opts, quest)
    end

    if runtime.completed_20613_after_start_teleport == true then
        return M.nextQuest20613AfterStartRewardAction(state, runtime, opts, quest)
    end

    if type(state.dialog) == "table" then
        return action("Idle", "quest 20613 after-start teleport waits for dialog close", {
            quest_id = M.quest_20613_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20613_after_start_teleport_stage,
        })
    end

    local reason = "quest 20613 current tracker after-start teleport"
    if M.isQuestDone(quest) then
        reason = "quest 20613 done; current tracker after-start teleport"
    elseif M.isQuestActive(quest) and M.questStep(quest) > 0 then
        reason = "quest 20613 progressed; current tracker after-start teleport"
    end

    return M.nextCurrentQuestTeleportAction(state, runtime, quest, reason, {
        quest_id = M.quest_20613_id,
        quest_step = M.questStep(quest),
        stage = M.quest_20613_after_start_teleport_stage,
        wait_teleport = true,
    })
end

function M.nextPostQuest20612Level14GrindAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20613_id)

    if type(state.dialog) == "table" then
        return action("Idle", "waiting quest 20612 reward dialog close before level 14 grind", {
            quest_id = M.quest_20613_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20613_level_grind_stage,
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_20613_id })
    end

    local required_level = number(opts.post_20612_level14_required_level)
    if required_level <= 0 then
        required_level = M.post_20612_level14_required_level
    end
    local char_level = number(char.level)
    if char_level <= 0 then
        return action("ReadState", "character level unavailable", { quest_id = M.quest_20613_id })
    end

    if char_level >= required_level then
        return M.nextQuest20613TaskTeleportAction(state, runtime, opts, quest)
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return action("Idle", "post quest 20612 level 14 grind wrong map", {
            quest_id = M.quest_20613_id,
            quest_step = M.questStep(quest),
            big_map_id = current_big_map,
            expected_big_map_id = M.big_map_id,
            required_level = required_level,
            char_level = char_level,
            stage = M.quest_20613_level_grind_stage,
        })
    end

    local active_stage = tostring(runtime.active_20611_grind_stage or "")
    if runtime.active_20611_grind == true
        and active_stage == M.quest_20613_level_grind_stage
        and number(runtime.level_grind_quest_id) == M.quest_20613_id then
        return action("WaitLevelGrind", "post quest 20612 level 14 grind running", {
            quest_id = M.quest_20613_id,
            quest_step = M.questStep(quest),
            required_level = required_level,
            char_level = char_level,
            stage = M.quest_20613_level_grind_stage,
        })
    end

    local range = number(opts.post_20612_level14_grind_point_range)
    if range <= 0 then
        range = number(opts.grind_point_range)
    end
    if range <= 0 then
        range = 10
    end
    local point = M.post_20612_level14_grind_point
    local dist = M.distanceToPost20612Level14GrindPoint(char)
    if dist > range then
        return action("NavigateToGrindPoint", "move to post quest 20612 level 14 grind point", {
            quest_id = M.quest_20613_id,
            quest_step = M.questStep(quest),
            required_level = required_level,
            char_level = char_level,
            stage = M.quest_20613_level_grind_stage,
            x = point.x,
            y = point.y,
            z = point.z,
            distance = dist,
            range = range,
        })
    end

    return action("StartStationaryGrind", "start post quest 20612 level 14 grind", {
        quest_id = M.quest_20613_id,
        quest_step = M.questStep(quest),
        required_level = required_level,
        char_level = char_level,
        until_level = required_level,
        stage = M.quest_20613_level_grind_stage,
        x = point.x,
        y = point.y,
        z = point.z,
    })
end

function M.nextQuest20614Level17GrindAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20614_id)
    if type(quest) ~= "table" then
        quest = {
            id = M.quest_20614_id,
            status_code = 6,
            req_count = 0,
            lv_num = M.quest_20614_level17_required_level,
        }
    end

    if type(state.dialog) == "table" then
        return action("Idle", "waiting dialog close before quest 20614 level 17 grind", {
            quest_id = M.quest_20614_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20614_level_grind_stage,
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_20614_id })
    end

    local required_level = number(opts.quest_20614_level17_required_level)
    if required_level <= 0 then
        required_level = M.questRequiredLevel(quest)
    end
    if required_level <= 0 then
        required_level = M.quest_20614_level17_required_level
    end

    local char_level = number(char.level)
    if char_level <= 0 then
        return action("ReadState", "character level unavailable", { quest_id = M.quest_20614_id })
    end

    if char_level >= required_level then
        return action("Idle", "quest 20614 level 17 reached; wait next instruction", {
            quest_id = M.quest_20614_id,
            quest_step = M.questStep(quest),
            required_level = required_level,
            char_level = char_level,
            stage = M.quest_20614_level_grind_stage,
        })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return action("Idle", "quest 20614 level 17 grind wrong map", {
            quest_id = M.quest_20614_id,
            quest_step = M.questStep(quest),
            big_map_id = current_big_map,
            expected_big_map_id = M.big_map_id,
            required_level = required_level,
            char_level = char_level,
            stage = M.quest_20614_level_grind_stage,
        })
    end

    local active_stage = tostring(runtime.active_20611_grind_stage or "")
    if runtime.active_20611_grind == true
        and active_stage == M.quest_20614_level_grind_stage
        and number(runtime.level_grind_quest_id) == M.quest_20614_id then
        return action("WaitLevelGrind", "quest 20614 level 17 grind running", {
            quest_id = M.quest_20614_id,
            quest_step = M.questStep(quest),
            required_level = required_level,
            char_level = char_level,
            stage = M.quest_20614_level_grind_stage,
        })
    end

    local route_wait = wait_route_if_active(
        opts,
        M.quest_20614_level_grind_stage,
        M.quest_20614_id,
        quest)
    if route_wait then
        return route_wait
    end

    local point = M.quest_20614_level17_grind_point
    local range = number(opts.quest_20614_level17_grind_point_range)
    if range <= 0 then
        range = number(opts.grind_point_range)
    end
    if range <= 0 then
        range = 4
    end

    local dist = M.distanceToQuest20614Level17GrindPoint(char)
    if dist > range then
        local target, index, nearest_dist = route_target(
            M.quest_20614_level17_route,
            char,
            opts.waypoint_range or 2.0)
        target = target or point
        return action("FollowRoute", "follow quest 20614 level 17 grind route", {
            quest_id = M.quest_20614_id,
            quest_step = M.questStep(quest),
            required_level = required_level,
            char_level = char_level,
            stage = M.quest_20614_level_grind_stage,
            x = target.x,
            y = target.y,
            z = target.z,
            route_name = "main_quest_20614_level17_grind",
            route_points = M.quest_20614_level17_route,
            final_x = point.x,
            final_y = point.y,
            final_z = point.z,
            route_index = index,
            route_count = #M.quest_20614_level17_route,
            nearest_route_distance = nearest_dist,
            main_quest_smooth_route = true,
            waypoint_radius = number(opts.quest_20614_route_waypoint_radius) > 0
                and number(opts.quest_20614_route_waypoint_radius) or 6,
            final_radius = number(opts.quest_20614_route_final_radius) > 0
                and number(opts.quest_20614_route_final_radius) or 2.5,
            resend_interval = number(opts.quest_20614_route_resend_interval) > 0
                and number(opts.quest_20614_route_resend_interval) or 0.5,
            smooth_max_skip = 50,
            distance = dist,
            range = range,
        })
    end

    return action("StartStationaryGrind", "start quest 20614 level 17 grind", {
        quest_id = M.quest_20614_id,
        quest_step = M.questStep(quest),
        required_level = required_level,
        char_level = char_level,
        until_level = required_level,
        stage = M.quest_20614_level_grind_stage,
        x = point.x,
        y = point.y,
        z = point.z,
    })
end

function M.nextQuest20615Level20GrindAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20615_id)
    if type(quest) ~= "table" then
        quest = {
            id = M.quest_20615_id,
            status_code = 6,
            req_count = 0,
            lv_num = M.quest_20615_level20_required_level,
        }
    end

    if type(state.dialog) == "table" then
        return action("Idle", "waiting dialog close before quest 20615 level 20 grind", {
            quest_id = M.quest_20615_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20615_level_grind_stage,
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_20615_id })
    end

    local required_level = number(opts.quest_20615_level20_required_level)
    if required_level <= 0 then
        required_level = M.questRequiredLevel(quest)
    end
    if required_level <= 0 then
        required_level = M.quest_20615_level20_required_level
    end

    local char_level = number(char.level)
    if char_level <= 0 then
        return action("ReadState", "character level unavailable", { quest_id = M.quest_20615_id })
    end

    if char_level >= required_level then
        return action("Idle", "quest 20615 level 20 reached; wait next instruction", {
            quest_id = M.quest_20615_id,
            quest_step = M.questStep(quest),
            required_level = required_level,
            char_level = char_level,
            stage = M.quest_20615_level_grind_stage,
        })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return action("Idle", "quest 20615 level 20 grind wrong map", {
            quest_id = M.quest_20615_id,
            quest_step = M.questStep(quest),
            big_map_id = current_big_map,
            expected_big_map_id = M.big_map_id,
            required_level = required_level,
            char_level = char_level,
            stage = M.quest_20615_level_grind_stage,
        })
    end

    local active_stage = tostring(runtime.active_20611_grind_stage or "")
    if runtime.active_20611_grind == true
        and active_stage == M.quest_20615_level_grind_stage
        and number(runtime.level_grind_quest_id) == M.quest_20615_id then
        return action("WaitLevelGrind", "quest 20615 level 20 grind running", {
            quest_id = M.quest_20615_id,
            quest_step = M.questStep(quest),
            required_level = required_level,
            char_level = char_level,
            stage = M.quest_20615_level_grind_stage,
        })
    end

    local route_wait = wait_route_if_active(
        opts,
        M.quest_20615_level_grind_stage,
        M.quest_20615_id,
        quest)
    if route_wait then
        return route_wait
    end

    local point = M.quest_20615_level20_grind_point
    local range = number(opts.quest_20615_level20_grind_point_range)
    if range <= 0 then
        range = number(opts.grind_point_range)
    end
    if range <= 0 then
        range = 4
    end

    local dist = M.distanceToQuest20615Level20GrindPoint(char)
    if dist > range then
        local target, index, nearest_dist = route_target(
            M.quest_20615_level20_route,
            char,
            opts.waypoint_range or 2.0)
        target = target or point
        return action("FollowRoute", "follow quest 20615 level 20 grind route", {
            quest_id = M.quest_20615_id,
            quest_step = M.questStep(quest),
            required_level = required_level,
            char_level = char_level,
            stage = M.quest_20615_level_grind_stage,
            x = target.x,
            y = target.y,
            z = target.z,
            route_name = "main_quest_20615_level20_grind",
            route_points = M.quest_20615_level20_route,
            final_x = point.x,
            final_y = point.y,
            final_z = point.z,
            route_index = index,
            route_count = #M.quest_20615_level20_route,
            nearest_route_distance = nearest_dist,
            main_quest_smooth_route = true,
            waypoint_radius = number(opts.quest_20615_route_waypoint_radius) > 0
                and number(opts.quest_20615_route_waypoint_radius) or 6,
            final_radius = number(opts.quest_20615_route_final_radius) > 0
                and number(opts.quest_20615_route_final_radius) or 2.5,
            resend_interval = number(opts.quest_20615_route_resend_interval) > 0
                and number(opts.quest_20615_route_resend_interval) or 0.5,
            smooth_max_skip = 50,
            distance = dist,
            range = range,
        })
    end

    return action("StartStationaryGrind", "start quest 20615 level 20 grind", {
        quest_id = M.quest_20615_id,
        quest_step = M.questStep(quest),
        required_level = required_level,
        char_level = char_level,
        until_level = required_level,
        stage = M.quest_20615_level_grind_stage,
        x = point.x,
        y = point.y,
        z = point.z,
    })
end

function M.nextQuest20621Level22GrindAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20621_id)
    if type(quest) ~= "table" then
        quest = {
            id = M.quest_20621_id,
            status_code = 6,
            req_count = 0,
            lv_num = M.quest_20621_level22_required_level,
        }
    end

    if type(state.dialog) == "table" then
        return action("Idle", "waiting dialog close before quest 20621 level 22 grind", {
            quest_id = M.quest_20621_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20621_level_grind_stage,
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_20621_id })
    end

    local required_level = number(opts.quest_20621_level22_required_level)
    if required_level <= 0 then
        required_level = M.questRequiredLevel(quest)
    end
    if required_level <= 0 then
        required_level = M.quest_20621_level22_required_level
    end

    local char_level = number(char.level)
    if char_level <= 0 then
        return action("ReadState", "character level unavailable", { quest_id = M.quest_20621_id })
    end

    if char_level >= required_level then
        return M.nextQuest20621TaskTeleportAction(state, runtime, opts, quest)
    end

    local point = M.quest_20621_level22_grind_point
    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= number(point.big_map_id) then
        return action("Idle", "quest 20621 level 22 grind wrong map", {
            quest_id = M.quest_20621_id,
            quest_step = M.questStep(quest),
            big_map_id = current_big_map,
            expected_big_map_id = number(point.big_map_id),
            required_level = required_level,
            char_level = char_level,
            stage = M.quest_20621_level_grind_stage,
        })
    end

    local active_stage = tostring(runtime.active_20611_grind_stage or "")
    if runtime.active_20611_grind == true
        and active_stage == M.quest_20621_level_grind_stage
        and number(runtime.level_grind_quest_id) == M.quest_20621_id then
        return action("WaitLevelGrind", "quest 20621 level 22 grind running", {
            quest_id = M.quest_20621_id,
            quest_step = M.questStep(quest),
            required_level = required_level,
            char_level = char_level,
            stage = M.quest_20621_level_grind_stage,
        })
    end

    local range = number(opts.quest_20621_level22_grind_point_range)
    if range <= 0 then
        range = number(opts.grind_point_range)
    end
    if range <= 0 then
        range = 4
    end

    local dist = M.distanceToQuest20621Level22GrindPoint(char)
    if dist > range then
        return action("NavigateToGrindPoint", "move to quest 20621 level 22 grind point", {
            quest_id = M.quest_20621_id,
            quest_step = M.questStep(quest),
            required_level = required_level,
            char_level = char_level,
            stage = M.quest_20621_level_grind_stage,
            x = point.x,
            y = point.y,
            z = point.z,
            distance = dist,
            range = range,
        })
    end

    return action("StartStationaryGrind", "start quest 20621 level 22 grind", {
        quest_id = M.quest_20621_id,
        quest_step = M.questStep(quest),
        required_level = required_level,
        char_level = char_level,
        until_level = required_level,
        stage = M.quest_20621_level_grind_stage,
        x = point.x,
        y = point.y,
        z = point.z,
    })
end

function M.nextQuest20622Level25GrindAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20622_id)
    if type(quest) ~= "table" then
        quest = {
            id = M.quest_20622_id,
            status_code = 6,
            req_count = 0,
            lv_num = M.quest_20622_level25_required_level,
        }
    end

    if type(state.dialog) == "table" then
        return action("Idle", "waiting dialog close before quest 20622 level 25 grind", {
            quest_id = M.quest_20622_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20622_level_grind_stage,
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_20622_id })
    end

    local required_level = number(opts.quest_20622_level25_required_level)
    if required_level <= 0 then
        required_level = M.questRequiredLevel(quest)
    end
    if required_level <= 0 then
        required_level = M.quest_20622_level25_required_level
    end

    local char_level = number(char.level)
    if char_level <= 0 then
        return action("ReadState", "character level unavailable", { quest_id = M.quest_20622_id })
    end

    if char_level >= required_level then
        return action("Idle", "quest 20622 level 25 reached; wait next instruction", {
            quest_id = M.quest_20622_id,
            quest_step = M.questStep(quest),
            required_level = required_level,
            char_level = char_level,
            stage = M.quest_20622_level_grind_stage,
        })
    end

    local point = M.quest_20622_level25_grind_point
    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= number(point.big_map_id) then
        return action("Idle", "quest 20622 level 25 grind wrong map", {
            quest_id = M.quest_20622_id,
            quest_step = M.questStep(quest),
            big_map_id = current_big_map,
            expected_big_map_id = number(point.big_map_id),
            required_level = required_level,
            char_level = char_level,
            stage = M.quest_20622_level_grind_stage,
        })
    end

    local active_stage = tostring(runtime.active_20611_grind_stage or "")
    if runtime.active_20611_grind == true
        and active_stage == M.quest_20622_level_grind_stage
        and number(runtime.level_grind_quest_id) == M.quest_20622_id then
        return action("WaitLevelGrind", "quest 20622 level 25 grind running", {
            quest_id = M.quest_20622_id,
            quest_step = M.questStep(quest),
            required_level = required_level,
            char_level = char_level,
            stage = M.quest_20622_level_grind_stage,
        })
    end

    local route_wait = wait_route_if_active(
        opts,
        M.quest_20622_level_grind_stage,
        M.quest_20622_id,
        quest)
    if route_wait then
        return route_wait
    end

    local range = number(opts.quest_20622_level25_grind_point_range)
    if range <= 0 then
        range = number(opts.grind_point_range)
    end
    if range <= 0 then
        range = 4
    end

    local dist = M.distanceToQuest20622Level25GrindPoint(char)
    if dist > range then
        local target, index, nearest_dist = route_target(
            M.quest_20622_level25_route,
            char,
            opts.waypoint_range or 2.0)
        target = target or point
        return action("FollowRoute", "follow quest 20622 level 25 grind route", {
            quest_id = M.quest_20622_id,
            quest_step = M.questStep(quest),
            required_level = required_level,
            char_level = char_level,
            stage = M.quest_20622_level_grind_stage,
            x = target.x,
            y = target.y,
            z = target.z,
            route_name = "main_quest_20622_level25_grind",
            route_points = M.quest_20622_level25_route,
            final_x = point.x,
            final_y = point.y,
            final_z = point.z,
            route_index = index,
            route_count = #M.quest_20622_level25_route,
            nearest_route_distance = nearest_dist,
            main_quest_smooth_route = true,
            waypoint_radius = number(opts.quest_20622_route_waypoint_radius) > 0
                and number(opts.quest_20622_route_waypoint_radius) or 6,
            final_radius = number(opts.quest_20622_route_final_radius) > 0
                and number(opts.quest_20622_route_final_radius) or 2.5,
            resend_interval = number(opts.quest_20622_route_resend_interval) > 0
                and number(opts.quest_20622_route_resend_interval) or 0.5,
            smooth_max_skip = 50,
            distance = dist,
            range = range,
        })
    end

    return action("StartStationaryGrind", "start quest 20622 level 25 grind", {
        quest_id = M.quest_20622_id,
        quest_step = M.questStep(quest),
        required_level = required_level,
        char_level = char_level,
        until_level = required_level,
        stage = M.quest_20622_level_grind_stage,
        x = point.x,
        y = point.y,
        z = point.z,
    })
end

function M.nextQuest20621TaskTeleportAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20621_id)

    if runtime.completed_20621_after_teleport_npc_dialog == true
        or M.isQuest20621AfterTeleportNpcDialog(state.dialog)
        or M.isNearQuest20621AfterTeleportNpc(state, opts) then
        return M.nextQuest20621AfterTeleportNpcAction(state, runtime, opts, quest)
    end

    if type(state.dialog) == "table" then
        return action("Idle", "quest 20621 task teleport waits for dialog close", {
            quest_id = M.quest_20621_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20621_teleport_stage,
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_20621_id })
    end

    local required_level = number(opts.quest_20621_level22_required_level)
    if required_level <= 0 then
        required_level = M.questRequiredLevel(quest)
    end
    if required_level <= 0 then
        required_level = M.quest_20621_level22_required_level
    end

    local char_level = number(char.level)
    if char_level <= 0 then
        return action("ReadState", "character level unavailable", { quest_id = M.quest_20621_id })
    end

    if char_level < required_level then
        return M.nextQuest20621Level22GrindAction(state, runtime, opts, quest)
    end

    if runtime.completed_20621_task_teleport == true then
        return M.nextQuest20621AfterTeleportNpcAction(state, runtime, opts, quest)
    end

    return action("QuestTeleport", "quest 20621 level 22 reached; task teleport", {
        quest_id = M.quest_20621_id,
        quest_step = M.questStep(quest),
        required_level = required_level,
        char_level = char_level,
        stage = M.quest_20621_teleport_stage,
        wait_teleport = true,
        direct_quest_id_only = true,
        open_panel_key = false,
        require_panel_visible = false,
    })
end

function M.nextQuest20621AfterTeleportNpcAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20621_id)

    if runtime.completed_20621_after_teleport_npc_dialog == true then
        return M.nextQuest20621AfterDialogTeleportAction(state, runtime, opts, quest)
    end

    local dialog = state.dialog
    if M.isQuest20621AfterTeleportNpcDialog(dialog) then
        return action("ClickDialogLastContinuousOk", "complete quest 20621 after-teleport npc dialog by last-option chain", {
            quest_id = M.quest_20621_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = dialog_content_id(dialog),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20621_after_teleport_npc.interact_id,
            npc_name = M.quest_20621_after_teleport_npc.name,
            npc_name_key = M.quest_20621_after_teleport_npc.name_key,
            click_x = opts.dialog_click_x or 25,
            stage = M.quest_20621_after_teleport_npc_stage,
        })
    end

    if type(dialog) == "table" then
        return action("DumpDialog", "different npc dialog is already open before quest 20621 after-teleport npc", {
            quest_id = M.quest_20621_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = dialog_content_id(dialog),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20621_after_teleport_npc.interact_id,
            npc_name = M.quest_20621_after_teleport_npc.name,
            npc_name_key = M.quest_20621_after_teleport_npc.name_key,
            stage = M.quest_20621_after_teleport_npc_stage,
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_20621_id })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.quest_20621_after_teleport_npc.big_map_id then
        return action("Idle", "quest 20621 after-teleport npc wrong map", {
            quest_id = M.quest_20621_id,
            quest_step = M.questStep(quest),
            big_map_id = current_big_map,
            expected_big_map_id = M.quest_20621_after_teleport_npc.big_map_id,
            stage = M.quest_20621_after_teleport_npc_stage,
        })
    end

    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    local dist = M.distanceToQuest20621AfterTeleportNpc(char)
    if dist > range then
        return action("NavigateToNpc", "move to quest 20621 after-teleport npc", {
            quest_id = M.quest_20621_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20621_after_teleport_npc_stage,
            interact_id = M.quest_20621_after_teleport_npc.interact_id,
            npc_name = M.quest_20621_after_teleport_npc.name,
            npc_name_key = M.quest_20621_after_teleport_npc.name_key,
            x = M.quest_20621_after_teleport_npc.x,
            y = M.quest_20621_after_teleport_npc.y,
            z = M.quest_20621_after_teleport_npc.z,
            distance = dist,
            range = range,
        })
    end

    return action("InteractNpc", "open quest 20621 after-teleport npc dialog", {
        quest_id = M.quest_20621_id,
        quest_step = M.questStep(quest),
        stage = M.quest_20621_after_teleport_npc_stage,
        interact_id = M.quest_20621_after_teleport_npc.interact_id,
        npc_name = M.quest_20621_after_teleport_npc.name,
        npc_name_key = M.quest_20621_after_teleport_npc.name_key,
        allow_interact_id_fallback = true,
        after_open_continuous_last = true,
        click_x = opts.dialog_click_x or 25,
    })
end

function M.nextQuest20621AfterDialogTeleportAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20621_id)

    if runtime.completed_20621_after_dialog_teleport_npc_dialog == true
        or runtime.completed_20621_after_dialog_teleport == true
        or M.isQuest20621AfterDialogTeleportNpcDialog(state.dialog)
        or M.isNearQuest20621AfterDialogTeleportNpc(state, opts) then
        return M.nextQuest20621AfterDialogTeleportNpcAction(state, runtime, opts, quest)
    end

    if type(state.dialog) == "table" then
        return action("Idle", "quest 20621 after-dialog teleport waits for dialog close", {
            quest_id = M.quest_20621_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20621_after_dialog_teleport_stage,
        })
    end

    if runtime.completed_20621_after_dialog_teleport == true then
        return action("Idle", "quest 20621 after-dialog teleport completed; wait next instruction", {
            quest_id = M.quest_20621_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20621_after_dialog_teleport_stage,
        })
    end

    return action("QuestTeleport", "quest 20621 direct task teleport after npc dialog", {
        quest_id = M.quest_20621_id,
        quest_step = M.questStep(quest),
        stage = M.quest_20621_after_dialog_teleport_stage,
        wait_teleport = true,
        direct_quest_id_only = true,
        open_panel_key = false,
        require_panel_visible = false,
    })
end

function M.nextQuest20621AfterDialogTeleportNpcAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20621_id)

    if runtime.completed_20621_after_dialog_teleport_npc_dialog == true then
        return action("Idle", "quest 20621 after-dialog teleport npc completed; wait next instruction", {
            quest_id = M.quest_20621_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20621_after_dialog_teleport_npc_stage,
        })
    end

    local dialog = state.dialog
    if M.isQuest20621AfterDialogTeleportNpcDialog(dialog) then
        return action("ClickDialogLastContinuousOk",
            "complete quest 20621 after-dialog teleport npc dialog by last-option chain", {
            quest_id = M.quest_20621_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = dialog_content_id(dialog),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20621_after_dialog_teleport_npc.interact_id,
            npc_name = M.quest_20621_after_dialog_teleport_npc.name,
            npc_name_key = M.quest_20621_after_dialog_teleport_npc.name_key,
            click_x = opts.dialog_click_x or 25,
            stage = M.quest_20621_after_dialog_teleport_npc_stage,
        })
    end

    if type(dialog) == "table" then
        return action("DumpDialog", "different npc dialog is already open before quest 20621 after-dialog teleport npc", {
            quest_id = M.quest_20621_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = dialog_content_id(dialog),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20621_after_dialog_teleport_npc.interact_id,
            npc_name = M.quest_20621_after_dialog_teleport_npc.name,
            npc_name_key = M.quest_20621_after_dialog_teleport_npc.name_key,
            stage = M.quest_20621_after_dialog_teleport_npc_stage,
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_20621_id })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.quest_20621_after_dialog_teleport_npc.big_map_id then
        return action("Idle", "quest 20621 after-dialog teleport npc wrong map", {
            quest_id = M.quest_20621_id,
            quest_step = M.questStep(quest),
            big_map_id = current_big_map,
            expected_big_map_id = M.quest_20621_after_dialog_teleport_npc.big_map_id,
            stage = M.quest_20621_after_dialog_teleport_npc_stage,
        })
    end

    local range = number(opts.npc_range)
    if range <= 0 then
        range = 6
    end
    local dist = M.distanceToQuest20621AfterDialogTeleportNpc(char)
    if dist > range then
        return action("NavigateToNpc", "move to quest 20621 after-dialog teleport npc", {
            quest_id = M.quest_20621_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20621_after_dialog_teleport_npc_stage,
            interact_id = M.quest_20621_after_dialog_teleport_npc.interact_id,
            npc_name = M.quest_20621_after_dialog_teleport_npc.name,
            npc_name_key = M.quest_20621_after_dialog_teleport_npc.name_key,
            x = M.quest_20621_after_dialog_teleport_npc.x,
            y = M.quest_20621_after_dialog_teleport_npc.y,
            z = M.quest_20621_after_dialog_teleport_npc.z,
            distance = dist,
            range = range,
        })
    end

    return action("InteractNpc", "open quest 20621 after-dialog teleport npc dialog", {
        quest_id = M.quest_20621_id,
        quest_step = M.questStep(quest),
        stage = M.quest_20621_after_dialog_teleport_npc_stage,
        interact_id = M.quest_20621_after_dialog_teleport_npc.interact_id,
        npc_name = M.quest_20621_after_dialog_teleport_npc.name,
        npc_name_key = M.quest_20621_after_dialog_teleport_npc.name_key,
        allow_interact_id_fallback = false,
        after_open_continuous_last = true,
        click_x = opts.dialog_click_x or 25,
    })
end

function M.isQuest20621AfterDialogTeleportRecoveryReady(state, runtime, opts, quest_20621, quest_20622)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    if runtime.completed_20621_after_teleport_npc_dialog == true
        or runtime.completed_20621_after_dialog_teleport == true then
        return true
    end
    if type(state.char) ~= "table" then
        return false
    end
    if M.isQuestKnown(quest_20621) and not M.isQuestDone(quest_20621) then
        return false
    end
    if not M.isQuestLevelBlocked(quest_20622) then
        return false
    end
    if M.questRequiredLevel(quest_20622) ~= 25 then
        return false
    end
    if number(state.char.level) < M.quest_20621_level22_required_level then
        return false
    end
    local range = number(opts.quest_20621_after_dialog_teleport_recovery_range)
    if range <= 0 then
        range = 80
    end
    return M.distanceToQuest20621AfterTeleportNpc(state.char) <= range
end

function M.nextAction(state, runtime, opts)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}

    local teleport_stage = tostring(runtime.teleport_stage or "")
    if runtime.waiting_teleport == true
        and (teleport_stage == M.level_move_stage
            or teleport_stage == M.target_teleport_stage
            or teleport_stage == M.hotspot_teleport_stage
            or teleport_stage == M.quest_20612_teleport_stage
            or teleport_stage == M.quest_20613_teleport_stage
            or teleport_stage == M.quest_20613_after_start_teleport_stage
            or teleport_stage == M.quest_20614_teleport_stage
            or teleport_stage == M.quest_20614_after_start_teleport_stage
            or teleport_stage == M.quest_20615_teleport_stage
            or teleport_stage == M.quest_20615_big_map_teleport_stage
            or teleport_stage == M.quest_20615_after_big_map_teleport_stage
            or teleport_stage == M.quest_20620_teleport_stage
            or teleport_stage == M.quest_20620_after_stigma_teleport_stage
            or teleport_stage == M.quest_20620_after_obelisk_teleport_stage
            or teleport_stage == M.quest_20621_teleport_stage
            or teleport_stage == M.quest_20621_after_dialog_teleport_stage) then
        local waiting_qid = number(runtime.teleport_quest_id)
        if waiting_qid <= 0 then
            if teleport_stage == M.quest_20612_teleport_stage then
                waiting_qid = M.quest_20612_id
            elseif teleport_stage == M.quest_20613_teleport_stage
                or teleport_stage == M.quest_20613_after_start_teleport_stage then
                waiting_qid = M.quest_20613_id
            elseif teleport_stage == M.quest_20614_teleport_stage
                or teleport_stage == M.quest_20614_after_start_teleport_stage then
                waiting_qid = M.quest_20614_id
            elseif teleport_stage == M.quest_20615_teleport_stage
                or teleport_stage == M.quest_20615_big_map_teleport_stage
                or teleport_stage == M.quest_20615_after_big_map_teleport_stage then
                waiting_qid = M.quest_20615_id
            elseif teleport_stage == M.quest_20620_teleport_stage
                or teleport_stage == M.quest_20620_after_stigma_teleport_stage
                or teleport_stage == M.quest_20620_after_obelisk_teleport_stage then
                waiting_qid = M.quest_20620_id
            elseif teleport_stage == M.quest_20621_teleport_stage
                or teleport_stage == M.quest_20621_after_dialog_teleport_stage then
                waiting_qid = M.quest_20621_id
            else
                waiting_qid = M.quest_id
            end
        end
        local detected, reason = M.teleportDetected(state, runtime, opts)
        if detected then
            if teleport_stage == M.quest_20615_big_map_teleport_stage then
                return action("CompleteBigMapTeleport", reason, {
                    quest_id = M.quest_20615_id,
                    stage = teleport_stage,
                    slot = M.quest_20615_big_map_teleport.slot,
                    price = M.quest_20615_big_map_teleport.price,
                    expected_big_map_id = M.quest_20615_big_map_teleport.expected_big_map_id,
                })
            end
            if teleport_stage == M.hotspot_teleport_stage then
                return action("CompleteMapNodeTeleport", reason, {
                    quest_id = M.quest_id,
                    stage = teleport_stage,
                })
            end
            return action("CompleteQuestTeleport", reason, {
                quest_id = waiting_qid,
                stage = teleport_stage,
            })
        end
        return action("WaitPositionChanged", reason, {
            quest_id = waiting_qid,
            stage = teleport_stage,
            min_distance = opts.teleport_min_distance or 20,
        })
    end

    local quest_20611 = M.findQuestById(state.quests, M.quest_id)
    local quest_20612 = M.findQuestById(state.quests, M.quest_20612_id)
    local quest_20613 = M.findQuestById(state.quests, M.quest_20613_id)
    local quest_20614 = M.findQuestById(state.quests, M.quest_20614_id)
    local quest_20615 = M.findQuestById(state.quests, M.quest_20615_id)
    local quest_20620 = M.findQuestById(state.quests, M.quest_20620_id)
    local quest_20621 = M.findQuestById(state.quests, M.quest_20621_id)
    local quest_20622 = M.findQuestById(state.quests, M.quest_20622_id)
    if M.isMissionNpcDialog(state.dialog)
        and runtime.completed_20611_mission_dialog ~= true then
        return M.nextMissionNpcAction(state, runtime, opts, quest_20611)
    end
    if M.isQuest20615TargetNpcDialog(state.dialog)
        and runtime.completed_20615_target_dialog ~= true
        and (runtime.completed_20615_task_teleport == true
            or M.isQuestActive(quest_20615)) then
        return M.nextQuest20615TargetNpcAction(state, runtime, opts, quest_20615)
    end
    if M.isQuest20615TargetNpcDialog(state.dialog)
        and runtime.completed_20615_target_dialog == true
        and runtime.completed_20615_big_map_teleport ~= true
        and (runtime.completed_20615_task_teleport == true
            or M.isQuestActive(quest_20615)) then
        return M.nextQuest20615BigMapTeleportAction(state, runtime, opts, quest_20615)
    end
    if M.isQuest20620StartNpcDialog(state.dialog)
        and runtime.completed_20620_start_dialog ~= true
        and M.isQuestActive(quest_20620) then
        return M.nextQuest20620StartNpcAction(state, runtime, opts, quest_20620)
    end
    if M.isQuest20620AfterTeleportNpcDialog(state.dialog)
        and runtime.completed_20620_after_teleport_npc_dialog ~= true
        and M.isQuestActive(quest_20620) then
        return M.nextQuest20620AfterTeleportNpcAction(state, runtime, opts, quest_20620)
    end
    if M.isQuest20621AfterTeleportNpcDialog(state.dialog)
        and runtime.completed_20621_after_teleport_npc_dialog ~= true
        and (runtime.completed_20621_task_teleport == true
            or M.isQuestActive(quest_20621)) then
        return M.nextQuest20621AfterTeleportNpcAction(state, runtime, opts, quest_20621)
    end
    if runtime.completed_20621_after_dialog_teleport_npc_dialog ~= true
        and (runtime.completed_20621_after_dialog_teleport == true
            or M.isQuest20621AfterDialogTeleportNpcDialog(state.dialog)
            or M.isNearQuest20621AfterDialogTeleportNpc(state, opts)) then
        return M.nextQuest20621AfterDialogTeleportNpcAction(state, runtime, opts, quest_20621)
    end
    if M.isQuest20615MorheimNpcDialog(state.dialog)
        and runtime.completed_20615_morheim_npc_dialog ~= true
        and (runtime.completed_20615_after_big_map_task_teleport == true
            or M.isQuestDone(quest_20615)) then
        return M.nextQuest20615MorheimNpcAction(state, runtime, opts, quest_20615)
    end
    if M.isTargetNpcDialog(state.dialog)
        and runtime.completed_20611_target_dialog ~= true then
        return M.nextTargetNpcAction(state, runtime, opts, quest_20611)
    end
    if M.isHotspotRewardNpcDialog(state.dialog)
        and runtime.completed_20611_hotspot_reward ~= true then
        return M.nextHotspotRewardAction(state, runtime, opts, quest_20611)
    end

    if M.isQuest20621AfterDialogTeleportRecoveryReady(
        state,
        runtime,
        opts,
        quest_20621,
        quest_20622) then
        return M.nextQuest20621AfterDialogTeleportAction(state, runtime, opts, quest_20621)
    end
    if runtime.completed_20621_after_teleport_npc_dialog == true then
        return M.nextQuest20621AfterTeleportNpcAction(state, runtime, opts, quest_20621)
    end

    local level_grind_blue_submit_action = M.nextLevelGrindBlueSubmitAction(state, runtime, opts)
    if level_grind_blue_submit_action then
        return level_grind_blue_submit_action
    end

    local sequential_quest = M.findSequentialQuest(state.quests, runtime)
    local sequential_qid = quest_id(sequential_quest)
    local allow_quest_20612_flow = sequential_qid <= 0
        or sequential_qid >= M.quest_20612_id
    if M.isQuest20612StartNpcDialog(state.dialog)
        and runtime.completed_20612_start_dialog ~= true then
        if not allow_quest_20612_flow then
            return action("Idle", "quest 20612 dialog blocked by earlier yellow mission", {
                quest_id = sequential_qid,
                blocked_quest_id = M.quest_20612_id,
                blocked_stage = M.quest_20612_start_stage,
            })
        end
        return M.nextQuest20612StartAction(state, runtime, opts, quest_20612)
    end
    if M.isQuest20612RewardNpcDialog(state.dialog)
        and runtime.completed_20612_reward_dialog ~= true then
        if not allow_quest_20612_flow then
            return action("Idle", "quest 20612 reward dialog blocked by earlier yellow mission", {
                quest_id = sequential_qid,
                blocked_quest_id = M.quest_20612_id,
                blocked_stage = M.quest_20612_reward_stage,
            })
        end
        return M.nextQuest20612RewardAction(state, runtime, opts, quest_20612)
    end
    if M.isQuest20613StartNpcDialog(state.dialog)
        and runtime.completed_20613_start_dialog ~= true then
        return M.nextQuest20613StartAction(state, runtime, opts, quest_20613)
    end
    if M.isQuest20613AfterStartRewardNpcDialog(state.dialog)
        and runtime.completed_20613_after_start_reward_dialog ~= true then
        return M.nextQuest20613AfterStartRewardAction(state, runtime, opts, quest_20613)
    end
    if M.isQuest20614StartNpcDialog(state.dialog)
        and runtime.completed_20614_start_dialog ~= true then
        return M.nextQuest20614StartAction(state, runtime, opts, quest_20614)
    end
    if M.isQuest20614RewardNpcDialog(state.dialog)
        and runtime.completed_20614_reward_dialog ~= true then
        return M.nextQuest20614RewardAction(state, runtime, opts, quest_20614)
    end

    local hotspot_reward_pending = runtime.completed_20611_hotspot_reward ~= true
        and (runtime.completed_20611_hotspot_teleport == true
            or (M.isQuestDone(quest_20611) and M.questStep(quest_20611) == 3))
    if hotspot_reward_pending then
        return M.nextHotspotRewardAction(state, runtime, opts, quest_20611)
    end

    local quest_20612_active_start = M.isQuestActive(quest_20612)
        and M.questStep(quest_20612) == 0
    local quest_20612_level_ready = M.isQuestLevelBlocked(quest_20612)
        and (runtime.completed_20611_hotspot_reward == true
            or not M.isQuestKnown(quest_20611))
    local quest_20612_task_teleport_ready = M.isQuestActive(quest_20612)
        and (runtime.completed_20612_start_dialog == true
            or M.questStep(quest_20612) == 1)
    local level_quest_after_20612 = state.level_blocked_quest or M.findLevelBlockedQuest(state.quests)
    local quest_20612_reward_dialog_open = M.isQuest20612RewardNpcDialog(state.dialog)
    local quest_20612_reward_npc_near = M.isNearQuest20612RewardNpc(state, opts)
    local quest_20612_reward_ready = M.isQuestDone(quest_20612)
        and runtime.completed_20612_reward_dialog ~= true
        and (runtime.completed_20612_task_teleport == true
            or quest_20612_reward_dialog_open
            or quest_20612_reward_npc_near)
    local quest_20612_done_task_teleport_ready = M.isQuestDone(quest_20612)
        and runtime.completed_20612_task_teleport ~= true
        and runtime.completed_20612_reward_dialog ~= true
        and M.isQuestLevelBlocked(level_quest_after_20612)
        and quest_id(level_quest_after_20612) > M.quest_20612_id
    if allow_quest_20612_flow and (quest_20612_reward_dialog_open or quest_20612_reward_ready) then
        return M.nextQuest20612RewardAction(state, runtime, opts, quest_20612)
    end
    if allow_quest_20612_flow and (quest_20612_active_start or quest_20612_level_ready) then
        return M.nextQuest20612LevelGateAction(state, runtime, opts, quest_20612)
    end
    if allow_quest_20612_flow and quest_20612_task_teleport_ready then
        return M.nextQuest20612TaskTeleportAction(state, runtime, opts, quest_20612)
    end
    if allow_quest_20612_flow and quest_20612_done_task_teleport_ready then
        return M.nextQuest20612TaskTeleportAction(state, runtime, opts, level_quest_after_20612)
    end

    local quest_20613_start_near = M.isNearQuest20613StartNpc(state, opts)
    local quest_20613_start_ready = M.isQuestActive(quest_20613)
        and M.questStep(quest_20613) == 0
        and (runtime.completed_20613_task_teleport == true
            or quest_20613_start_near)
        and runtime.completed_20613_start_dialog ~= true
    if quest_20613_start_ready then
        return M.nextQuest20613StartAction(state, runtime, opts, quest_20613)
    end
    local quest_20613_after_start_reward_dialog_open = M.isQuest20613AfterStartRewardNpcDialog(state.dialog)
    local quest_20613_after_start_reward_near = M.isNearQuest20613AfterStartRewardNpc(state, opts)
    local quest_20613_after_start_reward_pending = runtime.completed_20613_after_start_reward_dialog ~= true
        and (runtime.completed_20613_after_start_teleport == true
            or quest_20613_after_start_reward_dialog_open
            or (M.isQuestDone(quest_20613) and quest_20613_after_start_reward_near))
    if quest_20613_after_start_reward_pending then
        return M.nextQuest20613AfterStartRewardAction(state, runtime, opts, quest_20613)
    end
    local quest_20613_after_start_ready = runtime.completed_20613_after_start_reward_dialog ~= true
        and (runtime.completed_20613_start_dialog == true
            or (M.isQuestActive(quest_20613) and M.questStep(quest_20613) > 0)
            or M.isQuestDone(quest_20613))
    if quest_20613_after_start_ready then
        return M.nextQuest20613AfterStartTeleportAction(state, runtime, opts, quest_20613)
    end

    local post_20612_level14_ready = runtime.completed_20612_reward_dialog == true
        or (M.isQuestLevelBlocked(level_quest_after_20612)
            and quest_id(level_quest_after_20612) == M.quest_20613_id
            and not M.isQuestKnown(quest_20612))
    if post_20612_level14_ready
        and runtime.completed_20613_task_teleport ~= true
        and runtime.completed_20613_after_start_teleport ~= true
        and runtime.completed_20613_after_start_reward_dialog ~= true then
        return M.nextPostQuest20612Level14GrindAction(state, runtime, opts, quest_20613 or level_quest_after_20612)
    end

    local quest_20614_level_quest = nil
    if M.isQuestLevelBlocked(quest_20614) then
        quest_20614_level_quest = quest_20614
    elseif M.isQuestLevelBlocked(level_quest_after_20612)
        and quest_id(level_quest_after_20612) == M.quest_20614_id then
        quest_20614_level_quest = level_quest_after_20612
    end
    local quest_20614_level_ready = type(quest_20614_level_quest) == "table"
        or (runtime.active_20611_grind == true
            and tostring(runtime.active_20611_grind_stage or "") == M.quest_20614_level_grind_stage)
    local quest_20613_cleared_for_20614 = runtime.completed_20613_after_start_reward_dialog == true
        or not M.isQuestKnown(quest_20613)
    if quest_20614_level_ready and quest_20613_cleared_for_20614 then
        return M.nextQuest20614Level17GrindAction(
            state,
            runtime,
            opts,
            quest_20614_level_quest or quest_20614)
    end

    local quest_20614_reward_dialog_open = M.isQuest20614RewardNpcDialog(state.dialog)
    local quest_20614_reward_near = M.isNearQuest20614RewardNpc(state, opts)
    local quest_20614_reward_pending = runtime.completed_20614_reward_dialog ~= true
        and (quest_20614_reward_dialog_open
            or runtime.completed_20614_after_start_teleport == true
            or (M.isQuestDone(quest_20614) and quest_20614_reward_near))
    if quest_20614_reward_pending then
        return M.nextQuest20614RewardAction(state, runtime, opts, quest_20614)
    end

    local quest_20614_after_start_teleport_ready = (M.isQuestActive(quest_20614) or M.isQuestDone(quest_20614))
        and quest_20613_cleared_for_20614
        and runtime.completed_20614_after_start_teleport ~= true
        and (runtime.completed_20614_start_dialog == true
            or M.questStep(quest_20614) > 0
            or tostring(runtime.last_interact_stage or "") == M.quest_20614_start_stage
            or M.isQuestDone(quest_20614))
    if quest_20614_after_start_teleport_ready then
        return M.nextQuest20614AfterStartTeleportAction(state, runtime, opts, quest_20614)
    end

    local quest_20614_active_teleport_ready = M.isQuestActive(quest_20614)
        and M.questStep(quest_20614) == 0
        and quest_20613_cleared_for_20614
    if quest_20614_active_teleport_ready then
        return M.nextQuest20614TaskTeleportAction(state, runtime, opts, quest_20614)
    end

    local quest_20615_level_quest = nil
    if M.isQuestLevelBlocked(quest_20615) then
        quest_20615_level_quest = quest_20615
    elseif M.isQuestLevelBlocked(level_quest_after_20612)
        and quest_id(level_quest_after_20612) == M.quest_20615_id then
        quest_20615_level_quest = level_quest_after_20612
    end
    local quest_20615_level_ready = type(quest_20615_level_quest) == "table"
        or (runtime.active_20611_grind == true
            and tostring(runtime.active_20611_grind_stage or "") == M.quest_20615_level_grind_stage)
    local quest_20614_cleared_for_20615 = runtime.completed_20614_reward_dialog == true
        or not M.isQuestKnown(quest_20614)
    local quest_20615_target_ready = M.isQuestActive(quest_20615)
        and M.questStep(quest_20615) == 0
        and quest_20614_cleared_for_20615
        and runtime.completed_20615_target_dialog ~= true
        and (runtime.completed_20615_task_teleport == true
            or M.isNearQuest20615TargetNpc(state, opts))
    if quest_20615_target_ready then
        return M.nextQuest20615TargetNpcAction(state, runtime, opts, quest_20615)
    end
    local quest_20615_big_map_teleport_ready = (M.isQuestActive(quest_20615) or M.isQuestDone(quest_20615))
        and quest_20614_cleared_for_20615
        and runtime.completed_20615_big_map_teleport ~= true
        and (runtime.completed_20615_target_dialog == true
            or M.questStep(quest_20615) > 0
            or M.isQuestDone(quest_20615))
    if quest_20615_big_map_teleport_ready then
        return M.nextQuest20615BigMapTeleportAction(state, runtime, opts, quest_20615)
    end
    if (M.isQuestActive(quest_20615) or M.isQuestDone(quest_20615))
        and quest_20614_cleared_for_20615
        and runtime.completed_20615_big_map_teleport == true then
        return M.nextQuest20615AfterBigMapTaskTeleportAction(state, runtime, opts, quest_20615)
    end
    local quest_20615_active_teleport_ready = M.isQuestActive(quest_20615)
        and M.questStep(quest_20615) == 0
        and quest_20614_cleared_for_20615
        and runtime.completed_20615_task_teleport ~= true
    if quest_20615_active_teleport_ready then
        return M.nextQuest20615TaskTeleportAction(state, runtime, opts, quest_20615)
    end
    if quest_20615_level_ready and quest_20614_cleared_for_20615 then
        return M.nextQuest20615Level20GrindAction(
            state,
            runtime,
            opts,
            quest_20615_level_quest or quest_20615)
    end

    local quest_20620_after_obelisk_npc_ready = (M.isQuestActive(quest_20620) or M.isQuestDone(quest_20620))
        and (runtime.completed_20620_after_obelisk_teleport == true
            or runtime.completed_20620_after_obelisk_npc_dialog == true
            or M.isQuest20620AfterObeliskNpcDialog(state.dialog)
            or M.isNearQuest20620AfterObeliskNpc(state, opts))
        and (runtime.completed_20620_obelisk == true
            or runtime.completed_20620_after_obelisk_teleport == true
            or runtime.completed_20620_after_obelisk_npc_dialog == true
            or M.isQuest20620AfterObeliskTeleportSnapshot(quest_20620))
        and (runtime.completed_20615_morheim_npc_dialog == true
            or not M.isQuestKnown(quest_20615))
    if quest_20620_after_obelisk_npc_ready then
        return M.nextQuest20620AfterObeliskNpcAction(state, runtime, opts, quest_20620)
    end

    local quest_20620_after_obelisk_teleport_ready = (M.isQuestActive(quest_20620) or M.isQuestDone(quest_20620))
        and (runtime.completed_20620_obelisk == true
            or M.isQuest20620AfterObeliskTeleportSnapshot(quest_20620))
        and runtime.completed_20620_after_obelisk_teleport ~= true
        and runtime.completed_20620_after_obelisk_npc_dialog ~= true
        and (runtime.completed_20615_morheim_npc_dialog == true
            or not M.isQuestKnown(quest_20615))
    if quest_20620_after_obelisk_teleport_ready then
        return M.nextQuest20620AfterObeliskTeleportAction(state, runtime, opts, quest_20620)
    end

    if (runtime.completed_20620_after_teleport_npc_dialog == true
            or runtime.completed_20620_stigma_socket == true
            or runtime.completed_20620_after_stigma_teleport == true
            or runtime.completed_20620_after_stigma_npc_dialog == true
            or runtime.completed_20620_obelisk == true
            or runtime.completed_20620_after_obelisk_teleport == true
            or runtime.completed_20620_after_obelisk_npc_dialog == true)
        and (runtime.completed_20615_morheim_npc_dialog == true
            or not M.isQuestKnown(quest_20615)) then
        return M.nextQuest20620SocketStigmaAction(state, runtime, opts, quest_20620)
    end

    local quest_20620_obelisk_ready = M.isQuestActive(quest_20620)
        and M.questStep(quest_20620) == 4
        and (runtime.completed_20620_after_stigma_npc_dialog == true
            or runtime.opened_20620_obelisk == true
            or M.isObeliskConfirmVisible(state)
            or M.isNearQuest20620Obelisk(state, opts))
        and runtime.completed_20620_obelisk ~= true
        and (runtime.completed_20615_morheim_npc_dialog == true
            or not M.isQuestKnown(quest_20615))
    if quest_20620_obelisk_ready then
        return M.nextQuest20620ObeliskAction(state, runtime, opts, quest_20620)
    end

    local quest_20620_after_stigma_npc_ready = M.isQuestActive(quest_20620)
        and M.questStep(quest_20620) == 3
        and (runtime.completed_20620_after_stigma_teleport == true
            or M.isNearQuest20620AfterStigmaNpc(state, opts)
            or M.isQuest20620AfterStigmaNpcDialog(state.dialog))
        and runtime.completed_20620_after_stigma_npc_dialog ~= true
        and (runtime.completed_20615_morheim_npc_dialog == true
            or not M.isQuestKnown(quest_20615))
    if quest_20620_after_stigma_npc_ready then
        return M.nextQuest20620AfterStigmaNpcAction(state, runtime, opts, quest_20620)
    end

    local quest_20620_after_teleport_npc_ready = M.isQuestActive(quest_20620)
        and M.questStep(quest_20620) == 1
        and (runtime.completed_20620_task_teleport == true
            or M.isNearQuest20620AfterTeleportNpc(state, opts))
        and runtime.completed_20620_after_teleport_npc_dialog ~= true
        and (runtime.completed_20615_morheim_npc_dialog == true
            or not M.isQuestKnown(quest_20615))
    if quest_20620_after_teleport_npc_ready then
        return M.nextQuest20620AfterTeleportNpcAction(state, runtime, opts, quest_20620)
    end

    local quest_20620_task_teleport_ready = (M.isQuestActive(quest_20620) or M.isQuestDone(quest_20620))
        and (runtime.completed_20620_start_dialog == true
            or M.questStep(quest_20620) > 0
            or M.isQuestDone(quest_20620))
        and runtime.completed_20620_task_teleport ~= true
        and runtime.completed_20620_after_teleport_npc_dialog ~= true
        and runtime.completed_20620_stigma_socket ~= true
        and runtime.completed_20620_after_stigma_teleport ~= true
        and runtime.completed_20620_after_stigma_npc_dialog ~= true
        and runtime.completed_20620_obelisk ~= true
        and runtime.completed_20620_after_obelisk_teleport ~= true
        and (runtime.completed_20615_morheim_npc_dialog == true
            or not M.isQuestKnown(quest_20615))
    if quest_20620_task_teleport_ready then
        return M.nextQuest20620TaskTeleportAction(state, runtime, opts, quest_20620)
    end

    local quest_20620_start_ready = M.isQuestActive(quest_20620)
        and M.questStep(quest_20620) == 0
        and (runtime.completed_20615_morheim_npc_dialog == true
            or not M.isQuestKnown(quest_20615))
        and runtime.completed_20620_start_dialog ~= true
    if quest_20620_start_ready then
        return M.nextQuest20620StartNpcAction(state, runtime, opts, quest_20620)
    end

    local quest_20620_cleared_for_20621 = runtime.completed_20620_after_obelisk_npc_dialog == true
        or not M.isQuestKnown(quest_20620)
    local quest_20621_level_ready = M.isQuestLevelBlocked(quest_20621)
        or (runtime.active_20611_grind == true
            and tostring(runtime.active_20611_grind_stage or "") == M.quest_20621_level_grind_stage)
    if quest_20621_level_ready and quest_20620_cleared_for_20621 then
        return M.nextQuest20621Level22GrindAction(state, runtime, opts, quest_20621)
    end

    local quest_20622_level_ready = M.isQuestLevelBlocked(quest_20622)
        or (runtime.active_20611_grind == true
            and tostring(runtime.active_20611_grind_stage or "") == M.quest_20622_level_grind_stage)
    local quest_20621_cleared_for_20622 = runtime.completed_20621_after_dialog_teleport_npc_dialog == true
        or M.isQuestDone(quest_20621)
        or not M.isQuestKnown(quest_20621)
    if quest_20622_level_ready and quest_20621_cleared_for_20622 then
        return M.nextQuest20622Level25GrindAction(state, runtime, opts, quest_20622)
    end

    if runtime.completed_20611_hotspot_reward == true
        or (M.isQuestDone(quest_20611) and M.questStep(quest_20611) == 3) then
        return M.nextHotspotRewardAction(state, runtime, opts, quest_20611)
    end

    local remote_reward_quest = state.remote_reward_quest or M.findRemoteRewardQuest(state.quests)
    local allow_remote_reward_flow = sequential_qid <= 0
    if M.isRemoteRewardDialog(state.dialog) then
        if not allow_remote_reward_flow then
            return action("Idle", "remote reward dialog blocked by earlier yellow mission", {
                quest_id = sequential_qid,
                blocked_quest_id = quest_id(remote_reward_quest),
                stage = "quest_20611_remote_reward",
            })
        end
        local dialog_qid = number(state.dialog.quest_id)
        if dialog_qid <= 0 then
            dialog_qid = quest_id(remote_reward_quest)
        end
        return action("ClickDialogOkCompleteQuest", "confirm blue grind remote reward", {
            quest_id = dialog_qid,
            quest_step = M.questStep(remote_reward_quest),
            stage = "quest_20611_remote_reward",
            content_id = number(state.dialog.dialog_content_id),
            type_text = tostring(state.dialog.type_text or ""),
        })
    end
    if allow_remote_reward_flow and M.isRemoteRewardReady(remote_reward_quest) then
        local ready_qid = quest_id(remote_reward_quest)
        return action("OpenQuestSubmit", "open blue grind remote reward", {
            quest_id = ready_qid,
            quest_step = M.questStep(remote_reward_quest),
            stage = "quest_20611_remote_reward",
        })
    end

    local quest = nil
    if allow_remote_reward_flow
        and M.isRemoteGrindActive(remote_reward_quest)
        and runtime.completed_20611_grind ~= true then
        quest = remote_reward_quest
    end
    local qid = quest_id(quest)
    if qid <= 0 then
        qid = M.quest_id
    end

    if not M.isRemoteGrindActive(quest) then
        local active_quest = nil
        if M.isQuestActive(sequential_quest) then
            active_quest = sequential_quest
        elseif sequential_qid <= 0 then
            if M.isQuestActive(state.quest) then
                active_quest = state.quest
            end
        end
        if not active_quest then
            active_quest = M.findActiveQuest(state.quests)
        end
        if active_quest and sequential_qid > 0
            and is_earlier_quest(sequential_quest, active_quest) then
            active_quest = nil
        end
        if M.isQuestActive(active_quest) then
            local active_qid = quest_id(active_quest)
            local active_step = M.questStep(active_quest)
            if active_qid == M.quest_id and active_step == 1 then
                return M.nextObeliskAction(state, runtime, opts, active_quest)
            end
            if active_qid == M.quest_id and active_step == 2 then
                return M.nextTargetStepAction(state, runtime, opts, active_quest)
            end
            if active_qid == M.quest_id and active_step == 3 then
                return M.nextHotspotTeleportAction(state, runtime, opts, active_quest)
            end
            if active_qid == M.quest_id and active_step > 1 then
                return action("Idle", "quest 20611 next step is not recorded yet", {
                    quest_id = active_qid,
                    quest_step = active_step,
                })
            end
            if active_qid == M.quest_20613_id and active_step == 0 then
                if runtime.completed_20613_task_teleport == true
                    or M.isNearQuest20613StartNpc(state, opts) then
                    return M.nextQuest20613StartAction(state, runtime, opts, active_quest)
                end
                return M.nextQuest20613TaskTeleportAction(state, runtime, opts, active_quest)
            end
            if active_qid == M.quest_20614_id and active_step == 0 then
                return M.nextQuest20614TaskTeleportAction(state, runtime, opts, active_quest)
            end
            if active_qid == M.quest_20614_id and active_step > 0 then
                return M.nextQuest20614AfterStartTeleportAction(state, runtime, opts, active_quest)
            end
            if active_qid == M.quest_20615_id and active_step == 0 then
                return M.nextQuest20615TaskTeleportAction(state, runtime, opts, active_quest)
            end
            if active_qid == M.quest_20620_id then
                if runtime.completed_20620_after_obelisk_teleport == true
                    or runtime.completed_20620_obelisk == true
                    or active_step > 4 then
                    return M.nextQuest20620AfterObeliskTeleportAction(state, runtime, opts, active_quest)
                end
                if active_step == 4
                    and (runtime.completed_20620_after_stigma_npc_dialog == true
                        or runtime.opened_20620_obelisk == true
                        or M.isObeliskConfirmVisible(state)
                        or M.isNearQuest20620Obelisk(state, opts)) then
                    return M.nextQuest20620ObeliskAction(state, runtime, opts, active_quest)
                end
                if active_step == 3
                    and (runtime.completed_20620_after_stigma_teleport == true
                        or M.isNearQuest20620AfterStigmaNpc(state, opts)
                        or M.isQuest20620AfterStigmaNpcDialog(state.dialog)) then
                    return M.nextQuest20620AfterStigmaNpcAction(state, runtime, opts, active_quest)
                end
                if runtime.completed_20620_stigma_socket == true
                    or runtime.completed_20620_after_stigma_teleport == true then
                    return M.nextQuest20620AfterStigmaTeleportAction(state, runtime, opts, active_quest)
                end
                if active_step == 0 and runtime.completed_20620_start_dialog ~= true then
                    return M.nextQuest20620StartNpcAction(state, runtime, opts, active_quest)
                end
                if active_step == 1
                    and (runtime.completed_20620_task_teleport == true
                        or M.isNearQuest20620AfterTeleportNpc(state, opts)
                        or M.isQuest20620AfterTeleportNpcDialog(state.dialog)) then
                    return M.nextQuest20620AfterTeleportNpcAction(state, runtime, opts, active_quest)
                end
                return M.nextQuest20620TaskTeleportAction(state, runtime, opts, active_quest)
            end
            if active_qid == M.quest_20621_id and active_step == 0 then
                return M.nextQuest20621TaskTeleportAction(state, runtime, opts, active_quest)
            end
            if active_qid > M.quest_id then
                return action("Idle", "active yellow mission next step is not recorded yet", {
                    quest_id = active_qid,
                    quest_step = active_step,
                })
            end
            local range = number(opts.npc_range)
            if range <= 0 then
                range = 4
            end
            local near_mission_npc = type(state.char) == "table"
                and M.distanceToNpc(state.char) <= range
            if active_qid == M.quest_id
                and (M.isMissionNpcDialog(state.dialog) or near_mission_npc) then
                return M.nextMissionNpcAction(state, runtime, opts, active_quest)
            end
            if active_qid == M.quest_id
                and runtime.completed_20611_level_move == true then
                return action("Idle", "waiting quest 20611 teleport landing", {
                    quest_id = active_qid,
                    quest_step = M.questStep(active_quest),
                    stage = M.level_move_stage,
                })
            end
            local required_level = M.questRequiredLevel(active_quest)
            if required_level <= 0 and number(runtime.level_grind_quest_id) == active_qid then
                required_level = number(runtime.level_grind_required_level)
            end
            local char_level = number(state.char and state.char.level)
            if required_level > 0 then
                if type(state.char) ~= "table" then
                    return action("ReadState", "character unavailable", { quest_id = active_qid })
                end
                if char_level <= 0 then
                    return action("ReadState", "character level unavailable", { quest_id = active_qid })
                end
                if char_level >= required_level then
                    if runtime.completed_20611_level_move == true
                        and number(runtime.level_move_quest_id) == active_qid then
                        return action("Idle", "yellow mission immediate move already requested", {
                            quest_id = active_qid,
                            required_level = required_level,
                            char_level = char_level,
                            stage = M.level_move_stage,
                        })
                    end
                    return M.nextCurrentQuestTeleportAction(state, runtime, active_quest, "active yellow mission level reached; immediate move", {
                        quest_id = active_qid,
                        quest_step = M.questStep(active_quest),
                        required_level = required_level,
                        char_level = char_level,
                        stage = M.level_move_stage,
                        wait_teleport = true,
                    })
                end
            end
        end

        local active_stage = tostring(runtime.active_20611_grind_stage or "")
        local tracked_level_qid = number(runtime.level_grind_quest_id)
        if runtime.active_20611_grind == true
            and active_stage == M.level_grind_stage
            and tracked_level_qid > 0 then
            if type(state.char) ~= "table" then
                return action("ReadState", "character unavailable", { quest_id = tracked_level_qid })
            end
            local tracked_quest = M.findQuestById(state.quests, tracked_level_qid)
            local required_level = number(runtime.level_grind_required_level)
            if required_level <= 0 then
                required_level = M.questRequiredLevel(tracked_quest)
            end
            local char_level = number(state.char and state.char.level)
            if required_level > 0 and char_level <= 0 then
                return action("ReadState", "character level unavailable", { quest_id = tracked_level_qid })
            end
            local idle_stage = M.level_grind_stage
            if required_level > 0 and char_level >= required_level then
                idle_stage = M.level_move_stage
            end
            return action("Idle", "generic yellow mission level grind is not recorded; stopping", {
                quest_id = tracked_level_qid,
                quest_step = M.questStep(tracked_quest),
                required_level = required_level,
                char_level = char_level,
                stage = idle_stage,
                active_stage = M.level_grind_stage,
            })
        end

        local level_quest = level_quest_after_20612 or state.level_blocked_quest or M.findLevelBlockedQuest(state.quests)
        if M.isQuestLevelBlocked(level_quest) then
            local level_qid = quest_id(level_quest)
            if type(state.char) ~= "table" then
                return action("ReadState", "character unavailable", { quest_id = level_qid })
            end
            local required_level = M.questRequiredLevel(level_quest)
            local char_level = number(state.char and state.char.level)
            if required_level > 0 and char_level <= 0 then
                return action("ReadState", "character level unavailable", { quest_id = level_qid })
            end
            local stage = M.level_grind_stage
            if required_level > 0 and char_level >= required_level then
                stage = M.level_move_stage
            end
            if runtime.completed_20611_level_move == true
                and number(runtime.level_move_quest_id) == level_qid then
                return action("Idle", "yellow mission immediate move already requested", {
                    quest_id = level_qid,
                    required_level = required_level,
                    char_level = char_level,
                    stage = stage,
                })
            end
            return action("Idle", "yellow mission level gate is not recorded yet", {
                quest_id = level_qid,
                quest_step = M.questStep(level_quest),
                required_level = required_level,
                char_level = char_level,
                stage = stage,
            })
        end
        if runtime.completed_20611_grind == true then
            return action("Idle", "blue grind quest already completed", { quest_id = M.remote_reward_quest_id })
        end
        return action("Idle", "blue normal grind task is not active", { quest_id = M.remote_reward_quest_id })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = qid })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return action("Idle", "blue grind quest wrong map", {
            quest_id = qid,
            big_map_id = current_big_map,
            expected_big_map_id = M.big_map_id,
        })
    end

    local range = number(opts.grind_point_range)
    if range <= 0 then
        range = 3
    end
    local dist = M.distanceToGrindPoint(char)
    if dist > range then
        return action("NavigateToGrindPoint", "move to blue grind quest point", {
            quest_id = qid,
            quest_step = M.questStep(quest),
            stage = "quest_20611_grind",
            x = M.grind_point.x,
            y = M.grind_point.y,
            z = M.grind_point.z,
            distance = dist,
            range = range,
        })
    end

    if runtime.active_20611_grind == true then
        return action("WaitQuestComplete", "blue grind quest stationary grind running", {
            quest_id = qid,
            quest_step = M.questStep(quest),
            stage = "quest_20611_grind",
        })
    end

    local anchor = anchor_from_char(char)

    return action("StartStationaryGrind", "start blue grind quest stationary grind", {
        quest_id = qid,
        quest_step = M.questStep(quest),
        stage = "quest_20611_grind",
        x = anchor.x,
        y = anchor.y,
        z = anchor.z,
    })
end

return M
