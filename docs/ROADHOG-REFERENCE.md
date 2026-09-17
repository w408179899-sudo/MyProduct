# Roadhog 借鉴与改进对照

目标是保留 Roadhog 的公共运行经验，并让具体项目通过数据适配和模块接入。模板不包含其游戏字段、偏移、战斗、恢复、寻路或卡密业务。

| Roadhog 参照点 | Smart 对应实现 | 必须保持的行为 |
|---|---|---|
| DmaSnapshotChannelRegistry | SnapshotCatalog | 强类型通道、显式分区、启动前封闭注册，禁止旁路读取 |
| AionVmmGameApi 的 ReadStable/StabilizeRead | SnapshotProvider + 项目 Infrastructure | 原始读取、校验、合并和发布都在提供者内部 |
| DmaStableSnapshotStore.Resolve | SnapshotSession | 成功立即发布，失败保持已有正式值，冷启动等待首份有效值 |
| 字段感知与集合合并 | CollectionMerger + 项目 merger | Partial 更新有效字段并保留未读全对象；完整有效结果才确认缺席 |
| RoadhogSnapshotReader | ISnapshotReader | 业务只接触正式快照，读质量与重试留在底层 |
| 会话/连接数据隔离 | SessionIdentity + Generation | 换设备、连接、进程或模块后失效旧快照，拒绝晚到提交 |
| 读取复用和采样频率控制 | MinimumCaptureInterval + 单次在途采集 + DmaDispatcher | 业务读取次数与物理读取次数分开，减少重复扫描，不延迟已经读到的有效值 |
| Hardware.KmBox 通信实现 | Vendor/Hardware.KmBox + KmBoxInputDevice | 保留通信适配，公共执行器负责所有权、取消和释放 |
| Roadhog/AGENTS.md 的窄适配器、纯决策、账号隔离、Mock 和诊断约定 | Domain/Application/Infrastructure/Bootstrap + ManagedAccount + 测试 | 添加业务不修改公共主循环，UI 只编辑配置和发送运行命令 |

Roadhog 中业务代码对自身字段的判断不能直接成为通用底座规则。每个新项目仍要定义字段合法性、稳定对象身份和完整遍历条件。动作确认检查后续正式状态；输入指令成功仅代表发送完成。

本轮重点改进：每会话模块配置与 token 装配、慢刷新下旧快照可用、不同设备初始化隔离、可重试连接清理、读取等待不消耗计算预算、停止全程有界等待、配置切换一致性、共同时间轴诊断和合成负载验收。

0.5.0 进一步把正式 DMA native 调用放入独立 worker，用实际进程故障验证初始化/读取/关闭卡死、父进程崩溃和独立设备不互相影响。设备绑定使用 D3XX 实际索引，不复制由 Windows USB 列表排序推断 devindex 的做法；无法确认身份时拒绝受保护连接。新增固定历史包的源码与二进制升级回归，以及发布包 worker 完整性和 Release 检查。

具体频率按项目和硬件测量，不直接继承某个游戏的 tick 或缓存常量。直接 VmmTransport 仍需 native 返回；正式隔离路径确认 worker 退出后再重连，实际 USB/驱动故障恢复仍需单独验收。共享时间轴回放用于复现正式输入与检查动作记录，不保证复现操作系统线程调度。
