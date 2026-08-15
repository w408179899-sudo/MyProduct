# DMA 稳定快照通道

Roadhog 的 DMA 业务数据统一注册在 `AionVmmSnapshotChannels`，并经过
`DmaStableSnapshotStore` 处理。全部 Application 控制器和 UI 读取入口都通过 `IRoadhogSnapshotReader` 取得数据，业务只接收 `PublishedGameSnapshot<T>`，不接收原始
`OperationResult<T>`、Partial/Failed、读取错误或空默认值。

## 发布规则

1. 每种业务数据必须先在 `AionVmmSnapshotChannels` 注册强类型通道；参数化数据必须使用分区键。
2. 原始读取、完整性校验、重试、字段合并和发布全部留在 VMM/快照层。
3. 读取失败不覆盖当前快照，也不会向业务发布新版本；业务读取器在底层继续等待。
4. 字段级读取只更新成功字段，失败字段保留上一版本的最后正常值。
5. 集合遍历不完整时保留未观察到的旧对象；只有完整遍历才能证明对象不存在并将其裁剪。
6. 合法的 `0`、`false` 和完整空集合都是正常数据，不能按值判断读取是否成功。
7. PID 变化、进程/模块消失或 VMM 连接重置属于会话生命周期变化，必须隔离并使旧快照失效。

## 业务约束

- 全部业务控制器只依赖 `AccountWorkerContext.Snapshots`，不得创建 `GameApiReadContext`，不得设置
  `BypassMemoryCache` 或 `RequireFresh`，也不得直接引用原始 GameApi。Runtime/UI 的数据刷新入口同样只能调用快照读取器。
- “动作后读取当前值”通过快照接口中的用途方法表达；是否绕过内存缓存、是否要求 fresh 仍由底层决定，业务不创建读取策略。
- DEBUG 地址探针是明确隔离的底层诊断入口，不向业务状态机提供数据。
- `PublishedGameSnapshot<T>.Version` 只在底层发布一份新的正常快照后增长。
- 不可逆动作先记录版本 N，动作后等待 `Version > N`，再用新快照验证结果。
- 等待快照期间保持原业务状态；读取故障不能触发状态重置、默认值分支或提前完成。

## 背包集合合并

背包以 `InstanceId` 作为稳定身份。模板 ID、数量、名称、格位、装备状态、物品类型、品质和
商店价格分别携带字段有效性。账号 3 的背包树循环属于不完整遍历：已成功字段合并，未遍历物品
保留；只有下一次完整遍历才能裁剪已删除的物品。

世界对象采用同样规则，以 `ServerObjectId` 为首选稳定身份。账号 2 的
`world_target_self_reference` 不会进入业务；下一份正常世界对象快照发布后，清背包继续安全检查。
