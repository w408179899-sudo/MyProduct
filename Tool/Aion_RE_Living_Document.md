
# 客户端逆向分析总文档（当前版本）

> 适用范围：本文件汇总本次对 `Game.dll / EntitySystem` 相关 IDA 导出、局部伪代码和运行时验证结果的分析。所有 RVA / 偏移只适用于当前客户端版本。客户端更新后必须重新验证。
>
> 约定：文中的 `GameBase` 表示运行时 `Game.dll` 实际模块基址；`运行时地址 = GameBase + RVA`。IDA 里的 `0x180...` 不是固定运行时地址。
>
> 用途：离线、私服或明确授权测试环境中的只读分析。本文不包含绕过反作弊、修改内存、自动攻击、自动出售或发包实现。

---

## 0. 最重要修正

之前把 `CEntity + 0x4E8` 误判为坐标，运行时测试后已修正：

```text
CEntity + 0x4B4 = 世界坐标 X
CEntity + 0x4B8 = 世界坐标 Y
CEntity + 0x4BC = 世界坐标 Z

CEntity + 0x4E8 = 身体角度 Pitch
CEntity + 0x4EC = 身体角度 Roll
CEntity + 0x4F0 = 身体角度 Yaw
```

验证特征：角色不移动只转身时，`+0x4F0` 变化；移动时，`+0x4B4/+0x4B8/+0x4BC` 变化。

---

## 1. 全局基址与核心全局变量

| 名称 | RVA / 偏移 | 类型 | 说明 |
|---|---:|---|---|
| `g_EntitySystem` | `GameBase + 0x904690` | `CEntitySystem*` | CryEngine EntitySystem 全局指针 |
| `g_PlayerDataManager` | `GameBase + 0xD004A0` | manager pointer | 技能、背包等玩家数据管理入口；按调用上下文命名 |
| 本地玩家 Entity ID | `GameBase + 0xD21798` | `uint16` | `dword_180D21798` 低 16 位 |
| 当前锁定目标 Entity ID | `GameBase + 0xD2179A` | `uint16` | `dword_180D21798` 高 16 位；0 表示无目标 |
| ServerObject → EntityId 映射树 | `GameBase + 0xD21740` | RB tree header | 用 Server Object ID 查 Entity ID |
| 当前 HP 全局 | `GameBase + 0xD267E0` | `uint32` | 本地玩家当前 HP |
| 最大 HP 全局 | `GameBase + 0xD267DC` | `uint32` | 本地玩家最大 HP |
| 当前 MP 全局 | `GameBase + 0xD267E8` | `uint32` | 本地玩家当前 MP |
| 最大 MP 全局 | `GameBase + 0xD267E4` | `uint32` | 本地玩家最大 MP |
| 当前 DP 全局 | `GameBase + 0xD267EE` | `uint16` | 本地玩家当前 DP |
| 相机 Pitch | `GameBase + 0xD1AD14` | `float` | 俯仰角，度 |
| 相机 Roll | `GameBase + 0xD1AD18` | `float` | 翻滚角，通常接近 0 |
| 相机 Yaw | `GameBase + 0xD1AD1C` | `float` | 水平朝向，度 |
| Party 主列表 | `GameBase + 0xD1BAE8` | list header | 队伍成员快照列表 |

---

## 2. CEntity / EntitySystem

### 2.1 EntitySystem 红黑树

```cpp
struct EntityTreeNode {
    EntityTreeNode* left;     // +0x00
    EntityTreeNode* parent;   // +0x08
    EntityTreeNode* right;    // +0x10
    uint8_t color;            // +0x18
    uint8_t isNil;            // +0x19
    uint8_t padding[6];
    uint16_t entityId;        // +0x20
    uint8_t padding2[6];
    CEntity* entity;          // +0x28
};
```

```text
CEntitySystem + 0x58 = entity map header
node + 0x20 = Entity ID
node + 0x28 = CEntity*
```

重要函数：

| RVA | 建议名 | 说明 |
|---:|---|---|
| `0x001200` | `CreateEntitySystem` | 创建 EntitySystem |
| `0x03B620` | `CEntitySystem_ctor` | 构造 EntitySystem，注册 CVar |
| `0x03C4E0` | `CEntitySystem_Init` | 初始化 EntitySystem |
| `0x03CE50` | `CEntitySystem_SpawnEntity` | 创建实体 |
| `0x03B230` | `CEntitySystem_DeleteEntity` | 删除实体 |
| `0x03D580` | `CEntitySystem_GetEntityById` | 通过 Entity ID 查 `CEntity*` |
| `0x03DC20` | `CEntitySystem_CreateEntityIterator` | 创建实体迭代器 |
| `0x03DD40` | `CEntitySystem_QueryEntitiesAroundPoint` | 引擎内置范围查询，进程内可用 |
| `0x03F4D0` | `EntitySystem_DebugDumpEntities` | 调试输出实体数量、ID、位置等 |

### 2.2 CEntity 关键偏移

| 偏移 | 类型 | 说明 |
|---:|---|---|
| `+0x00` | vtable | `CEntity::vftable` |
| `+0xC0` | `uint32` | Entity flags |
| `+0xC4` | `uint32` | 扩展 flags |
| `+0xD0` | `int32` | 可能是部分实体 ID/状态字段，初始化 `-1` |
| `+0xF0` | `uint16` | CEntity 内部 Entity ID，验证指针用 |
| `+0xF2` | `uint16` | Entity 大类。统计逻辑中 `3` 常用于 NPC 类 Entity |
| `+0xF8` | string | Entity 名称，默认 `No entity loaded` |
| `+0x4B4` | `float` | 世界坐标 X |
| `+0x4B8` | `float` | 世界坐标 Y |
| `+0x4BC` | `float` | 世界坐标 Z |
| `+0x4E8` | `float` | 身体 Pitch |
| `+0x4EC` | `float` | 身体 Roll |
| `+0x4F0` | `float` | 身体 Yaw |
| `+0x4F4` | `float` | local/pending X |
| `+0x4F8` | `float` | local/pending Y |
| `+0x4FC` | `float` | local/pending Z |
| `+0x500` | `float` | local angle Pitch |
| `+0x504` | `float` | local angle Roll |
| `+0x508` | `float` | local angle Yaw |

建议重命名：

```cpp
sub_1800014D0 -> CEntity_ctor
sub_180006F30 -> CEntity_SetPosition
sub_180006FB0 -> CEntity_GetPositionPtr
sub_180007250 -> CEntity_SetAngles
sub_1800073F0 -> CEntity_GetAnglesPtr
sub_180008A60 -> CEntity_SetHidden
sub_18049B8A0 -> CEntity_GetCharacterObject
sub_18045A680 -> CEntity_GetGameObject
```

---

## 3. 当前玩家信息

### 3.1 全局 HP / MP / DP

本地玩家的基础血蓝直接读全局更方便：

```cpp
uint32_t maxHp     = *(uint32_t*)(GameBase + 0xD267DC);
uint32_t currentHp = *(uint32_t*)(GameBase + 0xD267E0);
uint32_t maxMp     = *(uint32_t*)(GameBase + 0xD267E4);
uint32_t currentMp = *(uint32_t*)(GameBase + 0xD267E8);
uint16_t currentDp = *(uint16_t*)(GameBase + 0xD267EE);
```

对应包：

| 包名 | ID | 说明 |
|---|---:|---|
| `S_HIT_POINT` | `3` | 更新本地 HP / MaxHP |
| `S_MANA_POINT` | `4` | 更新本地 MP / MaxMP |
| `S_DP` | `6` | 更新 DP |

### 3.2 当前玩家坐标

流程：

```text
GameBase + 0x904690 -> CEntitySystem*
GameBase + 0xD21798 -> localEntityId
CEntitySystem + 0x58 -> entity tree
entity tree 查 localEntityId -> CEntity*
CEntity + 0x4B4/+0x4B8/+0x4BC -> X/Y/Z
```

### 3.3 Actor / CharacterObject 常用字段

| Actor 偏移 | 类型 | 说明 |
|---:|---|---|
| `+0x08` | `CEntity*` | 对应引擎实体 |
| `+0x20` | `uint32` | objectType。`1=玩家`，`2=NPC/怪物`，`7=采集物` 候选/已确认场景 |
| `+0x2C` | `uint32` | Server Object ID |
| `+0x30` | `uint32` | NPC Template ID / 模板 ID |
| `+0x3E` | `uint16` | 等级 |
| `+0x40` | `uint8` | HP 百分比 |
| `+0x42` | UTF-16 | 名称，约 64 wchar |
| `+0x358` | `uint32` | 当前目标 Server Object ID |
| `+0xF18` | ptr | 异常状态数组 begin |
| `+0xF20` | ptr | 异常状态数组 end |
| `+0xF28` | ptr | 异常状态数组 capacity |
| `+0xF38` | `uint32` | 肉体异常 debuffphy 数量 |
| `+0xF3C` | `uint32` | 精神异常 debuffmen 数量 |
| `+0x11A0` | `uint32` | 最大 HP |
| `+0x11A4` | `uint32` | 当前 HP |

---

## 4. 相机视角

| 信息 | RVA | 类型 |
|---|---:|---|
| Pitch 俯仰角 | `0xD1AD14` | `float`，度 |
| Roll 翻滚角 | `0xD1AD18` | `float`，度 |
| Yaw 水平朝向 | `0xD1AD1C` | `float`，度 |
| Override flag | `0xD1AD38` | 临时覆盖标记 |
| Override pitch/roll/yaw | `0xD1AD20/24/28` | 覆盖值 |

