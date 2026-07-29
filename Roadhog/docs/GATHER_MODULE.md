# 采集模块数据链和竞争判定

本文记录 2026-07-25 在 `C:\Users\GoldGiven\Desktop\script\4` 上做过的只读验证，以及后续采集模块实现应采用的边界。

当前结论分两层：

- 采集物身份、坐标、可采状态、采空消失可以用 VMM 只读链路稳定判断。
- 其他玩家是否正在采集本路线点，先采用固定路线点下的实用竞争判定；不把它描述成协议级精确占用。

## 当前边界

路线采集点是固定坐标，并且录制时已经知道该点对应的 `GatherSourceId`。在这个前提下，不需要先强行捕获 `S_GATHER_OTHER`，可以先用小半径内的玩家采集动作判断竞争关系。

推荐业务行为：

```text
到达录制采集点前/到达后，扫描可见玩家。
如果小半径内有非本机玩家正在采集同 GatherSourceId，则认为当前路线点被竞争，直接跳下一个采集点。
```

默认半径先用 `5m`。如果某条路线点附近存在多个同模板采集物，应为该点单独缩小半径或改用强判定。

## 已实现的只读 API

2026-07-27 已在 Roadhog 中加入独立采集 API：

```csharp
IRoadhogGameApi.ReadGatherSnapshotAsync(...)
IRoadhogScopedGameApi.ReadGatherSnapshotAsync(context, ...)
RoadhogRuntime.RefreshGatherSnapshotAsync(accountName, ...)
```

`GatherSnapshot` 一次读取返回：

- `Objects`：按角色距离排序的可见采集物。
- `NearbyPlayers`：可见非本机玩家及竞争判断原始字段。
- `NearbyMonsters`：同一次 ServerObject 遍历得到的可见怪物、位置、生命、目标和主动属性。
- `LocalGathering`：本机 `DlgGathering` 可见状态、当前 SourceId/目标上下文和成功/失败进度条。
- `LocalEntityId`、`LocalServerObjectId`、`LocalPosition`。
- `MonsterDataAvailable`：当前实现是否包含怪物环境数据。
- `CompetitionDataAvailable`：当前实现是否包含玩家竞争数据。

`GatherObjectSnapshot` 包含：

- `ServerObjectId`、`GatherSourceId`、运行时名称。
- 当前世界坐标、生成坐标、角色距离、交互半径。
- `RuntimeAvailabilityRaw`、`InteractionState`、是否为当前锁定目标。
- 从 `gather_src.xml` 关联的 `GatherSourceDefinition`。

`GatherCompetitionPlayerSnapshot` 包含：

- 玩家 `ServerObjectId`、名称、坐标和距离。
- `Actor+0xAB0`、`Actor+0xAB4`、`Actor+0x500` 三个原始值。
- `IsGatheringActionCandidate` 和 `MatchesGatherSource(...)` 辅助判断。

`GatherSnapshot.FindLikelyCompetitors(...)` / `IsLikelyOccupied(...)` 实现本文记录的固定路线点实用竞争规则。`ContainsObject(ServerObjectId)` 用于后续判断具体采集物是否已从遍历结果消失。

`GatherSourceCatalog` 读取 `Source/gather_src.xml`，当前目录包含 `451` 条记录，并提供采集类别、采集技能、熟练度、角色等级、理论次数、条件道具、产物等静态信息。构建时该 XML 会复制到 Roadhog 输出目录。

2026-07-29 已接入采集物过滤 UI 的账号/方案持久化、按类别配置按键，以及“原地先采集后打怪”控制器。路径采集控制器仍未实现。

## 原地先采集后打怪

开关默认关闭。关闭时原地控制器不读取采集快照，也不改变原有打怪扫描和时序。

打开后，仅在原地打怪角色空闲时插入采集优先级：

