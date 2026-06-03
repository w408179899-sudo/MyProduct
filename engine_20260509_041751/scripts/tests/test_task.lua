--[[
    task 模块测试 - 任务管理功能验证
    覆盖: 任务ID/计数/列表/进度/创建/控制/等待/生命周期等
    
    API 列表 (来自 LuaExports.cpp):
    - task.create(script, config?)     创建任务
    - task.run(script, args?)          创建并启动任务
    - task.start(task_id)              启动任务
    - task.pause(task_id)              暂停任务
    - task.resume(task_id)             恢复任务
    - task.stop(task_id)               停止任务
    - task.wait(task_id, timeout?)     等待任务完成
    - task.status(task_id)             获取任务状态
    - task.info(task_id)               获取任务详细信息
    - task.id()                        获取当前任务ID
    - task.set_progress(progress)      设置任务进度
    - task.list()                      获取所有任务列表
    - task.stop_all()                  停止所有任务
    - task.wait_all(timeout?)          等待所有任务
    - task.cleanup()                   清理已完成任务
    - task.count()                     获取运行中任务数量
]]
local T = require("tests.test_framework")

-- 获取当前脚本所在目录 (用于构建文件路径任务的绝对路径)
local script_dir = debug.getinfo(1, "S").source:match("@(.+[\\/])") or ""

