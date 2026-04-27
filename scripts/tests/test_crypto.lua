--[[
    crypto 模块测试 - 加密算法和哈希函数验证
    覆盖: MD5/SHA1/SHA256/XXHash/Base64/Hex/RC4/AES/ChaCha20/XXTEA/随机数
]]
local T = require("tests.test_framework")

-- 测试数据
local DATA = "Hello, World!"
local KEY16 = "1234567890123456"
local KEY32 = "12345678901234567890123456789012"
local NONCE = "123456789012"

local function run()
    T.reset()
    T.log("\n=== crypto 模块测试 ===")

    -- 哈希函数
    T.test("crypto.md5: 计算MD5哈希", function()
        local r = crypto.md5(DATA)
        T.assert_type(r, "string"); T.assert_eq(#r, 32)
        T.assert_eq(r, "65a8e27d8879283831b664bd8b7f0ad4")
        T.log("  " .. r)
    end)

    T.test("crypto.sha1: 计算SHA1哈希", function()
        local r = crypto.sha1(DATA)
        T.assert_type(r, "string"); T.assert_eq(#r, 40)
        T.assert_eq(r, "0a0a9f2a6772942557ab5355d76af442f8f65e01")
    end)

    T.test("crypto.sha256: 计算SHA256哈希", function()
        local r = crypto.sha256(DATA)
        T.assert_type(r, "string"); T.assert_eq(#r, 64)
        T.assert_eq(r, "dffd6021bb2bd5b0af676290809ec3a53191dd81c7f70a4b28688a362182986f")
    end)

    T.test("crypto.xxhash64: 计算XXHash64", function()
        local r = crypto.xxhash64(DATA)
        T.assert_type(r, "string"); T.assert_eq(#r, 16)
    end)

    -- 编码/解码
    T.test("crypto.base64: 编码解码", function()
        local enc = crypto.base64_encode(DATA)
        T.assert_eq(enc, "SGVsbG8sIFdvcmxkIQ==")
        T.assert_eq(crypto.base64_decode(enc), DATA)
    end)

    T.test("crypto.hex: 编码解码", function()
        local enc = crypto.hex_encode(DATA)
        T.assert_eq(enc, "48656c6c6f2c20576f726c6421")
        T.assert_eq(crypto.hex_decode(enc), DATA)
    end)

    -- 对称加密
    T.test("crypto.rc4: 加密解密", function()
        local enc = crypto.rc4(DATA, KEY16)
        T.assert_not_nil(enc)
        T.assert_eq(crypto.rc4(enc, KEY16), DATA)
    end)

    T.test("crypto.aes: 加密解密", function()
        local enc = crypto.aes_encrypt(DATA, KEY16)
        T.assert_not_nil(enc)
        T.assert_eq(crypto.aes_decrypt(enc, KEY16), DATA)
    end)

    T.test("crypto.chacha20: 加密解密", function()
        local enc = crypto.chacha20(DATA, KEY32, NONCE)
        T.assert_not_nil(enc)
        T.assert_eq(crypto.chacha20(enc, KEY32, NONCE), DATA)
    end)

    T.test("crypto.xxtea: 加密解密", function()
        local enc = crypto.xxtea_encrypt(DATA, KEY16)
        T.assert_not_nil(enc)
        T.assert_eq(crypto.xxtea_decrypt(enc, KEY16), DATA)
    end)

    -- 随机数
    T.test("crypto.random: 随机字符串生成", function()
        local r1 = crypto.random(16)
        T.assert_eq(#r1, 16)
        local r2 = crypto.random(32, "0123456789abcdef")
        T.assert_eq(#r2, 32)
        T.assert_true(r1 ~= crypto.random(16), "应生成不同值")
    end)

    return T.report("crypto")
end

return { run = run }
