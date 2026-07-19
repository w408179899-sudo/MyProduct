# Roadhog 数据偏移说明

本文档把 Roadhog 当前运行时用到的游戏内存数据偏移，和对应业务用途逐项对应起来。

范围说明：
- Roadhog 运行时内存读取集中在 `Roadhog/Infrastructure/Vmm/AionVmmGameApi.cs`。
- `Tool/Program.cs` 里还有一些探针测试偏移，除非复制进 `AionVmmGameApi`，否则不算 Roadhog 运行时依赖。
- `RoadhogInputKeyMap` 里的数值是 HID 按键码，不是游戏数据偏移，所以不列入本文档。
- `config/offsets.example.json` 当前为空；现在 VMM adapter 仍直接使用代码里的常量。

## Game.dll RVA

| RVA 常量 | 值 | 业务用途 |
|---|---:|---|
| `EntitySystemPointerRva` | `0x904690` | EntitySystem 根指针；读取本地玩家、锁定目标、周围怪物、尸体、异常状态都从这里进入。 |
| `ServerObjectTreeRva` | `0xD21740` | Server object 树根；把 server object id 映射到 entity id，用于目标身份、怪物列表、尸体列表。 |
| `PrimaryPartyListRva` | `0xD1BAE8` | 主队伍成员链表；用于召唤物归属和队伍召唤物快照。 |
| `SecondaryPartyListRva` | `0xD1BB50` | 备用队伍成员链表；和主链表合并、去重。 |
| `LocalEntityIdRva` | `0xD21798` | 本地玩家 entity id；`+0x2` 是当前目标 entity id，用于玩家状态、锁定目标、距离和排除自己。 |
| `LocalMaxHpRva` | `0xD267DC` | 本地最大 HP；用于维护、死亡判断、回血阈值。 |
| `LocalCurrentHpRva` | `0xD267E0` | 本地当前 HP；用于维护、死亡判断、回血阈值。 |
| `LocalMaxMpRva` | `0xD267E4` | 本地最大 MP；用于回蓝维护和阈值判断。 |
| `LocalCurrentMpRva` | `0xD267E8` | 本地当前 MP；用于回蓝维护和阈值判断。 |
| `LocalCurrentDpRva` | `0xD267EE` | 本地当前 DP；随玩家快照读出。 |
| `CameraPitchRva` | `0xD1AD14` | 普通镜头 pitch；用于转向/俯仰计算，可被 `AION_CAMERA_PITCH_RVA` 覆盖。 |
| `CameraRollRva` | `0xD1AD18` | 普通镜头 roll；随镜头角度读取，可被 `AION_CAMERA_ROLL_RVA` 覆盖。 |
| `CameraYawRva` | `0xD1AD1C` | 普通镜头 yaw；用于面向目标和寻路转向，可被 `AION_CAMERA_YAW_RVA` 覆盖。 |
| `SpecialCameraModeRva` | `0xD218C8` | 特殊镜头模式标记；未设置镜头 RVA 覆盖时，用它决定是否读取特殊镜头角度。 |
| `SpecialCameraPitchRva` | `0xD218D8` | 特殊镜头 pitch。 |
| `SpecialCameraRollRva` | `0xD218DC` | 特殊镜头 roll。 |
| `SpecialCameraYawRva` | `0xD218E0` | 特殊镜头 yaw。 |
| `SkillManagerGlobalRva` | `0xD004A0` | Skill manager 指针；技能列表、冷却、技能名读取都从这里进入。 |
| `InventoryManagerGlobalRva` | `0xD004A0` | 当前版本和 Skill manager 共用根对象；背包、金币、容量和装备 instance id 从这里进入。 |
| `ItemStaticIndexRva` | `0x908FF8` | 物品 template id 到 packed handle 的静态索引；用于品质映射。 |
| `StaticResolverChunkListRva` | `0xD03860` | 静态数据压缩块指针列表；根据 packed handle 读取物品品质。 |
| `DlgInventoryDialog27MethodRva` | `0x1BF060` | `DlgInventory` DialogId 27 方法；用于扫描背包窗口 vtable/对象。 |
| `DlgInventoryDialog28MethodRva` | `0x1C48D0` | `DlgInventory` DialogId 28 方法；用于扫描背包窗口 vtable/对象。 |

