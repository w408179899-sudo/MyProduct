-- 单独运行 config 模块测试
local script_dir = debug.getinfo(1, "S").source:match("@(.+[\\/])")
if script_dir then
    package.path = script_dir .. "../?.lua;" .. script_dir .. "?.lua;" .. package.path
end

local test_config = require("tests.test_config")
local passed, failed = test_config.run()
print(string.format("\n测试结果: %d 通过, %d 失败", passed, failed))
os.exit(failed > 0 and 1 or 0)
