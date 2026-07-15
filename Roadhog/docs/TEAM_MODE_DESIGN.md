# Roadhog 组队模式设计记录

记录日期：2026-07-13

本文只记录本轮业务讨论结论，方便后续实现时拆分任务。这里描述的是设计目标和业务约束，不表示当前代码已经实现。

## 目标

新增一个独立的“组队模式”Tab，让账号可以按队伍职责运行：

- 队长：行为基本等同当前普通挂机/半自动/路径等现有战斗模式。
- 队员：不再按普通挂机自己找怪，而是按选择的队员类型执行自己的职责。
- 队员类型由配置决定，不由职业硬编码决定。一个加血职业也可以被设置为输出队员。
- 组队打怪分为野外和刷本，两种模式的队伍完整性要求不同。
- 输出队员允许自卫，但自卫属于防御逻辑，不等同于恢复普通挂机的自主找怪。
- 维护加血队员不自卫；如果维护加血队员受到攻击，由队长优先标记攻击它的怪物，让输出队员优先击杀。
- 队员允许拾取，但必须由开关控制。

组队模式不是简单给现有挂机加几个开关，而应该作为账号级业务模式进入调度层，再把输出、维护、移动、死亡、队伍检测拆开处理。

## UI 草案

新增独立 Tab：组队模式。

基础配置：

- 是否启用组队模式。
- 组队场景：野外 / 刷本。
- 当前账号职责：队长 / 队员。
- 队员类型：输出队员 / 维护加血队员。
- 队长账号选择：队员需要知道跟随哪个队长。
- 队员是否允许拾取。
- 队伍成员槽位和选人按键映射。

距离配置：

- 目标跟随距离。
- 开始追随距离。
- 停止追随距离。
- 过近距离，第一版可以先只记录不处理。
- 队员丢失距离。
- 距离检测间隔。

刷本配置：

- 是否要求所有队员存活。
- 是否要求所有队员在同一副本。
- 同副本/就位真实距离阈值，第一版默认 50m。
- 队员死亡时是否暂停队长推进。
- 队员过远时是否暂停队长推进。
- 队员心跳超时时间。
- 最大队员距离。

死亡配置：

- 队长死亡时：默认全队停止等待。
- 输出队员死亡时：默认只暂停该队员。
- 维护加血队员死亡时：默认只暂停该队员，后续可以加“关键队员死亡全队暂停”。
- 任意队员死亡时是否全队暂停：刷本可默认更保守。

## 角色职责

### 队长

队长基本等同当前普通挂机：

- 可以继续复用现有普通挂机、半自动、路径战斗等逻辑。
- 队长负责找怪、锁怪、攻击、移动、拾取等现有主流程。
- 队长额外发布自己的状态，供队员同步：
  - 心跳。
  - 当前目标。
  - 目标血量。
  - 坐标。
  - 是否战斗中。
  - 是否死亡。

野外模式下，队长可以对队员状态不强依赖。

刷本模式下，队长也必须监控队员信息：

- 队员是否存活。
- 队员是否掉线或心跳过期。
- 队员是否在同一副本。
- 队员是否距离过远。
- 队员是否受到攻击。
- 队伍是否完整。

### 输出队员

输出队员只负责攻击队长正在打的怪。

业务规则：

- 不自己找怪。
- 不自己决定目标。
- 不走完整普通挂机目标选择。
- 队长无目标时停手等待。
- 队长目标丢失时停手等待。
- 队长心跳超时时停手等待。
- 队长死亡时停手等待。
- 选中队长目标后，可以复用现有半自动输出逻辑。
- 输出队员允许自卫。
- 自卫只处理威胁自身或自身宝宝/召唤物的怪，不允许重新变成自主找怪。
- 是否参与拾取由队员拾取开关决定。

输出队员精确同步队长目标的第一版方案：

- 游戏内有“标记怪物”机制。
- 标记怪物可以配置一个按键，由队长在锁定当前目标后触发。
- VMM 从战术标记全局表读取标记目标，技术细节见 `TACTICS_SIGN_TABLE.md`。
- 战术标记表位于 `GameBase +0xD1BA68`，结构是 `uint32 ServerObjectId[16]`。
- `markedTargetId` 第一版定义为指定标记槽里的目标 `ServerObjectId`。
- 输出队员可以通过游戏机制绑定一个“锁定标记怪物”的指定按键。
- 输出队员按下该指定按键后，直接锁定被标记的怪物。
- 因为游戏机制会直接锁定标记怪物，所以队员不需要验证目标 ID 是否等于队长目标。
- 队员只需要确认自己已经锁定到一个怪物。
- 确认已锁定怪物后，输出队员才进入半自动输出。
- 没有锁定到怪物时，输出队员不输出，进入重试或等待。

目标同步不再依赖不可靠的 Tab 选择或附近怪筛选。队长负责“标记当前目标”，队员负责“按指定键锁定标记目标，并确认当前已有怪物锁定”。

建议目标同步流程：

```text
队长锁定怪物
  -> 队长按标记怪物键
  -> VMM 读取 D1BA68[leaderMarkIndex] 得到 markedTargetId
  -> TeamMonitor / TeamTargetSync 发布 markedTargetId
  -> 输出队员按锁定标记怪物键
  -> 输出队员读取自己的 LockedTargetSnapshot
  -> 当前已锁定怪物
  -> 允许半自动输出
```

现场验证结论（2026-07-13）：

- 队长标记怪物后，队员客户端的战术标记表也能读到同一标记。
- 队员客户端 `GameBase +0xD1BA68` 的 `SignSlot#00` 读到 `ServerId=2219430696`，即 `MarkedTargetId=2219430696`。
- 队员按下游戏内“锁定标记怪物”按键后，队员当前目标变为 `CurrentTargetServerId=2219430696`。
- probe 输出 `MatchesCurrentTarget=yes`、`CurrentTargetMatched=yes`。
- 因此输出队员选怪问题第一版已闭环：队员不需要自己筛怪、Tab 找怪或计算队长目标，只需要读标记槽、按锁定标记怪物键、确认当前已锁定怪物，然后复用现有半自动输出。