## 通用树和链表结构

| 结构字段 | 偏移 | 业务用途 |
|---|---:|---|
| `NodeLeftOffset` / 红黑树左节点 | `+0x00` | 遍历 entity 树、server object 树、已学技能树。 |
| `NodeParentOffset` / 红黑树父节点 | `+0x08` | 获取树根起点、遍历下一个节点。 |
| `NodeRightOffset` / 红黑树右节点 | `+0x10` | 遍历 entity 树、server object 树、已学技能树。 |
| `NodeIsNilOffset` / sentinel 标记 | `+0x19` | 判断红黑树空节点，停止遍历。 |
| `NodeIdOffset` / 节点 ID | `+0x20` | entity 树查找 key；技能树也复用同类布局。 |
| `NodeEntityOffset` / 节点实体指针 | `+0x28` | entity 树 value，指向 `CEntity`。 |
| `ListNodePrevOffset` / 链表前节点 | `+0x08` | 从技能 item list header 取最后一个技能 item 节点。 |
| `ListNodeValueOffset` / 链表 value | `+0x10` | 从链表节点读取 `SkillItem*`。 |

## EntitySystem 和 CEntity

| 字段 | 偏移 / 值 | 业务用途 |
|---|---:|---|
| `EntitySystem + EntityTreeOffset` | `+0x58` | entity 树 header；读取本地玩家、当前目标、周围怪、尸体、异常状态都依赖它。 |
| `CEntity + EntityTypeOffset` | `+0xF2` | entity 类型；Roadhog 当前用值 `3` 过滤 NPC/怪物类实体。 |
| `CEntity + EntityPositionFlagsOffset` | `+0xC0` | 坐标来源标记；如果包含 `0x400`，读取 alternate/local 坐标。 |
| `EntityUseAlternatePositionFlag` | `0x400` | 选择读取 `CEntity + 0x4F4` 这一组坐标。 |
| `CEntity + EntityWorldPositionOffset` | `+0x4B4` | 世界坐标 X；玩家、目标、怪物、尸体坐标都使用。 |
| `CEntity + 0x4B8` | `+0x4B4 + 4` | 世界坐标 Y。 |
| `CEntity + 0x4BC` | `+0x4B4 + 8` | 世界坐标 Z / 地面高度。 |
| `CEntity + EntityWorldAnglesOffset` | `+0x4E8` | 世界角度块；Roadhog 读取该块 `+0x08` 作为 actor yaw，用于面向/转向反馈。 |
| `CEntity + EntityLocalPositionOffset` | `+0x4F4` | alternate/local 坐标 X。 |
| `CEntity + 0x4F8` | `+0x4F4 + 4` | alternate/local 坐标 Y。 |
| `CEntity + 0x4FC` | `+0x4F4 + 8` | alternate/local 坐标 Z。 |
| `CEntity vtable + EntityProxyManagerVfuncOffset` | `+0xB8` | 读取 vfunc，并从函数体推导 proxy manager 成员偏移，用来从 `CEntity` 解析 `Actor`。 |

## Server Object 树节点

| 字段 | 偏移 | 业务用途 |
|---|---:|---|
| `ServerNodeServerObjectIdOffset` / server object id | `+0x1C` | 稳定对象身份；用于锁定目标、周围怪物、尸体、错误锁怪校验。 |
| `ServerNodeEntityIdOffset` / entity id | `+0x20` | 把 server object 树节点映射回 entity 树里的 `CEntity`。 |

## Actor