建议：持续读取 `D1AD14/18/1C` 即可。相关函数：

```cpp
sub_180030340 -> Player_UpdateCamera
```

---

## 5. 当前锁定目标

```cpp
uint16_t targetEntityId = *(uint16_t*)(GameBase + 0xD2179A);
```

`0` 表示无目标。锁定对象获取流程：

```text
targetEntityId
  -> EntitySystem::GetEntityById
  -> CEntity*
  -> sub_18049B8A0(CEntity*)
  -> Actor* / CharacterObject*
```

重要函数：

| RVA | 建议名 | 说明 |
|---:|---|---|
| `0x3268A0` | `SetCurrentTarget` | 设置/取消当前目标 |
| `0x326560` | `GetCurrentTargetServerObjectId` | 返回当前目标 Server ID |
| `0x326670` | `GetCurrentTargetGameObject` | 返回当前目标 GameObject |
| `0x326700` | `GetCurrentTargetCharacter` | 返回当前目标 Actor |
| `0x498FE0` | `Actor_SetTargetServerObjectId` | 写 Actor +0x358 |
| `0x046210` | `TrySelectTarget` | 选目标逻辑入口之一 |

判断当前锁定的是怪物：

```cpp
Actor* target = GetCurrentTargetCharacter();
bool lockedMonster = target && *(uint32_t*)(target + 0x20) == 2;
```

判断当前锁定的是采集物：

```cpp
bool lockedGather = target && *(uint32_t*)(target + 0x20) == 7;
```

---

## 6. 怪物 / NPC

### 6.1 静态 NPC XML 解析

```cpp
sub_1804272A0 -> ParseNpcXmlDataNode
sub_180429A90 -> LoadNpcXmlAndMeshTables
sub_180029580 -> LoadClientNpcsXml
sub_1804330B0 -> LoadAiCharProperties
```

重要 XML / 资源：

```text
NPC\npc.xml
data\NPCs\client_npcs.xml
Data/Npcs/AiCharProperties.xml
npc_mesh_scale.txt
npc_mesh_replace.txt
npc_tribe_relation.xml
npcfactions.xml
```

`ParseNpcXmlDataNode` 解析的静态字段包括：

| XML 字段 | 结构偏移 | 说明 |
|---|---:|---|
| `name` | `NpcInfo + 0x08` | 名称句柄 |
| `ai_name` | `+0x90` | AI 名称 |
| `quest_ai_name` | `+0x98` | 任务 AI 名称 |
| `scale` | `+0x38` | 模型缩放 |
| `attack_range` | `+0x288` | 攻击距离 |
| `sensory_range` | `+0x128` | 感知范围，int |
| `visible_range` | `+0x224` | 可视范围 |
| `hpgauge_level` | `+0x88` | 血条显示等级，不是 HP 数值 |
| `npc_type` | `+0x26C` | NPC/怪物类型 |
| `abyss_npc_type` | `+0x268` | 深渊/特殊 NPC 类型 |
| `tribe` | `+0x238` | 阵营/种族 |
| `aggressive` | `+0x60` | 主动/侵略配置 |
| `move_speed_*` | `+0xAC~+0xB8` | 移动速度 |
| `bound_radius/front/side/upper` | `+0x218/+0x21C/+0x220` | 包围盒相关 |

### 6.2 运行时怪物对象

运行时 Actor / CharacterObject：

| 偏移 | 类型 | 说明 |
|---:|---|---|
| `+0x08` | `CEntity*` | 坐标在 CEntity |
| `+0x20` | `uint32` | objectType，`2 = NPC/怪物类` |
| `+0x2C` | `uint32` | Server Object ID |
| `+0x30` | `uint32` | NPC Template ID |
| `+0x3E` | `uint16` | 等级 |
| `+0x40` | `uint8` | HP 百分比 |
| `+0x42` | UTF-16 | 名称 |
| `+0x358` | `uint32` | 当前目标 Server ID |
| `+0x11A0` | `uint32` | 最大 HP |
| `+0x11A4` | `uint32` | 当前 HP |

### 6.3 怪物生成 / 移动 / 血量包

| 包名 | ID | 说明 |
|---|---:|---|
| `S_PUT_NPC` | `14` | 怪物/NPC 生成 |
| `S_MOVE_OBJECT` | `56` | 对象移动坐标 |
| `S_TARGET_INFO` | `41` | 目标等级、当前 HP、最大 HP |
| `S_HIT_POINT_OTHER` | `5` | 其他对象 HP 百分比 / 伤害事件 |
| `S_NPC_CHANGED_TARGET` | `40` | NPC/怪物改变目标 |

`S_MOVE_OBJECT` 结构：

```cpp
#pragma pack(push, 1)
struct S_MOVE_OBJECT {
    uint32_t serverObjectId; // +0x00
    float x;                 // +0x04
    float y;                 // +0x08
    float z;                 // +0x0C
    uint8_t directionFlags;   // +0x10
};
#pragma pack(pop)
```

`S_TARGET_INFO` 结构：

```cpp
#pragma pack(push, 1)
struct S_TARGET_INFO {
    uint32_t serverObjectId; // +0x00
    uint16_t level;          // +0x04
    uint32_t maxHp;          // +0x06
    uint32_t currentHp;      // +0x0A
};
#pragma pack(pop)
```

`S_HIT_POINT_OTHER` 主要携带 HP 百分比，不一定携带完整 HP：

```cpp
#pragma pack(push, 1)
struct S_HIT_POINT_OTHER {
    uint32_t serverObjectId; // +0x00
    int32_t amount;          // +0x04
    int8_t eventType;        // +0x08
    uint8_t hpPercent;       // +0x09
    uint16_t sourceOrEffect; // +0x0A
    uint8_t extraFlag;       // +0x0C
    uint8_t visualType;      // +0x0D
};
#pragma pack(pop)
```

### 6.4 附近怪物遍历

推荐只读方案：遍历 ServerObject 映射树或 EntitySystem 实体树，拿到 `CEntity*`，读取世界坐标，计算与玩家距离，再通过 `sub_18049B8A0` 或已验证指针链取得 `Actor*`，筛选：

```cpp
actor && *(uint32_t*)(actor + 0x20) == 2
```

重要函数：

```cpp
sub_180328F10 -> ScanNearbyTargetsAndPickBest
sub_18003DD40 -> CEntitySystem_QueryEntitiesAroundPoint
```

### 6.5 怪物是否锁定当前玩家

每只怪物的当前目标：

```cpp
uint32_t monsterTargetServerId = *(uint32_t*)(monsterActor + 0x358);
uint32_t localServerId         = *(uint32_t*)(localActor + 0x2C);

bool monsterTargetsMe = monsterTargetServerId == localServerId;
```

包 `S_NPC_CHANGED_TARGET`：

```cpp
#pragma pack(push, 1)
struct S_NPC_CHANGED_TARGET {
    uint32_t npcServerObjectId;    // +0x00
    uint32_t targetServerObjectId; // +0x04
};
#pragma pack(pop)
```

---

## 7. 障碍 / 视线 / 是否隔墙可攻击

障碍判定不是固定布尔地址，而是攻击或检测时临时调用物理射线。

| 内容 | RVA | 说明 |
|---|---:|---|
| `GetPhysicalWorld` | `0x3201B0` | 获取物理世界对象 |
| `Physics_HasClearLine` | `0x4A4250` | 通用两点无遮挡判断 |
| `g_testIntersect` | `0x00BAF0` | 测试两点碰撞命令 |
| `S_ATTACK_RESULT` | 包 ID `115` | 服务器攻击结果 |

底层射线函数：

```text
world = GetPhysicalWorld()
RayWorldIntersection = *(uintptr_t*)(*(uintptr_t*)world + 0xD0)
```

常规判断：

```text
返回碰撞数量 0 = 无障碍
返回 >0 = 有碰撞/被挡
```

注意：最终攻击是否成功可能由服务器权威确认。客户端锁定目标不等于一定可攻击。

---

## 8. 采集物

### 8.1 静态表

```cpp
sub_1804CAB00 -> LoadGatherSourceTable
```

加载：

```text
data\Gather\gather_src.xml
```

静态记录候选：

```cpp
struct GatherSourceInfo {
    uint32_t id;              // +0x00
    uint32_t nameStringId;    // +0x18
    uint32_t resultCount;     // +0x1C，产物槽数量，不是剩余采集次数
    uint32_t resultIds[8];    // +0x20 ~ +0x3C
    float interactRadius;     // +0x78
    uint32_t color;           // +0x7C
};
```

### 8.2 运行时采集物对象

```cpp
sub_18040ACD0 -> Handle_S_PUT_OBJECT
sub_1804C2020 -> SpawnGatherSourceFromPacket
```

采集物模板 ID 范围：

```text
400000 ~ 499999
```

运行时结构：

| 偏移 | 类型 | 说明 |
|---:|---|---|
| `+0x08` | `CEntity*` | 坐标读取入口 |
| `+0x20` | `uint32` | objectType，采集物为 `7` |
| `+0x2C` | `uint32` | Server Object ID |
| `+0x30` | `uint32` | gatherSourceId |
| `+0x3E` | `uint16` | 显示等级，通常 1 |
| `+0x40` | `uint8` | 疑似剩余次数/可用状态，需断点验证 |
| `+0x42` | UTF-16 | 名称 |
| `+0x168` | `float` | interaction radius |
| `+0x19C/+0x1A0/+0x1A4` | `float[3]` | 生成时坐标副本 |