```text
死亡/复活、战后拾取、保命维护、当前战斗
  -> 原样优先

角色空闲
  -> 一次采集快照同时读取采集物、竞争玩家、附近怪物和本机读条
  -> 选中配置启用、已设置按键、SourceId 匹配、Allowed、搜索半径内且无竞争的节点
  -> 节点附近安全清怪
  -> 走到交互半径
  -> 按该类别自己的采集键
  -> 等待 DlgGathering 出现和消失
  -> 节点仍存在则继续下一次
  -> 同一 ServerObjectId 连续两次独立成功遍历都消失后完成
  -> 回到原地打怪
```

安全清怪规则：

- 采集物安全半径内，已知主动且对玩家主动的活怪会先处理。
- 无论距离，只要怪物正在攻击本机或本机召唤物，立即交回战斗链。
- 如果安全半径内的危险怪已经被别人占用，或已进入忽略名单，不抢怪，也不继续冒险采集；当前节点短时抑制后回到正常打怪。
- 被动怪和安全半径外、未攻击本机的主动怪不阻止本次采集。

失败边界：

- 怪物、竞争或本机读条数据任一不可用时，不按采集键。
- 读条开始连续三次未确认，或输入连续三次失败，短时抑制该具体节点，防止死循环。
- 接近阶段会检测无位移、尝试跳跃；持续无法接近则抑制节点。
- 采集物单次读取缺失不算采空；相同 `CapturedAt` 的缓存快照不会重复累计。

## 路径采集执行需求

采集模式走“路径点 + 可选采集动作”的模型。路径点本身可以只是移动/绕障点；只有在该点配置了采集动作时，才执行对应采集。

建议数据形态：

```text
SharedPathPoint
  Index
  Position: X/Y/Z
  GatherActions: List<GatherPointAction>

GatherPointAction
  ExpectedGatherSourceId
  GatherName
  GatherKey
  SearchRadiusMeters
  OccupiedCheckRadiusMeters
```

`GatherKey` 随路线点上的具体采集动作保存，不做成一个全局采集键。第一版 UI 按 `GatherSourceId` 管理类别按键：同类采集物共用并同步一个按键，不同类别可以绑定不同按键。底层保留 `List<GatherPointAction>`，后续可以扩展一个点采多个物。

运行时行为：

```text
1. 到达路径点。
2. 如果该点没有 GatherActions，则只当普通路径点，继续下一个点。
3. 如果该点有 GatherAction：
   - 在该点 SearchRadiusMeters 内找 ExpectedGatherSourceId 相同的采集物。
   - 要求采集物 Actor+0x1CC == 40，即当前可采。
   - 用路线点竞争判定确认没有非本机玩家占用。
   - 通过该动作自己的 GatherKey 触发采集。
   - 锁定本次刷新出来的具体 Actor+0x2C ServerObjectId。
   - 不按“采一次”离开；持续等待/必要时重试采集，直到该 ServerObjectId 从采集物遍历结果里消失，再进入下一个点。
```

如果采集物不存在、状态 blocked、竞争命中、没有配置 `GatherKey`，第一版都应跳过该采集动作并打日志，不做内存写入或协议调用。当前产品化实现先按用户配置按键走输入链；如果实测某些采集需要先锁定目标或额外交互键，再把“锁定/交互键”作为独立配置补进来。

## 采集物只读遍历链

当前只读遍历链：

```text
GameBase
  -> [GameBase + EntitySystemPointerRva]
  -> EntitySystem + 0x58 entity tree

GameBase
  -> [GameBase + ServerObjectTreeRva]
  -> server object tree
  -> server node ServerObjectId + EntityId
  -> entity tree 根据 EntityId 找 CEntity
  -> CEntity 解析 Actor/GameObject
  -> Actor + 0x20 == 7 过滤采集物
```

RVA：

