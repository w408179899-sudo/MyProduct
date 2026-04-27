--[[
    ImGui Demo 风格测试脚本
    
    展示 AetherEngine imgui 模块的全部控件和功能
    
    使用方法:
    1. 编辑器模式: AetherRunner.exe --gui，然后运行此脚本
    2. 命令行模式: AetherRunner.exe scripts/test_ui.lua
]]

--============================================================================
-- 状态变量
--============================================================================

local frame_count = 0

-- 演示窗口开关
local show_demo_window = true
local show_overlay = true
local show_style_editor = false
local show_metrics = false

-- 基础控件状态
local state = {
    -- 基础
    counter = 0,
    checkbox = true,
    radio_value = 1,
    
    -- 文本输入
    text_input = "Hello, 世界!",
    text_multiline = "这是多行文本\n可以换行\n支持中文",
    int_input = 42,
    float_input = 3.14,
    
    -- 滑块和拖拽
    slider_int = 50,
    slider_float = 0.5,
    drag_int = 100,
    drag_float = 1.0,
    
    -- 下拉和列表
    combo_index = 1,
    listbox_index = 1,
    
    -- 颜色
    color3 = {0.4, 0.7, 0.2},
    color4 = {1.0, 0.5, 0.2, 1.0},
    
    -- 树节点
    tree_selection = 1,
    
    -- 标签页
    tab_index = 1,
    
    -- 表格
    table_data = {
        {name = "Alice", age = 25, score = 95},
        {name = "Bob", age = 30, score = 87},
        {name = "Charlie", age = 22, score = 91},
        {name = "Diana", age = 28, score = 88},
    },
}

-- 颜色定义
local COLORS = {}

local function init_colors()
    COLORS.red = imgui.color(255, 0, 0, 255)
    COLORS.green = imgui.color(0, 255, 0, 255)
    COLORS.blue = imgui.color(0, 0, 255, 255)
    COLORS.yellow = imgui.color(255, 255, 0, 255)
    COLORS.white = imgui.color(255, 255, 255, 255)
    COLORS.cyan = imgui.color(0, 255, 255, 255)
    COLORS.magenta = imgui.color(255, 0, 255, 255)
    COLORS.orange = imgui.color(255, 165, 0, 255)
end

--============================================================================
-- 辅助函数
--============================================================================

local function help_marker(desc)
    imgui.text_disabled("(?)")
    if imgui.is_item_hovered() then
        imgui.begin_tooltip()
        imgui.text(desc)
        imgui.end_tooltip()
    end
end

--============================================================================
-- 覆盖层绘制
--============================================================================

local function draw_overlay()
    local mx, my = imgui.get_mouse_pos()
    if mx < 0 then mx = 0 end
    if my < 0 then my = 0 end
    
    -- 十字准星
    imgui.add_line(mx - 15, my, mx + 15, my, COLORS.green, 2)
    imgui.add_line(mx, my - 15, mx, my + 15, COLORS.green, 2)
    imgui.add_circle(mx, my, 20, COLORS.green, 32, 1)
    
    -- 信息面板
    local info_x, info_y = 10, 10
    imgui.add_rect_filled(info_x - 5, info_y - 5, info_x + 250, info_y + 70, imgui.color(0, 0, 0, 180), 5)
    imgui.add_text(info_x, info_y, "AetherEngine ImGui Demo", COLORS.cyan)
    imgui.add_text(info_x, info_y + 20, string.format("鼠标: %.0f, %.0f", mx, my), COLORS.yellow)
    imgui.add_text(info_x, info_y + 40, string.format("帧: %d  FPS: %.1f", frame_count, 1.0 / imgui.get_delta_time()), COLORS.green)
    
    -- 图形示例
    local gx, gy = 10, 100
    imgui.add_rect(gx, gy, gx + 60, gy + 40, COLORS.red, 5, 2)
    imgui.add_rect_filled(gx + 70, gy, gx + 130, gy + 40, COLORS.blue, 5)
    imgui.add_circle(gx + 165, gy + 20, 20, COLORS.yellow, 0, 2)
    imgui.add_circle_filled(gx + 220, gy + 20, 20, COLORS.magenta, 0)
    imgui.add_triangle(gx + 255, gy + 40, gx + 280, gy, gx + 305, gy + 40, COLORS.cyan, 2)
end

--============================================================================
-- 基础控件演示
--============================================================================

