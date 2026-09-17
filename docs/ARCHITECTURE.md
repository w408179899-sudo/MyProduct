# 架构与边界

## 工程职责

| 工程 | 职责 |
|---|---|
| Smart.Contracts | 正式快照、模块生命周期、动作和诊断契约 |
| Smart.Data | 通道注册、原始读校验合并、唯一发布器、回放输入 |
| Smart.Runtime | 有界模块调度、依赖验证、动作仲裁、设备租约 |
| Smart.Adapters.Dma | VMM C ABI、scatter/普通读取、有界队列、隔离 worker 客户端 |
| Smart.Dma.Worker | native 初始化/读取/关闭进程、实际设备租约及绑定核验；由 Adapter 包部署，不单独发公共包 |
| Smart.Adapters.KmBox | KMBox 输入适配及来源保持不变的底层通信代码 |
| Smart.Hosting | 配置、账号会话监督、Mock、日志、快照记录、诊断包 |
| Smart.Hosting.Windows | 真实设备组合、连接池、只读设备清单 |
| Project.Domain | 留空，仅供具体项目添加纯规则 |
| Project.Application | 留空，仅供具体项目注册模块 |
| Project.Infrastructure | 留空，提供项目读取映射、字段校验、Mock 与硬件通道注册入口 |
| Project.Bootstrap | 组合项目通道与模块，选择 Mock/Hardware |
| Project.Console/Desktop | 通用运行配置和状态界面 |

Domain 不引用基础设施；Application 仅依赖 Domain 与 Contracts。UI 通过账号状态模型工作，不读取 Mock 内部数据或自行读取 DMA。无需为满足 DDD 形式添加消息总线、数据库或复杂聚合根。

## 数据和模块

所有外部业务数据经 SnapshotCatalog 注册，Seal 后才创建会话。空目录合法，因此不需要虚构一个业务通道才能启动模板。

ModuleCatalog 在启动前检查唯一 ID、最多 64 个模块、依赖完整性、依赖环和 RequiredChannels。模块收到的读取器限制到已声明通道；漏声明会在原始读取前失败。

每模块最多一个进行中的 Tick。一个 Tick 的异步读取尚未结束时，其他模块继续执行。每个模块有独立操作数与时间预算，不会因为前一个模块耗尽预算而长期不获得调度。

TickContext.Snapshots 的等待自动暂停计算耗时预算；操作数预算始终生效。外部读取通过该入口，避免模块自己计时或阻塞调用线程。该预算是协作式计算时间限制，不是操作系统线程 CPU 计量。

SessionComposition 为每个账号会话新建通道 token、强类型选项和模块实例，Mock/Hardware 都调用同一个模块注册方法。模块工厂只收到声明过的通道读取器；配置字段拼写错误、未知模块、依赖错误在激活阶段明确失败。模块构造函数只保存参数，需要释放的资源放到 InitializeAsync/StopAsync 中管理。

同步 CPU 循环无法在进程内强制安全终止。必须限制集合规模、使用 WorkBudget，并把长计算拆成多次 Tick。模块停止必须响应取消。停止超时会保留未完成状态，不伪装成功。

## 输入

模块提交 ActionPlan，执行器统一操作设备。Precondition 在实际执行前判断动作条件，ExpiresAt 防止过期提案。拒绝反馈返回模块，模板不替模块重试旧动作。

UntilOwnerChanges 持续输入会保留所有者与优先级。低优先级或同优先级其他模块不能覆盖；原所有者通过 ModuleResult.ReleaseInput 释放，或被更高优先级动作接管。停止、暂停和会话替换清理输入。

释放失败后禁止继续发送动作；Dispose 可重试。物理释放和设备销毁完成后才释放租约。恢复失败保留旧 scope，使宿主不能创建重复执行器。

## 连接与账号生命周期

ManagedAccount：Connecting → Running → Reconnecting / Stopping → Stopped / Paused / Faulted。模块自身异常只停止该账号；输入传输异常尝试重建会话。清理异常进入 Faulted，保留 scope 供再次停止清理。

硬件会话变化时先取消旧 worker、失效旧快照，再等待动作与模块结束并释放资源，然后创建新 scope。暂停采用停止并释放，继续时创建新 scope，避免保留跨暂停的旧动作状态。

VmmConnectionPool 让相同设备的多个账号共享一个连接、一个 native 队列；进程、模块、快照与模块实例仍按账号隔离。最后一个账号释放才关闭连接。退役连接等待所有旧引用退出，新的连接不得抢占旧租约。

全局连接表只锁短暂元数据操作；native 初始化和关闭分别使用设备自己的锁。关闭失败后重试不会再次减少引用计数，也不会丢失设备租约。

正式 native 工作在独立 worker 进程；共享源编译同一套 VMM 和绑定实现，不复制第二份业务读取算法。启动/操作/关闭分别有时限；启动内部超时属于可恢复连接故障，调用方取消仍表示停止，DLL 缺失/格式错误/入口缺失属于配置错误。worker 退出立即失效共享连接上的全部快照会话；普通坏读仍由 provider 保持原快照。

StopAsync 的超时覆盖整段停止与再次清理。超时后后台清理继续，账号保持 Stopping，不能启动重叠会话。配置切换先禁止旧账号启动并完成清理，再保存文件和替换账号集合；准备或保存失败时，旧配置仍可使用。

根宿主使用 LocalApplicationData/Smart/device-leases 下的文件锁协调同一 Windows 用户的独立进程；锁句柄由 OS 在进程退出后释放。不同系统用户和不同 URI 别名仍需部署方统一设备标识与锁目录。
