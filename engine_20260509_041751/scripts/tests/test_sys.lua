--[[
    sys 模块测试 - 系统功能验证
    覆盖: 版本/平台/时间/环境变量/共享内存/进程信息/PE加载等
]]
local T = require("tests.test_framework")

local function run()
    T.reset()
    T.log("\n=== sys 模块测试 ===")

    -- 版本与平台
    T.test("sys.version: 返回x.y.z格式版本", function()
        local v = sys.version()
        T.assert_type(v, "string"); T.assert_true(v:match("^%d+%.%d+%.%d+"), "格式应为x.y.z")
        T.log("  版本: " .. v)
    end)

    T.test("sys.platform: 返回有效平台名", function()
        local p = sys.platform()
        T.assert_type(p, "string")
        T.assert_true(p == "windows" or p == "android" or p == "ios", "应为windows/android/ios")
        T.log("  平台: " .. p)
    end)

    T.test("sys.arch: 返回CPU架构", function()
        local a = sys.arch()
        T.assert_type(a, "string")
        T.assert_true(a == "x64" or a == "x86" or a == "arm64" or a == "arm", "架构无效")
        T.log("  架构: " .. a)
    end)

    T.test("sys.winver: 返回Windows版本号", function()
        local v = sys.winver()
        T.assert_type(v, "string")
        if sys.platform() == "windows" then
            T.assert_true(v:match("%d+%.%d+%.%d+%.%d+"), "格式应为x.x.xxxxx.xxxx")
        end
        T.log("  WinVer: " .. v)
    end)

    T.test("sys.info: 返回系统信息表", function()
        local i = sys.info()
        T.assert_type(i, "table")
        T.assert_type(i.version, "string"); T.assert_type(i.platform, "string")
        T.assert_type(i.arch, "string"); T.assert_type(i.bits, "number")
        T.log(string.format("  %s/%s/%dbit", i.platform, i.arch, i.bits))
    end)

    -- 硬件ID
    T.test("sys.hwid: 返回非空硬件ID", function()
        local h = sys.hwid()
        T.assert_type(h, "string"); T.assert_gt(#h, 0, "不应为空")
        T.log("  HWID: " .. h:sub(1, 16) .. "...")
    end)

    -- 时间相关
    T.test("sys.time: 毫秒时间戳递增", function()
        local t1 = sys.time()
        T.assert_type(t1, "number"); T.assert_gt(t1, 0)
        sys.sleep(15)
        local t2 = sys.time()
        T.assert_gt(t2, t1); T.assert_gte(t2 - t1, 10)
        T.log(string.format("  时间差: %dms", t2 - t1))
    end)

    T.test("sys.tick: 微秒级高精度时间戳", function()
        local t1 = sys.tick()
        T.assert_type(t1, "number"); T.assert_gt(t1, 0)
        sys.sleep(1)
        T.assert_gt(sys.tick(), t1)
    end)

    T.test("sys.sleep: 延迟精度验证", function()
        local start = sys.time()
        sys.sleep(50)
        local elapsed = sys.time() - start
        T.assert_gte(elapsed, 45); T.assert_lt(elapsed, 150)
        T.log(string.format("  sleep(50)实际: %dms", elapsed))
    end)

    -- 进程/线程
    T.test("sys.pid/tid: 返回正整数ID", function()
        local pid, tid = sys.pid(), sys.tid()
        T.assert_type(pid, "number"); T.assert_gt(pid, 0)
        T.assert_type(tid, "number"); T.assert_gt(tid, 0)
        T.log(string.format("  PID=%d, TID=%d", pid, tid))
    end)

    T.test("sys.cpu_count: 返回CPU核心数", function()
        local c = sys.cpu_count()
        T.assert_type(c, "number"); T.assert_gte(c, 1)
        T.log("  CPU核心: " .. c)
    end)

    T.test("sys.memory_info: 返回内存信息", function()
        local m = sys.memory_info()
        T.assert_type(m, "table")
        T.assert_type(m.total, "number"); T.assert_gt(m.total, 0)
        T.assert_type(m.available, "number"); T.assert_type(m.used, "number")
        T.log(string.format("  内存: %dMB/%dMB (%d%%)", m.used, m.total, m.percent or 0))
    end)

    -- 目录路径
    T.test("sys.get_cwd: 返回当前工作目录", function()
        local d = sys.get_cwd()
        T.assert_type(d, "string"); T.assert_gt(#d, 0)
        T.log("  CWD: " .. d)
    end)

    T.test("sys.tmpdir: 返回临时目录", function()
        local d = sys.tmpdir()
        T.assert_type(d, "string"); T.assert_gt(#d, 0)
    end)

    T.test("sys.homedir: 返回用户目录", function()
        local d = sys.homedir()
        T.assert_type(d, "string"); T.assert_gt(#d, 0)
    end)

    T.test("sys.username: 返回用户名", function()
        local u = sys.username()
        T.assert_type(u, "string"); T.assert_gt(#u, 0)
        T.log("  用户: " .. u)
    end)

    -- 环境变量
    T.test("sys.get_env: 读取PATH环境变量", function()
        local p = sys.get_env("PATH")
        T.assert_type(p, "string"); T.assert_gt(#p, 0)
    end)

    T.test("sys.set_env: 设置环境变量", function()
        T.assert_true(sys.set_env("AETHER_TEST_VAR", "test_value"))
        T.assert_eq(sys.get_env("AETHER_TEST_VAR"), "test_value")
    end)

    -- 共享内存 (跨脚本通信)
    T.test("sys.set_share/get_share: 整数", function()
        sys.set_share("_test_int", 12345)
        T.assert_eq(sys.get_share("_test_int"), 12345)
        sys.set_share("_test_int", -999)
        T.assert_eq(sys.get_share("_test_int"), -999)
    end)

    T.test("sys.set_share/get_share: 浮点数", function()
        sys.set_share("_test_float", 3.14159)
        T.assert_true(math.abs(sys.get_share("_test_float") - 3.14159) < 0.0001)
    end)

    T.test("sys.set_share/get_share: 字符串", function()
        sys.set_share("_test_str", "Hello")
        T.assert_eq(sys.get_share("_test_str"), "Hello")
        sys.set_share("_test_cn", "中文测试")
        T.assert_eq(sys.get_share("_test_cn"), "中文测试")
    end)

    T.test("sys.set_share/get_share: 布尔值", function()
        sys.set_share("_test_bool", true)
        T.assert_eq(sys.get_share("_test_bool"), true)
        sys.set_share("_test_bool", false)
        T.assert_eq(sys.get_share("_test_bool"), false)
    end)

    T.test("sys.get_share: 不存在键返回nil", function()
        T.assert_nil(sys.get_share("_nonexistent_key_xyz"))
    end)

    T.test("sys.set_share: nil清除共享变量", function()
        sys.set_share("_test_clear", "exists")
        T.assert_eq(sys.get_share("_test_clear"), "exists")
        sys.set_share("_test_clear", nil)
        T.assert_nil(sys.get_share("_test_clear"), "nil应清除变量")
    end)

    T.test("sys.get_env: 不存在的环境变量返回nil", function()
        local val = sys.get_env("AETHER_NONEXISTENT_VAR_12345")
        T.assert_nil(val, "不存在的环境变量应返回nil")
    end)

    -- sys.log 已删除，使用 log 模块替代

    -- 命令执行
    T.test("sys.exec: 执行echo命令", function()
        local code, out = sys.exec("echo hello")
        T.assert_eq(code, 0)
        T.assert_type(out, "string"); T.assert_true(out:find("hello") ~= nil)
    end)

    T.test("sys.exec: 失败命令返回非零", function()
        local code, out = sys.exec("cmd /c exit 1")
        T.assert_eq(code, 1, "exit 1 应返回退出码 1")
    end)

    -- 剪贴板
    T.test("sys.set_clipboard/get_clipboard: 读写剪贴板", function()
        T.assert_type(sys.set_clipboard, "function")
        T.assert_type(sys.get_clipboard, "function")
        -- 实际读写测试
        local test_str = "AetherClipTest_" .. tostring(sys.time())
        sys.set_clipboard(test_str)
        sys.sleep(50)
        local got = sys.get_clipboard()
        T.assert_eq(got, test_str, "剪贴板读写应一致")
        T.log("  剪贴板: " .. test_str)
    end)

    -- -- PE内存加载
    -- T.test("sys.mmap_pe: 函数存在", function()
    --     T.assert_type(sys.mmap_pe, "function")
    --     T.assert_type(sys.free_pe, "function")
    -- end)

    -- T.test("sys.mmap_pe: 无效数据返回错误", function()
    --     local pe, err = sys.mmap_pe("invalid")
    --     T.assert_nil(pe); T.assert_type(err, "string")
    --     T.log("  预期错误: " .. err)
    -- end)

    -- T.test("sys.mmap_pe: 加载TestDll并调用DllMain", function()
    --     local dll_path = "TestDll.dll"
    --     local f = io.open(dll_path, "rb")
    --     if not f then
    --         T.log("  [SKIP] DLL不存在: " .. dll_path)
    --         return
    --     end
    --     local data = f:read("*a")
    --     f:close()
    --     T.assert_gt(#data, 0, "DLL数据不应为空")
    --     T.log(string.format("  DLL大小: %d bytes", #data))

    --     -- 加载PE, 第二个参数true表示调用DllMain(DLL_PROCESS_ATTACH)
    --     local pe, err = sys.mmap_pe(data, true)
    --     T.assert_not_nil(pe, "加载应成功: " .. (err or ""))
    --     T.assert_type(pe.base, "number")
    --     T.assert_gt(pe.base, 0, "基址应大于0")
    --     T.assert_type(pe.size, "number")
    --     T.assert_gt(pe.size, 0, "映像大小应大于0")
    --     T.assert_type(pe.exports, "table")
    --     T.log(string.format("  加载成功: base=0x%X size=%d", pe.base, pe.size))

    --     -- 打印导出函数
    --     local export_count = 0
    --     for name, addr in pairs(pe.exports) do
    --         T.log(string.format("  导出: %s @ 0x%X", name, addr))
    --         export_count = export_count + 1
    --     end
    --     T.log(string.format("  导出函数数: %d", export_count))

    --     -- 释放PE
    --     local ok = sys.free_pe(pe.base)
    --     T.assert_true(ok, "释放应成功")
    --     T.log("  已释放PE内存")
    -- end)

    -- DPI 和鼠标加速度 API
    T.log("\n--- DPI/鼠标设置 API ---")
    
    T.test("sys.dpi: 返回DPI缩放信息", function()
        local dpi = sys.dpi()
        T.assert_type(dpi, "table")
        T.assert_type(dpi.scale, "number")
        T.assert_type(dpi.x, "number")
        T.assert_type(dpi.y, "number")
        T.assert_gt(dpi.scale, 0, "缩放比例应大于0")
        T.assert_gt(dpi.x, 0, "DPI X应大于0")
        T.log(string.format("  DPI: scale=%.2f, x=%d, y=%d", dpi.scale, dpi.x, dpi.y))
    end)

    T.test("sys.screen_size: 返回屏幕尺寸", function()
        local w, h = sys.screen_size()
        T.assert_type(w, "number")
        T.assert_type(h, "number")
        T.assert_gt(w, 0, "宽度应大于0")
        T.assert_gt(h, 0, "高度应大于0")
        T.log(string.format("  屏幕: %dx%d", w, h))
    end)

    -- mouse_accel/mouse_speed 已迁移到 mouse 模块

    -- sys.set_cwd 测试
    T.test("sys.set_cwd: 设置工作目录", function()
        local original = sys.get_cwd()
        T.assert_type(original, "string")
        -- 切换到临时目录再切回
        local tmp = sys.tmpdir()
        if tmp and #tmp > 0 then
            local ok = sys.set_cwd(tmp)
            T.assert_type(ok, "boolean")
            if ok then
                local newCwd = sys.get_cwd()
                T.log("  临时CWD: " .. newCwd)
            end
            -- 恢复原始目录
            sys.set_cwd(original)
            T.assert_eq(sys.get_cwd(), original, "应恢复原始目录")
        end
    end)

    T.test("sys.set_cwd: 无效目录返回false", function()
        local ok = sys.set_cwd("Z:\\nonexistent_dir_12345")
        T.assert_false(ok, "无效目录应返回false")
    end)

    -- sys.exit / sys.msgbox / sys.debug 存在性测试 (不实际调用，避免副作用)
    T.log("\n--- 危险API存在性 ---")

    T.test("sys.exit: 函数存在", function()
        T.assert_type(sys.exit, "function")
    end)

    T.test("sys.msgbox: 函数存在", function()
        T.assert_type(sys.msgbox, "function")
    end)

    T.test("sys.debug: 函数存在", function()
        T.assert_type(sys.debug, "function")
    end)

    T.test("sys.suicide: 函数存在", function()
        T.assert_type(sys.suicide, "function")
    end)

    T.test("sys.winver: 函数存在", function()
        T.assert_type(sys.winver, "function")
    end)

    return T.report("sys")
end

return { run = run }
