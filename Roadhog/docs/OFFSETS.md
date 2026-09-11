# Roadhog 偏移说明（2026-09-09 更新后）

当前源码已应用同事提供的 2026-09-09 静态对比结果，并连接账号 4 做只读验证。实机另发现并修复地图上下文多解引用一次的问题；详细证据与未触发场景见 [更新验证记录](OFFSETS_VALIDATION_2026-09-09.md)。

核对入口：`Roadhog/Infrastructure/Vmm/AionVmmGameApi.cs`，当前包含 **199 个游戏读取相关命名常量 + 36 项内联子偏移/数组布局/动态解析补充项**（在原 35 项外增加地图 ID 实际地址）。同一地址的别名保留各自名字，计数不是去重后的独立地址数量。仅 DEBUG 探针、只保留定义和程序扫描上限均单独标注，避免误当成正式业务读取。

- 默认进程 `Aion.bin`，模块 `Game.dll`；可分别由 `VMM_PROCESS` / `VMM_MODULE` 覆盖。
- `GameBase` 是该进程 `Game.dll` 的运行时模块基址；RVA 地址计算为 `GameBase + RVA`，不是固定绝对地址。
- `ptr64` 表示当前 64 位对象布局中的指针，需要按链路解引用。现有 `TryReadPointer` / `TryReadPointerOrNull` 会先读 8 字节，未通过校验时再尝试同地址 4 字节；表内指针数组和虚表步长仍为 8。其他字段按表中实际读取宽度解释，不能按 C# 偏移常量的 `ulong` 类型判断字段宽度。
- `config/offsets.example.json` 的 `offsets` 仍为空，当前适配器直接使用源码常量。
- 2026-09-09 验证时的源码基线 HEAD：`c6c2e3ad5c44b382d3ca7fa401d38f803b3d0c85`；当时工作区 `AionVmmGameApi.cs` 原始字节 SHA-256：`c03ce382a2f92ea0dcb6f2c98c1c45cb392843aa3f65295abb6ef92d931bb126`。
- 发布核对使用换行统一为 LF 后的源码 SHA-256：`d7252372c6d787acef58044cbdce475e2d42d3cbf31ca48aebbbab178e70e77a`。Git 的 CRLF 转换会改变原始字节散列，不代表源码逻辑变化。
- 旧文档标注的历史 Game.dll 基线为 2026-07-22 / `1926.1027.0706.7134`；这里只保留作历史参考，当前每项以源码表为准，不能把此版本号视为本次更新后的版本。
- 当前表：[OFFSETS_CURRENT_2026-09-09.csv](OFFSETS_CURRENT_2026-09-09.csv)。同事原始对比：[Markdown](OFFSETS_COMPARISON_2026-09-09.md) / [CSV](OFFSETS_COMPARISON_2026-09-09.csv)，保持原文；其中地图上下文“ptr64”的判断已被本次实机证据纠正。旧 REQUEST CSV 仅保留更新前历史。

## 本次更新范围

37 个全局 RVA 有效值发生变化：34 个直接常量和 3 个依赖它们的别名/计算值；EntitySystem 及其余对象成员布局保持对比表数值。地图上下文旧名 `CurrentMapContextPointerRva` 改为 `CurrentMapContextRva`，其 RVA 保持同事给出的 `0xD647C0`，但按内嵌对象读取。

账号 4 通过当前唯一设备 `fpga://devindex=0` 连接，PID 3400，Game.dll 基址 `0x4A240000`。从加载模块 PE 头读取 timestamp `0x6A962841`、SizeOfImage `0x141D000`、Machine `0x8664`；未把内存镜像散列当作磁盘文件 SHA-256。

## 全局 RVA 与成员常量

表中“类型”是当前代码如何读内存；未知语义保留为原始值或候选，不作额外推断。以下数值对应更新后的当前源码；同事的静态结论和实机覆盖范围分别保留记录。


### 01 Game.dll 全局 RVA

