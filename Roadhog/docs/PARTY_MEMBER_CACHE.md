# Roadhog 队伍成员缓存技术记录

记录日期：2026-07-13

来源：同事提供的 IDA 导出分析。本文记录为后续实现 `TeamMonitor` / `TeamSnapshot` 的技术依据；偏移和字段命名仍需要在真实运行环境用只读 probe 验证后再写入正式 adapter。

## 总结

客户端里有一套独立的队员缓存列表，不需要队伍窗口打开，也不要求队员当前在视野内。

后续实现 `TeamMonitor` 时，最稳的方式是合并两层数据：

```text
队伍缓存 PartyMemberRecord
  负责：ServerId、名字、职业、等级、准确 HP/MP、飞行时间、
        区域字段、缓存坐标候选、状态列表

附近实时 Actor
  负责：精确世界坐标、当前目标、动作、飞行状态、实时实体状态
```

队伍缓存适合做通用队伍状态底座；实时 Actor 只作为补充，不应成为读取队友 HP/MP 的前置条件。

## 普通队伍全局地址

以下地址均为 `GameBase + RVA`：

```text
0xD1BAB8  uint32  PartyId / 队伍激活状态候选，非零通常表示处于队伍中
0xD1BABC  uint32  队伍权限/状态 flags 候选
0xD1BAC0  uint32  队长 ServerObjectId，高置信
0xD1BAE8  pointer 队员 std::list 哨兵节点指针
0xD1BAF0  uint64  队员记录数量
```

判断是否有队伍时，不建议只依赖 `0xD1BAB8`。建议组合判断：

```csharp
bool hasParty =
    memory.ReadUInt64(gameBase + 0xD1BAF0) > 0 &&
    memory.ReadPointer(gameBase + 0xD1BAE8) != 0;
```

## 队员链表

链表节点结构：

```text
ListNode +0x00 = next
ListNode +0x08 = prev
ListNode +0x10 = PartyMemberRecord*
```

遍历逻辑：

```text
head = *(GameBase + 0xD1BAE8)
node = *(head + 0x00)

while (node != head)
{
    memberRecord = *(node + 0x10)
    node = *(node + 0x00)
}
```

普通队伍最多 6 人，列表里通常也包含自己。不要直接假设链表第一项就是 UI 上的队员 1；客户端可能把本地角色放在前面。

建议先读取 `ServerId`，再通过 `localServerId` 和 `leaderServerId` 标记 `IsSelf` / `IsLeader`，最后由业务层生成自己的队伍槽位映射。

## PartyMemberRecord

记录大小：

```text
0x85D = 2141 字节
```

这是紧凑结构，会出现 `+0x3B`、`+0x6F` 这种非对齐偏移。

| 偏移 | 类型 | 含义 | 可信度 |
| ---: | --- | --- | --- |
| `+0x00` | `uint32` | 原始队伍字段候选；不能直接当 UI 槽位 | 候选 |
| `+0x04` | `uint32` | 队员 `ServerObjectId` | 已确认 |
| `+0x08` | `uint32` | 最大 HP | 已确认 |
| `+0x0C` | `uint32` | 当前 HP | 已确认 |
| `+0x10` | `uint32` | 最大 MP | 已确认 |
| `+0x14` | `uint32` | 当前 MP | 已确认 |
| `+0x18` | `uint32` | 最大飞行时间，毫秒 | 高 |
| `+0x1C` | `uint32` | 剩余飞行时间，毫秒 | 高 |
| `+0x20` | `uint32` | 区域/指针类原始字段候选；不能直接当地图 ID | 候选 |
| `+0x24` | `uint32` | 区域/指针类原始字段候选；不能直接当地图 ID | 候选 |
| `+0x28` | `float` | 缓存 X 坐标候选 | 强候选 |
| `+0x2C` | `float` | 缓存 Y 坐标候选 | 强候选 |
| `+0x30` | `float` | 缓存 Z 坐标候选 | 强候选 |
| `+0x34` | `uint8` | 职业 `ClassId` | 已确认 |
| `+0x36` | `uint8` | 等级 | 已确认 |
| `+0x37` | `uint8` | 队员状态 flags | 高 |
| `+0x38` | `uint8` | 可飞区域/飞行许可候选 | 候选 |
| `+0x39` | `uint8` | 飞行 flags 候选 | 候选 |
| `+0x3A` | `uint8` | 运行时状态字段 | 未定 |
| `+0x3B` | `wchar[26]` | 队员名字，UTF-16 | 已确认 |
| `+0x6F` | `uint64` | 控制/异常状态掩码 | 高 |
| `+0x77` | `int16` | 异常状态条目数量，最大 112 | 已确认 |
| `+0x79` | `entry[112]` | Buff/Debuff 状态数组 | 已确认 |
| `+0x859` | `uint32` | 状态时间参考 tick | 高 |

