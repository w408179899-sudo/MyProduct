--[[
    trajectory 模块测试
    测试拟人轨迹生成功能 (鼠标/触控/摇杆)
]]

local T = require("tests.test_framework")
local M = {}

function M.run()
    T.reset()
    log.info("=== trajectory 模块测试 ===")

-- 基本轨迹生成测试
T.test("生成鼠标轨迹: (0,0) -> (500,300)", function()
    local points = trajectory.generate(0, 0, 500, 300)
    T.assert_type(points, "table")
    T.assert_gte(#points, 10, "应有足够多的点")
    
    -- 验证起点终点
    T.assert_eq(points[1].x, 0, "起点X")
    T.assert_eq(points[1].y, 0, "起点Y")
    T.assert_eq(points[#points].x, 500, "终点X")
    T.assert_eq(points[#points].y, 300, "终点Y")
    
    -- 计算总时间和平均速度
    local totalTime = points[#points].time
    local distance = math.sqrt(500*500 + 300*300)
    local avgSpeed = distance / (totalTime / 1000)
    
    log.info(string.format("  生成 %d 个点, 总时间 %.0fms, 平均速度 %.0f px/s", 
        #points, totalTime, avgSpeed))
end)

T.test("轨迹点包含完整数据结构", function()
    local points = trajectory.generate(0, 0, 100, 100)
    local pt = points[math.floor(#points/2)]  -- 取中间点
    
    T.assert_type(pt.x, "number", "x坐标")
    T.assert_type(pt.y, "number", "y坐标")
    T.assert_type(pt.time, "number", "时间戳")
    T.assert_type(pt.pressure, "number", "压力值")
    
    log.info(string.format("  中间点: x=%.1f, y=%.1f, time=%.0fms, pressure=%.2f",
        pt.x, pt.y, pt.time, pt.pressure))
end)

T.test("轨迹时间单调递增", function()
    local points = trajectory.generate(0, 0, 300, 200)
    local violations = 0
    for i = 2, #points do
        if points[i].time < points[i-1].time then
            violations = violations + 1
        end
    end
    T.assert_eq(violations, 0, "时间应单调递增")
    log.info(string.format("  检查 %d 个点, 时间递增正确", #points))
end)

-- 过冲轨迹测试
T.test("生成带过冲的轨迹", function()
    -- 使用高过冲概率的预设
    local nature = trajectory.preset("granny")
    local points = trajectory.generate_overshoot(0, 0, 200, 200, nature)
    
    T.assert_gte(#points, 5)
    T.assert_eq(points[#points].x, 200, "最终到达目标X")
    T.assert_eq(points[#points].y, 200, "最终到达目标Y")
    
    -- 检查是否有过冲 (中间点可能超过目标)
    local maxX, maxY = 0, 0
    for _, pt in ipairs(points) do
        maxX = math.max(maxX, pt.x)
        maxY = math.max(maxY, pt.y)
    end
    
    log.info(string.format("  轨迹 %d 点, 最大X=%.1f, 最大Y=%.1f (目标200,200)",
        #points, maxX, maxY))
end)

-- 预设配置测试
T.test("Robot预设: 直线匀速无噪声", function()
    local nature = trajectory.preset("robot")
    T.assert_eq(nature.noise, 0, "噪声为0")
    T.assert_eq(nature.deviation, 0, "偏移为0")
    
    local points = trajectory.generate(0, 0, 100, 0, nature)
    
    -- 检查Y坐标偏离
    local maxDeviation = 0
    for _, pt in ipairs(points) do
        maxDeviation = math.max(maxDeviation, math.abs(pt.y))
    end
    T.assert_lte(maxDeviation, 1, "直线运动Y偏离应<1")
    
    log.info(string.format("  速度: %d-%d px/s, 最大Y偏离: %.2f",
        nature.min_speed, nature.max_speed, maxDeviation))
end)

T.test("FastGamer预设: 高速低噪声", function()
    local nature = trajectory.preset("fast_gamer")
    T.assert_gte(nature.min_speed, 500, "最低速度>=500")
    T.assert_lte(nature.noise, 2, "噪声<=2")
    
    local points = trajectory.generate(0, 0, 400, 300, nature)
    local totalTime = points[#points].time
    
    log.info(string.format("  速度范围: %d-%d px/s, 噪声: %.1f, 耗时: %.0fms",
        nature.min_speed, nature.max_speed, nature.noise, totalTime))
end)

T.test("Granny预设: 慢速高噪声多过冲", function()
    local nature = trajectory.preset("granny")
    T.assert_lte(nature.max_speed, 400, "最高速度<=400")
    T.assert_gte(nature.noise, 3, "噪声>=3")
    T.assert_gte(nature.overshoot_probability, 0.3, "过冲概率>=0.3")
    
    local points = trajectory.generate(0, 0, 200, 150, nature)
    local totalTime = points[#points].time
    
    log.info(string.format("  速度范围: %d-%d px/s, 噪声: %.1f, 过冲概率: %.0f%%, 耗时: %.0fms",
        nature.min_speed, nature.max_speed, nature.noise, 
        nature.overshoot_probability * 100, totalTime))
end)

-- 触控滑动测试
T.test("触控滑动: 压力曲线验证", function()
    local nature = trajectory.preset("touch_swipe")
    local points = trajectory.generate(100, 500, 100, 100, nature)
    
    -- 检查压力值范围
    local minP, maxP = 1, 0
    for _, pt in ipairs(points) do
        minP = math.min(minP, pt.pressure)
        maxP = math.max(maxP, pt.pressure)
    end
    
    T.assert_gte(minP, 0, "压力>=0")
    T.assert_lte(maxP, 1, "压力<=1")
    
    -- 检查压力曲线趋势 (开始小, 中间大, 结束小)
    local startP = points[1].pressure
    local midP = points[math.floor(#points/2)].pressure
    local endP = points[#points].pressure
    
    log.info(string.format("  压力曲线: 起始=%.2f, 中间=%.2f, 结束=%.2f (范围:%.2f-%.2f)",
        startP, midP, endP, minP, maxP))
end)

T.test("触控快速滑动 vs 拖动对比", function()
    local swipeFast = trajectory.preset("touch_swipe_fast")
    local drag = trajectory.preset("touch_drag")
    
    local p1 = trajectory.generate(0, 0, 300, 0, swipeFast)
    local p2 = trajectory.generate(0, 0, 300, 0, drag)
    
    local t1 = p1[#p1].time
    local t2 = p2[#p2].time
    
    T.assert_lt(t1, t2, "快速滑动应比拖动快")
    
    log.info(string.format("  快速滑动: %.0fms (%d点), 拖动: %.0fms (%d点)",
        t1, #p1, t2, #p2))
end)

-- 摇杆测试
T.test("摇杆: 归一化坐标 (-1到1)", function()
    local nature = trajectory.preset("joystick_smooth")
    local points = trajectory.generate(0, 0, 0.8, -0.6, nature)
    
    T.assert_gte(#points, 2)
    
    local last = points[#points]
    T.assert_lte(math.abs(last.x - 0.8), 0.02, "终点X接近0.8")
    T.assert_lte(math.abs(last.y - (-0.6)), 0.02, "终点Y接近-0.6")
    
    log.info(string.format("  目标(0.8,-0.6), 实际(%.3f,%.3f), %d个点",
        last.x, last.y, #points))
end)

T.test("摇杆瞄准: 高精度低噪声", function()
    local nature = trajectory.preset("joystick_aim")
    T.assert_lte(nature.noise, 0.02, "噪声应很低")
    
    local points = trajectory.generate(0, 0, 0.5, 0.3, nature)
    
    -- 检查轨迹抖动
    local maxJitter = 0
    for i = 2, #points - 1 do
        local dx = math.abs(points[i].x - points[i-1].x)
        local dy = math.abs(points[i].y - points[i-1].y)
        maxJitter = math.max(maxJitter, math.sqrt(dx*dx + dy*dy))
    end
    
    local deadzone = nature.deadzone or 0
    log.info(string.format("  噪声: %.3f, 死区: %.2f, 最大步进抖动: %.4f",
        nature.noise, deadzone, maxJitter))
end)

-- 随机种子测试
T.test("随机种子: 可重复生成", function()
    trajectory.set_seed(99999)
    local p1 = trajectory.generate(0, 0, 200, 150)
    
    trajectory.set_seed(99999)
    local p2 = trajectory.generate(0, 0, 200, 150)
    
    T.assert_eq(#p1, #p2, "点数相同")
    
    local identical = true
    for i = 1, #p1 do
        if p1[i].x ~= p2[i].x or p1[i].y ~= p2[i].y then
            identical = false
            break
        end
    end
    T.assert_true(identical, "相同种子生成相同轨迹")
    
    log.info(string.format("  种子99999: 两次生成 %d 点, 完全一致", #p1))
end)

T.test("不同种子产生不同轨迹", function()
    trajectory.set_seed(11111)
    local p1 = trajectory.generate(0, 0, 100, 100)
    
    trajectory.set_seed(22222)
    local p2 = trajectory.generate(0, 0, 100, 100)
    
    local different = false
    for i = 2, math.min(#p1, #p2) - 1 do
        if math.abs(p1[i].x - p2[i].x) > 0.1 or math.abs(p1[i].y - p2[i].y) > 0.1 then
            different = true
            break
        end
    end
    T.assert_true(different, "不同种子应产生不同轨迹")
    
    log.info("  种子11111和22222产生不同轨迹")
end)

-- 边界情况测试
T.test("极短距离: 5像素", function()
    local points = trajectory.generate(0, 0, 3, 4)  -- 距离=5
    T.assert_gte(#points, 2, "至少2个点")
    T.assert_eq(points[#points].x, 3)
    T.assert_eq(points[#points].y, 4)
    
    log.info(string.format("  5像素距离生成 %d 个点", #points))
end)

T.test("极长距离: 2000像素", function()
    local points = trajectory.generate(0, 0, 1600, 1200)  -- 距离=2000
    T.assert_gte(#points, 20, "长距离应有更多点")
    
    local totalTime = points[#points].time
    log.info(string.format("  2000像素生成 %d 个点, 耗时 %.0fms", #points, totalTime))
end)

T.test("同一点: 距离为0", function()
    local points = trajectory.generate(100, 100, 100, 100)
    T.assert_gte(#points, 1, "至少1个点")
    log.info(string.format("  零距离生成 %d 个点", #points))
end)

-- 自定义参数测试
T.test("自定义参数: 高速直线", function()
    local points = trajectory.generate(0, 0, 500, 0, {
        min_speed = 2000,
        max_speed = 3000,
        deviation = 0,
        noise = 0,
        flow = "constant"
    })
    
    local totalTime = points[#points].time
    local actualSpeed = 500 / (totalTime / 1000)
    
    T.assert_gte(actualSpeed, 1500, "速度应>=1500")
    
    -- 检查直线
    local maxY = 0
    for _, pt in ipairs(points) do
        maxY = math.max(maxY, math.abs(pt.y))
    end
    T.assert_lte(maxY, 1, "无偏移直线")
    
    log.info(string.format("  配置速度2000-3000, 实际速度 %.0f px/s, 耗时 %.0fms",
        actualSpeed, totalTime))
end)

T.test("速度曲线对比: constant vs ease_in_out", function()
    local p1 = trajectory.generate(0, 0, 300, 0, {
        min_speed = 500, max_speed = 500,
        deviation = 0, noise = 0, flow = "constant"
    })
    
    local p2 = trajectory.generate(0, 0, 300, 0, {
        min_speed = 500, max_speed = 500,
        deviation = 0, noise = 0, flow = "ease_in_out"
    })
    
    -- 匀速移动中间点应在中间位置
    local mid1 = p1[math.floor(#p1/2)]
    local mid2 = p2[math.floor(#p2/2)]
    
    log.info(string.format("  constant中点: x=%.1f (期望150), ease_in_out中点: x=%.1f",
        mid1.x, mid2.x))
end)

    -- 打印报告
    return T.report("trajectory")
end

return M