| 常量名 | 当前值 | 读取类型/布局 | 用途/读取基准 | 使用状态 | 源码行 |
|---|---|---|---|---|---:|
| `EntitySystemPointerRva` | `0x94C7B0` | ptr64 | EntitySystem 根指针；本地角色、目标、世界对象、队友、宠物与采集都从这里查实体。 | 运行时/辅助解析 | 36 |
| `ServerObjectTreeRva` | `0xD6CAB0` | ptr64 | ServerObject 树 header 指针；ServerObjectId 与 EntityId 映射。 | 运行时/辅助解析 | 37 |
| `PartyIdRva` | `0xD66920` | uint32 | 队伍 id / 队伍激活状态候选；队伍快照保留原始值，业务不单独依赖它判断组队。 | 运行时/辅助解析 | 38 |
| `PartyFlagsRva` | `0xD66924` | uint32 | 队伍权限/状态 flags 候选；队伍快照保留原始诊断值。 | 运行时/辅助解析 | 39 |
| `PartyLeaderServerObjectIdRva` | `0xD66928` | uint32 | 队长 server object id；用于标记队长成员和判断本地角色是否队长。 | 运行时/辅助解析 | 40 |
| `PrimaryPartyListRva` | `0xD66950` | ptr64 | 主队伍 std::list 的 head 指针；node + 0x10 再解引用得到成员记录。 | 运行时/辅助解析 | 41 |
| `PrimaryPartyCountRva` | `0xD66958` | uint64 | 主队伍成员记录数量；队伍快照保留原始数量，并和链表读取互相诊断。 | 运行时/辅助解析 | 42 |
| `SecondaryPartyListRva` | `0xD669B8` | ptr64 | 备用队伍成员链表 head 指针；与主链表按 ServerObjectId 合并去重。 | 运行时/辅助解析 | 43 |
| `TacticsSignTableRva` | `0xD668D0` | uint32[16] 数组基址 | 16 个 uint32 ServerObjectId 战术标记槽；队长验证和输出队员选怪信号均扫描全部槽。 | 运行时/辅助解析 | 44 |
| `CurrentMapContextRva` | `0xD647C0` | 内嵌对象基址 | GameBase + RVA 即地图上下文本身；直接 + 0x20DC 读 uint32 地图 ID。首个 qword 是虚表，不能先解引用。旧名称 CurrentMapContextPointerRva 已纠正。 | 运行时/辅助解析 | 46 |
| `CurrentChannelIndexRva` | `0xD71CB0` | uint32 | 当前频道索引，从 0 开始；此地址连续读取 8 字节，后 4 字节为频道总数。 | 运行时/辅助解析 | 48 |
| `CurrentChannelCountRva` | `0xD71CB4` | uint32 | 频道总数；实际通过 CurrentChannelIndexRva + 4 的连续块读取，此常量名本身未被引用。 | 运行时连续块内实际读取 | 49 |
| `LocalEntityIdRva` | `0xD6CB08` | uint16 | 本地 EntityId；当前目标 EntityId 在同地址 + 2，详见补充表。 | 运行时/辅助解析 | 50 |
| `LocalMaxHpRva` | `0xD71BB4` | uint32 | 本地最大 HP；用于维护、死亡判断、回血阈值。 | 运行时/辅助解析 | 51 |
| `LocalCurrentHpRva` | `0xD71BB8` | uint32 | 本地当前 HP；用于维护、死亡判断、回血阈值。 | 运行时/辅助解析 | 52 |
| `LocalMaxMpRva` | `0xD71BBC` | uint32 | 本地最大 MP；用于回蓝维护和阈值判断。 | 运行时/辅助解析 | 53 |
| `LocalCurrentMpRva` | `0xD71BC0` | uint32 | 本地当前 MP；用于回蓝维护和阈值判断。 | 运行时/辅助解析 | 54 |
| `LocalCurrentDpRva` | `0xD71BC6` | uint16 | 本地当前 DP；随玩家快照读出。 | 运行时/辅助解析 | 55 |
| `CameraPitchRva` | `0xD65B74` | float32 | 普通镜头俯仰角；支持 AION_CAMERA_PITCH_RVA 覆盖。 | 运行时/辅助解析 | 56 |
| `CameraRollRva` | `0xD65B78` | float32 | 普通镜头翻滚角；支持 AION_CAMERA_ROLL_RVA 覆盖。 | 运行时/辅助解析 | 57 |
| `CameraYawRva` | `0xD65B7C` | float32 | 普通镜头水平朝向；支持 AION_CAMERA_YAW_RVA 覆盖。 | 运行时/辅助解析 | 58 |
| `SpecialCameraModeRva` | `0xD6CC38` | uint16 | 非零表示特殊镜头候选；没有设置任何镜头 RVA 覆盖时才切换到特殊镜头三轴。 | 运行时/辅助解析 | 59 |
| `SpecialCameraPitchRva` | `0xD6CC48` | float32 | 特殊镜头俯仰角。 | 运行时/辅助解析 | 60 |
| `SpecialCameraRollRva` | `0xD6CC4C` | float32 | 特殊镜头翻滚角。 | 运行时/辅助解析 | 61 |
| `SpecialCameraYawRva` | `0xD6CC50` | float32 | 特殊镜头水平朝向。 | 运行时/辅助解析 | 62 |
| `SkillManagerGlobalRva` | `0xD4B010` | ptr64 | 技能管理器根指针；背包管理器使用同一根指针。 | 运行时/辅助解析 | 63 |
| `CurrentGatherSourceIdRva` | `0xD68CD8` | uint32 | 本地当前采集资源 ID。 | 运行时/辅助解析 | 115 |
| `CurrentGatherTargetEntityRva` | `0xD68CE0` | uint64 原始指针值 | 本地采集目标实体地址槽；当前代码只判断是否非零，不在此处解引用。 | 运行时/辅助解析 | 116 |
| `CurrentGatherSkillIdRva` | `0xD68CE8` | uint32 | 本地当前采集技能 ID。 | 运行时/辅助解析 | 117 |
| `DlgGatheringPointerRva` | `0xD63E28` | uint64 原始指针值 | 采集窗口对象地址槽，允许为 0；非零后读取窗口 flags 和两条进度条。 | 运行时/辅助解析 | 118 |
| `InventoryManagerGlobalRva` | `0xD4B010 = SkillManagerGlobalRva` | ptr64 | 等于 SkillManagerGlobalRva，当前值 0xD4B010。 | 运行时/辅助解析 | 175 |
| `ItemStaticIndexRva` | `0xD75418` | 内嵌索引头 | GameBase + RVA 就是索引头地址；+ 4 读数量，+ 0x10 读 entries 指针。 | 运行时/辅助解析 | 195 |
| `StaticResolverChunkListRva` | `0xD4E4F0` | ptr64 数组基址 | GameBase + RVA + chunkIndex * 8 直接读取压缩块指针。 | 运行时/辅助解析 | 196 |
| `DlgInventoryDialog27MethodRva` | `0x1C6820` | 代码地址 | DialogId 27 方法 RVA；仅在背包对象备用扫描路径和 DEBUG 探针中读取/匹配地址。 | 运行时/辅助解析 | 207 |
| `DlgInventoryDialog28MethodRva` | `0x1CC0E0` | 代码地址 | DialogId 28 方法 RVA；仅在背包对象备用扫描路径和 DEBUG 探针中读取/匹配地址。 | 运行时/辅助解析 | 208 |
| `DlgInventoryDialogTableRva` | `0xD63990` | ptr64 数组基址 | 通用 UI 对话框指针表；DialogId * 8 为槽偏移。 | 运行时/辅助解析 | 209 |
| `DlgInventoryDialog27PointerRva` | `0xD63A68 = DlgInventoryDialogTableRva + (27UL * 8UL)` | ptr64 | UI 表第 27 槽，GameBase + 0xD63A68；背包窗口与待丢弃物品读取入口。 | 运行时/辅助解析 | 210 |
| `DlgInventoryDialog28PointerRva` | `0xD63A70 = DlgInventoryDialogTableRva + (28UL * 8UL)` | ptr64 | UI 表第 28 槽，GameBase + 0xD63A70；另一个背包窗口候选。 | 运行时/辅助解析 | 211 |

### 02 地图上下文

| 常量名 | 当前值 | 读取类型/布局 | 用途/读取基准 | 使用状态 | 源码行 |
|---|---|---|---|---|---:|
| `CurrentMapIdOffset` | `0x20DC` | uint32 | MapContext 内的地图 ID；地图识别和固定频道控制。 | 运行时/辅助解析 | 47 |

### 03 战术标记数组

| 常量名 | 当前值 | 读取类型/布局 | 用途/读取基准 | 使用状态 | 源码行 |
|---|---|---|---|---|---:|
| `TacticsSignCount` | `16` | 数量 | 战术标记槽数；从 0 到 15，每槽 uint32，0 表示空。 | 运行时/辅助解析 | 45 |

### 04 已学技能树

| 常量名 | 当前值 | 读取类型/布局 | 用途/读取基准 | 使用状态 | 源码行 |
|---|---|---|---|---|---:|
| `LearnedSkillTreeOffset` | `0x830` | ptr64 | 已学技能 outer tree header；2026-07-22 版本由旧 `+0x828` 后移 8 字节。 | 运行时/辅助解析 | 64 |
| `LearnedSkillOuterSkillIdOffset` | `0x20` | uint32 | 已学技能 outer tree 的 skill id key。 | 运行时/辅助解析 | 65 |
| `LearnedSkillOuterLevelTreeHeaderOffset` | `0x28` | ptr64 | 指向该技能按等级分组的 inner tree。 | 运行时/辅助解析 | 66 |
| `LearnedSkillOuterLevelTreeSizeOffset` | `0x30` | uint64 | inner tree 数量诊断字段。 | 运行时/辅助解析 | 67 |
| `LearnedSkillInnerLevelOffset` | `0x20` | uint16 | 该技能已学习等级。 | 运行时/辅助解析 | 68 |
| `LearnedSkillInnerItemListHeaderOffset` | `0x28` | ptr64 | 当前等级下 runtime skill item 列表 header。 | 运行时/辅助解析 | 69 |
| `LearnedSkillInnerItemListSizeOffset` | `0x30` | uint64 | skill item list 数量诊断字段。 | 运行时/辅助解析 | 70 |

### 05 红黑树与链表节点

| 常量名 | 当前值 | 读取类型/布局 | 用途/读取基准 | 使用状态 | 源码行 |
|---|---|---|---|---|---:|
| `NodeLeftOffset` | `0x0` | ptr64 | 遍历 entity 树、server object 树、已学技能树。 | 运行时/辅助解析 | 71 |
| `NodeParentOffset` | `0x8` | ptr64 | 获取树根起点、遍历下一个节点。 | 运行时/辅助解析 | 72 |
| `NodeRightOffset` | `0x10` | ptr64 | 遍历 entity 树、server object 树、已学技能树。 | 运行时/辅助解析 | 73 |
| `NodeIsNilOffset` | `0x19` | uint8 | 判断红黑树空节点，停止遍历。 | 运行时/辅助解析 | 74 |
| `NodeIdOffset` | `0x20` | uint16 | entity 树查找 key；技能树也复用同类布局。 | 运行时/辅助解析 | 75 |
| `NodeEntityOffset` | `0x28` | ptr64 | entity 树 value，指向 `CEntity`。 | 运行时/辅助解析 | 76 |
| `ListNodeNextOffset` | `0x0` | ptr64 | 遍历队伍成员 `std::list`；从 head 的 next 开始前进。 | 运行时/辅助解析 | 77 |
| `ListNodePrevOffset` | `0x8` | ptr64 | 从技能 item list header 取最后一个技能 item 节点。 | 运行时/辅助解析 | 78 |
| `ListNodeValueOffset` | `0x10` | ptr64 | 从链表节点读取 `SkillItem*`。 | 运行时/辅助解析 | 79 |
| `ServerNodeServerObjectIdOffset` | `0x1C` | uint32 | 稳定对象身份；用于锁定目标、周围怪物、尸体、错误锁怪校验。 | 运行时/辅助解析 | 92 |
| `ServerNodeEntityIdOffset` | `0x20` | uint16 | 把 server object 树节点映射回 entity 树里的 `CEntity`。 | 运行时/辅助解析 | 93 |