## 只读 Live Probe 记录

验证时间：2026-07-13

验证入口：

```powershell
ROADHOG_TEST_MODE=party_probe
VMM_DEVICE=fpga://devindex=0
MEMPROCFS_HOME=C:\Users\GoldGiven\Desktop\script\3
Roadhog.Tests\bin\DebugPartyProbe\Roadhog.Tests.exe
```

运行结果：

```text
Connected to process: Aion.bin (PID 1596)
Module base: Game.dll = 0x48710000
PartyMemberRecords Count=2

Party#01 Name="KiraHa" ServerId=1711614598 ClassId=8 Level=39
  HP=2589/2589 MP=5783/5783 RawAbnormalCount=1 EntryCount=1 PhysicalCount=0
  RawPartyField0=1325966 DataFlags=0x01 CachedPositionCandidate=506.15,1896.19,261.25

Party#02 Name="Jone" ServerId=1711614976 ClassId=10 Level=38
  HP=3316/3316 MP=4373/4637 RawAbnormalCount=3 EntryCount=3 PhysicalCount=0
  RawPartyField0=1325966 DataFlags=0x11 CachedPositionCandidate=493.70,1801.89,207.67
```

本次验证结论：

- `+0x04` ServerId、`+0x08/+0x0C` HP、`+0x10/+0x14` MP、`+0x34` ClassId、`+0x36` Level、`+0x3B` Name 均可读，且数值合理。
- `+0x77/+0x79` 异常状态数量和条目可读，本次分别读到 1 条和 3 条；`PhysicalCount=0` 表示这些条目不是肉体异常。
- 队伍链表本次返回 2 条记录，说明普通小队列表会包含当前客户端视角下的队伍成员；正式逻辑仍要用 `localServerId` 标记 `IsSelf`，不能按链表顺序猜谁是自己。
- `+0x00` 本次两条记录都读到 `1325966`，不是可直接使用的 UI 槽位或 F2-F6 顺序，第一版不要用它做选人映射。
- `+0x20/+0x24` 本次两条记录都读到 `0xD1D8B40`，像原始指针/共享字段，不像稳定地图 ID；同副本/就位判断仍按 live position 距离处理。
- `+0x37` 本次分别为 `0x01` 和 `0x11`。即使 `0x08` 未置位，也能读到异常条目，所以 `0x08` 不能当作“是否存在异常数组”的硬门槛。
- `+0x28/+0x2C/+0x30` 缓存坐标能读到看似合理的数值，但第一版仍只作为诊断字段，不参与追随、同副本、就位、屏幕内可见判断。

### 近距离 LiveActor 验证

验证时间：2026-07-13，同一队友移动到本地角色附近后再次读取。

运行结果：

```text
LiveActorProbe=ok
LocalServerId=1711614598
LocalName="KiraHa"
LocalPosition=506.15,1896.19,261.25
VisiblePlayerActors=9

KiraHa:
  IsSelf=yes
  LiveActor=yes
  DistanceToLocal=0.00
  VisibilityState=ScreenVisible

Jone:
  IsSelf=no
  LiveActor=yes
  Position=520.06,1867.78,261.35
  DistanceToLocal=31.64
  VisibilityState=ScreenVisible
```

本次验证结论：

- 队友进入附近后，可以通过 `PartyMemberRecord +0x04` 的 ServerId 对上 live Actor。
- live Actor 能补出 `Actor` 地址、`CEntity` 地址、实时坐标和当前目标 `LiveTargetId`。
- Jone 到本地角色 KiraHa 的 live position 真实距离为 `31.64m`，小于 50m，按第一版业务规则判定为 `ScreenVisible`。
- 本次缓存坐标和 live 坐标一致，但业务仍以 live Actor/CEntity 坐标为准；缓存坐标只保留诊断用途。

### F2/F3 队员槽位验证

验证时间：2026-07-13，三人队伍：

```text
ListIndex=0  KiraHa   IsSelf=yes  ServerId=1711614598
ListIndex=1  Jone     IsSelf=no   ServerId=1711614976
ListIndex=2  HiApple  IsSelf=no   ServerId=1711615144
```

按键验证：

```text
按 F2 后：
  Local LiveTargetId=1711614976
  命中 Jone

按 F3 后：
  Local LiveTargetId=1711615144
  命中 HiApple
```

本次验证结论：

- 当前普通小队里，按链表顺序排除自己后，可以得到 F2/F3 槽位映射。
- `selectableMembers[0] -> F2`，本次对应 `Jone`。
- `selectableMembers[1] -> F3`，本次对应 `HiApple`。
- 远距离且 `LiveActor=no` 的队友仍可通过 F3 选中；本地角色 `Actor +0x358` 会变成该队友的 `ServerId`。
- 正式实现仍不要使用 `PartyMemberRecord +0x00` 作为槽位字段；它在本轮多次读取中都不是 F2/F3 顺序。

