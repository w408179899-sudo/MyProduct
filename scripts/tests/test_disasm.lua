--[[
    disasm 模块测试 - 反汇编 (Capstone)
    覆盖: disassemble/open/句柄方法/指令字段/错误处理
]]
local T = require("tests.test_framework")

local function run()
    T.reset()
    T.log("\n=== disasm 模块测试 ===")

    -- 测试 1: 模块存在性检查
    T.test("disasm: 模块存在", function()
        T.assert_not_nil(disasm, "disasm module should exist")
        T.assert_type(disasm.disassemble, "function")
        T.assert_type(disasm.open, "function")
    end)

    -- x64 测试数据: mov rax, rbx (48 89 D8) + ret (C3)
    local x64_bytes = string.char(0x48, 0x89, 0xD8, 0xC3)
    local x64_addr = 0x140001000

    -- 测试 2: 一次性反汇编 (string 输入)
    T.test("disasm: disassemble (string)", function()
        local insns, err = disasm.disassemble(x64_bytes, x64_addr)
        T.assert_not_nil(insns, "disassemble should succeed: " .. tostring(err))
        T.assert_eq(#insns, 2, "should have 2 instructions")

        -- 第一条: mov rax, rbx
        local i1 = insns[1]
        T.assert_eq(i1.address, x64_addr)
        T.assert_eq(i1.size, 3)
        T.assert_eq(i1.mnemonic, "mov")
        T.assert_not_nil(i1.operands)
        T.assert_not_nil(i1.text)
        T.assert_not_nil(i1.hex)
        T.assert_eq(i1.is_call, false)
        T.assert_eq(i1.is_jump, false)
        T.assert_eq(i1.is_ret, false)
        T.log("  insn[1]: " .. i1.text .. " (" .. i1.hex .. ")")

        -- 第二条: ret
        local i2 = insns[2]
        T.assert_eq(i2.address, x64_addr + 3)
        T.assert_eq(i2.size, 1)
        T.assert_eq(i2.mnemonic, "ret")
        T.assert_eq(i2.is_ret, true)
        T.log("  insn[2]: " .. i2.text .. " (" .. i2.hex .. ")")
    end)

    -- 测试 3: 一次性反汇编 (table 输入)
    T.test("disasm: disassemble (table)", function()
        local bytes_table = {0x48, 0x89, 0xD8, 0xC3}
        local insns, err = disasm.disassemble(bytes_table, 0x1000, "x64")
        T.assert_not_nil(insns, "table input should work: " .. tostring(err))
        T.assert_eq(#insns, 2)
        T.assert_eq(insns[1].mnemonic, "mov")
        T.assert_eq(insns[2].mnemonic, "ret")
    end)

    -- 测试 4: max_count 限制
    T.test("disasm: max_count 限制", function()
        -- nop(90) x 5 + ret(C3)
        local code = string.char(0x90, 0x90, 0x90, 0x90, 0x90, 0xC3)
        local insns = disasm.disassemble(code, 0x1000, "x64", 3)
        T.assert_not_nil(insns)
        T.assert_eq(#insns, 3, "should limit to 3 instructions")
        T.log("  max_count=3, got " .. #insns .. " instructions")
    end)

    -- 测试 5: call 指令识别
    T.test("disasm: call 指令识别", function()
        -- call rel32: E8 xx xx xx xx
        local code = string.char(0xE8, 0x10, 0x00, 0x00, 0x00)
        local insns = disasm.disassemble(code, 0x1000, "x64", 1)
        T.assert_not_nil(insns)
        T.assert_eq(#insns, 1)
        T.assert_eq(insns[1].is_call, true)
        T.assert_eq(insns[1].is_jump, false)
        T.assert_eq(insns[1].is_ret, false)
        T.log("  call detected: " .. insns[1].text)
    end)

    -- 测试 6: jmp 指令识别
    T.test("disasm: jmp 指令识别", function()
        -- jmp rel8: EB xx
        local code = string.char(0xEB, 0x10)
        local insns = disasm.disassemble(code, 0x1000, "x64", 1)
        T.assert_not_nil(insns)
        T.assert_eq(insns[1].is_jump, true)
        T.assert_eq(insns[1].is_call, false)
        T.log("  jmp detected: " .. insns[1].text)
    end)

    -- 测试 7: 指令字段完整性
    T.test("disasm: 指令字段完整性", function()
        -- mov [rax+0x10], rcx  =>  48 89 48 10
        local code = string.char(0x48, 0x89, 0x48, 0x10)
        local insns = disasm.disassemble(code, 0x1000, "x64", 1)
        T.assert_not_nil(insns)
        local i = insns[1]

        -- 基本字段
        T.assert_type(i.address, "number")
        T.assert_type(i.size, "number")
        T.assert_type(i.mnemonic, "string")
        T.assert_type(i.operands, "string")
        T.assert_type(i.text, "string")
        T.assert_type(i.bytes, "string")
        T.assert_type(i.hex, "string")

        -- 布尔字段
        T.assert_type(i.is_call, "boolean")
        T.assert_type(i.is_jump, "boolean")
        T.assert_type(i.is_ret, "boolean")
        T.assert_type(i.has_rip_rel, "boolean")

        -- 数值字段
        T.assert_type(i.imm, "number")
        T.assert_type(i.disp, "number")
        T.assert_type(i.rip_disp, "number")

        T.log("  字段完整: " .. i.text)
    end)

    -- 测试 8: x86 操作数详情 (ops)
    T.test("disasm: x86 操作数详情", function()
        -- mov rax, rbx => 48 89 D8
        local insns = disasm.disassemble(x64_bytes, 0x1000, "x64", 1)
        T.assert_not_nil(insns)
        local i = insns[1]
        T.assert_not_nil(i.ops, "ops should exist for mov")
        T.assert_true(#i.ops >= 2, "mov should have at least 2 operands")

        for idx, op in ipairs(i.ops) do
            T.assert_not_nil(op.type)
            T.log(string.format("  op[%d]: type=%s reg=%d imm=%d",
                idx, op.type, op.reg, op.imm))
        end
    end)

    -- 测试 9: 复用句柄
    T.test("disasm: open/句柄复用", function()
        local d, err = disasm.open("x64")
        T.assert_not_nil(d, "open should succeed: " .. tostring(err))
        T.assert_eq(d:is_open(), true)
        T.assert_eq(d:arch(), "x64")

        -- 第一次反汇编
        local insns1 = d:disasm(x64_bytes, 0x1000)
        T.assert_not_nil(insns1)
        T.assert_eq(#insns1, 2)

        -- 第二次反汇编 (复用同一句柄)
        local code2 = string.char(0x90, 0x90, 0xC3)  -- nop; nop; ret
        local insns2 = d:disasm(code2, 0x2000)
        T.assert_not_nil(insns2)
        T.assert_eq(#insns2, 3)

        d:close()
        T.assert_eq(d:is_open(), false)
        T.log("  句柄复用正确")
    end)

    -- 测试 10: x86 架构
    T.test("disasm: x86 架构", function()
        -- x86: mov eax, 0x42 (B8 42 00 00 00) + ret (C3)
        local code = string.char(0xB8, 0x42, 0x00, 0x00, 0x00, 0xC3)
        local insns = disasm.disassemble(code, 0x401000, "x86")
        T.assert_not_nil(insns)
        T.assert_eq(#insns, 2)
        T.assert_eq(insns[1].mnemonic, "mov")
        T.assert_eq(insns[2].mnemonic, "ret")
        T.log("  x86: " .. insns[1].text)
    end)

    -- 测试 11: 关闭后调用报错
    T.test("disasm: 关闭后调用", function()
        local d = disasm.open("x64")
        T.assert_not_nil(d)
        d:close()

        local insns, err = d:disasm(x64_bytes, 0x1000)
        T.assert_nil(insns, "closed handle should return nil")
        T.assert_not_nil(err)
        T.log("  关闭后错误: " .. err)
    end)

    -- 测试 12: 无效架构处理
    T.test("disasm: 无效架构", function()
        local d, err = disasm.open("invalid_arch")
        T.assert_nil(d, "invalid arch should return nil")
        T.assert_not_nil(err)
        T.assert_type(err, "string")
        T.log("  错误: " .. err)
    end)

    -- 测试 13: 空输入处理
    T.test("disasm: 空输入", function()
        local insns, err = disasm.disassemble("", 0x1000)
        T.assert_nil(insns, "empty input should fail")
        T.assert_not_nil(err)
        T.log("  空输入错误: " .. err)
    end)

    -- 测试 14: 大量指令反汇编
    T.test("disasm: 大量 NOP 反汇编", function()
        -- 100 个 nop + ret
        local nops = string.rep(string.char(0x90), 100) .. string.char(0xC3)
        local insns = disasm.disassemble(nops, 0x1000, "x64")
        T.assert_not_nil(insns)
        T.assert_eq(#insns, 101, "should have 101 instructions")
        T.assert_eq(insns[101].mnemonic, "ret")
        T.log("  100 NOPs + RET 反汇编正确")
    end)

    -- 测试 15: RIP 相对寻址
    T.test("disasm: RIP 相对寻址", function()
        -- lea rax, [rip+0x10]  =>  48 8D 05 10 00 00 00
        local code = string.char(0x48, 0x8D, 0x05, 0x10, 0x00, 0x00, 0x00)
        local insns = disasm.disassemble(code, 0x1000, "x64", 1)
        T.assert_not_nil(insns)
        T.assert_eq(insns[1].has_rip_rel, true, "should detect RIP-relative")
        T.log("  RIP相对: " .. insns[1].text .. " rip_disp=" .. insns[1].rip_disp)
    end)

    return T.report("disasm")
end

return { run = run }
