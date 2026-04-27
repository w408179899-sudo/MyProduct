--[[
    测试用 Lua 脚本 - 用于上传到 DistributionServer 验证加密下载流程
    上传步骤:
    1. 登录 DistributionServer 管理面板
    2. 上传此文件，路径设为: test/hello.lua
    3. 选择加密算法 (chacha20/rc4) 和密钥
    4. 运行 test_resource_server.lua 测试
]]

print("[hello.lua] 加密脚本加载并执行成功!")
print("[hello.lua] 当前时间: " .. os.date("%Y-%m-%d %H:%M:%S"))

return {
    name = "hello",
    version = "1.0.0",
    loaded = true,
    message = "Hello from encrypted cloud script!"
}
