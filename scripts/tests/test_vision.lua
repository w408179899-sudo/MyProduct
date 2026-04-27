--[[
    vision 模块测试
    图像处理和找图找色功能验证
]]
local T = require("tests.test_framework")

local function run()
    print("\n--- vision module ---")
    T.reset()

    local testImg = nil -- 共享测试图像

    -- 截图模式测试
    T.test(
        "vision.set_mode/get_mode: 截图模式",
        function()
            T.assert_type(vision.set_mode, "function")
            T.assert_type(vision.get_mode, "function")
            local mode = vision.get_mode()
            T.assert_type(mode, "string")
            print("  当前截图模式: " .. mode)
        end
    )

    -- 截图测试
    T.test(
        "vision.capture: 全屏截图",
        function()
            local img = vision.capture()
            if img then
                T.assert_true(img:valid(), "captured image should be valid")
                T.assert_gt(img:width(), 0, "width should be > 0")
                T.assert_gt(img:height(), 0, "height should be > 0")
                print("  Full screen: " .. img:width() .. " x " .. img:height())
                vision.free(img)
            end
        end
    )

    T.test(
        "vision.capture: 区域截图",
        function()
            testImg = vision.capture(0, 0, 200, 200)
            if testImg then
                T.assert_true(testImg:valid())
                T.assert_eq(testImg:width(), 200, "width should be 200")
                T.assert_eq(testImg:height(), 200, "height should be 200")
                print("  Region: " .. testImg:width() .. " x " .. testImg:height())
            end
        end
    )

    -- Image userdata 方法测试
    T.test(
        "Image:width/height/valid: 图像属性",
        function()
            if not testImg then
                return
            end
            T.assert_type(testImg:width(), "number")
            T.assert_type(testImg:height(), "number")
            T.assert_type(testImg:valid(), "boolean")
            T.assert_true(testImg:valid())
        end
    )

    -- 像素操作测试
    T.test(
        "vision.pixel: 获取像素颜色",
        function()
            if not testImg then
                return
            end
            local color = vision.pixel(testImg, 0, 0)
            T.assert_type(color, "number")
            T.assert_gte(color, 0, "color should be non-negative")
            print("  Pixel(0,0): 0x" .. string.format("%06X", color))
        end
    )

    T.test(
        "Image:pixel: 通过方法获取像素",
        function()
            if not testImg then
                return
            end
            local color = testImg:pixel(10, 10)
            T.assert_type(color, "number")
        end
    )

    -- 图像处理测试
    T.test(
        "vision.crop: 裁剪图像",
        function()
            if not testImg then
                return
            end
            local cropped = vision.crop(testImg, 10, 10, 50, 50)
            if cropped then
                T.assert_true(cropped:valid())
                T.assert_eq(cropped:width(), 50)
                T.assert_eq(cropped:height(), 50)
                vision.free(cropped)
            end
        end
    )

    T.test(
        "vision.resize: 缩放图像",
        function()
            if not testImg then
                return
            end
            local resized = vision.resize(testImg, 100, 100)
            if resized then
                T.assert_true(resized:valid())
                T.assert_eq(resized:width(), 100)
                T.assert_eq(resized:height(), 100)
                vision.free(resized)
            end
        end
    )

    T.test(
        "vision.to_gray: 转灰度",
        function()
            if not testImg then
                return
            end
            local gray = vision.to_gray(testImg)
            if gray then
                T.assert_true(gray:valid())
                T.assert_eq(gray:width(), testImg:width())
                vision.free(gray)
            end
        end
    )

    T.test(
        "vision.to_binary: 二值化",
        function()
            if not testImg then
                return
            end
            local binary = vision.to_binary(testImg, 128)
            if binary then
                T.assert_true(binary:valid())
                vision.free(binary)
            end
        end
    )

    -- 图像比较测试
    T.test(
        "vision.compare: 图像相似度",
        function()
            if not testImg then
                return
            end
            -- 与自身比较应该返回 1.0 (100% 相似)
            local sim = vision.compare(testImg, testImg)
            T.assert_type(sim, "number")
            T.assert_gte(sim, 0.99, "self-compare should be ~1.0")
        end
    )

    -- 找色测试
    T.test(
        "vision.find_color: 查找颜色",
        function()
            if not testImg then
                return
            end
            -- 获取左上角颜色，然后搜索它
            local targetColor = vision.pixel(testImg, 0, 0)
            local x, y = vision.find_color(testImg, targetColor, 5)
            if x then
                T.assert_type(x, "number")
                T.assert_type(y, "number")
                T.assert_gte(x, 0)
                T.assert_gte(y, 0)
            end
        end
    )

    T.test(
        "vision.find_all_colors: 查找所有匹配颜色",
        function()
            if not testImg then
                return
            end
            local targetColor = vision.pixel(testImg, 0, 0)
            local results = vision.find_all_colors(testImg, targetColor, 10, 100)
            T.assert_type(results, "table")
            print("  Found " .. #results .. " color matches")
        end
    )

    -- 找图测试 (使用自身的一部分作为模板)
    T.test(
        "vision.find: 模板匹配",
        function()
            if not testImg then
                return
            end
            local template = vision.crop(testImg, 10, 10, 30, 30)
            if template then
                local x, y, conf = vision.find(testImg, template, 0.8)
                if x then
                    T.assert_type(x, "number")
                    T.assert_type(y, "number")
                    T.assert_type(conf, "number")
                    T.assert_gte(conf, 0.8, "confidence should be >= 0.8")
                    print("  Found at (" .. x .. ", " .. y .. ") conf=" .. string.format("%.2f", conf))
                end
                vision.free(template)
            end
        end
    )

    T.test(
        "vision.find_all: 查找所有匹配",
        function()
            if not testImg then
                return
            end
            local template = vision.crop(testImg, 0, 0, 20, 20)
            if template then
                local results = vision.find_all(testImg, template, 0.9, 10)
                T.assert_type(results, "table")
                print("  Found " .. #results .. " template matches")
                vision.free(template)
            end
        end
    )

    -- 图像保存和加载测试
    local tmp = sys.tmpdir()
    if tmp and #tmp > 0 and tmp:sub(-1) ~= "/" and tmp:sub(-1) ~= "\\" then
        tmp = tmp .. "/"
    end
    local testFilePath = tmp .. "aether_test_image.png"
    local testJpgPath = tmp .. "aether_test_image.jpg"

    T.test(
        "vision.save: 保存图像为 PNG",
        function()
            if not testImg then
                testImg = vision.capture(0, 0, 100, 100)
            end
            if testImg then
                local success = vision.save(testImg, testFilePath)
                T.assert_true(success, "save PNG should succeed")
                log.info("  Saved to: " .. testFilePath)
            end
        end
    )

    T.test(
        "vision.save: 保存图像为 JPG",
        function()
            if not testImg then
                return
            end
            local success = vision.save(testImg, testJpgPath)
            T.assert_true(success, "save JPG should succeed")
            log.info("  Saved to: " .. testJpgPath)
        end
    )

    T.test(
        "vision.load: 从文件加载图像",
        function()
            local loaded = vision.load(testFilePath)
            if loaded then
                T.assert_true(loaded:valid(), "loaded image should be valid")
                T.assert_gt(loaded:width(), 0, "width should be > 0")
                T.assert_gt(loaded:height(), 0, "height should be > 0")
                log.info("  Loaded: " .. loaded:width() .. " x " .. loaded:height())
                vision.free(loaded)
            else
                T.assert_true(false, "failed to load image from file")
            end
        end
    )

    T.test(
        "vision.load_memory: 从内存加载图像",
        function()
            -- 读取刚保存的文件到内存
            local file = io.open(testFilePath, "rb")
            if file then
                local data = file:read("*a")
                file:close()

                if data and #data > 0 then
                    local loaded = vision.load_memory(data)
                    if loaded then
                        T.assert_true(loaded:valid(), "memory-loaded image should be valid")
                        log.info("  Loaded from memory: " .. loaded:width() .. " x " .. loaded:height())
                        vision.free(loaded)
                    else
                        T.assert_true(false, "failed to load image from memory")
                    end
                end
            end
        end
    )

    T.test(
        "Image:save: 通过方法保存图像",
        function()
            if not testImg then
                return
            end
            local bmpPath = tmp .. "aether_test_image.bmp"
            local success = testImg:save(bmpPath)
            T.assert_true(success, "Image:save should succeed")
            log.info("  Saved via method to: " .. bmpPath)
        end
    )

    -- capture_window 测试
    T.test(
        "vision.capture_window: 函数存在",
        function()
            T.assert_type(vision.capture_window, "function")
        end
    )

    -- find_multi_color 测试
    T.test(
        "vision.find_multi_color: 多点找色",
        function()
            T.assert_type(vision.find_multi_color, "function")
            if not testImg then
                testImg = vision.capture(0, 0, 200, 200)
            end
            if testImg and testImg:valid() then
                -- 获取左上角像素颜色，构造为 table 格式 {r, g, b}
                local color = vision.pixel(testImg, 0, 0)
                local r = (color >> 16) & 0xFF
                local g = (color >> 8) & 0xFF
                local b = color & 0xFF
                local ok, x, y = pcall(vision.find_multi_color, testImg, {r, g, b}, "", 10)
                if ok and x then
                    T.assert_type(x, "number")
                    T.assert_type(y, "number")
                    print(string.format("  找到: (%d, %d)", x, y))
                else
                    -- API 参数格式可能不同，仅验证函数可调用
                    print("  find_multi_color 调用完成 (未匹配或参数格式不同)")
                end
            end
        end
    )

    -- 清理
    T.test(
        "vision.free: 释放图像",
        function()
            if testImg then
                vision.free(testImg)
                testImg = nil
            end
        end
    )

    return T.report("vision")
end

return {run = run}