实际坐标建议仍读：

```text
CEntity + 0x4B4/+0x4B8/+0x4BC
```

### 8.3 采集相关包与函数

| 包名 | ID | 函数 | 说明 |
|---|---:|---|---|
| `C_GATHER` | `19` | `sub_1800200C0` | 发送/取消采集入口 |
| `S_GATHER_OTHER` | `34` | `sub_180417F20` | 其他人采集事件 |
| `S_GATHER` | `35` | `sub_180055A60` | 本地采集事件 |

其他函数：

```cpp
sub_180324D80 -> CheckGatherTargetRange
sub_1804C4160 -> RemoveGatherObject
sub_18017D8B0 -> InitGatheringDialog
```

---

## 9. 异常状态 / 肉体状态异常 / 组队成员异常

### 9.1 Actor 异常列表

```cpp
Actor + 0xF18 = AbnormalStatus begin
Actor + 0xF20 = AbnormalStatus end
Actor + 0xF28 = AbnormalStatus capacity
Actor + 0xF38 = 肉体异常 debuffphy 数量
Actor + 0xF3C = 精神异常 debuffmen 数量
```

每条异常记录大小：

```text
0x12 = 18 字节
```

```cpp
#pragma pack(push, 1)
struct AbnormalStatusEntry {
    uint32_t field_00;          // +0x00
    uint32_t abnormalId;        // +0x04
    uint32_t dispelCategory;    // +0x08
    int32_t timeOrSource;       // +0x0C
    uint16_t levelOrStack;      // +0x10
};
#pragma pack(pop)
```

`dispelCategory`：

```text
0 = never
1 = buff
2 = debuffphy / 肉体异常
3 = debuffmen / 精神异常
8 = extra
```

判断当前玩家是否有肉体异常：

```cpp
bool hasPhysicalAbnormal = *(uint32_t*)(localActor + 0xF38) != 0;
```

### 9.2 组队成员快照

主列表：

```text
GameBase + 0xD1BAE8 -> Party list header
```

`PartyMember`：

| 偏移 | 类型 | 说明 |
|---:|---|---|
| `+0x04` | `uint32` | 队员 Server Object ID |
| `+0x37` | `uint8` | 标志位；bit `0x08` 表示包含异常状态块 |
| `+0x77` | `int16` | 异常状态数量 |
| `+0x79` | array | 异常数组开始，每条 `0x12` |
| `+0x859` | `uint32` | 更新时间/包时间基准 |

扫描 `member + 0x79 + i*0x12 + 0x08 == 2`，即可判断队友快照中存在肉体异常。

关键函数：

```cpp
sub_180410B70 -> Handle_S_ABNORMAL_STATUS
sub_180410D80 -> Handle_S_ABNORMAL_STATUS_OTHER
sub_18047D8C0 -> Actor_ClearAbnormalStatuses
sub_18047DA30 -> Actor_AddAbnormalStatus
sub_180025620 -> UpdatePartyMemberInfo
sub_18003D1C0 -> RefreshPartyMemberAbnormalListFromActor
```

---

## 10. 技能系统

### 10.1 已学习技能容器

```text
GameBase + 0xD004A0 -> PlayerData/Skill manager
manager + 0x828 -> learned skills tree
```

容器结构近似：

```cpp
std::map<uint32_t skillId,
    std::map<uint16_t level,
        std::list<SkillItem*>>>
```

取最高等级技能：外层按 `skillId` 遍历；内层取 `header->right` 最大 level；list 取 `header->prev` 最后一个 `SkillItem*`。

重要函数：

| RVA | 建议名 | 说明 |
|---:|---|---|
| `0x390CF0` | `SkillManager_GetHighestLearnedSkill` | 按技能 ID 查询最高等级已学技能 |
| `0x390F60` | `SkillManager_AddLearnedSkill` | 添加已学习技能 |
| `0x390DC0` | `SkillManager_RemoveLearnedSkill` | 删除已学习技能 |
| `0x410280` | `Handle_S_ADD_SKILL` | 服务器增加技能 |
| `0x039240` | `Handle_S_TOGGLE_SKILL_ON_OFF` | Toggle 状态更新 |

### 10.2 SkillItem 偏移

| 偏移 | 类型 | 说明 |
|---:|---|---|
| `+0x08` | `uint32` | skillId |
| `+0x18` | `std::wstring` | 名称 |
| `+0x50` | `uint32` | 冷却总时长，毫秒 |
| `+0x54` | `uint32` | 冷却结束 tick，毫秒 |
| `+0x58` | `uint32` | itemType，通常 `21` |
| `+0x60` | `uint32` | Toggle 状态：`0=关`，`4=开` |
| `+0x64` | `uint32` | skillLevel |
| `+0x6C` | `uint32` | runtimeState，语义未完全确认 |
| `+0x74` | `uint32` | sourceFlags，语义未完全确认 |

剩余冷却：

```cpp
remainingMs = max(0, *(uint32_t*)(skillItem + 0x54) - GetClientTickMs());
```

一般可用 `GetTickCount()` 对比；若时钟源不一致，需要校准偏移。

### 10.3 技能静态 Detail

技能静态表：

```text
Data\skills\client_skills.xml
```

单条静态记录大小：

```text
0x7D8 = 2008 字节
```

查询函数：

```cpp
sub_1800E62F0 -> StaticSkillRecordQuery
sub_180517870 -> ParseClientSkillRecord
```

核心字段：

| 偏移 | 说明 |
|---:|---|
| `+0x000` | skill id |
| `+0x008` | icon name |
| `+0x010` | name text |
| `+0x038` | skillSchool：0=Physical，1=Magical |
| `+0x040` | activationAttribute |
| `+0x044` | costParameter |
| `+0x0D0` | delayType |
| `+0x0D4` | delayTime |
| `+0x0D8` | castingDelay |
| `+0x0DC` | chargingDelay |
| `+0x0E0` | delayId |
| `+0x0E4` | dispelCategory |
| `+0x0EC` | firstTarget |
| `+0x0F0` | targetRange |
| `+0x0F4` | targetRelation |
| `+0x0F8` | targetAreaType |
| `+0x100` | targetValidStatus |

`activationAttribute`：

```text
1  = Toggle
2  = Active
4  = Maintain
8  = Passive
16 = Provoked
```

### 10.4 技能是否可用

没有单独的 `SkillItem.isUsable` 偏移。

最准确函数：

```cpp
sub_1805F7580 -> CanUseSkillNow(skillId)
```

辅助函数：

```cpp
sub_1805F6400 -> IsSkillBlockedOrUnusable
sub_1803930C0 -> GetSkillCooldownOrChargeState
```

外部只读可做基础判断：已学习 + 主动/Toggle/Maintain + 冷却为 0。但这不等于游戏内部完整判断，因为还涉及目标、距离、MP、武器、状态、区域等。

---

## 11. 背包 / 物品

### 11.1 Inventory Manager

```text
GameBase + 0xD004A0 -> PlayerData/Inventory manager
manager + 0x774 -> 背包最大槽位数
manager + 0x778 -> inventory item tree header
manager + 0x780 -> item tree count
manager + 0x788 -> 装备栏 InstanceId 数组，32 个 uint32
```

相关函数：

```cpp
sub_180382790 -> InventoryManager_ctor
sub_180333720 -> CreateInventoryItemFromPacket
sub_1803881F0 -> InventoryManager_InsertItem
sub_1803886D0 -> InventoryManager_FindItemByInstanceId
sub_1803887B0 -> InventoryManager_RemoveItem
sub_18038BA80 -> InventoryManager_SetBagCapacity
sub_18038BB10 -> InventoryManager_UpdateEquipmentSlots
sub_18038E000 -> InventorySlotToPage
sub_18040DCC0 -> Handle_S_LOAD_INVENTORY
sub_18040DF20 -> Handle_S_ADD_INVENTORY
sub_18040E1D0 -> Handle_S_CHANGE_ITEM_DESC
```

包：

| 包名 | ID |
|---|---:|
| `S_LOAD_INVENTORY` | `26` |
| `S_ADD_INVENTORY` | `27` |
| `S_REMOVE_INVENTORY` | `28` |
| `S_CHANGE_ITEM_DESC` | `29` |

### 11.2 InventoryItem 偏移

单个物品对象大小：

```text
0x558 = 1368 字节
```

| 偏移 | 类型 | 说明 |
|---:|---|---|
| `+0x08` | `uint32` | Item Instance ID，唯一运行时 ID |
| `+0x0C` | `uint32` | Item Template ID，静态物品 ID |
| `+0x10` | `int64` | 堆叠数量 |
| `+0x18` | `std::wstring` | 显示名称 |
| `+0x60` | `uint32` | 内部物品类型 |
| `+0x74` | `uint32` | 装备槽 bitmask；非零通常表示已装备 |
| `+0x78` | `uint32` | flags；`0x1000` 被当作 Cash Item |
| `+0x80` | `uint64` | 价格/价值候选 |
| `+0x4EE` | `int16` | 背包槽位，`-1` 表示不在普通背包 |
| `+0x4F4` | `wchar_t[26]` | 自定义名称 |
| `+0x530` | `uint64` | 到期时间原始值 |
| `+0x548` | `uint32` | 时长秒数 |
| `+0x550` | `uint32` | 额外计数/状态，语义未完全确认 |

### 11.3 背包槽位与相对坐标

已确认每页 27 格：

```cpp
page = slot / 27;
indexInPage = slot % 27;
```

