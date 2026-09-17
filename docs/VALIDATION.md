# 0.5.0 验证记录

日期：2026-09-17。目标仍是无游戏业务、无卡密的公共 C# 模板。本轮增加原生进程隔离、可选 DMA 设备身份绑定、固定旧包升级验收和完整应用发布。
原 [0.4.1 实机记录](VALIDATION-0.4.1.md) 已归档，保留首轮 DMA/KMBox 通信证据。

## Visual Studio 构建补验（2026-09-17）

- 安装 Visual Studio Community 2026 18.10.1、.NET 桌面开发工作负载和系统 SDK 10.0.401；安装器退出码 0，无需重启。VS 2022 17.14 不支持本项目的 net10.0，仓库内私有 SDK 不能替代 IDE 升级。
- 使用新版 `devenv.com /Rebuild` 构建独立源码副本，21 个项目全部成功。实际复现 IDE 会跳过 ProjectReference 的自定义 worker 输出目标，导致 17 项隔离测试缺少 fake-worker 而失败。
- 补齐 IDE 对已构建 worker 的内容收集，Console/Desktop 收到完整四文件，隔离测试收到完整 fake-worker；修正 SampleProject.Tests 的 x64 目标以匹配引用的宿主。
- 修复后 IDE 构建产物测试 224/224、原 Release 命令行测试 224/224、重新生成模板测试 225/225，均通过。
- 另通过 VS 2026 自动化打开实际 Smart.slnx，等待项目加载后执行 IDE Build：失败项目数 0，错误列表为空。没有连接 DMA/KMBox 或部署现有脚本。
- 本机证据：`artifacts/vs2026-install/result.json`、`ide-build-fixed.log`、`ide-tests-fixed.log`、`ide-dte-result.json`、`cli-tests.log`、`template-smoke.log`。本次源码修复未重新打包旧的交付产物。

## 最终软件门槛

| 检查 | 最终结果 | 证据 |
|---|---|---|
| 默认 Release 源码 | 224/224；Data 74、Runtime/Hosting 79、Architecture 4、模板/绑定/连接池 50、进程隔离 17；零警告、零错误 | artifacts/source-0.5.0-final.log |
| 新名字生成项目 | 225/225；双账号四层扩展夹具仅存在于产物；Mock 正常停止 | artifacts/template-0.5.0-final.log；artifacts/template-smoke/1eedaf77ca534978be82b79c841ad3e3 |
| 七包实际消费者 | 224/224；隔离缓存并检查实际 package 引用 | artifacts/package-0.5.0-final.log；artifacts/packages-0.5.0-final |
| 0.4.1 → 0.5.0 升级 | 旧源码旧包、同源码换新包、旧消费者 DLL 原样换框架三轮共 18 项行为通过；旧消费者和配置 SHA 不变 | artifacts/upgrade-0.5.0-final/20260917-111544-336cd26c/upgrade-summary.json |
| API 兼容结构 | 旧 1721 条到新 1953 条；旧签名无缺失/变化、无新增抽象实现义务；删除签名负例按预期失败 | 同上目录的 API 结果及负例日志 |
| 最终诊断工具 | 49/49：NativeSmoke 26、SnapshotSmoke 13、KmBoxProbe 10；11 个项目实际 Release 闭包验证通过 | artifacts/delivery-0.5.0-verified-diagnostics.log |
| ProbeTarget / HardwareProbe | 33 项本机目标协议/有限运行验收、17 项离线适配器故障通过；Release 闭包通过 | artifacts/delivery-probe-verification.log 及对应适配器日志 |
| 最终 NuGet worker 部署 | 独立包消费者 build/publish 均收到四个 worker 文件，SHA 与包一致，apphost --help 正常退出 | artifacts/delivery-0.5.0-verified/delivery-summary.json |
| 最终应用发布 | 框架依赖 70 文件、自包含 976 文件；每种模式 Console/Desktop Mock 与各自 worker --help 全部正常；运行前后交付文件 SHA 不变 | 同上 delivery-summary.json |

最终七包在 artifacts/packages-0.5.0-final/framework/packages，也按相同 SHA 复制到默认 artifacts/packages；核对记录为 artifacts/packages-0.5.0-final/verified-package-hashes.json。
可复用应用路径由 delivery-summary.json 的 Publications 指向。旧候选仍保留，正式入口是 **delivery-0.5.0-verified**，不是旧 delivery-0.5.0-final。

## 关键故障验证

隔离测试启动真实 Windows 子进程、命名管道与私有 Job，只把 native 连接工厂替换为独立编译的假设备。
覆盖初始化/读取/关闭卡死、普通坏读保持连接、短块清空、目标不存在、worker 崩溃、相邻设备不受影响、同设备租约冲突、重新连接和 worker 缺失。
父进程死亡测试仅终止测试父进程，不使用进程树结束，由 Job 回收 worker；随后确认文件租约可重获。

内部启动 deadline 明确报 TimeoutException，监督器可进入重连；真实调用方取消仍报 OperationCanceledException。
原生 DLL 缺失、格式错误、入口缺失属于配置错误，不能无限当作瞬时故障重连。
worker 的设备标识必须等于 native 参数实际设备，缺失和错配均在启动前拒绝。
共享 worker 死亡即时失效每个账号的正式快照；普通读取失败仍保持相同对象、版本与时间。