异常处理：

- 队长当前没有目标：清空 markedTargetId，输出队员停手等待。
- 队长标记失败：输出队员不输出，等待下一次标记。
- `markedTargetId == 0`：没有有效标记目标，输出队员停手等待。
- 队员锁定失败：按配置重试，超过次数后等待。
- 队员按键后仍未锁定怪物：不输出，重新尝试锁定标记目标。
- 标记目标死亡：输出队员停手，等待队长下一个标记目标。
- markedTargetId 过期：输出队员停手，避免打旧目标。

队长标记目标来源优先级：

```text
1. 攻击维护加血队员的怪物
2. 队长当前正在打的普通目标
```

也就是说，维护加血队员被打时，队长应临时切换标记目标，把攻击维护加血队员的怪标记出来。输出队员仍然只需要按“锁定标记怪物”键并输出，不需要理解这是普通目标还是防御目标。

输出队员自卫规则：

- 输出队员允许自卫。
- 队员本体受到攻击，算该队员受到攻击。
- 队员宝宝/召唤物受到攻击，也算该队员受到攻击。
- 这个语义和精灵逻辑类似：宝宝收到攻击和本体收到攻击，都归到该角色本地侧受威胁。
- 自卫目标必须来自“正在攻击该队员本体或宝宝”的怪物。
- 自卫目标不需要是队长标记目标。
- 自卫结束后，输出队员回到“锁定队长标记目标并输出”的主流程。
- 如果队长死亡、队伍 TeamNotReady、或队员自己死亡，自卫也必须停止。

队长也需要检测队员是否受到攻击：

- TeamMonitor 应把每个队员的受攻击状态写入 TeamMemberSnapshot。
- 队长在刷本模式下需要消费这个状态，用于决定是否暂停推进、回头清怪或等待防御处理完成。
- 野外模式下，输出队员被攻击时可以自己自卫，队长不一定暂停。
- 野外模式下，维护加血队员被攻击时仍然不自卫，队长应优先标记攻击它的怪。
- 刷本模式下，队员被攻击可以作为 TeamNotReady 或 TeamNeedsDefense 的原因之一，具体策略由配置决定。

队长标记防御目标的优先级：

- 维护加血队员受到攻击时，队长优先标记攻击维护加血队员的怪物。
- 被标记的防御怪物会成为输出队员优先攻击目标。
- 这个优先级高于队长当前普通打怪目标。
- 防御目标处理完后，队长再回到正常标记当前打怪目标。
- 输出队员自身受到攻击时，可以自己自卫；是否要求队长也切标记，后续可按配置扩展。

### 队员受攻击判定来源

结论：第一版按“怪物当前目标反查”实现。这个思路和现有精灵逻辑一致：先知道本体和宝宝的 `ServerObjectId`，再看哪些怪物的当前目标指向这些 ID。

现有技术依据：

- `Actor +0x358` 是 Actor 当前目标 `ServerObjectId`。
- 现有 `ReadWorldObjects` 已经把怪物 Actor 的 `Actor +0x358` 读成 `WorldObjectSnapshot.TargetServerObjectId`。
- 现有维护防御语义里，`IsTargetingLocalSide` 会把怪物 `TargetServerObjectId` 和本体 `ServerObjectId`、宝宝 `ServerObjectId` 比较。
- 因此组队模式不需要重新找一套“受攻击”来源，可以把这个逻辑从“本地本体/本地宝宝”泛化成“任意队员本体/该队员宝宝”。

第一版泛化流程：

```text
从 PartyMemberRecord 读取队员 bodyServerId
-> 从已加载 Actor/召唤物信息补充 memberPetServerIds
-> 遍历已加载怪物 Actor
-> 读取 monsterTargetId = monsterActor +0x358
-> monsterTargetId == bodyServerId      => 队员本体受攻击
-> monsterTargetId in memberPetServerIds => 队员宝宝受攻击
```

队员本体受攻击：

- 只要拿到队员 `ServerObjectId`，理论上就可以判断。
- 不强制要求队员自己的 Actor 已加载。
- 但要求攻击者怪物 Actor 已加载，因为 `monsterActor +0x358` 必须从怪物实时 Actor 读取。

队员宝宝受攻击：

- 需要先知道该队员宝宝的 `ServerObjectId`。
- 已加载宝宝 Actor 可以通过 `Actor +0xFC` 读取 owner `ServerObjectId`，owner 等于队员 `ServerObjectId` 时，把该宝宝归到这个队员。
- `Actor +0xFA0` 可作为“角色当前召唤宝宝 ServerObjectId”的候选来源；当前现有逻辑主要用于本地角色，扩展到非本地队员前需要只读 probe 验证。
- 拿不到队员宝宝 ID 时，不能可靠判断“怪正在攻击该队员宝宝”，只能判断队员本体是否被锁定。

### 队员宝宝/召唤物归属

结论：第一版不需要退化成“只支持玩家本体”。可以支持“已加载并且归属可确认的队员宝宝/召唤物”。拿不到宝宝归属时，再自动退回只判断队员本体。

现有精灵专用链路已经有可复用基础：

- `ReadSummonedPetRosterAsync` 产出 `SummonedPetRosterSnapshot`。
- `SummonedPetRosterSnapshot.LocalPlayerPet` 表示本地角色宝宝。
- `SummonedPetRosterSnapshot.PartyMemberPets` 表示已识别到的队伍成员宝宝。
- `OwnedSummonedPetSnapshot.OwnerServerObjectId` 是宝宝归属角色的 `ServerObjectId`。
- `OwnedSummonedPetSnapshot.Pet.ServerObjectId` 是宝宝自己的 `ServerObjectId`。

