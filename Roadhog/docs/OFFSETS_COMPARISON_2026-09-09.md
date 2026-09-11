# Roadhog 游戏更新偏移对比结果（2026-09-09）

> 基线：同事当前 Roadhog 偏移清单（2026-07-22 基线）  
> 新分析：`aion202609091125.json`（由更新后 `Game.dll` 的 IDA 导出）  
> 结论性质：**静态比对结果**。没有读取正在运行的游戏，因此“运行时值域/窗口状态/动态扫描命中率”仍建议同事在新版本实机做一次快速验证。

## 版本/导出信息

| 项目 | 旧基线 | 新分析 |
|---|---|---|
| IDA 导出文件 | `ida_analysis_export_20260722.json` | `aion202609091125.json` |
| ImageBase | `0x180000000` | `0x180000000` |
| function_count | `25849` | `25621` |
| PE 文件版本 / timestamp / SizeOfImage / SHA-256 | 旧文档有历史版本说明 | **新 JSON 未包含这些 PE 元数据，不能从本次导出可靠填写** |

## 先看结论

- `EntitySystemPointerRva`：`0x94C7B0 → 0x94C7B0`，**未变**。
- 大多数 `0xD4xxxx ~ 0xD7xxxx` 全局数据：本次表现为 **RVA - 0x10**，但这是针对已逐项比对的这些全局，**不要把 -0x10 套到对象成员偏移上**。
- `DlgInventoryDialog27MethodRva` / `28MethodRva`：代码地址分别 `+0x130`，不是 `-0x10`。
- `CEntity / Actor / SkillItem / InventoryItem / PartyMemberRecord / Gauge` 等**对象内成员偏移未发现整体变化**；表内仍保留旧值并标注“静态结构比对未变”。
- 动态 Actor / Rect / VTable 扫描规则本身未改；其依赖的全局表或方法 RVA 已按新值更新。

## 关键变更项

| 数据项 | 旧值 | 新值 | 变化 |
|---|---:|---:|---|
| `EntitySystemPointerRva` | `0x94C7B0` | `0x94C7B0` | 未变 |
| `ServerObjectTreeRva` | `0xD6CAC0` | `0xD6CAB0` | 变更 -0x10 |
| `PartyIdRva` | `0xD66930` | `0xD66920` | 变更 -0x10 |
| `PartyFlagsRva` | `0xD66934` | `0xD66924` | 变更 -0x10 |
| `PartyLeaderServerObjectIdRva` | `0xD66938` | `0xD66928` | 变更 -0x10 |
| `PrimaryPartyListRva` | `0xD66960` | `0xD66950` | 变更 -0x10 |
| `PrimaryPartyCountRva` | `0xD66968` | `0xD66958` | 变更 -0x10 |
| `SecondaryPartyListRva` | `0xD669C8` | `0xD669B8` | 变更 -0x10 |
| `TacticsSignTableRva` | `0xD668E0` | `0xD668D0` | 变更 -0x10 |
| `CurrentMapContextPointerRva` | `0xD647D0` | `0xD647C0` | 变更 -0x10 |
| `CurrentChannelIndexRva` | `0xD71CC0` | `0xD71CB0` | 变更 -0x10 |
| `CurrentChannelCountRva` | `0xD71CC4` | `0xD71CB4` | 变更 -0x10 |
| `LocalEntityIdRva` | `0xD6CB18` | `0xD6CB08` | 变更 -0x10 |
| `LocalMaxHpRva` | `0xD71BC4` | `0xD71BB4` | 变更 -0x10 |
| `LocalCurrentHpRva` | `0xD71BC8` | `0xD71BB8` | 变更 -0x10 |
| `LocalMaxMpRva` | `0xD71BCC` | `0xD71BBC` | 变更 -0x10 |
| `LocalCurrentMpRva` | `0xD71BD0` | `0xD71BC0` | 变更 -0x10 |
| `LocalCurrentDpRva` | `0xD71BD6` | `0xD71BC6` | 变更 -0x10 |
| `CameraPitchRva` | `0xD65B84` | `0xD65B74` | 变更 -0x10 |
| `CameraRollRva` | `0xD65B88` | `0xD65B78` | 变更 -0x10 |
| `CameraYawRva` | `0xD65B8C` | `0xD65B7C` | 变更 -0x10 |
| `SpecialCameraModeRva` | `0xD6CC48` | `0xD6CC38` | 变更 -0x10 |
| `SpecialCameraPitchRva` | `0xD6CC58` | `0xD6CC48` | 变更 -0x10 |
| `SpecialCameraRollRva` | `0xD6CC5C` | `0xD6CC4C` | 变更 -0x10 |
| `SpecialCameraYawRva` | `0xD6CC60` | `0xD6CC50` | 变更 -0x10 |
| `SkillManagerGlobalRva` | `0xD4B020` | `0xD4B010` | 变更 -0x10 |
| `CurrentGatherSourceIdRva` | `0xD68CE8` | `0xD68CD8` | 变更 -0x10 |
| `CurrentGatherTargetEntityRva` | `0xD68CF0` | `0xD68CE0` | 变更 -0x10 |
| `CurrentGatherSkillIdRva` | `0xD68CF8` | `0xD68CE8` | 变更 -0x10 |
| `DlgGatheringPointerRva` | `0xD63E38` | `0xD63E28` | 变更 -0x10 |
| `InventoryManagerGlobalRva` | `0xD4B020 = SkillManagerGlobalRva` | `0xD4B010 = SkillManagerGlobalRva` | 变更 -0x10 |
| `ItemStaticIndexRva` | `0xD75428` | `0xD75418` | 变更 -0x10 |
| `StaticResolverChunkListRva` | `0xD4E500` | `0xD4E4F0` | 变更 -0x10 |
| `DlgInventoryDialog27MethodRva` | `0x1C66F0` | `0x1C6820` | 变更 +0x130 |
| `DlgInventoryDialog28MethodRva` | `0x1CBFB0` | `0x1CC0E0` | 变更 +0x130 |
| `DlgInventoryDialogTableRva` | `0xD639A0` | `0xD63990` | 变更 -0x10 |
| `DlgInventoryDialog27PointerRva` | `0xD63A78 = DlgInventoryDialogTableRva + (27UL * 8UL)` | `0xD63A68 = DlgInventoryDialogTableRva + (27UL * 8UL)` | 变更 -0x10 |
| `DlgInventoryDialog28PointerRva` | `0xD63A80 = DlgInventoryDialogTableRva + (28UL * 8UL)` | `0xD63A70 = DlgInventoryDialogTableRva + (28UL * 8UL)` | 变更 -0x10 |