| 名称 | 值 | 用途 |
|---|---:|---|
| `EntitySystemPointerRva` | `0x94C7B0` | `GameBase + RVA` 读 EntitySystem 指针。 |
| `ServerObjectTreeRva` | `0xD6CAC0` | `GameBase + RVA` 读 server object tree header。 |
| `LocalEntityIdRva` | `0xD6CB18` | 本机 entity id；`+0x2` 是当前目标 entity id。 |
| `CurrentGatherSourceIdRva` | `0xD68CE8` | 本地采集上下文里的当前 GatherSourceId；可能残留，只作诊断。 |
| `CurrentGatherTargetEntityRva` | `0xD68CF0` | 本地采集上下文里的当前目标 CEntity；可能残留，只作诊断。 |
| `GatherSourceRecordCountRva` | `0xD9B778` | 静态 GatherSource 记录数量诊断值；实测为 `451`。 |

红黑树/节点字段：

| 字段 | 偏移 | 用途 |
|---|---:|---|
| `NodeLeftOffset` | `+0x00` | 树左节点。 |
| `NodeParentOffset` | `+0x08` | 树父节点；从 header 取起点时使用。 |
| `NodeRightOffset` | `+0x10` | 树右节点。 |
| `NodeIsNilOffset` | `+0x19` | sentinel/nil 判断。 |
| `NodeIdOffset` | `+0x20` | entity tree key。 |
| `NodeEntityOffset` | `+0x28` | entity tree value，指向 `CEntity`。 |
| `ServerNodeServerObjectIdOffset` | `+0x1C` | server object tree 节点上的 ServerObjectId。 |
| `ServerNodeEntityIdOffset` | `+0x20` | server object tree 节点上的 EntityId。 |

Entity 字段：

| 字段 | 偏移 / 值 | 用途 |
|---|---:|---|
| `EntitySystem + EntityTreeOffset` | `+0x58` | entity tree header。 |
| `CEntity + EntityTypeOffset` | `+0x122` | entity 类型诊断。采集物样本为 `9`，玩家样本为 `1`。 |
| `CEntity + EntityWorldPositionOffset` | `+0x4E4` | 世界坐标 X/Y/Z，当前采集导航和距离判断使用这里。 |
| `CEntity vtable + EntityProxyManagerVfuncOffset` | `+0xB8` | 解析 Actor/GameObject 的辅助路径。 |

Actor/GameObject 字段：

| 字段 | 偏移 / 值 | 用途 |
|---|---:|---|
| `Actor + 0x08` | `CEntity*` | Actor 反指 CEntity；玩家竞争 watcher 用它取坐标。 |
| `Actor + 0x20` | `objectType` | `1=玩家`，`7=采集物`。采集物过滤必须用 `7`。 |
| `Actor + 0x2C` | `ServerObjectId` | 具体运行时对象身份。采集物用它作为唯一节点身份。 |
| `Actor + 0x30` | `GatherSourceId` | 采集物模板/类别 ID，不唯一。多个节点可共享同一个值。 |
| `Actor + 0x3E` | raw field | 诊断字段；不要用于剩余次数。 |
| `Actor + 0x40` | state byte | 实测一直为 `100`，不是可靠剩余次数。 |
| `Actor + 0x42` | UTF-16 name | 采集物/玩家名称。 |
| `Actor + 0x168` | gather radius | 采集交互半径。 |
| `Actor + 0x19C` | spawn position | 采集物生成坐标，可作为 `CEntity + 0x4E4` 的交叉校验。 |
| `Actor + 0x1CC` | InteractionState | 实测 `40=Allowed`，`41=Blocked`。 |
| `Actor + 0x358` | TargetServerObjectId | 通用当前目标字段；采集竞争当前不依赖它。 |

重要修正：

- `CEntity + 0x4E4` 是当前可用世界坐标链；旧候选 `CEntity + 0x4B4/+0x4F4` 在本轮采集样本中读成 `0,1,0`，不能用于采集导航。
- `Actor + 0x30` 是 `GatherSourceId`，只能表示采集物类别，不能表示具体节点。
- `Actor + 0x2C` 才是具体采集物节点身份。

