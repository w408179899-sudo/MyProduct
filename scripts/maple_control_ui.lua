local cwd = sys and sys.get_cwd and sys.get_cwd() or "."
package.path = cwd .. "/scripts/?.lua;" .. cwd .. "/scripts/?/init.lua;" .. package.path

local Store = require("maple.account.store")
local Orchestrator = require("maple.account.orchestrator")

local wnd_open = true
local root = Store.load()
local selected_index = 1
local orchestrator = Orchestrator.new()
local last_event = ""

local draft = Store.new_account({
    key = "new_account",
    account = "",
    password = "",
    server = "",
    character_name = "",
    profile = "default"
})

local function set_event(text)
    last_event = text or ""
    if log and log.info then log.info("[maple_ui] " .. last_event) end
end

local function save_root()
    local ok, err = Store.save(root)
    if ok ~= false then set_event("配置已保存到 script_config.json") else set_event("保存失败: " .. tostring(err)) end
end

local function selected_account()
    return Store.get(root, selected_index)
end

local function share_text(index, name)
    if not sys or not sys.get_share then return "" end
    local value = sys.get_share(Store.status_key(index, name))
    return value and tostring(value) or ""
end

local function import_accounts()
    local path = cwd .. "/账号.txt"
    local f = io.open(path, "r")
    if not f then
        set_event("未找到账号.txt")
        return
    end
    root.items = {}
    for line in f:lines() do
        local trimmed = line:gsub("^\239\187\191", ""):match("^%s*(.-)%s*$")
        if trimmed ~= "" and not trimmed:match("^//") then
            local fields = {}
            for part in (trimmed .. "----"):gmatch("(.-)----") do
                fields[#fields + 1] = part:match("^%s*(.-)%s*$")
            end
            if #fields >= 2 then
                Store.add(root, Store.new_account({
                    account = fields[1],
                    password = fields[2],
                    server = fields[3] or "",
                    character_name = fields[4] or "",
                    profile = fields[5] or "default"
                }))
            end
        end
    end
    f:close()
    selected_index = math.min(selected_index, math.max(1, #root.items))
    set_event("导入账号: " .. tostring(#root.items))
end

local function draw_account_table()
    local flags = (imgui.TableFlags_Borders or 1) + (imgui.TableFlags_RowBg or 0) + (imgui.TableFlags_Resizable or 0)
    if not imgui.begin_table("MapleAccounts", 9, flags) then return end
    imgui.table_setup_column("启用", nil, 48)
    imgui.table_setup_column("序号", nil, 48)
    imgui.table_setup_column("账号", nil, 140)
    imgui.table_setup_column("密码", nil, 120)
    imgui.table_setup_column("服务器", nil, 100)
    imgui.table_setup_column("角色", nil, 120)
    imgui.table_setup_column("方案", nil, 100)
    imgui.table_setup_column("状态", nil, 90)
    imgui.table_setup_column("目标", nil, 90)
    imgui.table_headers_row()

    for i, account in ipairs(root.items or {}) do
        imgui.push_id(i)
        imgui.table_next_row()

        imgui.table_next_column()
        local changed, val = imgui.checkbox("##enabled", account.enabled ~= false)
        if changed then account.enabled = val == true end

        imgui.table_next_column()
        if imgui.selectable(string.format("%02d", i), selected_index == i) then selected_index = i end

        imgui.table_next_column()
        changed, val = imgui.input_text("##account", account.account or "", 128)
        if changed then account.account = val end

        imgui.table_next_column()
        changed, val = imgui.input_text("##password", account.password or "", 128)
        if changed then account.password = val end

        imgui.table_next_column()
        changed, val = imgui.input_text("##server", account.server or "", 64)
        if changed then account.server = val end

        imgui.table_next_column()
        changed, val = imgui.input_text("##character", account.character_name or "", 64)
        if changed then account.character_name = val end

        imgui.table_next_column()
        changed, val = imgui.input_text("##profile", account.profile or "default", 64)
        if changed then account.profile = val end

        imgui.table_next_column()
        imgui.text(share_text(i, "status"))

        imgui.table_next_column()
        imgui.text(share_text(i, "goal"))

        imgui.pop_id()
    end
    imgui.end_table()
end

local function draw_add_panel()
    if imgui.collapsing_header("新增账号") then
        local changed, val = imgui.input_text("账号##new_account", draft.account or "", 128)
        if changed then draft.account = val end
        changed, val = imgui.input_text("密码##new_password", draft.password or "", 128)
        if changed then draft.password = val end
        changed, val = imgui.input_text("服务器##new_server", draft.server or "", 64)
        if changed then draft.server = val end
        changed, val = imgui.input_text("角色##new_character", draft.character_name or "", 64)
        if changed then draft.character_name = val end
        changed, val = imgui.input_text("方案##new_profile", draft.profile or "default", 64)
        if changed then draft.profile = val end
        if imgui.button("添加账号", 100, 26) then
            local account = Store.new_account(draft)
            Store.add(root, account)
            selected_index = #root.items
            draft = Store.new_account({ key = "new_account", profile = "default" })
            set_event("账号已添加")
        end
    end
end

local function draw_toolbar()
    local changed, val = imgui.input_int("多开数量", tonumber(root.max_parallel) or 1)
    if changed then root.max_parallel = math.max(1, tonumber(val) or 1) end

    if imgui.button("启动选中", 90, 28) then
        local ok, result = orchestrator:start_account(selected_account(), selected_index)
        set_event(ok and ("启动任务: " .. tostring(result)) or ("启动失败: " .. tostring(result)))
        save_root()
    end
    imgui.same_line()
    if imgui.button("停止选中", 90, 28) then
        local ok, result = orchestrator:stop_account(selected_account(), selected_index, "ui_stop")
        set_event(ok and "已请求停止" or ("停止失败: " .. tostring(result)))
        save_root()
    end
    imgui.same_line()
    if imgui.button("启动全部", 90, 28) then
        local count = orchestrator:start_all(root)
        set_event("启动数量: " .. tostring(count))
        save_root()
    end
    imgui.same_line()
    if imgui.button("停止全部", 90, 28) then
        local count = orchestrator:stop_all(root, "ui_stop_all")
        set_event("停止数量: " .. tostring(count))
        save_root()
    end
    imgui.same_line()
    if imgui.button("导入账号", 90, 28) then import_accounts() end
    imgui.same_line()
    if imgui.button("删除选中", 90, 28) then
        if Store.remove(root, selected_index) then
            selected_index = math.min(selected_index, math.max(1, #root.items))
            set_event("账号已删除")
        end
    end
    imgui.same_line()
    if imgui.button("保存", 70, 28) then save_root() end
end

local function draw_main_window()
    imgui.set_next_window_size(1120, 620, imgui.Cond_FirstUseEver)
    local visible, open = imgui.begin_window("MapleStory 控制台", true)
    if open == false then wnd_open = false end
    if visible then
        draw_toolbar()
        imgui.separator()
        draw_account_table()
        imgui.separator()
        draw_add_panel()
        imgui.separator()
        imgui.text("状态: " .. tostring(last_event))
    end
    imgui.end_window()
end

local function on_render()
    draw_main_window()
end

imgui.style_colors_light()
imgui.on_render(on_render)
set_event("MapleStory UI 已启动")

if not imgui.is_initialized() then
    if imgui.init("MapleStory 控制台") then imgui.run(on_render) end
end

while wnd_open do
    for i, account in ipairs(root.items or {}) do
        orchestrator:poll_account(account, i)
    end
    if sys and sys.sleep then sys.sleep(100) else break end
end

save_root()
return true