当前 VMM 归属流程：

```text
读取本地 Actor
-> 读取本地 Actor +0xFA0，得到本地当前召唤宝宝 ServerObjectId 候选
-> 读取 PartyMemberRecord 队员 ServerObjectId，建立 owner 表
-> 遍历已加载 Actor
-> 过滤玩家 Actor 和召唤物/怪物类 Actor
-> 对召唤物候选读取 Actor +0xFC ownerServerObjectId
-> ownerServerObjectId 命中 owner 表
-> 归属到 LocalPlayerPet 或 PartyMemberPets
```

归属证据来源：

```text
owner
  召唤物 Actor +0xFC 指向某个玩家/队员 ServerObjectId

local-link
  本地角色 Actor +0xFA0 指向该召唤物 ServerObjectId
  当前主要用于本地角色宝宝确认

static-summon-pet
  静态 NPC 数据识别为召唤物类型
```

组队模式第一版采用的规则：

- 队员宝宝集合来自 `PartyMemberPets`，按 `OwnerServerObjectId` 分组。
- `memberPetServerIds = PartyMemberPets where OwnerServerObjectId == member.ServerObjectId select Pet.ServerObjectId`。
- 只有 `Pet.IsSummoned == true` 且 `Pet.ServerObjectId != 0` 的记录才进入受攻击判定集合。
- 如果同一队员识别到多个召唤物，全部加入 `memberPetServerIds`，受攻击反查时任意一个被怪物锁定都算该队员 `petUnderAttack=true`。
- 如果没有识别到宝宝，不代表队员一定没有宝宝，只代表当前客户端没有可确认的宝宝归属；此时 `petServerIds` 为空，受攻击判定退回本体。
- 队长保护维护加血队员时，只能保护队长客户端已加载并确认归属的队员宝宝。
- 输出队员自卫时，可以优先使用自己客户端读取到的本地宝宝归属；这比队长视角更可靠。

有效性边界：

- `Actor +0xFC` 归属判断要求宝宝/召唤物 Actor 已加载。
- 队员本体 Actor 不一定要加载，因为 owner 表可以来自队伍缓存。
- `Actor +0xFA0` 当前不要直接假设对所有非本地队员都可读；扩展前需要 probe 验证。
- 静态数据只能辅助判断“这是召唤物类型”，真正归属仍以 owner `ServerObjectId` 为准。
- 日志里要保留 `petServerIds` 和归属证据，避免把普通怪物误当成队员宝宝。

攻击者选择规则：

- `underAttack = selfUnderAttack || petUnderAttack`。
- `selfUnderAttack` 表示有怪物当前目标等于队员本体 `ServerObjectId`。
- `petUnderAttack` 表示有怪物当前目标等于该队员任一已确认宝宝 `ServerObjectId`。
- `attackerTargetIds` 记录所有命中的怪物 `ServerObjectId`。
- `attackerTargetId` 是第一优先攻击者，用于队长标记防御目标。
- 多个怪同时攻击同一队员时，优先选择离被攻击队员最近的怪；拿不到队员实时坐标时，选择离队长最近的怪；仍无法排序时按 `ServerObjectId` 稳定排序。

有效性边界：

- 该判定只对已加载怪物 Actor 有效。
- 怪物未进入客户端实体加载范围时，读不到它的 `Actor +0x358`，不能确认它正在攻击谁。
- 怪物 `TargetServerObjectId == 0` 时，不判定为攻击队员。
- 队员离线、死亡、队伍缓存 stale 时，不应仅凭旧 `ServerObjectId` 触发防御，先走 TeamNotReady / Dead / Lost 逻辑。
- 对维护加血队员，队长消费 `attackerTargetId` 后按战术标记键把防御怪标记出来，输出队员仍然只锁定标记怪物。

### 维护加血队员

维护加血队员不参与输出战斗，也就是不打怪。

业务规则：

- 不攻击怪物。
- 不走普通输出技能链。
- 被攻击时不自卫。
- 被攻击时只把自身 underAttack 状态交给 TeamMonitor。
- 队长检测到维护加血队员 underAttack 后，优先标记攻击该维护加血队员的怪物。
- 输出队员通过标记目标机制优先击杀该防御目标。
- 主要维护队伍成员血量、蓝量、肉体异常状态等信息。
- 可以按规则治疗、解异常、保护或回蓝。
- 自身也需要基础自维护。
- 是否需要跟随队长，由距离模块统一处理，不直接写在加血逻辑里。
- 是否参与拾取由队员拾取开关决定。

解异常的业务门槛：

- 不能只看 `PartyMemberRecord` 状态条目的 `DispelCategory == 2`。
- 运行时 `entry+0x08 DispelCategory` 只能当诊断字段；它可能把正面吟唱读成 `2`，也可能漏掉 XML 里明确是 `DebuffPhy` 的负面状态。
- 必须用 `AbnormalId` 查 `client_skills.xml`，按 `target_slot` 分类。
- `Buff` / `Chant` / `Boost` 归为正面状态，不能触发解异常。
- `Debuff` 归为负面状态。
- `client_skills.xml` 里的 `dispel_category=DebuffPhy` 才是肉体异常解除候选的主要判断来源。
- `client_skills.xml` 里的 `dispel_category=DebuffMen` 是精神异常解除候选，需要用精神解除技能单独处理。
- 找不到静态表或无法分类时按 `Unknown` 处理，第一版不要自动解。
- 只有 `StatusKind == Negative` 且 `XmlDispelCategory == DebuffPhy` 的条目，才进入 `CleanseCandidate`，再按对应 F 键选中队友本体并释放解状态技能。
- 只有 `StatusKind == Negative` 且 `XmlDispelCategory == DebuffMen` 的条目，才进入 `MentalCleanseCandidate`，再按对应 F 键选中队友本体并释放精神解除技能。