## 采集物可采和采空判断

可采状态：

```text
Actor + 0x20 == 7
Actor + 0x1CC == 40
```

blocked 状态：

```text
Actor + 0x1CC == 41
```

采空/失效判断：

```text
锁定的采集物 ServerObjectId = Actor + 0x2C

如果该 ServerObjectId 从当前 objectType==7 的采集物遍历结果中消失，
则认为该具体节点已经采空/失效。

业务上建议要求连续 2 次成功遍历都缺失，再确认采空，
避免单帧 VMM/tree read 抖动。
```

不要用 `Actor + 0x40` 当剩余采集次数。本轮验证中，正常、部分采集、采空相关样本里该值都保持 `100`，不提供 3/2/1/0 这种剩余次数信号。

## 玩家采集动作字段

可见玩家从同一条 server object/entity/actor 链读出，过滤：

```text
Actor + 0x20 == 1
```

玩家采集相关字段：

| 字段 | 偏移 | 用途 |
|---|---:|---|
| `playerActor + 0x500` | `+0x500` | 当前/最近采集的 `GatherSourceId`。可能残留，不能单独判断正在采集。 |
| `playerActor + 0xAB0` | `+0xAB0` | 动作有效/动作状态候选。实测采集动作中为 `1`，空闲为 `0`。 |
| `playerActor + 0xAB4` | `+0xAB4` | 当前动作 ID 候选。实测采集相关出现 `1036/1037/1038`，空闲为 `4294967295`。 |
| `playerActor + 0x08 -> CEntity + 0x4E4` | position | 玩家世界坐标，用于和录制采集点算距离。 |

玩家是否处于采集动作：

```csharp
bool gatherAction =
    actionActive != 0 &&
    actionId is 1036 or 1037 or 1038;
```

`1038` 是结束类动作，持续时间短。业务上把它也当占用是保守策略，最多导致多跳过一次点，风险比抢点低。

## 路线点竞争判定

每个录制采集点需要保存：

```text
RouteGatherPoint
  Position: X/Y/Z
  ExpectedGatherSourceId
  OccupiedCheckRadiusMeters, default 5.0
```

判定规则：

```csharp
bool possibleOccupied =
    !player.IsLocal &&
    Distance(player.Position, routePoint.Position) <= routePoint.OccupiedCheckRadiusMeters &&
    player.ActionActiveCandidate != 0 &&
    player.ActionIdCandidate is 1036 or 1037 or 1038 &&
    player.GatherSourceIdCandidate == routePoint.ExpectedGatherSourceId;
```

建议加防抖：

```text
连续 2 次扫描满足，间隔 150-300ms
=> 判定当前路线点被竞争
```

命中后的默认行为：

```text
跳过当前路线点，直接走下一个采集点。
```

不要只因为 `Actor+0x500 == ExpectedGatherSourceId` 就判定竞争；该字段会残留，必须结合 `0xAB0/0xAB4` 动作字段和小半径距离。

## 现场验证样本

测试目标路线点：

```text
Name=阔尼玳
ServerObjectId=3256104532
SourceId=400951
Position=X=1780.178 Y=1688.052 Z=249.614
Radius=5m
```

不竞争样本：

```text
AYOK 正在采集动作:
  Actor+0x500=400851
  Actor+0xAB0=1
  Actor+0xAB4=1036/1037/1038
  TargetDist=46-48m

目标点:
  ExpectedSourceId=400951

结果:
  CompetitionCandidate=no
```

竞争样本：

```text
AYOK 正在采集动作:
  Actor+0x500=400951
  Actor+0xAB0=1
  Actor+0xAB4=1036/1037/1038
  TargetDist=0.87m

目标点:
  ExpectedSourceId=400951

结果:
  CompetitionCandidate=yes
```

停止后解除样本：

```text
Actor+0x500=400951
Actor+0xAB0=0
Actor+0xAB4=4294967295
TargetDist=0.87m
CompetitionCandidate=no
```

