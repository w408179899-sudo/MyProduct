--[[
    测试用 Lua 模块 - 用于上传到 DistributionServer 验证 require 懒加载
    上传步骤:
    1. 登录 DistributionServer 管理面板
    2. 上传此文件，路径设为: test/mymodule.lua
    3. 选择加密算法 (chacha20/rc4) 和密钥 (需与 hello.lua 一致)
    4. 运行 test_resource_server.lua，require("test.mymodule") 将自动下载此文件
]]

local M = {}

M.name = "mymodule"
M.version = "1.0.0"

--- 简单的加法函数 (用于验证模块功能正常)
function M.add(a, b)
    return a + b
end

--- 返回问候信息
function M.greet(name)
    return string.format("Hello, %s! (from cloud module v%s)", name or "world", M.version)
end

print("[mymodule] 云端模块加载成功: " .. M.name .. " v" .. M.version)

return M