local function draw_basic_widgets()
    if imgui.collapsing_header("基础控件", imgui.TreeNodeFlags_DefaultOpen) then
        -- 文本
        imgui.text("普通文本")
        imgui.text_colored(0.4, 0.8, 0.4, 1.0, "绿色文本")
        imgui.text_disabled("禁用文本 (灰色)")
        imgui.text_wrapped("这是一段很长的文本，会自动换行显示。ImGui 支持各种文本显示方式，包括普通文本、彩色文本、禁用文本和自动换行文本。")
        
        imgui.bullet_text("项目符号文本 1")
        imgui.bullet_text("项目符号文本 2")
        
        imgui.separator()
        
        -- 按钮
        if imgui.button("普通按钮") then
            state.counter = state.counter + 1
        end
        imgui.same_line()
        imgui.text(string.format("计数: %d", state.counter))
        
        imgui.same_line()
        if imgui.small_button("小按钮") then
            state.counter = 0
        end
        
        -- 箭头按钮
        imgui.arrow_button("##left", imgui.Dir_Left)
        imgui.same_line()
        imgui.arrow_button("##right", imgui.Dir_Right)
        imgui.same_line()
        imgui.arrow_button("##up", imgui.Dir_Up)
        imgui.same_line()
        imgui.arrow_button("##down", imgui.Dir_Down)
        
        imgui.separator()
        
        -- 复选框
        local changed, val = imgui.checkbox("复选框", state.checkbox)
        if changed then state.checkbox = val end
        
        -- 单选按钮
        if imgui.radio_button("选项 A", state.radio_value == 1) then
            state.radio_value = 1
        end
        imgui.same_line()
        if imgui.radio_button("选项 B", state.radio_value == 2) then
            state.radio_value = 2
        end
        imgui.same_line()
        if imgui.radio_button("选项 C", state.radio_value == 3) then
            state.radio_value = 3
        end
    end
end

--============================================================================
-- 输入控件演示
--============================================================================

local function draw_input_widgets()
    if imgui.collapsing_header("输入控件") then
        -- 文本输入
        local changed, val = imgui.input_text("文本输入", state.text_input)
        if changed then state.text_input = val end
        
        changed, val = imgui.input_text_multiline("##multiline", state.text_multiline, 300, 80)
        if changed then state.text_multiline = val end
        
        changed, val = imgui.input_int("整数输入", state.int_input)
        if changed then state.int_input = val end
        
        changed, val = imgui.input_float("浮点输入", state.float_input)
        if changed then state.float_input = val end
        
        imgui.separator()
        
        -- 滑块
        changed, val = imgui.slider_int("整数滑块", state.slider_int, 0, 100)
        if changed then state.slider_int = val end
        
        changed, val = imgui.slider_float("浮点滑块", state.slider_float, 0.0, 1.0)
        if changed then state.slider_float = val end
        
        imgui.separator()
        
        -- 拖拽
        changed, val = imgui.drag_int("整数拖拽", state.drag_int, 1, 0, 1000)
        if changed then state.drag_int = val end
        
        changed, val = imgui.drag_float("浮点拖拽", state.drag_float, 0.01, 0.0, 10.0)
        if changed then state.drag_float = val end
        
        imgui.separator()
        
        -- 进度条
        imgui.text("进度条:")
        imgui.progress_bar(state.slider_float, -1, 0, string.format("%.1f%%", state.slider_float * 100))
    end
end

--============================================================================
-- 选择控件演示
--============================================================================

local function draw_selection_widgets()
    if imgui.collapsing_header("选择控件") then
        local items = {"苹果", "香蕉", "橙子", "葡萄", "西瓜"}
        
        -- 下拉框
        local changed, val = imgui.combo("下拉框", state.combo_index, items)
        if changed then state.combo_index = val end
        
        imgui.separator()
        
        -- 列表框
        changed, val = imgui.list_box("列表框", state.listbox_index, items, 4)
        if changed then state.listbox_index = val end
        
        imgui.separator()
        
        -- 可选择项
        imgui.text("可选择项:")
        for i, item in ipairs(items) do
            if imgui.selectable(item, state.listbox_index == i) then
                state.listbox_index = i
            end
        end
    end
end

--============================================================================
-- 颜色控件演示
--============================================================================