已验证案例：2026-07-15 在 `script\2` 三人队伍中，全队都有 `AbnormalId=8232`，运行时条目 `DispelCategory=2`。静态表显示 `target_slot=Chant`、`target_relation_restriction=Friend`、`effect1_type=StatUp`，probe 分类为 `Positive`，`CleanseCandidateCount=0`。自动清除开启时没有按 `NumPad7`，这是正确行为。

持续伤害负面案例：同一环境下，让队友 `HiApple` 中 `AbnormalId=1632`，probe 读到 `NegativeIds=1632:L9:Debuff`，同时 HP 按 tick 下降。静态表显示 `name=EL_EarthGrab_G2`、`skill_category=SKILLCTG_PHYSICAL_DEBUFF`、`dispel_category=DebuffPhy`、`target_slot=Debuff`、`target_relation_restriction=Enemy`。这个样本证明正负面分类可以闭环，同时也证明肉体解除候选应优先查 XML 的 `dispel_category`。

精神异常分类：`client_skills.xml` 中 `dispel_category=DebuffMen` 有 208 条，典型是恐惧、睡眠、变形类状态，例如 `KN_AbsoluteScare_G1`、`RA_ParalyzeArrow_G1`、`WI_SleepingStorm_G1`、`EL_Fear_G1`。对应解除技能表现为 `effect*_type=DispelDebuffMental`，例如 `PR_CureMind_G1/G2/G3` 和 `PR_MassDispel_G1`。第一版约定精神解除按 `NumPad8`。

清状态 + 加血端到端验证：2026-07-15 使用 `script\2` 治愈星 `Jone`，KMBox `192.168.4.188:49412 / C5440C3D`，队友 `HiApple` 对应 `F2`。当 `HiApple` 同时 `HP=3522/3903` 且 `CleanseCandidateIds=1632:L9:Debuff` 时，probe 先执行 `Action=cleanse`，按键 `F2,NumPad7`，结果 `Success=yes`。清状态候选消失后，继续执行 `Action=heal`，按 `NumPad1`，结果 `Success=yes`。最终 120 秒测试统计为 `CleansePressCount=13 / CleansePressSuccessCount=13`、`HealPressCount=9 / HealPressSuccessCount=9`、`SawDamage=yes`、`SawHealAfterDamage=yes`、`SawPhysicalCleared=yes`。

第一版正式业务顺序：

```text
if team member has CleanseCandidate:
  select member body by F2-F6
  press physical cleanse key NumPad7
else if team member has MentalCleanseCandidate:
  select member body by F2-F6
  press mental cleanse key NumPad8
else if team member HP is below max:
  select member body by F2-F6
  press heal key NumPad1
```

加血队员选中具体队友的第一版方案：

- 一个小队最多 6 人。
- F1 选中自己。
- F2-F6 用于选中 5 个队友槽位。
- 维护规则应按队伍槽位配置，执行前先按对应功能键选中目标队友。
- 按 F 键后必须读取本地当前目标 `ServerId`，确认选中的是目标队友本体，再释放治疗、解状态、保护或回蓝技能。
- 不能只按一次 F 键就默认成功。精灵等带宝宝职业可能出现连续按同一个队友 F 键时，在角色本体和宝宝/召唤物之间切换。
- 第一版维护技能目标默认是“玩家本体”，不是队友宝宝。若当前目标 `ServerId` 不等于目标队友 `ServerId`，即使它可能是该队友宝宝，也不能释放玩家治疗/维护技能。
- 选人前要先读一次本地当前目标。若当前目标已经等于目标队友本体 `ServerId`，不要再按 F 键，直接释放维护技能。这个 fast path 对精灵等带宝宝职业很重要，可以避免重复按 F2-F6 导致本体/宝宝来回切换。

槽位映射草案：

```text
self       -> F1
member1    -> F2
member2    -> F3
member3    -> F4
member4    -> F5
member5    -> F6
```

选人确认流程：

```text
想维护某个队友本体
  -> 用目标队友 ServerId 查 TeamSlotMap，得到 F2-F6
  -> 如果当前目标已经是目标队友 ServerId，直接进入释放技能
  -> 否则按对应 F 键
  -> 等待短暂确认窗口
  -> 读取本地当前目标 ServerId
  -> 等于目标队友 ServerId：选中成功，可以释放维护技能
  -> 等于该队友宝宝/召唤物 ServerId：说明选到了宝宝，继续按同一个 F 键重试，直到选回玩家本体
  -> 等于其他目标或 0：刷新 TeamSlotMap 或重试
  -> 超过重试次数仍不匹配：本轮不释放技能，避免奶错目标
```

已验证的精灵队长/本体锁定 fast path：

```text
targetMemberServerId = 队长 KiraHa ServerId=1711614598
targetMemberClassId = 8  // 精灵星
localCurrentTargetServerId = 1711614598

因为 localCurrentTargetServerId == targetMemberServerId:
  不按 F2
  直接按治疗键 NumPad1
  日志模式: heal_only_current_target
```

2026-07-14 使用 `script\2` 的治疗角色 `Jone` 做 60 秒 live probe：

- 队长 `KiraHa` 被读取为 `LeaderClassId=8`，即精灵星。
- 当前目标持续等于队长本体：`LocalTargetServerId == LeaderServerId`。
- 两轮 60 秒测试中，自动治疗分别执行 6 次和 19 次，`AutoPressSuccessCount` 均等于 `AutoPressCount`。
- 每次按键状态都是 `pressed:NumPad1:heal_only_current_target`，没有再按 `F2`。
- probe 均看到 `SawDamage=yes` 和 `SawHealAfterDamage=yes`，说明掉血后确实触发了治疗并读到了血量回升。