如果 UI 为 9 列 × 3 行：

```cpp
column = indexInPage % 9;
row = indexInPage / 9;
```

格子中心相对网格的归一化坐标：

```cpp
normalizedX = (column + 0.5f) / 9;
normalizedY = (row + 0.5f) / 3;
```

界面像素坐标还需要：窗口位置、格子尺寸、间距、UI 缩放。可通过 `BindInventoryItemToSlotWidget` 继续定位 UI 控件矩形。

```cpp
sub_1805A3550 -> BindInventoryItemToSlotWidget
```

### 11.4 静态物品表

运行时物品能给出实例 ID、模板 ID、数量、槽位、名称、flags。是否可出售/可丢弃/品质/最大堆叠等需要静态物品表。

已知静态查询：

```cpp
sub_1800E62F0(..., itemId, variant, 0x208)
sub_180375060(itemId) -> 静态名称
sub_1803769E0(itemId) -> 静态记录 +0x140
sub_180376A40(itemId) -> 静态记录 +0x144
```

---

## 12. 重要网络包速查

| ID | 包名 | 作用 |
|---:|---|---|
| `3` | `S_HIT_POINT` | 本地 HP |
| `4` | `S_MANA_POINT` | 本地 MP |
| `5` | `S_HIT_POINT_OTHER` | 其他对象 HP 事件 / 百分比 |
| `6` | `S_DP` | DP |
| `14` | `S_PUT_NPC` | 生成 NPC/怪物 |
| `19` | `C_GATHER` | 采集请求/取消 |
| `26` | `S_LOAD_INVENTORY` | 加载背包 |
| `27` | `S_ADD_INVENTORY` | 添加背包物品 |
| `28` | `S_REMOVE_INVENTORY` | 删除背包物品 |
| `29` | `S_CHANGE_ITEM_DESC` | 修改物品描述/状态 |
| `31` | `C_CHANGE_TARGET` | 客户端改变目标 |
| `34` | `S_GATHER_OTHER` | 其他人采集事件 |
| `35` | `S_GATHER` | 本地采集事件 |
| `40` | `S_NPC_CHANGED_TARGET` | NPC 改变目标 |
| `41` | `S_TARGET_INFO` | 当前目标详细信息 |
| `49` | `S_ABNORMAL_STATUS` | 本地异常状态 |
| `50` | `S_ABNORMAL_STATUS_OTHER` | 其他对象异常状态 |
| `55` | `S_MOVE_NEW` | 移动相关 |
| `56` | `S_MOVE_OBJECT` | 对象移动 |
| `91` | `S_PARTY_MEMBER_INFO` | 队伍成员快照 |
| `115` | `S_ATTACK_RESULT` | 攻击结果 |

分发函数：

```cpp
sub_180405100 -> DispatchServerPacket
```

---

## 13. C# 只读实现片段

### 13.1 读取基础状态和坐标

```csharp
// HP/MP/DP
uint hp    = memory.ReadUInt32(gameBase + 0xD267E0);
uint maxHp = memory.ReadUInt32(gameBase + 0xD267DC);
uint mp    = memory.ReadUInt32(gameBase + 0xD267E8);
uint maxMp = memory.ReadUInt32(gameBase + 0xD267E4);
ushort dp  = memory.ReadUInt16(gameBase + 0xD267EE);

// Camera
float pitch = memory.ReadSingle(gameBase + 0xD1AD14);
float roll  = memory.ReadSingle(gameBase + 0xD1AD18);
float yaw   = memory.ReadSingle(gameBase + 0xD1AD1C);

// CEntity 坐标
float x = memory.ReadSingle(entity + 0x4B4);
float y = memory.ReadSingle(entity + 0x4B8);
float z = memory.ReadSingle(entity + 0x4BC);

// CEntity 身体角度
float bodyPitch = memory.ReadSingle(entity + 0x4E8);
float bodyRoll  = memory.ReadSingle(entity + 0x4EC);
float bodyYaw   = memory.ReadSingle(entity + 0x4F0);
```

### 13.2 红黑树节点遍历原则

MSVC 红黑树通用结构：

```csharp
bool IsNil(ulong node) => node == 0 || ReadByte(node + 0x19) != 0;

// header->left   = begin / 最小节点
// header->parent = root
// header->right  = 最大节点
```

对 Entity tree：

```text
node + 0x20 = entityId
node + 0x28 = CEntity*
```

对 Inventory tree：

```text
node + 0x20 = Item Instance ID
node + 0x28 = InventoryItem*
```

### 13.3 计算背包页、行、列

```csharp
int page = slot / 27;
int indexInPage = slot % 27;
int column = indexInPage % 9;
int row = indexInPage / 9;
```

### 13.4 计算技能剩余冷却

```csharp
uint duration = memory.ReadUInt32(skillItem + 0x50);
uint endTick  = memory.ReadUInt32(skillItem + 0x54);
uint now      = GetTickCount();

int remaining = unchecked((int)(endTick - now));
if (remaining < 0)
    remaining = 0;
```

### 13.5 判断怪物是否锁定当前玩家

```csharp
uint localServerId = memory.ReadUInt32(localActor + 0x2C);
uint monsterTarget = memory.ReadUInt32(monsterActor + 0x358);
bool targetingMe = monsterTarget == localServerId;
```

### 13.6 判断肉体异常

```csharp
bool hasPhysicalAbnormal =
    memory.ReadUInt32(actor + 0xF38) != 0;
```

扫描完整异常列表：

```csharp
ulong begin = memory.ReadPointer(actor + 0xF18);
ulong end   = memory.ReadPointer(actor + 0xF20);

for (ulong p = begin; p < end; p += 0x12)
{
    uint category = memory.ReadUInt32(p + 0x08);
    if (category == 2)
    {
        uint abnormalId = memory.ReadUInt32(p + 0x04);
        // debuffphy
    }
}
```

---

## 14. 推荐验证清单

### 玩家坐标

```text
1. 移动角色，观察 CEntity +0x4B4/+0x4B8/+0x4BC。
2. 原地转身，观察 CEntity +0x4E8/+0x4EC/+0x4F0。
3. 只转相机，观察 GameBase+D1AD14/D1AD1C。
```

### 怪物信息

```text
1. 断 sub_18040BB10，验证 S_PUT_NPC 的生成字段。
2. 断 sub_1804C4E40，验证 S_MOVE_OBJECT 的坐标字段。
3. 断 sub_180039660，验证 S_TARGET_INFO 的 HP 字段。
4. 断 sub_180498FE0，验证 Actor +0x358 目标 Server ID。
```

### 采集物

```text
1. 断 sub_1804C2020，记录新采集物 Actor。
2. 检查 Actor +0x20 == 7。
3. 检查 +0x2C、+0x30、+0x42。
4. 对 +0x40 下写入断点，确认是否是剩余采集次数/状态。
```

### 背包

```text
1. 断 sub_180333720，观察 InventoryItem 创建。
2. 断 sub_1803881F0，观察插入 tree。
3. 检查 item +0x4EE 与 UI 格子是否一致。
```

### 技能

```text
1. 断 sub_180410280 和 sub_180390F60，观察技能加载。
2. 断 sub_1803910F0，释放技能后检查 SkillItem +0x50/+0x54。
3. 断 sub_1805F7580，点击技能时看返回值。
```

---

## 15. 高风险误区

1. `CEntity + 0x4E8` 是角度，不是坐标。
2. `hpgauge_level` 不是 HP。
3. `S_HIT_POINT_OTHER` 通常给 HP 百分比，不是完整 HP；完整 HP 主要来自 `S_TARGET_INFO`。
4. 技能“是否可用”不是一个布尔偏移，而是 `CanUseSkillNow(skillId)` 的综合结果。
5. 背包清理不要只按 `ItemType` 或未知 flags 自动删除，可能误删任务物品/高价值物品。
6. `objectType == 2` 是 NPC/怪物大类，可能包括非敌对 NPC；还需静态表或关系字段过滤。
7. 视线障碍不是持久地址，是射线检测结果。
8. 当前锁定目标和怪物锁定玩家是两回事：前者是 `GameBase+D2179A`，后者是 `monsterActor+0x358`。

---

## 16. 推荐 IDA 重命名汇总