local function draw_color_widgets()
    if imgui.collapsing_header("颜色控件") then
        -- RGB 颜色编辑器
        local changed, r, g, b = imgui.color_edit3("RGB 颜色", 
            state.color3[1], state.color3[2], state.color3[3])
        if changed then
            state.color3 = {r, g, b}
        end
        
        -- RGBA 颜色编辑器
        local a
        changed, r, g, b, a = imgui.color_edit4("RGBA 颜色",
            state.color4[1], state.color4[2], state.color4[3], state.color4[4])
        if changed then
            state.color4 = {r, g, b, a}
        end
        
        imgui.separator()
        
        -- 颜色按钮
        imgui.text("颜色按钮:")
        imgui.color_button("##red", 1, 0, 0, 1, 30, 30)
        imgui.same_line()
        imgui.color_button("##green", 0, 1, 0, 1, 30, 30)
        imgui.same_line()
        imgui.color_button("##blue", 0, 0, 1, 1, 30, 30)
        imgui.same_line()
        imgui.color_button("##custom", state.color4[1], state.color4[2], state.color4[3], state.color4[4], 30, 30)
    end
end

--============================================================================
-- 树节点演示
--============================================================================

local function draw_tree_widgets()
    if imgui.collapsing_header("树节点") then
        if imgui.tree_node("基础树节点") then
            imgui.text("树节点内容")
            imgui.bullet_text("子项 1")
            imgui.bullet_text("子项 2")
            
            if imgui.tree_node("嵌套节点") then
                imgui.text("嵌套内容")
                imgui.tree_pop()
            end
            
            imgui.tree_pop()
        end
        
        -- 带标志的树节点
        if imgui.tree_node_ex("高级树节点", imgui.TreeNodeFlags_Framed) then
            for i = 1, 3 do
                local flags = imgui.TreeNodeFlags_Leaf + imgui.TreeNodeFlags_Bullet
                if state.tree_selection == i then
                    flags = flags + imgui.TreeNodeFlags_Selected
                end
                
                if imgui.tree_node_ex("叶子节点 " .. i, flags) then
                    if imgui.is_item_clicked() then
                        state.tree_selection = i
                    end
                    imgui.tree_pop()
                end
            end
            imgui.tree_pop()
        end
    end
end

--============================================================================
-- 标签页演示
--============================================================================

local function draw_tab_widgets()
    if imgui.collapsing_header("标签页") then
        if imgui.begin_tab_bar("DemoTabBar") then
            if imgui.begin_tab_item("标签 1") then
                imgui.text("这是标签页 1 的内容")
                imgui.text("可以放置任意控件")
                if imgui.button("标签1按钮") then
                    log.info("标签1按钮被点击")
                end
                imgui.end_tab_item()
            end
            
            if imgui.begin_tab_item("标签 2") then
                imgui.text("这是标签页 2 的内容")
                local changed, val = imgui.slider_int("标签2滑块", state.slider_int, 0, 100)
                if changed then state.slider_int = val end
                imgui.end_tab_item()
            end
            
            if imgui.begin_tab_item("标签 3") then
                imgui.text("这是标签页 3 的内容")
                imgui.text_wrapped("标签页非常适合组织复杂的界面，将相关功能分组显示。")
                imgui.end_tab_item()
            end
            
            imgui.end_tab_bar()
        end
    end
end

--============================================================================
-- 表格演示
--============================================================================

local function draw_table_widgets()
    if imgui.collapsing_header("表格") then
        local flags = imgui.TableFlags_Borders + imgui.TableFlags_RowBg + imgui.TableFlags_Resizable
        
        if imgui.begin_table("DemoTable", 3, flags) then
            -- 设置列
            imgui.table_setup_column("姓名", imgui.TableColumnFlags_WidthFixed, 100)
            imgui.table_setup_column("年龄", imgui.TableColumnFlags_WidthFixed, 60)
            imgui.table_setup_column("分数", imgui.TableColumnFlags_WidthStretch)
            imgui.table_headers_row()
            
            -- 填充数据
            for _, row in ipairs(state.table_data) do
                imgui.table_next_row()
                
                imgui.table_next_column()
                imgui.text(row.name)
                
                imgui.table_next_column()
                imgui.text(tostring(row.age))
                
                imgui.table_next_column()
                imgui.progress_bar(row.score / 100, -1, 0, tostring(row.score))
            end
            
            imgui.end_table()
        end
    end
end

--============================================================================
-- 布局演示
--============================================================================