结论：维护加血逻辑应先判断“当前是否已经锁定目标队友本体”。若已经锁定，直接释放维护技能；不要为了保险重复按 F 键。正式实现中这条规则不只适用于队长，也适用于任意目标队友。

后续仍需要确认读取队友状态的底层方案。设计上需要预留：

- 读取队伍成员血量。
- 读取队伍成员异常状态。
- 对指定队友释放维护技能。

## 队员和队长距离

距离控制应作为独立的组队移动/站位模块，不混进输出或加血逻辑。

队员追随队长期间，根据配置距离执行。

不建议做成精确锁死一个固定距离，而是做成距离带，避免抖动：

```text
目标跟随距离：12m
开始追随距离：18m
停止追随距离：12m
过近距离：5m
```

示例规则：

- 队员距离队长超过 18m：开始追随。
- 追到 12m 以内：停止追随。
- 12m 到 18m 之间：保持当前状态，不频繁小碎步。
- 小于 5m：第一版可以先不处理，后续再考虑拉开距离。

关键原则：

- 队员不是追队长脚底，而是追到配置的安全距离。
- 队长小范围转身、打怪、拾取时，队员不能每个 tick 都追最新坐标。
- 能执行当前职责时，不要因为轻微距离变化打断职责。

建议状态：

```text
InBand        距离合适，执行当前职责
CatchUp       离队长太远，追随队长
CombatHold    战斗中距离可接受，不乱移动
Reposition    战斗中技能/治疗距离不满足，短距离补位
LostLeader    队长距离/心跳异常，停止输出和维护，等待恢复
```

队长移动中：

- 队员追随队长。

队长停下且无战斗：

- 队员进入队长附近站位。

队长战斗中：

- 输出队员：能打到队长目标就站住输出，打不到再靠近队长或目标。
- 维护加血队员：能奶到目标队友就站住维护，奶不到再靠近队长或队伍中心。

队长拾取中：

- 队员不要贴着队长抢位置。
- 第一版可以保持等待或轻微跟随。
- 如果队员拾取开关开启，队员可以参与拾取，但仍要服从距离、死亡、防御和队伍状态门槛。

队员拾取规则：

- 队员允许拾取。
- 是否允许拾取由开关控制。
- 开关关闭时，队员不执行拾取动作。
- 开关开启时，队员可以在安全状态下参与拾取。
- 队长死亡时，队员不拾取。
- 队员自己死亡时，不拾取。
- TeamNotReady 时，不拾取。
- 维护加血队员受到攻击且防御目标未处理时，不拾取。
- 输出队员正在自卫时，不拾取。
- 队员距离队长过远、正在 CatchUp 时，不拾取。
- 拾取不能打断更高优先级的输出、防御和维护动作。

距离超大：

- 队员停止输出/治疗按键。
- 进入追随恢复。
- 避免边跑边乱放技能。

## 死亡处理

第一版死亡判定：

```text
currentHp == 0 -> Dead
currentHp > 0  -> Alive
HP 不可读 / 成员 stale -> Unknown
```

这里的 HP 优先来自 `PartyMemberRecord +0x0C` 的当前 HP。`aliveState` 应作为 TeamSnapshot 里的派生字段保留，方便队长、队员和策略层统一消费；但不需要额外读取独立的 `IsDead` / `IsAlive` 字段。

### 队长死亡

队长死亡时，队员进入统一的 LeaderDead 状态。

默认规则：

- 输出队员立刻停手。
- 输出队员不再攻击队长目标。
- 维护加血队员等待，不执行复活/救援技能。
- 没有复活能力时，所有队员停止战斗，等待队长恢复。
- 队员不要自动接管队长职责。
- 队员不要自己找怪。
- 队长复活并恢复心跳后，队员重新进入追随/输出/维护状态。

第一版建议：

```text
队长死亡 -> 队员全部停手等待
队长复活 -> 队员重新追随队长
```

### 队员死亡

队员死亡默认不影响队长继续挂机，除非配置开启“队员死亡全队暂停”。

建议配置：

```text
Ignore         队长继续，死亡队员等待手动/外部恢复
PauseFollower 只暂停死亡队员
PauseTeam     任意队员死亡，全队暂停
```

默认建议：

- 输出队员死亡：只暂停这个队员，队长继续。
- 维护加血队员死亡：第一版也只暂停自己。
- 后续可以把维护加血队员标记为关键队员，关键队员死亡时全队暂停。

抽象状态：

```text
LeaderAlive + FollowerAlive -> 正常组队逻辑
LeaderDead                  -> 全队停止输出，等待恢复
FollowerDead                -> 该队员停止逻辑，按配置决定是否通知队长暂停
LeaderLost                  -> 心跳/距离异常，队员停止输出并追随或等待
```

## 野外和刷本

### 野外模式

野外模式相对宽松。

规则倾向：

- 队长按普通挂机运行。
- 队员关注队长状态：距离、目标、队长是否死亡、队长是否掉线。
- 队员之间不强依赖。
- 输出队员不需要关心其他输出队员状态。
- 维护加血队员需要读取队友血量和异常，但这是维护需求，不是所有队员的强制门槛。

默认处理：

```text
队长死         -> 队员停止
输出队员死     -> 只停自己
加血队员死     -> 可配置是否全队停止
普通队员过远   -> 自己追队长，不影响队长
```

### 刷本模式

刷本模式更保守，队伍完整性是推进条件。

规则倾向：

- 队长也要监控所有队员。
- 队员也要实时或准实时监控其他队员。
- 队伍不完整时，队长不能继续无脑推进。
- 有人死亡、掉线、没进本、距离过远时，进入 TeamNotReady。
- TeamNotReady 后根据配置暂停、等待、回收站位或停止。

