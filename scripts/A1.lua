--[[
    A1.lua - 种族职业选择界面
]]

-- A1.lua  — UI 主线程
-- A2.lua  — 主线程（多账号调度）
-- A3.lua  — 窗口线程（登录+任务入口）
-- A4.lua  — 登录流程封装
-- A5.lua  — 驱动加载（独立线程）
-- A7.lua  — API 封装层
-- A8.lua  — INI 文件读写工具
-- C1.lua  — 任务系统（主线/挂机/技能/换装）
-- C2.lua  — 功能封装（对话/UI按钮）
-- C3.lua  — 键鼠操作封装

local imgui = imgui
local api = require("A7")
local ini = require("A8")

-- 所有日志输出自动附加线程索引前缀
do
    local xi = "主"
    local _info, _warn, _error = log.info, log.warn, log.error
    log.info = function(fmt, ...) _info("[" .. xi .. "] " .. (fmt or ""), ...) end
    log.warn = function(fmt, ...) _warn("[" .. xi .. "] " .. (fmt or ""), ...) end
    log.error = function(fmt, ...) _error("[" .. xi .. "] " .. (fmt or ""), ...) end
end

--============================================================================
-- 状态变量
--============================================================================
local wnd_open = true
local guaji_settings_open = false  -- 游戏挂机设置窗口是否打开
local guaji_account_idx = 0         -- 选中账号的序号
local guaji_coord_list = {}       -- 挂机坐标列表 {enabled, level1, level2, map, coord}
local guaji_coord_popup_idx = 0  -- 右键行索引
local guaji_coord_hover = nil    -- 当前悬停行索引
local script_running = false
local script_task_id = nil
local pending_start = false
local pending_stop = false

-- 坐标录制状态
local coord_recording = false
local coord_text = ""
local last_record_x = 0
local last_record_y = 0

-- 分组框内四个下拉框
-- (已废弃，由 configN.ini 驱动)

-- 主线停止等级
local sel_stop_level = 10
local stop_level_options = {"5级", "8级", "11级", "14级", "17级", "20级", "22级", "25级", "28级", "31级", "34级", "36级", "38级", "40级", "42级", "44级", "45级", "99级"}

-- 账号列表
local account_list = {}
local selected_account_idx = 1  -- 默认选中第一行（Lua数组1-based）
local right_clicked_idx = 1       -- 右键点击的行索引