```cpp
// Core / dispatch
sub_180405100 -> DispatchServerPacket
sub_1802F90B0 -> RegisterServerPacketNames

// Entity
sub_180001000 -> InitCryMemoryManager
sub_180001160 -> CryMallocWrapper
sub_1800011D0 -> CryFreeWrapper
sub_1800011F0 -> GetGlobalSystem
sub_1800014D0 -> CEntity_ctor
sub_180006F30 -> CEntity_SetPosition
sub_180006FB0 -> CEntity_GetPositionPtr
sub_180007250 -> CEntity_SetAngles
sub_1800073F0 -> CEntity_GetAnglesPtr
sub_180008A60 -> CEntity_SetHidden
sub_18003B620 -> CEntitySystem_ctor
sub_18003C4E0 -> CEntitySystem_Init
sub_18003CE50 -> CEntitySystem_SpawnEntity
sub_18003B230 -> CEntitySystem_DeleteEntity
sub_18003D580 -> CEntitySystem_GetEntityById
sub_18003DD40 -> CEntitySystem_QueryEntitiesAroundPoint

// Target / actor
sub_1803268A0 -> SetCurrentTarget
sub_180326560 -> GetCurrentTargetServerObjectId
sub_180326670 -> GetCurrentTargetGameObject
sub_180326700 -> GetCurrentTargetCharacter
sub_180498FE0 -> Actor_SetTargetServerObjectId
sub_18049B8A0 -> CEntity_GetCharacterObject
sub_18045A680 -> CEntity_GetGameObject
sub_180465310 -> Actor_SetCurrentAndMaxHp
sub_180465250 -> Actor_SetHpPercent

// Monster / NPC
sub_1804272A0 -> ParseNpcXmlDataNode
sub_180429A90 -> LoadNpcXmlAndMeshTables
sub_180029580 -> LoadClientNpcsXml
sub_1804330B0 -> LoadAiCharProperties
sub_18040BB10 -> Handle_S_PUT_NPC
sub_1804C0310 -> SpawnNpcFromPacket
sub_1804C4E40 -> Handle_S_MOVE_OBJECT
sub_180039660 -> Handle_S_TARGET_INFO
sub_1804C3CE0 -> Handle_S_HIT_POINT_OTHER
sub_180417480 -> Handle_S_NPC_CHANGED_TARGET

// Gather
sub_1804CAB00 -> LoadGatherSourceTable
sub_18040ACD0 -> Handle_S_PUT_OBJECT
sub_1804C2020 -> SpawnGatherSourceFromPacket
sub_1804C4160 -> RemoveGatherObject
sub_180055A60 -> Handle_S_GATHER
sub_180417F20 -> Handle_S_GATHER_OTHER
sub_1800200C0 -> SendOrCancelGather
sub_180324D80 -> CheckGatherTargetRange

// Abnormal / party
sub_180410B70 -> Handle_S_ABNORMAL_STATUS
sub_180410D80 -> Handle_S_ABNORMAL_STATUS_OTHER
sub_18047D8C0 -> Actor_ClearAbnormalStatuses
sub_18047DA30 -> Actor_AddAbnormalStatus
sub_180025620 -> UpdatePartyMemberInfo
sub_18003D1C0 -> RefreshPartyMemberAbnormalListFromActor

// Skills
sub_180390CF0 -> SkillManager_GetHighestLearnedSkill
sub_180390F60 -> SkillManager_AddLearnedSkill
sub_180390DC0 -> SkillManager_RemoveLearnedSkill
sub_1805F7580 -> CanUseSkillNow
sub_1805F6400 -> IsSkillBlockedOrUnusable
sub_1803930C0 -> GetSkillCooldownOrChargeState
sub_1803910F0 -> SkillManager_SetCooldown
sub_180517870 -> ParseClientSkillRecord
sub_180410280 -> Handle_S_ADD_SKILL
sub_180039240 -> Handle_S_TOGGLE_SKILL_ON_OFF

// Inventory
sub_180382790 -> InventoryManager_ctor
sub_180333720 -> CreateInventoryItemFromPacket
sub_1803881F0 -> InventoryManager_InsertItem
sub_1803886D0 -> InventoryManager_FindItemByInstanceId
sub_1803887B0 -> InventoryManager_RemoveItem
sub_18038BA80 -> InventoryManager_SetBagCapacity
sub_18038BB10 -> InventoryManager_UpdateEquipmentSlots
sub_18038E000 -> InventorySlotToPage
sub_18040DCC0 -> Handle_S_LOAD_INVENTORY
sub_18040DF20 -> Handle_S_ADD_INVENTORY
sub_18040E1D0 -> Handle_S_CHANGE_ITEM_DESC
sub_1805A3550 -> BindInventoryItemToSlotWidget

// Camera / physics
sub_180030340 -> Player_UpdateCamera
sub_1803201B0 -> GetPhysicalWorld
sub_1804A4250 -> Physics_HasClearLine
```

---

## 17. 后续建议

1. 把本文函数名先写回 IDA。
2. 对每个核心函数加函数注释，避免后续重复分析。
3. 对 C# 读取器做统一基址管理：不要硬编码 `0x180...`。
4. 每个结构都要做指针验证，例如 `CEntity+0xF0 == expectedEntityId`。
5. 输出 CSV/JSON 时同时输出：地址、原始字段、解释字段、置信度。
6. 所有自动化行为先只读，不要直接写内存或发包。

---

## 18. 活文档说明 / Living Document
生成时间：`2026-06-26 22:41:11`。本版是“可持续更新”的总索引，不只包含已经问过的内容，也包含从 IDA 导出中自动归类出的高价值方向。

本包新增文件：

- `data/offsets_cheatsheet.csv`：所有核心 RVA/偏移一览。
- `data/high_value_shortlist.csv`：人工确认的高价值函数短名单。
- `data/subsystem_index.csv`：自动按子系统分类的函数索引。
- `data/data_paths_and_resources.csv`：所有资源路径、XML、TXT、DDS、CGF 字符串引用。
- `data/server_packets.csv` / `data/client_packets.csv`：服务器/客户端包名注册列表。
- `data/rename_plan_living.json`：可导回 IDA 的重命名计划。
- `code/Offsets.cs`：C# RVA/偏移常量。
- `scripts/regeneration_workflow.md`：客户端更新后的维护步骤。

### 活文档更新原则

1. 先定位字符串和调用链，再确认偏移。
2. 任何客户端更新后，RVA 默认失效。
3. 运行时验证优先级高于静态推断。
4. `CEntity` 只保存引擎实体状态，游戏业务数据通常在 `Actor/CharacterObject` 或 Manager 容器中。
5. 本文只做只读结构分析，不包含绕过反作弊、自动发包、自动丢弃/出售等实现。

## 19. 未逐项询问但已归类的子系统地图

| 子系统 | 候选函数数 | 代表函数/线索 | 下一步建议 |
|---|---:|---|---|
| `network_packets` | 1479 | 0x1800155B0 sub_1800155B0 (605 strings); 0x1802F90B0 sub_1802F90B0 (247 strings); 0x180517870 sub_180517870 (225 strings) | 继续从 server/client packet CSV 建立 handler 映射。 |
| `player_status` | 178 | 0x1800155B0 sub_1800155B0 (605 strings); 0x1802F90B0 sub_1802F90B0 (247 strings); 0x180517870 sub_180517870 (225 strings) | 围绕 S_STATUS/S_HIT_POINT/S_MANA_POINT 查本地状态。 |
| `entity_system` | 73 | 0x1800155B0 sub_1800155B0 (605 strings); 0x18035C3C0 sub_18035C3C0 (174 strings); 0x18003B620 sub_18003B620 (89 strings) | 围绕 CEntitySystem 红黑树、Spawn/Delete/Update 查运行时对象。 |
| `npc_monster` | 103 | 0x1800155B0 sub_1800155B0 (605 strings); 0x1802F90B0 sub_1802F90B0 (247 strings); 0x180603D80 sub_180603D80 (217 strings) | 继续还原 NPC 静态记录和 S_PUT_NPC/S_TARGET_INFO。 |
| `gather` | 27 | 0x1800155B0 sub_1800155B0 (605 strings); 0x1802F90B0 sub_1802F90B0 (247 strings); 0x180603D80 sub_180603D80 (217 strings) | 还原 gather_src 静态表和 S_GATHER 事件编号。 |
| `skill` | 123 | 0x1800155B0 sub_1800155B0 (605 strings); 0x1802F90B0 sub_1802F90B0 (247 strings); 0x180517870 sub_180517870 (225 strings) | 完成 SkillRecord 0x7D8 结构和 CanUseSkillNow 调用。 |
| `inventory_item` | 276 | 0x1800155B0 sub_1800155B0 (605 strings); 0x1802F90B0 sub_1802F90B0 (247 strings); 0x180603D80 sub_180603D80 (217 strings) | 完成 client_items.xml 静态物品记录 0x208。 |
| `abnormal_status` | 18 | 0x1802F90B0 sub_1802F90B0 (247 strings); 0x180517870 sub_180517870 (225 strings); 0x180603D80 sub_180603D80 (217 strings) | 进一步枚举异常 entry 的所有字段。 |
| `party_social` | 118 | 0x1802F90B0 sub_1802F90B0 (247 strings); 0x180517870 sub_180517870 (225 strings); 0x180603D80 sub_180603D80 (217 strings) | 完善 PartyMember 结构和组队 UI/状态包。 |
| `quest_task` | 241 | 0x1802F90B0 sub_1802F90B0 (247 strings); 0x180603D80 sub_180603D80 (217 strings); 0x1802FCD20 sub_1802FCD20 (199 strings) | 从 quest/task loader 入手解析任务目标/奖励/坐标。 |
| `map_world` | 387 | 0x1800155B0 sub_1800155B0 (605 strings); 0x1802F90B0 sub_1802F90B0 (247 strings); 0x180517870 sub_180517870 (225 strings) | 整理 world/map/zone/fly path/terrain 表。 |
| `chat_mail` | 234 | 0x1800155B0 sub_1800155B0 (605 strings); 0x1802F90B0 sub_1802F90B0 (247 strings); 0x180603D80 sub_180603D80 (217 strings) | 可拆 mail_list、mailbox、chat channel。 |
| `loot_drop` | 46 | 0x1802F90B0 sub_1802F90B0 (247 strings); 0x180603D80 sub_180603D80 (217 strings); 0x1802FCD20 sub_1802FCD20 (199 strings) | 查 line drop、goodslist、loot UI 和掉落包。 |
| `craft_recipe` | 29 | 0x1802F90B0 sub_1802F90B0 (247 strings); 0x1802FCD20 sub_1802FCD20 (199 strings); 0x18035C3C0 sub_18035C3C0 (174 strings) | 查 recipe、material、craft result 静态表。 |
| `trade_shop` | 113 | 0x1800155B0 sub_1800155B0 (605 strings); 0x1802F90B0 sub_1802F90B0 (247 strings); 0x180603D80 sub_180603D80 (217 strings) | 查 vendor/broker/warehouse/auction 结构。 |
| `camera_physics` | 33 | 0x1800155B0 sub_1800155B0 (605 strings); 0x180517870 sub_180517870 (225 strings); 0x18035C3C0 sub_18035C3C0 (174 strings) | 继续验证射线、碰撞、相机矩阵/FOV。 |
| `ui_dialog` | 520 | 0x1800155B0 sub_1800155B0 (605 strings); 0x1802F90B0 sub_1802F90B0 (247 strings); 0x180517870 sub_180517870 (225 strings) | 可用作 UI 控件坐标、背包格子、技能栏定位。 |
| `character_create_custom` | 16 | 0x18014B520 sub_18014B520 (105 strings); 0x1804D3710 sub_1804D3710 (28 strings); 0x1805697F0 sub_1805697F0 (26 strings) | 角色创建/捏脸/外观预设相关。 |
| `debug_cvar` | 63 | 0x1800155B0 sub_1800155B0 (605 strings); 0x18003B620 sub_18003B620 (89 strings); 0x1801229C0 sub_1801229C0 (86 strings) | 大量调试命令和 CVar，可用于定位内部功能。 |

