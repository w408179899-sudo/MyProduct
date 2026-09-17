# 0.2.0 → 0.3.0

本版调整会话装配接口。升级公共包后修改项目组合根，再运行项目自己的测试。

1. MockSessionFactory 的 register/modules 两个回调合并为 configure(SessionComposition)。从 composition.Profile 获取账号配置，向 composition.Channels 注册数据，使用 AddModule 注册模块工厂。
2. HardwareSessionFactory 使用 configure(SessionComposition, DmaDispatcher, ProcessBinding)。模块注册与 Mock 共用同一个方法；只有读取适配不同。
3. typed token 由本次会话的 ProjectReaders 创建，通过 ProjectChannels 传入模块工厂，禁止用共享变量在两个回调间传 token。不要保留跨账号的模块实例。
4. 模块参数使用 AddModule<TOptions> 的默认值、校验器和工厂。原先只保存但未使用的 JSON 会开始生效；未知模块/字段会明确报错。
5. 新模板提供 Project.Infrastructure。已有项目可继续使用现有基础设施工程；Application 保持仅引用 Domain/Contracts。
6. ActionFeedback 新增 Failure 分类；action.started/completed 的 Detail 改为结构化 JSON。外部日志解析器应随之更新。
7. 多通道回放采用 SnapshotTraceTimeline 和同一个 ReplayClock。单通道辅助 API 仍可使用。普通读取失败仍保持原正式值、版本和时间。

停止超时表示后台仍在完成清理；等待或重试 StopAsync，完成前不能重启账号。配置切换失败不会先覆盖旧配置文件。