## 完整逐项对比

### 01 Game.dll 全局 RVA

| 数据项 | 旧值/规则 | 更新后 | 类型/布局 | 状态/说明 |
|---|---|---|---|---|
| `EntitySystemPointerRva` | `0x94C7B0` | `0x94C7B0` | ptr64 | **未变**；直接确认：大量对应函数引用仍为 0x18094C7B0。 |
| `ServerObjectTreeRva` | `0xD6CAC0` | `0xD6CAB0` | ptr64 | **变更 -0x10**；直接确认：旧 0xD6CAC0 → 新 0xD6CAB0。 |
| `PartyIdRva` | `0xD66930` | `0xD66920` | uint32 | **变更 -0x10**；直接确认：旧 0xD66930 → 新 0xD66920。 |
| `PartyFlagsRva` | `0xD66934` | `0xD66924` | uint32 | **变更 -0x10**；直接/邻接确认：与 PartyId 连续字段保持 +4。 |
| `PartyLeaderServerObjectIdRva` | `0xD66938` | `0xD66928` | uint32 | **变更 -0x10**；直接确认：旧 0xD66938 → 新 0xD66928。 |
| `PrimaryPartyListRva` | `0xD66960` | `0xD66950` | ptr64 | **变更 -0x10**；直接结构确认：旧 xmmword_180D66960 → 新 xmmword_180D66950。 |
| `PrimaryPartyCountRva` | `0xD66968` | `0xD66958` | uint64 | **变更 -0x10**；由 PrimaryPartyList 连续布局 +8 推导；需实机读 count 复核。 |
| `SecondaryPartyListRva` | `0xD669C8` | `0xD669B8` | ptr64 | **变更 -0x10**；直接确认：旧 0xD669C8 → 新 0xD669B8。 |
| `TacticsSignTableRva` | `0xD668E0` | `0xD668D0` | uint32[16] 数组基址 | **变更 -0x10**；直接确认：旧 0xD668E0 → 新 0xD668D0。 |
| `CurrentMapContextPointerRva` | `0xD647D0` | `0xD647C0` | ptr64 | **变更 -0x10**；直接确认：旧 0xD647D0 → 新 0xD647C0。 |
| `CurrentChannelIndexRva` | `0xD71CC0` | `0xD71CB0` | uint32 | **变更 -0x10**；由连续 index/count 8 字节块确认；count 新址为 0xD71CB4。 |
| `CurrentChannelCountRva` | `0xD71CC4` | `0xD71CB4` | uint32 | **变更 -0x10**；直接确认：旧 0xD71CC4 → 新 0xD71CB4。 |
| `LocalEntityIdRva` | `0xD6CB18` | `0xD6CB08` | uint16 | **变更 -0x10**；直接确认：旧 0xD6CB18 → 新 0xD6CB08。 |
| `LocalMaxHpRva` | `0xD71BC4` | `0xD71BB4` | uint32 | **变更 -0x10**；直接确认。 |
| `LocalCurrentHpRva` | `0xD71BC8` | `0xD71BB8` | uint32 | **变更 -0x10**；直接确认。 |
| `LocalMaxMpRva` | `0xD71BCC` | `0xD71BBC` | uint32 | **变更 -0x10**；直接确认。 |
| `LocalCurrentMpRva` | `0xD71BD0` | `0xD71BC0` | uint32 | **变更 -0x10**；直接确认。 |
| `LocalCurrentDpRva` | `0xD71BD6` | `0xD71BC6` | uint16 | **变更 -0x10**；直接对应引用可见：旧 0xD71BD6 → 新 0xD71BC6。 |
| `CameraPitchRva` | `0xD65B84` | `0xD65B74` | float32 | **变更 -0x10**；直接确认。 |
| `CameraRollRva` | `0xD65B88` | `0xD65B78` | float32 | **变更 -0x10**；直接对应引用可见：旧 0xD65B88 → 新 0xD65B78。 |
| `CameraYawRva` | `0xD65B8C` | `0xD65B7C` | float32 | **变更 -0x10**；直接确认。 |
| `SpecialCameraModeRva` | `0xD6CC48` | `0xD6CC38` | uint16 | **变更 -0x10**；直接确认。 |
| `SpecialCameraPitchRva` | `0xD6CC58` | `0xD6CC48` | float32 | **变更 -0x10**；与特殊镜头连续结构保持 +0x10。 |
| `SpecialCameraRollRva` | `0xD6CC5C` | `0xD6CC4C` | float32 | **变更 -0x10**；直接/连续结构确认。 |
| `SpecialCameraYawRva` | `0xD6CC60` | `0xD6CC50` | float32 | **变更 -0x10**；与特殊镜头连续结构保持 +0x18。 |
| `SkillManagerGlobalRva` | `0xD4B020` | `0xD4B010` | ptr64 | **变更 -0x10**；高置信直接确认：数百处对应引用从 0xD4B020 → 0xD4B010。 |
| `CurrentGatherSourceIdRva` | `0xD68CE8` | `0xD68CD8` | uint32 | **变更 -0x10**；直接引用计数对应：旧 0xD68CE8 → 新 0xD68CD8。 |
| `CurrentGatherTargetEntityRva` | `0xD68CF0` | `0xD68CE0` | uint64 原始指针值 | **变更 -0x10**；由 Source/Target/Skill 连续 8 字节布局推导；建议实机非零采集时复核。 |
| `CurrentGatherSkillIdRva` | `0xD68CF8` | `0xD68CE8` | uint32 | **变更 -0x10**；直接确认：旧 0xD68CF8 → 新 0xD68CE8。 |
| `DlgGatheringPointerRva` | `0xD63E38` | `0xD63E28` | uint64 原始指针值 | **变更 -0x10**；直接确认：旧 0xD63E38 → 新 0xD63E28。 |
| `InventoryManagerGlobalRva` | `0xD4B020 = SkillManagerGlobalRva` | `0xD4B010 = SkillManagerGlobalRva` | ptr64 | **变更 -0x10**；同 SkillManagerGlobalRva。 |
| `ItemStaticIndexRva` | `0xD75428` | `0xD75418` | 内嵌索引头 | **变更 -0x10**；高置信直接确认：旧 0xD75428 → 新 0xD75418。 |
| `StaticResolverChunkListRva` | `0xD4E500` | `0xD4E4F0` | ptr64 数组基址 | **变更 -0x10**；直接确认：旧 0xD4E500 → 新 0xD4E4F0。 |
| `DlgInventoryDialog27MethodRva` | `0x1C66F0` | `0x1C6820` | 代码地址 | **变更 +0x130**；函数体/大小对应：旧 0x1801C66F0 → 新 0x1801C6820。 |
| `DlgInventoryDialog28MethodRva` | `0x1CBFB0` | `0x1CC0E0` | 代码地址 | **变更 +0x130**；函数体/大小对应：旧 0x1801CBFB0 → 新 0x1801CC0E0。 |
| `DlgInventoryDialogTableRva` | `0xD639A0` | `0xD63990` | ptr64 数组基址 | **变更 -0x10**；高置信直接确认：大量 UI 表引用从 0xD639A0 → 0xD63990。 |
| `DlgInventoryDialog27PointerRva` | `0xD63A78 = DlgInventoryDialogTableRva + (27UL * 8UL)` | `0xD63A68 = DlgInventoryDialogTableRva + (27UL * 8UL)` | ptr64 | **变更 -0x10**；由新 DialogTable 直接推导；对应槽引用也直接匹配。 |
| `DlgInventoryDialog28PointerRva` | `0xD63A80 = DlgInventoryDialogTableRva + (28UL * 8UL)` | `0xD63A70 = DlgInventoryDialogTableRva + (28UL * 8UL)` | ptr64 | **变更 -0x10**；由新 DialogTable 直接推导；对应槽引用也直接匹配。 |

