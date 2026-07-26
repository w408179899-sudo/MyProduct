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

以下来自 2026-07-22 IDA 导出/同事整理，尚未像竞争字段一样完成本轮现场闭环验证。后续做本机采集读条时可以按此链路验证：

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
