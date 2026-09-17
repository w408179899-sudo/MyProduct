# 0.4.1 验证记录

日期：2026-09-17。公共版本：0.4.1。此前的生命周期、性能与持续负载证据保留在 [0.4.0 验证记录](VALIDATION-0.4.0.md)。本轮使用用户明确指定且已退出脚本的账号 2 配置，完成首轮真实 DMA 和 KMBox 通信验证。

## 实机发现与修复

最初 Console、连接池及 HardwareProbe 把程序名放在 VMM 参数第 0 项，导致 VMMDLL_Initialize 返回空句柄。使用同一设备和同一套原生 DLL，仅将第 0 项改为空后初始化成功，排除了本次失败由游戏偏移引起的解释。

新增 VmmTransport.CreateArguments，统一构造空首项、设备 URI 和额外选项。三个正式入口和通用诊断工具均复用；加载 DLL 前拒绝程序名首项、重复设备参数、NUL、超出预算的参数和关闭实时刷新的选项。原生 DLL 加载策略、provider 正式快照规则及运行调度没有修改。

最小证据位于 artifacts/hardware-account2/20260916-214832：原失败日志、仅空首项的成功对照 init-empty-first.stdout.log，以及最终正式 Console 成功输出 dma-inspection-fixed.json。新增 18 项参数回归；错误程序名的两项测试先在旧实现上失败。

## 0.4.1 软件验证

| 验证 | 结果 | 证据 |
|---|---|---|
| Release 源码 | 179/179：Data 74、Runtime/Hosting 79、Architecture 4、模板 22 | artifacts/source-0.4.1-release.log |
| 新名字生成项目 | 180/180；构建零警告、零错误；双账号四层扩展通过；Mock 正常停止 | artifacts/template-0.4.1-release-verified.log；artifacts/template-smoke/51a2c51453b142459726bdbc6097b03b |
| 七个 0.4.1 公共包的实际消费者 | 179/179；两个消费者资产中的公共引用均为 package | artifacts/package-0.4.1-release.log |
| 包内容核对 | 七个实际缓存 nupkg 与本轮 feed SHA256 一致；产物复制到 artifacts/packages 后再次一致 | artifacts/packages-0.4.1-release/verified-package-hashes.json |
| HardwareProbe 离线适配器 | 17/17，不打开设备 | artifacts/probe-adapter-0.4.1-release/test-results |
| NativeSmoke 离线边界 | 25/25，包含批内坏读隔离、合法块短读和异常清理 | artifacts/native-smoke-verification/test-results |
| KMBox Connect 探针离线协议 | 10/10，只使用本机回环 UDP | artifacts/kmbox-probe-verification/test-results |
| 最终诊断工具统一门槛 | 47/47：NativeSmoke 25、KmBoxProbe 10、SnapshotSmoke 12；已加入 CI | artifacts/diagnostic-0.4.1-release.log；artifacts/diagnostic-0.4.1-release/test-results |
| 新项目中的诊断工具导出 | 独立模板 hive 生成后 47/47；9 个关键工程/脚本/CI 文件与源 SHA256 一致；187 个导出文件不含本机产物、原生 DLL 或真实账号配置 | artifacts/generated-diagnostics-0.4.1.log；artifacts/diagnostics-export/60372f7280a946bc976bc5698607e799 |

首轮模板生成出现一次 RuntimeSafetyTests 测试夹具竞态：独立的可取消 Delay 可能先唤醒 Send，注销尚未运行的抛错回调，使预期异常没有发生。生成前后的测试和生产 ActionExecutor 源码哈希一致。仅将测试唤醒改由该真实 token 回调完成，随后 20 次独立重复全部通过，再完整生成验证通过。失败日志 artifacts/template-0.4.1-release-failed.log 与原 TRX 保留；重复证据在 artifacts/runtime-callback-fixture-verification。未以简单重跑替代修复，生产 Runtime 未因这项测试修改。

最终测试夹具另外对同一批实际 0.4.1 包重新验证，RuntimeSafetyTests 9/9 通过；结果 artifacts/packages-0.4.1-release/runtime-safety-fixture-final。七包在 feed、消费者缓存和 artifacts/packages 的 SHA256 再次核对一致，没有重新打包覆盖先前产物。

默认输出目录也已执行 scripts/Build.ps1 Build，包含 Desktop，零警告、零错误；证据 artifacts/default-build-0.4.1-release.log，确保常规启动产物使用修正后的初始化路径。

SnapshotSmoke 独立审阅另外发现诊断 scope 的取消异常可能越过清理，以及 Run/Dispose 初始化窗口竞态。仅修正该验证工具：控制任务错误一次汇总，继续按依赖顺序收束；仍在途 native 或关闭失败不释放租约；scope 结束后拒绝启动。新增 5 项故障回归与原 7 项一起通过，包含真正卡住 Acquire 的并发复现。最终统一诊断门槛使用该修正版本；公共 Core 未改变。