### 03 战术标记数组

| 数据项 | 旧值/规则 | 更新后 | 类型/布局 | 状态/说明 |
|---|---|---|---|---|
| `TacticsSignCount` | `16` | `16` | 数量 | **未变**；未发现与本次 Game.dll 更新相关的结构变化。 |

### 02 地图上下文

| 数据项 | 旧值/规则 | 更新后 | 类型/布局 | 状态/说明 |
|---|---|---|---|---|
| `CurrentMapIdOffset` | `0x20DC` | `0x20DC` | uint32 | **未变**；未发现与本次 Game.dll 更新相关的结构变化。 |

### 04 已学技能树

| 数据项 | 旧值/规则 | 更新后 | 类型/布局 | 状态/说明 |
|---|---|---|---|---|
| `LearnedSkillTreeOffset` | `0x830` | `0x830` | ptr64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `LearnedSkillOuterSkillIdOffset` | `0x20` | `0x20` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `LearnedSkillOuterLevelTreeHeaderOffset` | `0x28` | `0x28` | ptr64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `LearnedSkillOuterLevelTreeSizeOffset` | `0x30` | `0x30` | uint64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `LearnedSkillInnerLevelOffset` | `0x20` | `0x20` | uint16 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `LearnedSkillInnerItemListHeaderOffset` | `0x28` | `0x28` | ptr64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `LearnedSkillInnerItemListSizeOffset` | `0x30` | `0x30` | uint64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |

### 05 红黑树与链表节点

