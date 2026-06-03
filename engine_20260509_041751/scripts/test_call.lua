--[[
    proc.call 测试: 调用目标进程中的 MessageBoxW
    用法: 先打开 SimpleBezier11.exe, 然后运行此脚本
]]

local MessageBoxW = 0x7FFD99B991F0

-- 找目标进程 PID
local pid = nil
for _, p in ipairs(proc.list()) do
    if p.name:lower():find("simplebezier11") then pid = p.pid; break end
end
if not pid then log.error("未找到 SimpleBezier11.exe"); return end
log.info(string.format("PID=%d", pid))

-- 分配字符串内存
local title = proc.alloc(pid, 256)
local text  = proc.alloc(pid, 256)
proc.write_bytes(pid, title, "\x41\x00\x65\x00\x74\x00\x68\x00\x65\x00\x72\x00\x00\x00")          -- "Aether"
proc.write_bytes(pid, text,  "\x48\x00\x65\x00\x6C\x00\x6C\x00\x6F\x00\x21\x00\x00\x00")          -- "Hello!"

-- MessageBoxW(NULL, text, title, MB_OK|MB_ICONINFORMATION=0x40)
log.info(string.format("call MessageBoxW(0, 0x%X, 0x%X, 0x40)", text, title))
-- local ret, err = proc.call(pid, MessageBoxW, 0, text, title, 0x40)
local ret, err = proc.call(pid, MessageBoxW, 0, text, title, 1)
if ret then
    log.info(string.format("return value: %d (0x%X)", ret, ret))
else
    log.error("call failed: " .. tostring(err))
end

proc.free(pid, title)
proc.free(pid, text)
