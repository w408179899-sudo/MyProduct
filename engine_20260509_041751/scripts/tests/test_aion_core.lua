local T = require("tests.test_framework")

local function load_core_with_data(data)
    package.loaded["aion.core"] = nil
    package.loaded["AionData"] = data
    return require("aion.core")
end

local function run()
    T.reset()
    T.log("\n=== aion core tests ===")

    T.test("ensureInit retries InitGameinfo when state has a different pid", function()
        local calls = {}
        local current_pid = 7560
        local data = {
            GetState = function()
                return { inited = true, pid = current_pid }
            end,
            InitGameinfo = function(pid)
                calls[#calls + 1] = pid
                current_pid = pid
                return true, nil
            end,
        }

        local core = load_core_with_data(data)
        local ok, err = core.ensureInit(20864)
        T.assert_true(ok, tostring(err))
        T.assert_eq(#calls, 1)
        T.assert_eq(calls[1], 20864)
    end)

    T.test("ensureInit reports mismatch if InitGameinfo does not bind selected pid", function()
        local data = {
            GetState = function()
                return { inited = true, pid = 7560 }
            end,
            InitGameinfo = function()
                return true, nil
            end,
        }

        local core = load_core_with_data(data)
        local ok, err = core.ensureInit(20864)
        T.assert_false(ok)
        T.assert_contains(tostring(err), "initialized pid mismatch")
    end)

    T.test("ensureInit reloads AionData once when cached module is bound to another pid", function()
        local old_preload = package.preload["AionData"]
        local old_loaded = package.loaded["AionData"]
        local current_pid = 7560
        local reloads = 0
        local first = {
            GetState = function()
                return { inited = true, pid = 7560 }
            end,
            InitGameinfo = function()
                return true, nil
            end,
        }
        local second = {
            GetState = function()
                return { inited = true, pid = current_pid }
            end,
            InitGameinfo = function(pid)
                current_pid = pid
                return true, nil
            end,
        }

        package.loaded["aion.core"] = nil
        package.loaded["AionData"] = first
        package.preload["AionData"] = function()
            reloads = reloads + 1
            return second
        end

        local ok, core = pcall(require, "aion.core")
        T.assert_true(ok, tostring(core))
        local init_ok, err = core.ensureInit(20864)
        T.assert_true(init_ok, tostring(err))
        T.assert_eq(reloads, 1)
        T.assert_eq(core.data, second)

        package.loaded["aion.core"] = nil
        package.loaded["AionData"] = old_loaded
        package.preload["AionData"] = old_preload
    end)

    T.test("ensureInit returns InitGameinfo failure text", function()
        local data = {
            GetState = function()
                return { inited = false, pid = 0 }
            end,
            InitGameinfo = function()
                return false, "init failed"
            end,
        }

        local core = load_core_with_data(data)
        local ok, err = core.ensureInit(20864)
        T.assert_false(ok)
        T.assert_eq(err, "init failed")
    end)

    package.loaded["aion.core"] = nil
    package.loaded["AionData"] = nil
    return T.report("aion_core")
end

return { run = run }