| 数据项 | 旧值/规则 | 更新后 | 类型/布局 | 状态/说明 |
|---|---|---|---|---|
| `NodeLeftOffset` | `0x0` | `0x0` | ptr64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `NodeParentOffset` | `0x8` | `0x8` | ptr64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `NodeRightOffset` | `0x10` | `0x10` | ptr64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `NodeIsNilOffset` | `0x19` | `0x19` | uint8 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `NodeIdOffset` | `0x20` | `0x20` | uint16 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `NodeEntityOffset` | `0x28` | `0x28` | ptr64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ListNodeNextOffset` | `0x0` | `0x0` | ptr64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ListNodePrevOffset` | `0x8` | `0x8` | ptr64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ListNodeValueOffset` | `0x10` | `0x10` | ptr64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ServerNodeServerObjectIdOffset` | `0x1C` | `0x1C` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ServerNodeEntityIdOffset` | `0x20` | `0x20` | uint16 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |

### 06 EntitySystem 与 CEntity

| 数据项 | 旧值/规则 | 更新后 | 类型/布局 | 状态/说明 |
|---|---|---|---|---|
| `EntityTreeOffset` | `0x58` | `0x58` | ptr64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `EntityTypeOffset` | `0x122` | `0x122` | uint16 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `EntityPositionFlagsOffset` | `0xF0` | `0xF0` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `EntityUseAlternatePositionFlag` | `0x400` | `0x400` | 掩码 | **未变（程序侧）**；Roadhog 侧策略/上限/环境变量，不是 Game.dll 地址。 |
| `EntityWorldPositionOffset` | `0x4E4` | `0x4E4` | float32[3] | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `EntityWorldAnglesOffset` | `0x518` | `0x518` | float32 块 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `EntityLocalPositionOffset` | `0x524` | `0x524` | float32[3] | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `EntityPositionVfuncOffset` | `0x8` | `0x8` | ptr64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `EntityProxyManagerVfuncOffset` | `0xB8` | `0xB8` | ptr64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `EntitySystemGetEntityVfuncOffset` | `0x30` | `0x30` | ptr64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `EntityTypeNpc` | `3` | `3` | 枚举值 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |

### 07 Actor 角色、怪物、宠物与采集物

| 数据项 | 旧值/规则 | 更新后 | 类型/布局 | 状态/说明 |
|---|---|---|---|---|
| `ActorEntityOffset` | `0x8` | `0x8` | ptr64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ActorObjectTypeOffset` | `0x20` | `0x20` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ActorPlayerObjectType` | `1` | `1` | 枚举值 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ActorServerObjectIdOffset` | `0x2C` | `0x2C` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ActorNpcTemplateIdOffset` | `0x30` | `0x30` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ActorStanceFlagsOffset` | `0x34` | `0x34` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ActorLevelOffset` | `0x3E` | `0x3E` | uint16 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ActorHpPercentOffset` | `0x40` | `0x40` | uint8 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ActorNameOffset` | `0x42` | `0x42` | UTF-16，最多 64 字符 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ActorSummonOwnerServerObjectIdOffset` | `0xFC` | `0xFC` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ActorGatherInteractionRadiusOffset` | `0x168` | `0x168` | float32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ActorGatherSpawnPositionOffset` | `0x19C` | `0x19C` | float32[3] | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ActorInteractionStateOffset` | `0x1CC` | `0x1CC` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ActorClassIdOffset` | `0x228` | `0x228` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ActorMotionModeOffset` | `0x2D0` | `0x2D0` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ActorTargetServerObjectIdOffset` | `0x358` | `0x358` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ActorGatherSourceIdCandidateOffset` | `0x500` | `0x500` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ActorGatherActionStateOffset` | `0xAB0` | `0xAB0` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ActorGatherActionIdOffset` | `0xAB4` | `0xAB4` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ActorCurrentSummonedPetServerObjectIdOffset` | `0xFA0` | `0xFA0` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ActorMaxHpOffset` | `0x11A0` | `0x11A0` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ActorCurrentHpOffset` | `0x11A4` | `0x11A4` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ActorLootableFlagOffset` | `0x11E0` | `0x11E0` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ActorGatherObjectType` | `7` | `7` | 枚举值 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |

### 08 采集窗口与 Gauge

| 数据项 | 旧值/规则 | 更新后 | 类型/布局 | 状态/说明 |
|---|---|---|---|---|
| `DlgGatheringFlagsOffset` | `0x28` | `0x28` | uint64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `DlgGatheringVisibleMask` | `0x1` | `0x1` | 掩码 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `DlgGatheringSuccessGaugeOffset` | `0x4E8` | `0x4E8` | ptr64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `DlgGatheringFailureGaugeOffset` | `0x500` | `0x500` | ptr64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `GatherGaugeMaximumOffset` | `0x300` | `0x300` | float64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `GatherGaugeDisplayedOffset` | `0x308` | `0x308` | float64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `GatherGaugeTargetOffset` | `0x310` | `0x310` | float64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |

### 09 本地异常状态数组

| 数据项 | 旧值/规则 | 更新后 | 类型/布局 | 状态/说明 |
|---|---|---|---|---|
| `ActorAbnormalStatusBeginOffset` | `0xF18` | `0xF18` | ptr64，可空 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ActorAbnormalStatusEndOffset` | `0xF20` | `0xF20` | ptr64，可空 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ActorAbnormalCategory2CountOffset` | `0xF38` | `0xF38` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `AbnormalStatusEntrySize` | `0x12` | `0x12` | 字节步长 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `MaxActorAbnormalStatusEntries` | `512` | `512` | 读取上限 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |

### 10 PartyMemberRecord 队伍成员

| 数据项 | 旧值/规则 | 更新后 | 类型/布局 | 状态/说明 |
|---|---|---|---|---|
| `PartyMemberPartySlotOffset` | `0x0` | `0x0` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `PartyMemberServerObjectIdOffset` | `0x4` | `0x4` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `PartyMemberMaxHpOffset` | `0x8` | `0x8` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `PartyMemberCurrentHpOffset` | `0xC` | `0xC` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `PartyMemberMaxMpOffset` | `0x10` | `0x10` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `PartyMemberCurrentMpOffset` | `0x14` | `0x14` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `PartyMemberMaxFlightTimeOffset` | `0x18` | `0x18` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `PartyMemberCurrentFlightTimeOffset` | `0x1C` | `0x1C` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `PartyMemberAreaField0Offset` | `0x20` | `0x20` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `PartyMemberAreaField1Offset` | `0x24` | `0x24` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `PartyMemberCachedXOffset` | `0x28` | `0x28` | float32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `PartyMemberCachedYOffset` | `0x2C` | `0x2C` | float32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `PartyMemberCachedZOffset` | `0x30` | `0x30` | float32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `PartyMemberClassIdOffset` | `0x34` | `0x34` | uint8 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `PartyMemberLevelOffset` | `0x36` | `0x36` | uint8 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `PartyMemberDataFlagsOffset` | `0x37` | `0x37` | uint8 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `PartyMemberFlightAreaFlagOffset` | `0x38` | `0x38` | uint8 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `PartyMemberFlightFlagsOffset` | `0x39` | `0x39` | uint8 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `PartyMemberRuntimeStateOffset` | `0x3A` | `0x3A` | uint8 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `PartyMemberNameOffset` | `0x3B` | `0x3B` | UTF-16，最多 26 字符 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `PartyMemberControlStatusMaskOffset` | `0x6F` | `0x6F` | uint64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `PartyMemberHasAbnormalBlockFlag` | `0x8` | `0x8` | 掩码 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `PartyMemberAbnormalCountOffset` | `0x77` | `0x77` | int16 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `PartyMemberAbnormalEntriesOffset` | `0x79` | `0x79` | 内嵌 entry 数组 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `PartyMemberUpdateTimeOffset` | `0x859` | `0x859` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `PartyMemberMaxAbnormalCount` | `112` | `112` | 读取上限 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |

