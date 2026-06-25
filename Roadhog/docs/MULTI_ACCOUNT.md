# 多账号运行时

Roadhog 的设计基础是：一个账号对应一个隔离 worker。

规则：

- 每个账号拥有一个 `AccountConfig`。
- 每个账号绑定一个硬件特征。
- `AccountConfig.HardwareKey` 为空、`0` 或 `auto` 时，表示自动绑定第一个匹配且未被其他运行中账号占用的硬件设备。
- 非空 `AccountConfig.HardwareKey` 表示手动硬件绑定请求。它可以是 `port:...`、`usb:...`、父设备实例 ID、子设备实例 ID、container id 或 hardware id 别名。
- `AccountConfig.ProcessId` 只在 VMM/进程解析后作为 runtime 细节保留；它不是持久账号绑定键。
- 每个启动后的账号拥有一个 `AccountWorkerHost`。
- 每个 worker 拥有自己的 `CancellationTokenSource`。
- 每个 worker 使用 `TaskCreationOptions.LongRunning` 启动。
- 停止流程由请求驱动，并且具备幂等性；Roadhog 会取消该账号的 token，并等待 worker 退出。
- UI 必须调用 `AccountOrchestrator.Start(config)` 和 `AccountOrchestrator.StopAsync(accountName)`。
- UI 不允许直接运行游戏逻辑、内存读取或动作循环。

当前 worker 循环：

```text
AccountOrchestrator
  -> 为账号绑定硬件特征
  -> 可用时解析 runtime 进程 PID
  -> 每账号一个 AccountWorkerHost
  -> DefaultAccountWorkerLoop
  -> 心跳 / 可选 mock 角色轮询
```

硬件绑定：

- 默认硬件匹配器是 FTDI FT601：`VID_0403&PID_601F`。
- 当 USB 序列号可信时，绑定键优先使用唯一 USB 序列号。
- 已知重复的 FTDI 序列号，例如 `000000000001`，不信任为永久绑定键。
- 当序列号缺失或重复时，Roadhog 使用固定 USB 口键，例如 `port:Port_#0004.Hub_#0002`。只要设备保持在分配好的 USB 口上，应用就能稳定运行。
- composite USB 节点视为物理父设备。
- FTDI `MI_00` 子节点提供显示名、厂商、驱动服务和接口实例 ID。
- USB 换口后遗留的幽灵设备或非在线设备，在绑定时会被忽略。
- 硬件绑定还携带 `VmmDeviceName`，当前默认值为 `fpga`，与 Tool 的 `VMM_DEVICE` 约定保持一致。
- `WindowsHardwareDeviceResolverOptions.VmmDeviceByHardwareKey` 可以把某个绑定键或别名映射到指定 VMM 设备参数。
- UI 中硬件字段为空、`0` 或 `auto` 时，Roadhog 自动绑定第一个空闲匹配硬件设备。
- UI 中硬件字段有值时，Roadhog 会在启动账号 worker 前校验该硬件键。
- 同一时间，一个硬件键只能被一个运行中账号占用。
- 停止只释放该账号的硬件占用，并保留 UI 中的硬件输入值。

当前 FT601 策略：

- 在序列号重复的情况下，固定 USB 口绑定是当前可直接使用的稳定模式。
- 如果后续设备写入了真实唯一序列号，绑定类型可以升级为 `usb-serial`，不需要改变 worker 模型。
- 账号表按当前在线 FT601 设备数量生成：一个在线设备生成一行，两个在线设备生成两行。

PID 策略：

- `ProcessId` 保留在 runtime 状态中，因为后续 VMM adapter 仍需要解析并初始化 Aion 进程。
- 不要把 PID 作为持久用户/账号绑定键，因为 PID 每次启动都可能变化，并且不能标识物理 VMM 硬件。
- 启动时会尽力通过 `VMM_PROCESS` 解析 `Aion.bin`，但 PID 解析失败不会阻止已经完成硬件绑定的 worker 启动。

未来业务逻辑应添加在单账号 worker 路径内：

```text
Worker -> Perception -> Blackboard -> Planner -> Behavior -> Executor -> Environment
```

不要为所有账号增加一个共享的全局 runtime 循环。共享服务只允许用于无状态或安全的基础设施，例如日志、配置加载和 adapter factory。
