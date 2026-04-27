--[[
    AetherEngine 测试框架 - 精简版
    提供统一的测试断言、日志输出和报告功能
]]
local M = {}

-- 统一日志输出
local function out(msg) (log and log.info or print)(msg) end
M.log = out

-- 测试统计
M.stats = { total = 0, passed = 0, failed = 0, errors = {} }

-- 重置统计
function M.reset()
    M.stats = { total = 0, passed = 0, failed = 0, errors = {} }
end

-- 运行单个测试
function M.test(name, func)
    M.stats.total = M.stats.total + 1
    local ok, err = pcall(func)
    if ok then
        M.stats.passed = M.stats.passed + 1
        out("[PASS] " .. name)
    else
        M.stats.failed = M.stats.failed + 1
        M.stats.errors[#M.stats.errors + 1] = { name = name, error = tostring(err) }
        out("[FAIL] " .. name .. ": " .. tostring(err))
    end
    return ok
end

-- 跳过测试 (平台不支持等)
function M.skip(name, reason)
    out("[SKIP] " .. name .. (reason and ": " .. reason or ""))
end

-- 断言辅助
local function fail(msg, ...) error(string.format(msg, ...)) end

function M.assert_eq(a, b, msg)
    if a ~= b then fail("%s: expected %s, got %s", msg or "assert_eq", tostring(b), tostring(a)) end
end

function M.assert_true(v, msg)
    if not v then fail("%s: expected true, got %s", msg or "assert_true", tostring(v)) end
end

function M.assert_false(v, msg)
    if v then fail("%s: expected false, got %s", msg or "assert_false", tostring(v)) end
end

function M.assert_type(v, t, msg)
    if type(v) ~= t then fail("%s: expected %s, got %s", msg or "assert_type", t, type(v)) end
end

function M.assert_nil(v, msg)
    if v ~= nil then fail("%s: expected nil, got %s", msg or "assert_nil", tostring(v)) end
end

function M.assert_not_nil(v, msg)
    if v == nil then fail("%s: expected non-nil", msg or "assert_not_nil") end
end

function M.assert_gt(a, b, msg)
    if not (a > b) then fail("%s: expected %s > %s", msg or "assert_gt", tostring(a), tostring(b)) end
end

function M.assert_gte(a, b, msg)
    if not (a >= b) then fail("%s: expected %s >= %s", msg or "assert_gte", tostring(a), tostring(b)) end
end

function M.assert_lt(a, b, msg)
    if not (a < b) then fail("%s: expected %s < %s", msg or "assert_lt", tostring(a), tostring(b)) end
end

function M.assert_lte(a, b, msg)
    if not (a <= b) then fail("%s: expected %s <= %s", msg or "assert_lte", tostring(a), tostring(b)) end
end

-- 断言字符串包含
function M.assert_contains(s, sub, msg)
    if type(s) ~= "string" or not s:find(sub, 1, true) then
        fail("%s: '%s' not found in '%s'", msg or "assert_contains", tostring(sub), tostring(s))
    end
end

-- 断言函数抛出错误
function M.assert_throws(func, msg)
    local ok = pcall(func)
    if ok then fail("%s: expected error but none thrown", msg or "assert_throws") end
end

-- 打印模块测试报告
function M.report(name)
    out(string.format("\n[%s] %d/%d passed", name or "Tests", M.stats.passed, M.stats.total))
    return M.stats.failed == 0
end

-- 打印完整报告
function M.full_report()
    local s = M.stats
    out("\n" .. ("="):rep(60))
    out(string.format("Total: %d | Passed: %d | Failed: %d", s.total, s.passed, s.failed))
    if s.failed > 0 then
        out("\nFailed tests:")
        for _, e in ipairs(s.errors) do out("  - " .. e.name .. "\n    " .. e.error) end
    end
    out(("="):rep(60))
    out(s.failed == 0 and "All tests PASSED!" or "Some tests FAILED!")
    return s.failed == 0
end

-- 辅助: 检查平台
function M.is_windows() return sys and sys.platform() == "windows" end
function M.is_mobile() local p = sys and sys.platform(); return p == "android" or p == "ios" end

-- 辅助: 安全执行并返回结果
function M.try(func, ...)
    local args = {...}
    local ok, result = pcall(function() return func(table.unpack(args)) end)
    return ok, result
end

-- 辅助: 序列化 table 为字符串 (用于日志输出)
function M.dump(obj, indent)
    indent = indent or ""
    local t = type(obj)
    if t == "table" then
        local s = "{"
        local first = true
        for k, v in pairs(obj) do
            if not first then s = s .. ", " end
            first = false
            local key = type(k) == "number" and "[" .. k .. "]" or k
            s = s .. key .. "=" .. M.dump(v, indent)
        end
        return s .. "}"
    elseif t == "string" then
        return '"' .. obj .. '"'
    elseif t == "nil" then
        return "nil"
    else
        return tostring(obj)
    end
end

-- 辅助: 格式化 API 调用日志
-- 用法: T.trace("api_name", {arg1, arg2}, result1, result2, ...)
function M.trace(api, args, ...)
    local argStr = ""
    if args then
        local parts = {}
        for i, v in ipairs(args) do
            parts[i] = M.dump(v)
        end
        argStr = table.concat(parts, ", ")
    end
    
    local results = {...}
    local retStr = ""
    if #results > 0 then
        local parts = {}
        for i, v in ipairs(results) do
            parts[i] = M.dump(v)
        end
        retStr = " => " .. table.concat(parts, ", ")
    end
    
    out(string.format("  %s(%s)%s", api, argStr, retStr))
end

return M