### 11 SkillItem 技能对象

| 数据项 | 旧值/规则 | 更新后 | 类型/布局 | 状态/说明 |
|---|---|---|---|---|
| `SkillItemSkillIdOffset` | `0x8` | `0x8` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `SkillItemField0COffset` | `0xC` | `0xC` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `SkillItemRankValueOffset` | `0x10` | `0x10` | uint64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `SkillItemNameOffset` | `0x18` | `0x18` | MSVC wstring | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `SkillItemCooldownDurationOffset` | `0x50` | `0x50` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `SkillItemCooldownEndTimeOffset` | `0x54` | `0x54` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `SkillItemToggleStateOffset` | `0x60` | `0x60` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `SkillItemSkillLevelOffset` | `0x64` | `0x64` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `SkillItemStaticFieldD8Offset` | `0x68` | `0x68` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `SkillItemRuntimeStateOffset` | `0x6C` | `0x6C` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `SkillItemSourceFlagsOffset` | `0x74` | `0x74` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |

### 12 InventoryManager 背包管理器

| 数据项 | 旧值/规则 | 更新后 | 类型/布局 | 状态/说明 |
|---|---|---|---|---|
| `InventoryCurrentMoneyOffset` | `0x770` | `0x770` | uint64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `InventoryMoneyInstanceIdOffset` | `0x778` | `0x778` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `InventoryCapacityOffset` | `0x77C` | `0x77C` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `InventoryItemTreeHeaderOffset` | `0x780` | `0x780` | ptr64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `InventoryItemTreeCountOffset` | `0x788` | `0x788` | uint64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `InventoryEquipmentIdsOffset` | `0x790` | `0x790` | uint32[32] | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `InventoryEquipmentIdCount` | `32` | `32` | 数组数量 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `InventorySlotsPerPage` | `27` | `27` | 布局参数 | **未变（程序侧）**；Roadhog 侧策略/上限/环境变量，不是 Game.dll 地址。 |
| `InventoryColumnsPerPage` | `9` | `9` | 布局参数 | **未变（程序侧）**；Roadhog 侧策略/上限/环境变量，不是 Game.dll 地址。 |

### 13 背包物品节点与物品对象

| 数据项 | 旧值/规则 | 更新后 | 类型/布局 | 状态/说明 |
|---|---|---|---|---|
| `InventoryNodeInstanceIdOffset` | `0x20` | `0x20` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `InventoryNodeItemOffset` | `0x28` | `0x28` | ptr64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `InventoryItemInstanceIdOffset` | `0x8` | `0x8` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `InventoryItemTemplateIdOffset` | `0xC` | `0xC` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `InventoryItemCountOffset` | `0x10` | `0x10` | uint64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `InventoryItemNameOffset` | `0x18` | `0x18` | MSVC wstring | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `InventoryItemTypeOffset` | `0x60` | `0x60` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `InventoryItemEquipmentMaskOffset` | `0x74` | `0x74` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `InventoryItemVendorSellUnitPriceOffset` | `0x80` | `0x80` | uint64 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `InventoryItemSlotOffset` | `0x4F6` | `0x4F6` | int16 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |

### 14 物品静态索引与压缩块

| 数据项 | 旧值/规则 | 更新后 | 类型/布局 | 状态/说明 |
|---|---|---|---|---|
| `ItemStaticRecordIdOffset` | `0x0` | `0x0` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `ItemStaticRecordQualityRankOffset` | `0x1E1` | `0x1E1` | uint8 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `StaticResolverEntrySize` | `0x10` | `0x10` | 字节步长 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `StaticResolverPackedHandleOffset` | `0x8` | `0x8` | uint64，取低 32 位 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `StaticResolverPackedChunkShift` | `14` | `14` | 位移量 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `StaticResolverPackedOffsetMask` | `0x3FFF` | `0x3FFF` | 掩码 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `MaxStaticResolverEntries` | `2000000` | `2000000` | 参数/上限 | **未变（程序侧）**；Roadhog 侧策略/上限/环境变量，不是 Game.dll 地址。 |
| `MaxStaticChunkCompressedBytes` | `4194304` | `4194304` | 参数/上限 | **未变（程序侧）**；Roadhog 侧策略/上限/环境变量，不是 Game.dll 地址。 |
| `MaxStaticChunkUncompressedBytes` | `16777216` | `16777216` | 参数/上限 | **未变（程序侧）**；Roadhog 侧策略/上限/环境变量，不是 Game.dll 地址。 |

### 15 背包窗口与对象扫描