### 06 EntitySystem 与 CEntity

| 常量名 | 当前值 | 读取类型/布局 | 用途/读取基准 | 使用状态 | 源码行 |
|---|---|---|---|---|---:|
| `EntityTreeOffset` | `0x58` | ptr64 | EntitySystem 内的实体红黑树 header 指针。 | 运行时/辅助解析 | 81 |
| `EntityTypeOffset` | `0x122` | uint16 | CEntity 实体类型；当前运行时用 3 过滤 NPC/怪物/宠物候选。 | 运行时/辅助解析 | 82 |
| `EntityPositionFlagsOffset` | `0xF0` | uint32 | CEntity 坐标来源标志；仅 DEBUG 地址探针读取。 | 仅 DEBUG 地址探针 | 83 |
| `EntityUseAlternatePositionFlag` | `0x400` | 掩码 | 历史备用坐标标志 0x400；本文件仅保留定义，没有读取分支引用。 | 仅保留定义，当前无引用 | 84 |
| `EntityWorldPositionOffset` | `0x4E4` | float32[3] | CEntity 世界坐标 X/Y/Z；玩家、目标、怪物、队友、宠物、寻路共享此读取链。 | 运行时/辅助解析 | 85 |
| `EntityWorldAnglesOffset` | `0x518` | float32 块 | CEntity 世界角度块；代码只读取块内 + 8，即 CEntity + 0x520，作为角色 yaw。 | 运行时/辅助解析 | 86 |
| `EntityLocalPositionOffset` | `0x524` | float32[3] | CEntity local/alternate 坐标；仅 DEBUG 探针诊断，寻路不使用此组坐标。 | 仅 DEBUG 地址探针 | 87 |
| `EntityPositionVfuncOffset` | `0x8` | ptr64 | CEntity 虚表中的位置 getter 槽位；仅 DEBUG 探针读取函数体，不调用函数。 | 仅 DEBUG 地址探针 | 88 |
| `EntityProxyManagerVfuncOffset` | `0xB8` | ptr64 | CEntity 虚表中的 proxy manager getter 槽位；读取函数体动态推导成员偏移。 | 运行时/辅助解析 | 89 |
| `EntitySystemGetEntityVfuncOffset` | `0x30` | ptr64 | EntitySystem 虚表中的 GetEntity 槽位；仅 DEBUG 探针读取函数体。 | 仅 DEBUG 地址探针 | 90 |
| `EntityTypeNpc` | `3` | 枚举值 | NPC/怪物 CEntity 类型值 3；世界对象、尸体及宠物扫描使用。 | 运行时/辅助解析 | 94 |

### 07 Actor 角色、怪物、宠物与采集物

| 常量名 | 当前值 | 读取类型/布局 | 用途/读取基准 | 使用状态 | 源码行 |
|---|---|---|---|---|---:|
| `ActorEntityOffset` | `0x8` | ptr64 | Actor 指回 CEntity，解析 Actor 候选时必须匹配预期实体。 | 运行时/辅助解析 | 96 |
| `ActorObjectTypeOffset` | `0x20` | uint32 | Actor 对象类型；玩家和采集物分别按 1、7 处理；候选有效范围为 1 到 32。 | 运行时/辅助解析 | 97 |
| `ActorPlayerObjectType` | `1` | 枚举值 | Actor 对象类型 1 表示玩家，用于队友和采集竞争玩家识别。 | 运行时/辅助解析 | 98 |
| `ActorServerObjectIdOffset` | `0x2C` | uint32 | actor server object id；用于目标身份和“怪物是否锁定我”的比较。 | 运行时/辅助解析 | 99 |
| `ActorNpcTemplateIdOffset` | `0x30` | uint32 | NPC 模板 ID；采集物复用为 GatherSourceId，与静态采集资源表关联。 | 运行时/辅助解析 | 100 |
| `ActorStanceFlagsOffset` | `0x34` | uint32 | 姿态 flags；当前客户端里低 4 位为 `5` 且 motion mode 为 `1` 时，判断为真实坐地板休息。 | 运行时/辅助解析 | 101 |
| `ActorLevelOffset` | `0x3E` | uint16 | Actor 等级；采集物复用为显示等级。 | 运行时/辅助解析 | 102 |
| `ActorHpPercentOffset` | `0x40` | uint8 | Actor HP 百分比；采集物复用同一原始字节为状态/剩余量信号，语义需单独核对。 | 运行时/辅助解析 | 103 |
| `ActorNameOffset` | `0x42` | UTF-16，最多 64 字符 | Actor 对象内的名字字符区；角色、怪物、尸体和采集物共用。 | 运行时/辅助解析 | 104 |
| `ActorSummonOwnerServerObjectIdOffset` | `0xFC` | uint32 | 召唤物 owner server object id；用于把已加载宝宝/召唤物归属到本地角色或队伍成员。 | 运行时/辅助解析 | 105 |
| `ActorGatherInteractionRadiusOffset` | `0x168` | float32 | 采集物交互半径，代码接受 0 到 100 的有限值。 | 运行时/辅助解析 | 106 |
| `ActorGatherSpawnPositionOffset` | `0x19C` | float32[3] | 采集物出生位置；世界坐标不可用时，采集距离计算会使用此位置。 | 运行时/辅助解析 | 107 |
| `ActorInteractionStateOffset` | `0x1CC` | uint32 | 尸体交互状态；用于拾取诊断和尸体元数据。 | 运行时/辅助解析 | 108 |
| `ActorClassIdOffset` | `0x228` | uint32 | 玩家职业 ID；本地角色与已加载队友读取。注意队伍记录的职业字段只有 1 字节。 | 运行时/辅助解析 | 109 |
| `ActorMotionModeOffset` | `0x2D0` | uint32 | 动作模式；和 stance low nibble 一起判断坐地板维护状态。 | 运行时/辅助解析 | 110 |
| `ActorTargetServerObjectIdOffset` | `0x358` | uint32 | actor 当前目标 server object id；用于判断怪物是否正在锁定本地角色。 | 运行时/辅助解析 | 111 |
| `ActorGatherSourceIdCandidateOffset` | `0x500` | uint32 | 其他玩家正在采集的资源 ID 候选；作为原始竞争诊断字段，不应当作已确认资源 ID。 | 运行时/辅助解析 | 112 |
| `ActorGatherActionStateOffset` | `0xAB0` | uint32 | 其他玩家采集动作状态原始值；竞争识别。 | 运行时/辅助解析 | 113 |
| `ActorGatherActionIdOffset` | `0xAB4` | uint32 | 其他玩家采集动作 ID 原始值；竞争识别。 | 运行时/辅助解析 | 114 |
| `ActorCurrentSummonedPetServerObjectIdOffset` | `0xFA0` | uint32 | 角色当前召唤宠物 ServerObjectId；定位本地宠物，随后通过实体树读取宠物状态。 | 运行时/辅助解析 | 126 |
| `ActorMaxHpOffset` | `0x11A0` | uint32 | Actor 最大 HP；目标、怪物、尸体、宠物等共用。当前本地 HP 读取以全局 HP RVA 为必需字段。 | 运行时/辅助解析 | 130 |
| `ActorCurrentHpOffset` | `0x11A4` | uint32 | Actor 当前 HP；目标生死、战斗进展、怪物、尸体及宠物状态。 | 运行时/辅助解析 | 131 |
| `ActorLootableFlagOffset` | `0x11E0` | uint32 | 尸体可拾取标记；用于优先拾取可拾取尸体。 | 运行时/辅助解析 | 132 |
| `ActorGatherObjectType` | `7` | 枚举值 | Actor 对象类型 7 表示采集物。 | 运行时/辅助解析 | 133 |