槽位交换验证：

```text
用户在本地队伍 UI 中交换 HiApple 和 Jone 后，再次读取：

ListIndex=0  KiraHa   IsSelf=yes  ServerId=1711614598
ListIndex=1  HiApple  IsSelf=no   ServerId=1711615144
ListIndex=2  Jone     IsSelf=no   ServerId=1711614976

用户本地观察：
  F2 锁 HiApple
```

结论：

- `F2-F6` 是当前客户端本地看到的队伍槽位顺序，不是角色固定属性。
- 当前规则应定义为：读取本地客户端队伍链表，排除自己，剩余成员按顺序映射到 `F2-F6`。
- 换槽位后，链表顺序会跟随本地 UI 槽位变化，因此槽位映射也会变化。
- 每个客户端都应维护自己的本地 `TeamSlotMap`；不要假设所有账号看到的槽位顺序完全一致。

建议缓存策略：

```text
组队完成 / 启动组队模式:
  读取 PartyMemberRecord 链表
  用 localServerId 标记并排除自己
  按剩余顺序生成 ServerId -> F2/F3/F4/F5/F6
  缓存到本账号本地 TeamSlotMap

运行中:
  常规逻辑优先使用缓存
  低频刷新校验队伍人数、ServerId 顺序和成员身份
  队伍人数变化、ServerId 变化、选人失败、治疗目标不对、队伍重组/换槽位时强制刷新
```

缓存分层：

```text
稳定身份缓存:
  ServerId / Name / AccountName / Role / FollowerType

本地槽位映射缓存:
  ServerId -> selectMemberKey(F2-F6)
  这是运行时缓存，不是永久配置
```

加血队员选人建议流程：

```text
想治疗某队友
-> 用 ServerId 找当前 TeamSlotMap
-> 先读取本地当前目标 ServerId
-> 若目标 ServerId == 队友 ServerId，则当前已经选中本体，不再按 F 键，直接释放治疗/解状态技能
-> 按映射得到的 F2/F3/F4/F5/F6
-> 读取本地当前目标 ServerId
-> 若目标 ServerId == 队友 ServerId，则确认选中成功
-> 若目标 ServerId == 该队友宝宝/召唤物 ServerId，则说明选到了宝宝，继续按同一个 F 键重试
-> 再释放治疗/解状态技能
-> 若不一致，刷新 TeamSlotMap 后重试
```

精灵等带宝宝职业需要额外校验：连续按同一个队友 F 键可能在玩家本体和宝宝之间切换。第一版维护技能目标默认是玩家本体，所以释放技能前必须确认本地当前目标 `ServerId == targetMemberServerId`。如果选到了宝宝，不能直接释放玩家治疗/维护技能；应继续按同一个槽位键或按重试策略重新选中本体。超过重试次数仍无法确认本体时，本轮维护失败并记录日志。

### 精灵本体已选中时的治疗 fast path 验证

验证时间：2026-07-14。

测试目的：验证目标队友是精灵星时，如果当前目标已经是玩家本体，维护加血逻辑是否可以跳过 F2-F6，避免重复按队友槽位键导致本体/宝宝切换。

测试环境：

```text
治疗端: script\2，Jone，治愈星
队长端: KiraHa，ClassId=8，精灵星
目标选择键: F2
治疗键: NumPad1
```

关键 probe 输出语义：

```text
Leader=KiraHa
LeaderServerId=1711614598
LeaderClassId=8
LeaderIsSpiritmaster=yes
LocalTargetServerId=1711614598
IsLeaderSelected=yes
HealPressMode=heal_only_current_target
AutoPressStatus="pressed:NumPad1:heal_only_current_target"
```

两轮 60 秒测试结论：

- 当前目标已经是队长本体时，探针没有再按 `F2`，只按 `NumPad1`。
- 第一轮 `AutoPressCount=6`，`AutoPressSuccessCount=6`。
- 第二轮 `AutoPressCount=19`，`AutoPressSuccessCount=19`。
- 两轮都读到 `SawDamage=yes` 和 `SawHealAfterDamage=yes`，说明从 `PartyMemberRecord` 读到队长掉血，并且治疗后能读到血量回升。
- 该逻辑可以作为正式 `TeamSupportLogic` 的第一版规则：先读当前目标，若已等于目标队友本体 `ServerId`，则 `skipSelect=true`，直接释放维护技能；只有当前目标不是目标队友本体时才按 `TeamSlotMap` 对应的 F 键。

## 队员状态 Flags

`PartyMemberRecord + 0x37`：

```text
0x01  成员有效/增加更新标志
0x02  离线、跨区域或不可实时访问候选
0x04  名字数据有效/已携带候选
0x08  abnormal 相关状态候选；不能作为读取异常数组的硬门槛
0x10  特殊状态、不可用或灰显候选
```

