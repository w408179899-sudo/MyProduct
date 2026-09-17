# 0.4 验证记录

日期：2026-09-17。公共版本：0.4.0。上一轮记录保留在 [0.3 验证记录](VALIDATION-0.3.md)。本页区分最新代码验证、较早版本持续负载和真实硬件验收。

## 本轮改变

- 保留 Roadhog 的正式快照原则：成功立即发布、Partial 合并、Failed 保持、冷启动等待、会话隔离。
- 同步失效和异步收束分开；会话关闭等待所有代次采集、观察者及取消回调。取消异常不能跳过输入、模块或会话清理。
- 失败清理保留 worker、设备租约与共享工厂，恢复后可重试；并发与重复释放不重复销毁资源。
- 框架 timer 通过可等待的 CancelAsync 触发取消；动作先固定超时状态，再释放输入，最后等待回调。停止操作与回调同时失败时保留两条原因。
- 分区通道可直接接入共享时间轴回放；未知键不能借用其他键的数据。
- 配置拒绝未知字段，旧字段通过显式迁移处理；释放后的工作区拒绝配置写入。
- Console 支持常驻、Ctrl+C、有限时长、严格参数和故障退出信息。
- 生成项目允许添加正常业务类型，继续执行分层与原始读取边界检查。
- native 短读不再暴露未经证明有效的字节前缀；正式模块身份拒绝地址 0，实时连接拒绝 -norefresh。
- 打包、消费者、性能与持续负载支持独立构建目录；隔离包验证只使用本轮额外包源，防止同版本旧包掩盖结果。

## 最终软件验证

以下测试包含最终 native 边界和安全时限修复，不沿用此前 140/152 项测试结果。

| 验证 | 结果 | 证据 |
|---|---|---|
| Release 源码 | 161/161；Data 56、Runtime/Hosting 79、Architecture 4、模板/Windows/Console 22 | artifacts/source-0.4-release.log；artifacts/core-0.4-release/test-results |
| 新名字生成项目 | 四层合成夹具；构建零警告、零错误；162/162；Mock Console 正常退出 | artifacts/template-extension-0.4-verified.log；artifacts/template-smoke/87b4872b4d284368bfed0d1a5f301a4a |
| 七个公共 NuGet 包实际消费者 | 161/161，资产中的公共依赖确为 package | artifacts/package-0.4-release-verified.log；artifacts/packages-0.4-release/test-results |
| 包内容一致性 | 七个消费者缓存 nupkg 与本轮产物 SHA256 全部一致，已更新 artifacts/packages 中的 0.4.0 包 | artifacts/packages-0.4-release/verified-package-hashes.json |
| Desktop | 最终隔离构建零警告、零错误；隐藏 Mock smoke 退出 0 | artifacts/desktop-0.4-release.log；artifacts/desktop-0.4-release-smoke/result.json |
| 可控目标协议/进程 | 33/33；一秒目标进程 smoke 通过 | artifacts/probe-0.4-release.log；artifacts/probe-tests；artifacts/probe-target-smoke/02fb338dbb8e49559ab7a2a9a244eadd |
| 只读探针替身故障 | 17/17，不打开真实设备 | artifacts/probe-adapter-0.4-release.log；artifacts/probe-adapter-0.4-release/test-results |
| Windows x64 自包含目标 | 发布成功；exe 一秒 smoke 退出 0，计数和撕裂窗口均出现 | artifacts/probe-target-win-x64；artifacts/probe-target-published-smoke/f38f90e5f530493894984d8b492d1d3b |

停止旧测试后，又执行默认输出目录的 `scripts/Build.ps1 Build`，零警告、零错误，确保本地常规源码构建已更新；日志 artifacts/default-build-0.4-release.log。此项是默认产物更新，不另算一轮测试。

最初加入纯领域 Health 类型的 161 项生成验证，仅证明可以增加领域类型，未覆盖真正注册第一个通道与模块。本轮贯穿验证复现了消费者仍被 Assert.Empty 拦住的问题。已将“空目录、空模块”限制为模板源专属断言；所有消费者继续检查目录封闭、模块依赖与声明通道。红例证据 artifacts/consumer-extension-red-result.json。

最新生成夹具同时保留 Health 类型，并在一次性产物中加入 Domain 合成值、Infrastructure 读取器、Application 模块与强类型参数、Bootstrap 注册及集成测试，通过实际 ProjectHost 运行两个账号。每个账号先发布 7，后续读取失败仍保持该正式值；参数产生不同 Mock 按键码，模块收到第一次动作成功反馈后再提交第二次动作。停止后两份 Mock 输入租约可重获，不将其当作真实按键释放证据。生成消费者 162/162 通过，其中 23 项为消费者测试；完整 TRX 位于该产物的 artifacts/template-test-results。

