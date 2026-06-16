local T = require("tests.test_framework")
local account_profile = require("aion.account_profile")

local function default_config()
    return {
        profile_name = "default",
        primary_mode = 1,
        priority_mode = 1,
        accounts = { items = {} },
        target = { pid = 0 },
        combat = { radius = 35, mode = 1 },
        gather = { enabled = false },
        skills = { enabled = true },
        character = { race = 0, job = 1 },
        route = {
            selected_route = 1,
            loop = true,
            waypoint_radius = 3,
            route_name = "shared grind",
            route_points = "1,2,3",
            revive_route_name = "shared revive",
            revive_points = "4,5,6",
            saved_routes = {
                route_points = {
                    { name = "saved", points = "7,8,9" },
                },
            },
        },
        leveling = { target_level = 50 },
        npc_dialog = { scan_radius = 45 },
        crafting = { enabled = false },
        supply = { hp_percent = 35 },
        safety = { max_failures = 5 },
        audit = { enabled = true },
        transfer = { route_export_path = "exports/aion_routes.lua" },
        test = { selected_node_id = 0 },
    }
end

local function run()
    T.reset()
    T.log("\n=== aion account profile tests ===")

    T.test("profile key is generated once and remains stable", function()
        local account = { account = "User One" }
        local key1 = account_profile.ensureProfileKey(account, 1)
        account.account = "Renamed"
        local key2 = account_profile.ensureProfileKey(account, 9)
        T.assert_eq(key1, key2)
        T.assert_contains(key1, "acc_001")
    end)

    T.test("effective configs keep private combat values isolated", function()
        local base = default_config()
        local shared = account_profile.sharedRouteFromConfig(base)
        local a = account_profile.buildEffectiveConfig(base, { combat = { radius = 20 } }, shared)
        local b = account_profile.buildEffectiveConfig(base, { combat = { radius = 55 } }, shared)
        T.assert_eq(a.combat.radius, 20)
        T.assert_eq(b.combat.radius, 55)
        T.assert_eq(a.route.route_points, "1,2,3")
        T.assert_eq(b.route.route_points, "1,2,3")
    end)

    T.test("capture splits shared route points from private route options", function()
        local effective = default_config()
        effective.route.selected_route = 2
        effective.route.loop = false
        effective.route.route_points = "shared changed"

        local private_profile, shared_route = account_profile.splitEffectiveConfig(effective)
        T.assert_eq(private_profile.route.selected_route, 2)
        T.assert_false(private_profile.route.loop)
        T.assert_nil(private_profile.route.route_points)
        T.assert_eq(shared_route.route_points, "shared changed")
    end)

    T.test("account target is applied to effective config", function()
        local base = default_config()
        local account = { target = { pid = 1234, hwnd = 5678, character_name = "A" } }
        local effective = account_profile.buildEffectiveConfig(base, {}, nil, account)
        T.assert_eq(effective.target.pid, 1234)
        T.assert_eq(effective.target.hwnd, 5678)
        T.assert_eq(effective.target.character_name, "A")
    end)

    T.test("shared route merge updates both path text and saved route library", function()
        local base = default_config()
        local shared = {
            route_points = "10,11,12",
            saved_routes = {
                route_points = {
                    { name = "library", points = "13,14,15" },
                },
            },
        }
        account_profile.mergeSharedRouteIntoConfig(base, shared)
        T.assert_eq(base.route.route_points, "10,11,12")
        T.assert_eq(base.route.saved_routes.route_points[1].name, "library")
    end)

    T.test("profile save and load round trip keeps private settings", function()
        local path = "exports/test_account_profile_roundtrip.lua"
        if os and os.remove then
            os.remove(path)
        end

        local private_profile = {
            primary_mode = 2,
            combat = { radius = 77 },
            route = { loop = false, selected_route = 2 },
        }
        local ok, err = account_profile.save(path, private_profile)
        T.assert_true(ok, tostring(err))

        local load_ok, loaded, load_err = account_profile.load(path)
        T.assert_true(load_ok, tostring(load_err))
        T.assert_eq(loaded.primary_mode, 2)
        T.assert_eq(loaded.combat.radius, 77)
        T.assert_false(loaded.route.loop)
        T.assert_eq(loaded.route.selected_route, 2)

        if os and os.remove then
            os.remove(path)
        end
    end)

    return T.report("aion_account_profile")
end

return { run = run }