| 字段 | 偏移 | 业务用途 |
|---|---:|---|
| `Actor + ActorEntityOffset` | `+0x08` | 指回 `CEntity`；用于校验 actor 候选是否属于当前实体。 |
| `Actor + ActorObjectTypeOffset` | `+0x20` | actor 对象类型；用于校验 actor 候选，也会放进怪物/尸体快照。 |
| `Actor + ActorServerObjectIdOffset` | `+0x2C` | actor server object id；用于目标身份和“怪物是否锁定我”的比较。 |
| `Actor + ActorNpcTemplateIdOffset` | `+0x30` | NPC template id；和 XML 静态数据关联，判断怪物/主动怪/被动怪类型。 |
| `Actor + ActorStanceFlagsOffset` | `+0x34` | 姿态 flags；当前客户端里低 4 位为 `5` 且 motion mode 为 `1` 时，判断为真实坐地板休息。 |
| `Actor + ActorLevelOffset` | `+0x3E` | 等级；用于玩家显示和怪物/尸体元数据。 |
| `Actor + ActorHpPercentOffset` | `+0x40` | HP 百分比；作为 actor/尸体状态的补充数据。 |
| `Actor + ActorNameOffset` | `+0x42` | UTF-16 显示名；玩家、目标、周围怪物、尸体都读这里。 |
| `Actor + ActorSummonOwnerServerObjectIdOffset` | `+0xFC` | 召唤物 owner server object id；用于把已加载宝宝/召唤物归属到本地角色或队伍成员。 |
| `Actor + ActorInteractionStateOffset` | `+0x1CC` | 尸体交互状态；用于拾取诊断和尸体元数据。 |
| `Actor + ActorMotionModeOffset` | `+0x2D0` | 动作模式；和 stance low nibble 一起判断坐地板维护状态。 |
| `Actor + ActorTargetServerObjectIdOffset` | `+0x358` | actor 当前目标 server object id；用于判断怪物是否正在锁定本地角色。 |
| `Actor + ActorAbnormalStatusBeginOffset` | `+0xF18` | 本地异常状态数组 begin 指针。 |
| `Actor + ActorAbnormalStatusEndOffset` | `+0xF20` | 本地异常状态数组 end 指针。 |
| `Actor + ActorAbnormalCategory2CountOffset` | `+0xF38` | 有害/物理类异常状态计数；坐地板前等待异常消失时使用。 |
| `Actor + ActorCurrentSummonedPetServerObjectIdOffset` | `+0xFA0` | 角色当前召唤宝宝 server object id；当前用于本地精灵宝宝确认，扩展到非本地队员前需要 probe 验证。 |
| `Actor + ActorMaxHpOffset` | `+0x11A0` | actor 最大 HP；用于目标生死判断和本地 HP fallback。 |
| `Actor + ActorCurrentHpOffset` | `+0x11A4` | actor 当前 HP；用于目标生死判断和本地 HP fallback。 |
| `Actor + ActorLootableFlagOffset` | `+0x11E0` | 尸体可拾取标记；用于优先拾取可拾取尸体。 |

## 异常状态 Entry

异常状态从 `Actor + 0xF18` 到 `Actor + 0xF20` 之间读取，单条大小是 `0x12`，Roadhog 最多读取 512 条。

| 字段 | 偏移 | 业务用途 |
|---|---:|---|
| `AbnormalStatusEntrySize` / entry 大小 | `0x12` | 异常状态数组步长。 |
| Field00 | `+0x00` | 原始诊断字段，保留在快照里。 |
| Abnormal ID | `+0x04` | 异常状态 ID。 |
| Category | `+0x08` | 分类；category `2` 当前按有害状态处理。 |
| Time/source raw | `+0x0C` | 原始时间/来源字段，保留在快照里。 |
| Level/stack | `+0x10` | 层数或等级信息，保留在快照里。 |

## 已学技能树

| 字段 | 偏移 | 业务用途 |
|---|---:|---|
| `SkillManager + LearnedSkillTreeOffset` | `+0x828` | 已学技能 outer tree header。 |
| `LearnedSkillOuterSkillIdOffset` / outer skill id | `+0x20` | 已学技能 outer tree 的 skill id key。 |
| `LearnedSkillOuterLevelTreeHeaderOffset` / level tree header | `+0x28` | 指向该技能按等级分组的 inner tree。 |
| `LearnedSkillOuterLevelTreeSizeOffset` / level tree size | `+0x30` | inner tree 数量诊断字段。 |
| `LearnedSkillInnerLevelOffset` / learned level | `+0x20` | 该技能已学习等级。 |
| `LearnedSkillInnerItemListHeaderOffset` / item list header | `+0x28` | 当前等级下 runtime skill item 列表 header。 |
| `LearnedSkillInnerItemListSizeOffset` / item list size | `+0x30` | skill item list 数量诊断字段。 |
| `ListNodePrevOffset` / item-list prev | `+0x08` | 取最后一个 list node，通常代表当前/最高 skill item。 |
| `ListNodeValueOffset` / item-list value | `+0x10` | 从 list node 取 `SkillItem*`。 |