`0x02` 和 `0x10` 的精确含义需要分别用队员下线、跨图、不可用状态验证。死亡第一版按当前 HP 为 0 推导，不依赖这些 flags。

2026-07-13 live probe 中，`DataFlags=0x01` 的成员也读到了 `RawAbnormalCount=1`，所以读取 `+0x77/+0x79` 时不要要求 `0x08` 必须置位。

## 身份字段

队员 ServerId：

```csharp
uint serverId = memory.ReadUInt32(memberRecord + 0x04);
```

队长 ServerId：

```csharp
uint leaderServerId = memory.ReadUInt32(gameBase + 0xD1BAC0);
```

判断队长：

```csharp
bool isLeader = serverId == leaderServerId;
```

判断自己：

```csharp
uint localServerId = memory.ReadUInt32(localActor + 0x2C);
bool isSelf = serverId == localServerId;
```

列表里通常包含自己，所以业务上的队友列表应先排除 `IsSelf` 后再按 UI/槽位规则映射。

## HP / MP / 职业 / 等级 / 名字

```csharp
uint maxHp = memory.ReadUInt32(record + 0x08);
uint currentHp = memory.ReadUInt32(record + 0x0C);

uint maxMp = memory.ReadUInt32(record + 0x10);
uint currentMp = memory.ReadUInt32(record + 0x14);

byte classId = memory.ReadByte(record + 0x34);
byte level = memory.ReadByte(record + 0x36);

string name = memory.ReadUnicodeString(record + 0x3B, 26);
```

队伍缓存的 HP/MP 是队伍系统专门维护的数据。即使队员不在视野内、没有实时 `Actor*`，通常仍能读取到队伍面板显示的 HP/MP。

第一版玩家死亡判定直接由当前 HP 推导：

```text
currentHp == 0 -> Dead
currentHp > 0  -> Alive
HP 不可读 / 成员 stale -> Unknown
```

也就是说，正式快照可以保留 `aliveState` 字段，但它应由 `PartyMemberRecord +0x0C` 的当前 HP 派生，不需要先找到额外的独立死亡字段。`PartyMemberRecord +0x37` flags 后续可用于辅助识别离线、跨图或不可用状态，但不作为第一版死亡判定的前置条件。

职业映射草案：

```csharp
public static string GetClassName(byte classId)
{
    return classId switch
    {
        0  => "战士",
        1  => "剑星",
        2  => "守护星",
        3  => "侦察者",
        4  => "杀星",
        5  => "弓星",
        6  => "法师",
        7  => "魔道星",
        8  => "精灵星",
        9  => "祭司",
        10 => "治愈星",
        11 => "护法星",
        _  => $"未知职业({classId})"
    };
}
```

## 队员 Buff / Debuff

队伍记录内携带异常状态：

```text
record +0x77 = 状态数量
record +0x79 = 状态数组
每条 0x12 字节
```

条目结构：

```text
entry +0x00 = raw/source 候选
entry +0x04 = AbnormalId
entry +0x08 = runtime bucket
entry +0x0C = 时间/持续时间 raw
entry +0x10 = 等级/层数
```

状态判断规则：

```text
状态是否存在：按 AbnormalId 判断
状态具体是 Buff 还是 Debuff：查 client_skills.xml
不要只按 entry+0x08 判断
```

状态分类规则：

```text
PartyMemberRecord +0x77/+0x79 只负责告诉我们“有哪些状态条目”
entry +0x04 AbnormalId 用来查 Source/client_skills.xml
entry +0x08 DispelCategory / runtime bucket 只能作为诊断字段，不能单独判定正负面，也不能稳定判定是否可用肉体解除

client_skills.xml:
  target_slot = Buff / 0   -> 正面状态
  target_slot = Chant / 2  -> 正面状态
  target_slot = Boost / 5  -> 正面状态
  target_slot = Debuff / 1 -> 负面状态
  dispel_category = DebuffPhy -> 肉体异常/可用对应解除技能处理
  dispel_category = DebuffMen -> 精神异常/可用精神解除技能处理
  target_relation_restriction = Friend 且有增益效果 -> 倾向正面
  找不到静态表或无法分类 -> Unknown，不触发自动解状态
```

维护加血队员的肉体解状态候选应使用更严格的业务字段：

```text
CleanseCandidate =
  AbnormalId != 0
  && StatusKind == Negative     // 静态表确认是 Debuff
  && XmlDispelCategory == DebuffPhy
```

精神解状态候选单独处理：

```text
MentalCleanseCandidate =
  AbnormalId != 0
  && StatusKind == Negative
  && XmlDispelCategory == DebuffMen
```

也就是说：