模板源对应的 3 项测试另行通过，见 artifacts/template-source-audit.log 及 artifacts/template-source-audit/test-results；文档中的 Test-Desktop.ps1 命令实际执行通过，见 artifacts/desktop-script-audit.log。本次仅修改模板测试和验证脚本，未修改公共运行核心，因此未重跑此前通过的包、性能或持续负载。源模板仍不包含业务，分层和数据边界检查继续执行。

初次隔离包验证确实读取了旧同版本包，因缺少新增实现而编译失败，见 artifacts/package-0.4-release.log。修复本轮包源隔离后重跑通过，并核对实际消费的包内容。源码通过不代替包通过。

## 故障复现与修复证据

- Runtime 原有取消异常、清理重试和并发释放：artifacts/test-results/runtime-safety-red.trx 与 runtime-safety-green.trx。
- 宿主停止异常：4 项旧版均失败，修复后通过，见 hosting-shutdown-red.trx 与 hosting-shutdown-green.trx。
- 自动时限：旧 CancellationTokenSource 自动 timer 的三条独立子进程路径均因抛错回调异常退出，finally 未执行。最终 CancellationDeadline 的直接 token 和 linked token 两条真实 System timer 路径均退出 0，执行 finally，DisposeAsync 观察到回调异常。证据 artifacts/deadline-callback-probe/results.json、helper-results.json、helper-source.sha256。
- 新增 9 项时限行为回归包含先释放再等待回调、Disarm 两阶段、执行门闩恢复、设备 lease 保留重试及模块双错误汇总；已包含在最终 Runtime 79 项中。动作保留输入前先调用 Disarm 固定到期状态，该调用顺序另经代码审阅。

## 性能与持续负载

最新代码的全部既定性能门槛通过。报告 artifacts/script-isolation-check/benchmark.json；以下为本机合成样本，不是跨机器或具体游戏性能保证。

| 性能场景 | 本机样本 |
|---|---|
| 100000 次缓存读取 | 捕获 1 次；38.71ms；分配 6584B；P99 0.3μs |
| 10000 次捕获/合并/发布 | 10.68ms；P99 5.1μs |
| 2000 对象 × 1000 次 Partial 合并 | 299.63ms；结果对象 1882 |
| 1/4/8 账号，各 4 模块，各 3 秒 | CPU 时间 109.375/15.625/62.5ms；进展和 CPU 门槛通过 |

最新代码的 300.17 秒、8 账号×4 模块故障负载已通过，报告 artifacts/soak-0.4-release/soak.json，完整日志 artifacts/soak-0.4-release.log。共建立 792 个会话，完成 201498 次决策，经历 508 次传输失败与 65 次输入失败。停止耗时 15.71ms；持有输入、排队读取、活动读取均为零，日志零丢弃。GC 后保留托管内存变化 -101472B，句柄变化 +3，均在既定门槛内。CPU 时间样本 1000ms，换算为测量期平均约 0.00333 个核，不能外推为真实硬件或复杂业务负载。更早的十秒脚本验证记录位于 artifacts/script-isolation-check/soak.json。

较早版本曾于本地 04:35 启动计划一小时的负载，日志 artifacts/soak-0.4-hour.log。启动后新增了 native 与安全时限修复，因此该运行不能代表最终代码；最新版本的五分钟负载完整通过后，已主动停止确切的旧测试子进程以释放构建文件。该轮未完成、不计为通过，也不声称完成一小时验证。所有最终检查均使用隔离输出，未覆盖运行中的 DLL。

## 真实硬件边界

没有连接现场 DMA/KMBox，没有使用游戏账号，没有初始化真实输入；没有改动或部署现有 Roadhog，没有提交或推送。所有负载均为合成传输与输入；native、固件、物理输入落点、真实断连恢复和数小时设备运行仍需按 [实机验收](HARDWARE-ACCEPTANCE.md)完成。

已准备 [无游戏业务的目标进程和只读探针](PROBE.md)，实测需要指定空闲设备、DLL、目标电脑及仍在运行的目标 manifest。框架不能替代每个游戏自己的读取映射、领域不变量、算法规模和动作结果确认；也不能强行结束不响应取消的扩展代码或 native 调用。
