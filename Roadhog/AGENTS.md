# Roadhog 项目说明

本项目以 `maplestory-agent-architecture` skill 作为架构参考，但会按 C# WinForms 和 .NET 8 的形态落地。

Roadhog 不是 MapleStory Lua 引擎。这里只采用可迁移的工程规则：

- 模块保持原子化，职责边界要明确。
- UI 代码只负责渲染状态和发送命令。
- 外部客户端、工具或进程 API 必须封装在窄适配器边界后面。
- 决策逻辑尽量保持纯净：输入上下文，输出提案或结果。
- manager/service 负责校验和编排决策，不直接调用 UI 或底层 API。
- 添加后台执行后，worker/runtime 流程必须隔离，并且停止、启动等操作要具备幂等性。
- 框架或 runtime 行为优先补 mock 测试，再依赖真实客户端。
- runtime 决策和失败原因优先使用结构化诊断。

安全边界：

- 不要在这里加入内存偏移、封包逻辑、hook、绕过、反检测逻辑，或未授权的网游自动化内部实现。
- 如果需要接入客户端，必须放在有文档说明的本地 adapter 后面，并保留 mock 路径。