| 数据项 | 旧值/规则 | 更新后 | 类型/布局 | 状态/说明 |
|---|---|---|---|---|
| `DlgInventoryWidgetFlagsOffset` | `0x28` | `0x28` | uint64 | **未变**；未发现与本次 Game.dll 更新相关的结构变化。 |
| `DlgInventoryVisibleMask` | `0x1` | `0x1` | 掩码 | **未变**；未发现与本次 Game.dll 更新相关的结构变化。 |
| `DlgInventoryPageDirtyFlagBaseOffset` | `0x585` | `0x585` | uint8，每页一步 | **未变（程序侧）**；Roadhog 侧策略/上限/环境变量，不是 Game.dll 地址。 |
| `DlgInventoryWindowRectOffset` | `0x58` | `0x58` | float64[4] | **未变**；未发现与本次 Game.dll 更新相关的结构变化。 |
| `DlgInventoryRootWidgetOffset` | `0x4D8` | `0x4D8` | ptr64 | **未变**；未发现与本次 Game.dll 更新相关的结构变化。 |
| `DlgInventoryVtableBackSlots` | `256` | `256` | 参数/上限 | **未变（程序侧）**；Roadhog 侧策略/上限/环境变量，不是 Game.dll 地址。 |
| `RootWidgetRectScanBytes` | `0x800` | `0x800` | 参数/上限 | **未变（程序侧）**；Roadhog 侧策略/上限/环境变量，不是 Game.dll 地址。 |
| `RootWidgetRectScanStep` | `0x8` | `0x8` | 参数/上限 | **未变（程序侧）**；Roadhog 侧策略/上限/环境变量，不是 Game.dll 地址。 |
| `RootWidgetRectOffsetEnvironmentVariable` | `ROADHOG_INVENTORY_ROOT_WIDGET_RECT_OFFSET` | `ROADHOG_INVENTORY_ROOT_WIDGET_RECT_OFFSET` | 环境变量名 | **未变（程序侧）**；Roadhog 侧策略/上限/环境变量，不是 Game.dll 地址。 |
| `InventoryUiMinAllocationSize` | `0x400` | `0x400` | 参数/上限 | **未变（程序侧）**；Roadhog 侧策略/上限/环境变量，不是 Game.dll 地址。 |
| `InventoryUiMaxAllocationSize` | `0x3000` | `0x3000` | 参数/上限 | **未变（程序侧）**；Roadhog 侧策略/上限/环境变量，不是 Game.dll 地址。 |
| `InventoryUiVadScanBytes` | `1073741824` | `1073741824` | 参数/上限 | **未变（程序侧）**；Roadhog 侧策略/上限/环境变量，不是 Game.dll 地址。 |
| `InventoryUiObjectScanLimit` | `32` | `32` | 参数/上限 | **未变（程序侧）**；Roadhog 侧策略/上限/环境变量，不是 Game.dll 地址。 |

### 16 丢弃确认框

| 数据项 | 旧值/规则 | 更新后 | 类型/布局 | 状态/说明 |
|---|---|---|---|---|
| `DlgInventoryPendingDestroyItemIdOffset` | `0x598` | `0x598` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `FirstNormalDiscardDialogId` | `336` | `336` | DialogId | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `LastNormalDiscardDialogId` | `355` | `355` | DialogId | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `FirstSpecialDiscardDialogId` | `356` | `356` | DialogId | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `LastSpecialDiscardDialogId` | `365` | `365` | DialogId | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `DiscardMsgBoxTypeOffset` | `0x4D8` | `0x4D8` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `DiscardMsgBoxConfirmActionOffset` | `0x4DC` | `0x4DC` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `DiscardMsgBoxCancelActionOffset` | `0x4E0` | `0x4E0` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `DiscardMsgBoxItemInstanceIdOffset` | `0x4E8` | `0x4E8` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `DiscardMsgBoxItemInstanceId2Offset` | `0x508` | `0x508` | uint32 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `NormalDiscardMsgBoxType` | `2049` | `2049` | 枚举值 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `NormalDiscardConfirmAction` | `2101` | `2101` | 枚举值 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `NormalDiscardCancelAction` | `2104` | `2104` | 枚举值 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `SpecialDiscardMsgBoxType` | `3` | `3` | 枚举值 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `SpecialDiscardConfirmAction` | `2105` | `2105` | 枚举值 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |
| `SpecialDiscardCancelAction` | `2106` | `2106` | 枚举值 | **未变（静态结构比对）**；对象成员/节点/数组布局在旧新对应函数中未见整体位移；建议关键业务字段上线前做一次实机值域校验。 |

### 17 内联子偏移、数组布局与动态解析