- `PhysicalCount` 只是 `DispelCategory == 2` 的原始计数。
- `PhysicalCount > 0` 不等于“队友有需要解除的负面肉体异常”。
- `entry +0x08 DispelCategory` 不能作为第一判断来源；实测负面 `1632` 的 XML 是 `DebuffPhy`，但运行时字段没有进入原来的 `PhysicalCount`。
- `NegativeCount` 表示静态表分类为负面状态的条目数量。
- `CleanseCandidateCount` 才是第一版可以触发 `NumPad7` 的数量。
- `MentalCleanseCandidateCount` 才是第一版可以触发 `NumPad8` 的数量。

### 状态分类 live probe 验证

验证时间：2026-07-15。

测试环境：

```text
运行根目录: C:\Users\GoldGiven\Desktop\script\2
VMM: fpga://devindex=2
PID: 9788
本地角色: Jone，治愈星
队伍人数: 3
静态表: C:\Users\GoldGiven\Desktop\script\2\Source\client_skills.xml
```

当时全队都有一个正面状态，读到的队伍条目均为：

```text
AbnormalId=8232
DispelCategory=2
```

`client_skills.xml` 中 `8232` 的关键信息：

```text
id=8232
name=CH_AuraStatUpAreaSpeedEffect
target_slot=Chant
target_relation_restriction=Friend
effect1_type=StatUp
effect1_reserved13=speed
```

probe 分类结果：

```text
StatusKind=Positive
PositiveCount=1
PositiveIds=8232:L1:Chant
NegativeCount=0
NegativeIds=None
CleanseCandidateCount=0
CleanseCandidateIds=None
NeedsCleanse=no
```

维护探针在自动清除打开时再次验证：

```text
AutoPressCleanse=yes
CleanseCandidateCount=0
NeedsCleanse=no
AutoPressAttempted=no
CleansePressCount=0
```

结论：`8232` 虽然 `DispelCategory=2`，但它是 `target_slot=Chant` 的正面吟唱/增益状态，不能触发 `NumPad7`。第一版正式实现必须按 `AbnormalId + client_skills.xml target_slot` 分类后，再决定是否解状态。

负面状态验证：

同一环境下，让队友 `HiApple` 受到持续伤害状态，probe 读到：

```text
Member=HiApple
NegativeCount=1
NegativeIds=1632:L9:Debuff
PositiveCount=1
PositiveIds=8232:L1:Chant
HP: 3903/3903 -> 3847/3903 -> 3795/3903 -> 3771/3903 -> 3747/3903 -> 3695/3903 -> 3643/3903
```

`client_skills.xml` 中 `1632` 的关键信息：

```text
id=1632
name=EL_EarthGrab_G2
type=Magical
skill_category=SKILLCTG_PHYSICAL_DEBUFF
dispel_category=DebuffPhy
target_slot=Debuff
target_relation_restriction=Enemy
effect2_type=SpellATK
effect2_checktime=3000
```

结论：

- `1632` 可以被明确分类为负面状态。
- HP tick 下降证明它确实是持续伤害/异常状态。
- `CleanseCandidate` 应优先根据 XML 的 `dispel_category=DebuffPhy` 判断，而不是依赖运行时 `entry+0x08`。

### 精神异常状态分类

`client_skills.xml` 里确实存在精神异常类别：

```text
dispel_category=DebuffMen: 208 条
skill_category=SKILLCTG_MENTAL_DEBUFF
target_slot=Debuff
target_relation_restriction=Enemy
```

典型样本：

```text
id=540  name=KN_AbsoluteScare_G1      effect=Fear
id=695  name=RA_ParalyzeArrow_G1      effect=Sleep/StatUp
id=1443 name=WI_SleepingStorm_G1      effect=Sleep/StatUp
id=1454 name=WI_CursedTree_G1         effect=Deform/Sleep
id=1636 name=EL_Fear_G1               effect=Fear/Deform
```

精神解除技能在同一份技能表里表现为：

```text
id=1063 name=PR_CureMind_G1    effect1_type=DispelDebuffMental
id=1064 name=PR_CureMind_G2    effect1_type=DispelDebuffMental
id=1110 name=PR_CureMind_G3    effect1_type=DispelDebuffMental
id=1180 name=PR_MassDispel_G1  effect1_type=DispelDebuffPhysical effect2_type=DispelDebuffMental
id=9910 name=item_potion_cure_mental effect1_type=DispelDebuffMental
```

第一版按键约定：

```text
肉体异常解除: NumPad7
精神异常解除: NumPad8
治疗: NumPad1
```

维护动作优先级：

```text
1. MentalCleanseCandidateCount > 0 -> F2-F6 选中队友本体 -> NumPad8
2. CleanseCandidateCount > 0       -> F2-F6 选中队友本体 -> NumPad7
3. HP 未满                         -> F2-F6 选中队友本体 -> NumPad1
```

这是严格优先级，不是“谁当前可按就按谁”：

