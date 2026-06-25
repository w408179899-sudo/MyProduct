# Roadhog 架构

Roadhog 遵循本地 agent 架构规则：UI 只渲染状态并发送命令。

当前分层：

- `Core`：纯契约、DTO、结果类型和日志接口。
- `Application`：账号运行态和编排入口。
- `Application/Workers`：一个账号对应一个隔离的长生命周期 worker。
- `Infrastructure/Hardware`：硬件特征发现和账号硬件绑定。
- `Infrastructure/Processes`：尽力而为的 runtime 进程发现。
- `Infrastructure/ToolBridge`：可选测试适配器，通过进程边界调用现有 `Tool.exe` 模式。
- `Infrastructure/Offsets`：偏移目录模型和加载器。偏移是数据，不是散落在代码里的常量。
- `Infrastructure/Mock`：无真实客户端时供 UI 和测试使用的 mock API。

Tool 测试集成：

- `Tool` 是 .NET Framework 4.8 可执行程序，Roadhog 是 .NET 8。
- Roadhog 是独立工程；`Tool` 只作为测试和探测来源。
- Roadhog 默认使用 `MockRoadhogGameApi`。
- 只有测试运行需要调用 `Tool.exe` 时，才设置 `RoadhogServiceOptions.UseToolTestBridge = true`。
- Roadhog 正式代码应该依赖 `IRoadhogGameApi`，不要依赖 Tool 内部实现。

禁止的依赖方向：

```text
UI -> Tool internals
UI -> offsets
Application -> raw memory process
Core -> Infrastructure
Production runtime -> Tool test bridge
```

允许的依赖方向：

```text
UI -> Application -> Core interfaces -> Infrastructure adapters
```

多账号规则：

```text
AccountConfig(chahohyur) -> 硬件绑定 -> 可选 PID 解析 -> AccountWorkerHost(chahohyur)
AccountConfig(account2)  -> 硬件绑定 -> 可选 PID 解析 -> AccountWorkerHost(account2)
```

硬件绑定默认匹配 FTDI FT601（`VID_0403&PID_601F`），按当前在线设备身份把 composite 父节点和 FTDI `MI_00` 子节点组合起来，并携带匹配的 VMM 设备名（默认 `fpga`）。重复序列号不适合作为长期绑定键；在设备具备唯一序列号之前，使用固定 USB 口绑定。`ProcessId` 只是后续 VMM/进程初始化所需的 runtime 细节，不是持久账号绑定键。

更多细节见 `MULTI_ACCOUNT.md`。