默认处理：

```text
队长死               -> 全队停止
任意队员死           -> 队长停止推进，按配置等待/暂停；第一版不复活/救援
任意队员掉线         -> 队长停止推进，队伍等待/停止推进
任意队员真实距离 >50m -> 视为不在同一副本/未就位，全队停止推进
任意队员距离过远     -> 队长暂停推进，队员追随归队
```

同一副本判断第一版规则：

- 用真实距离判断，不优先依赖地图/区域字段。
- 真实距离来自实时 Actor/CEntity 坐标，也就是 `positionSource == LiveActor`。
- 队友与队长真实距离在 50m 范围内，视为同一副本/已就位。
- 队友与队长真实距离超过 50m，视为不在同一副本或未就位，刷本队长停止推进。
- 拿不到实时 Actor/CEntity 坐标时，不肯定判定为同一副本；刷本模式下先按 Unknown/TeamNotReady 处理。
- `PartyMemberRecord` 缓存坐标候选第一版明确不启用，不用于同副本、追随、距离、就位或可见性判断，只作为诊断记录。

## 通用队伍检测

队伍检测应该是通用模块，不应该只属于队长、输出队员或加血队员。

建议新增 TeamMonitor / 队伍状态监控器。

TeamMonitor 不负责：

- 打怪。
- 加血。
- 移动。
- 死亡恢复。
- 决定职业策略。

TeamMonitor 只负责产出统一队伍快照。

队伍状态读取的技术依据记录在 `PARTY_MEMBER_CACHE.md`。当前结论是：客户端存在独立的 `PartyMemberRecord` 队员缓存列表，不需要队伍窗口打开，也不要求队员在视野内。`TeamMonitor` 应优先读取队伍缓存，再用附近实时 Actor 补充精确坐标、当前目标和动作。

数据来源分层：

```text
PartyMemberRecord
  ServerId、名字、职业、等级、HP/MP、飞行时间、异常状态、缓存坐标候选

Live Actor / CEntity
  精确世界坐标、当前目标、动作、实时飞行/实体状态
```

队友 HP/MP 和队友异常状态可以从队伍缓存读取；偏移和字段命名需要通过只读 probe 验证后再写进正式 adapter。

“可视范围”在设计里拆成两层：

```text
实体加载范围：
  能否用队员 ServerId 找到并校验有效 Actor*
  第一版用于判断是否可读取精确坐标、当前目标、动作等实时信息

组队业务里的屏幕内可见：
  队员有有效 LiveActor/CEntity 坐标
  队友与队长真实距离 <= 50m
  距离来源必须是 LiveActor，不允许使用 PartyMemberRecord 缓存坐标
```

第一版按上面的业务定义判断屏幕内可见。严格渲染意义上的屏幕投影、摄像机前方、边界内、墙体遮挡等不做。

### TeamSnapshot 草案

```text
TeamSnapshot
  mode: Wild / Dungeon
  leader
  members
  teamReady
  reason: None / MemberDead / MemberLost / OutOfRange / NotSameInstance / MemberUnderAttack
  updatedAt
```

### TeamMemberSnapshot 草案

```text
TeamMemberSnapshot
  accountName
  serverId
  partySlot
  selectMemberKey
  slotMapSource: LocalPartyList
  slotMapCachedAt
  slotMapVerifiedAt
  role
  followerType
  heartbeat
  classId
  className
  level
  mapId / instanceId
  areaField0 / areaField1
  position
  positionSource: LiveActor / Unknown
  cachedPositionCandidate: diagnostic only
  hasLiveActor
  isInLoadedRange
  visibilityState: NotLoaded / LoadedOutOfRange / ScreenVisible / Unknown
  actorAddress
  entityAddress
  hpPercent
  maxHp
  currentHp
  mpPercent
  maxMp
  currentMp
  aliveState: Alive / Dead / Unknown
  aliveStateSource: Hp / Unknown
  partyFlags
  abnormalStatuses
  currentTargetId
  liveTargetServerId
  markedTargetId
  markedTargetSignIndex
  markedTargetResourceName
  petServerIds
  petOwnershipSource
  underAttack
  selfUnderAttack
  petUnderAttack
  attackerTargetId
  attackerTargetIds
  underAttackSource: Body / Pet / Mixed / Unknown
  distanceToLeader
  distanceSource: LiveActor / Unknown
  sameInstanceState: Same / NotSame / Unknown
  inCombat
  stale
```

所有业务都读同一份 TeamSnapshot：

- 队长：判断刷本能不能继续推进。
- 输出队员：判断能不能协助队长打怪。
- 维护加血队员：判断谁需要治疗、谁需要解状态。
- 距离模块：判断谁要追随、谁离队。
- 死亡模块：判断队长死、队员死、是否全队暂停。

关键原则：

- 检测层只给事实，不做职业决策。
- 例如检测层只说“队员 A HP 32%”、“队员 B Dead”、“队员 C 距离队长 45m”。
- 对受攻击状态，检测层只说“队员 A underAttack=true”、“攻击来源 targetId=xxx”、“触发源是本体或宝宝”。
- 是否加血、是否暂停、是否继续刷本，由上层策略决定。
- 不要求队伍窗口打开；队伍缓存是数据来源，UI 只是显示层。
- 队员不在视野内时，仍应读取队伍缓存中的 HP/MP 和异常状态；精确坐标、当前目标和动作则需要实时 Actor。
- 实体加载范围以有效 Actor 为准：用队员 ServerId 查 Actor 后，还要校验 objectType、ServerId、CEntity 指针和坐标有效性。
- 组队业务里的屏幕内可见性第一版按真实距离判断：有效 LiveActor 距离 <= 50m，且距离不是从缓存坐标得来。
- 有效 Actor 但真实距离 > 50m 时，记为 `LoadedOutOfRange`，不算屏幕内可见。
- 只有缓存坐标时，不能判定屏幕内可见，也不能用于追随、同副本或就位判断。
- 玩家死亡第一版按 HP 判断：`currentHp == 0` 视为死亡；`currentHp > 0` 视为存活。`aliveState` 是从 HP 推导出的快照字段，不需要额外读取独立 `IsDead` / `IsAlive`。
- 如果 HP 数据无效、成员记录不可读或队员状态 stale，则 `aliveState = Unknown`，不要误判为死亡。