## 20. 服务器包速查（自动提取）

| Index | 包名 |
|---:|---|
| 0 | `S_VERSION_CHECK` |
| 1 | `S_STATUS` |
| 2 | `S_STATUS_OTHER` |
| 3 | `S_HIT_POINT` |
| 4 | `S_MANA_POINT` |
| 5 | `S_HIT_POINT_OTHER` |
| 6 | `S_DP_USER` |
| 7 | `S_EXP` |
| 8 | `S_LOGIN_CHECK` |
| 9 | `S_CUTSCENE_NPC_INFO` |
| 10 | `S_CHANGE_GUILD_MEMBER_NICKNAME` |
| 11 | `S_GUILD_HISTORY` |
| 12 | `S_ENTER_WORLD_CHECK` |
| 13 | `S_PUT_NPC` |
| 14 | `S_WORLD` |
| 15 | `S_DUMMY_PACKET` |
| 16 | `S_PUT_OBJECT` |
| 17 | `S_PUT_VEHICLE` |
| 18 | `S_BUILDER_RESULT` |
| 19 | `S_REQUEST_TELEPORT` |
| 20 | `S_BLINK` |
| 21 | `S_REMOVE_OBJECT` |
| 22 | `S_WAIT_LIST` |
| 23 | `S_MESSAGE` |
| 24 | `S_MESSAGE_CODE` |
| 25 | `S_LOAD_INVENTORY` |
| 26 | `S_ADD_INVENTORY` |
| 27 | `S_REMOVE_INVENTORY` |
| 28 | `S_CHANGE_ITEM_DESC` |
| 29 | `S_LOAD_CLIENT_SETTINGS` |
| 30 | `S_CHANGE_STANCE` |
| 31 | `S_PUT_USER` |
| 32 | `S_USE_SKILL` |
| 33 | `S_GATHER_OTHER` |
| 34 | `S_GATHER` |
| 35 | `S_WIELD` |
| 36 | `S_ACTION` |
| 37 | `S_TIME` |
| 38 | `S_SYNC_TIME` |
| 39 | `S_NPC_CHANGED_TARGET` |
| 40 | `S_TARGET_INFO` |
| 41 | `S_SKILL_CANCELED` |
| 42 | `S_SKILL_SUCCEDED` |
| 43 | `S_ADD_SKILL` |
| 44 | `S_DELETE_SKILL` |
| 45 | `S_TOGGLE_SKILL_ON_OFF` |
| 46 | `S_ADD_MAINTAIN_SKILL` |
| 47 | `S_DELETE_MAINTAIN_SKILL` |
| 48 | `S_ABNORMAL_STATUS` |
| 49 | `S_ABNORMAL_STATUS_OTHER` |
| 50 | `S_LOAD_SKILL_COOLTIME` |
| 51 | `S_ASK` |
| 52 | `S_CANCEL_ASK` |
| 53 | `S_ATTACK` |
| 54 | `S_MOVE_NEW` |
| 55 | `S_MOVE_OBJECT` |
| 56 | `S_CHANGE_DIRECTION` |
| 57 | `S_POLYMORPH` |
| 58 | `S_SKILL_OTHER` |
| 59 | `S_NPC_HTML_MESSAGE` |
| 60 | `S_GUILD_OTHER_INFO` |
| 61 | `S_ADD_BOOKMARK` |
| 62 | `S_ITEM_LIST` |
| 63 | `S_GUILD_OTHER_MEMBER_INFO` |
| 64 | `S_WEATHER` |
| 65 | `S_INVISIBLE_LEVEL` |
| 66 | `S_RECALLED_BY_OTHER` |
| 67 | `S_EFFECT` |
| 68 | `S_LOAD_WORKINGQUEST` |
| 69 | `S_KEY` |
| 70 | `S_RESET_SKILL_COOLING_TIME` |
| 71 | `S_XCHG_START` |
| 72 | `S_ADD_XCHG` |
| 73 | `S_REMOVE_XCHG` |
| 74 | `S_XCHG_GOLD` |
| 75 | `S_XCHG_RESULT` |
| 76 | `S_ADDREMOVE_SOCIAL` |
| 77 | `S_CHECK_MESSAGE` |
| 78 | `S_USER_CHANGED_TARGET` |
| 79 | `S_EDIT_CHARACTER` |
| 80 | `S_SERIAL_KILLER_LIST` |
| 81 | `S_ABYSS_NEXT_PVP_CHANGE_TIME` |
| 82 | `S_ABYSS_CHANGE_NEXT_PVP_STATUS` |
| 83 | `S_CAPTCHA` |
| 84 | `S_ADDED_SERVICE_CHANGE` |
| 85 | `S_FIND_NPC_POS_RESULT` |
| 86 | `S_PARTY_INFO` |
| 87 | `S_PARTY_MEMBER_INFO` |
| 88 | `S_GGAUTH_CHECK_QUERY` |
| 89 | `S_ASK_QUIT_RESULT` |
| 90 | `S_ASK_INFO_RESULT` |
| 91 | `S_FATIGUE_INFO` |
| 92 | `S_FUNCTIONAL_PET` |
| 93 | `S_QUERY_NUMBER` |
| 94 | `S_LOAD_ITEM_COOLTIME` |
| 95 | `S_TODAY_WORDS` |
| 96 | `S_PLAY_CUTSCENE` |
| 97 | `S_GET_ON_VEHICLE` |
| 98 | `S_GET_OFF_VEHICLE` |
| 99 | `S_KICK` |

完整列表见 `data/server_packets.csv`。

## 21. 客户端包速查（自动提取）

| Index | 包名 |
|---:|---|
| 0 | `C_VERSION` |
| 1 | `C_LOGOUT` |
| 2 | `C_ASK_QUIT` |
| 3 | `C_READY_TO_QUIT` |
| 4 | `C_DEAD_RESTART` |
| 5 | `C_CHECK_LEVEL_DATA_VERSION` |
| 6 | `C_EDIT_CHARACTER` |
| 7 | `C_ENTER_WORLD` |
| 8 | `C_LEVEL_READY` |
| 9 | `C_SAVE_CLIENT_SETTINGS` |
| 10 | `C_FIND_NPC_POS` |
| 11 | `C_CHANGE_OPTION_FLAGS` |
| 12 | `C_CHANGE_DIRECTION` |
| 13 | `C_CAPTCHA` |
| 14 | `C_ACCEPT_TELEPORT` |
| 15 | `C_REQUEST_GUILD_NAME` |
| 16 | `C_BLINK` |
| 17 | `C_SYNC_TIME` |
| 18 | `C_GATHER` |
| 19 | `C_MINIGAME` |
| 20 | `C_FUNCTIONAL_PET_MOVE` |
| 21 | `C_FUNCTIONAL_PET` |
| 22 | `C_TOGGLE_DOOR` |
| 23 | `C_TOGGLE_CHEST` |
| 24 | `C_GIVE_ITEM` |
| 25 | `C_PETITION` |
| 26 | `C_SAY` |
| 27 | `C_WHISPER` |
| 28 | `C_CHANGE_TARGET` |
| 29 | `C_ATTACK` |
| 30 | `C_USE_SKILL` |
| 31 | `C_TURN_OFF_TOGGLE_SKILL` |
| 32 | `C_TURN_OFF_ABNORMAL_STATUS` |
| 33 | `C_TURN_OFF_MAINTAIN_SKILL` |
| 34 | `C_USE_ITEM` |
| 35 | `C_USE_EQUIPMENT_ITEM` |
| 36 | `C_ASK_PC_INFO` |
| 37 | `C_SAVE` |
| 38 | `C_BUILDER_COMMAND` |
| 39 | `C_BUILDER_CONTROL` |
| 40 | `C_ACTION` |
| 41 | `C_ALIVE` |
| 42 | `C_GUILD` |
| 43 | `C_LEAVE_INSTANTDUNGEON` |
| 44 | `C_REQUEST_GUILD_EMBLEM_IMG` |
| 45 | `C_MOVE_NEW` |
| 46 | `C_PATH_FLY` |
| 47 | `C_ANSWER` |
| 48 | `C_BUY_SELL` |
| 49 | `C_START_DIALOG` |
| 50 | `C_END_DIALOG` |
| 51 | `C_HACTION` |
| 52 | `C_REQUEST_GUILD_HISTORY` |
| 53 | `C_BOOKMARK` |
| 54 | `C_DELETE_BOOKMARK` |
| 55 | `C_TODAY_WORDS` |
| 56 | `C_CHANGE_EMBLEM_VER` |
| 57 | `C_ASK_PARTY_INFO` |
| 58 | `C_ASK_LOG` |
| 59 | `C_ASK_XCHG` |
| 60 | `C_ADD_XCHG` |
| 61 | `C_REMOVE_XCHG` |
| 62 | `C_XCHG_GOLD` |
| 63 | `C_CHECK_XCHG` |
| 64 | `C_ACCEPT_XCHG` |
| 65 | `C_CANCEL_XCHG` |
| 66 | `C_WIND_PATH` |
| 67 | `C_CUSTOM_ANIM` |
| 68 | `C_ENCHANT_ITEM` |
| 69 | `C_GUILD_FUND` |
| 70 | `C_PARTY_MATCH` |
| 71 | `C_CHARGE_ITEM` |
| 72 | `C_GIVE_UP_QUEST` |
| 73 | `C_QUIT_CUTSCENE` |
| 74 | `C_ACCOUNT_INSTANTDUNGEON` |
| 75 | `C_UNUSED_NEW_5` |
| 76 | `C_QUERY_NUMBER_RESULT` |
| 77 | `C_FATIGUE_KOREA` |
| 78 | `C_TRADE_IN` |
| 79 | `C_CHANGE_ITEM_SKIN` |
| 80 | `C_GIVE_ITEM_PROC` |
| 81 | `C_GET_ON_VEHICLE` |
| 82 | `C_GET_OFF_VEHICLE` |
| 83 | `C_PARTY` |
| 84 | `C_PARTY_BY_NAME` |
| 85 | `C_ALLI_CHANGE_GROUP` |
| 86 | `C_UNUSED_19` |
| 87 | `C_VIEW_OTHER_INVENTORY` |
| 88 | `C_PING` |
| 89 | `C_NCGUARD` |
| 90 | `C_UNUSED_21` |
| 91 | `C_PLATE` |
| 92 | `C_SIMPLE_DICE` |
| 93 | `C_SPLIT_GOLD` |
| 94 | `C_GET_PK_COUNT` |
| 95 | `C_QUERY_BUDDY` |
| 96 | `C_ADD_BUDDY` |
| 97 | `C_REMOVE_BUDDY` |
| 98 | `C_SMS` |
| 99 | `C_DUEL` |