local function run()
    T.reset()
    T.log("\n=== task 模块测试 ===")

    --------------------------
    -- 基础 API 存在性测试
    --------------------------
    T.log("\n--- 基础 API ---")
    
    T.test("task.id: 获取当前任务ID", function()
        local id = task.id()
        T.assert_type(id, "number")
        T.assert_gte(id, 0)
        T.log("  当前任务ID: " .. id)
    end)

    T.test("task.count: 获取运行中任务数", function()
        local c = task.count()
        T.assert_type(c, "number")
        T.assert_gte(c, 0)
        T.log("  运行中任务数: " .. c)
    end)

    T.test("task.list: 获取任务列表", function()
        local list = task.list()
        T.assert_type(list, "table")
        T.log("  任务列表长度: " .. #list)
    end)

    T.test("task.set_progress: 设置任务进度", function()
        -- 设置不同进度值
        task.set_progress(0.0)
        task.set_progress(0.5)
        task.set_progress(1.0)
        -- 边界值测试
        task.set_progress(0)
        task.set_progress(100)  -- 如果是百分比
    end)

    --------------------------
    -- 函数存在性测试
    --------------------------
    T.log("\n--- API 存在性 ---")
    
    T.test("task.create/run: 创建任务函数存在", function()
        T.assert_type(task.create, "function")
        T.assert_type(task.run, "function")
    end)

    T.test("task.start/pause/resume/stop: 控制函数存在", function()
        T.assert_type(task.start, "function")
        T.assert_type(task.pause, "function")
        T.assert_type(task.resume, "function")
        T.assert_type(task.stop, "function")
    end)

    T.test("task.wait/status/info: 查询函数存在", function()
        T.assert_type(task.wait, "function")
        T.assert_type(task.status, "function")
        T.assert_type(task.info, "function")
    end)

    T.test("task.stop_all/wait_all/cleanup: 批量操作函数存在", function()
        T.assert_type(task.stop_all, "function")
        T.assert_type(task.wait_all, "function")
        T.assert_type(task.cleanup, "function")
    end)

    --------------------------
    -- 任务状态查询测试
    --------------------------
    T.log("\n--- 状态查询 ---")
    
    T.test("task.status: 无效任务ID返回nil或错误", function()
        local status = task.status(99999)
        -- 无效ID应返回nil或特定错误状态
        if status then
            T.assert_type(status, "string")
        end
    end)

    T.test("task.info: 无效任务ID返回nil", function()
        local info = task.info(99999)
        T.assert_nil(info)
    end)

    --------------------------
    -- 无效任务ID测试
    --------------------------
    T.log("\n--- 无效任务ID处理 ---")
    
    T.test("task.stop: 停止无效任务ID", function()
        local result = task.stop(99999)
        T.assert_type(result, "boolean")
        T.assert_false(result)
    end)

    T.test("task.pause: 暂停无效任务ID", function()
        local result = task.pause(99999)
        T.assert_type(result, "boolean")
        T.assert_false(result)
    end)

    T.test("task.resume: 恢复无效任务ID", function()
        local result = task.resume(99999)
        T.assert_type(result, "boolean")
        T.assert_false(result)
    end)

    T.test("task.wait: 等待无效任务ID", function()
        local result = task.wait(99999, 10)
        T.assert_type(result, "boolean")
    end)

    --------------------------
    -- 任务生命周期测试
    --------------------------
    T.log("\n--- 任务生命周期 ---")
    
    T.test("task.run: 创建并运行简单任务", function()
        local id = task.run([[
            -- 简单脚本：设置进度并完成
            task.set_progress(0.5)
            sys.sleep(50)
            task.set_progress(1.0)
        ]])
        T.assert_type(id, "number")
        T.assert_gt(id, 0)
        
        -- 等待任务完成
        local finished = task.wait(id, 1000)
        T.assert_true(finished)
        
        -- 检查状态
        local status = task.status(id)
        T.log("  任务状态: " .. tostring(status))
    end)

    T.test("task.run: 运行长任务并停止", function()
        local id = task.run([[
            -- 长时间运行的脚本
            for i = 1, 100 do
                sys.sleep(50)
                task.set_progress(i / 100)
            end
        ]])
        T.assert_type(id, "number")
        
        -- 等待一小段时间后停止
        sys.sleep(100)
        local stopped = task.stop(id)
        T.assert_true(stopped)
        
        -- 等待任务实际停止
        task.wait(id, 500)
        
        local status = task.status(id)
        T.log("  停止后状态: " .. tostring(status))
    end)

    T.test("task.pause/resume: 暂停和恢复", function()
        local id = task.run([[
            for i = 1, 20 do
                sys.sleep(50)
                task.set_progress(i / 20)
            end
        ]])
        
        -- 等待任务开始运行
        sys.sleep(100)
        
        -- 暂停
        local paused = task.pause(id)
        T.log("  暂停结果: " .. tostring(paused))
        
        sys.sleep(100)
        local status_paused = task.status(id)
        T.log("  暂停后状态: " .. tostring(status_paused))
        
        -- 恢复
        local resumed = task.resume(id)
        T.log("  恢复结果: " .. tostring(resumed))
        
        -- 停止任务
        task.stop(id)
        task.wait(id, 500)
    end)

    --------------------------
    -- task.run 配置表测试
    --------------------------
    T.log("\n--- 配置表 ---")

    T.test("task.run: 使用配置表 (name/priority)", function()
        local id = task.run([[
            task.set_progress(1.0)
        ]], { name = "test_config_task", priority = "high" })
        T.assert_type(id, "number")
        T.assert_gt(id, 0)
        task.wait(id, 1000)
        T.log("  配置表任务ID: " .. id)
    end)

    T.test("task.run: 传入脚本文件路径", function()
        sys.set_share("_test_file_task_done", 0)
        local id = task.run(script_dir .. "test_task_helper.lua")
        T.assert_type(id, "number")
        T.assert_gt(id, 0)
        
        local finished = task.wait(id, 2000)
        T.assert_true(finished)
        
        local val = sys.get_share("_test_file_task_done")
        T.assert_eq(val, 1, "文件任务应通过共享变量通知完成")
        T.log("  文件路径任务ID: " .. id .. ", 完成标记: " .. tostring(val))
    end)

    T.test("task.run: 脚本文件路径 + 配置表", function()
        sys.set_share("_test_file_task_done", 0)
        local id = task.run(script_dir .. "test_task_helper.lua", {
            name = "file_config_task",
            priority = "high"
        })
        T.assert_type(id, "number")
        T.assert_gt(id, 0)
        
        local finished = task.wait(id, 2000)
        T.assert_true(finished)
        
        -- 验证任务名称
        local info = task.info(id)
        if info then
            T.assert_eq(info.name, "file_config_task")
            T.log("  任务名: " .. info.name .. ", 状态: " .. info.status)
        end
    end)

    T.test("task.run: 脚本执行错误应标记为 failed", function()
        local id = task.run([[
            error("intentional test error")
        ]])
        T.assert_type(id, "number")
        
        task.wait(id, 1000)
        local status = task.status(id)
        T.assert_eq(status, "failed", "执行错误的任务应为 failed 状态")
        T.log("  错误任务状态: " .. tostring(status))
    end)

    T.test("task.run: 不存在的脚本文件", function()
        local id = task.run("nonexistent_script_12345.lua")
        T.assert_type(id, "number")
        
        task.wait(id, 1000)
        local status = task.status(id)
        T.assert_eq(status, "failed", "不存在的脚本文件应为 failed 状态")
        
        local info = task.info(id)
        if info and info.error then
            T.log("  错误信息: " .. info.error)
        end
    end)

    T.test("task.create: 创建任务不自动启动", function()
        local id = task.create([[
            task.set_progress(1.0)
        ]])
        T.assert_type(id, "number")
        T.assert_gt(id, 0)
        
        -- 创建后应为 pending 状态
        local status = task.status(id)
        T.assert_eq(status, "pending", "create 后应为 pending")
        T.log("  创建后状态: " .. tostring(status))
        
        -- 手动启动
        local started = task.start(id)
        T.assert_true(started)
        
        task.wait(id, 1000)
        local final_status = task.status(id)
        T.log("  完成后状态: " .. tostring(final_status))
    end)

    T.test("task.run: auto_start=false 配置", function()
        local id = task.run([[
            task.set_progress(1.0)
        ]], { auto_start = false })
        T.assert_type(id, "number")
        T.assert_gt(id, 0)
        
        -- auto_start=false 时应为 pending
        local status = task.status(id)
        T.assert_eq(status, "pending", "auto_start=false 后应为 pending")
        
        -- 手动启动
        task.start(id)
        task.wait(id, 1000)
        T.assert_eq(task.status(id), "completed", "手动启动后应完成")
        T.log("  auto_start=false 测试完成")
    end)

    T.test("task.start: 重复启动已完成任务返回false", function()
        local id = task.run([[
            task.set_progress(1.0)
        ]])
        task.wait(id, 1000)
        T.assert_eq(task.status(id), "completed")
        
        -- 对已完成任务调用 start 应返回 false
        local ok = task.start(id)
        T.assert_false(ok, "已完成任务不应重复启动")
    end)

    --------------------------
    -- task.info 状态字符串测试
    --------------------------
    T.log("\n--- info/status 字符串 ---")

    T.test("task.info: 返回完整信息表", function()
        local id = task.run([[
            task.set_progress(0.5)
            sys.sleep(100)
            task.set_progress(1.0)
        ]], { name = "info_test" })
        sys.sleep(50)
        
        local info = task.info(id)
        T.assert_type(info, "table")
        T.assert_type(info.name, "string")
        T.assert_type(info.status, "string")
        T.assert_type(info.progress, "number")
        T.log(string.format("  name=%s, status=%s, progress=%.1f", 
            info.name, info.status, info.progress))
        
        task.wait(id, 1000)
        
        -- 完成后再次检查
        local info2 = task.info(id)
        if info2 then
            T.log(string.format("  完成后: status=%s, progress=%.1f", 
                info2.status, info2.progress))
        end
    end)

    T.test("task.list: 返回包含 status 字符串的列表", function()
        local id = task.run("sys.sleep(200)\n")
        sys.sleep(50)
        
        local list = task.list()
        T.assert_type(list, "table")
        if #list > 0 then
            local item = list[#list]
            T.assert_type(item.id, "number")
            T.assert_type(item.name, "string")
            T.assert_type(item.status, "string")
            T.log(string.format("  列表项: id=%d, name=%s, status=%s", 
                item.id, item.name, item.status))
        end
        
        task.stop(id)
        task.wait(id, 500)
    end)

    --------------------------
    -- task.on_stop 回调测试
    --------------------------
    T.log("\n--- on_stop 回调 ---")

    T.test("task.on_stop: 函数存在", function()
        if task.on_stop then
            T.assert_type(task.on_stop, "function")
        else
            T.log("  on_stop 未导出")
        end
    end)

    T.test("task.on_stop: 停止时触发回调", function()
        if not task.on_stop then
            T.log("  [跳过] on_stop 未导出")
            return
        end
        -- 通过共享变量验证回调是否执行
        sys.set_share("_test_on_stop", 0)
        local id = task.run([[
            task.on_stop(function()
                sys.set_share("_test_on_stop", 1)
            end)
            sys.sleep(5000)
        ]])
        sys.sleep(100)
        task.stop(id)
        task.wait(id, 1000)
        sys.sleep(100)
        local val = sys.get_share("_test_on_stop")
        T.assert_eq(val, 1, "on_stop 回调应将共享变量设为1")
        T.log("  on_stop 回调已触发: " .. tostring(val))
    end)

    --------------------------
    -- 批量操作测试 -- 暂时不测，批量停止会让当前测试任务也停止
    --------------------------
    -- T.log("\n--- 批量操作 ---")

    -- T.test("task.stop_all: 停止所有任务", function()
    --     local ids = {}
    --     for i = 1, 3 do
    --         ids[i] = task.run("sys.sleep(5000)\n")
    --     end
        
    --     sys.sleep(100)
    --     task.stop_all()
    --     task.wait_all(1000)
        
    --     T.log("  已停止 " .. #ids .. " 个任务")
    -- end)

    -- T.test("task.cleanup: 清理已完成任务 (返回清理数量)", function()
    --     local count = task.cleanup()
    --     T.assert_type(count, "number")
    --     T.assert_gte(count, 0, "清理数量应 >= 0")
    --     T.log("  cleanup 清理了 " .. count .. " 个任务")
    -- end)

    -- T.test("task.wait_all: 等待所有任务", function()
    --     task.wait_all(10)
    --     T.log("  wait_all 调用完成")
    -- end)

    return T.report("task")
end

return { run = run }