local function draw_layout_widgets()
    if imgui.collapsing_header("布局") then
        -- 分组
        imgui.begin_group()
        imgui.text("分组 1")
        imgui.button("按钮 A", 80, 0)
        imgui.button("按钮 B", 80, 0)
        imgui.end_group()
        
        imgui.same_line()
        
        imgui.begin_group()
        imgui.text("分组 2")
        imgui.button("按钮 C", 80, 0)
        imgui.button("按钮 D", 80, 0)
        imgui.end_group()
        
        imgui.separator()
        
        -- 禁用区域
        imgui.begin_disabled(not state.checkbox)
        imgui.text("这部分在复选框未选中时被禁用")
        imgui.button("禁用按钮")
        imgui.slider_int("禁用滑块", 50, 0, 100)
        imgui.end_disabled()
        
        imgui.separator()
        
        -- 缩进
        imgui.text("无缩进")
        imgui.indent()
        imgui.text("缩进 1 级")
        imgui.indent()
        imgui.text("缩进 2 级")
        imgui.unindent()
        imgui.unindent()
        
        imgui.separator()
        
        -- 内容区域信息
        local w, h = imgui.get_content_region_avail()
        imgui.text(string.format("可用区域: %.0f x %.0f", w, h))
    end
end

--============================================================================
-- 控件宽度控制演示
--============================================================================

local function draw_width_control()
    if imgui.collapsing_header("控件宽度控制") then
        imgui.text_wrapped("默认情况下，输入框/滑块等控件会撑满窗口宽度。使用宽度控制 API 可以固定控件大小。")
        imgui.separator()
        
        -- set_next_item_width: 仅影响下一个控件
        imgui.text("set_next_item_width (仅影响下一个控件):")
        imgui.set_next_item_width(120)
        local changed, val = imgui.input_int("120px 宽##w1", state.int_input)
        if changed then state.int_input = val end
        
        imgui.set_next_item_width(200)
        changed, val = imgui.slider_int("200px 宽##w2", state.slider_int, 0, 100)
        if changed then state.slider_int = val end
        
        imgui.set_next_item_width(250)
        changed, val = imgui.input_text("250px 宽##w3", state.text_input)
        if changed then state.text_input = val end
        
        imgui.separator()
        
        -- push/pop_item_width: 批量控制
        imgui.text("push_item_width / pop_item_width (批量控制):")
        imgui.push_item_width(150)
        changed, val = imgui.slider_float("滑块A##pw1", state.slider_float, 0.0, 1.0)
        if changed then state.slider_float = val end
        changed, val = imgui.drag_float("拖拽A##pw2", state.drag_float, 0.01, 0.0, 10.0)
        if changed then state.drag_float = val end
        changed, val = imgui.input_float("输入A##pw3", state.float_input)
        if changed then state.float_input = val end
        imgui.pop_item_width()
        
        imgui.separator()
        
        -- 负数宽度：距右边距的偏移
        imgui.text("负数宽度 (距窗口右边的偏移):")
        imgui.set_next_item_width(-100)
        changed, val = imgui.slider_int("右留100px##nw", state.slider_int, 0, 100)
        if changed then state.slider_int = val end
        
        imgui.separator()
        
        -- calc_item_width
        local cur_width = imgui.calc_item_width()
        imgui.text(string.format("当前控件宽度: %.0f px", cur_width))
    end
end

--============================================================================
-- 弹出窗口演示
--============================================================================

local function draw_popup_widgets()
    if imgui.collapsing_header("弹出窗口和提示") then
        -- 工具提示
        imgui.button("悬停查看提示")
        if imgui.is_item_hovered() then
            imgui.set_tooltip("这是一个工具提示!")
        end
        
        imgui.same_line()
        help_marker("这是帮助标记的示例")
        
        imgui.separator()
        
        -- 弹出窗口
        if imgui.button("打开弹出窗口") then
            imgui.open_popup("DemoPopup")
        end
        
        if imgui.begin_popup("DemoPopup") then
            imgui.text("弹出窗口内容")
            imgui.separator()
            if imgui.button("关闭") then
                imgui.close_popup()
            end
            imgui.end_popup()
        end
    end
end

--============================================================================
-- 项目状态演示
--============================================================================

local function draw_item_status()
    if imgui.collapsing_header("项目状态检测") then
        imgui.button("测试按钮 (检查状态)")
        
        imgui.text("is_item_hovered: " .. tostring(imgui.is_item_hovered()))
        imgui.text("is_item_active: " .. tostring(imgui.is_item_active()))
        imgui.text("is_item_focused: " .. tostring(imgui.is_item_focused()))
        imgui.text("is_item_clicked: " .. tostring(imgui.is_item_clicked()))
        
        local minx, miny = imgui.get_item_rect_min()
        local maxx, maxy = imgui.get_item_rect_max()
        local sizex, sizey = imgui.get_item_rect_size()
        imgui.text(string.format("Rect: (%.0f,%.0f) - (%.0f,%.0f)  Size: %.0fx%.0f", 
            minx, miny, maxx, maxy, sizex, sizey))
    end
end