完整列表见 `data/client_packets.csv`。

## 22. 数据文件 / 资源路径索引

| 函数 | 字符串 |
|---|---|
| `0x18000BAF0 sub_18000BAF0` | `%slevelInfo.xml` |
| `0x180013BB0 sub_180013BB0` | `UI_Login.xml` |
| `0x1800155B0 sub_1800155B0` | `CreateInfo file name. Default is "CreateInfos.xml".
Usage: g_create_info "filename"
Default value is "CreateInfos.xml".` |
| `0x1800155B0 sub_1800155B0` | `CreateInfos.xml` |
| `0x180027EE0 sub_180027EE0` | `Levels\%s\client_world_%s.xml` |
| `0x180029580 sub_180029580` | `data\NPCs\client_npcs.xml` |
| `0x18002B080 sub_18002B080` | `skillTooltipDebug.txt` |
| `0x18002C4F0 sub_18002C4F0` | `UI_Scene.xml` |
| `0x18002C4F0 sub_18002C4F0` | `UI_MoviePlayer.xml` |
| `0x18002EA40 sub_18002EA40` | `UI_Ending.xml` |
| `0x18002F150 sub_18002F150` | `Data\Npcs` |
| `0x18002F150 sub_18002F150` | `Data/Npcs/AiCharProperties.xml` |
| `0x180038F60 sub_180038F60` | `UI_Customizing.xml` |
| `0x18003AED0 sub_18003AED0` | `UI_Preload.xml` |
| `0x18003AED0 sub_18003AED0` | `fontshader.xml` |
| `0x18003AED0 sub_18003AED0` | `UI_SpriteFx.xml` |
| `0x18003AED0 sub_18003AED0` | `UI_SpriteFxSeq.xml` |
| `0x180041A80 sub_180041A80` | `UI_Credit.xml` |
| `0x180044190 sub_180044190` | `banned.txt` |
| `0x180044190 sub_180044190` | `AionFilterChat.txt` |
| `0x180044190 sub_180044190` | `AionFilterLine.dat` |
| `0x180044190 sub_180044190` | `HtmlPages.xml` |
| `0x180044190 sub_180044190` | `data/dialogs/` |
| `0x180044190 sub_180044190` | `CutScenes.xml` |
| `0x180044190 sub_180044190` | `data/CutScene/` |
| `0x180044190 sub_180044190` | `CutSceneMovies.xml` |
| `0x180044190 sub_180044190` | `data\NPCs\client_npcs.xml` |
| `0x180044190 sub_180044190` | `data\func_pet\toypet_item.xml` |
| `0x180044190 sub_180044190` | `data\func_pet\ToyPets.xml` |
| `0x180044190 sub_180044190` | `Failed to Load Abyss or Artifact table(client_abyss.xml)` |
| `0x180044190 sub_180044190` | `Failed to Load Abyss or Artifact table(client_artifact.xml)` |
| `0x180044190 sub_180044190` | `Failed to Load Abyss or Artifact table(client_abyss_rank.xml)` |
| `0x180044190 sub_180044190` | `data\npcs\client_npc_goodslist.xml` |
| `0x180044190 sub_180044190` | `data\npcs\client_npc_trade_in_list.xml` |
| `0x180044190 sub_180044190` | `stringtable_tip.xml` |
| `0x180047910 sub_180047910` | `UI_Wait.xml` |
| `0x1800505C0 sub_1800505C0` | `Outputs a list of commands and variables.
Usage: dumpcommandsvars
Saves a list of all registered commands and variables
to a file called consolecommandsandvars.txt` |
| `0x180052DC0 sub_180052DC0` | `UI_Create.xml` |
| `0x180054510 sub_180054510` | `UI_ServerSelect.xml` |
| `0x180054840 sub_180054840` | `Data\Check_Animation.txt` |
| `0x180054840 sub_180054840` | `Data\AnimationMarkers\*.xml` |
| `0x1800588F0 sub_1800588F0` | `Data\AnimationMarkers\%s.xml` |
| `0x1800588F0 sub_1800588F0` | `Data\skills\client_skills.xml` |
| `0x1800588F0 sub_1800588F0` | `
**FILENAME :: %s.xml**
` |
| `0x18005D230 sub_18005D230` | `UI_UserAgreement.xml` |
| `0x18005D510 sub_18005D510` | `banned.txt` |
| `0x18005D510 sub_18005D510` | `HtmlPages.xml` |
| `0x18005D510 sub_18005D510` | `data/dialogs/` |
| `0x18005D510 sub_18005D510` | `CutScenes.xml` |
| `0x18005D510 sub_18005D510` | `data/CutScene/` |
| `0x18005D510 sub_18005D510` | `data\npcs\client_npc_goodslist.xml` |
| `0x18005D510 sub_18005D510` | `data\npcs\client_npc_trade_in_list.xml` |
| `0x18005D510 sub_18005D510` | `stringtable_tip.xml` |
| `0x18005E830 sub_18005E830` | `data/CutScene/` |
| `0x18005ECC0 sub_18005ECC0` | `data/CutScene/` |
| `0x18005ECC0 sub_18005ECC0` | `data/CutScene/` |
| `0x180061620 sub_180061620` | `UI_Select.xml` |
| `0x180066C80 sub_180066C80` | `data\world\client_world_%s.xml` |
| `0x18006C0A0 sub_18006C0A0` | `Data\PC\pcexp_table.xml` |
| `0x18006C0A0 sub_18006C0A0` | `Error while loading pcexp_table.xml : %s` |
| `0x18006C380 sub_18006C380` | `Data\Prophet\prophet.xml` |
| `0x18006C380 sub_18006C380` | `Error while loading prophet.xml : %s` |
| `0x1800743E0 sub_1800743E0` | `quest\quest.xml` |
| `0x180074CE0 sub_180074CE0` | `Data\Quest\Quest.XML` |
| `0x180074F30 sub_180074F30` | `QuestDB(quest.xml, %s)` |
| `0x180074F30 sub_180074F30` | `Error in quest db(quest.xml), duplicated quest, id = %d, name = %s` |
| `0x1800758B0 sub_1800758B0` | `data\faction\npcfactions.xml` |
| `0x18007AE10 sub_18007AE10` | `Load Fail : event\%s\eventsetup.txt` |
| `0x18007E330 sub_18007E330` | `Data\PC\client_titles.xml` |
| `0x180080910 sub_180080910` | `npc_tribe_relation.xml` |
| `0x180080910 sub_180080910` | `Data\Npcs\%s` |
| `0x1800858E0 sub_1800858E0` | `NPCs.xml` |
| `0x18008A1D0 sub_18008A1D0` | `data\world\client_abyss.xml` |
| `0x18008A1D0 sub_18008A1D0` | `check client_abyss.xml NodeName` |
| `0x18008A810 sub_18008A810` | `data\world\client_artifact.xml` |
| `0x18008A810 sub_18008A810` | `Check client_artifact.xml NodeName` |
| `0x18008AD00 sub_18008AD00` | `data\world\client_abyss_rank.xml` |
| `0x18008AD00 sub_18008AD00` | `Check client_abyss_rank.xml NodeName` |
| `0x18008B5B0 sub_18008B5B0` | `data\PC\abyss_race_bonuses.xml` |
| `0x18008B5B0 sub_18008B5B0` | `Check abyss_race_bonuses.xml NodeName` |
| `0x18008BB00 sub_18008BB00` | `data\world\client_instance_cooltime.xml` |
| `0x18008BB00 sub_18008BB00` | `Check client_instance_cooltime.xml NodeName` |
| `0x18008C330 sub_18008C330` | `data\world\client_matchmaker.xml` |
| `0x18008C840 sub_18008C840` | `data\world\client_instance_bonusattr.xml` |
| `0x18008C840 sub_18008C840` | `Check client_instance_bonusattr.xml NodeName` |
| `0x1800BA960 sub_1800BA960` | `Data/PC/LongTypeMesh.lst` |
| `0x1800BB940 sub_1800BB940` | `Data/PC/Remove_FullFace_Mesh.lst` |
| `0x1800BBB60 sub_1800BBB60` | `Data/PC/Remove_Hair_Mesh.lst` |
| `0x1800BBD80 sub_1800BBD80` | `Data/PC/Force_Attachment_Mesh.lst` |
| `0x1800BBFA0 sub_1800BBFA0` | `Data/PC/Remove_Beard_Mesh.lst` |
| `0x1800BC1C0 sub_1800BC1C0` | `Data/PC/Shorten_Beard_Mesh.lst` |
| `0x1800BC3E0 sub_1800BC3E0` | `Data/PC/Normalize_Ear_Mesh.lst` |
| `0x1800BC600 sub_1800BC600` | `Data/PC/Normalize_Jaw_Mesh.lst` |
| `0x1800BC820 sub_1800BC820` | `Data/PC/Normalize_Nose_Mesh.lst` |
| `0x1800BCA40 sub_1800BCA40` | `Data/PC/Remove_Tail_Mesh.lst` |
| `0x1800BF190 sub_1800BF190` | `AutoSkillTest.txt` |
| `0x1800C0C50 sub_1800C0C50` | `banned.txt` |
| `0x1800C1130 sub_1800C1130` | `data/strings/` |
| `0x1800C1130 sub_1800C1130` | `data/strings/` |
| `0x1800C1130 sub_1800C1130` | `data/strings/` |
| `0x1800C1130 sub_1800C1130` | `%s%d.txt` |
| `0x1800C1660 sub_1800C1660` | `AionFilterChat.txt` |
| `0x1800C24E0 sub_1800C24E0` | `AionFilterLine.dat` |
| `0x1800C7FD0 sub_1800C7FD0` | `data\pc\client_battlepass_season.xml` |
| `0x1800C85C0 sub_1800C85C0` | `data\pc\client_battlepass_reward.xml` |
| `0x1800C9070 sub_1800C9070` | `data\bm\client_bm_restrict.xml` |
| `0x1800C9560 sub_1800C9560` | `data\bm\client_bm_pack.xml` |
| `0x1800C99F0 sub_1800C99F0` | `data\bm\client_bm_config.xml` |
| `0x1800CFB00 sub_1800CFB00` | `http://aion_client_temp/%d.html` |
| `0x1800DB4A0 sub_1800DB4A0` | `data\ui\` |
| `0x1800DB4A0 sub_1800DB4A0` | `http://aion_client/data/ui/` |
| `0x1800DB4A0 sub_1800DB4A0` | `data\fonts\` |
| `0x1800DB4A0 sub_1800DB4A0` | `http://aion_client/data/font/` |
| `0x1800E4040 sub_1800E4040` | `Data\CustomPreset\BodyTypes.xml` |
| `0x1800E84E0 sub_1800E84E0` | `LFWing_001.cgf` |
| `0x1800E84E0 sub_1800E84E0` | `LMWing_001.cgf` |
| `0x1800E84E0 sub_1800E84E0` | `DMWing_001.cgf` |
| `0x1800E84E0 sub_1800E84E0` | `DFWing_001.cgf` |
| `0x1800F04C0 sub_1800F04C0` | `Data\animations\custom_animation.xml` |
| `0x1800F0D50 sub_1800F0D50` | `data\animations\DamageDir.xml` |

