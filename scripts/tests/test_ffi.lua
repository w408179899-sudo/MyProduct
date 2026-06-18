--[[
    ffi 模块测试
    测试 cffi-lua 调用 Windows API
    使用 ntdll.dll 的 RtlGetVersion 和 NtQuerySystemInformation
]]
local T = require("tests.test_framework")

local function run()
    -- 仅在 Windows 平台运行
    if sys.platform() ~= "windows" then
        log.info("\n--- ffi module (SKIPPED: Windows only) ---")
        return true
    end

    log.info("\n--- ffi module ---")
    T.reset()

    -- 检查 FFI 是否可用
    local ffi_available = false
    local ffi = nil

    T.test(
        "ffi: 加载 cffi 模块",
        function()
            local ok, result = pcall(require, "cffi")
            if ok then
                ffi = result
                ffi_available = true
                log.info("  cffi-lua loaded successfully")
            else
                log.info("  cffi-lua not available: " .. tostring(result))
            end
        end
    )

    if not ffi_available then
        log.info("  Skipping FFI tests (cffi not available)")
        return T.report("ffi")
    end

    T.test(
        "ffi: 定义 Windows 类型",
        function()
            ffi.cdef [[
            typedef unsigned long ULONG;
            typedef unsigned short USHORT;
            typedef unsigned char UCHAR;
            typedef long NTSTATUS;
            typedef wchar_t WCHAR;
            
            typedef struct _OSVERSIONINFOEXW {
                ULONG dwOSVersionInfoSize;
                ULONG dwMajorVersion;
                ULONG dwMinorVersion;
                ULONG dwBuildNumber;
                ULONG dwPlatformId;
                WCHAR szCSDVersion[128];
                USHORT wServicePackMajor;
                USHORT wServicePackMinor;
                USHORT wSuiteMask;
                UCHAR wProductType;
                UCHAR wReserved;
            } OSVERSIONINFOEXW, *POSVERSIONINFOEXW, *LPOSVERSIONINFOEXW;
            
            NTSTATUS __stdcall RtlGetVersion(LPOSVERSIONINFOEXW lpVersionInformation);
            unsigned long __stdcall GetTickCount(void);
            unsigned long __stdcall GetCurrentProcessId(void);
        ]]

            T.assert_true(true, "FFI types defined")
        end
    )

    -- 加载 ntdll.dll
    local ntdll = nil
    T.test(
        "ffi: 加载 ntdll.dll",
        function()
            ntdll = ffi.load("ntdll")
            T.assert_not_nil(ntdll)
            log.info("  ntdll.dll loaded")
        end
    )

    -- 调用 RtlGetVersion
    T.test(
        "ffi: RtlGetVersion 获取 Windows 版本",
        function()
            if not ntdll then
                return
            end

            local osvi = ffi.new("OSVERSIONINFOEXW")
            osvi.dwOSVersionInfoSize = ffi.sizeof("OSVERSIONINFOEXW")

            local status = ntdll.RtlGetVersion(osvi)
            T.assert_eq(status, 0, "RtlGetVersion should return STATUS_SUCCESS (0)")

            local major = osvi.dwMajorVersion
            local minor = osvi.dwMinorVersion
            local build = osvi.dwBuildNumber

            T.assert_gte(major, 6, "Major version should be >= 6 (Vista+)")
            T.assert_gt(build, 0, "Build number should be > 0")

            log.info(string.format("  Windows Version: %d.%d.%d", major, minor, build))

            -- Windows 10/11 检测
            if major == 10 then
                if build >= 22000 then
                    log.info("  Detected: Windows 11")
                else
                    log.info("  Detected: Windows 10")
                end
            elseif major == 6 then
                if minor == 3 then
                    log.info("  Detected: Windows 8.1")
                elseif minor == 2 then
                    log.info("  Detected: Windows 8")
                elseif minor == 1 then
                    log.info("  Detected: Windows 7")
                end
            end
        end
    )

    -- 测试 kernel32 API (函数已在上面的 cdef 中定义)
    local kernel32 = ffi.load("kernel32")

    T.test(
        "ffi: kernel32 GetTickCount",
        function()
            local tick = kernel32.GetTickCount()

            T.assert_gt(tick, 0, "TickCount should be > 0")

            local hours = tick / 1000 / 60 / 60
            log.info(string.format("  System Uptime: %.2f hours", hours))
        end
    )

    T.test(
        "ffi: kernel32 GetCurrentProcessId",
        function()
            local pid = kernel32.GetCurrentProcessId()

            T.assert_gt(pid, 0, "PID should be > 0")
            log.info(string.format("  Current PID: %d", pid))
        end
    )

    return T.report("ffi")
end

return {run = run}
