local cwd = sys and sys.get_cwd and sys.get_cwd() or "."
package.path = cwd .. "/scripts/?.lua;" .. cwd .. "/scripts/?/init.lua;" .. package.path

local Store = require("maple.account.store")
local Orchestrator = require("maple.account.orchestrator")

local wnd_open = true
local cfg = Store.load()
local orchestrator = Orchestrator.new()

local runtime = {
    accounts = {
        selected_index = 1,
        add_window_visible = false,
        add_force_size = false,
        add_draft = nil,
        settings_window_visible = false,
        settings_index = nil,
        save_feedback_active = false,
        save_feedback_ok = true,
        save_feedback_text = "",
        last_status = "",
        last_poll_at = 0
    }
}

local function log_info(text)
    if log and log.info then log.info("[MapleControlUI] " .. tostring(text or "")) end
end

local function set_event(text)
    runtime.accounts.last_status = tostring(text or "")
    log_info(runtime.accounts.last_status)
end

local function account_items()
    cfg.items = cfg.items or {}
    return cfg.items
end

local function clamp_selected_index()
    local items = account_items()
    if #items == 0 then
        runtime.accounts.selected_index = 0
        return
    end
    local index = tonumber(runtime.accounts.selected_index) or 1
    runtime.accounts.selected_index = math.max(1, math.min(index, #items))
end

local function selected_account()
    clamp_selected_index()
    return account_items()[runtime.accounts.selected_index]
end

local function account_display_name(account)
    if not account then return "" end
    if tostring(account.character_name or "") ~= "" then return tostring(account.character_name) end
    if tostring(account.account or "") ~= "" then return tostring(account.account) end
    return tostring(account.key or "")
end

local function account_table_text(value)
    imgui.text(tostring(value == nil and "" or value))
end

local function account_share(index, name)
    if not sys or not sys.get_share then return nil end
    return sys.get_share(Store.status_key(index, name))
end

local function account_status(account, index)
    local shared = account_share(index, "status")
    if shared ~= nil and tostring(shared) ~= "" then return tostring(shared) end
    if account and account.runtime and account.runtime.status then return tostring(account.runtime.status) end
    return "idle"
end

local function account_goal(index)
    return tostring(account_share(index, "goal") or "")
end

local function account_level(index)
    return tostring(account_share(index, "level") or "")
end

local function account_runtime_seconds(account)
    local audit = account and account.audit or {}
    return tonumber(audit.runtime_seconds) or 0
end

local function format_duration(seconds)
    seconds = math.max(0, tonumber(seconds) or 0)
    local h = math.floor(seconds / 3600)
    local m = math.floor((seconds % 3600) / 60)
    local s = math.floor(seconds % 60)
    if h > 0 then return string.format("%d:%02d:%02d", h, m, s) end
    return string.format("%02d:%02d", m, s)
end

local function account_save_domain()
    local ok, err = Store.save(cfg)
    if ok ~= false then
        set_event("账号配置已保存")
        return true
    end
    set_event("账号配置保存失败: " .. tostring(err))
    return false, err
end

local function account_mark_save_feedback(ok, text)
    runtime.accounts.save_feedback_active = true
    runtime.accounts.save_feedback_ok = ok == true
    runtime.accounts.save_feedback_text = tostring(text or "")
end

local function account_select(index, open_settings)
    runtime.accounts.selected_index = tonumber(index) or 0
    clamp_selected_index()
    if open_settings then
        runtime.accounts.settings_index = runtime.accounts.selected_index
        runtime.accounts.settings_window_visible = true
    end
end

local function account_settings_account()
    local index = tonumber(runtime.accounts.settings_index) or tonumber(runtime.accounts.selected_index) or 0
    local account = account_items()[index]
    return account, index
end

local function account_open_settings(account, index)
    if not account then return end
    account_select(index, false)
    runtime.accounts.settings_index = index
    runtime.accounts.settings_window_visible = true
end

local function account_open_add_window()
    runtime.accounts.add_draft = Store.new_account({ profile = "default", task = "main" })
    runtime.accounts.add_force_size = true
    runtime.accounts.add_window_visible = true
end

local function account_confirm_add_window()
    local draft = Store.new_account(runtime.accounts.add_draft or {})
    if draft.account == "" or draft.password == "" then
        set_event("新增账号失败: 账号或密码为空")
        return false
    end
    Store.add(cfg, draft)
    runtime.accounts.selected_index = #account_items()
    runtime.accounts.add_window_visible = false
    runtime.accounts.add_draft = nil
    runtime.accounts.settings_window_visible = false
    account_save_domain()
    set_event("新增账号: " .. account_display_name(draft))
    return true
end

local function account_remove_selected()
    local index = tonumber(runtime.accounts.selected_index) or 0
    local account = account_items()[index]
    if not account then return false end
    orchestrator:stop_account(account, index, "remove_account")
    Store.remove(cfg, index)
    runtime.accounts.settings_window_visible = false
    clamp_selected_index()
    account_save_domain()
    set_event("删除账号: " .. account_display_name(account))
    return true
end

local function account_queue_local_script(action, account, index, source)
    if not account then
        set_event("账号操作失败: 未选择账号")
        return false
    end
    account_save_domain()
    local ok, result
    if action == "start" then
        ok, result = orchestrator:start_account(account, index)
    elseif action == "stop" then
        ok, result = orchestrator:stop_account(account, index, source or "ui_stop")
    else
        ok, result = false, "unknown_action"
    end
    set_event(string.format(
        "%s %s: %s",
        action == "start" and "启动" or "停止",
        account_display_name(account),
        tostring(result)))
    account_save_domain()
    return ok == true
end

local function account_start_runtime_all()
    account_save_domain()
    local count = orchestrator:start_all(cfg)
    account_save_domain()
    set_event("全部启动请求: " .. tostring(count))
    return count
end

local function account_stop_runtime_all()
    local count = orchestrator:stop_all(cfg, "all-stop-button")
    account_save_domain()
    set_event("全部停止请求: " .. tostring(count))
    return count
end

local function account_poll(force)
    local now = os.clock and os.clock() or 0
    if not force and (now - (runtime.accounts.last_poll_at or 0)) < (tonumber(cfg.poll_interval) or 2.0) then
        return
    end
    runtime.accounts.last_poll_at = now
    for index, account in ipairs(account_items()) do
        orchestrator:poll_account(account, index)
    end
end

local function import_accounts()
    local candidates = { cwd .. "/账号.txt", cwd .. "/accounts.txt" }
    local file
    for _, path in ipairs(candidates) do
        file = io.open(path, "r")
        if file then break end
    end
    if not file then
        set_event("导入失败: 未找到 账号.txt 或 accounts.txt")
        return false
    end

    cfg.items = {}
    for line in file:lines() do
        local trimmed = line:gsub("^\239\187\191", ""):match("^%s*(.-)%s*$")
        if trimmed ~= "" and not trimmed:match("^//") then
            local fields = {}
            for part in (trimmed .. "----"):gmatch("(.-)----") do
                fields[#fields + 1] = part:match("^%s*(.-)%s*$")
            end
            if #fields >= 2 then
                Store.add(cfg, Store.new_account({
                    account = fields[1],
                    password = fields[2],
                    server = fields[3] or "",
                    character_name = fields[4] or "",
                    profile = fields[5] or "default",
                    task = fields[6] or "main"
                }))
            end
        end
    end
    file:close()
    runtime.accounts.selected_index = #account_items() > 0 and 1 or 0
    account_save_domain()
    set_event("导入账号: " .. tostring(#account_items()))
    return true
end

local function draw_account_save_feedback()
    local text = runtime.accounts.save_feedback_text or ""
    if not runtime.accounts.save_feedback_active or text == "" then return end
    imgui.same_line()
    if imgui.text_colored then
        if runtime.accounts.save_feedback_ok then
            imgui.text_colored(0.12, 0.48, 0.20, 1.0, text)
        else
            imgui.text_colored(0.90, 0.18, 0.12, 1.0, text)
        end
    else
        imgui.text(text)
    end
end

local function draw_account_identity_fields(account, id_suffix, width)
    width = width or 320
    local changed, val
    imgui.set_next_item_width(width)
    changed, val = imgui.input_text("服务器" .. id_suffix, account.server or "", 128)
    if changed then account.server = val end

    imgui.set_next_item_width(width)
    changed, val = imgui.input_text("角色名" .. id_suffix, account.character_name or "", 128)
    if changed then account.character_name = val end

    imgui.set_next_item_width(width)
    changed, val = imgui.input_text("方案" .. id_suffix, account.profile or "default", 128)
    if changed then account.profile = val end

    imgui.set_next_item_width(width)
    changed, val = imgui.input_text("任务" .. id_suffix, account.task or "main", 128)
    if changed then account.task = val end
end

local function draw_account_login_common_panel()
    local changed, val

    imgui.text("账号公共参数")
    imgui.same_line()
    if imgui.button("保存公共参数", 110, 26) then
        account_save_domain()
        account_mark_save_feedback(true, "公共参数已保存")
    end
    draw_account_save_feedback()
    imgui.separator()

    changed, val = imgui.checkbox("登录完成后自动启动挂机##maple_auto_start_after_login", cfg.auto_start_after_login == true)
    if changed then cfg.auto_start_after_login = val == true end

    changed, val = imgui.checkbox("断线后自动重登##maple_auto_relogin_on_disconnect", cfg.auto_relogin_on_disconnect == true)
    if changed then cfg.auto_relogin_on_disconnect = val == true end

    imgui.same_line()
    imgui.set_next_item_width(90)
    changed, val = imgui.input_float("Cooldown##maple_auto_relogin_cooldown", tonumber(cfg.auto_relogin_cooldown_seconds) or 30)
    if changed then cfg.auto_relogin_cooldown_seconds = math.max(1.0, tonumber(val) or 30) end

    imgui.same_line()
    imgui.set_next_item_width(90)
    changed, val = imgui.input_int("Max tries##maple_auto_relogin_max", tonumber(cfg.auto_relogin_max_attempts) or 0)
    if changed then cfg.auto_relogin_max_attempts = math.max(0, tonumber(val) or 0) end

    imgui.same_line()
    imgui.set_next_item_width(80)
    changed, val = imgui.input_int("多开上限##maple_max_parallel", tonumber(cfg.max_parallel) or 1)
    if changed then cfg.max_parallel = math.max(1, tonumber(val) or 1) end

    imgui.spacing()
    imgui.set_next_item_width(620)
    changed, val = imgui.input_text("游戏路径", cfg.game_path or "", 512)
    if changed then cfg.game_path = val end

    imgui.set_next_item_width(620)
    changed, val = imgui.input_text("启动器路径", cfg.launcher_path or "", 512)
    if changed then cfg.launcher_path = val end
end

local function draw_accounts_overview()
    draw_account_login_common_panel()

    if imgui.button("新增账号", 90, 26) then
        account_open_add_window()
    end
    imgui.same_line()
    if imgui.button("导入账号", 90, 26) then
        import_accounts()
    end
    imgui.same_line()
    if imgui.button("全部启动", 90, 26) then
        account_start_runtime_all()
    end
    imgui.same_line()
    if imgui.button("全部停止", 90, 26) then
        account_stop_runtime_all()
    end
    imgui.same_line()
    if imgui.button("刷新审计", 90, 26) then
        account_poll(true)
        set_event("账号审计已刷新")
    end
    imgui.same_line()
    if imgui.button("保存配置", 90, 26) then
        account_save_domain()
    end

    imgui.spacing()
    local items = account_items()
    if #items == 0 then
        imgui.text("No account. Add one.")
        return
    end

    local table_flags = imgui.TableFlags_Borders + imgui.TableFlags_RowBg + imgui.TableFlags_Resizable
    if imgui.begin_table("##maple_account_overview_table", 8, table_flags) then
        imgui.table_setup_column("账号", imgui.TableColumnFlags_WidthFixed, 130)
        imgui.table_setup_column("角色", imgui.TableColumnFlags_WidthFixed, 100)
        imgui.table_setup_column("任务", imgui.TableColumnFlags_WidthFixed, 90)
        imgui.table_setup_column("状态", imgui.TableColumnFlags_WidthFixed, 70)
        imgui.table_setup_column("目标", imgui.TableColumnFlags_WidthFixed, 90)
        imgui.table_setup_column("等级", imgui.TableColumnFlags_WidthFixed, 50)
        imgui.table_setup_column("时长", imgui.TableColumnFlags_WidthFixed, 70)
        imgui.table_setup_column("操作", imgui.TableColumnFlags_WidthStretch)
        imgui.table_headers_row()

        for index, account in ipairs(items) do
            local selected = runtime.accounts.selected_index == index
            imgui.table_next_row()

            imgui.table_next_column()
            if imgui.selectable(account_display_name(account) .. "##maple_account_row_" .. tostring(index), selected) then
                account_select(index, false)
            end
            if imgui.is_item_hovered and imgui.is_mouse_double_clicked and imgui.is_item_hovered() and imgui.is_mouse_double_clicked(0) then
                account_open_settings(account, index)
            end

            imgui.table_next_column()
            account_table_text(account.character_name)

            imgui.table_next_column()
            account_table_text(account.task or "main")

            imgui.table_next_column()
            account_table_text(account_status(account, index))

            imgui.table_next_column()
            account_table_text(account_goal(index))

            imgui.table_next_column()
            account_table_text(account_level(index))

            imgui.table_next_column()
            account_table_text(format_duration(account_runtime_seconds(account)))

            imgui.table_next_column()
            if imgui.small_button("设置##maple_account_settings_" .. tostring(index)) then
                account_open_settings(account, index)
            end
            imgui.same_line()
            if imgui.small_button("启动##maple_account_start_" .. tostring(index)) then
                account_queue_local_script("start", account, index, "row-start-button")
            end
            imgui.same_line()
            if imgui.small_button("停止##maple_account_stop_" .. tostring(index)) then
                account_queue_local_script("stop", account, index, "row-stop-button")
            end
            imgui.same_line()
            if imgui.small_button("删除##maple_account_delete_" .. tostring(index)) then
                account_select(index, false)
                account_remove_selected()
                imgui.end_table()
                return
            end
        end
        imgui.end_table()
    end
end

local function draw_overview_tab(account, index)
    local changed, val
    imgui.text("账号运行概览")
    imgui.separator()
    imgui.text("状态: " .. account_status(account, index))
    imgui.text("目标: " .. account_goal(index))
    imgui.text("等级: " .. account_level(index))
    imgui.text("任务ID: " .. tostring(account.runtime and account.runtime.task_id or ""))

    imgui.spacing()
    imgui.set_next_item_width(320)
    changed, val = imgui.input_text("路线名", account.route or "", 128)
    if changed then account.route = val end

    imgui.set_next_item_width(520)
    changed, val = imgui.input_text("备注", account.note or "", 512)
    if changed then account.note = val end
end

local function draw_route_tab(account)
    local changed, val
    imgui.text("路径配置")
    imgui.separator()
    imgui.set_next_item_width(520)
    changed, val = imgui.input_text("路线", account.route or "", 256)
    if changed then account.route = val end
    imgui.text("Maple 路径执行器后续接入 Environment adapter。")
end

local function draw_maintenance_tab(account)
    imgui.text("维护配置")
    imgui.separator()
    imgui.text("账号启停、断线重登、补给和异常恢复会在这里继续扩展。")
    imgui.text("当前账号: " .. account_display_name(account))
end

local function draw_account_tab(account)
    local changed, val
    imgui.text("账号设置")
    imgui.separator()

    changed, val = imgui.checkbox("启用账号", account.enabled ~= false)
    if changed then account.enabled = val == true end

    imgui.set_next_item_width(320)
    changed, val = imgui.input_text("账号", account.account or "", 128)
    if changed then account.account = val end

    imgui.set_next_item_width(320)
    changed, val = imgui.input_text("密码", account.password or "", 128)
    if changed then account.password = val end

    imgui.set_next_item_width(320)
    changed, val = imgui.input_text("二级密码", account.second_password or "", 128)
    if changed then account.second_password = val end

    imgui.spacing()
    draw_account_identity_fields(account, "##maple_account_settings", 320)
end

local function draw_test_tab(account, index)
    imgui.text("测试入口")
    imgui.separator()
    if imgui.button("写入测试状态", 120, 26) then
        if sys and sys.set_share then
            sys.set_share(Store.status_key(index, "status"), "test")
            sys.set_share(Store.status_key(index, "goal"), "mock")
        end
        set_event("已写入测试状态: " .. account_display_name(account))
    end
end

local function draw_account_settings_window()
    if not runtime.accounts.settings_window_visible then return end

    local account, index = account_settings_account()
    local title_name = account and account_display_name(account) or "未选择账号"

    imgui.set_next_window_size(860, 760, imgui.Cond_FirstUseEver)
    imgui.set_next_window_pos(180, 120, imgui.Cond_FirstUseEver)

    local visible, open = imgui.begin_window(
        "账号设置 - " .. tostring(title_name) .. "###maple_account_settings_window",
        true,
        imgui.WindowFlags_NoCollapse)
    if open == false then runtime.accounts.settings_window_visible = false end

    if visible then
        if imgui.button("保存配置##maple_account_save_config", 120, 26) then
            local ok = account_save_domain()
            account_mark_save_feedback(ok == true, ok and "配置已保存" or "保存失败")
        end
        draw_account_save_feedback()

        if account then
            imgui.same_line()
            if imgui.button("启动脚本", 90, 26) then
                account_queue_local_script("start", account, index, "settings-start-button")
            end
            imgui.same_line()
            if imgui.button("停止脚本", 90, 26) then
                account_queue_local_script("stop", account, index, "settings-stop-button")
            end
        end

        imgui.separator()
        if not account then
            imgui.text("请先在账号总览中选择账号。")
        elseif imgui.begin_tab_bar("##maple_account_settings_tabs") then
            if imgui.begin_tab_item("总览") then
                draw_overview_tab(account, index)
                imgui.end_tab_item()
            end
            if imgui.begin_tab_item("路径") then
                draw_route_tab(account)
                imgui.end_tab_item()
            end
            if imgui.begin_tab_item("维护") then
                draw_maintenance_tab(account)
                imgui.end_tab_item()
            end
            if imgui.begin_tab_item("账号") then
                draw_account_tab(account)
                imgui.end_tab_item()
            end
            if imgui.begin_tab_item("测试") then
                draw_test_tab(account, index)
                imgui.end_tab_item()
            end
            imgui.end_tab_bar()
        end
    end

    imgui.end_window()
end

local function draw_account_add_window()
    if not runtime.accounts.add_window_visible then return end

    local size_cond = runtime.accounts.add_force_size and imgui.Cond_Always or imgui.Cond_FirstUseEver
    imgui.set_next_window_size(560, 330, size_cond)
    imgui.set_next_window_pos(260, 180, imgui.Cond_FirstUseEver)
    local visible, open = imgui.begin_window("新增账号###maple_add_account_window", true, imgui.WindowFlags_NoCollapse)
    if open == false then
        runtime.accounts.add_window_visible = false
        runtime.accounts.add_draft = nil
        runtime.accounts.add_force_size = false
    end

    if visible then
        runtime.accounts.add_force_size = false
        if type(runtime.accounts.add_draft) ~= "table" then
            runtime.accounts.add_draft = Store.new_account({ profile = "default", task = "main" })
        end
        local draft = runtime.accounts.add_draft
        local changed, val

        imgui.set_next_item_width(360)
        changed, val = imgui.input_text("账号", draft.account or "", 128)
        if changed then draft.account = val end

        imgui.set_next_item_width(360)
        changed, val = imgui.input_text("密码", draft.password or "", 128)
        if changed then draft.password = val end

        imgui.set_next_item_width(360)
        changed, val = imgui.input_text("二级密码", draft.second_password or "", 128)
        if changed then draft.second_password = val end

        imgui.spacing()
        draw_account_identity_fields(draft, "##maple_add_account", 360)

        imgui.spacing()
        if imgui.button("确认", 90, 26) then
            account_confirm_add_window()
        end
        imgui.same_line()
        if imgui.button("取消", 90, 26) then
            runtime.accounts.add_window_visible = false
            runtime.accounts.add_draft = nil
            runtime.accounts.add_force_size = false
        end
    end
    imgui.end_window()
end

local function draw_main_window()
    imgui.set_next_window_size(1040, 720, imgui.Cond_FirstUseEver)
    imgui.set_next_window_pos(80, 80, imgui.Cond_FirstUseEver)

    local visible, open = imgui.begin_window("MapleStory 控制台###maple_control_ui", true, imgui.WindowFlags_NoCollapse)
    if open == false then wnd_open = false end

    if visible then
        if imgui.begin_tab_bar("##maple_main_tabs") then
            if imgui.begin_tab_item("账号") then
                draw_accounts_overview()
                imgui.end_tab_item()
            end
            if imgui.begin_tab_item("运行") then
                imgui.text("运行总览")
                imgui.separator()
                imgui.text("账号数: " .. tostring(#account_items()))
                imgui.text("多开上限: " .. tostring(cfg.max_parallel or 1))
                imgui.text("最近状态: " .. tostring(runtime.accounts.last_status or ""))
                imgui.end_tab_item()
            end
            if imgui.begin_tab_item("日志") then
                imgui.text("最近状态: " .. tostring(runtime.accounts.last_status or ""))
                imgui.text("日志文件由引擎 logs/ 输出。")
                imgui.end_tab_item()
            end
            imgui.end_tab_bar()
        end
    end
    imgui.end_window()
end

local function on_render()
    draw_main_window()
    draw_account_settings_window()
    draw_account_add_window()
end

imgui.style_colors_light()
imgui.on_render(on_render)
set_event("MapleStory UI 已启动")

if not imgui.is_initialized() then
    if imgui.init("MapleStory 控制台") then imgui.run(on_render) end
end

while wnd_open do
    account_poll(false)
    if sys and sys.sleep then sys.sleep(100) else break end
end

account_save_domain()
return true
