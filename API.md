# data.lua 接口文档

## 模块导入

```lua
local data = require 'data'
```

---

## M.connect(target_name, license_key)

连接游戏进程,执行全部初始化(驱动加载、IL2CPP attach、DoString 地址修复)。

**参数**

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| target_name | string | `'msw.exe'` | 游戏进程名 | 非必填
| license_key | string | `'LC06B1...'` | 驱动授权密钥 |非必填

**返回** `true, pid` | `false, errmsg`

**示例**
```lua
local ok, pid = data.connect()
if not ok then log.error(tostring(pid)); return end
log.info('PID=' .. pid)
```

---

## M.player_info()

获取当前角色信息。

**参数** 无

**返回** `table | nil`

```lua
{
    Hp = "1500",
    Mp = "800",
    Level = "30",
    Exp = "45.2",
    Job = "312",
    MaxHp = "2000",
    MaxMp = "1000",
    Nickname = "玩家名",
    Gender = "0",
    CharId = "12345678",
    AP = "15",
    X = "320.5",
    Y = "-180.0",
    WalkSpeed = "2.0",
    Gravity = "-1.0",
    Invincible = "false",
    MapId = "100020000",
    MapName = "魔法森林南郊",  -- 本地化地图名,可能不存在
    Entity = "entity/xxxx-xxxx"
}
```

**示例**
```lua
local info = data.player_info()
if info then
    log.info(string.format('%s Lv.%s HP:%s/%s at (%s,%s)',
        info.Nickname, info.Level, info.Hp, info.MaxHp, info.X, info.Y))
end
```

---

## M.list_inventory()

获取背包物品列表。

**参数** 无

**返回** `table | nil`

```lua
{
    meso = 5000000,       -- 金币
    items = {
        {
            type = 2,              -- 背包类型: 1=装备 2=消耗 3=设置 4=其他 5=现金
            index = 1,             -- 槽位索引
            Code = 2000003,        -- 物品ID
            Count = 50,            -- 数量
            CUID = "abc123",       -- 唯一ID
            name = "红色药水",     -- 物品名称
            itemType = 2,          -- 物品类别 (同 type)
            itemTypeName = "消耗",
            equipInfo = nil        -- 装备详情 (仅装备类型)
        },
        -- 装备示例:
        {
            type = 1, index = 5, Code = 1002000, Count = 1,
            name = "铁剑", itemType = 1, itemTypeName = "装备",
            equipInfo = {
                atkspd = 5, subtype = 1, detail = 2,
                upg = 7, kb = 10, cash = false, scissor = 0
            }
        }
    }
}
```

**示例**
```lua
local inv = data.list_inventory()
if inv then
    log.info('金币: ' .. tostring(inv.meso))
    for _, item in ipairs(inv.items) do
        log.info(string.format('[%s] %s x%d (Code=%d)',
            item.itemTypeName, item.name, item.Count, item.Code))
    end
end
```

---

## M.list_skills()

获取已学技能列表。

**参数** 无

**返回** `table | nil`

```lua
{
    point = 10,    -- 剩余技能点
    used = 25,     -- 已用技能点
    skills = {
        {
            tier = 1,           -- 技能阶层 (0-4)
            index = 3,          -- 槽位索引
            Code = 2001005,     -- 技能ID
            CurrentLevel = 10,  -- 当前等级
            name = "魔力爪"     -- 技能名称
        }
    }
}
```

**示例**
```lua
local sk = data.list_skills()
if sk then
    log.info(string.format('剩余SP: %d', sk.point))
    for _, s in ipairs(sk.skills) do
        log.info(string.format('[T%d] %s Lv.%d (Code=%d)',
            s.tier, s.name, s.CurrentLevel, s.Code))
    end
end
```

---

## M.list_nearby()

获取周围实体(怪物/掉落物/传送门/NPC)。

**参数** 无

**返回** `table` (始终返回,空时各列表为空)

```lua
{
    mobCount = 5, dropCount = 3, portalCount = 1, npcCount = 2,
    mobs = {
        { Name = "蜗牛", MobId = 100101, Level = 5, x = 100.0, y = 200.0, Hp = "50", MaxHp = "50" }
    },
    drops = {
        { Name = "红色药水", ItemId = 2000003, OwnerCID = "mine", DropperType = 1, Free = false, x = 105.0, y = 201.0 }
    },
    portals = {
        { Name = "west00", PortalType = 1, DestMap = "100000000", DestPortal = "east00", x = 0.0, y = 150.0 }
    },
    npcs = {
        { Name = "武器商人", NpcCode = 2010001, x = 200.0, y = 180.0 }
    }
}
```

**字段说明**
- `drops[].OwnerCID`: `"mine"` 表示自己的掉落,其他为角色ID
- `drops[].Free`: `true` 表示可自由拾取
- `portals[].DestMap`: `"999999999"` 表示无目标

**示例**
```lua
local nb = data.list_nearby()
log.info(string.format('怪物%d 掉落%d 门%d NPC%d',
    nb.mobCount, nb.dropCount, nb.portalCount, nb.npcCount))
```

---

## M.pick_all()

拾取附近3格内的所有掉落物。

**参数** 无

**返回** `string`

```
"ok: picked=3 skipped=2"
```

**示例**
```lua
local result = data.pick_all()
log.info(result)
```

---

## M.list_quickslot()

获取8个快捷栏槽位状态。

**参数** 无

**返回** `table | nil`

```lua
{
    slots = {
        { slot = 1, key = "Shift",     cat = "Item",   id = "2000003" },
        { slot = 2, key = "Insert",    cat = "Empty",  id = "" },
        { slot = 3, key = "Home",      cat = "Skill",  id = "2001005" },
        { slot = 4, key = "PageUp",    cat = "Default",id = "5" },
        { slot = 5, key = "Control",   cat = "Empty",  id = "" },
        { slot = 6, key = "Delete",    cat = "Empty",  id = "" },
        { slot = 7, key = "End",       cat = "Empty",  id = "" },
        { slot = 8, key = "PageDown",  cat = "Empty",  id = "" }
    }
}
```

**槽位映射**

| slot | 按键 |
|------|------|
| 1 | Shift |
| 2 | Insert |
| 3 | Home |
| 4 | PageUp |
| 5 | Control |
| 6 | Delete |
| 7 | End |
| 8 | PageDown |

**cat 取值**: `Empty` / `Default` / `Fixed` / `Item` / `Skill`

---

## 其他接口

| 函数 | 说明 |
|------|------|
| `M.quickslot_set(slot, id, item_type)` | 设置快捷栏槽。slot: 1-8, id: 物品/技能ID, item_type: `'item'`/`'skill'` |
| `M.quickslot_use(slot, action)` | 触发快捷栏。action: `'press'`/`'hold'`/`'release'` |
| `M.quickslot_clear()` | 清空全部8个快捷栏槽 |
| `M.use_item(item_code)` | 直接使用背包中的物品 |
| `M.equip_item(item_code)` | 穿戴装备 |
| `M.do_attack()` | 普通攻击 |
| `M.list_portals()` | 获取当前地图传送门列表 |
| `M.list_characters()` | 获取角色列表 |
| `M.get_channel_info()` | 获取频道信息 |
| `M.probe_systems()` | 系统诊断(探测可用API) |
| `M.dump(obj, depth, label)` | 深度打印表到日志 |
| `M.call(cmd, arg)` | 转发 CSZF.call |
| `M.exec_raw(code, name)` | 转发 CSZF.exec_raw |
| `M.init(cszf)` | 兼容旧接口,手动设置 CSZF 对象 |