```text
只要 MentalCleanseCandidateCount > 0，就不能降级去按 NumPad7 或 NumPad1。
如果 NumPad8 刚按过、动作冷却未到，则等待下一轮继续尝试 NumPad8。

只要 CleanseCandidateCount > 0 且没有精神解除候选，就不能降级去按 NumPad1。
如果 NumPad7 刚按过、动作冷却未到，则等待下一轮继续尝试 NumPad7。
```

精神解除技能等级会影响实际解除结果：

```text
如果精神解除技能等级低于异常等级，可能需要连续按两次 NumPad8。
技能等级提升后，同样精神异常一次 NumPad8 即可解除。
这不改变业务优先级；精神异常存在期间仍然持续尝试 NumPad8，不降级按 NumPad7。
```

响应速度第一版按“快速维护”处理：

```text
队伍维护采样默认约 200ms 一次，允许通过 ROADHOG_TEAM_HEAL_INTERVAL_MS 压到 100ms。
维护动作默认约 300ms 允许一次，允许通过 ROADHOG_TEAM_HEAL_PRESS_INTERVAL_MS 继续压到 100ms。
目标动作冷却默认跟随维护动作间隔；精神异常仍存在时，可以按该冷却连续补 NumPad8。
KMBox 默认按键保持约 45ms，按键间隔约 120ms，可按测试稳定性继续下调。
```

### 清状态 + 加血端到端验证

验证时间：2026-07-15。

测试环境：

```text
运行根目录: C:\Users\GoldGiven\Desktop\script\2
VMM: fpga://devindex=2
PID: 9788
本地角色: Jone，治愈星
目标队友: HiApple，F2
KMBox: 192.168.4.188:49412 / C5440C3D
治疗键: NumPad1
解状态键: NumPad7
```

端到端结果：

```text
sample=1
HiApple HP=3522/3903
NegativeIds=1632:L9:Debuff
CleanseCandidateIds=1632:L9:Debuff
Action=cleanse
Key=F2,NumPad7
Success=yes
Status=pressed:F2,NumPad7:cleanse:target_confirmed:attempt=2

sample=3
CleanseCandidateIds=None
Action=heal
Key=NumPad1
Success=yes
Status=pressed:NumPad1:heal:already_selected
```

后续多次重复验证也成立：

```text
NegativeIds=1360:L9:Debuff -> Action=cleanse -> NumPad7 success
NegativeIds=1632:L9:Debuff -> Action=cleanse -> NumPad7 success
CleanseCandidateIds=None 且 HP 未满 -> Action=heal -> NumPad1 success
```

最终摘要：

```text
SawDamage=yes
SawHealAfterDamage=yes
SawPhysicalAbnormal=yes
SawPhysicalCleared=yes
AutoPressCount=22
AutoPressSuccessCount=22
CleansePressCount=13
CleansePressSuccessCount=13
HealPressCount=9
HealPressSuccessCount=9
```

结论：加血维护队员的第一版核心闭环成立。读取队员状态后，优先按 `MentalCleanseCandidate` 选择队友并释放 `NumPad8`；没有精神解除候选时，再按 `CleanseCandidate` 选择队友并释放 `NumPad7`；清状态候选消失后，如果 HP 未满，再对同一目标释放 `NumPad1`。

### 精神 + 肉体 + 加血三项联测

验证时间：2026-07-15。

测试环境：

```text
运行根目录: C:\Users\GoldGiven\Desktop\script\2
VMM: fpga://devindex=2
PID: 9788
本地角色: Jone，治愈星
目标队友: HiApple，F2
KMBox: 192.168.4.188:49412 / C5440C3D
精神解除键: NumPad8
肉体解除键: NumPad7
治疗键: NumPad1
```

关键样本：

```text
sample=17
HP=3620/3903
NegativeIds=1636:L1:Debuff,1632:L9:Debuff,1360:L9:Debuff
MentalCleanseCandidateIds=1636:L1:Debuff
CleanseCandidateIds=1632:L9:Debuff,1360:L9:Debuff
Action=mental_cleanse
ActionKey=NumPad8
Success=yes

sample=25
MentalCleanseCandidateIds=None
CleanseCandidateIds=1632:L9:Debuff,1360:L9:Debuff
Action=cleanse
ActionKey=NumPad7
Success=yes

sample=29
CleanseCandidateIds=None
MentalCleanseCandidateIds=None
HP=3131/3903
Action=heal
ActionKey=NumPad1
Success=yes
```

最终摘要：

```text
SawDamage=yes
SawHealAfterDamage=yes
SawPhysicalAbnormal=yes
SawPhysicalCleared=yes
SawMentalCleanseCandidate=yes
SawMentalCleanseCleared=yes
AutoPressCount=44
AutoPressSuccessCount=44
MentalCleansePressCount=9
MentalCleansePressSuccessCount=9
CleansePressCount=17
CleansePressSuccessCount=17
HealPressCount=18
HealPressSuccessCount=18
```

