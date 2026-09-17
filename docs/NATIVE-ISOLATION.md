# DMA 原生进程隔离

0.5.0 的 VmmConnectionPool 与 Console `--probe` 默认使用 IsolatedVmmTransport。
同一连接池中的同一设备共享一个 worker、一个有界调度队列和一套 native 连接。
业务模块仍只引用 Contracts 中的正式快照与动作接口，进程通信不进入业务代码。

## 时限与所有权

worker 在自己的进程中加载 VMM/D3XX、校验可选设备绑定、打开 DMA、读取和关闭。
初始化默认 30 秒，每次操作 5 秒，关闭 3 秒；Dma.Worker 可配置 100..120000 毫秒。
这些是各阶段上限，不是整个账号停止流程的单个总时限；等待正在执行的操作和终止确认也需要时间。
管道 RPC 一次传递整个批次，最多 256 段、总计 1 MiB；消息有 4 MiB 上限。

私有 Windows Job 在发送初始化请求前接管新 worker。父子管道限定当前用户、核对双方 PID。
父进程崩溃后 Job 关闭会终止自己的 worker；依据 [Windows Job Objects 的 KILL_ON_JOB_CLOSE 语义](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)。
此机制从不按名称查找并结束其他脚本进程。

跨进程文件租约由 worker 持有。打开或关闭 native 卡住时仍持有租约；宿主超时后终止自己的 worker，并等待进程实际退出。
终止无法确认会报错，不能声称设备已释放。重连还必须等旧账号 scope、读取队列及输入清理完成。
父进程仅保留池内所有权；不会在 native 尚存活时先释放实际文件锁。

## 数据语义

普通失败或短读不触发连接退役，provider 保持原正式快照；有效新数据立即发布，Partial 仍按已注册策略合并。
worker 退出、超时或协议损坏是连接生命周期结束。所有共享该连接的账号立即使旧快照会话失效；晚到采集不能重新发布。
新的连接及账号 scope 从新的首个正式值开始。设备绑定不符不会退回未绑定模式。

## 部署与边界

宿主的 `native-worker/` 必须包含 Smart.Dma.Worker 的 exe、dll、deps.json 和 runtimeconfig.json。
源码引用和 Smart.Adapters.Dma 包的 buildTransitive 规则负责复制；发布脚本按宿主相同运行模式发布 worker。
缺少 worker 明确报错，没有隐式进程内回退。外部 VMM/D3XX DLL 仍由部署方提供，不进入模板或发布包。

公开 VmmTransport 保留为低层直接适配器，旧的原生诊断探针仍可显式使用它，其同步调用无法强制取消。
上述硬时限只覆盖 IsolatedVmmTransport 路径。桌面主动“绑定 DMA”的只读驱动枚举目前在宿主后台线程执行，尚未迁入 worker。
无法保证返回的自定义业务 Tick、用户取消回调、原生内核/USB 驱动失效，都不是此进程隔离可以完全修复的问题。
设备是否支持被其他进程占用时完整枚举，以及真实拔插后的驱动行为，需要在对应部署环境验证。

离线进程故障测试、实际发布文件与实机结果分开记录在 [本轮验证](VALIDATION.md)。