### 08 采集窗口与 Gauge

| 常量名 | 当前值 | 读取类型/布局 | 用途/读取基准 | 使用状态 | 源码行 |
|---|---|---|---|---|---:|
| `DlgGatheringFlagsOffset` | `0x28` | uint64 | 采集窗口 flags，使用最低位判断可见。 | 运行时/辅助解析 | 119 |
| `DlgGatheringVisibleMask` | `0x1` | 掩码 | 采集窗口可见位 0x01。 | 运行时/辅助解析 | 120 |
| `DlgGatheringSuccessGaugeOffset` | `0x4E8` | ptr64 | DlgGathering 内成功进度条指针，须解引用后读取 Gauge 字段。 | 运行时/辅助解析 | 121 |
| `DlgGatheringFailureGaugeOffset` | `0x500` | ptr64 | DlgGathering 内失败进度条指针，须解引用后读取 Gauge 字段。 | 运行时/辅助解析 | 122 |
| `GatherGaugeMaximumOffset` | `0x300` | float64 | Gauge 最大值。 | 运行时/辅助解析 | 123 |
| `GatherGaugeDisplayedOffset` | `0x308` | float64 | Gauge 当前显示值。 | 运行时/辅助解析 | 124 |
| `GatherGaugeTargetOffset` | `0x310` | float64 | Gauge 目标值。 | 运行时/辅助解析 | 125 |

### 09 本地异常状态数组

| 常量名 | 当前值 | 读取类型/布局 | 用途/读取基准 | 使用状态 | 源码行 |
|---|---|---|---|---|---:|
| `ActorAbnormalStatusBeginOffset` | `0xF18` | ptr64，可空 | 本地 Actor 异常状态数组 begin；单条步长 0x12。 | 运行时/辅助解析 | 127 |
| `ActorAbnormalStatusEndOffset` | `0xF20` | ptr64，可空 | 本地 Actor 异常状态数组 end；以 (end - begin) / 0x12 计算条数。 | 运行时/辅助解析 | 128 |
| `ActorAbnormalCategory2CountOffset` | `0xF38` | uint32 | 异常状态 bucket 2 原始计数；不单独等同于有害/物理异常。 | 运行时/辅助解析 | 129 |
| `AbnormalStatusEntrySize` | `0x12` | 字节步长 | 本地与队伍异常状态 entry 大小均为 0x12；entry 五个字段见补充表。 | 运行时/辅助解析 | 134 |
| `MaxActorAbnormalStatusEntries` | `512` | 读取上限 | 本地异常条数保护上限 512；这是程序限制，不是已经确认的游戏数组容量。 | 运行时/辅助解析 | 135 |

### 10 PartyMemberRecord 队伍成员

| 常量名 | 当前值 | 读取类型/布局 | 用途/读取基准 | 使用状态 | 源码行 |
|---|---|---|---|---|---:|
| `PartyMemberPartySlotOffset` | `0x0` | uint32 | 原始队伍字段候选；快照保留为 `PartySlot`，不能直接当 UI 槽位或 F2-F6 顺序。 | 运行时/辅助解析 | 136 |
| `PartyMemberServerObjectIdOffset` | `0x4` | uint32 | 队员 server object id；队员身份、队长/本地角色匹配、召唤物归属都依赖它。 | 运行时/辅助解析 | 137 |
| `PartyMemberMaxHpOffset` | `0x8` | uint32 | 队员最大 HP；用于队伍快照、保护目标选择、队友生死判断。 | 运行时/辅助解析 | 138 |
| `PartyMemberCurrentHpOffset` | `0xC` | uint32 | 队员当前 HP；用于队友血量阈值、死亡判断和队长保护逻辑。 | 运行时/辅助解析 | 139 |
| `PartyMemberMaxMpOffset` | `0x10` | uint32 | 队员最大 MP；保留在队伍快照中。 | 运行时/辅助解析 | 140 |
| `PartyMemberCurrentMpOffset` | `0x14` | uint32 | 队员当前 MP；保留在队伍快照中。 | 运行时/辅助解析 | 141 |
| `PartyMemberMaxFlightTimeOffset` | `0x18` | uint32 | 队员最大飞行时间，毫秒；队伍状态诊断字段。 | 运行时/辅助解析 | 142 |
| `PartyMemberCurrentFlightTimeOffset` | `0x1C` | uint32 | 队员剩余飞行时间，毫秒；队伍状态诊断字段。 | 运行时/辅助解析 | 143 |
| `PartyMemberAreaField0Offset` | `0x20` | uint32 | 区域/指针类原始字段候选；当前只保留诊断，不作为地图判断。 | 运行时/辅助解析 | 144 |
| `PartyMemberAreaField1Offset` | `0x24` | uint32 | 区域/指针类原始字段候选；当前只保留诊断，不作为地图判断。 | 运行时/辅助解析 | 145 |
| `PartyMemberCachedXOffset` | `0x28` | float32 | 队员缓存坐标 X；队友不在 live actor 可见范围时作为诊断位置。 | 运行时/辅助解析 | 146 |
| `PartyMemberCachedYOffset` | `0x2C` | float32 | 队员缓存坐标 Y。 | 运行时/辅助解析 | 147 |
| `PartyMemberCachedZOffset` | `0x30` | float32 | 队员缓存坐标 Z。 | 运行时/辅助解析 | 148 |
| `PartyMemberClassIdOffset` | `0x34` | uint8 | 队员职业 id；映射到 `AionClassId` 和职业名。 | 运行时/辅助解析 | 149 |
| `PartyMemberLevelOffset` | `0x36` | uint8 | 队员等级。 | 运行时/辅助解析 | 150 |
| `PartyMemberDataFlagsOffset` | `0x37` | uint8 | 队员数据 flags；用于诊断和 `HasAbnormalBlock`，不作为异常数组读取硬门槛。 | 运行时/辅助解析 | 151 |
| `PartyMemberFlightAreaFlagOffset` | `0x38` | uint8 | 可飞区域/飞行许可候选；保留在快照中。 | 运行时/辅助解析 | 152 |
| `PartyMemberFlightFlagsOffset` | `0x39` | uint8 | 飞行状态 flags 候选；保留在快照中。 | 运行时/辅助解析 | 153 |
| `PartyMemberRuntimeStateOffset` | `0x3A` | uint8 | 运行时状态字段；含义未完全定型，保留诊断。 | 运行时/辅助解析 | 154 |
| `PartyMemberNameOffset` | `0x3B` | UTF-16，最多 26 字符 | 队伍成员记录内的名字字符区。 | 运行时/辅助解析 | 155 |
| `PartyMemberControlStatusMaskOffset` | `0x6F` | uint64 | 控制/异常状态掩码；队伍快照保留给保护逻辑和诊断消费。 | 运行时/辅助解析 | 156 |
| `PartyMemberHasAbnormalBlockFlag` | `0x8` | 掩码 | DataFlags 的 0x08 位；快照保存此信号，读取异常数组不以此位作为硬门槛。 | 运行时/辅助解析 | 157 |
| `PartyMemberAbnormalCountOffset` | `0x77` | int16 | 队员异常状态原始数量；读取时按 `PartyMemberMaxAbnormalCount` 截断。 | 运行时/辅助解析 | 158 |
| `PartyMemberAbnormalEntriesOffset` | `0x79` | 内嵌 entry 数组 | 成员异常列表起始地址为 member + 0x79，不再解引用；步长 0x12。 | 运行时/辅助解析 | 159 |
| `PartyMemberUpdateTimeOffset` | `0x859` | uint32 | 队员状态更新时间 tick；保留为诊断字段。 | 运行时/辅助解析 | 160 |
| `PartyMemberMaxAbnormalCount` | `112` | 读取上限 | 队伍成员异常条数上限 112。 | 运行时/辅助解析 | 161 |