结论：三项联测验证通过。正式优先级应保持为 `精神解除 -> 肉体解除 -> 加血`。后续修正：优先级必须严格阻断低优先级动作，精神异常存在时即使 `NumPad8` 暂时处于动作冷却，也不能先按 `NumPad7`。精神解除技能等级较低时，可能需要多次 `NumPad8` 才能解除；这是技能效果问题，不是优先级问题。

读取时应把数量 clamp 到 `0..112`，避免异常数据导致越界。

## 队员位置

## 队员实体加载范围与屏幕可见性

“可视范围”需要拆成两个概念：

```text
实体加载范围：
  能否取得有效 Actor*
  这是读取精确坐标、当前目标、动作等实时信息的前提

组队业务里的屏幕内可见：
  队员有有效 LiveActor/CEntity 坐标
  队友与队长真实距离 <= 50m
  距离来源必须是 LiveActor，不能用 PartyMemberRecord 缓存坐标

严格渲染屏幕可见：
  队员是否真的投影到当前摄像机画面内
  需要 WorldToScreen / ViewProjection 矩阵
```

当前组队模式第一版采用业务定义的“屏幕内可见”：有效 LiveActor 距离 50m 内。严格渲染屏幕可见性不是第一版需求。

判断队员是否进入实体加载范围：

```text
PartyMemberRecord +0x04 = 队员 ServerObjectId
  -> ServerObjectId -> EntityId 映射树
  -> CEntitySystem -> CEntity*
  -> CEntity* -> Actor*
```

客户端函数：

```text
Game.dll +0x171010
sub_180171010(ServerObjectId) -> Actor*
```

该函数会用 `GameBase +0xD21740` 的映射树将 `ServerObjectId` 转成 `EntityId`，再从 `GameBase +0x904690` 的实体系统取得 `CEntity`，最后转成角色 `Actor`。找不到时返回空指针。队伍界面本身也会对每个队员调用此函数：返回非空时使用实时 Actor，返回空时退回队伍缓存信息。

外部只读程序不需要直接调用 `sub_180171010`。更稳的做法是用现有 Actor 遍历结果建立：

```csharp
Dictionary<uint, nuint> actorsByServerId;
```

键为：

```text
Actor +0x2C = ServerObjectId
```

然后用 `PartyMemberRecord +0x04` 的队员 ServerId 查询。

只判断字典里有指针还不够稳，建议校验：

```text
Actor +0x20 = objectType，应为 1
Actor +0x2C = ServerObjectId，应等于队员 ServerId
Actor +0x08 = CEntity*，必须有效
CEntity +0x4E4/+0x4E8/+0x4EC 坐标必须是正常有限数
```

结果解释：

```text
有效 Actor*:
  队员实体已加载
  可读取精确坐标、当前目标、动作、飞行状态、Actor 状态等

无有效 Actor*:
  队员实时实体不在加载范围
  仍是队伍成员，但只能读 PartyMemberRecord 缓存数据
```

不要使用 `PartyMemberRecord +0x37` 的某个位直接判断实体加载范围。该字段混合了离线、跨区域、状态有效等标志，目前不能可靠等同于“是否在附近”。

Actor 是否存在优先级高于距离。距离只用于已经加载的队员：有效 Actor 且 LiveActor 真实距离 <= 50m，才算组队业务里的屏幕内可见。只有缓存坐标时不能判定屏幕内可见。

严格渲染意义上判断“是否在屏幕画面内”需要：

```text
1. 读取队员世界坐标
2. 使用当前摄像机 ViewProjection 矩阵做 WorldToScreen
3. 检查投影深度在摄像机前方
4. 检查 screenX/screenY 是否在屏幕边界内
```

即使投影在屏幕内，也不能证明没有被墙体遮挡；遮挡还需要射线检测或渲染可见性数据，目前没有已坐实的简单 Actor 偏移。

建议状态：

```csharp
public enum PartyMemberVisibilityState
{
    NotLoaded,       // 无有效 Actor，只能读队伍缓存
    LoadedOutOfRange, // Actor 已加载，但真实距离 >50m
    ScreenVisible     // Actor 已加载，且 LiveActor 真实距离 <=50m
}
```

第一版先准确区分：

```text
NotLoaded vs LoadedOutOfRange vs ScreenVisible
```

也就是在队员快照里保存：

```text
isInLoadedRange
visibilityState
actorAddress
entityAddress
distance
```

### 队员在附近，Actor 已加载

这是最可靠的位置来源：

```text
memberRecord +0x04 = ServerObjectId
  -> ServerObjectId 找到 Actor*
  -> Actor +0x08 = CEntity*
  -> CEntity +0x4E4 = X
  -> CEntity +0x4E8 = Y
  -> CEntity +0x4EC = Z
```

客户端内部函数：

```text
sub_180171010(ServerObjectId) -> Actor*
```

它会经由：

```text
GameBase +0xD21740  ServerObjectId -> EntityId
GameBase +0x904690  EntityId -> CEntity*
sub_18049B8A0       CEntity* -> Actor*
```

