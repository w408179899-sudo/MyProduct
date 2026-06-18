--[[
    Remote 模块测试 UI
    
    带 ImGui 界面的中控连接测试工具
    可视化测试 remote 模块的连接、状态上报、命令接收
    
    使用方法:
    1. 编辑器模式: AetherRunner.exe --gui，然后运行此脚本
    2. 命令行模式: AetherRunner.exe scripts/test_remote_ui.lua
]]

--============================================================================
-- 状态变量
--============================================================================

local state = {
    -- 连接配置
    server_url = "ws://127.0.0.1:8080/ws",
    device_id = sys.hwid(),
    license_key = "",
    
    -- 字段定义
    fields = "角色名|等级|职业|地图|状态",
    
    -- 多账号状态上报 (每行一个账号，|分隔字段，多行用\n合并上报)
    accounts = {
        { data = "角色A|99|战士|龙之谷|运行中" },
        { data = "角色B|50|法师|冰雪城|挂机中" },
        { data = "角色C|30|刺客|沙漠|离线" },
    },
    
    -- 自定义上报
    custom_key = "gold",
    custom_value = "99999",
    
    -- 命令日志
    cmd_log = {},
    max_log = 50,
    
    -- 自动上报
    auto_report = false,
    report_interval = 2000,
    last_report_time = 0,
}

--============================================================================
-- 辅助函数
--============================================================================

local function add_log(msg)
    table.insert(state.cmd_log, 1, os.date("[%H:%M:%S] ") .. msg)
    if #state.cmd_log > state.max_log then
        table.remove(state.cmd_log)
    end
end

local function build_status_str()
    local lines = {}
    for _, acc in ipairs(state.accounts) do
        if #acc.data > 0 then
            lines[#lines + 1] = acc.data
        end
    end
    return table.concat(lines, "\n")
end

--============================================================================
-- UI 绘制
--============================================================================

local function draw_connection_panel()
    -- 连接状态
    if remote.is_connected() then
        imgui.text_colored(0, 220, 0, 255, "[已连接]")
    else
        imgui.text_colored(255, 80, 80, 255, "[未连接]")
    end
    imgui.same_line()
    imgui.text("Device: " .. state.device_id)
    
    imgui.separator()
    
    local changed, new_url = imgui.input_text("服务器", state.server_url, 256)
    if changed then state.server_url = new_url end
    
    changed, new_val = imgui.input_text("License", state.license_key, 128)
    if changed then state.license_key = new_val end
    
    -- 连接/断开按钮
    if not remote.is_connected() then
        if imgui.button("连接", 80, 25) then
            local key = #state.license_key > 0 and state.license_key or nil
            remote.connect(state.server_url, state.device_id, key)
            add_log("连接 " .. state.server_url)
        end
    else
        if imgui.button("断开", 80, 25) then
            remote.disconnect()
            add_log("已断开")
        end
    end
end

local function draw_fields_panel()
    imgui.separator()
    imgui.text("字段定义")
    
    local changed, new_fields = imgui.input_text("字段(|分隔)", state.fields, 512)
    if changed then state.fields = new_fields end
    
    imgui.same_line()
    if imgui.button("发送##fields", 50, 0) then
        remote.define_fields(state.fields)
        add_log("字段: " .. state.fields)
    end
end

local function draw_report_panel()
    imgui.separator()
    imgui.text("状态上报 (多账号, 每行一个, |分隔字段)")
    
    -- 多账号编辑
    local remove_idx = nil
    for i, acc in ipairs(state.accounts) do
        imgui.push_id(i)
        local changed, new_data = imgui.input_text("##acc", acc.data, 512)
        if changed then acc.data = new_data end
        imgui.same_line()
        if imgui.button("X##del", 20, 0) then
            remove_idx = i
        end
        imgui.pop_id()
    end
    if remove_idx then
        table.remove(state.accounts, remove_idx)
    end
    
    if imgui.button("+", 20, 0) then
        state.accounts[#state.accounts + 1] = { data = "" }
    end
    imgui.same_line()
    
    if imgui.button("上报状态", 80, 0) then
        local s = build_status_str()
        remote.report_status(s)
        add_log("上报 " .. #state.accounts .. " 个账号")
    end
    imgui.same_line()
    
    local auto_changed, auto_val = imgui.checkbox("自动", state.auto_report)
    if auto_changed then state.auto_report = auto_val end
    
    -- 自定义键值
    imgui.spacing()
    local changed, new_val
    changed, new_val = imgui.input_text("Key", state.custom_key, 64)
    if changed then state.custom_key = new_val end
    imgui.same_line()
    changed, new_val = imgui.input_text("Value", state.custom_value, 128)
    if changed then state.custom_value = new_val end
    imgui.same_line()
    if imgui.button("上报##kv", 40, 0) then
        remote.report(state.custom_key, state.custom_value)
        add_log(state.custom_key .. "=" .. state.custom_value)
    end
end

local function draw_command_log()
    imgui.separator()
    imgui.text("命令日志")
    imgui.same_line()
    if imgui.button("清空", 40, 0) then
        state.cmd_log = {}
    end
    
    imgui.begin_child("log_scroll", 0, 0, true)
    for _, line in ipairs(state.cmd_log) do
        imgui.text(line)
    end
    imgui.end_child()
end

--============================================================================
-- 主渲染
--============================================================================

local function on_render()
    imgui.set_next_window_size(480, 520, imgui.Cond_FirstUseEver)
    
    if imgui.begin_window("Remote Test") then
        draw_connection_panel()
        draw_fields_panel()
        draw_report_panel()
        draw_command_log()
    end
    imgui.end_window()
    
    -- 轮询命令
    local cmds = remote.poll()
    for _, cmd in ipairs(cmds) do
        local parts = {}
        for k, v in pairs(cmd.payload) do
            parts[#parts + 1] = k .. "=" .. v
        end
        add_log("[CMD] " .. cmd.type .. "/" .. cmd.name .. " " .. table.concat(parts, " "))
    end
    
    -- 自动上报
    if state.auto_report and remote.is_connected() then
        local now = sys.tick()
        if now - state.last_report_time >= state.report_interval then
            remote.report_status(build_status_str())
            state.last_report_time = now
        end
    end
end

--============================================================================
-- 主入口
--============================================================================

log.info("Remote Test UI")

imgui.on_render(on_render)

if imgui.is_initialized() then
    log.info("编辑器模式: 渲染回调已注册")
else
    log.info("命令行模式: 初始化 ImGui")
    if not imgui.init() then
        log.error("ImGui 初始化失败")
        return
    end
    imgui.run()
    log.info("Remote 测试 UI 已关闭")
end

hotkey.start()
while true do
    if hotkey.is_pressed(0x11) and hotkey.is_pressed(0x7B) then
        log.info("Ctrl+F12 被按下")
        hotkey.stop()
        break
    end
    sys.sleep(10)
end
