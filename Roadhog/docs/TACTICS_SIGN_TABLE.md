# Roadhog 战术标记表技术记录

记录日期：2026-07-13

来源：同事提供的 IDA 导出分析。本文记录组队/部队给怪物头顶加的战术标记读取方式，用于后续实现 `TeamTargetSync` 和队长防御标记逻辑。

## 总结

战术标记不存放在怪物 `Actor` 内部。客户端维护了一张全局标记表：

```text
GameBase +0xD668E0
类型：uint32[16]
含义：16 个战术标记槽对应的目标 ServerObjectId
```

也就是说，第一版 `markedTargetId` 可以定义为：

```text
markedTargetId = 战术标记槽中的目标 ServerObjectId
```

空标记槽：

```text
ServerObjectId == 0
```

有效标记槽：

```text
ServerObjectId != 0
```

当前运行版从 `GameBase +0xD668E0` 读取；实际代码按 16 个 `uint32` 遍历和写入，不是 8 个 `uint64`。

## 标记表结构

```text
GameBase +0xD668E0 +0x00 = 标记槽 0 的 ServerObjectId
GameBase +0xD668E0 +0x04 = 标记槽 1 的 ServerObjectId
GameBase +0xD668E0 +0x08 = 标记槽 2 的 ServerObjectId
...
GameBase +0xD668E0 +0x3C = 标记槽 15 的 ServerObjectId
```

读取公式：

```csharp
uint markedServerId =
    memory.ReadUInt32(gameBase + 0xD668E0 + (nuint)(markIndex * 4));
```

索引范围：

```text
markIndex: 0..15
displayNumber: markIndex + 1
resourceName: sign_{markIndex + 1}
```

核心客户端函数 `sub_180067480` 会遍历 16 个槽，先清除同一个 `ServerObjectId` 的旧标记，再把它放进新的标记槽。因此正常情况下，一个目标最多对应一个战术标记。

## 判断怪物是否被标记

怪物的运行时 ID：

```text
monsterActor +0x2C = ServerObjectId
```

怪物类型：

```text
monsterActor +0x20 = objectType
同事资料中提到 objectType == 2 表示 NPC/怪物类
```

Roadhog 现有 VMM 代码里，`objectType == 2` 已经被召唤物识别链路使用。因此第一版读取战术标记时，不应把 `objectType == 2` 作为硬条件。更稳的做法是：

```text
读取标记表时：
  只认 GameBase +0xD668E0 的 uint32 ServerObjectId[16]

需要把标记对应到可见怪物时：
  先用 ServerObjectId 对上已加载对象
  再结合 EntityTypeNpc、NPC 静态数据、WorldObjectSnapshot.Kind 等 Roadhog 现有怪物判定
```

判断逻辑：

```text
monsterServerId = *(monsterActor +0x2C)

遍历 GameBase +0xD668E0 + i*4

某一项 == monsterServerId
  -> 怪物身上有战术标记
  -> i 是标记索引

16 项均不匹配
  -> 怪物没有战术标记
```

不要寻找：

```text
monsterActor + 某偏移 = 标记编号
```

正确关系是：

```text
全局标记槽 index -> ServerObjectId -> 对应 monsterActor +0x2C
```

## 读取全部活动标记

建议一次读取 16 个槽，构造按 `ServerObjectId` 查询的字典。不要遍历大量怪物时对每只怪重复读 16 次内存。

```csharp
public readonly record struct TacticsSignInfo(
    int Index,
    int DisplayNumber,
    uint ServerObjectId,
    string ResourceName);

public static Dictionary<uint, TacticsSignInfo> ReadTacticsSignMapByServerId(
    IMemoryReader memory,
    nuint gameBase)
{
    const nuint TacticsSignTableRva = 0xD668E0;
    const int SignCount = 16;

    var result = new Dictionary<uint, TacticsSignInfo>(SignCount);

    for (int index = 0; index < SignCount; index++)
    {
        uint serverId = memory.ReadUInt32(
            gameBase + TacticsSignTableRva + (nuint)(index * 4));

        if (serverId == 0)
            continue;

        result[serverId] = new TacticsSignInfo(
            Index: index,
            DisplayNumber: index + 1,
            ServerObjectId: serverId,
            ResourceName: $"sign_{index + 1}");
    }

    return result;
}
```

## 队长标记目标读取

当前实现不固定标记槽，而是一次读取全部 16 个槽：

```text
activeMarkedTargetIds = all nonzero uint32 values from GameBase +0xD668E0
```

`markedTargetId == 0` 表示当前没有标记目标。

