--[[
    ocr 模块测试 (chineseocr_lite + NCNN)
    测试全屏 OCR、区域 OCR 和关键词查找功能
    
    模型文件 (models/ 目录):
    - dbnet_op.param/bin      (文本检测)
    - angle_op.param/bin      (方向分类)  
    - crnn_lite_op.param/bin  (文字识别)
    - keys.txt                (字符集)
]]
local T = require("tests.test_framework")

-- 获取脚本所在目录，构建模型路径
local function get_models_dir()
    -- 通过脚本自身路径定位模型目录 (scripts/tests/ -> models/)
    local src = debug.getinfo(1, "S").source:match("@(.+[\\/])")
    local candidates = {}
    if src then
        candidates[#candidates + 1] = src .. "../../models"  -- scripts/tests/ -> project root
        candidates[#candidates + 1] = src .. "../models"     -- scripts/ -> project root
    end
    candidates[#candidates + 1] = "./models"
    candidates[#candidates + 1] = "../models"
    candidates[#candidates + 1] = "models"
    for _, p in ipairs(candidates) do
        local f = io.open(p .. "/keys.txt", "r")
        if f then f:close(); return p end
    end
    return "./models"
end

local function run()
    log.info("\n--- ocr module ---")
    T.reset()

    local models_dir = get_models_dir()
    log.info("  模型目录: " .. models_dir)

    --------------------------------------------------------------------------------
    -- API 存在性测试
    --------------------------------------------------------------------------------
    T.test(
        "ocr module exists",
        function()
            T.assert_type(ocr, "table")
        end
    )

    T.test(
        "ocr.init function exists",
        function()
            T.assert_type(ocr.init, "function")
        end
    )

    T.test(
        "ocr.recognize function exists",
        function()
            T.assert_type(ocr.recognize, "function")
        end
    )

    T.test(
        "ocr.release function exists",
        function()
            T.assert_type(ocr.release, "function")
        end
    )

    T.test(
        "ocr.is_initialized function exists",
        function()
            T.assert_type(ocr.is_initialized, "function")
        end
    )

    --------------------------------------------------------------------------------
    -- 初始化测试
    --------------------------------------------------------------------------------
    T.test(
        "ocr.init: 使用字符串路径初始化",
        function()
            local success = ocr.init(models_dir)
            T.assert_true(success, "OCR init with string path should succeed")
            ocr.release()
        end
    )

    T.test(
        "ocr.init: 使用配置表初始化",
        function()
            local success =
                ocr.init(
                {
                    models_dir = models_dir,
                    padding = 50,
                    max_side_len = 1024,
                    box_score_thresh = 0.5,
                    do_angle = true
                }
            )
            T.assert_true(success, "OCR init with config table should succeed")
        end
    )

    T.test(
        "ocr.is_initialized: 检查初始化状态",
        function()
            T.assert_true(ocr.is_initialized(), "OCR should be initialized")
        end
    )

    --------------------------------------------------------------------------------
    -- 全屏 OCR 测试
    --------------------------------------------------------------------------------
    T.test(
        "ocr.recognize: 全屏截图识别",
        function()
            local img = vision.capture()
            T.assert_true(img and img:valid(), "screen capture should succeed")

            local start_time = sys.time()
            local results = ocr.recognize(img)
            local elapsed = sys.time() - start_time

            T.assert_type(results, "table")
            log.info(string.format("  全屏 OCR: %d 个结果, 耗时 %.0fms", #results, elapsed))

            -- 打印识别结果 (前10条)
            local count = math.min(#results, 10)
            for i = 1, count do
                log.info(string.format('  [%d] "%s" (%.2f)', i, results[i].text, results[i].score))
            end
            if #results > 10 then
                log.info(string.format("  ... 还有 %d 条结果", #results - 10))
            end

            -- 验证结果结构
            if #results > 0 then
                local r = results[1]
                T.assert_type(r.text, "string", "result should have text field")
                T.assert_type(r.score, "number", "result should have score field")
                T.assert_type(r.box, "table", "result should have box field")
            end

            vision.free(img)
        end
    )

    --------------------------------------------------------------------------------
    -- 区域 OCR 测试
    --------------------------------------------------------------------------------
    T.test(
        "ocr.recognize: 区域截图识别 (800x600)",
        function()
            local img = vision.capture(0, 0, 800, 600)
            T.assert_true(img and img:valid(), "region capture should succeed")

            local start_time = sys.time()
            local results = ocr.recognize(img)
            local elapsed = sys.time() - start_time

            T.assert_type(results, "table")
            log.info(string.format("  区域 OCR: %d 个结果, 耗时 %.0fms", #results, elapsed))

            -- 打印识别结果
            for i, r in ipairs(results) do
                log.info(string.format('  [%d] "%s" (%.2f)', i, r.text, r.score))
            end

            vision.free(img)
        end
    )

    --------------------------------------------------------------------------------
    -- 关键词查找测试
    --------------------------------------------------------------------------------
    T.test(
        "ocr.recognize: 关键词查找",
        function()
            local img = vision.capture()
            if not img or not img:valid() then
                return
            end

            local results = ocr.recognize(img)

            -- 查找常见关键词
            local keywords = {"文件", "编辑", "视图", "帮助", "File", "Edit", "View", "Help"}
            local found = {}

            for _, r in ipairs(results) do
                for _, kw in ipairs(keywords) do
                    if string.find(r.text, kw) then
                        table.insert(found, {keyword = kw, text = r.text, score = r.score})
                        break
                    end
                end
            end

            log.info(string.format("  找到 %d 个包含关键词的文本", #found))
            for _, f in ipairs(found) do
                log.info(string.format('    [%s] -> "%s" (%.2f)', f.keyword, f.text, f.score))
            end

            vision.free(img)
        end
    )

    --------------------------------------------------------------------------------
    -- 结果字段验证测试
    --------------------------------------------------------------------------------
    T.test(
        "ocr.recognize: 结果字段完整性",
        function()
            local img = vision.capture(100, 100, 400, 300)
            if not img or not img:valid() then
                return
            end

            local results = ocr.recognize(img)

            if #results > 0 then
                local r = results[1]
                T.assert_not_nil(r.text, "should have text")
                T.assert_not_nil(r.score, "should have score")
                T.assert_not_nil(r.box, "should have box")
                T.assert_eq(#r.box, 8, "box should have 8 coordinates")
                T.assert_type(r.detect_time, "number", "should have detect_time")
                T.assert_type(r.recognize_time, "number", "should have recognize_time")
            end

            vision.free(img)
        end
    )

    --------------------------------------------------------------------------------
    -- 释放测试
    --------------------------------------------------------------------------------
    T.test(
        "ocr.release: 释放 OCR 引擎",
        function()
            ocr.release()
            T.assert_false(ocr.is_initialized(), "OCR should not be initialized after release")
        end
    )

    T.test(
        "ocr.recognize: 未初始化时返回空表",
        function()
            local img = vision.capture(0, 0, 100, 100)
            if img and img:valid() then
                local results = ocr.recognize(img)
                T.assert_type(results, "table")
                T.assert_eq(#results, 0, "should return empty table when not initialized")
                vision.free(img)
            end
        end
    )

    return T.report("ocr")
end

return {run = run}