### 11 SkillItem 技能对象

| 常量名 | 当前值 | 读取类型/布局 | 用途/读取基准 | 使用状态 | 源码行 |
|---|---|---|---|---|---:|
| `SkillItemSkillIdOffset` | `0x8` | uint32 | 校验 runtime item 是否和已学技能树 key 一致。 | 运行时/辅助解析 | 163 |
| `SkillItemField0COffset` | `0xC` | uint32 | runtime 原始字段，保留作诊断。 | 运行时/辅助解析 | 164 |
| `SkillItemRankValueOffset` | `0x10` | uint64 | runtime rank/类似等级字段，保留作诊断。 | 运行时/辅助解析 | 165 |
| `SkillItemNameOffset` | `0x18` | MSVC wstring | 技能名；用于 UI 映射、配置技能匹配、日志。 | 运行时/辅助解析 | 166 |
| `SkillItemCooldownDurationOffset` | `0x50` | uint32 | 技能冷却时长。 | 运行时/辅助解析 | 167 |
| `SkillItemCooldownEndTimeOffset` | `0x54` | uint32 | 判断技能是否可用，以及维护技能是否释放成功。 | 运行时/辅助解析 | 168 |
| `SkillItemToggleStateOffset` | `0x60` | uint32 | toggle 技能状态。 | 运行时/辅助解析 | 169 |
| `SkillItemSkillLevelOffset` | `0x64` | uint32 | runtime 技能等级。 | 运行时/辅助解析 | 170 |
| `SkillItemStaticFieldD8Offset` | `0x68` | uint32 | runtime/static 信号，用于有用技能过滤和诊断。 | 运行时/辅助解析 | 171 |
| `SkillItemRuntimeStateOffset` | `0x6C` | uint32 | runtime 状态信号，用于有用技能过滤和诊断。 | 运行时/辅助解析 | 172 |
| `SkillItemSourceFlagsOffset` | `0x74` | uint32 | runtime 来源 flags，用于有用技能过滤和诊断。 | 运行时/辅助解析 | 173 |

### 12 InventoryManager 背包管理器

| 常量名 | 当前值 | 读取类型/布局 | 用途/读取基准 | 使用状态 | 源码行 |
|---|---|---|---|---|---:|
| `InventoryCurrentMoneyOffset` | `0x770` | uint64 | 背包管理器内当前金币。 | 运行时/辅助解析 | 176 |
| `InventoryMoneyInstanceIdOffset` | `0x778` | uint32 | 金币对象 instance id，作为读取诊断。 | 运行时/辅助解析 | 177 |
| `InventoryCapacityOffset` | `0x77C` | uint32 | 背包总容量。 | 运行时/辅助解析 | 178 |
| `InventoryItemTreeHeaderOffset` | `0x780` | ptr64 | 背包物品红黑树 header。 | 运行时/辅助解析 | 179 |
| `InventoryItemTreeCountOffset` | `0x788` | uint64 | 背包物品树节点数量。 | 运行时/辅助解析 | 180 |
| `InventoryEquipmentIdsOffset` | `0x790` | uint32[32] | 已装备物品 InstanceId 数组，逐项步长 4 字节。 | 运行时/辅助解析 | 181 |
| `InventoryEquipmentIdCount` | `32` | 数组数量 | 已装备物品 InstanceId 数组长度 32。 | 运行时/辅助解析 | 182 |
| `InventorySlotsPerPage` | `27` | 布局参数 | 每页 27 格；此 VMM 文件中只保留常量定义，未引用。 | 仅保留定义，当前无引用 | 183 |
| `InventoryColumnsPerPage` | `9` | 布局参数 | 每页 9 列；此 VMM 文件中只保留常量定义，未引用。 | 仅保留定义，当前无引用 | 184 |

### 13 背包物品节点与物品对象

| 常量名 | 当前值 | 读取类型/布局 | 用途/读取基准 | 使用状态 | 源码行 |
|---|---|---|---|---|---:|
| `InventoryNodeInstanceIdOffset` | `0x20` | uint32 | 背包物品树节点上的 instance id key；用于校验节点和物品对象一致。 | 运行时/辅助解析 | 185 |
| `InventoryNodeItemOffset` | `0x28` | ptr64 | 背包物品树节点上的 `InventoryItem*`。 | 运行时/辅助解析 | 186 |
| `InventoryItemInstanceIdOffset` | `0x8` | uint32 | 物品 instance id。 | 运行时/辅助解析 | 187 |
| `InventoryItemTemplateIdOffset` | `0xC` | uint32 | 物品 template id。 | 运行时/辅助解析 | 188 |
| `InventoryItemCountOffset` | `0x10` | uint64 | 堆叠数量。 | 运行时/辅助解析 | 189 |
| `InventoryItemNameOffset` | `0x18` | MSVC wstring | 物品名 MSVC 宽字符串。 | 运行时/辅助解析 | 190 |
| `InventoryItemTypeOffset` | `0x60` | uint32 | 物品类型，用于装备、魔石、烙印等分类。 | 运行时/辅助解析 | 191 |
| `InventoryItemEquipmentMaskOffset` | `0x74` | uint32 | 装备类别掩码。 | 运行时/辅助解析 | 192 |
| `InventoryItemVendorSellUnitPriceOffset` | `0x80` | uint64 | 当前 NPC 杂货商单件收购价。 | 运行时/辅助解析 | 193 |
| `InventoryItemSlotOffset` | `0x4F6` | int16 | 物品背包 slot，按有符号 16 位读取。 | 运行时/辅助解析 | 194 |

### 14 物品静态索引与压缩块

| 常量名 | 当前值 | 读取类型/布局 | 用途/读取基准 | 使用状态 | 源码行 |
|---|---|---|---|---|---:|
| `ItemStaticRecordIdOffset` | `0x0` | uint32 | 解压后静态物品记录的模板 ID，用来校验记录与目标物品一致。 | 运行时/辅助解析 | 197 |
| `ItemStaticRecordQualityRankOffset` | `0x1E1` | uint8 | 解压后静态物品记录的品质字节；不在 InventoryItem 对象内。 | 运行时/辅助解析 | 198 |
| `StaticResolverEntrySize` | `0x10` | 字节步长 | 静态索引 entry 步长 0x10。 | 运行时/辅助解析 | 199 |
| `StaticResolverPackedHandleOffset` | `0x8` | uint64，取低 32 位 | 静态索引 entry 中的 packed handle；读取 8 字节后截为 uint32。 | 运行时/辅助解析 | 200 |
| `StaticResolverPackedChunkShift` | `14` | 位移量 | packed handle 右移 14 位得到 rawChunkIndex；非零时再减 1，0 保持 0。 | 运行时/辅助解析 | 201 |
| `StaticResolverPackedOffsetMask` | `0x3FFF` | 掩码 | packed handle & 0x3FFF 得到解压块内记录偏移。 | 运行时/辅助解析 | 202 |
| `MaxStaticResolverEntries` | `2000000` | 参数/上限 | 静态索引数量安全上限，防止损坏数据导致超大遍历。 | 程序参数/读取保护上限 | 203 |
| `MaxStaticChunkCompressedBytes` | `4194304` | 参数/上限 | 单个静态压缩块大小上限。 | 程序参数/读取保护上限 | 204 |
| `MaxStaticChunkUncompressedBytes` | `16777216` | 参数/上限 | 单个静态解压块大小上限。 | 程序参数/读取保护上限 | 205 |