-- 导入账号函数
local function import_accounts()
    local file_path = sys.get_cwd() .. "/账号.txt"
    local file_ansi = encoding.utf8_to_ansi(file_path)
    local f = io.open(file_ansi or file_path, "r")
    if f then
        account_list = {}
        for line in f:lines() do
            local trimmed = line:gsub("^\xEF\xBB\xBF", ""):match("^%s*(.-)%s*$")
            if trimmed ~= "" and not trimmed:match("^//") then
                local fields = {}
                for field in (trimmed .. "----"):gmatch("(.-)" .. string.rep("%-", 4)) do
                    fields[#fields + 1] = field:match("^%s*(.-)%s*$")
                end
                if #fields >= 2 then
                    -- ANSI 转 UTF-8（账号文件是ANSI编码，Lua脚本中映射表键名是UTF-8）
                    local function to_utf8(s)
                        if not s or s == "" then return s end
                        local ok, r = pcall(encoding.ansi_to_utf8, s)
                        return (ok and r) or s
                    end
                    table.insert(account_list, {
                        account = fields[1] or "",
                        password = fields[2] or "",
                        phone = fields[3] or "",
                        server = to_utf8(fields[4] or ""),
                        race   = to_utf8(fields[5] or ""),
                        job    = to_utf8(fields[6] or ""),
                    })
                end
            end
        end
        f:close()
        log.info(string.format("导入账号成功, 共 %d 个", #account_list))
    else
        log.warn("无法打开账号文件: " .. file_path)
    end
end

-- 二级密码
local second_password = ""

-- 多开数量
local multi_count = 1
local multi_count_text = "1"  -- 用于输入框显示的文本
local max_multi_count = 99  -- 从 key.txt 读取的最大多开数量

--- 从主目录 key.txt 读取卡密第3-4位，设为最大多开数量
-- N4 = 20，其他尝试解析为数字
local function load_max_multi_count()
    local key_path = sys.get_cwd() .. "/key.txt"
    local f = io.open(key_path, "r")
    if not f then
        log.warn("key.txt 不存在: " .. key_path .. ", 默认最大多开=1")
        max_multi_count = 1
        return
    end
    local content = f:read("*l") or ""
    f:close()
    content = content:match("^%s*(.-)%s*$") or ""
    if #content < 4 then
        log.warn("key.txt 内容太短, 默认最大多开=1")
        max_multi_count = 1
        return
    end
    local chars = content:sub(3, 4)
    if chars == "N4" then
        max_multi_count = 20
    else
        max_multi_count = tonumber(chars) or 1
    end
    log.info(string.format("key.txt 第3-4位: [%s] -> 最大多开: %d", chars, max_multi_count))
end
load_max_multi_count()

-- 路径配置
local game_path = ""
local purple_path = ""
local captcha_key = ""

-- 任务配置
local available_tasks = {"主线任务", "挂机打怪"}
local selected_tasks = {}

-- OTP
local otp_secret = ""
local otp_code = ""

--============================================================================
-- 配置保存和加载
--============================================================================

-- 在列表中查找值的索引（找不到返回1）
local function find_index(list, value)
    if not list or not value then return 1 end
    for i, v in ipairs(list) do
        if v == value then return i end
    end
    return 1
end

local peizhi_dir = "C:\\P"
local peizhi_ini_path = peizhi_dir .. "\\P.ini"

--- 获取指定线程的账号配置文件路径
local function account_config_path(idx)
    return peizhi_dir .. "\\C" .. idx .. ".ini"
end

--- 保存全局设置到 peizhi/peizhi.ini（仅全局页）
local function save_peizhi_config()
    ini.write(peizhi_ini_path, "全局设置", "多开数量", tostring(multi_count))
    ini.write(peizhi_ini_path, "全局设置", "紫P路径", purple_path)
    ini.write(peizhi_ini_path, "全局设置", "游戏路径", game_path)
    ini.write(peizhi_ini_path, "全局设置", "打码密钥", captcha_key)
    log.info("全局设置已保存到 C:\\P\\P.ini")
end

--- 从 peizhi/peizhi.ini 读取全局设置（仅全局页）
local function load_peizhi_config()
    multi_count = math.min(tonumber(ini.read(peizhi_ini_path, "全局设置", "多开数量") or "1") or 1, max_multi_count)
    multi_count_text = tostring(multi_count)
    game_path = ini.read(peizhi_ini_path, "全局设置", "游戏路径") or ""
    purple_path = ini.read(peizhi_ini_path, "全局设置", "紫P路径") or ""
    captcha_key = ini.read(peizhi_ini_path, "全局设置", "打码密钥") or ""
    log.info("全局设置已从 C:\\P\\P.ini 加载")
end

--- 保存指定线程的账号配置到 peizhi/config{idx}.ini
local function save_account_config(idx)
    if idx <= 0 then return end
    local fp = account_config_path(idx)
    -- 账号设置节（有序）
    ini.write_section(fp, "账号设置", {
        {"二级密码", second_password},
        {"otp密钥", otp_secret},
        {"停止等级", stop_level_options[sel_stop_level] or "31级"},
    })
    -- 挂机位置节（有序：先数量，再逐条属性）
    local coord_data = {{"挂机坐标数量", tostring(#guaji_coord_list)}}
    for i, item in ipairs(guaji_coord_list) do
        table.insert(coord_data, {"挂机坐标_" .. i .. "_生效", tostring(item.enabled)})
        table.insert(coord_data, {"挂机坐标_" .. i .. "_等级1", tostring(item.level1 or 1)})
        table.insert(coord_data, {"挂机坐标_" .. i .. "_等级2", tostring(item.level2 or 100)})
        table.insert(coord_data, {"挂机坐标_" .. i .. "_地图", item.map or ""})
        table.insert(coord_data, {"挂机坐标_" .. i .. "_坐标", item.coord or ""})
    end
    ini.write_section(fp, "挂机位置", coord_data)
    log.info("账号配置已保存到 C:\\P\\C" .. idx .. ".ini")
end

--- 从 peizhi/config{idx}.ini 加载指定线程的账号配置
local function load_account_config(idx)
    if idx <= 0 then return end
    local fp = account_config_path(idx)
    -- 账号设置
    second_password = ini.read(fp, "账号设置", "二级密码") or ""
        if second_password == "" then second_password = "66668888" end
    otp_secret = ini.read(fp, "账号设置", "otp密钥") or ""
    local stop_text = ini.read(fp, "账号设置", "停止等级") or ""
    if stop_text ~= "" then
        sel_stop_level = find_index(stop_level_options, stop_text) or 10
    end
    -- 挂机坐标表
    guaji_coord_list = {}
    local count = tonumber(ini.read(fp, "挂机位置", "挂机坐标数量") or "0") or 0
    for i = 1, count do
        local prefix = "挂机坐标_" .. i .. "_"
        local item = {
            enabled = (ini.read(fp, "挂机位置", prefix .. "生效") or "true") == "true",
            level1 = tonumber(ini.read(fp, "挂机位置", prefix .. "等级1") or "1") or 1,
            level2 = tonumber(ini.read(fp, "挂机位置", prefix .. "等级2") or "100") or 100,
            map = ini.read(fp, "挂机位置", prefix .. "地图") or "",
            coord = ini.read(fp, "挂机位置", prefix .. "坐标") or "",
        }
        table.insert(guaji_coord_list, item)
    end
    log.info("账号配置已从 C:\\P\\C" .. idx .. ".ini 加载")
end

local function save_config()
    save_peizhi_config()
    save_account_config(guaji_account_idx)
    log.info("设置已保存")
end

local function load_config()
    load_peizhi_config()
    log.info("设置已加载")
end

--============================================================================
-- 主窗口绘制
--============================================================================

local function draw_main_window()
    if not wnd_open then return end  -- 窗口已关闭则跳过绘制

    imgui.set_next_window_size(1000, 415, imgui.Cond_Always)  -- 设置窗口初始尺寸 1000x390
    imgui.set_next_window_pos(100, 100, imgui.Cond_FirstUseEver)  -- 设置窗口初始位置（仅首次）

    -- 创建主窗口（带关闭按钮，禁止调整大小）
    local visible, open = imgui.begin_window("Aui", true,
        imgui.WindowFlags_NoResize + imgui.WindowFlags_NoScrollbar)

    if not open then  -- 用户点了关闭按钮
        wnd_open = false  -- 标记窗口已关闭
        imgui.end_window()  -- 结束窗口绘制
        return
    end

    if visible then  -- 窗口可见时才绘制内容

        -- 动态计算表格高度：可用高度 - 底部组件预留
        local _, avail_h = imgui.get_content_region_avail()
        local table_h = math.max(100, avail_h - 120)  -- 预留120px给底部输入框+按钮

        -- 按比例计算编辑框高度（基于可用空间）
        local fp_y = math.max(4, math.floor((avail_h - table_h - 60) / 6))

        -- 账号列表表格（9 列，带边框和滚动条，高度动态）
                    if imgui.begin_table("AccountTable", 9, imgui.TableFlags_Borders + imgui.TableFlags_RowBg + imgui.TableFlags_ScrollY + 1 + 4194304, 0, table_h) then  -- +1=Resizable +4194304=ScrollX
                        imgui.table_setup_column("序号", imgui.TableColumnFlags_WidthFixed, 40)
                        imgui.table_setup_column("账号", imgui.TableColumnFlags_WidthFixed, 60)
                        imgui.table_setup_column("密码", imgui.TableColumnFlags_WidthFixed, 60)
                        imgui.table_setup_column("手机", imgui.TableColumnFlags_WidthFixed, 60)
                        imgui.table_setup_column("区服", imgui.TableColumnFlags_WidthFixed, 60)
                        imgui.table_setup_column("任务", imgui.TableColumnFlags_WidthFixed, 80)
                        imgui.table_setup_column("状态", imgui.TableColumnFlags_WidthFixed, 60)
                        imgui.table_setup_column("等级", imgui.TableColumnFlags_WidthFixed, 40)
                        imgui.table_setup_column("金币", imgui.TableColumnFlags_WidthFixed, 80)
                        imgui.table_headers_row()  -- 绘制表头

                        -- 遍历账号列表，绘制每一行
                        for i, acc in ipairs(account_list) do
                            imgui.push_id("acc_row_" .. i)  -- 推送唯一 ID（避免控件 ID 冲突）
                            imgui.table_next_row()  -- 新行
                            imgui.table_next_column()  -- 第 1 列：序号（两位数）
                            imgui.text(string.format("%02d", i))
                            -- 第 2 列：账号（SpanAllColumns 跨整行高亮 + AllowItemOverlap）
                            imgui.table_next_column()
                            if imgui.selectable(acc.account, selected_account_idx == i,
                                18) then  -- SpanAllColumns(2) + AllowItemOverlap(16) = 18
                                selected_account_idx = i  -- 更新选中索引
                                log.info("选中账号: " .. acc.account)
                            end
                            -- 右键检测（SpanAllColumns 覆盖整行，点任意列均可触发右键）
                            if imgui.is_item_hovered(imgui.HoveredFlags_AllowWhenBlockedByActiveItem) and imgui.is_mouse_clicked(1) then
                                right_clicked_idx = i
                                imgui.open_popup("##account_menu")
                            end
                            imgui.table_next_column()  -- 第 3 列：密码
                            imgui.text(acc.password)
                            imgui.table_next_column()  -- 第 4 列：手机
                            imgui.text(acc.phone or "")
                            imgui.table_next_column()  -- 第 5 列：区服
                            imgui.text(acc.server or "")
                            imgui.table_next_column()  -- 第 6 列：任务
                            imgui.text(sys.get_share("current_task_" .. i) or "")
                            imgui.table_next_column()  -- 第 7 列：状态（带颜色）
                            local status_text = sys.get_share("status_" .. i) or ""
                            if status_text == "封号" then
                                imgui.push_style_color(imgui.Col_Text, 1.0, 0.2, 0.2, 1.0)  -- 红色
                            elseif status_text == "完成" then
                                imgui.push_style_color(imgui.Col_Text, 0.2, 1.0, 0.2, 1.0)  -- 绿色
                            elseif status_text == "运行" then
                                imgui.push_style_color(imgui.Col_Text, 1.0, 0.8, 0.2, 1.0)  -- 橙色
                            end
                            imgui.text(status_text)
                            if status_text == "封号" or status_text == "完成" or status_text == "运行" then
                                imgui.pop_style_color()
                            end
                            imgui.table_next_column()  -- 第 8 列：等级
                            imgui.text(sys.get_share("level_" .. i) or "")
                            imgui.table_next_column()  -- 第 9 列：金币
                            imgui.text(sys.get_share("gold_" .. i) or "")

                            -- ====== 右键弹出菜单（与 push_id 同作用域）======
                            if imgui.begin_popup("##account_menu") then
                                local right_acc = account_list[right_clicked_idx]

                                -- 1. 启动选中
                                if imgui.selectable("启动选中##menu_start", false) then
                                    log.info(string.format("右键菜单 [启动选中] 账号: %s", right_acc and right_acc.account or ""))
                                end

                                -- 2. 停止选中
                                if imgui.selectable("停止选中##menu_stop", false) then
                                    log.info(string.format("右键菜单 [停止选中] 账号: %s", right_acc and right_acc.account or ""))
                                    sys.set_share("status_" .. right_clicked_idx, "")
                                end

                                -- 3. 游戏挂机设置
                                if imgui.selectable("游戏挂机设置##menu_guaji", false) then
                                    log.info(string.format("右键菜单 [游戏挂机设置] 账号: %s", right_acc and right_acc.account or ""))
                                    guaji_settings_open = true
                                    guaji_account_idx = right_clicked_idx
                                    load_account_config(guaji_account_idx)
                                end

                                -- 4. 重新导入账号
                                if imgui.selectable("重新导入账号##menu_reimport", false) then
                                    log.info(string.format("右键菜单 [重新导入账号]"))
                                    import_accounts()
                                end

                                -- 5. 重置金币记录
                                if imgui.selectable("重置金币记录##menu_reset_gold", false) then
                                    log.info(string.format("右键菜单 [重置金币记录] 账号: %s", right_acc and right_acc.account or ""))
                                    sys.set_share("gold_" .. right_clicked_idx, "")
                                end

                                -- 6. 关闭所有游戏
                                if imgui.selectable("关闭所有游戏##menu_close_all", false) then
                                    log.info(string.format("右键菜单 [关闭所有游戏]"))
                                end

                                imgui.end_popup()
                            end

                            imgui.pop_id()  -- 弹出 ID（与 push_id 配对）
                        end

                        imgui.end_table()  -- 结束账号表格
                    end

        -- 全局设置组件
        -- 第一行：多开数量 + 打码密钥
        imgui.push_style_var(imgui.StyleVar_FramePadding, 8, fp_y)
        imgui.text("多开数量:")
        imgui.same_line()
        imgui.set_next_item_width(400)
        local mc_changed, mc_text = imgui.input_text("##multi_count", multi_count_text)
        if mc_changed then
            multi_count_text = mc_text:gsub("%D", "")
            local num = tonumber(multi_count_text)
            if num then
                multi_count = math.max(1, math.min(num, max_multi_count))
                multi_count_text = tostring(multi_count)
            end
        end

        imgui.same_line()
        imgui.text("打码密钥:")
        imgui.same_line()
        imgui.set_next_item_width(400)
        local ck_changed, ck_val = imgui.input_text("##captcha_key", captcha_key)
        if ck_changed then captcha_key = ck_val end

        -- 第二行：紫P路径 + 游戏路径
        imgui.text("紫P路径:")
        imgui.same_line()
        imgui.set_next_item_width(400)
        local pp_changed, pp_val = imgui.input_text("##purple_path", purple_path)
        if pp_changed then purple_path = pp_val end

        imgui.same_line()
        imgui.text("游戏路径:")
        imgui.same_line()
        imgui.set_next_item_width(400)
        local gp_changed, gp_val = imgui.input_text("##game_path", game_path)
        if gp_changed then game_path = gp_val end
        imgui.pop_style_var()  -- 恢复 FramePadding

        -- 第三行：开启/停止脚本按钮
        if script_running then
            if imgui.button("停止脚本", 120, 40) then
                pending_stop = true
                log.info("点击了停止脚本")
            end
        else
            if imgui.button("开启脚本", 120, 40) then
                pending_start = true
                log.info("点击了开启脚本")
            end
        end

    end  -- closes if visible

    imgui.end_window()  -- 结束主窗口绘制

    -- ====== 游戏挂机设置窗口 ======
    if guaji_settings_open then
        imgui.set_next_window_size(1000, 600, imgui.Cond_Always)  -- 窗口尺寸 1000x600
        local gs_title = "游戏挂机设置 - 账号" .. guaji_account_idx
        local gs_visible, gs_open = imgui.begin_window(gs_title, true)  -- 创建窗口
        if not gs_open then
            guaji_settings_open = false  -- 关闭标记
            imgui.end_window()
            return
        end
        if gs_visible then
            -- ====== 标签栏（原生TabBar） ======
            if imgui.begin_tab_bar("GuajiTabBar") then
        
                -- ====== 标签页1：全局设置 ======
                if imgui.begin_tab_item("全局设置") then
                    
                    imgui.end_tab_item()
                end

                -- ====== 标签页2：账号设置 ======
                if imgui.begin_tab_item("账号设置") then

            -- 二级密码 + otp密钥（同行）
            imgui.text("二级密码:")
            imgui.same_line()
            imgui.set_next_item_width(100)
            local pwd_changed, pwd_val = imgui.input_text("##second_pwd", second_password)
            if pwd_changed then second_password = pwd_val end
            imgui.same_line()
            imgui.text("otp密钥:")
            imgui.same_line()
            imgui.set_next_item_width(100)
            local otp_changed, otp_val = imgui.input_text("##otp_secret", otp_secret)
            if otp_changed then otp_secret = otp_val end
            imgui.same_line()
            if imgui.button("生成", 50, 25) then
                if otp_secret ~= "" then
                    local ok, result = pcall(api.GenOTP, otp_secret)
                    if ok and result then
                        otp_code = result
                        log.info("OTP 生成: " .. result)
                    else
                        log.warn("OTP 生成失败")
                    end
                else
                    log.warn("请输入 OTP 密钥")
                end
            end
            if otp_code ~= "" then
                imgui.same_line()
                imgui.text("OTP: " .. otp_code)
            end

            -- 主线等级
            imgui.text("主线等级:")
            imgui.same_line()
            imgui.set_next_item_width(100)
            local sl_changed, sl_idx = imgui.combo("##stop_level", sel_stop_level, stop_level_options)
            if sl_changed then
                sel_stop_level = sl_idx
                log.info("主线等级: " .. stop_level_options[sel_stop_level])
            end

                    imgui.end_tab_item()
                end
        
                -- ====== 标签页3：任务设置 ======
                if imgui.begin_tab_item("任务设置") then
        
            if imgui.begin_table("TaskTable", 2, imgui.TableFlags_Borders, 0, 0) then
                imgui.table_setup_column("可选任务", imgui.TableColumnFlags_WidthFixed, 150)
                imgui.table_setup_column("已选任务", imgui.TableColumnFlags_WidthFixed, 150)
                imgui.table_headers_row()
                imgui.table_next_row()

                -- 左列：可选任务
                imgui.table_next_column()
                imgui.begin_child("AvailTaskScroll", 0, 200, true)
                for i, task_name in ipairs(available_tasks) do
                    local task_id = string.format("avail_%d", i)
                    imgui.selectable(task_name .. "##" .. task_id, false,
                        imgui.SelectableFlags_AllowDoubleClick)
                    if imgui.is_item_hovered() and imgui.is_mouse_double_clicked(0) then
                        local already_added = false
                        for _, st in ipairs(selected_tasks) do
                            if st == task_name then already_added = true; break end
                        end
                        if not already_added then
                            table.insert(selected_tasks, task_name)
                            log.info("添加任务: " .. task_name)
                        end
                    end
                end
                imgui.end_child()

                -- 右列：已选任务
                imgui.table_next_column()
                imgui.begin_child("SelTaskScroll", 0, 200, true)
                local remove_idx = nil
                for i, task_name in ipairs(selected_tasks) do
                    local task_id = string.format("sel_%d", i)
                    imgui.selectable(task_name .. "##" .. task_id, false,
                        imgui.SelectableFlags_AllowDoubleClick)
                    if imgui.is_item_hovered() and imgui.is_mouse_double_clicked(0) then
                        remove_idx = i
                        log.info("移除任务: " .. task_name)
                    end
                end
                if remove_idx then
                    table.remove(selected_tasks, remove_idx)
                end
                imgui.end_child()

                imgui.end_table()
            end

                    imgui.end_tab_item()
                end
        
                -- ====== 标签页4：物品补给 ======
                if imgui.begin_tab_item("物品补给") then
                    imgui.text("物品补给 - 待实现")
                    imgui.end_tab_item()
                end
        
                -- ====== 标签页5：挂机位置 ======
                if imgui.begin_tab_item("挂机位置") then
        
            imgui.separator()

            -- 初始化游戏连接（从 configN.ini 读取 PID）
            local function init_game_with_pid()
                local config_path = string.format("C:\\P\\C%d.ini", guaji_account_idx)
                local saved_pid = ini.read(config_path, "shuju", "pid")
                if saved_pid and saved_pid ~= "" and saved_pid ~= "0" then
                    local pid_num = tonumber(saved_pid)
                    if pid_num then
                        log.info(string.format("使用 PID=%d 初始化游戏连接", pid_num))
                        return api.InitGameinfo(pid_num)
                    end
                end
                coord_text = "PID无效"
                return false
            end

            local gc_flags = imgui.TableFlags_Borders + imgui.TableFlags_RowBg + imgui.TableFlags_Resizable + imgui.TableFlags_ScrollY
            if imgui.begin_table("GuajiCoordTable", 5, gc_flags, 0, 200) then
                imgui.table_setup_column("生效", imgui.TableColumnFlags_WidthFixed, 50)
                imgui.table_setup_column("等级1", imgui.TableColumnFlags_WidthFixed, 60)
                imgui.table_setup_column("等级2", imgui.TableColumnFlags_WidthFixed, 60)
                imgui.table_setup_column("地图", imgui.TableColumnFlags_WidthFixed, 200)
                imgui.table_setup_column("坐标", imgui.TableColumnFlags_WidthStretch)
                imgui.table_headers_row()
        
                for i, item in ipairs(guaji_coord_list) do
                    imgui.push_id(i)
                    imgui.table_next_row()
        
                    imgui.table_next_column()
                    local en_changed, en_val = imgui.checkbox("##en", item.enabled)
                    if en_changed then item.enabled = en_val end
                    -- 点击复选框也选中行
                    if imgui.is_item_clicked(imgui.MouseButton_Left) then
                        guaji_coord_popup_idx = i
                    end
        
                    imgui.table_next_column()
                    imgui.set_next_item_width(-1)
                    local l1c, l1v = imgui.input_text("##l1", tostring(item.level1 or 1), 8)
                    if l1c then local n = tonumber(l1v) if n then item.level1 = n end end
                    if imgui.is_item_clicked(imgui.MouseButton_Left) then guaji_coord_popup_idx = i end
        
                    imgui.table_next_column()
                    imgui.set_next_item_width(-1)
                    local l2c, l2v = imgui.input_text("##l2", tostring(item.level2 or 100), 8)
                    if l2c then local n = tonumber(l2v) if n then item.level2 = n end end
                    if imgui.is_item_clicked(imgui.MouseButton_Left) then guaji_coord_popup_idx = i end
        
                    imgui.table_next_column()
                    imgui.set_next_item_width(-1)
                    local mc, mv = imgui.input_text("##map", item.map or "", 256)
                    if mc then item.map = mv end
                    if imgui.is_item_clicked(imgui.MouseButton_Left) then guaji_coord_popup_idx = i end
        
                    imgui.table_next_column()
                    imgui.set_next_item_width(-1)
                    local cc, cv = imgui.input_text("##coord", item.coord or "", 65536)
                    if cc then item.coord = cv end
        
                    -- 鼠标悬停检测（用于右键菜单行定位）
                    if imgui.is_item_hovered() then
                        guaji_coord_hover = i
                    end
                    -- 左键点击选中行
                    if imgui.is_item_clicked(imgui.MouseButton_Left) then
                        guaji_coord_popup_idx = i
                    end
                    imgui.pop_id()
                end
                imgui.end_table()

                -- 右键弹出菜单
                if imgui.is_window_hovered() and imgui.is_mouse_clicked(imgui.MouseButton_Right) then
                    guaji_coord_popup_idx = guaji_coord_hover or 0
                    imgui.open_popup("##gc_popup")
                end
                local hover_any = guaji_coord_hover
                guaji_coord_hover = nil
        
                if imgui.begin_popup("##gc_popup") then
                    if imgui.selectable("新增", false) then
                        local pos = guaji_coord_popup_idx > 0 and (guaji_coord_popup_idx + 1) or (#guaji_coord_list + 1)
                        table.insert(guaji_coord_list, pos, { enabled = true, level1 = 1, level2 = 100, map = "", coord = "" })
                    end
                    if guaji_coord_popup_idx > 0 and #guaji_coord_list > 0 and imgui.selectable("删除", false) then
                        table.remove(guaji_coord_list, guaji_coord_popup_idx)
                    end
                    if guaji_coord_popup_idx > 0 and imgui.selectable("在此处添加挂机坐标", false) then
                        if init_game_with_pid() then
                            local ch = api.GetCharacter()
                            if ch then
                                local cx, cy, cz = tonumber(ch.x), tonumber(ch.y), tonumber(ch.z)
                                if cx and cy and cz then
                                    guaji_coord_list[guaji_coord_popup_idx].coord = string.format("%.2f,%.2f,%.2f", cx, cy, cz)
                                    log.info("挂机坐标已添加: " .. guaji_coord_list[guaji_coord_popup_idx].coord)
                                end
                            end
                        end
                    end
                    imgui.end_popup()
                end
            end
        
            -- 底部新增/删除按钮
            if imgui.button("新增行", 80, 25) then
                table.insert(guaji_coord_list, { enabled = true, level1 = 1, level2 = 100, map = "", coord = "" })
            end
            imgui.same_line()
            if imgui.button("删除行", 80, 25) then
                if guaji_coord_popup_idx > 0 and #guaji_coord_list > 0 then
                    table.remove(guaji_coord_list, guaji_coord_popup_idx)
                    guaji_coord_popup_idx = 0
                end
            end
        
            imgui.separator()
        
            -- 坐标编辑框 + 按钮
            if imgui.begin_table("CoordEditLayout", 2, 0) then
                imgui.table_setup_column("Editor", imgui.TableColumnFlags_WidthStretch)
                imgui.table_setup_column("Btn", imgui.TableColumnFlags_WidthFixed, 110)
                imgui.table_next_row()
                imgui.table_next_column()
                imgui.set_next_item_width(-1)
                local coord_changed, coord_val = imgui.input_text_multiline("##coord", coord_text, -1, 85)
                if coord_changed then coord_text = coord_val end
                imgui.table_next_column()

                -- 获取当前坐标（单次）
                if imgui.button("获取当前坐标", 100, 25) then
                    if init_game_with_pid() then
                        local ch = api.GetCharacter()
                        if ch then
                            local cx, cy, cz = tonumber(ch.x), tonumber(ch.y), tonumber(ch.z)
                            if cx and cy and cz then
                                local new_coord = string.format("{ x = %.2f, y = %.2f, z = %.2f }", cx, cy, cz)
                                if coord_text ~= "" then
                                    coord_text = coord_text .. "\n" .. new_coord
                                else
                                    coord_text = new_coord
                                end
                                log.info("获取坐标: " .. new_coord)
                            end
                        end
                    end
                end
                -- 循环获取坐标 / 停止录制
                if coord_recording then
                    if imgui.button("停止录制", 100, 25) then
                        coord_recording = false
                        log.info("坐标录制已停止")
                    end
                else
                    if imgui.button("循环获取坐标", 100, 25) then
                        if init_game_with_pid() then
                            local ch = api.GetCharacter()
                            if ch then
                                local cx, cy, cz = tonumber(ch.x), tonumber(ch.y), tonumber(ch.z)
                                if cx and cy and cz then
                                    local new_coord = string.format("{ x = %.2f, y = %.2f, z = %.2f }", cx, cy, cz)
                                    if coord_text ~= "" then
                                        coord_text = coord_text .. "\n" .. new_coord
                                    else
                                        coord_text = new_coord
                                    end
                                    log.info("读取坐标: " .. new_coord)
                                    last_record_x = cx
                                    last_record_y = cy
                                    coord_recording = true
                                    log.info("坐标录制已开始 (移动距离>3 时自动记录)")
                                end
                            end
                        end
                    end
                end
                -- 获取地图信息
                if imgui.button("获取地图信息", 100, 25) then
                    if init_game_with_pid() then
                        local big_id = api.GetBigMapId()
                        local cur_map = api.GetCurrentMap()
                        local map_idx = cur_map and cur_map.index or "?"
                        local ch = api.GetCharacter()
                        local nodes = api.GetMapNodeList(big_id) or {}
                        local best_id = "?"
                        local cx, cy, cz = tonumber(ch.x), tonumber(ch.y), tonumber(ch.z)
                        if ch and cx and cy and cz and #nodes > 0 then
                            local best_dist = math.huge
                            for _, n in ipairs(nodes) do
                                local dx = (tonumber(n.x) or 0) - cx
                                local dy = (tonumber(n.y) or 0) - cy
                                local dz = (tonumber(n.z) or 0) - cz
                                local d = math.sqrt(dx*dx + dy*dy + dz*dz)
                                if d < best_dist then
                                    best_dist = d
                                    best_id = n.node_id
                                end
                            end
                        end
                        local result = string.format("%s|%s|%s", tostring(big_id), tostring(map_idx), tostring(best_id))
                        coord_text = result
                        log.info("地图信息: " .. result)
                    end
                end
                imgui.end_table()
            end
        
                    imgui.end_tab_item()
                end
        
                imgui.end_tab_bar()
            end
        
            -- 保存设置按钮（固定在左下角，距左5距底5）
            local _, cy = imgui.get_cursor_pos()
            local _, ch = imgui.get_content_region_avail()
            imgui.set_cursor_pos(5, cy + ch - 40)
            if imgui.button("保存设置", 120, 40) then
                save_config()
            end
        
        end  -- closes if gs_visible
        imgui.end_window()  -- 结束挂机设置窗口
    end
end

--============================================================================
-- 共享变量写入（UI → 业务脚本）
--============================================================================

--- 将 UI 所有选中值写入共享变量，供 main.lua / B.lua 读取
local function write_share_vars()
    -- 二级密码
    sys.set_share("second_password",  tostring(second_password))

    -- 路径配置
    sys.set_share("game_path",        game_path)
    sys.set_share("purple_path",      purple_path)
    sys.set_share("captcha_key",      captcha_key)

    -- 多开数量
    sys.set_share("multi_count",      multi_count)

    log.info("共享变量写入完成")
end

--============================================================================
-- 主渲染回调
--============================================================================

local function on_render()
    draw_main_window()
end

--============================================================================
-- 入口
--============================================================================
log.info("A1.lua 启动")
local ok, err = pcall(import_accounts)
if not ok then log.error("import_accounts 失败: " .. tostring(err)) end
ok, err = pcall(load_config)
if not ok then log.error("load_config 失败: " .. tostring(err)) end

-- 清空等级、金币、当前任务、状态列（不读入上次残留数据）
if account_list then
    for i = 1, #account_list do
        sys.set_share("level_" .. i, "")
        sys.set_share("gold_" .. i, "")
        sys.set_share("current_task_" .. i, "")
        sys.set_share("status_" .. i, "")
    end
end

imgui.style_colors_light()  -- 亮色主题
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
end

-- 在独立线程中加载驱动 (不影响 UI 线程)
-- 驱动已加载则跳过，避免重复注入损坏内存
if driver.is_loaded() then
    log.info("驱动已加载, 跳过 A5.lua")
else
    local driver_flag_path = sys.get_cwd() .. "/拒绝加载驱动.txt"
    local driver_flag_path_ansi = encoding.utf8_to_ansi(driver_flag_path)
    local skip_driver = io.open(driver_flag_path_ansi or driver_flag_path, "r")
    log.info(string.format("检查驱动开关: 存在=%s", tostring(skip_driver ~= nil)))
    if skip_driver then
        skip_driver:close()
        log.info("检测到 拒绝加载驱动.txt，跳过驱动加载")
    else
        task.run("scripts/A5.lua")
    end
end

-- 保持脚本运行
hotkey.start()
while true do
    if hotkey.is_pressed(0x11) and hotkey.is_pressed(0x7B) then
        log.info("Ctrl+F12 退出")
        hotkey.stop()
        break
    end

    -- 窗口关闭时退出
    if not wnd_open then
        log.info("窗口已关闭，退出脚本")
        hotkey.stop()
        break
    end

    -- 处理延迟启动
    if pending_start then
        pending_start = false
        save_peizhi_config()
        write_share_vars()
        script_running = true
        script_task_id = task.run("scripts/A2.lua")
        if script_task_id then
            log.info(string.format("脚本已启动, ID: %s", tostring(script_task_id)))
        else
            log.error("脚本启动失败")
            script_running = false
        end
    end

    -- 处理延迟停止
    if pending_stop then
        pending_stop = false

        -- 停止所有子任务（排除自身）
        local my_id = task.id()
        local list = task.list()
        if list then
            for _, item in ipairs(list) do
                if item.id ~= my_id and item.status == "running" then
                    pcall(task.stop, item.id)
                    log.info(string.format("已停止: %s (ID: %d)", item.name or "", item.id))
                end
            end
        end

        script_task_id = nil
        script_running = false

        -- 清空状态列和当前任务列
        for i = 1, #account_list do
            sys.set_share("status_" .. i, "")
            sys.set_share("current_task_" .. i, "")
        end

        log.info("所有脚本已停止")
    end

    -- 坐标录制循环：角色移动距离>3时自动记录坐标
    if coord_recording then
        local ch = api.GetCharacter()
        if ch then
            local cx, cy, cz = tonumber(ch.x), tonumber(ch.y), tonumber(ch.z)
            if cx and cy and cz then
                local dx = cx - last_record_x
                local dy = cy - last_record_y
                local dist = math.sqrt(dx * dx + dy * dy)
                if dist > 3 then
                    local new_coord = string.format("{ x = %.2f, y = %.2f, z = %.2f }", cx, cy, cz)
                    coord_text = coord_text .. "\n" .. new_coord
                    last_record_x = cx
                    last_record_y = cy
                    log.info("自动记录坐标: " .. new_coord)
                end
            end
        end
    end

    sys.sleep(10)
end