### 采集一致性

不要让每个逻辑自己读一次队友状态。

不要出现：

- 加血逻辑读一次。
- 队长推进逻辑读一次。
- 距离逻辑又读一次。

这样会慢，也会导致同一个 tick 里看到的状态不一致。

建议：

- TeamMonitor 按固定间隔统一采集一次。
- 所有模块消费同一份快照。
- 快照带时间戳和 stale 标记。
- 读不到时不要猜，状态应变为 Unknown 或 Stale。

建议配置：

```text
teamMonitorIntervalMs
teamMemberStaleTimeoutMs
teamMemberLostTimeoutMs
```

## 建议的运行结构

不要复制一整套普通挂机逻辑。

建议在账号级调度层分流：

```text
DefaultAccountWorkerLoop
  ├─ Normal / Stationary / Path: 现有逻辑
  ├─ TeamLeader: 现有普通挂机 + 发布队长状态 + 刷本队伍门槛
  ├─ TeamDpsFollower: 同步队长目标 + 半自动输出
  └─ TeamSupportFollower: 队伍维护规则 + 自身维护 + 不攻击
```

职责拆分：

```text
TeamMonitor        -> 采集队伍事实，产出 TeamSnapshot
TeamDistanceLogic  -> 判断追随、补位、丢失
TeamDeathPolicy    -> 判断队长死、队员死、是否暂停
TeamTargetSync     -> 输出队员同步队长目标
TeamDefenseLogic   -> 输出队员自卫、队长识别队员受攻击、队长优先标记攻击加血队员的怪
TeamSupportLogic   -> 维护加血队员处理 HP/MP/异常
```

## 配置草案

```text
TeamModeSettings
  enabled
  scene: Wild / Dungeon
  role: Leader / Follower
  followerType: Dps / Support
  leaderAccountName
  markTargetKey
  leaderMarkIndex
  lockMarkedTargetKey
  teamId
  allowLoot
  followerLootEnabled
  allowSelfDefense
  leaderPausesWhenMemberUnderAttack
  memberUnderAttackAction: Ignore / SelfDefense / PauseLeader / TeamDefense
  supportUnderAttackAction: LeaderMarkAttacker

TeamDistanceSettings
  followTargetDistance
  followStartDistance
  followStopDistance
  tooCloseDistance
  lostLeaderDistance
  catchUpStopDistance
  distanceCheckIntervalMs

DungeonRules
  requireAllMembersAlive
  requireAllMembersInSameInstance
  sameInstanceDistanceMeters: 50
  pauseLeaderWhenMemberDead
  stopProgressWhenAnyMemberLost
  pauseLeaderWhenMemberOutOfRange
  memberLostTimeoutMs
  maxMemberDistance

PartyMaintenanceRules
  memberServerId / accountName
  selectMemberKey: runtime TeamSlotMap 生成，不作为永久固定配置
  condition: hpBelow / mpBelow / abnormalPresent
  threshold
  skillKey
  cooldownMs
  confirmWindowMs
```

## 本地槽位缓存

`F2-F6` 选人按键只表示当前客户端本地看到的队伍槽位顺序，不是角色固定属性。

已验证规则：

```text
读取本地客户端 PartyMemberRecord 链表
-> 用 localServerId 标记 IsSelf
-> 排除自己
-> 剩余成员保持链表顺序
-> selectableMembers[0] -> F2
-> selectableMembers[1] -> F3
-> selectableMembers[2] -> F4
-> selectableMembers[3] -> F5
-> selectableMembers[4] -> F6
```

换队伍槽位后，链表顺序会跟随变化，所以每个账号都要维护自己的本地 `TeamSlotMap`。

第一版可以在组队完成或启动组队模式时生成并缓存：

```text
TeamSlotMap
  ownerAccountName
  ownerServerId
  entries:
    memberServerId
    memberName
    selectMemberKey
    listIndex
  cachedAt
  verifiedAt
```

缓存不是永久配置。运行中可以低频校验，队伍人数变化、ServerId 顺序变化、选人失败、治疗目标校验失败或队伍重组/换槽位时强制刷新。

加血队员选人时不要直接保存“HiApple=F2”这种永久配置，而应保存目标身份，然后从当前 `TeamSlotMap` 查按键：

```text
targetMemberServerId
-> TeamSlotMap[targetMemberServerId]
-> read local current target ServerId
-> already equals targetMemberServerId => skip select key, cast maintenance skill directly
-> press selectMemberKey
-> read local current target ServerId
-> equals targetMemberServerId => selected
-> equals targetMemberPetServerId => selected pet, press same key again / retry until body selected
-> mismatch => refresh TeamSlotMap and retry
```

精灵队友或其他带宝宝职业需要特别注意：同一个 F 键可能在队友本体和宝宝之间切换。因此 `TeamSupportLogic` 的目标选择函数不能只返回“按键已发送”，必须返回“目标本体已确认选中”。正式实现建议抽成通用方法：

```text
SelectPartyMemberBodyAsync(targetMemberServerId)
  inputs: TeamSlotMap, PartyMemberPets
  output: AlreadySelected / Selected / SelectedPet / WrongTarget / Failed
```

## 日志草案

第一版就需要清晰日志，否则组队问题很难排查。

建议事件：