--============================================================================
-- 主演示窗口
--============================================================================

local demo_open = true

local function draw_demo_window()
    if not demo_open then return end
    
    imgui.set_next_window_size(450, 600, imgui.Cond_FirstUseEver)
    imgui.set_next_window_pos(300, 50, imgui.Cond_FirstUseEver)
    
    local flags = imgui.WindowFlags_MenuBar
    local visible, open = imgui.begin_window("AetherEngine ImGui Demo", true, flags)
    if not open then
        demo_open = false
        imgui.end_window()
        imgui.shutdown()
        task.stop_all()
        return
    end
    if visible then
        -- 菜单栏
        if imgui.begin_menu_bar() then
            if imgui.begin_menu("文件") then
                if imgui.menu_item("新建", "Ctrl+N") then
                    log.info("菜单: 新建")
                end
                if imgui.menu_item("打开", "Ctrl+O") then
                    log.info("菜单: 打开")
                end
                imgui.separator()
                if imgui.menu_item("退出", "Alt+F4") then
                    log.info("菜单: 退出")
                end
                imgui.end_menu()
            end
            
            if imgui.begin_menu("视图") then
                local changed, val = imgui.checkbox("覆盖层", show_overlay)
                if changed then show_overlay = val end
                
                changed, val = imgui.checkbox("指标窗口", show_metrics)
                if changed then show_metrics = val end
                
                imgui.end_menu()
            end
            
            if imgui.begin_menu("帮助") then
                if imgui.menu_item("关于") then
                    log.info("AetherEngine ImGui Demo v1.0")
                end
                imgui.end_menu()
            end
            
            imgui.end_menu_bar()
        end
        
        -- 标题
        imgui.text("AetherEngine ImGui Lua 绑定演示")
        imgui.text_disabled("展示所有可用的 UI 控件")
        imgui.separator()
        
        -- 帧信息
        local dt = imgui.get_delta_time()
        imgui.text(string.format("帧: %d  |  FPS: %.1f  |  帧时间: %.3f ms", 
            frame_count, 1.0 / dt, dt * 1000))
        imgui.separator()
        
        -- 各类控件演示
        draw_basic_widgets()
        draw_input_widgets()
        draw_selection_widgets()
        draw_color_widgets()
        draw_tree_widgets()
        draw_tab_widgets()
        draw_table_widgets()
        draw_layout_widgets()
        draw_width_control()
        draw_popup_widgets()
        draw_item_status()
    end
    imgui.end_window()
end

--============================================================================
-- 指标窗口
--============================================================================

local function draw_metrics_window()
    if not show_metrics then return end
    
    imgui.set_next_window_size(300, 200, imgui.Cond_FirstUseEver)
    if imgui.begin_window("指标") then
        local sw, sh = imgui.get_screen_size()
        imgui.text(string.format("屏幕大小: %d x %d", sw, sh))
        
        local mx, my = imgui.get_mouse_pos()
        imgui.text(string.format("鼠标位置: %.0f, %.0f", mx, my))
        
        local fc = imgui.get_frame_count()
        imgui.text(string.format("ImGui 帧数: %d", fc))
        
        local dt = imgui.get_delta_time()
        imgui.text(string.format("帧时间: %.4f s", dt))
        imgui.text(string.format("FPS: %.1f", 1.0 / dt))
        
        imgui.separator()
        imgui.text(string.format("Lua 帧数: %d", frame_count))
    end
    imgui.end_window()
end

--============================================================================
-- 渲染回调
--============================================================================

local function on_render()
    frame_count = frame_count + 1
    
    if show_overlay then
        draw_overlay()
    end
    
    draw_demo_window()
    draw_metrics_window()
end

--============================================================================
-- 主入口
--============================================================================
sys.debug()
log.info("AetherEngine ImGui Demo 启动")
init_colors()

imgui.on_render(on_render)

if imgui.is_initialized() then
    log.info("编辑器模式: 渲染回调已注册")
else
    log.info("命令行模式: 初始化 ImGui")
    
    if not imgui.init() then
        log.error("ImGui 初始化失败")
        return
    end
    
    log.info("按 ESC 退出")
    imgui.run()
    log.info("ImGui Demo 已关闭")
end

local id = task.run([[
    for i = 1, 20000000 do
        sys.sleep(50)
        task.set_progress(i / 20)
    end
]])

hotkey.start()
while true do
    if hotkey.is_pressed(0x11) and hotkey.is_pressed(0x7B) then
        log.info("Ctrl+F12 被按下")
        hotkey.stop()
        break
    end
    sys.sleep(10)
end
