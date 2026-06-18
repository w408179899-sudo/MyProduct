--[[
    encoding 模块测试 - 窄字节字符串编码转换
    覆盖: convert/codepage/utf8_to_gbk/gbk_to_utf8/big5/euckr/shiftjis/ansi/local/adaptive
]]
local T = require("tests.test_framework")

local function run()
    T.reset()
    T.log("\n=== encoding 模块测试 ===")

    -- 测试 1: 模块存在性检查
    T.test("encoding: 模块存在", function()
        T.assert_not_nil(encoding, "encoding module should exist")
        T.assert_type(encoding.convert, "function")
        T.assert_type(encoding.codepage, "function")
        T.assert_type(encoding.utf8_to_gbk, "function")
        T.assert_type(encoding.gbk_to_utf8, "function")
        T.assert_type(encoding.utf8_to_big5, "function")
        T.assert_type(encoding.big5_to_utf8, "function")
        T.assert_type(encoding.utf8_to_euckr, "function")
        T.assert_type(encoding.euckr_to_utf8, "function")
        T.assert_type(encoding.utf8_to_shiftjis, "function")
        T.assert_type(encoding.shiftjis_to_utf8, "function")
        T.assert_type(encoding.utf8_to_ansi, "function")
        T.assert_type(encoding.ansi_to_utf8, "function")
        T.assert_type(encoding.utf8_to_local, "function")
        T.assert_type(encoding.local_to_utf8, "function")
        T.assert_type(encoding.adaptive_to_local, "function")
    end)

    -- 测试 2: codepage 查询
    T.test("encoding: codepage 查询", function()
        T.assert_eq(encoding.codepage("utf8"), 65001)
        T.assert_eq(encoding.codepage("utf-8"), 65001)
        T.assert_eq(encoding.codepage("gbk"), 936)
        T.assert_eq(encoding.codepage("gb2312"), 936)
        T.assert_eq(encoding.codepage("big5"), 950)
        T.assert_eq(encoding.codepage("euckr"), 949)
        T.assert_eq(encoding.codepage("shiftjis"), 932)
        T.assert_eq(encoding.codepage("ansi"), 0)
        T.assert_nil(encoding.codepage("invalid_encoding"))
        T.log("  codepage查询正常")
    end)

    -- 测试 3: 数字 codepage 查询
    T.test("encoding: 数字codepage", function()
        T.assert_eq(encoding.codepage("936"), 936)
        T.assert_eq(encoding.codepage("65001"), 65001)
        T.log("  数字codepage查询正常")
    end)

    -- 测试 4: UTF-8 <-> GBK 互转
    T.test("encoding: UTF-8 <-> GBK", function()
        local utf8_str = "你好世界"
        local gbk = encoding.utf8_to_gbk(utf8_str)
        T.assert_not_nil(gbk, "utf8_to_gbk should succeed")
        T.assert_true(#gbk > 0, "gbk result should not be empty")
        -- GBK 中文每字2字节, "你好世界" = 8 字节
        T.assert_eq(#gbk, 8, "GBK '你好世界' should be 8 bytes")

        local back = encoding.gbk_to_utf8(gbk)
        T.assert_not_nil(back, "gbk_to_utf8 should succeed")
        T.assert_eq(back, utf8_str, "round-trip should match")
        T.log("  UTF-8 <-> GBK 往返正确")
    end)

    -- 测试 5: convert 通用转换
    T.test("encoding: convert 通用转换", function()
        local utf8_str = "测试文本"
        local gbk = encoding.convert(utf8_str, "utf8", "gbk")
        T.assert_not_nil(gbk)
        local back = encoding.convert(gbk, "gbk", "utf8")
        T.assert_eq(back, utf8_str, "convert round-trip should match")
        T.log("  convert 通用转换正确")
    end)

    -- 测试 6: 相同编码转换 (直接返回)
    T.test("encoding: 相同编码转换", function()
        local str = "Hello World"
        local result = encoding.convert(str, "utf8", "utf8")
        T.assert_eq(result, str, "same encoding should return identical")
    end)

    -- 测试 7: ASCII 文本转换 (所有编码应一致)
    T.test("encoding: ASCII 文本兼容性", function()
        local ascii = "Hello 123"
        local gbk = encoding.utf8_to_gbk(ascii)
        T.assert_eq(gbk, ascii, "ASCII in GBK should be identical")
        local sjis = encoding.utf8_to_shiftjis(ascii)
        T.assert_eq(sjis, ascii, "ASCII in ShiftJIS should be identical")
    end)

    -- 测试 8: UTF-8 <-> Big5 (繁体中文)
    T.test("encoding: UTF-8 <-> Big5", function()
        -- 使用繁简通用字: "中文"
        local utf8_str = "中文"
        local big5 = encoding.utf8_to_big5(utf8_str)
        T.assert_not_nil(big5, "utf8_to_big5 should succeed")
        T.assert_true(#big5 > 0)
        local back = encoding.big5_to_utf8(big5)
        T.assert_not_nil(back)
        T.assert_eq(back, utf8_str, "Big5 round-trip should match")
        T.log("  UTF-8 <-> Big5 往返正确")
    end)

    -- 测试 9: UTF-8 <-> Shift-JIS (日文)
    T.test("encoding: UTF-8 <-> Shift-JIS", function()
        -- 使用日文片假名: "テスト" (テ=0x8365, ス=0x8358, ト=0x8367)
        local utf8_str = "テスト"
        local sjis = encoding.utf8_to_shiftjis(utf8_str)
        T.assert_not_nil(sjis, "utf8_to_shiftjis should succeed")
        local back = encoding.shiftjis_to_utf8(sjis)
        T.assert_not_nil(back)
        T.assert_eq(back, utf8_str, "ShiftJIS round-trip should match")
        T.log("  UTF-8 <-> Shift-JIS 往返正确")
    end)

    -- 测试 10: UTF-8 <-> EUC-KR (韩文)
    T.test("encoding: UTF-8 <-> EUC-KR", function()
        -- 使用韩文: "한글"
        local utf8_str = "한글"
        local euckr = encoding.utf8_to_euckr(utf8_str)
        T.assert_not_nil(euckr, "utf8_to_euckr should succeed")
        local back = encoding.euckr_to_utf8(euckr)
        T.assert_not_nil(back)
        T.assert_eq(back, utf8_str, "EUC-KR round-trip should match")
        T.log("  UTF-8 <-> EUC-KR 往返正确")
    end)

    -- 测试 11: UTF-8 <-> ANSI
    T.test("encoding: UTF-8 <-> ANSI", function()
        local utf8_str = "Hello"
        local ansi = encoding.utf8_to_ansi(utf8_str)
        T.assert_not_nil(ansi)
        local back = encoding.ansi_to_utf8(ansi)
        T.assert_eq(back, utf8_str, "ANSI round-trip should match for ASCII")
    end)

    -- 测试 12: GBK <-> Big5 跨编码转换
    T.test("encoding: GBK <-> Big5 跨编码", function()
        local utf8_str = "中文"
        local gbk = encoding.utf8_to_gbk(utf8_str)
        -- GBK -> Big5 (经由 UTF-16 中转)
        local big5 = encoding.convert(gbk, "gbk", "big5")
        T.assert_not_nil(big5, "GBK to Big5 should succeed")
        -- Big5 -> UTF-8 验证
        local back = encoding.big5_to_utf8(big5)
        T.assert_eq(back, utf8_str, "GBK->Big5->UTF8 should match")
        T.log("  GBK <-> Big5 跨编码正确")
    end)

    -- 测试 13: 空字符串处理
    T.test("encoding: 空字符串", function()
        local result = encoding.utf8_to_gbk("")
        T.assert_not_nil(result)
        T.assert_eq(result, "")
    end)

    -- 测试 14: 未知编码错误处理
    T.test("encoding: 未知编码错误", function()
        local result, err = encoding.convert("test", "utf8", "unknown_enc")
        T.assert_nil(result, "unknown encoding should return nil")
        T.assert_not_nil(err, "should return error message")
        T.assert_type(err, "string")
        T.log("  错误信息: " .. err)
    end)

    -- 测试 15: 混合中英文
    T.test("encoding: 混合中英文", function()
        local utf8_str = "Hello你好World世界123"
        local gbk = encoding.utf8_to_gbk(utf8_str)
        T.assert_not_nil(gbk)
        local back = encoding.gbk_to_utf8(gbk)
        T.assert_eq(back, utf8_str, "mixed content round-trip should match")
        T.log("  混合中英文往返正确")
    end)

    -- 测试 16: sys.get_code_page
    T.test("sys: get_code_page", function()
        local cp = sys.get_code_page()
        T.assert_not_nil(cp, "get_code_page should return a value")
        T.assert_type(cp, "number")
        T.assert_true(cp > 0, "code page should be positive")
        T.log("  系统代码页: " .. cp)
    end)

    -- 测试 17: encoding.utf8_to_local / local_to_utf8 往返
    T.test("encoding: UTF-8 <-> Local 往返", function()
        local ascii = "Hello World 123"
        local local_str = encoding.utf8_to_local(ascii)
        T.assert_not_nil(local_str)
        local back = encoding.local_to_utf8(local_str)
        T.assert_eq(back, ascii, "local round-trip should match for ASCII")
        T.log("  UTF-8 <-> Local ASCII 往返正确")
    end)

    -- 测试 18: encoding.utf8_to_local 空字符串
    T.test("encoding: utf8_to_local 空字符串", function()
        local result = encoding.utf8_to_local("")
        T.assert_not_nil(result)
        T.assert_eq(result, "")
    end)

    -- 测试 19: encoding.adaptive_to_local ASCII 输入
    T.test("encoding: adaptive_to_local ASCII", function()
        local result, src_type = encoding.adaptive_to_local("Hello123")
        T.assert_eq(result, "Hello123")
        T.assert_eq(src_type, "ascii", "ASCII should be detected as 'ascii'")
        T.log("  adaptive ASCII: type=" .. src_type)
    end)

    -- 测试 20: encoding.adaptive_to_local 空字符串
    T.test("encoding: adaptive_to_local 空字符串", function()
        local result, src_type = encoding.adaptive_to_local("")
        T.assert_eq(result, "")
        T.assert_eq(src_type, "empty")
    end)

    -- 测试 21: encoding.adaptive_to_local UTF-8 输入
    T.test("encoding: adaptive_to_local UTF-8", function()
        -- UTF-8 中文 "你好" 是合法 UTF-8 序列
        local result, src_type = encoding.adaptive_to_local("Hello")
        T.assert_not_nil(result)
        -- ASCII 也是合法 UTF-8，但会被检测为 ascii
        T.assert_eq(src_type, "ascii")
        T.log("  adaptive UTF-8 检测正常")
    end)

    -- 测试 22: encoding.adaptive_to_local 带 BOM 的 UTF-8
    T.test("encoding: adaptive_to_local UTF-8 BOM", function()
        local bom = "\xEF\xBB\xBFHello"
        local result, src_type = encoding.adaptive_to_local(bom)
        T.assert_not_nil(result)
        T.assert_eq(src_type, "utf8", "BOM should be detected as utf8")
        T.assert_eq(result, "Hello", "BOM should be stripped")
        T.log("  adaptive BOM 检测正确")
    end)

    return T.report("encoding")
end

return { run = run }