## SkillItem

| 字段 | 偏移 | 业务用途 |
|---|---:|---|
| `SkillItemSkillIdOffset` / skill id | `+0x08` | 校验 runtime item 是否和已学技能树 key 一致。 |
| `SkillItemField0COffset` / Field0C | `+0x0C` | runtime 原始字段，保留作诊断。 |
| `SkillItemRankValueOffset` / rank value | `+0x10` | runtime rank/类似等级字段，保留作诊断。 |
| `SkillItemNameOffset` / MSVC string name | `+0x18` | 技能名；用于 UI 映射、配置技能匹配、日志。 |
| `SkillItemCooldownDurationOffset` / cooldown duration | `+0x50` | 技能冷却时长。 |
| `SkillItemCooldownEndTimeOffset` / cooldown end time | `+0x54` | 判断技能是否可用，以及维护技能是否释放成功。 |
| `SkillItemToggleStateOffset` / toggle state | `+0x60` | toggle 技能状态。 |
| `SkillItemSkillLevelOffset` / skill level | `+0x64` | runtime 技能等级。 |
| `SkillItemStaticFieldD8Offset` / static field D8 | `+0x68` | runtime/static 信号，用于有用技能过滤和诊断。 |
| `SkillItemRuntimeStateOffset` / runtime state | `+0x6C` | runtime 状态信号，用于有用技能过滤和诊断。 |
| `SkillItemSourceFlagsOffset` / source flags | `+0x74` | runtime 来源 flags，用于有用技能过滤和诊断。 |

## MSVC 宽字符串对象

Roadhog 从 MSVC 风格的 wide string 对象读取技能名。

| 字段 | 偏移 | 业务用途 |
|---|---:|---|
| Inline/pointer storage | `+0x00` | 如果 capacity 足够大，这里当作字符 buffer 指针；否则对象本身作为 inline storage。 |
| Length | `+0x10` | 字符数量。 |
| Capacity | `+0x18` | 判断 inline 还是 pointer storage，并过滤异常字符串。 |

## InventoryManager 和物品静态数据

| 字段 | 偏移 | 业务用途 |
|---|---:|---|
| `InventoryCurrentMoneyOffset` | `+0x768` | 当前金币，按 `UInt64` 读取。 |
| `InventoryMoneyInstanceIdOffset` | `+0x770` | 金币对象 instance id，作为读取诊断。 |
| `InventoryCapacityOffset` | `+0x774` | 背包总容量。 |
| `InventoryItemTreeHeaderOffset` | `+0x778` | 背包物品红黑树 header。 |
| `InventoryItemTreeCountOffset` | `+0x780` | 背包物品树节点数量。 |
| `InventoryEquipmentIdsOffset` | `+0x788` | 32 个已装备物品 instance id，用于区分背包物品和已装备物品。 |
| `InventoryItemInstanceIdOffset` | `+0x08` | 物品 instance id。 |
| `InventoryItemTemplateIdOffset` | `+0x0C` | 物品 template id。 |
| `InventoryItemCountOffset` | `+0x10` | 堆叠数量。 |
| `InventoryItemNameOffset` | `+0x18` | 物品名 MSVC 宽字符串。 |
| `InventoryItemTypeOffset` | `+0x60` | 物品类型，用于装备、魔石、烙印等分类。 |
| `InventoryItemEquipmentMaskOffset` | `+0x74` | 装备类别掩码。 |
| `InventoryItemSlotOffset` | `+0x4EE` | 背包格子 slot，用于计算页、行、列和屏幕坐标。 |
| `ItemStaticIndexRva + 0x04` | `+0x04` | 静态索引数量。 |
| `ItemStaticIndexRva + 0x10` | `+0x10` | 静态索引 entries 指针。 |
| `StaticResolverPackedHandleOffset` | `+0x08` | 索引 entry 中的 packed handle。 |
| `ItemStaticRecordQualityRankOffset` | `+0x1D9` | 解压后的物品静态记录品质。 |

## DlgInventory 窗口

