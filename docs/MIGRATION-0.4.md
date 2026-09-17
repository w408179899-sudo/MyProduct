# 0.3 → 0.4

0.4.1 修复真实设备初始化的 VMM 参数错误。直接构造 `VmmTransport` 的调用改用 `VmmTransport.CreateArguments(deviceUri, extraArguments)`；VMMDLL 从第 0 项开始解析，不能传入程序名。框架连接池、Console 探测和验收探针已统一修复；账号配置中的 `Arguments` 仍只填写额外选项。原生 DLL 的加载方式没有改变。

成功立即发布、Partial 合并、失败保持和冷启动等待的正式数据语义保持不变。项目仍只添加领域模型、读取映射和模块，不加入卡密或具体游戏业务。

## 生命周期

SnapshotSession 实现 IAsyncDisposable。Reset/Dispose 只负责立即失效并发出异步取消；直接持有会话的宿主改用 await using 或 await DisposeAsync，等采集和观察者结束后再关闭依赖。RuntimeSession 已集成。取消回调抛错不能跳过后续清理；清理失败保留资源所有权，恢复后可以再次停止。

AccountWorkspace 开始释放后拒绝新配置，只有账号都清理完成才释放共享工厂。释放成功后再次释放是空操作。不要把释放过的工作区用于重新加载配置，应创建新工作区。

动作、输入清理与模块停止的框架时限使用可等待的异步取消。扩展取消回调抛错时，错误由执行反馈或异步清理报告，不从框架 timer 线程逸出而终止宿主。动作结束先固定是否已超时，再决定保留或释放输入，最后等待取消回调结束；停止操作本身和回调同时失败时保留两条原因。扩展仍须响应取消并结束回调，框架不会强制终止任意托管代码或同步 native 调用。

## 配置和控制台

JsonConfigStore 拒绝根文档及账号设置中的未知字段；修正拼写错误，旧字段通过显式 schema migration 转换，不能依赖未知字段被丢弃。

实时 DMA 连接参数不再允许 -norefresh。native 短读的 Bytes 一律为空；如果旧 decoder 用短读的字节计数推断有效前缀，应改成独立字段请求并仅解析 Complete 的块。正式模块身份查询遇到地址 0 会报检查失败，而不是产生伪身份。

Console 默认仍为一秒演示。--run 常驻，Ctrl+C 请求有界停止；--duration-ms 指定有限时长，与 --run 互斥。硬件执行仍需 --run-hardware。退出 JSON 去掉原先写死的 Modules=0，增加 Failure；账号运行失败即使清理后状态为 Stopped 也保留失败原因与非零退出码。

## 回放和生成项目

现有 CreateReader<T,TP>(generation, channel, partition, clock) 把一个选定分区用于无分区分析。真实分区通道使用新增 CreatePartitionedReader<T,TP>(generation, channel, clock)，直接注册原分区类型；不同键共享时钟，未知键按冷启动等待，不借用其他键的数据。

“模板不得有业务”只检查模板源工程。生成项目可以加入正常业务类型；分层依赖、原始读取和数据质量边界检查继续运行。生成工程中的 TemplateSmoke 明确显示不适用，普通构建、业务测试和架构测试照常运行。