这说明 `Actor+0x500` 残留时，只要动作字段回到空闲，竞争关系会解除。

## 本地采集读条信息

以下来自 2026-07-22 IDA 导出/同事整理。2026-07-29 已接入 VMM 只读 `LocalGatheringSnapshot` 和控制器测试；仍需在可独占 VMM 设备的现场进程上完成读条出现/消失闭环验证：

| 字段 | 偏移 / 值 | 用途 |
|---|---:|---|
| Dialog pointer table | `GameBase + 0xD639A0` | UI dialog pointer table。 |
| `DlgGathering` pointer | `[GameBase + 0xD63E38]` | DialogId `147` 的采集窗口对象指针。 |
| `DlgGathering + 0x28` | flags | `flags & 1` 表示可见。 |
| `DlgGathering + 0x4E8` | `UIGauge*` | 成功进度条。 |
| `DlgGathering + 0x500` | `UIGauge*` | 失败进度条。 |
| `UIGauge + 0x300` | double | max。 |
| `UIGauge + 0x308` | double | displayed/current。 |
| `UIGauge + 0x310` | double | target/latest。 |

本地“正在采集”建议组合：

```text
DlgGathering visible
GameBase + 0xD68CE8 当前 GatherSourceId != 0
GameBase + 0xD68CF0 当前目标 CEntity != 0
```

## S_GATHER_OTHER 强判定边界

协议级强判定仍然是观察 `S_GATHER_OTHER`：

| 项 | 值 |
|---|---:|
| `S_GATHER_OTHER` case | `34` |
| dispatch | `Game.dll + 0x426DE0` |
| handler | `Game.dll + 0x439F70` |
| handler 参数 | Windows x64 下入口 `RDX = packet*` |

payload 计算：

```text
packet = RDX
payload = *(uint8_t**)(packet + 0x18) + *(int32_t*)(packet + 0x20)
```

payload 字段：

| 字段 | 偏移 | 含义 |
|---|---:|---|
| `payload + 0x00` | `uint32` | gatherer ServerObjectId。 |
| `payload + 0x04` | `uint32` | gather object ServerObjectId，具体采集物节点。 |
| `payload + 0x0A` | `uint8` | event。 |

event 边界：

```text
0 = 开始采集
1 = 采集进行/循环阶段
2/3/4 = 终止类事件；静态 handler 里走同一类结束清理，不要先强行命名成功/失败/取消。
```

如果后续有稳定事件入口，可以维护强占用表：

```csharp
event 0 or 1:
    occupied[gatherObjectServerId] = gathererServerObjectId;

event 2 or 3 or 4:
    occupied.Remove(gatherObjectServerId);
```

兜底清理：

```text
采集物 ServerObjectId 从 objectType==7 遍历结果消失 => remove
LastSeen 超过 10-20 秒 => remove
```

当前实现阶段先不依赖这条链，因为没有稳定产品化事件捕获入口。固定路线点下的小半径竞争判定已经够用。

## 实现注意事项

- 采集路线点保存的是固定坐标和 `ExpectedGatherSourceId`，不是运行时 `ServerObjectId`；运行时 `ServerObjectId` 每次刷新会变。
- 采集当前目标时，仍应优先用 `Actor+0x2C ServerObjectId` 锁定具体节点；节点消失后放弃该节点。
- 竞争判定只服务于“是否跳过路线点”，不用于精确显示“某玩家正在采具体哪个 ServerObjectId”。
- 日志必须打印：路线点 id、期望 `GatherSourceId`、目标点坐标、候选玩家名/ServerObjectId、玩家距离、`Actor+0x500`、`0xAB0`、`0xAB4`、最终 `CompetitionCandidate`。
- 如果未来接入 `S_GATHER_OTHER`，协议事件表优先级高于启发式；启发式仍可作为没有事件或事件丢失时的保守兜底。
