# AionData.lua API 说明

AION 客户端（Aion.bin）数据读取模块，基于 AetherEngine 3.2.0。

> 本文档只描述对外可调用的接口和返回字段，不涉及具体地址 / 偏移 / 卡密等实现细节。

---

> ## 📌 本文档包含两个独立模块
>
> | 模块 | 用途 | 是否依赖游戏进程 |
> |---|---|---|
> | **`AionData`** | 游戏内数据读取 / 操作（角色 / 背包 / 技能 / 任务 / UI / 远程调用 …） | ✅ 需要游戏已启动 |
> | **`AionLogin`** | **协议自动登录**（账号密码 → token → 启动游戏） | ❌ 不依赖, 启动器阶段即可调用 |
>
> **想用协议登录功能直接跳到文末**: [▶ 协议登录 (AionLogin 模块)](#协议登录-aionlogin-模块)

---

## 目录

1. [快速开始](#快速开始)
2. [初始化](#初始化)
3. [服务器](#服务器)
4. [角色](#角色)
5. [周围实体](#周围实体)
6. [背包](#背包)
7. [商店 (NPC)](#商店-npc)
8. [二级密码](#二级密码)
9. [技能（已学习）](#技能已学习)
10. [Buff](#buff)
11. [自动技能](#自动技能)
12. [任务](#任务)
13. [地图](#地图)
14. [UI 窗口](#ui-窗口)
15. [UI 子控件树](#ui-子控件树)
16. [频道](#频道)
17. [技能类型查询](#技能类型查询)
18. [常用枚举](#常用枚举)
19. [远程调用](#远程调用)
20. [选中目标](#选中目标)
21. [移动](#移动)
22. [NPC 对话](#npc-对话)
23. [物品](#物品)
24. [拾取](#拾取)
25. [OTP / TOTP](#otp--totp)
26. [注意事项](#注意事项)
27. [**协议登录 (AionLogin 模块)**](#协议登录-aionlogin-模块) ← 独立模块, 不依赖游戏

---

## 快速开始

把 `AionData.lua` 放到 AE 脚本目录, 同目录新建你的脚本：

```lua
local data = require("AionData")

-- 枚举进程, 传 PID 初始化
local pid = 0
for _, p in ipairs(proc.list()) do
    if p.name == "Aion.bin" then pid = p.pid; break end
end
if pid == 0 then log.error("找不到进程"); return end

local ok, err = data.InitGameinfo(pid)
if not ok then log.error(err); return end

local char  = data.GetCharacter()
local list  = data.GetAroundList()
local items = data.GetInventoryList()
local sks   = data.GetSkillList()
local act   = data.GetAutoActiveSkills()
local buf   = data.GetAutoBuffSkills()
local uis   = data.GetUIList()
local qs    = data.GetQuestList()
local map   = data.GetCurrentMap()
```

运行前提：
1. AION 客户端已启动
2. AE 引擎以管理员身份运行
3. 模块文件 `AionData.lua` 内 `LICENSE_KEY` 已填入有效驱动卡密

---

## 初始化

### `M.InitGameinfo(pid) → ok, err`

加载驱动、定位进程、获取模块基址、初始化主线程劫持调用。**所有其它接口必须先调用一次此函数。**

| 参数 | 类型 | 说明 |
|---|---|---|
| `pid` | int | 目标游戏进程 PID, 由调用方枚举后传入 |

| 返回值 | 类型 | 说明 |
|---|---|---|
| `ok` | bool | true = 成功 |
| `err` | string | 失败原因 |

可重复调用，已初始化时直接返回 `true`。

### `M.GetState() → table`

| 字段 | 类型 | 说明 |
|---|---|---|
| `inited` | bool | 是否已初始化 |
| `pid` | u32 | 进程 ID |
| `base` | u64 | 主模块基址 |
| `modSize` | u32 | 主模块大小 |
| `hwnd` | u64 | 游戏窗口句柄 |
| `tid` | u32 | 主线程 ID |
| `sbuf` | u64 | 远程调用共享缓冲区 |
| `dep` | u64 | 远程调用备用上下文 |

### `M.GetSceneIndex() → idx, name`

返回当前界面序号及中文名。

| 序号 | 名称 |
|---|---|
| `0x8` | 用户协议界面 |
| `0x9` | 同意协议界面 |
| `0xA` | 选择服务器界面 |
| `0xB` | 排队界面 |
| `0xC` | 选择角色界面 |
| `0xF` | 游戏中 |
| `0x18` | 退出游戏界面 |

未初始化时返回 `0, ""`；未知值返回 `idx, "未知(0xN)"`。

### `M.GetCharacterList() → { char, ... }`

读取**角色选择界面**的角色列表（场景 `0xC` 时有效；进入游戏后通常会被清空）。

| 字段 | 类型 | 说明 |
|---|---|---|
| `addr` | u64 | 角色对象地址 |
| `id` | u32 | 角色 ID |
| `name` | string | 角色名（UTF-8，inline UNICODE 至多 30 字符） |
| `job` | u32 | 职业 ID |
| `level` | u8 | 等级 |
| `map_id` | u32 | 所在地图 ID |
| `race` | u32 | 种族（1=天族, 0=魔族） |
| `race_name` | string | 种族中文名 |

### `M.SelectCharacter(char_or_index) → bool`

在角色选择界面选中某个角色并进入游戏。

| 参数 | 类型 | 说明 |
|---|---|---|
| `char_or_index` | table 或 int | 传 `GetCharacterList()[i]` 的整条 entry，或 1-based 下标 |

示例：

```lua
-- 按下标选第一个角色
data.SelectCharacter(1)

-- 或者按名字查找后选中
for i, c in ipairs(data.GetCharacterList()) do
    if c.name == "黑魔狼" then
        data.SelectCharacter(c)
        break
    end
end
```

### `M.CreateCharacter(name, gender, race, job_id) → bool`

在**角色选择界面**（场景 `0xC`）创建一个新角色。提交成功返回 `true`，最终能否创建（重名/槽位满/职业不符合种族等）由服务器判定。

| 参数 | 类型 | 说明 |
|---|---|---|
| `name` | string | 角色名（UTF-8）。最多 10 个字符（中英文均按 1 个计），超出截断 |
| `gender` | int | 性别。`0`=男，`1`=女 |
| `race` | int | 种族。`0`=天族，`1`=魔族 |
| `job_id` | int | 职业 ID（见下表） |

职业 ID：

| 职业 | id |    | 职业 | id |    | 职业 | id |
|---|---:|---|---|---:|---|---|---:|
| 剑星 | `0x1` |  | 守护星 | `0x2` |  | 杀星 | `0x4` |
| 弓星 | `0x5` |  | 魔道星 | `0x7` |  | 精灵星 | `0x8` |
| 治愈星 | `0xA` |  | 护法星 | `0xB` |  | 执行者 | `0xD` |
| 拳星 | `0x10` |  | 鲁米内斯 | `0x13` |  | 火神 | `0x16` |
| 魔剑星 | `0x19` |  |  |  |  |  |  |

示例：

```lua
-- 创建：天族 弓星 男 名叫 "测试abc"
data.CreateCharacter("测试abc", 0, 0, 0x5)

-- 创建：魔族 杀星 女 名叫 "fghgeww"
data.CreateCharacter("fghgeww", 1, 1, 0x4)

-- 创建后建议稍等再调 GetCharacterList() 复核
```

> 仅在选角界面调用才有意义；外观使用默认模板，不可自定义。

---

## 服务器

> 仅在**选择服务器界面**（场景 `0xA`）有效。这三个接口都内部自动定位服务器列表 UI，调用方无需手动找 UI 对象。

### `M.GetServerList(server_ui?) → { srv, ... }`

读取服务器列表。返回为空表 `{}` 表示当前不在选服务器界面 / UI 尚未就绪。

| 参数 | 类型 | 说明 |
|---|---|---|
| `server_ui` | u64? | 可选。手动传入 `server_select_dialog` 的 UI 对象；不传则内部自动 `FindUIObj` |

返回每项字段：

| 字段 | 类型 | 说明 |
|---|---|---|
| `addr` | u64 | 服务器列表项节点地址 |
| `key` | u32 | UI 节点 key（按列表显示顺序 0/1/2/…，作为 `SelectServer` 的入参） |
| `server_id` | u32 | 服务器 ID |

### `M.GetCurSelectedServerId() → u32`

返回当前光标已选中的服务器 ID（不是 `key`）。未在选服务器界面 / 未选中时返回 `0`。

### `M.SelectServer(server_index) → bool`

提交进入指定服务器。

| 参数 | 类型 | 说明 |
|---|---|---|
| `server_index` | int | 服务器序号（对应 `GetServerList()` 项的 `key`） |

调用成功返回 `true`；UI 未就绪 / 链路未初始化返回 `false`。最终能否进服由服务器判定。

示例：

```lua
local list = data.GetServerList()
for _, s in ipairs(list) do
    if s.server_id == 0x10100065 then
        data.SelectServer(s.key)
        break
    end
end
```

---

## 角色

### `M.GetCharacter() → table | nil`

获取当前角色信息。**未进场景时返回 `nil`。**

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | u32 | 角色 ID |
| `IEntity` | u64 | IEntity 对象地址 |
| `obj` | u64 | 角色对象地址 |
| `flags` | u32 | IEntity 标志位 |
| `level` | u8 | 等级 |
| `job` | u32 | 职业 ID |
| `race` | u32 | 种族 (0=天族, 1=魔族) |
| `race_name` | string | 种族中文名 |
| `gender` | u32 | 性别 (0=男性, 1=女性) |
| `gender_name` | string | 性别中文名 |
| `face` | float | 朝向（弧度） |
| `move_state` | u32 | 移动状态 (0=未移动, 2=移动中) |
| `hp` / `mhp` | u32 | 当前/最大 HP |
| `mp` / `mmp` | u32 | 当前/最大 MP |
| `exp` / `max_exp` | u32 | 当前/最大经验 |
| `hit` | u32 | 命中 |
| `guardian` | u32 | 守护者潜力 |
| `x` / `y` / `z` | float | 坐标 |
| `name` | string | 角色名（UTF-8） |
| `dead` | bool | 是否死亡（`true` = 已死亡，`false` = 存活） |

---

## 周围实体

### `M.GetAroundList() → { entity, ... }`

返回当前视野内所有实体（玩家、怪物、NPC、地标等）。被屏蔽类型不返回（摆物、投射物、椅子）。

| 字段 | 类型 | 说明 |
|---|---|---|
| `tag` | string | 分类标签（见 [常用枚举](#常用枚举)） |
| `type` | u8 | 实体 Type（1=玩家、2=怪物/NPC 等） |
| `id` | u32 | 实体 ID |
| `IEntity` | u64 | IEntity 对象地址 |
| `obj` | u64 | 实体对象地址 |
| `level` | u8 | 等级 |
| `job` | u32 | 职业 ID（玩家） |
| `face` | float | 朝向（弧度） |
| `move_state` | u32 | 移动状态 (0=未移动, 2=移动中) |
| `hp` / `mhp` | u32 | 当前/最大 HP |
| `mp` / `mmp` | u32 | 当前/最大 MP |
| `exp` / `max_exp` | u32 | 当前/最大经验 |
| `hit` | u32 | 命中 |
| `guardian` | u32 | 守护者潜力 |
| `x` / `y` / `z` | float | 坐标 |
| `name` | string | 名称（UTF-8） |
| `is_self` | bool | 是否为本人 |
| `ct` | u8 | CreatureType |
| `type_name` | string | 类型名（地标/客 NPC 等） |
| `lootable` | u32 | 是否可拾取（0=不可拾取, 1=可拾取，主要用于怪物尸体） |
| `flags` | u32 | IEntity 标志位 |
| `rating` | u32 | 怪物阶级数值（即游戏目标卡上显示的"X级"，常见 1~6；非怪物或查表失败为 0） |
| `is_mutant` | bool | 是否为变异体（仅对怪物 / NPC 有意义） |
| `interact_id` | u32 | 交互 id（用于 `InteractNpc` / 商店购买 / 据点传送 等业务；不可交互对象为 0） |
| `dead` | bool | 是否死亡（`true` = 已死亡） |

> `rating` / `is_mutant` 仅对怪物/NPC（`type == 2`）有意义。模块内部首次读取时会自动构建一次全表缓存，之后每帧查询为常数时间。

---

## 背包

### `M.GetInventoryList() → { item, ... }`

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | u32 | 物品实例 ID |
| `addr` | u64 | 物品对象地址 |
| `count` | u32 | 堆叠数量 |
| `cat` | u32 | 分类 ID |
| `cat_name` | string | 分类名 |
| `slot` | u32 | 装备槽位 ID (0=未穿戴) |
| `slot_name` | string | 槽位名 |
| `equip_pos` | u32 | 可穿戴位置 ID (仅装备类有意义; 表示该装备**可以穿在哪个槽**, 与是否已穿戴无关) |
| `equip_pos_name` | string | 可穿戴位置名 |
| `text` | string | 物品名（UTF-8） |
| `item_info_id` | u32 | 物品信息 ID, 用于内部查表匹配 |
| `quality` | int | 品质 (1=白色, 2=绿色, 3=蓝色, 4=黄色; 0=未匹配) |
| `item_level` | int | 物品等级 (0=未匹配) |

### `M.GetKinah() → number`

返回当前角色基纳(金币)数量。未初始化或链路无效时返回 `0`。

### `M.EquipItem(item_id, equip_pos, unequip) → bool`

穿戴或脱下一件装备。

| 参数 | 类型 | 说明 |
|---|---|---|
| `item_id` | number | 物品 id (背包项的 `item.id`) |
| `equip_pos` | number | 可穿戴位置 (背包项的 `item.equip_pos`); 脱下时本参数被忽略 |
| `unequip` | bool/nil | `false` 或 `nil` = 穿戴; `true` = 脱下 |

成功提交返回 `true`, 链路未初始化或物品 id 非法返回 `false`。

> 实际穿戴/脱下结果由服务器最终判定; 建议调用后稍等再用 `M.GetInventoryList()` 查 `slot_name` 校验。

### `M.UseItem(item_id) → bool`

使用一件物品（消耗品 / 卷轴 / 药水等）。

| 参数 | 类型 | 说明 |
|---|---|---|
| `item_id` | number | 物品 id (背包项的 `item.id`) |

提交成功返回 `true`, 链路未初始化或 id 非法返回 `false`。

### `M.InteractNpc(interact_id) → bool`

与 NPC / 可交互对象发起交互（打开商店 / 任务对话 / 采集 / 据点传送台 / 制作台 等）。

| 参数 | 类型 | 说明 |
|---|---|---|
| `interact_id` | number | 交互 id，直接取自 `GetAroundList()` 项的 `interact_id` 字段 |

成功提交返回 `true`，链路未初始化 / id 为 0 返回 `false`。

> 实际是否弹商店 / 对话框由服务器判定。交互成功后通常需要等 1 帧再用 `FindUIObj("dlg_dialog")` 之类去拿对应 UI 对象。

---

## 商店 (NPC)

> 仅在 `trade_dialog` 顶层 UI 已打开时（即玩家先通过 `InteractNpc` 打开了商店后）才能拿到商店物品列表；商店未打开时 `GetShopItems()` 返回空表 `{}`。

### `M.GetShopItems() → { item, ... }`

返回当前已打开商店的物品列表。

每项字段：

| 字段 | 类型 | 说明 |
|---|---|---|
| `addr` | u64 | 物品对象指针（同一商店刷新后地址可能变化） |
| `id` | u32 | 物品 id |
| `sub_id` | u32 | 副 id（同一物品不同变体 / 等级 / 品质用于区分） |
| `price_base` | u32 | 价格基数（基础参考价；实际购买价需经 `GetShopItemPrice` 换算） |
| `name` | string | 物品名称（UTF-16 → Lua string） |
| `interact_id` | u32 | 本次商店所属 NPC 的 `interact_id`（同一次商店中所有项相同，可直接喂给 `BuyShopItem`） |

交互npc后 会打开对话   有两个选项 购买和出售 在调用SendNpcDialog的时候  下阶段id 购买-2  出售-3   才可以打开相应的商店界面


### `M.GetShopItemPrice(price_base) → int`

把 `price_base` 换算成"实际成交价"（含会员折扣 / 任务折扣 / 等级修正等）。

| 参数 | 类型 | 说明 |
|---|---|---|
| `price_base` | number | 来自 `GetShopItems()` 项的 `price_base` |

返回最终售价（基纳数额）。链路未初始化或入参为 0 时返回 `0`。

### `M.BuyShopItem(interact_id, item_id, sub_id, count) → bool`

向当前已打开商店购买物品。

| 参数 | 类型 | 说明 |
|---|---|---|
| `interact_id` | number | 商店 NPC 的 `interact_id`（来自 `GetAroundList()` 项 / 调用时打开商店的那一只 NPC） |
| `item_id` | number | `GetShopItems()` 项的 `id` |
| `sub_id` | number | `GetShopItems()` 项的 `sub_id`（区分同物品不同变体/品质，必传） |
| `count` | number | 购买数量 |

成功提交发包返回 `true`，链路未初始化 / 参数非法返回 `false`。实际是否扣钱/到包由服务器最终判定，建议调用后用 `M.GetInventoryList()` 校验。

### `M.SHOP_ITEMS` 静态商品表

抓取自杂货商人 NPC 的常用商品列表（两个阵营各一份）。结构：

```lua
M.SHOP_ITEMS = {
    elyos    = { { id, sub_id, price_base, name }, ... },  -- 天族
    asmodian = { { id, sub_id, price_base, name }, ... },  -- 魔族
}
```

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | number | 物品 id |
| `sub_id` | number | 副 id（两族同名物品的 `sub_id` 不同，必须配对使用） |
| `price_base` | number | 价格基数（实际成交价用 `GetShopItemPrice` 换算） |
| `name` | string | 物品名称 |

用法示例：

```lua
-- 给当前 NPC 一键买齐天族杂货商人列表里所有"治疗秘药"x10
for _, it in ipairs(data.SHOP_ITEMS.elyos) do
    if it.name == "治疗秘药" then
        data.BuyShopItem(npc_interact_id, it.id, it.sub_id, 10)
    end
end
```

---

## 二级密码

### `M.GetSecondPwdDialog() → { addr, title }`

获取二级密码对话框信息。对话框未弹出（顶层 UI 不可见）时返回 `{ addr = 0, title = "" }`，调用方可直接读字段不必判 `nil`。

| 字段 | 类型 | 说明 |
|---|---|---|
| `addr` | u64 | 对话框对象指针；未弹出为 `0`。可直接传给 `InputSecondPwd` 的 `dialog_addr` |
| `title` | string | 对话框描述标题文本（UTF-16 → Lua string）；未弹出为 `""`。可用于判定当前业务（如"输入二级密码"/"注册二级密码"/"修改二级密码"） |

### `M.InputSecondPwd(dialog_addr, pwd_str, register) → bool`

向二级密码对话框依次填入 6~8 位密码。

| 参数 | 类型 | 说明 |
|---|---|---|
| `dialog_addr` | number | 二级密码对话框对象指针（this），可通过 `GetSecondPwdDialog().addr` 取得 |
| `pwd_str` | string | 6~8 位密码字符串（游戏允许长度），例如 `"123456"` / `"1234567"` / `"12345678"` |
| `register` | bool / nil | `true` = 注册流程（填两排 / 两份），`false` 或 `nil` = 填入（登录验证）流程（只填一排） |

成功提交返回 `true`，参数非法（长度不在 6~8）或链路异常返回 `false`。

> 内部按 `pwd_str` 实际长度循环调用底层输入函数，每个字符一格，由游戏端完成乱序键盘映射 + 加密 + 发包。无需关心密文如何生成。

---

## 技能（已学习）

### `M.GetSkillList() → { skill, ... }`

返回角色已学的所有技能（等级 > 0）。首次调用会构建 `id → type` 映射（约 50ms），之后每次查询为常数时间。

需要刷新（学新技能/换职业后）调用 `M.RebuildSkillTypeMap()`。

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | u32 | 技能 ID |
| `addr` | u64 | 技能对象地址 |
| `level` | u8 | 技能等级 |
| `name` | string | 技能名（UTF-8） |
| `type` | int | 技能类型值（2=主动, 3=提取, 8=被动, 0=未知, -1=查表失败） |
| `type_name` | string | 上面 type 对应的中文标签 |

---

## Buff

### `M.GetBuffList() → { buff, ... }`

返回当前角色身上所有 Buff。

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | u32 | 节点 id |
| `addr` | u64 | Buff 对象地址 |
| `bid` | u32 | Buff ID |
| `level` | u32 | Buff 等级 |
| `name` | string | Buff 名（UTF-8） |

---

## 自动技能

游戏内"自动战斗"使用的技能列表，主动 / 增益分开返回。

### `M.GetAutoActiveSkills() → { skillId, ... }`

返回自动战斗里**主动**技能 ID 列表。

### `M.GetAutoBuffSkills() → { skillId, ... }`

返回自动战斗里**增益**技能 ID 列表。

### `M.IsSkillAuto(skill_id) → bool`

查询单个技能是否可加入自动战斗。

| 返回值 | 含义 |
|---|---|
| `true` | 可自动 |
| `false` | 不可自动 |

| 参数 | 类型 | 说明 |
|---|---|---|
| `skill_id` | int | 技能 id |

```lua
print(string.format("技能是否可自动:%s",tostring(data.IsSkillAuto(0x68D))))  -- true/false
```

---

## 任务

### `M.GetQuestList() → { quest, ... }`

返回**已接任务**列表. 以角色当前已接状态为准 (权威来源), 再用任务面板索引补充名称 / 等级要求等显示信息.
未在面板里索到的任务, 名称 / 等级文本为空字符串.

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | u32 | 任务 ID |
| `status_code` | u8 | 状态码 (3=正在进行中, 4=已完成, 6=等级未到) |
| `status_name` | string | 状态中文名 |
| `req_count` | u8 | 语义随 tab 不同: **使命** 中为步骤序号; **任务** 中为击杀数量 |
| `tab` | int | 0/1/2 (未在面板中时为 `nil`) |
| `tab_name` | string | `使命` / `任务` / `制作委托` / `?` |
| `seq` | u32 | 任务在面板的序号 |
| `quest` | u64 | 任务结构地址 |
| `elems` | u64 | 任务元素地址 |
| `lv_text` | string | 等级要求文本（如 `13级`） |
| `lv_num` | int | 等级要求数字 |
| `name` | string | 任务名 |
| `item_id` | u32 | 需要交付/收集的物品 id (无要求时为 0) |
| `item_count` | u32 | 需要的物品数量 |
| `exp_reward` | u32 | 完成后经验奖励 |
| `kinah` | u32 | 完成后基纳奖励 |

### `M.OpenQuestSubmit(quest_id) → bool`

打开**已完成**任务的提交面板（适用"任务"或"制作委托"分类，**不适用"使命"任务**）。任务状态必须为 `已完成`（`status_code == 4`），否则面板不会弹出或弹出后无法提交。

| 参数 | 类型 | 说明 |
|---|---|---|
| `quest_id` | int | 已完成任务的 id（取自 `GetQuestList()` 项的 `id`，且 `status_code == 4`） |

提交成功返回 `true`；未初始化、任务 id 非法或对话框不可用返回 `false`。

> 调用后会弹出该任务的提交对话框，最终交付动作仍需在 UI 上确认（或后续配合点击按钮接口完成）。

示例：

```lua
-- 自动打开第一个 "已完成" 状态的任务/制作委托 提交面板
for _, q in ipairs(data.GetQuestList()) do
    if (q.tab_name == "任务" or q.tab_name == "制作委托")
        and q.status_code == 4 then
        data.OpenQuestSubmit(q.id)
        break
    end
end
```

### `M.GetAchievementTaskList(type_id?) → { task, ... }`

读取成就任务列表。`type_id` 可选；不传时返回全部支持类型。

可用类型常量：

| 常量 | 值 | 说明 |
|---|---:|---|
| `M.ACHIEVEMENT_TYPE_HINT` | `0x7` | 提示 |
| `M.ACHIEVEMENT_TYPE_FEAT` | `0x8` | 功绩 |

返回的每个 `task` 字段：

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | u32 | 成就任务 id |
| `task_id` | u32 | 同 `id`，便于业务代码按任务语义读取 |
| `type` | u32 | 成就类型 |
| `type_name` | string | 成就类型中文名 |
| `name` | string | 成就任务中文名 |
| `name_en` | string | 成就任务英文名 |
| `max_count` | u32 | 要求最大数量 |

示例：

```lua
-- 遍历全部提示 / 功绩成就，并按任务 id 读取实时对象状态
for _, t in ipairs(data.GetAchievementTaskList()) do
    local obj = data.GetAchievementTaskObject(t.id)
    if obj then
        log.info(string.format("[%s] id:%X lv:%d reward:%X state:%s(%d) count:%d/%d name:%s name_en:%s", t.type_name,
            t.id, obj.level_limit or 0, obj.reward_id or 0, obj.status_name or "", obj.status or 0, obj.count or 0,
            t.max_count, t.name, t.name_en))
    end
end

-- 只取功绩
local feats = data.GetAchievementTaskList(data.ACHIEVEMENT_TYPE_FEAT)
```

### `M.GetAchievementTaskObject(task_id) → object | nil`

按成就任务 id 读取该任务的实时对象状态。

| 参数 | 类型 | 说明 |
|---|---|---|
| `task_id` | u32 | 成就任务 id，取自 `GetAchievementTaskList()` 项的 `id` |

返回 `nil` 的常见情况：未初始化、任务 id 无效、对象不可用，或任务已达到等级要求但没有可读奖励状态。

返回的 `object` 字段：

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | u32 | 成就任务 id |
| `task_id` | u32 | 同 `id` |
| `level_limit` | u32 | 等级限制 |
| `character_level` | u32 | 当前角色等级 |
| `reward_id` | u32 | 领取 id / 奖励 id |
| `status` | u8 | 状态码：`0`=未开放，`1`=进行中，`2`=已完成待领取，`3`=已完成已领取 |
| `status_name` | string | 状态中文名 |
| `count` | u32 | 当前数量 |

---

## 地图

### `M.GetCurrentMap() → table | nil`

获取当前所在地图信息。

| 字段 | 类型 | 说明 |
|---|---|---|
| `index` | int | 地图下标 |
| `addr` | u64 | 地图结构地址 |
| `name_en` | string | 英文名（如 `LF1_SZ_Whistle_Woods`） |
| `name_cn` | string | 中文原始名 |
| `region` | string | 纯中文地区名（如 `达米努森林`） |
| `level` | int | 推荐等级数字 |

### `M.GetMap(index) → table | nil`

按下标取任意地图（字段同上）。

### `M.GetMapNodeList(big_map_id?) → { node, ... }`

读取**地图据点表**，返回所有据点条目。

| 参数 | 类型 | 说明 |
|---|---|---|
| `big_map_id` | int? | 可选过滤；传入则只返回 `node.map_id == big_map_id` 的条目，不传返回全部 |

返回的每个 `node` 字段：

| 字段 | 类型 | 说明 |
|---|---|---|
| `map_id` | u32 | 所属大地图 id |
| `node_id` | u32 | 据点 id |
| `name` | string | 据点中文名 |
| `name_en` | string | 据点英文名 |
| `x` | float | 据点世界坐标 x |
| `y` | float | 据点世界坐标 y |
| `z` | float | 据点世界坐标 z |
| `price` | int | 据点传送价格（基纳），可直接传给 `NodeTeleport` 第二参数 |

示例：

```lua
-- 列出大地图 210010000 的所有据点
local list = data.GetMapNodeList(210010000)
for _, v in ipairs(list) do
    print(v.map_id, v.node_id, v.name, v.name_en, v.x, v.y, v.z, v.price)
end
```

### `M.GetBigMapId() → int`

返回当前所在大地图 id。未初始化或读取失败返回 `0`。

### `M.IsCanTeleport() → bool`

据点传送全局冷却是否已结束：return `true` 表示可传送，`false` 表示在冷却。

### `M.NodeTeleport(node_id, price?) → bool`

发起据点传送。`node_id` 取自 `GetMapNodeList` 项的 `node_id` 字段；`price` 可选，建议传入同一项的 `price` 字段，不传时按 `0` 提交。返回 `true` 表示调用已发出（实际能否到达受冷却 / 距离 / 限制等服务器逻辑约束）。

| 参数 | 类型 | 说明 |
|---|---|---|
| `node_id` | int | 目标据点 id |
| `price` | int? | 可选，据点传送价格；建议使用 `GetMapNodeList()` 项的 `price` 字段 |

示例：

```lua
if data.IsCanTeleport() then
    local list = data.GetMapNodeList(data.GetBigMapId())
    if list[1] then
        data.NodeTeleport(list[1].node_id, list[1].price)
    end
else
    print("传送冷却中")
end
```

### `M.BigMapTeleport(slot) → bool`

发起**大地图传送**（不同于据点传送，按大地图传送列表的序号跳转，会消耗对应基纳）。`slot` 取自 `M.BIG_MAP_TELEPORTS` 表的 `slot` 字段，按角色阵营选 `elyos` / `asmodian`。返回 `true` 表示调用已发出（实际能否传送受等级 / 基纳 / 服务器条件约束）。

| 参数 | 类型 | 说明 |
|---|---|---|
| `slot` | int | 大地图传送列表序号（**非地图 id**，是传送界面里的列表序号） |

### `M.BIG_MAP_TELEPORTS` 大地图传送表

静态表，按阵营拆分。每项字段：

| 字段 | 类型 | 说明 |
|---|---|---|
| `slot` | int | 大地图传送列表序号（传给 `BigMapTeleport`） |
| `price` | int | 消耗基纳 |
| `min_lv` | int | 限制等级 |
| `name` | string | 大地图名 |

**天族 `elyos`**：

| slot | 名称 | 价格 | 等级 |
|---|---|---|---|
| `0x01` | 极乐世界 | 700 | 1 |
| `0x02` | 普埃塔 | 350 | 5 |
| `0x03` | 埃尔特内 | 1200 | 20 |
| `0x04` | 因特尔蒂卡 | 2000 | 42 |
| `0x11` | 境界之地贝拉 | 1200 | 21 |
| `0x13` | 硫磺树列岛 | 7200 | 35 |
| `0x15` | 扭曲的普埃塔 | 24000 | 45 |

**魔族 `asmodian`**：

| slot | 名称 | 价格 | 等级 |
|---|---|---|---|
| `0x05` | 伏魔殿 | 700 | 1 |
| `0x06` | 伊斯夏尔肯 | 350 | 5 |
| `0x07` | 莫尔海姆 | 1200 | 20 |
| `0x08` | 贝鲁斯兰 | 2000 | 42 |
| `0x12` | 境界之地贝拉 | 1200 | 21 |
| `0x14` | 硫磺树列岛 | 7200 | 35 |
| `0x16` | 扭曲的普埃塔 | 24000 | 45 |

示例：

```lua
-- 推荐: 自动按当前角色阵营选表, 不用关心 key 名
for _, t in ipairs(data.GetBigMapTeleports()) do
    print(string.format("%X %s 价格:%d 等级:%d",
        t.slot, t.name, t.price, t.min_lv))
end

-- 直传序号传送
data.BigMapTeleport(0x1)

-- 也可手动按阵营 key 取
for _, t in ipairs(data.BIG_MAP_TELEPORTS.elyos) do ... end
```

### `M.GetBigMapTeleports(race?) → { teleport, ... }`

按阵营取对应的大地图传送表。**省略 `race` 时自动取当前角色阵营**（`GetCharacter().race`），上层不用判断 `elyos` / `asmodian` key。

| 参数 | 类型 | 说明 |
|---|---|---|
| `race` | int / nil | 阵营，可传 `M.RACE_ELYOS` (0) / `M.RACE_ASMODIAN` (1)；省略时自动取当前角色 |

返回的列表项字段同 `M.BIG_MAP_TELEPORTS.elyos[i]`（`slot` / `price` / `min_lv` / `name`）。

> 同款 helper 也提供给商店静态表：`M.GetShopItemsStatic(race?)` —— 自动按阵营返回 `M.SHOP_ITEMS.elyos` 或 `M.SHOP_ITEMS.asmodian`。

---

## UI 窗口

### `M.FindUIObj(name) → ui | nil`

按 ASCII 名称查找 UI 控件，返回一个描述表；找不到返回 `nil`。

| 参数 | 类型 | 说明 |
|---|---|---|
| `name` | string | 要查找的控件 ASCII 名（必须全匹配，区分大小写） |

返回的 `ui` 表字段：

| 字段 | 类型 | 说明 |
|---|---|---|
| `addr` | u64 | 控件对象地址（之前版本直接返回的值；现需用 `ui.addr`） |
| `name` | string | 控件名（回显，与传入相同） |
| `visible` | bool | 是否可见 |
| `flag28` | u64 | 标志位原始值 |
| `x` | double | 相对游戏窗口客户区的 X 坐标 |
| `y` | double | 相对游戏窗口客户区的 Y 坐标 |

> 【破坏性变更】之前版本返回`地址 | nil`，现在返回`table | nil`。使用处需改为 `local r = data.FindUIObj(name); if r then use(r.addr) end`。

### `M.GetUIList(includeNoName?) → { ui, ... }`

遍历所有 UI 控件（按 z-order 8 层）。

| 参数 | 类型 | 说明 |
|---|---|---|
| `includeNoName` | bool | 是否包含控件名为空的项（默认 `false`）。传 `true` 可拿到未命名的控件 |

每个 `ui` 字段：

| 字段 | 类型 | 说明 |
|---|---|---|
| `layer` | int | z-order 层级 0~7 |
| `node` | u64 | 列表节点地址 |
| `obj` | u64 | 控件对象地址 |
| `name` | string | 控件名（ASCII） |
| `visible` | bool | 是否可见（true=可见, false=隐藏） |
| `x` | double | 相对游戏窗口客户区的 X 坐标 |
| `y` | double | 相对游戏窗口客户区的 Y 坐标 |

---

## UI 子控件树

### `M.GetUIChildren(parent, max_depth?) → { child, ... }`

递归遍历指定 UI 控件的子控件列表。是否递归到下一层完全遵循游戏自身行为。

| 参数 | 类型 | 说明 |
|---|---|---|
| `parent` | number 或 string | 控件地址或控件名（string 自动 FindUIObj） |
| `max_depth` | int | 递归最大深度, 默认 16 |

每个 `child` 字段：

| 字段 | 类型 | 说明 |
|---|---|---|
| `depth` | int | 当前深度（顶层为 1） |
| `parent` | u64 | 上一层父控件地址 |
| `node` | u64 | 列表节点地址 |
| `obj` | u64 | 子控件对象地址 |
| `name` | string | 子控件名（ASCII） |
| `flag28` | u64 | 游戏原始标志字段 |
| `visible` | bool | 是否可见（`flag28 & 1 == 1`） |
| `recurse` | bool | 是否实际递归进了下一层（等同 `visible`） |
| `x` | double | 相对游戏窗口客户区的 X 坐标 |
| `y` | double | 相对游戏窗口客户区的 Y 坐标 |

---

## 频道

### `M.GetChannelInfo() → table | nil`

返回当前频道信息。对话框未打开时返回 `nil`。

| 字段 | 类型 | 说明 |
|---|---|---|
| `current` | int | 当前所在频道序号（从0数） |
| `count` | int | 频道总数 |

```lua
local ch = data.GetChannelInfo()
if ch then
    print(string.format("频道: %d/%d", ch.current, ch.count))
end
```

### `M.SwitchChannel(channel_index) → bool`

切换到指定频道。

| 参数 | 类型 | 说明 |
|---|---|---|
| `channel_index` | int | 目标频道序号（0-based，如 `0`=1 频道） |

```lua
-- 切换到第 3 频道
data.SwitchChannel(2)
```

---

## 技能类型查询

### `M.GetSkillType(SkillId) → int`

查询技能的原始 typ 值。

| 返回值 | 含义 |
|---|---|
| `2` | 主动 |
| `3` | 提取 |
| `8` | 被动 |
| `0` | 未知 |
| `-1` | 查表失败 |

实现：**纯表查询**，覆盖游戏当前已加载的所有技能。

### `M.RebuildSkillTypeMap() → bool`

强制重建技能类型映射表，返回 `true` = 成功。

何时调用：游戏内学习新技能、切换职业、跨地图重新加载技能列表后；以及发现 `GetSkillType` 返回 `-1` 时。

---

## 常用枚举

### 实体分类标签 (`entity.tag`)

| 标签 | 来源 |
|---|---|
| `我` | type==1 且 is_self |
| `玩家` | type==1 |
| `主动怪` | ct==8 (AGGRESSIVE) |
| `无敌` | ct==10 (INVULNERABLE) |
| `支援` | ct==54 (SUPPORT) |
| `NPC` | ct==38 (FRIEND) |
| `和平` | ct==2 (PEACE) |
| `怪` | type==2 且 name 不空 |
| `NPC?` | type==2 且 name 空 |
| `摆物` / `地标` / `客NPC` / `指示` | type 映射 |
| `未知(N)` | 其它 |

### 背包分类 (`item.cat_name`)

`武器` / `胸甲` / `饰品/防具` / `粉末/材料` / `技能卷轴` / `材料` / `精气/精髓` / `治疗药水` / `消耗品` / `货币` / `勋章碎片` / `礼盒` / `经验药品` / `未知(N)`

### 装备槽位 (`item.slot_name`)

`未穿戴` / `主+副手` / `头` / `胸` / `手` / `脚` / `副耳` / `主耳` / `副戒` / `主戒` / `项链` / `护肩` / `护腿` / `腰带` / `技能` / `未知槽(0x...)`

### 技能类型 (`skill.type_name` / `M.GetSkillType` 返回)

| typ | type_name |
|---:|---|
| `2` | `主动` |
| `3` | `提取` |
| `8` | `被动` |
| `0` | `未知(0)` |
| `-1` | `查表失败` |

### 任务 Tab (`quest.tab_name`)

`使命` / `任务` / `制作委托`

### 种族 / 性别

| race | name |    | gender | name |
|---:|---|---|---:|---|
| 0 | 天族 |    | 0 | 男性 |
| 1 | 魔族 |    | 1 | 女性 |

---

## 远程调用

> 这一组接口在游戏主线程上下文中执行内置函数。需 `InitGameinfo` 成功。

### `M.PressKey(keycode) → bool`

向游戏发送按键事件。`keycode` 为目标键码。

### `M.PlaceQuickbar(bar_index, slot_index, kind, id) → bool`

放置物品/技能到快捷栏。

| 参数 | 说明 |
|---|---|
| `bar_index` | 快捷栏组序号（从下往上，从0开始） |
| `slot_index` | 插槽位置 |
| `kind` | `0x1`=物品, `0x15`=技能 |
| `id` | 物品 id / 技能 id |

### `M.SkillAutoToggle(skill_id, type) → bool`

切换指定条目的自动战斗状态（再次调用会翻转：开→关 / 关→开）。

| 参数 | 说明 |
|---|---|
| `skill_id` | 技能 id 或 物品 id |
| `type` | `0x1`=物品，`0x15`=技能 |

### `M.GetQuestTeleportId(quest_id) → int`

任务 id 取传送 id。失败返回 `-1`。

### `M.QuestTeleport(quest_id, teleport_id?) → bool`

使命任务传送。`teleport_id` 省略时自动先调 `GetQuestTeleportId(quest_id)` 解析。

### `M.GetTaskTeleportPrice(quest_id) → int`

读取普通任务（蓝色任务）的任务传送费用。使命 / 主线任务传送通常免费，不需要调用本接口。

| 参数 | 类型 | 说明 |
|---|---|---|
| `quest_id` | int | 任务 id |

返回费用数值；未初始化、任务 id 无效或读取失败时返回 `0`。

### `M.TaskTeleport(quest_id) → bool`

发起普通任务（蓝色任务）传送。

| 参数 | 类型 | 说明 |
|---|---|---|
| `quest_id` | int | 任务 id |

成功提交返回 `true`；未初始化或任务 id 无效返回 `false`。实际是否传送成功由游戏 / 服务器条件决定。

示例：

```lua
local price = data.GetTaskTeleportPrice(quest_id)
log.info(string.format("任务传送费用: %d", price))
data.TaskTeleport(quest_id)
```

### `M.IsAutoBattleOn() → bool`

查询当前是否处于自动战斗中。

### `M.GetAutoBattleStatus() → table`

一次性返回自动战斗相关的所有联动状态。

| 字段 | 类型 | 说明 |
|---|---|---|
| `state` | int | 0=未开启, 1=自动战斗中 |
| `state_name` | string | `state` 对应中文名 |
| `gather_search` | bool | 采集物搜索 开/关 |
| `auto_arrange` | bool | 自动拾取 开/关 |
| `target_priority` | int | 0=近身对象优先, 1=威胁对象优先 |
| `target_priority_name` | string | `target_priority` 对应中文名 |

### `M.AutoBattleOn() → bool`

开启自动战斗。

### `M.AutoBattleOff() → bool`

关闭自动战斗。

---

## 选中目标

### `M.GetCurrentTarget() → { obj, id } | nil`

返回当前已选中的目标。未选中返回 `nil`。

| 字段 | 类型 | 说明 |
|---|---|---|
| `obj` | u64 | 目标对象指针 |
| `id` | u32 | 目标 id |

### `M.SelectTarget(target_obj) → bool`

选中指定对象。`target_obj` 取自 `GetAroundList()` 项的 `obj` 字段。内部自动以本人对象作为调用上下文。

---

## 移动

### `M.MoveTo(x, y, z) → bool`

让本人移动到指定世界坐标。内部自动以本人对象作为调用上下文。

| 参数 | 类型 | 说明 |
|---|---|---|
| `x` | float | 目标世界坐标 X |
| `y` | float | 目标世界坐标 Y |
| `z` | float | 目标世界坐标 Z |

坐标来源可取自 `GetAroundList()` 项的 `x/y/z`、`GetCharacter()` 的 `x/y/z` 或外部预设的导航点。

---

## NPC 对话

### `M.GetNpcDialogInfo(dlg_obj) → info` 或 `nil`

读取当前 NPC 对话框 UI 上的全部信息。需先取得对话框 UI 对象指针（通过 `M.FindUIObj("dlg_dialog").addr`）。未打开对话框时返回 `nil`。

| 参数 | 类型 | 说明 |
|---|---|---|
| `dlg_obj`（对话框对象） | u64 | NPC 对话框 UI 对象指针 |

返回 `info` 字段：

| 字段 | 类型 | 说明 |
|---|---|---|
| `dlg_obj`（对话框对象） | u64 | 对话框 UI 对象指针（透传） |
| `npc_dialog_id`（NPC对话id） | u32 | 当前已打开的 NPC 对话 id |
| `dialog_content_id`（对话内容id） | u16 | 对话内容 id |
| `quest_id`（任务id） | u32 | 关联任务 id（未接任务=0） |
| `type_text`（对话类型文本） | string | 对话类型英文标识，可用于判断阶段：`select_quest`=选择任务（选择任务的时候 一般要遍历任务才能拿到任务id），`select_quest_reward`=领奖励，`select_success`=完成任务，其它=子对话 |
| `content_text`（对话内容文本） | string | 对话内容文本（中文，UTF-8） |
| `next_text`（下阶段文本） | string | 下阶段对话文本（英文，仅展示/判断用） |
| `next_text_addr`（下阶段文本地址） | u64 | 下阶段文本所在地址（内部用于解析下阶段 id） |
| `has_next`（是否有下阶段） | bool | 是否存在下阶段对话 |

### `M.SendNpcDialog(npc_dialog_id, next_dialog_id, dialog_content_id, quest_id) → bool`

向服务器发送选择 NPC 对话选项的封包。所有参数取自 `GetNpcDialogInfo` 的返回。

| 参数 | 类型 | 说明 |
|---|---|---|
| `npc_dialog_id`（NPC对话id） | u32 | 来自 `info.npc_dialog_id` |
| `next_dialog_id`（下阶段对话id） | u16 | 下阶段对话 id；末端无下阶段时填 `0` |
| `dialog_content_id`（对话内容id） | u16 | 来自 `info.dialog_content_id` |
| `quest_id`（任务id） | u32 | 来自 `info.quest_id`（是0的话  要遍历任务获取） |

**典型调用流程**：

```lua
local dlg = data.FindUIObj("dlg_dialog")
local info = dlg and data.GetNpcDialogInfo(dlg.addr) or nil
if info then
    -- next_dialog_id 由使用者根据业务自行确定（如完成/领奖/拒绝等分支选择不同 id）
    data.SendNpcDialog(info.npc_dialog_id, 0, info.dialog_content_id, info.quest_id)
end
```

---

## 物品

### `M.DecomposeItem(item_id) → bool`

分解指定物品。`item_id` 取自 `GetInventoryList()` 项的 `id` 字段。

| 参数 | 类型 | 说明 |
|---|---|---|
| `item_id` | int | 物品 id |

示例：

```lua
for _, it in ipairs(data.GetInventoryList()) do
    if it.cat_name == "粉末/材料" then
        data.DecomposeItem(it.id)
    end
end
```

### `M.GetSelectBoxCandidates() → { item, ... }`

读取游戏内“**选择道具**”对话框当前列出的候选物品列表（当玩家双击“选择箱”类奖励后弹出的那个对话框）。对话框未打开 / 列表为空时返回空表 `{}`。

> UI 内部名为 `select_disassembly_item_dialog`（名字里带 "disassembly" 是游戏内部命名历史遗留，实际不是分解、是选箱奖励选物）。

| 字段 | 类型 | 说明 |
|---|---|---|
| `addr` | u64 | 物品对象地址 |
| `slot` | u32 | 位置序号 |
| `count` | u32 | 数量 |
| `name` | string | 物品名（UTF-8） |

示例：

```lua
for i, it in ipairs(data.GetSelectBoxCandidates()) do
    print(string.format("[%d] %s x%d", i, it.name, it.count))
end
```

### `M.ClaimSelectBox(box_id, item_index) → bool`

向服务器**发送"领取选择箱奖励"包**：在已打开的"选择道具"对话框里指定要拿哪一个候选物品，相当于在 UI 里点了"确定"。返回 `true` 表示包已发出（实际入包由服务器返回）。

> **不是用来打开对话框**——打开是 `M.UseItem(box_id)`（直接使用那个选择箱物品）。本函数只发"领取"确认包。

调用流程：

```lua
data.UseItem(0xAABBCC)                  -- 1. 打开"选择道具"对话框
local cands = data.GetSelectBoxCandidates()  -- 2. 读出候选列表
data.ClaimSelectBox(0xAABBCC, 0)         -- 3. 选第一个 (0-based / 1-based 实测)
```

| 参数 | 类型 | 说明 |
|---|---|---|
| `box_id` | int | 箱子（选择箱）物品 id |
| `item_index` | int | 选择第几个候选物品 |

---

## 拾取

### `M.LootPickup(loot_obj) → bool`

一键拾取战利品对话框中的所有物品。

| 参数 | 类型 | 说明 |
|---|---|---|
| `loot_obj` | number 或 string | 战利品对话框 UI 对象指针；传 string 时自动调 `FindUIObj` 查找 |

示例：

```lua
-- 直接传控件名
data.LootPickup("dlg_loot")
```

### `M.ReturnCharacter() → bool`

退出当前角色返回角色选择界面（等同游戏内"返回角色"菜单）。无参数。

### `M.ClickButton(ctrl) → bool`

点击按钮类 UI 控件（通过控件自身虚函数派发点击消息）。

| 参数 | 类型 | 说明 |
|---|---|---|
| `ctrl` | number 或 string | 目标按钮控件对象指针；传 string 时自动调 `FindUIObj` 查找 |

示例：

```lua
data.ClickButton("btn_ok")
```

---

## OTP / TOTP

### `M.GenOTP(secret_b32) → code, remain | nil, err`

基于 RFC 6238 (TOTP) 的 6 位动态口令生成器,30 秒窗口

| 参数 | 类型 | 说明 |
|---|---|---|
| `secret_b32` | string | Base32 编码的共享秘钥 (Google Authenticator / Nexon OTP 等通用格式) |

成功返回：

| 返回值 | 类型 | 说明 |
|---|---|---|
| `code` | string | 6 位数字字符串 (前导 0 已补齐) |
| `remain` | int | 当前 30 秒窗口剩余秒数 |

失败返回：

| 返回值 | 类型 | 说明 |
|---|---|---|
| `nil` |  |  |
| `err` | string | 错误描述 (秘钥为空 / 解码失败 / 同窗口重复调用) |

行为说明：

- 30 秒窗口内重复调用会返回 `nil, "时间未刷新, 等待 N 秒"`，避免同一个 OTP 码被重复读取/提交。
- 时间基准为本地系统时间 (`os.time`)，要求客户端时间与服务端时间偏差不大。

示例：

```lua
local code, remain = data.GenOTP("UCWLM65RN5TSFJQU")
if code then
    log.info(string.format("[OTP] %s  剩余 %d 秒", code, remain))
else
    log.warn("[OTP-ERR] " .. remain)
end
```




## 注意事项

- **必须先调用 `M.InitGameinfo()`** 一次, 其它接口才能正常工作
- 所有读取在地址无效时安全返回 `nil` / `0` / `""`, 一般无需额外保护
- `M.GetSkillList()` 首次调用会构建技能类型映射表（约 50ms），后续调用快得多，可高频轮询
- 学习新技能或切换职业后, 如发现 `GetSkillType` 返回 `-1`, 调用 `M.RebuildSkillTypeMap()` 强制刷新
- 模块文件 `AionData.lua` 内的 `LICENSE_KEY` 必须填入有效卡密, 否则驱动加载失败
- 中文输出乱码请检查 AE 日志面板编码（应为 UTF-8）
- 本模块仅做内存读取与远程查询, 不写入任何游戏内存, 安全无副作用


---

## 协议登录 (AionLogin 模块)

> **独立模块, 不依赖游戏进程**, 可在启动器阶段直接调用。
> 模块文件: `AionLogin.lua` (与 `AionData.lua` 同级)

### 快速开始

```lua
local login = require("AionLogin")
-- 可选: 指定 DLL 绝对路径; 不调用则按"裸文件名"在宿主目录搜索
-- login.SetDllPath([[C:\xxx\NcProtocol.dll]])

local ret = login.AutoLogin(
    "your_account@kakao.com",                            -- 账号
    "your_password",                                     -- 密码
    [[H:\AION_KR\bin64\aion.bin]],                       -- 游戏可执行文件路径
    [[C:\Program Files (x86)\NCSOFT\Purple]],            -- Purple 启动器安装根目录
    "",                                                  -- 语言 (留空=简中, "ko-KR"=韩, "zh-TW"=繁)
    "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",                  -- 打码平台 key
    "+8210xxxxxxxx",                                     -- 手机号 (含国际区号)
    ""                                                   -- 解码邮箱 (一般留空)
)
log.info(string.format("AutoLogin ret=%d  %s", ret, login.RetText[ret] or "未知"))
```

### 部署要求

| 文件 | 放置位置 |
|---|---|
| `AionLogin.lua` / `AionLogin.luac` | Aion 脚本目录 (与 `AionData.lua` 同级) |
| `NcProtocol.dll` | 与 `AionLogin.lua` 同目录即可 (默认搜索路径); 也可放任意位置, 用 `SetDllPath` 指定绝对路径 |

---

### `M.SetDllPath(path) → nil`

设置 `NcProtocol.dll` 的加载路径。不调用 / 传空 → 默认从**脚本同目录**加载 `NcProtocol.dll`。

| 参数 | 类型 | 说明 |
|---|---|---|
| `path` | string | DLL 绝对路径; 留空 / `nil` 走默认 (脚本同目录) |

调用后下次 `AutoLogin` 会按新路径重新加载, 已加载的实例会被丢弃。

---

### `M.AutoLogin(account, password, game_path, purple_root, lang, captcha_key, phone, decode_mail) → int`

执行账号自动登录, **同步返回**, 内部完成: Purple 启动器版本号读取 → 服务器握手 → token 获取 → 验证码处理 → 启动游戏进程。

| 参数 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `account` | string | ✅ | 账号 (邮箱形式) |
| `password` | string | ✅ | 密码 |
| `game_path` | string | ✅ | 游戏主程序路径, 例 `H:\AION_KR\bin64\aion.bin` |
| `purple_root` | string | ✅ | Purple 启动器安装根目录, 例 `C:\Program Files (x86)\NCSOFT\Purple` |
| `lang` | string |  | 语言代码; 空串=简中, `ko-KR`=韩, `zh-TW`=繁 |
| `captcha_key` | string |  | 打码平台 API key (出验证码时使用) |
| `phone` | string |  | 注册手机号, 含国际区号, 例 `+8210xxxxxxxx` (解验证用) |
| `decode_mail` | string |  | 解码邮箱 (一般留空) |

返回值: `int` —— 详见下方 `M.RetText` 返回码表。

> **首次调用**会触发 `ffi.load` 加载 DLL, 约 50ms。后续调用复用同一实例。

---

### `M.RetText` (table)

返回码 → 中文语义映射表, 用于把 `AutoLogin` 的返回值打印成可读文字:

```lua
log.info(login.RetText[ret] or "未知返回码")
```

| 返回码 | 含义 |
|---|---|
| `1` | 成功 |
| `0` | 未知错误 |
| `-1` | 启动参数获取失败 (版本号未读到等) |
| `-2` | 需要验证 / 账号出现问题 |
| `-3` | 可能服务器维护了 |
| `-5` | 账号密码错误 |
| `2` | 未检测到 NC 平台 |
| `3` | 进程创建失败 |
| `4` | 连接服务器失败 |
| `5` | 获取登录 token 失败 |
| `6` | 超时返回 |
| `7` | 接收 IpData 超时 |
| `8` | age 获取失败 |
| `9` | 账号需要验证 |
| `10` | 账号永久封禁 100 年 |
| `11` | 自动解验证失败 |
| `12` | 解验证打码时间过长 连接已断开 需重新调用 |
| `13` | 账号一个月封禁 |
| `14` | 解验证超出请求限制 等待 24 小时 |
| `15` | 同意条款失败 |

---

### 注意事项

- 必填四参 (account/password/game_path/purple_root) 任一为空会**直接返回 0**, 不真正调用 DLL
- 同一 lua 进程内 DLL 只会加载一次; 想换 DLL 路径调 `SetDllPath(new_path)` 强制重载
- `AutoLogin` 是**阻塞调用**, 内部要走完登录全流程才返回, 期间不能 yield
- 若出现 `-2 需要验证`, 通常意味着触发风控, 需配合 `captcha_key` + `phone` 才能解
- 本模块**不依赖游戏进程**, 可单独使用 (跟 `AionData` 互不影响, 都不调用也行)