一次崩溃负例显示，进程退出后文件锁仍可能约 58ms 才完成系统回收；原失败 TRX 及 SharingViolation 完整异常保留在 artifacts/isolation-verification。
新连接不能绕开尚未释放的文件锁；测试仅验证它在短时间内释放，不把 PID 消失等同于瞬间可以重新获取设备。

设备绑定的 20 项离线回归证明 D3XX 原始索引、唯一序列号、拓扑和 DLL 指纹的拒绝规则与配置往返。
本轮未对真实设备启用 Binding；账号 2 继续使用人工确认的旧 URI。不得把这次实机读取称为物理绑定验收。

## 账号 2 的真实只读验证

连接前重新核对脚本进程、USB 实例和 Roadhog 租约；账号 2 的脚本未运行，其他五个 Roadhog 进程未被停止或替换。
设备为 fpga://devindex=0，VMM 库仍读取用户原目录。本轮只通过隔离 worker 读取通用 PE 头和预期失败的地址 0，没有游戏偏移或输入指令。
原生版本沿用 0.4.1 记录的用户部署版本，本轮未升级或替换原生 DLL。

| 项目 | 结果 | artifacts/hardware-account2/20260917-isolated 下证据 |
|---|---|---|
| 正式 Console probe | 142 个进程；Aion.bin PID 5612、模块 0x140000000；scatter 可用；worker 正常退出 | console-probe.json |
| 一分钟隔离读取 | 60.005 秒，529 轮成功；529 个坏地址单独失败；1587 次调度调用，零非预期失败；清理 38.17ms、队列与在途为零 | native-smoke-60s.json |
| 最终异常分类修复后的短复核 | 10 轮成功、10 个坏地址拒绝、30 次调度调用；清理 29.99ms | native-smoke-final.json |
| 最终真实快照契约 | 6 项全部通过；7 次坏地址失败、3 次有效采集/发布；Stamp 1:1 → 1:2 → 2:1；清理 61.79ms | snapshot-contract-final.json |
| 最终退出核对 | native worker 数量 0；实际重新打开文件锁确认账号 2 租约已释放；其他五个 Roadhog 进程仍在 | cleanup-final.json |

前后模块指纹一致。分钟样本早于仅异常分类的最后修复，最终同路径又做短读取及完整快照契约复核。
静态 PE 头无法证明真实动态字段原子性，显式 Reset 也不是物理断连。调度调用数不等于底层 DMA 事务数。
本轮没有发送 KMBox 命令；已有 Connect-only 通信证据见 0.4.1，不计为本轮输入验收。

## 效率与持续负载

合成性能门槛通过：10 万次缓存读取只捕获一次，总分配 392B，P99 0.2µs；1 万次捕获/合并/发布 P99 4µs；2000 对象的 1000 次 Partial 合并约 310ms。
1/4/8 账号有界模块调度均满足 CPU 与进展门槛。证据 artifacts/performance-0.5.0-release/benchmark.json。

八账号、每账号四模块的故障负载持续 300.08 秒：792 个会话、203019 次决策、510 次注入读取异常和 68 次注入发送失败；停止 16.44ms，输入持有数 0，读取 Queued/Active=0，日志丢弃 0。
GC 后托管堆增长 -87256B、句柄增长 11，均在既定门槛内。证据 artifacts/soak-0.5.0-release/soak.json。
此负载使用合成传输与 Mock 输入，覆盖的 provider/调度/执行器热路径没有因最后 worker 异常分类修改而变化；不包括真实硬件或 IPC 的多账号成本。

最终隔离测试另保留 5 秒无节流 IPC 样本：每批 128×64B、36746 批，P99 0.3991ms；父/子 CPU 分别 7640.63/6484.38ms，父进程累计分配约 2.23GB，停止 8.95ms。
这是包含测试断言与假数据生成的满速压力样本，不是低 CPU 实际账号配置，也不是 DMA 吞吐；不能只摘取吞吐而隐去 CPU/分配成本。
证据 artifacts/isolation-0.5.0-final/ipc-batch-sample.json。正式运行仍需采样节流、批量读取及规模预算。

## 尚未达到的验收范围

- 受控目标的真实动态变化、半写/混拼、真实进程退出与重启。
- 真实设备绑定、USB 重插/物理断连与多小时实际负载。
- 可控桌面的实际输入、长按释放、坐标效果，以及具体项目的 CPU/吞吐目标。
- Desktop 主动“绑定 DMA”的驱动枚举目前仍在宿主后台线程；运行期 DMA 已隔离，主动发现尚未迁入 worker。

绑定还受完整拓扑元数据限制：其他板卡被 Roadhog 占用而无法枚举身份时也会拒绝受保护连接，不能猜测或降级。
这些限制及下一步验收分别见 [原生隔离](NATIVE-ISOLATION.md)、[设备绑定](DEVICE-BINDING.md) 和 [实机流程](HARDWARE-ACCEPTANCE.md)。
本轮没有添加业务、卡密，没有修改/部署 Roadhog，也没有提交或推送；远程 CI 工作流已补齐，但不宣称已在远程运行。
