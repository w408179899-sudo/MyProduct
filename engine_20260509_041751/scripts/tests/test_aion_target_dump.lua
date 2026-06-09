local T = require("tests.test_framework")

local function clear_modules()
    package.loaded["aion.target_dump"] = nil
    package.loaded["aion.core"] = nil
    package.loaded["aion.combat"] = nil
    package.loaded["aion.entity"] = nil
end

local function install_mocks(target, entities)
    clear_modules()
    package.loaded["aion.core"] = {
        getCharacter = function()
            return true, { x = 0, y = 0, z = 0 }, nil
        end,
        distance3 = function(a, b)
            local dx = (a.x or 0) - (b.x or 0)
            local dy = (a.y or 0) - (b.y or 0)
            local dz = (a.z or 0) - (b.z or 0)
            return math.sqrt(dx * dx + dy * dy + dz * dz)
        end,
    }
    package.loaded["aion.combat"] = {
        currentTarget = function()
            return true, target, nil
        end,
    }
    package.loaded["aion.entity"] = {
        list = function()
            return true, entities or {}, nil
        end,
    }
    return require("aion.target_dump")
end

local function run()
    T.reset()
    T.log("\n=== aion target dump tests ===")

    T.test("matches selected target by object id", function()
        local dump = install_mocks({ obj = 1001, id = 77 }, {
            { obj = 1001, id = 77, name = "Training Dummy", tag = "NPC", x = 3, y = 4, z = 0, interact_id = 12 },
        })

        local ok, result, err = dump.read()
        T.assert_true(ok, err)
        T.assert_eq(result.status, "target_matched")
        T.assert_contains(result.summary, "Training Dummy")
        T.assert_contains(table.concat(result.lines, "\n"), "matched=true")
        T.assert_contains(table.concat(result.lines, "\n"), "dist=5.00")
    end)

    T.test("falls back to matching by target id", function()
        local dump = install_mocks({ obj = 9999, id = 42 }, {
            { obj = 1001, id = 42, name = "PlayerOne", type_name = "Player", x = 1, y = 0, z = 0 },
        })

        local ok, result, err = dump.read()
        T.assert_true(ok, err)
        T.assert_eq(result.status, "target_matched")
        T.assert_contains(result.lines[1], "match=id")
        T.assert_contains(result.lines[2], "kind=Player")
    end)

    T.test("reports no target cleanly", function()
        local dump = install_mocks(nil, {})
        local ok, result, err = dump.read()
        T.assert_true(ok, err)
        T.assert_eq(result.status, "no_target")
        T.assert_eq(result.lines[1], "current none")
    end)

    T.test("reports unmatched target when around list has no entry", function()
        local dump = install_mocks({ obj = 2002, id = 88 }, {
            { obj = 1001, id = 42, name = "Other" },
        })

        local ok, result, err = dump.read()
        T.assert_true(ok, err)
        T.assert_eq(result.status, "target_unmatched")
        T.assert_contains(table.concat(result.lines, "\n"), "target not found")
    end)

    clear_modules()
    return T.report("aion_target_dump")
end

return { run = run }