| 数据项 | 旧值/规则 | 更新后 | 类型/布局 | 状态/说明 |
|---|---|---|---|---|
| `当前目标 EntityId` | `GameBase + 0xD6CB1A` | `GameBase + 0xD6CB0A` | uint16 | **派生地址已更新**；成员/数组规则未变，仅其 GameBase 全局基址按新构建更新。 |
| `世界坐标 Y` | `CEntity + 0x4E8` | `CEntity + 0x4E8` | float32 | **未变**；对象内/数组/ABI/运行时解析规则；静态结构比对未发现需要改动。 |
| `世界坐标 Z` | `CEntity + 0x4EC` | `CEntity + 0x4EC` | float32 | **未变**；对象内/数组/ABI/运行时解析规则；静态结构比对未发现需要改动。 |
| `角色朝向 yaw` | `CEntity + 0x520` | `CEntity + 0x520` | float32 | **未变**；对象内/数组/ABI/运行时解析规则；静态结构比对未发现需要改动。 |
| `诊断 local 坐标 Y` | `CEntity + 0x528` | `CEntity + 0x528` | float32 | **未变**；对象内/数组/ABI/运行时解析规则；静态结构比对未发现需要改动。 |
| `诊断 local 坐标 Z` | `CEntity + 0x52C` | `CEntity + 0x52C` | float32 | **未变**；对象内/数组/ABI/运行时解析规则；静态结构比对未发现需要改动。 |
| `采集出生坐标 Y` | `Actor + 0x1A0` | `Actor + 0x1A0` | float32 | **未变**；对象内/数组/ABI/运行时解析规则；静态结构比对未发现需要改动。 |
| `采集出生坐标 Z` | `Actor + 0x1A4` | `Actor + 0x1A4` | float32 | **未变**；对象内/数组/ABI/运行时解析规则；静态结构比对未发现需要改动。 |
| `异常 entry Field00` | `entry + 0x00` | `entry + 0x00` | uint32 | **未变**；对象内/数组/ABI/运行时解析规则；静态结构比对未发现需要改动。 |
| `异常 entry AbnormalId` | `entry + 0x04` | `entry + 0x04` | uint32 | **未变**；对象内/数组/ABI/运行时解析规则；静态结构比对未发现需要改动。 |
| `异常 entry Category` | `entry + 0x08` | `entry + 0x08` | uint32 | **未变**；对象内/数组/ABI/运行时解析规则；静态结构比对未发现需要改动。 |
| `异常 entry TimeOrSource` | `entry + 0x0C` | `entry + 0x0C` | uint32 | **未变**；对象内/数组/ABI/运行时解析规则；静态结构比对未发现需要改动。 |
| `异常 entry LevelOrStack` | `entry + 0x10` | `entry + 0x10` | uint16 | **未变**；对象内/数组/ABI/运行时解析规则；静态结构比对未发现需要改动。 |
| `MSVC wstring 字符存储` | `stringObject + 0x00` | `stringObject + 0x00` | 16 字节内联区或 ptr64 | **未变**；对象内/数组/ABI/运行时解析规则；静态结构比对未发现需要改动。 |
| `MSVC wstring 长度` | `stringObject + 0x10` | `stringObject + 0x10` | uint64 | **未变**；对象内/数组/ABI/运行时解析规则；静态结构比对未发现需要改动。 |
| `MSVC wstring 容量` | `stringObject + 0x18` | `stringObject + 0x18` | uint64 | **未变**；对象内/数组/ABI/运行时解析规则；静态结构比对未发现需要改动。 |
| `物品静态索引数量` | `GameBase + 0xD7542C` | `GameBase + 0xD7541C` | uint32 | **派生地址已更新**；成员/数组规则未变，仅其 GameBase 全局基址按新构建更新。 |
| `物品静态索引 entries` | `GameBase + 0xD75438` | `GameBase + 0xD75428` | ptr64 | **派生地址已更新**；成员/数组规则未变，仅其 GameBase 全局基址按新构建更新。 |
| `物品静态索引 key` | `entry + 0x00` | `entry + 0x00` | uint32 | **未变**；对象内/数组/ABI/运行时解析规则；静态结构比对未发现需要改动。 |
| `压缩块 compressedSize` | `chunkPointer + 0x00` | `chunkPointer + 0x00` | uint32 | **未变**；对象内/数组/ABI/运行时解析规则；静态结构比对未发现需要改动。 |
| `压缩块 uncompressedSize` | `chunkPointer + 0x04` | `chunkPointer + 0x04` | uint32 | **未变**；对象内/数组/ABI/运行时解析规则；静态结构比对未发现需要改动。 |
| `压缩块数据` | `chunkPointer + 0x08` | `chunkPointer + 0x08` | byte[compressedSize] | **未变**；对象内/数组/ABI/运行时解析规则；静态结构比对未发现需要改动。 |
| `背包 Rect X` | `DlgInventory + 0x58` | `DlgInventory + 0x58` | float64 | **未变**；对象内/数组/ABI/运行时解析规则；静态结构比对未发现需要改动。 |
| `背包 Rect Y` | `DlgInventory + 0x60` | `DlgInventory + 0x60` | float64 | **未变**；对象内/数组/ABI/运行时解析规则；静态结构比对未发现需要改动。 |
| `背包 Rect 宽` | `DlgInventory + 0x68` | `DlgInventory + 0x68` | float64 | **未变**；对象内/数组/ABI/运行时解析规则；静态结构比对未发现需要改动。 |
| `背包 Rect 高` | `DlgInventory + 0x70` | `DlgInventory + 0x70` | float64 | **未变**；对象内/数组/ABI/运行时解析规则；静态结构比对未发现需要改动。 |
| `战术标记槽地址` | `GameBase + 0xD668E0 + i * 4` | `GameBase + 0xD668D0 + i * 4` | uint32[16] | **派生地址已更新**；成员/数组规则未变，仅其 GameBase 全局基址按新构建更新。 |
| `已装备物品槽地址` | `InventoryManager + 0x790 + i * 4` | `InventoryManager + 0x790 + i * 4` | uint32[32] | **未变**；对象内/数组/ABI/运行时解析规则；静态结构比对未发现需要改动。 |
| `UI 对话框槽地址` | `GameBase + 0xD639A0 + dialogId * 8` | `GameBase + 0xD63990 + dialogId * 8` | ptr64 | **派生地址已更新**；成员/数组规则未变，仅其 GameBase 全局基址按新构建更新。 |
| `静态压缩块槽地址` | `GameBase + 0xD4E500 + chunkIndex * 8` | `GameBase + 0xD4E4F0 + chunkIndex * 8` | ptr64 | **派生地址已更新**；成员/数组规则未变，仅其 GameBase 全局基址按新构建更新。 |
| `对象虚表指针` | `CEntity / EntitySystem / DlgInventory + 0x00` | `CEntity / EntitySystem / DlgInventory + 0x00` | ptr64 | **未变**；对象内/数组/ABI/运行时解析规则；静态结构比对未发现需要改动。 |
| `动态 CEntity 到 proxy manager 成员偏移` | `从 [CEntity.vtable + 0xB8] 的函数体推导` | `从 [CEntity.vtable + 0xB8] 的函数体推导` | uint32 disp32 或 uint8 disp8 | **未变**；对象内/数组/ABI/运行时解析规则；静态结构比对未发现需要改动。 |
| `动态 proxy manager 到 Actor 指针位置` | `扫描 proxyManager 的前 0x400 字节` | `扫描 proxyManager 的前 0x400 字节` | ptr64，步长 8 | **未变**；对象内/数组/ABI/运行时解析规则；静态结构比对未发现需要改动。 |
| `动态 root widget Rect 偏移` | `[DlgInventory + 0x4D8] + 运行时解析偏移` | `[DlgInventory + 0x4D8] + 运行时解析偏移` | float64[4] | **未变**；对象内/数组/ABI/运行时解析规则；静态结构比对未发现需要改动。 |
| `动态 DlgInventory 虚表与对象定位` | `GameBase + 方法 RVA 的引用槽反向扫描` | `GameBase + 方法 RVA 的引用槽反向扫描` | ptr64 | **规则未变；方法 RVA 已更新**；反向扫描规则不变；Dialog27/28 方法 RVA 改为 0x1C6820 / 0x1CC0E0。 |