### 15 背包窗口与对象扫描

| 常量名 | 当前值 | 读取类型/布局 | 用途/读取基准 | 使用状态 | 源码行 |
|---|---|---|---|---|---:|
| `DlgInventoryWidgetFlagsOffset` | `0x28` | uint64 | UIWidget flags；背包和丢弃确认框共用最低可见位。 | 运行时/辅助解析 | 212 |
| `DlgInventoryVisibleMask` | `0x1` | 掩码 | flags & 0x01 非零表示窗口可见。 | 运行时/辅助解析 | 213 |
| `DlgInventoryPageDirtyFlagBaseOffset` | `0x585` | uint8，每页一步 | 历史页面 dirty 标志 + 0x585 + pageIndex；此文件中仅保留定义，不用于判断窗口开关。 | 仅保留定义，当前无引用 | 214 |
| `DlgInventoryWindowRectOffset` | `0x58` | float64[4] | DlgInventory 内 Rect 起始；默认 LegacyDialogRect 路径读取 X/Y/宽/高，间隔 8 字节。 | 运行时/辅助解析 | 215 |
| `DlgInventoryRootWidgetOffset` | `0x4D8` | ptr64 | DlgInventory 内 root widget 指针；RootWidgetRectExperimental 路径使用。 | 运行时/辅助解析 | 216 |
| `DlgInventoryVtableBackSlots` | `256` | 参数/上限 | 从 `DlgInventory` 方法地址向前扫描 vtable 槽位的最大数量。 | 程序参数/读取保护上限 | 233 |
| `RootWidgetRectScanBytes` | `0x800` | 参数/上限 | root widget 内扫描 Rect 候选的范围。 | 程序参数/读取保护上限 | 234 |
| `RootWidgetRectScanStep` | `0x8` | 参数/上限 | root widget Rect 候选扫描步长。 | 程序参数/读取保护上限 | 235 |
| `RootWidgetRectOffsetEnvironmentVariable` | `ROADHOG_INVENTORY_ROOT_WIDGET_RECT_OFFSET` | 环境变量名 | ROADHOG_INVENTORY_ROOT_WIDGET_RECT_OFFSET；指定 root widget 内 Rect 起点，未设置时扫描候选。 | 运行时/辅助解析 | 236 |
| `InventoryUiMinAllocationSize` | `0x400` | 参数/上限 | 背包 UI 对象 VAD 扫描的最小 allocation size。 | 程序参数/读取保护上限 | 237 |
| `InventoryUiMaxAllocationSize` | `0x3000` | 参数/上限 | 背包 UI 对象 VAD 扫描的最大 allocation size。 | 程序参数/读取保护上限 | 238 |
| `InventoryUiVadScanBytes` | `1073741824` | 参数/上限 | 背包 UI VAD 扫描范围上限。 | 程序参数/读取保护上限 | 239 |
| `InventoryUiObjectScanLimit` | `32` | 参数/上限 | 背包 UI 对象候选扫描数量上限。 | 程序参数/读取保护上限 | 240 |

### 16 丢弃确认框

| 常量名 | 当前值 | 读取类型/布局 | 用途/读取基准 | 使用状态 | 源码行 |
|---|---|---|---|---|---:|
| `DlgInventoryPendingDestroyItemIdOffset` | `0x598` | uint32 | DialogId 27 背包对象内正在等待丢弃确认的物品 InstanceId；为 0 表示没有待确认物品。 | 运行时/辅助解析 | 217 |
| `FirstNormalDiscardDialogId` | `336` | DialogId | 普通丢弃确认框扫描范围起点，包含 336。 | 运行时/辅助解析 | 218 |
| `LastNormalDiscardDialogId` | `355` | DialogId | 普通丢弃确认框扫描范围终点，包含 355。 | 运行时/辅助解析 | 219 |
| `FirstSpecialDiscardDialogId` | `356` | DialogId | 特殊丢弃确认框扫描范围起点，包含 356。 | 运行时/辅助解析 | 220 |
| `LastSpecialDiscardDialogId` | `365` | DialogId | 特殊丢弃确认框扫描范围终点，包含 365。 | 运行时/辅助解析 | 221 |
| `DiscardMsgBoxTypeOffset` | `0x4D8` | uint32 | 丢弃确认框类型，用于筛选正确弹窗。 | 运行时/辅助解析 | 222 |
| `DiscardMsgBoxConfirmActionOffset` | `0x4DC` | uint32 | 丢弃确认框确认动作 ID。 | 运行时/辅助解析 | 223 |
| `DiscardMsgBoxCancelActionOffset` | `0x4E0` | uint32 | 丢弃确认框取消动作 ID。 | 运行时/辅助解析 | 224 |
| `DiscardMsgBoxItemInstanceIdOffset` | `0x4E8` | uint32 | 丢弃确认框第一个物品 InstanceId 字段。 | 运行时/辅助解析 | 225 |
| `DiscardMsgBoxItemInstanceId2Offset` | `0x508` | uint32 | 丢弃确认框第二个物品 InstanceId 字段；与待丢弃 ID 一同校验。 | 运行时/辅助解析 | 226 |
| `NormalDiscardMsgBoxType` | `2049` | 枚举值 | 普通丢弃确认框类型 2049（0x801）。 | 运行时/辅助解析 | 227 |
| `NormalDiscardConfirmAction` | `2101` | 枚举值 | 普通丢弃确认动作 2101（0x835）。 | 运行时/辅助解析 | 228 |
| `NormalDiscardCancelAction` | `2104` | 枚举值 | 普通丢弃取消动作 2104（0x838）。 | 运行时/辅助解析 | 229 |
| `SpecialDiscardMsgBoxType` | `3` | 枚举值 | 特殊丢弃确认框类型 3。 | 运行时/辅助解析 | 230 |
| `SpecialDiscardConfirmAction` | `2105` | 枚举值 | 特殊丢弃确认动作 2105（0x839）。 | 运行时/辅助解析 | 231 |
| `SpecialDiscardCancelAction` | `2106` | 枚举值 | 特殊丢弃取消动作 2106（0x83A）。 | 运行时/辅助解析 | 232 |

## 内联子偏移、数组布局与动态解析

这些项没有全部独立定义成常量。只复制源码顶部的 RVA/Offset 常量表会漏掉它们。