| 字段 | 偏移 | 业务用途 |
|---|---:|---|
| `DlgInventoryOpenFlagOffset` | `+0x585` | 背包窗口 open/visible 候选标记。 |
| `DlgInventoryWindowRectOffset` | `+0x58` | 旧版窗口 Rect，四个 `double`。 |
| `DlgInventoryRootWidgetOffset` | `+0x4D8` | root widget 指针，用于实验版 Rect 定位。 |
| root widget Rect | 环境变量或 `0x800` 字节扫描 | 可用 `ROADHOG_INVENTORY_ROOT_WIDGET_RECT_OFFSET` 固定；否则按 `8` 字节步长扫描合理 Rect。 |

## Debug API 地址探针

- “API探针”按钮、结果类型、地址探针接口和 VMM 实现都在 `#if DEBUG` 内，Release 产物不包含这些符号。
- 每项地址检查同时显示 `Game.dll base`、`RVA`、最终绝对地址；对象成员显示对象地址、成员偏移和最终绝对地址。
- 除业务 API 外，探针独立验证玩家基础值、普通/特殊相机、队伍链表、技能/背包管理器、背包成员、物品静态索引、静态数据块和两个 `DlgInventory` 方法地址。

## Actor 解析辅助值

这些值用于从已知 `CEntity` 找到对应 `Actor`。

| 值 | 偏移 / 范围 | 业务用途 |
|---|---:|---|
| Proxy-manager vfunc | `CEntity vtable + 0xB8` | 读取短函数体，推导 proxy manager 成员偏移。 |
| Proxy-manager scan range | `0x400` | 在 proxy manager 区域扫描候选 actor 指针。 |
| Direct CEntity scan range | `0x800` | 在 `CEntity` 内部/附近直接扫描候选 actor 指针。 |
| Nested CEntity pointer scan range | `0x800`，步长 `8` | 扫描 `CEntity` 下的指针字段，作为二级候选区域。 |
| Nested candidate region size | `0x300` | 每个二级候选区域内继续扫描 actor 指针。 |
| Actor candidate validation | `Actor + 0x08`、`+0x20`、`+0x2C` | 候选 actor 必须能指回目标 entity，object type 合理，并尽量匹配 server object id。 |

## 业务覆盖索引

| 业务 | 使用的偏移组 |
|---|---|
| 玩家 HP/MP/DP、死亡判断、维护阈值 | 本地 HP/MP/DP RVA、本地 entity id、entity 坐标、actor HP fallback。 |
| 真实坐地板休息判断 | `Actor + 0x34`、`Actor + 0x2D0`。 |
| 镜头朝向、寻路转向、面向目标 | 普通/特殊镜头 RVA、`CEntity + 0x4E8 + 8`、坐标。 |
| 锁定目标读取 | `LocalEntityIdRva + 2`、entity 树、server object 树、CEntity 类型/坐标、actor 字段。 |
| 维护期间防御 / targeting me | 怪物 `Actor + 0x358` 和本地角色 `Actor + 0x2C` 比较。 |
| 周围怪物扫描 | entity system 树、server object 树、CEntity 类型/坐标、actor template/name/HP/target、NPC XML 分类。 |
| 尸体和拾取扫描 | 周围怪物扫描偏移，再加 `Actor + 0x11E0` 和 `Actor + 0x1CC`。 |
| 坐地板前等待有害异常 | 本地 actor abnormal begin/end/category2 count 和 abnormal entry 字段。 |
| 已学技能、冷却、连续技/维护技能确认 | Skill manager RVA、已学技能树、list offset、SkillItem offset、MSVC string offset。 |
| 背包物品、金币、容量、装备状态 | Inventory manager、物品树、金币/容量、装备 instance id 和物品字段。 |
| 背包物品品质 | 物品静态索引、packed handle、静态压缩块和品质字段。 |
| 背包窗口位置 | 两个 DlgInventory 方法 RVA、open flag、旧 Rect、root widget 和实验 Rect。 |

## 当前没有作为 Roadhog VMM 直接偏移实现的内容

- 采集业务目前没有单独的 Roadhog VMM 数据接口；Tool probe 里的采集偏移不算 Roadhog 运行时依赖。
- XML 技能/NPC 文件提供静态元数据，用于技能分类和怪物分类；它们是文件数据，不是内存偏移。