## 更新后的关键指针链

```text
EntitySystem = ptr64(GameBase + 0x94C7B0)
EntityTreeHeader = ptr64(EntitySystem + 0x58)
LocalEntityId = u16(GameBase + 0xD6CB08)
TargetEntityId = u16(GameBase + 0xD6CB0A)
EntityTreeNode.key = u16(node + 0x20)
CEntity = ptr64(EntityTreeNode + 0x28)

ServerTreeHeader = ptr64(GameBase + 0xD6CAB0)
ServerObjectId = u32(ServerTreeNode + 0x1C)
EntityId = u16(ServerTreeNode + 0x20)

PartyHead = ptr64(GameBase + 0xD66950 或 0xD669B8)
PartyNode = ptr64(PartyHead + 0x00)
PartyMemberRecord = ptr64(PartyNode + 0x10)

SkillManager = ptr64(GameBase + 0xD4B010)
SkillOuterHeader = ptr64(SkillManager + 0x830)

InventoryManager = ptr64(GameBase + 0xD4B010)
InventoryTreeHeader = ptr64(InventoryManager + 0x780)
InventoryItem = ptr64(InventoryNode + 0x28)

StaticIndex = GameBase + 0xD75418
StaticEntries = ptr64(StaticIndex + 0x10)
Chunk = ptr64(GameBase + 0xD4E4F0 + chunkIndex * 8)

GatherDialog = ptr64(GameBase + 0xD63E28)
SuccessGauge = ptr64(GatherDialog + 0x4E8)
FailureGauge = ptr64(GatherDialog + 0x500)

InventoryDialog27 = ptr64(GameBase + 0xD63990 + 27 * 8)  // = GameBase + 0xD63A68 slot
InventoryDialog28 = ptr64(GameBase + 0xD63990 + 28 * 8)  // = GameBase + 0xD63A70 slot
```

## 拍卖行补充（基于本次更新前后同函数比对）

这部分不在同事原始 Roadhog 清单中，但你前面正在使用，因此单独列出来：

| 项目 | 旧值 | 新值 | 说明 |
|---|---:|---:|---|
| GameData / SkillManager 根 | `0xD4B020` | `0xD4B010` | 全局 RVA -0x10 |
| DlgVendor 指针槽（DialogId 152） | `0xD63E60` | `0xD63E50` | Dialog 表 0xD639A0→0xD63990，因此槽同步 -0x10 |
| DlgVendor ctor | `0x2E2610` | `0x2E27A0` | 代码 RVA +0x190；归一化函数体一致 |
| DlgVendor UI init | `0x2E29A0` | `0x2E2B30` | +0x190；函数大小一致 |
| 分页状态处理 | `0x2E4730` | `0x2E48C0` | +0x190；归一化函数体一致 |
| VendorItem 加入 List | `0x2E48B0` | `0x2E4A40` | +0x190；大小一致，局部代码有轻微差异 |
| 按物品过滤搜索 | `0x2E8420` | `0x2E85B0` | +0x190；归一化函数体一致 |
| 拍卖分页 | `0x2E8C40` | `0x2E8DD0` | +0x190；归一化函数体一致 |
| 普通搜索页请求 | `0x357A70` | `0x357C00` | +0x190；大小一致 |
| VendorItem 创建 | `0x3B6380` | `0x3B6510` | +0x190；归一化函数体一致 |
| itemId 过滤搜索请求 | `0x422770` | `0x422900` | +0x190；大小一致 |
| 公开拍卖搜索结果解析 | `0x43BB00` | `0x43BC90` | +0x190；归一化函数体一致 |
| Dialog 全局指针表登记 | `0x309220` | `0x3093B0` | +0x190；归一化函数体一致 |

拍卖对象内成员偏移在这些对应函数中没有出现整体位移，因此目前仍按：`DlgVendor + 0x4F4/0x4FC/0x500/0x504/0x508/0x518`、List `+0x368/+0x370`、Row `+0xA0/+0xA8/+0xB0`、VendorItem `+0x08/+0x0C/+0x10/+0x18/+0x80/+0x560/+0x5A0` 使用。建议同事实机搜索一次物品，用 listingId/itemId/quantity/price/sellerName 做交叉校验。

## 建议同事实机优先验证的 8 项

1. `LocalEntityIdRva = 0xD6CB08` 与 `TargetEntityId = 0xD6CB0A`。
2. HP/MP 连续块 `0xD71BB4 ~ 0xD71BC0` 以及 DP `0xD71BC6`。
3. `Skill/InventoryManager = [GameBase + 0xD4B010]`。
4. `InventoryTreeHeader = Manager + 0x780`，确认背包节点数量和物品 instanceId。
5. `PartyHead = [GameBase + 0xD66950]` / `0xD669B8`，尤其 `PrimaryPartyCountRva 0xD66958`。
6. `GatherSource/Target/Skill = 0xD68CD8 / 0xD68CE0 / 0xD68CE8`。
7. `DlgInventoryDialogTableRva = 0xD63990`，27/28 槽是否能拿到有效窗口对象。
8. 动态 `CEntity.vtable + 0xB8` getter 是否仍以当前两种 MOV 指令模式开头；静态规则未变，但这是动态解析最容易因编译器改动失效的一项。

## 注意

- **不要**对所有数值统一做 `-0x10`。`EntitySystemPointerRva` 没变；代码 RVA 甚至是 `+0x130` /（拍卖相关）`+0x190`；对象成员偏移则目前保持原值。
- 新 IDA JSON 没有 PE 文件版本、timestamp、SizeOfImage、SHA-256；如果同事需要严格锁构建，还需要从实际更新后的 `Game.dll` 额外取这些元数据。
- 本报告没有进行运行时内存读取；标记“直接确认”指旧/新 IDA 函数/引用的静态对应确认。