| 数据项 | 当前地址/规则 | 类型 | 说明 | 源码行 |
|---|---|---|---|---:|
| 当前目标 EntityId | `GameBase + 0xD6CB0A` | uint16 | LocalEntityIdRva + 2；没有独立命名常量。 | 5647 |
| 世界坐标 Y | `CEntity + 0x4E8` | float32 | EntityWorldPositionOffset + 4。 | 8740 |
| 世界坐标 Z | `CEntity + 0x4EC` | float32 | EntityWorldPositionOffset + 8。 | 8741 |
| 角色朝向 yaw | `CEntity + 0x520` | float32 | EntityWorldAnglesOffset + 8；代码直接按角度归一化，不做弧度转换。 | 5811 |
| 诊断 local 坐标 Y | `CEntity + 0x528` | float32 | EntityLocalPositionOffset + 4；仅 DEBUG 坐标探针。 | 1749 |
| 诊断 local 坐标 Z | `CEntity + 0x52C` | float32 | EntityLocalPositionOffset + 8；仅 DEBUG 坐标探针。 | 1749 |
| 采集出生坐标 Y | `Actor + 0x1A0` | float32 | ActorGatherSpawnPositionOffset + 4。 | 9485 |
| 采集出生坐标 Z | `Actor + 0x1A4` | float32 | ActorGatherSpawnPositionOffset + 8。 | 9485 |
| 异常 entry Field00 | `entry + 0x00` | uint32 | 本地/队伍共用，原始诊断字段。 | 6022 |
| 异常 entry AbnormalId | `entry + 0x04` | uint32 | 本地/队伍共用，异常状态 ID。 | 6023 |
| 异常 entry Category | `entry + 0x08` | uint32 | 本地/队伍共用，原始 bucket/category，不能直接等同 Buff/Debuff。 | 6024 |
| 异常 entry TimeOrSource | `entry + 0x0C` | uint32 | 本地/队伍共用，原始时间/来源字段。 | 6025 |
| 异常 entry LevelOrStack | `entry + 0x10` | uint16 | 本地/队伍共用，等级/层数原始值。 | 6026 |
| MSVC wstring 字符存储 | `stringObject + 0x00` | 16 字节内联区或 ptr64 | capacity 小于 8 时从对象本身读 UTF-16；否则先解引用此处的字符指针。 | 10232 |
| MSVC wstring 长度 | `stringObject + 0x10` | uint64 | 字符数；代码读取上限 256。 | 10215 |
| MSVC wstring 容量 | `stringObject + 0x18` | uint64 | 以 8 为内联/堆字符串分界；容量保护上限 0x100000。 | 10216 |
| 物品静态索引数量 | `GameBase + 0xD7541C` | uint32 | ItemStaticIndexRva + 0x04；索引头为直接内嵌结构。 | 10338 |
| 物品静态索引 entries | `GameBase + 0xD75428` | ptr64 | ItemStaticIndexRva + 0x10；解引用为排序的 entry 数组。 | 10341 |
| 物品静态索引 key | `entry + 0x00` | uint32 | TemplateId 二分查找键，entry 步长 0x10。 | 10353 |
| 压缩块 compressedSize | `chunkPointer + 0x00` | uint32 | 压缩长度。 | 10397 |
| 压缩块 uncompressedSize | `chunkPointer + 0x04` | uint32 | 解压长度。 | 10398 |
| 压缩块数据 | `chunkPointer + 0x08` | byte[compressedSize] | zlib 数据；当前解码去掉 2 字节头和 4 字节尾，再以 Deflate 解压。 | 10403 |
| 背包 Rect X | `DlgInventory + 0x58` | float64 | 默认 Rect 的第 0 项。实验路径则相对动态 rectAddress + 0。 | 9805 |
| 背包 Rect Y | `DlgInventory + 0x60` | float64 | 默认 Rect 的第 1 项。实验路径相对 rectAddress + 8。 | 9806 |
| 背包 Rect 宽 | `DlgInventory + 0x68` | float64 | 默认 Rect 的第 2 项。实验路径相对 rectAddress + 0x10。 | 9807 |
| 背包 Rect 高 | `DlgInventory + 0x70` | float64 | 默认 Rect 的第 3 项。实验路径相对 rectAddress + 0x18。 | 9808 |
| 战术标记槽地址 | `GameBase + 0xD668D0 + i * 4` | uint32[16] | i 为 0 到 15，一次读取 64 字节；保存 ServerObjectId，0 表示空。 | 2618 |
| 已装备物品槽地址 | `InventoryManager + 0x790 + i * 4` | uint32[32] | i 为 0 到 31；每槽保存物品 InstanceId。 | 4906 |
| UI 对话框槽地址 | `GameBase + 0xD63990 + dialogId * 8` | ptr64 | 背包 27/28，普通丢弃 336 到 355，特殊丢弃 356 到 365；范围包含端点。 | 3460 |
| 静态压缩块槽地址 | `GameBase + 0xD4E4F0 + chunkIndex * 8` | ptr64 | 数组基址直接加索引后读取指针；chunkIndex 来自 packed handle。 | 10395 |
| 对象虚表指针 | `CEntity / EntitySystem / DlgInventory + 0x00` | ptr64 | 对象头解引用获得 vtable；再读取对应 vfunc 槽。 | 8838 |
| 动态 CEntity 到 proxy manager 成员偏移 | `从 [CEntity.vtable + 0xB8] 的函数体推导` | uint32 disp32 或 uint8 disp8 | 读取 16 字节函数体；48 8B 81 后 +3 读取 disp32，或 48 8B 41 后 +3 读取 disp8，再读取 [CEntity + disp]。 | 8845 |
| 动态 proxy manager 到 Actor 指针位置 | `扫描 proxyManager 的前 0x400 字节` | ptr64，步长 8 | 以 Actor + 8 回指 CEntity、对象类型和 ServerObjectId 校验候选；没有写死单一 Actor 指针偏移。 | 8781 |
| 动态 root widget Rect 偏移 | `[DlgInventory + 0x4D8] + 运行时解析偏移` | float64[4] | 实验路径；由环境变量指定，或在 root widget 前 0x800 字节内以 8 字节步长扫描。 | 9786 |
| 动态 DlgInventory 虚表与对象定位 | `GameBase + 方法 RVA 的引用槽反向扫描` | ptr64 | 备用路径：方法地址引用槽向前 0 到 256 个槽（每槽 8 字节）形成虚表候选，再扫 heap/private VAD 中对象。 | 9250 |

| 当前地图 ID 实际地址 | `GameBase + 0xD6689C` | uint32 | 内嵌地图上下文直接加成员偏移；账号 4 读到 120010000。错误的额外解引用会读到字符串片段 op，即 7340143。 | 2688 |

## 必须一起核对的指针链

以下 `ptr64(addr)`、`u16(addr)`、`u32(addr)` 表示从地址读取对应类型；`GameBase` 本身不解引用。

```text
MapContext = GameBase + 0xD647C0  // 内嵌对象，不解引用
MapId = u32(MapContext + 0x20DC)  // GameBase + 0xD6689C

EntitySystem = ptr64(GameBase + 0x94C7B0)
EntityTreeHeader = ptr64(EntitySystem + 0x58)
LocalEntityId = u16(GameBase + 0xD6CB08)
TargetEntityId = u16(GameBase + 0xD6CB0A)
EntityTreeNode.key = u16(node + 0x20)
CEntity = ptr64(EntityTreeNode + 0x28)

ServerTreeHeader = ptr64(GameBase + 0xD6CAB0)
ServerObjectId = u32(ServerTreeNode + 0x1C)
EntityId = u16(ServerTreeNode + 0x20)
ServerObjectId -> EntityId -> EntityTree -> CEntity -> 动态 proxy/Actor 解析

PartyHead = ptr64(GameBase + 0xD66950 或 0xD669B8)
PartyNode = ptr64(PartyHead + 0x00)
PartyMemberRecord = ptr64(PartyNode + 0x10)
NextPartyNode = ptr64(PartyNode + 0x00)
PartyMemberRecord.ServerObjectId = u32(member + 0x04)
队友实时位置 = 匹配 Actor + 0x2C 的 ServerObjectId 后，从其 CEntity + 0x4E4 读取
队友缓存位置 = member + 0x28 / 0x2C / 0x30（float32，只保留缓存/诊断含义）

SkillManager = ptr64(GameBase + 0xD4B010)
SkillOuterHeader = ptr64(SkillManager + 0x830)
OuterNode.SkillId = u32(OuterNode + 0x20)
InnerHeader = ptr64(OuterNode + 0x28)
HighestLevelNode = ptr64(InnerHeader + 0x10)
ItemListHeader = ptr64(HighestLevelNode + 0x28)
LastItemNode = ptr64(ItemListHeader + 0x08)
SkillItem = ptr64(LastItemNode + 0x10)

InventoryManager = ptr64(GameBase + 0xD4B010)
InventoryTreeHeader = ptr64(InventoryManager + 0x780)
InventoryItem = ptr64(InventoryNode + 0x28)
InventoryNode.InstanceId = u32(InventoryNode + 0x20)
InventoryItem.InstanceId = u32(InventoryItem + 0x08)

StaticIndex = GameBase + 0xD75418
StaticEntries = ptr64(StaticIndex + 0x10)
Entry = StaticEntries + index * 0x10
PackedHandle = low32(u64(Entry + 0x08))
rawChunkIndex = PackedHandle >> 14
chunkIndex = rawChunkIndex == 0 ? 0 : rawChunkIndex - 1
recordOffset = PackedHandle & 0x3FFF
Chunk = ptr64(GameBase + 0xD4E4F0 + chunkIndex * 8)
Record = 解压 Chunk + 8 的 zlib 数据后，在解压缓冲区内加 recordOffset
Record.TemplateId = u32(Record + 0x00)
Record.QualityRank = u8(Record + 0x1E1)

GatherDialog = ptr64(GameBase + 0xD63E28)
SuccessGauge = ptr64(GatherDialog + 0x4E8)
FailureGauge = ptr64(GatherDialog + 0x500)
Gauge.Maximum / Displayed / Target = double(Gauge + 0x300 / 0x308 / 0x310)

InventoryDialog27 = ptr64(GameBase + 0xD63990 + 27 * 8)
PendingDiscardInstanceId = u32(InventoryDialog27 + 0x598)
ConfirmDialog = ptr64(GameBase + 0xD63990 + dialogId * 8)
确认框 flags、type、confirmAction、cancelAction、两个 itemId 一起匹配
```