独立 Diagnostics.slnx 的工具与测试按 Release 构建，未显式列入该解决方案的部分 Core 项目引用由 SDK 使用 Debug 输出；本轮将其作为行为和导出验证，不作为 Release 性能证明。主 Smart.slnx 与实际 NuGet 包的 Release 验证另行完成。实机诊断时间也只表示对应探针样本。

## 真实 DMA 与 KMBox

使用账号 2 的明确设备映射 fpga://devindex=0。连接前核对脚本进程、USB 绑定和 Roadhog 设备租约，其他账号继续运行。原生版本为 VMM 5.16.13.227、LeechCore 2.22.7.93、FTD3XX 1.3.0.10；原文件 SHA256 留在本轮 native-versions.json。设备报告 FPGA Enigma X1 PCIe gen2 x1；目标系统报告 Windows 10.0.19041。

正式 Console 成功枚举 141 个进程，选中已运行的 Aion.bin，PID 5612，模块基址 0x140000000，scatter 入口可用。进程数量是当时样本，不能当作固定值。测试只读取通用 PE 头和预期失败的虚拟地址 0，不使用游戏偏移或业务字段。

| 真实检查 | 结果 | 本轮证据文件 |
|---|---|---|
| 一秒 PE/Scatter smoke | 9 轮通过；9 个无效块单独失败；27 次调度 native 调用；正常退出 | native-smoke-1s.json |
| 重新打开后的一分钟 PE/Scatter smoke | 测量 60.002 秒；538 轮通过；538 个无效块单独失败；1614 次调度 native 调用；零非预期迭代失败 | native-smoke-60s.json |
| 一分钟结束清理 | 36.38ms；CleanupComplete=true；Queued=0、Active=0 | native-smoke-60s.json |
| 实际 provider 快照契约（最终版本） | 6 项检查通过；7 次无效地址失败、3 次成功采集和正式发布；版本依次为 1:1、1:2、2:1；队列与在途采集归零 | snapshot-contract-verified.json |
| 500ms deadline 预期失败检查 | 正确退出 2、Outcome=Deadline；初始化返回时已耗时 655.29ms，再用 108.37ms 完成清理；CleanupComplete=true | snapshot-deadline-500ms.json |
| KMBox Connect 握手 | 发送完成 1 包，收到匹配的 16 字节响应；11.56ms；socket 和租约已释放 | kmbox-handshake.stdout.json |

每轮先有依赖地读取 DOS/PE 前缀，再同一批读取两个有效块与地址 0 坏块。报告的 native 调用数是 DmaDispatcher 调度调用，未包含身份检查内部的读取，也不是物理 DMA 事务数。预期的坏块不是整次调用异常，故可同时出现 ZeroReadRejected>0 和 Dma.Failures=0。模块头通常静态，不能由这些数字宣称游戏数据持续更新或证明跨字段原子读取。

KMBox 工具直接发送 Connect 协议，未构造会自动 ReleaseAll 的 vendor 设备对象。本次未发送按键、鼠标、ReleaseAll 或重启命令；成功响应只证明端点通信，不证明目标桌面的输入、释放或落点效果。

SnapshotSmoke 使用真实设备、DmaDispatcher、封闭 typed catalog 和 SnapshotProvider，没有第二份测试快照缓存。冷启动读失败时不返回值且等待可取消；成功后立即发布；随后实际坏读完成时，正式值、版本、时间和发布计数均保持；恢复读取产生新版本；显式 Reset 后先等待新首值，再在新代次发布。这个场景验证静态 PE 与真实坏地址的集成语义，不将显式 Reset 当作已验证目标进程退出或设备拔出。

最终快照成功场景紧接 500ms deadline 负例执行，重新获取同一设备并正常完成，耗时 1256.03ms、清理 71.24ms。它证明这次初始化软超时后的关闭与重新打开可行，不能替代物理拔出、网络中断或不返回 native 调用的验收。最初正常场景 snapshot-contract.json 保留作为修正诊断异常路径前的证据。

## 性能及剩余验收边界

0.4.1 的生产改动仅在 VMM 初始化参数路径；此前 [0.4.0 的性能与五分钟故障负载](VALIDATION-0.4.0.md)仍作为对应读取/provider/调度实现的已有证据，本轮没有重新计为性能或持续负载验收。一分钟静态 PE 读取不代替数小时设备运行，也不代表具体项目的 CPU、读取吞吐或动作频率。

尚需可控目标完成真实动态数据、半写/混拼、目标退出与重启的验收；尚需可控桌面检查真实输入与释放，以及物理断连恢复和长时间运行。已有独立 [ProbeTarget](PROBE.md) 与 [实机验收流程](HARDWARE-ACCEPTANCE.md)。native 同步调用仍不能强制取消；等待其返回并完成清理前不能提前释放设备租约。

没有加入游戏业务或卡密，没有修改或部署 Roadhog，没有提交或推送。模板能够提供公共读取、快照、模块、调度和生命周期约束；不同项目仍需自己的读取映射、领域校验和动作确认。