完整路径索引见 `data/data_paths_and_resources.csv`。

## 23. 高价值函数短名单

| RVA | 建议名 | 类别 | 价值 |
|---:|---|---|---|
| `0x405100` | `DispatchServerPacket` | network | 服务器包总分发；多数 S_* 包从这里进入 |
| `0x2f90b0` | `RegisterServerPacketNames` | network | S_* 包名/编号注册表 |
| `0x2fcd20` | `RegisterClientPacketNames` | network | C_* 包名/编号注册表 |
| `0x3ce50` | `CEntitySystem_SpawnEntity` | entity | 实体创建/SpawnEntity |
| `0x3d580` | `CEntitySystem_GetEntityById` | entity | 按 Entity ID 查 CEntity |
| `0x3dd40` | `CEntitySystem_QueryEntitiesAroundPoint` | entity | 物理范围查询附近实体 |
| `0x6f30` | `CEntity_SetPosition` | entity | 世界坐标写入路径 |
| `0x6fb0` | `CEntity_GetPositionPtr` | entity | 返回 CEntity+0x4B4 世界坐标指针 |
| `0x7250` | `CEntity_SetAngles` | entity | 实体身体欧拉角写入，非坐标 |
| `0x30340` | `Player_UpdateCamera` | camera | 更新相机 Pitch/Roll/Yaw |
| `0x3268a0` | `SetCurrentTarget` | target | 设置当前锁定目标 |
| `0x326700` | `GetCurrentTargetCharacter` | target | 返回当前目标 Actor |
| `0x417480` | `Handle_S_NPC_CHANGED_TARGET` | combat | 怪物改变目标；写 Actor+0x358 |
| `0x498fe0` | `Actor_SetTargetServerObjectId` | combat | Actor+0x358 当前目标 ServerObjectId |
| `0x39660` | `Handle_S_TARGET_INFO` | combat | 目标 HP/等级信息 |
| `0x465310` | `Actor_SetCurrentAndMaxHp` | combat | 写 Actor+0x11A0/0x11A4 |
| `0x4c4e40` | `Handle_S_MOVE_OBJECT` | movement | 移动包，坐标更新 |
| `0x40bb10` | `Handle_S_PUT_NPC` | npc | NPC/怪物生成包处理 |
| `0x4272a0` | `ParseNpcXmlDataNode` | npc | NPC
pc.xml 单条记录解析 |
| `0x4cab00` | `LoadGatherSourceTable` | gather | data\Gather\gather_src.xml |
| `0x4c2020` | `SpawnGatherSourceFromPacket` | gather | 采集物生成包/对象创建 |
| `0x55a60` | `Handle_S_GATHER` | gather | 本地采集事件 |
| `0x410b70` | `Handle_S_ABNORMAL_STATUS` | status | 当前玩家异常状态包 |
| `0x410d80` | `Handle_S_ABNORMAL_STATUS_OTHER` | status | 其他对象异常状态包 |
| `0x390cf0` | `SkillManager_GetHighestLearnedSkill` | skill | 按 ID 找当前已学最高等级技能 |
| `0x390f60` | `SkillManager_AddLearnedSkill` | skill | 添加已学习技能 |
| `0x5f7580` | `CanUseSkillNow` | skill | 高层技能可用性判断 |
| `0x3910f0` | `SkillManager_SetCooldown` | skill | 写 SkillItem+0x50/0x54 冷却 |
| `0x517870` | `ParseClientSkillRecord` | skill | client_skills.xml 技能静态记录解析 |
| `0x382790` | `InventoryManager_ctor` | inventory | 背包管理器构造，大小约 0xCF0 |
| `0x3886d0` | `InventoryManager_FindItemByInstanceId` | inventory | 按 InstanceId 查物品 |
| `0x40dcc0` | `Handle_S_LOAD_INVENTORY` | inventory | 加载背包列表 |
| `0x333720` | `CreateInventoryItemFromPacket` | inventory | InventoryItem 0x558 构造/解析 |
| `0x38e000` | `InventorySlotToPage` | inventory | slot / 27 页格转换 |
| `0x4a4250` | `Physics_HasClearLine` | physics | 两点无遮挡/射线判断候选 |

## 24. 待验证 / 开放问题

- `CEntity* -> Actor*` 外部只读指针链仍需最终确认；进程内函数 `sub_18049B8A0` 已确认。
- `client_items.xml` 静态物品记录 `0x208` 需要完整字段恢复。
- `SkillRecord 0x7D8` 已有核心字段，但完整 effect/motion/weapon restrictions 仍可继续拆。
- 背包 UI 绝对像素位置需要从 `BindInventoryItemToSlotWidget` 和 UI 控件矩形继续确认。
- 怪物“是否敌对/可攻击”需要结合 NPC 静态 `npc_type/tribe/aggressive` 和运行时关系判断。
- 障碍/LOS 的攻击专用上层调用者需要通过断点确认；底层 RayWorldIntersection 已确认。
- 采集次数 `GatherObject + 0x40` 是高概率候选，但还需运行时采集连续断点确认。

## 25. 附录：C# 常量文件说明
`code/Offsets.cs` 包含本文所有核心 RVA 和偏移的 C# 常量。建议业务代码只引用这个文件；版本更新时只改常量。