```text
team.monitor.snapshot
team.monitor.member_stale
team.monitor.member_lost
team.monitor.member_loaded_range
team.monitor.member_not_loaded
team.monitor.member_actor_invalid
team.monitor.same_instance
team.monitor.same_instance_unknown
team.monitor.not_same_instance
team.leader.heartbeat
team.leader.team_not_ready
team.follower.leader_lost
team.follower.distance.catch_up
team.follower.distance.in_band
team.follower.target_sync.started
team.follower.target_sync.locked
team.follower.target_sync.failed
team.follower.target_sync.marked_target_expired
team.dps.attack_allowed
team.dps.attack_blocked
team.defense.member_under_attack
team.defense.self_under_attack
team.defense.pet_under_attack
team.defense.support_under_attack
team.defense.self_defense_started
team.defense.self_defense_finished
team.leader.member_under_attack
team.leader.mark_support_attacker
team.leader.defense_mark_cleared
team.follower.loot_allowed
team.follower.loot_blocked
team.follower.loot_started
team.follower.loot_finished
team.support.select_member
team.support.select_member_failed
team.support.member_status_read
team.support.heal_pressed
team.support.status_cleanse_pressed
team.death.leader_dead
team.death.follower_dead
```

日志需要带上：

- accountName
- role
- followerType
- leaderAccountName
- teamId
- targetId
- markedTargetId
- markedTargetSignIndex
- markedTargetResourceName
- memberName
- petServerIds
- petOwnershipSource
- underAttack
- attackerTargetId
- attackerTargetIds
- underAttackSource
- distance
- hpPercent
- reason

## 第一版建议落地顺序

1. 先做组队配置和独立 Tab。
2. 做通用 TeamSnapshot / TeamMonitor 数据结构。
3. 做队长心跳和状态发布。
4. 做队员读取队长状态。
5. 做距离带追随逻辑，只解决“队员按配置距离跟队长”。
6. 做队长标记目标、VMM 读取 markedTargetId、队员锁定标记目标的同步闭环。
7. 做输出队员“只攻击队长标记目标”的闭环。
8. 做队员受攻击检测，把本体/宝宝受攻击都归为该队员 underAttack。
9. 做输出队员自卫闭环。
10. 做维护加血队员受攻击时队长优先标记攻击者的闭环。
11. 做队员拾取开关和安全门槛。
12. 做队长死亡、队员死亡的保守处理。
13. 做刷本 TeamNotReady 门槛。
14. 基础 `PartyMemberRecord` 只读 probe 已完成；正式实现时把已验证字段接入 adapter，继续验证 flags、UI 槽位顺序和区域原始字段。
15. 做队员实体加载范围判断：ServerId 查 Actor，并校验 objectType、ServerId、CEntity 和坐标。
16. 做加血队员 F1-F6 选人按键和队伍槽位映射。
17. 做维护加血队员 HP 维护。
18. 后续再扩展异常状态维护、复活/救援、严格渲染屏幕投影、关键队员、复杂队形。

第一版最稳目标：

```text
队长正常挂机。
输出队员稳定跟随队长。
输出队员只打队长正在打的怪。
输出队员允许对攻击自身或宝宝的怪进行自卫。
队长能看到队员是否受到攻击。
维护加血队员不自卫；队长会优先标记攻击维护加血队员的怪。
队员允许拾取，但由开关控制，并且不能打断防御、维护和死亡/队伍异常处理。
队友 HP/MP 和异常状态从队伍缓存读取，不依赖队伍窗口打开。
队员实时实体加载范围按有效 Actor 判断；组队业务里的屏幕内可见按 LiveActor 真实距离 <=50m 判断，缓存距离不算。
玩家死亡第一版按 HP=0 判断，`aliveState` 是派生字段。
第一版不做复活/救援技能。
队长死后队员停手。
队员死后不拖垮队长，除非配置要求。
刷本模式下队伍不完整时队长暂停推进。
刷本模式下任意队员掉线时停止推进。
刷本模式下队友与队长真实距离在 50m 内视为同一副本/已就位；超过 50m 或无法取得真实距离时停止推进。
```

## 待确认能力

这些能力后续由用户继续补充或确认底层方案：

- `PartyMemberRecord` 基础偏移已通过只读 live probe 验证：ServerId、HP/MP、名字、职业、等级、异常数量和异常条目可读；正式 adapter 仍需保持只读防御和字段可信度分级。
- `PartyMemberRecord +0x00` 不能直接当 UI 槽位；`+0x20/+0x24` 暂不能当地图/区域 ID；这些字段真实含义仍需继续验证。
- `PartyMemberRecord +0x37` 的离线、跨图、不可用 flags 精确含义需要验证。
- 严格渲染屏幕投影需要的相机矩阵 / WorldToScreen 数据来源。

## 重要约束

- 职业和职责解耦：加血职业可以被设置为输出队员。
- 输出队员不能继承普通挂机的自主找怪行为。
- 输出队员允许自卫，但自卫目标必须来自正在攻击自己本体或宝宝/召唤物的怪。
- 维护加血队员不能走普通攻击开怪流程。
- 维护加血队员被攻击时不自卫，由队长优先标记攻击它的怪物，再由输出队员集火。
- 队员允许拾取，但必须由开关控制；拾取优先级低于防御、维护、死亡和队伍异常处理。
- 距离控制独立于输出和加血逻辑。
- 队伍检测是通用底座，不属于某个具体职责。
- 检测层只产出事实，策略层才做决策。
- 刷本模式比野外模式更保守。
- 队长死亡时队员不要自动接管队长逻辑。
- 队员死亡默认不阻塞队长，除非配置指定。
- 第一版不实现复活/救援技能；死亡后只暂停、等待或停止推进。
- 后续实现时应优先保持现有普通挂机逻辑不被破坏。