外部只读程序可以先把已有 Actor 遍历结果建成：

```csharp
Dictionary<uint, nuint> actorByServerId;
```

键为：

```text
Actor +0x2C = ServerObjectId
```

### 队员不在附近

此时可能没有 Actor：

```text
ResolveActor(ServerId) == 0
```

仍可读取队伍缓存坐标候选，但第一版不启用：

```text
record +0x28 cachedXCandidate
record +0x2C cachedYCandidate
record +0x30 cachedZCandidate
```

这些字段不参与同副本、追随、距离、就位或可见性判断。实现命名应保留 `Candidate`，只作为日志/诊断记录，避免误当成可信世界坐标。

## 实时 Actor 可补充的信息

通过 `ServerId -> Actor*` 后可补充：

```text
Actor +0x20   objectType，玩家应为 1
Actor +0x2C   ServerObjectId
Actor +0x3E   等级
Actor +0x40   HP百分比
Actor +0x42   名字
Actor +0x228  职业
Actor +0x2D8  移动/飞行状态 flags
Actor +0x338  控制状态掩码
Actor +0x358  当前目标 ServerObjectId
Actor +0xF18  abnormal begin
Actor +0xF20  abnormal end
Actor +0x11A0 最大HP
Actor +0x11A4 当前HP

Actor +0x08 -> CEntity*
CEntity +0x4E4/+0x4E8/+0x4EC = 精确坐标
```

## 数据优先级

```text
ServerId、名字、职业、等级：
  PartyMemberRecord 为主

准确 HP/MP：
  PartyMemberRecord 为主

队友 Buff/Debuff：
  PartyMemberRecord 状态数组为主，按 AbnormalId 判断

精确世界坐标：
  Live Actor/CEntity 为主

队员不在视野：
  使用 PartyMemberRecord 的 HP/MP、异常状态等缓存信息；缓存坐标只诊断记录，不启用

当前目标、动作、真实飞行状态：
  只能在 Live Actor 已加载时可靠读取
```

## 部队 / 联盟

部队/联盟不是普通队伍链表，使用另一套全局缓存：

```text
GameBase +0xD1BAF8  部队/联盟激活状态候选
GameBase +0xD1BB48  自身子队编号候选
GameBase +0xD1BB50  部队成员 list 哨兵指针
GameBase +0xD1BB58  部队成员数量候选
```

成员记录仍然是同一个 `0x85D` 结构：

```text
record +0x00 = 原始队伍字段候选，不能直接当 UI 槽位
record +0x04 = ServerObjectId
```

当前组队模式第一阶段先聚焦普通 6 人小队；部队/联盟可以作为后续扩展。

## 对 TeamMonitor 的影响

这份信息解决了组队模式中的几个关键点：

- 加血队员读取队友 HP/MP：可从 `PartyMemberRecord` 直接读取。
- 加血队员读取队友异常状态：可从 `PartyMemberRecord +0x77/+0x79` 读取状态数组。
- 玩家死亡：第一版按 `PartyMemberRecord +0x0C` 当前 HP 是否为 0 推导。
- 队长监控队员是否掉线/不可用：可先用队伍 flags、心跳/列表存在性组合判断，但 flags 精确语义仍要验证。
- 队员不在视野内：仍可读取基本队伍状态；但精确坐标、当前目标、动作依赖实时 Actor。
- 队员是否进入客户端实体加载范围：用队员 ServerId 查实时 Actor，并校验 objectType、ServerId、CEntity 指针和坐标有效性。
- 组队业务里的屏幕内可见性：有效 LiveActor 真实距离 <=50m，且不能使用缓存坐标；有效 Actor 但距离 >50m 记为 LoadedOutOfRange。
- 严格渲染屏幕投影：需要 WorldToScreen / 摄像机矩阵，第一版不做。
- 距离规则：只使用 live position；不在附近时可记录 cached position candidate，但不启用到距离逻辑。
- 刷本同副本/就位判断第一版按真实距离处理：队友与队长 live position 距离在 50m 内视为同一副本/已就位；超过 50m 或缺少 live position 时，刷本推进按 NotReady 处理。

## 待验证

- `PartyMemberRecord +0x00` 的真实含义；live probe 已证明它不能直接当 UI 槽位或 F2-F6 顺序。
- `PartyMemberRecord +0x20/+0x24` 的真实含义；live probe 中像原始指针/共享字段，第一版同副本/就位判断不依赖它们。
- `PartyMemberRecord +0x37` 中 `0x02`、`0x10` 在队员下线、跨图、不可用时分别如何变化。
- 严格渲染屏幕投影需要的相机矩阵 / WorldToScreen 数据来源。
- UI 槽位和链表顺序的稳定对应关系。
- 部队/联盟缓存是否需要纳入组队模式第一版。