红黑树 header 的 `+0` 是最左节点、`+8` 是根节点、`+0x10` 是最右节点；普通节点相同三个位置为左/父/右指针。`+0x19` 为 nil 字节。不能把树 header、普通树节点和 std::list head 当作同一种对象。

## 动态读取和格式约束

- **Actor 解析**：优先从 `CEntity.vtable + 0xB8` 的函数体提取 proxy manager 成员偏移，再扫 proxy manager 前 `0x400` 字节。备用路径扫描 CEntity 前 `0x800` 字节，再扫描 CEntity 前 `0x800` 内每个指针所指向区域的前 `0x300` 字节；指针步长均为 8。Actor 候选必须回指原 CEntity，objectType 在 1 到 32 之间；ServerObjectId 用于身份匹配评分。更新后函数不是 `48 8B 81 disp32` / `48 8B 41 disp8` 开头时，现有动态 getter 解析无法命中。
- **背包窗口**：优先读取对话框表的 27/28 槽。备用方法扫描以整个模块为范围，模块大小读取失败时采用 `0x02000000`；方法引用搜索参数 `0x1000` 是每次读取的块大小。虚表槽与对象指针均为 8 字节。VAD 扫描读取块大小 `0x10000`，heap allocation 过滤范围、扫描总量上限见常量表。这些扫描大小是程序策略，不是需要同事重新寻找的字段偏移。
- **Rect 实验路径**：`ROADHOG_INVENTORY_ROOT_WIDGET_RECT_OFFSET` 指定相对 root widget 的偏移；未设置时在 `0x800` 字节范围以 8 字节步长找四个连续 double。默认路径仍为 DlgInventory 自身 `+0x58`。
- **DEBUG 探针**：位置扫描读取 CEntity 前 `0x1000` 字节，以 4 字节步长找 float3，最多展示 16 个候选；虚函数探针读取函数前 48 字节；代码地址探针读取前 8 字节。这些是探针长度，不是额外的业务成员偏移。
- **字符串**：Actor 名字最多读 64 个 UTF-16 字符，队员名字最多 26 个；SkillItem/InventoryItem 使用 MSVC wstring，长度和容量均 uint64，容量小于 8 时使用内联字符区，其他情况解引用字符指针。字符步长 2 字节。
- **镜头**：`AION_CAMERA_PITCH_RVA`、`AION_CAMERA_ROLL_RVA`、`AION_CAMERA_YAW_RVA` 可以覆盖默认 RVA，支持十进制或 `0x` 十六进制。`AION_CAMERA_PITCH_UNIT` / `AION_CAMERA_YAW_UNIT` 默认 `deg`，也接受 `rad/radian/radians` 与 `auto`。CEntity 角色 yaw 直接按角度归一化，不受这两个单位环境变量控制。这里列的是源码默认值，未审计桌面各脚本进程的环境覆盖。
- **连续读取约束**：频道 index/count 当前假定紧邻、步长 4，一次读 8 字节；战术标记一次读 `16 * 4 = 64` 字节。若更新后字段不再连续，仅换顶部 RVA 常量仍不足以修复。
- **指针校验**：现有代码把非零且不大于 `0x00007FFFFFFFFFFF` 的地址作为用户态指针候选；这是适配器校验规则，不是游戏模块 RVA。

## 静态文件与语义也要保持对应

当前职业枚举为：0 战士、1 剑星、2 守护星、3 侦察者、4 杀星、5 弓星、6 法师、7 魔道星、8 精灵星、9 祭司、10 治愈星、11 护法星；`AionClassCatalog` 目前只接受 0 到 11。Actor 职业读 uint32，队伍记录职业读 uint8。当前坐地板判定为 `(StanceFlags & 0xF) == 5 && MotionMode == 1`。这两组枚举/掩码分别见 `Core/Model/AionClassId.cs` 和 `Core/Model/PlayerSnapshot.cs`，也应确认更新后未变。

技能/NPC/异常/采集资源的部分含义来自本地 XML 或其他静态目录，不是额外的内存地址。新版本如果更改 ID 或结构，需要同步提供对应版本静态表；重点是技能 ID/名字/连续技及激活条件、NPC 模板与主动怪分类、AbnormalId 类型、GatherSourceId 资源信息，以及物品品质等级映射。

原始字段不能只按名字猜语义：队伍 PartySlot 不直接等于 UI/F2-F6 排序，队伍缓存坐标不等于 live CEntity 实时坐标，异常 category/bucket 不单独等于有害异常，Actor + 0x500 仍是其他玩家采集资源 ID 候选，采集物 + 0x40 是复用的原始字节。请同事在给出新偏移时一并确认这些语义。

## 范围和完整性核对

本清单覆盖当前 Roadhog 游戏内存适配器的全部游戏相关 `private const` 声明，以及读取函数中的内联成员偏移、派生字段、指针数组步长和动态解析规则。`VmmConfig*`、读缓存/重连参数属于 VMM 库配置；HID 键码、鼠标移动像素偏移、UI 固定点击坐标属于输入行为；均不是向同事索取的游戏内存偏移。

`Tool/Program.cs` 是独立探针程序。本次同步了对比表中能明确对应的 32 个共享字段声明，并把掷点窗口首槽改为 `RollDialogTableRva + PartyRollDialogFirstId * 8`（当前 `0xD64100`），避免独立硬编码随全局表更新而遗漏。队伍与战术标记探针中的重复 RVA、频道偏移测试也已同步。

Tool 专有的静态技能解析缓存、特殊镜头距离、额外物品字段等未列入同事的 234 项数据，不能从本次报告推导其新值，也不参与本次 Roadhog 游戏 API 验证。拍卖行附表已保存在同事原始 Markdown 内；当前项目未找到对应拍卖行实现。

本次构建输出位于工作区 `.tmp/offset-update-20260909`。构建的自动复制目标已重定向到此临时目录，没有覆盖桌面其他账号；账号 4 实机验证使用新构建的只读探针。源码、实机验证、桌面部署、提交/推送状态见验证记录。
