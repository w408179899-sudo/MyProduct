local T = require("tests.test_framework")
local account_limit = require("aion.account_limit")

local function fake_config(card)
    return {
        get = function(key, default)
            if key == "savedUserCard" then
                return card
            end
            return default
        end,
    }
end

local function make_items(count)
    local items = {}
    for index = 1, count do
        items[index] = { account = "account" .. tostring(index) }
    end
    return items
end

local function run()
    T.reset()
    T.log("\n=== aion account limit tests ===")

    T.test("YY10 card allows ten account windows", function()
        T.assert_eq(account_limit.limitFromCard("YY10ABC"), 10)
    end)

    T.test("YY05 card allows five account windows", function()
        T.assert_eq(account_limit.limitFromCard("YY05ABC"), 5)
    end)

    T.test("old YY card without two digits is unrestricted", function()
        T.assert_nil(account_limit.limitFromCard("YYDC19C1B0D28ED93E4561FA04C26F5CA0"))
    end)

    T.test("add flow allows fifth account for YY05 and blocks sixth", function()
        local cfg = fake_config("YY05ABC")
        local ok_fifth, limit_fifth, count_fifth = account_limit.canAdd(make_items(4), cfg)
        T.assert_true(ok_fifth)
        T.assert_eq(limit_fifth, 5)
        T.assert_eq(count_fifth, 4)

        local ok_sixth, limit_sixth, count_sixth = account_limit.canAdd(make_items(5), cfg)
        T.assert_false(ok_sixth)
        T.assert_eq(limit_sixth, 5)
        T.assert_eq(count_sixth, 5)
    end)

    T.test("login worker fallback filters source indexes beyond license limit", function()
        local items = {
            { __index = 1, account = "a1" },
            { __index = 5, account = "a5" },
            { __index = 6, account = "a6" },
        }
        local allowed, blocked, limit = account_limit.filterLoginItems(items, fake_config("YY05ABC"))
        T.assert_eq(limit, 5)
        T.assert_eq(#blocked, 1)
        T.assert_eq(blocked[1].source_index, 6)
        T.assert_eq(#allowed, 2)
        T.assert_eq(allowed[1].account, "a1")
        T.assert_eq(allowed[2].account, "a5")
    end)

    T.test("login worker fallback leaves old card unrestricted", function()
        local allowed, blocked, limit = account_limit.filterLoginItems(make_items(12), fake_config("YYDC19"))
        T.assert_nil(limit)
        T.assert_eq(#blocked, 0)
        T.assert_eq(#allowed, 12)
    end)

    return T.report("aion_account_limit")
end

return { run = run }
