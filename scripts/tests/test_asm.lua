--[[
    asm 模块测试
    测试汇编器功能: 编译汇编代码、生成可执行内存、通过 ffi 调用
    支持架构: x86, x64, ARM, Thumb, ARM64
]]
local T = require("tests.test_framework")

local function run()
    log.info("\n--- asm module ---")
    T.reset()

    -- 检测当前架构
    local current_arch = asm.arch()
    log.info("  Current architecture: " .. current_arch)

    -- 测试 1: 模块存在性检查
    T.test(
        "asm: 模块存在",
        function()
            T.assert_not_nil(asm, "asm module should exist")
            T.assert_type(asm.compile, "function", "asm.compile should be function")
            T.assert_type(asm.arch, "function", "asm.arch should be function")
            T.assert_type(asm.archs, "function", "asm.archs should be function")
            T.assert_type(asm.emit, "function", "asm.emit should be function")
        end
    )

    -- 测试 2: 获取当前架构
    T.test(
        "asm: 获取当前架构",
        function()
            local arch = asm.arch()
            T.assert_not_nil(arch, "arch should not be nil")
            T.assert_type(arch, "string", "arch should be string")

            -- 验证是已知架构
            local known = {x86 = true, x64 = true, arm = true, thumb = true, arm64 = true}
            T.assert_true(known[arch], "arch should be known: " .. arch)
            log.info("  Current arch: " .. arch)
        end
    )

    -- 测试 3: 获取支持的架构列表
    T.test(
        "asm: 支持的架构列表",
        function()
            local archs = asm.archs()
            T.assert_not_nil(archs, "archs should not be nil")
            T.assert_type(archs, "table", "archs should be table")
            T.assert_gte(#archs, 5, "should support at least 5 architectures")

            log.info("  Supported architectures: " .. table.concat(archs, ", "))
        end
    )

    -- 测试 4: 编译简单汇编 (根据架构)
    T.test(
        "asm: 编译简单汇编代码",
        function()
            local code, err

            if current_arch == "x64" then
                -- x64: mov rax, 42; ret
                code, err = asm.compile("mov rax, 42; ret")
            elseif current_arch == "x86" then
                -- x86: mov eax, 42; ret
                code, err = asm.compile("mov eax, 42; ret")
            elseif current_arch == "arm64" then
                -- ARM64: mov x0, #42; ret
                code, err = asm.compile("mov x0, #42; ret")
            elseif current_arch == "arm" then
                -- ARM: mov r0, #42; bx lr
                code, err = asm.compile("mov r0, #42; bx lr")
            else
                log.info("  Skipping: unsupported arch " .. current_arch)
                return
            end

            T.assert_not_nil(code, "compile should succeed: " .. tostring(err))
            T.assert_true(code:valid(), "code block should be valid")
            T.assert_gt(code:size(), 0, "code size should be > 0")
            T.assert_not_nil(code:ptr(), "code ptr should not be nil")

            log.info("  Compiled size: " .. code:size() .. " bytes")
            log.info("  Machine code: " .. code:hex())
        end
    )

    -- 测试 5: CodeBlock 方法
    T.test(
        "asm: CodeBlock 方法",
        function()
            local code, err

            if current_arch == "x64" then
                code, err = asm.compile("nop; nop; nop; ret")
            elseif current_arch == "x86" then
                code, err = asm.compile("nop; nop; nop; ret")
            elseif current_arch == "arm64" then
                code, err = asm.compile("nop; nop; nop; ret")
            elseif current_arch == "arm" then
                code, err = asm.compile("nop; nop; nop; bx lr")
            else
                return
            end

            T.assert_not_nil(code, "compile should succeed: " .. tostring(err))

            -- 测试 valid()
            T.assert_true(code:valid(), "valid() should return true")

            -- 测试 size()
            local size = code:size()
            T.assert_type(size, "number", "size() should return number")
            T.assert_gt(size, 0, "size should be > 0")

            -- 测试 ptr()
            local ptr = code:ptr()
            T.assert_not_nil(ptr, "ptr() should return non-nil")

            -- 测试 hex()
            local hex = code:hex()
            T.assert_type(hex, "string", "hex() should return string")
            T.assert_gt(#hex, 0, "hex string should not be empty")

            log.info("  size: " .. size)
            log.info("  hex: " .. hex)
        end
    )

    -- 测试 6: emit 从字节数组生成代码
    T.test(
        "asm: emit 从字节数组生成",
        function()
            local bytes

            if current_arch == "x64" then
                -- x64: mov eax, 123 (0x7B); ret
                -- B8 7B 00 00 00  mov eax, 0x7B
                -- C3              ret
                bytes = {0xB8, 0x7B, 0x00, 0x00, 0x00, 0xC3}
            elseif current_arch == "x86" then
                -- x86: mov eax, 123; ret
                bytes = {0xB8, 0x7B, 0x00, 0x00, 0x00, 0xC3}
            elseif current_arch == "arm64" then
                -- ARM64: mov w0, #123; ret
                -- 简化: 使用 nop; ret
                bytes = {0x1F, 0x20, 0x03, 0xD5, 0xC0, 0x03, 0x5F, 0xD6}
            else
                log.info("  Skipping emit test for arch: " .. current_arch)
                return
            end

            local code, err = asm.emit(bytes)
            T.assert_not_nil(code, "emit should succeed: " .. tostring(err))
            T.assert_true(code:valid(), "emitted code should be valid")
            T.assert_eq(code:size(), #bytes, "size should match input")

            log.info("  Emitted " .. #bytes .. " bytes")
            log.info("  hex: " .. code:hex())
        end
    )

    -- 测试 7: 通过 ffi 调用生成的代码
    T.test(
        "asm: 通过 ffi 调用机器码",
        function()
            -- 仅在 x64/x86 平台测试
            if current_arch ~= "x64" and current_arch ~= "x86" then
                log.info("  Skipping ffi call test for arch: " .. current_arch)
                return
            end

            local ffi_ok, ffi = pcall(require, "cffi")
            if not ffi_ok then
                log.info("  Skipping: cffi not available")
                return
            end

            -- 编译返回常量 42 的函数
            local code, err
            -- 注意: Keystone 默认把裸数字当十六进制，42 十进制 = 0x2a
            if current_arch == "x64" then
                code, err = asm.compile("mov eax, 0x2a; ret") -- 0x2a = 42 decimal
            else
                code, err = asm.compile("mov eax, 0x2a; ret") -- 0x2a = 42 decimal
            end

            T.assert_not_nil(code, "compile should succeed: " .. tostring(err))

            -- 定义函数类型
            ffi.cdef [[
            typedef int (*ReturnInt)(void);
        ]]

            -- 转换指针为函数
            local func = ffi.cast("ReturnInt", code:ptr())
            T.assert_not_nil(func, "ffi.cast should succeed")

            -- 调用并验证结果
            local result = func()
            T.assert_eq(result, 42, "function should return 42 (0x2a)")

            log.info("  ffi call result: " .. tostring(result))
        end
    )

    -- 测试 8: 编译带参数的函数
    T.test(
        "asm: 编译带参数的函数",
        function()
            if current_arch ~= "x64" and current_arch ~= "x86" then
                log.info("  Skipping: only x64/x86 supported")
                return
            end

            local ffi_ok, ffi = pcall(require, "cffi")
            if not ffi_ok then
                log.info("  Skipping: cffi not available")
                return
            end

            local code, err
            if current_arch == "x64" then
                -- Windows x64: 第一个参数在 rcx
                -- 返回 rcx + 10 (0xa = 10 decimal)
                code, err = asm.compile([[
                lea rax, [rcx + 0xa]
                ret
            ]])
            else
                -- x86 cdecl: 参数在栈上 [esp+4]
                code, err =
                    asm.compile(
                    [[
                mov eax, [esp + 4]
                add eax, 0xa
                ret
            ]]
                )
            end

            T.assert_not_nil(code, "compile should succeed: " .. tostring(err))

            -- 定义带参数的函数类型
            local func_type
            if current_arch == "x64" then
                ffi.cdef [[
                typedef long long (*AddTen64)(long long x);
            ]]
                func_type = "AddTen64"
            else
                ffi.cdef [[
                typedef int (__cdecl *AddTen32)(int x);
            ]]
                func_type = "AddTen32"
            end

            local func = ffi.cast(func_type, code:ptr())

            -- 测试多个输入
            local test_values = {0, 5, 100, -10}
            for _, v in ipairs(test_values) do
                local result = func(v)
                T.assert_eq(result, v + 10, "AddTen(" .. v .. ") should be " .. (v + 10))
            end

            log.info("  AddTen function works correctly")
        end
    )

    -- 测试 9: 编译加法函数
    T.test(
        "asm: 编译双参数加法函数",
        function()
            if current_arch ~= "x64" then
                log.info("  Skipping: only x64 supported")
                return
            end

            local ffi_ok, ffi = pcall(require, "cffi")
            if not ffi_ok then
                log.info("  Skipping: cffi not available")
                return
            end

            -- Windows x64: rcx = 第一参数, rdx = 第二参数
            local code, err = asm.compile([[
            lea rax, [rcx + rdx]
            ret
        ]])

            T.assert_not_nil(code, "compile should succeed: " .. tostring(err))

            ffi.cdef [[
            typedef long long (*Add64)(long long a, long long b);
        ]]

            local func = ffi.cast("Add64", code:ptr())

            -- 测试
            T.assert_eq(func(1, 2), 3, "1 + 2 = 3")
            T.assert_eq(func(100, 200), 300, "100 + 200 = 300")
            T.assert_eq(func(-5, 10), 5, "-5 + 10 = 5")

            log.info("  Add function works correctly")
        end
    )

    -- 测试 10: 指定架构编译
    T.test(
        "asm: 指定架构编译",
        function()
            -- 测试指定 x64 架构
            local code, err = asm.compile("nop; ret", "x64")
            T.assert_not_nil(code, "compile for x64 should succeed: " .. tostring(err))
            log.info("  x64: " .. code:hex())

            -- 测试指定 x86 架构
            code, err = asm.compile("nop; ret", "x86")
            T.assert_not_nil(code, "compile for x86 should succeed: " .. tostring(err))
            log.info("  x86: " .. code:hex())

            -- 测试指定 arm64 架构
            code, err = asm.compile("nop; ret", "arm64")
            T.assert_not_nil(code, "compile for arm64 should succeed: " .. tostring(err))
            log.info("  arm64: " .. code:hex())
        end
    )

    -- 测试 11: 编译错误处理
    T.test(
        "asm: 编译错误处理",
        function()
            -- 无效的汇编代码
            local code, err = asm.compile("invalid_instruction_xyz")
            T.assert_nil(code, "invalid code should return nil")
            T.assert_not_nil(err, "should return error message")
            T.assert_type(err, "string", "error should be string")

            log.info("  Error message: " .. err)
        end
    )

    -- 测试 12: 无效架构处理
    T.test(
        "asm: 无效架构处理",
        function()
            local code, err = asm.compile("nop", "invalid_arch")
            T.assert_nil(code, "invalid arch should return nil")
            T.assert_not_nil(err, "should return error message")

            log.info("  Error: " .. err)
        end
    )

    -- 测试 13: 空输入处理
    T.test(
        "asm: 空字节数组处理",
        function()
            local code, err = asm.emit({})
            T.assert_nil(code, "empty bytes should return nil")
            T.assert_not_nil(err, "should return error message")

            log.info("  Error: " .. err)
        end
    )

    return T.report("asm")
end

return {run = run}
