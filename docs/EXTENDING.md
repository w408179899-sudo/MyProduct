# 扩展接口，不附带业务

## 增加数据通道

1. 在 Domain 中定义不可变的数据类型。
2. 在 Project.Infrastructure 中定义原始读字段有效性、解码器与 merger；它们引用 Smart.Data。
3. 在 ProjectReaders.RegisterMock/RegisterHardware 注册强类型 SnapshotChannel，返回 Project.Application 的 ProjectChannels；两个模式使用相同正式数据契约。
4. 在模块 RequiredChannels 中声明通道 ID。目录在所有注册结束后统一 Seal。

真实读取通过 DmaChannelReader 的有界地址计划进入共享 dispatcher，解码器只使用已验证的字节。不要把原始完整性标志传给 Application。

## 增加模块

实现 IAccountModule，提供 Id、Priority、Dependencies、RequiredChannels，以及 TickAsync。InitializeAsync 在依赖初始化后调用；StopAsync 按逆依赖顺序调用，部分初始化失败也会清理。

每个账号的每个会话创建新的模块实例。在 Bootstrap/ProjectComposition.RegisterModules 中使用 SessionComposition.AddModule 注册；不往 AccountWorker 主循环添加项目判断。模块自身状态和 token 不能 static 共享。

AddModule 的强类型重载接收模块 ID、所需通道、默认选项、选项校验器和模块工厂。框架从当前账号 ModuleSettings[模块 ID] 反序列化选项并执行校验；未知字段和未知模块配置明确报错。模块工厂闭包只捕获本次会话的 ProjectChannels 和已验证选项。完整的无业务可执行示例见 tests/Smart.Runtime.Tests/CompositionTests.cs。

没有参数的模块使用简单重载；为这种模块填写 JSON 参数会报错，避免配置被静默忽略。

TickContext.Snapshots 是模块的正式读取入口。首次读取可等待，框架不会编造默认值；异步等待期间该模块不重入，其他模块照常调度。

普通模块只返回下次运行间隔。需要动作时才返回 ActionPlan；需要结束持续输入时返回 ReleaseInput=true。依赖协作式取消，长算法限制输入规模并分段执行。

## 动作条件

IActionPrecondition.EvaluateAsync 检查当前正式数据是否仍满足动作条件。它可以捕获模块收到的正式读取器和领域条件，不检查读质量、不要求强制刷新。

设置 ExpiresAt 可让长时间未执行的提案失效。动作被拒绝后，下一次 Tick 应根据当前领域状态重新决定；底座不会无条件重提缓存动作。输入成功仅说明指令完成，具体业务结果确认由将来的业务模块实现。

## 配置与回放

AccountProfile.ModuleSettings 为模块保存独立 JSON 参数；SessionComposition 负责绑定，项目提供强类型选项校验器。通用账号配置由 JsonConfigStore 原子保存；旧版本迁移显式注册 version → version+1 转换函数。

启用 RecordSnapshots 后，正式发布值和运行配置写入日志；默认日志宿主在有界后台队列中序列化快照。动作记录携带相同 RunId，包含实际命令、执行结果和错误分类。

多通道回放使用 SnapshotTraceTimeline.LoadAsync(files, runId)，通过 CreateReader 为各通道创建输入源，并共用同一个 ReplayClock。首次数据出现时间相对同一会话起点计算，晚出现的通道不会提前产生值。重复采样同一帧不会增加正式版本。Events 保留配置、跨通道快照和动作记录；缺少配置、帧号缺失、混入其他身份会报错。SnapshotTraceReplay.LoadAsync 保留为单通道分析辅助接口。

回放是在记录时间轴上重现输入状态；消费端采样间隔可能跳过中间状态，实时线程调度和设备执行时间也不由日志重现。要验证业务决策，应给模块注入时钟，配合 Mock 输入比较正式状态与动作命令。日志是有界诊断记录，轮转或丢弃后可能不完整；保留完整会话文件并检查 DroppedEvents/WriteError。

模板没有默认业务模块、业务通道或自动动作。测试中的整数源、合成集合和输入设备替身仅用于公共契约验证。

添加第一个通道和模块后，生成项目的测试会继续验证装配与依赖，不再要求目录为空。维护模板时，TemplateSmoke 会在一次性生成工程中接入四层合成夹具，验证正式快照、两个账号参数、动作反馈与停止后租约释放；夹具不会出现在普通新项目的业务源码中。具体操作与证据见 TESTING.md、VALIDATION.md。

## 控制台长期运行

Console 无参数仍运行约一秒的演示。业务项目使用 `--run --config <path>` 持续运行，也可加 `--account <id>` 只运行指定账号；硬件账号同时需要显式 `--run-hardware`。按 Ctrl+C 发出取消后会等待账号停止、输入释放和日志关闭，不把用户取消传给资源清理。

有限时长使用 `--duration-ms <100..2147483647>`，不能与 `--run` 混用。参数重复、拼写错误、缺少值以及不存在的账号会明确报错。故障账号立即输出错误，其他账号继续运行；所选账号全部故障时结束，最终退出码为非零。结果保留停止前的故障信息，不再输出假定为零的模块数量。