`markedTargetId != 0` 表示当前标记槽指向某个目标 `ServerObjectId`。

这正好满足输出队员同步需求：

```text
队长标记怪物
  -> VMM 一次读取 D668E0 的全部 16 个槽
  -> 任意槽出现目标 ServerObjectId 即表示标记成功
  -> 输出队员按“锁定标记怪物”键
  -> 队员确认自己已锁定怪物
  -> 进入半自动输出
```

队员不需要验证自己的锁定目标 ID 是否等于 `markedTargetId`，因为游戏机制会直接锁定标记怪物。`markedTargetId` 仍然有价值：用于判断是否有标记、标记是否过期、日志记录、目标死亡/未加载时的诊断。

## 输出队员选怪实测结论

测试时间：2026-07-13

测试场景：

```text
队长已经给一只怪物打上战术标记。
队员客户端可以在游戏画面中看到该标记。
Roadhog.Tests 使用 script\3 的 VMM 读取队员客户端。
```

队员按“锁定标记怪物”前：

```text
SignSlot#00 ServerId=2219430696 Active=yes
TacticsSignSummary MarkedTargetId=2219430696
CurrentTargetServerId=2147555726
MatchesCurrentTarget=no
```

说明队员客户端已经同步到队长标记，但队员当前选中的目标还不是被标记怪物。

队员按“锁定标记怪物”后：

```text
CurrentTargetServerId=2219430696
SignSlot#00 ServerId=2219430696
MatchesCurrentTarget=yes
TacticsSignSummary CurrentTargetMatched=yes
```

结论：

```text
队长标记怪物
  -> 队员客户端同步 SignSlot#00
  -> 队员按锁定标记怪物键
  -> 队员当前目标 ServerId == MarkedTargetId
```

因此输出队员选怪问题第一版已经解决。后续实现时，输出队员不需要主动遍历怪物找队长目标，也不需要额外验证“是不是队长目标”。第一版流程只需要：

```text
1. 一次读取全部 16 个 SignSlot。
2. 如果任意槽的 ServerObjectId != 0，则按“锁定标记怪物”键。
3. 读取当前锁定目标，确认当前已经锁定到怪物。
4. 进入现有半自动输出逻辑。
5. 如果没有锁定到怪物，则等待或重试，不输出。
```

## 标记目标不在实体加载范围

标记表只保存 `ServerObjectId`，所以可能出现：

```text
D668E0 表里有 ServerObjectId
但当前遍历不到对应 Actor
```

这说明标记映射仍在客户端缓存中，但目标当前没有加载成实时实体。

此时可以知道：

```text
某个 ServerObjectId 有标记
标记索引是多少
```

但仅靠标记表无法知道：

```text
怪物名字
模板 ID
坐标
血量
```

需要目标重新进入实体加载范围，通过 `ServerObjectId -> Actor*` 补齐。

## 服务器同步来源

服务器的战术标记同步在分发函数的 `case 250` 中处理。数据会逐条取出：

```text
signIndex
targetServerObjectId
```

然后调用：

```text
sub_180067480(context, signIndex, targetServerObjectId)
```

客户端收到更新后，最终写入 `GameBase +0xD668E0` 这张 16 槽表。怪物/NPC 生成后，也会用 `ServerObjectId` 检查这张表并刷新头顶标记。

## 待验证

- 游戏里每个具体标记图案/中文名称与内部 `markIndex` 的对应关系。
- 队长和队员可以配置相应的游戏按键；业务层不绑定具体标记图案或槽号。
- 已验证队员按“锁定标记怪物”键后，可以锁定队长标记的怪物；按键后只需确认当前目标是活怪。

## 只读 Live Probe

Roadhog.Tests 中提供只读探针：

```powershell
ROADHOG_TEST_MODE=tactics_sign_probe
VMM_DEVICE=fpga://devindex=0
MEMPROCFS_HOME=C:\Users\GoldGiven\Desktop\script\3
Roadhog.Tests\bin\DebugPartyProbe\Roadhog.Tests.exe
```

探针输出：

```text
LocalEntityId
CurrentTargetEntityId
CurrentTargetServerId
SignSlot#00..15
Active
MatchesCurrentTarget
TacticsSignSummary.ActiveCount
TacticsSignSummary.MarkedTargetId
```

建议验证流程：

```text
1. 队长锁定一只怪
2. 运行 probe，记录 CurrentTargetServerId
3. 队长按战术标记键
4. 再运行 probe
5. 预期某个 SignSlot 的 ServerId == CurrentTargetServerId
6. 预期至少一个槽的 ServerId 等于目标 ServerId
```
