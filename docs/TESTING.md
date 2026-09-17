# 测试和性能验证

测试默认不连接硬件，不包含项目业务。五组测试分别覆盖数据契约、运行和宿主故障、架构边界、空模板与连接池，以及真实操作系统进程隔离。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build.ps1 Test
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build.ps1 TemplateSmoke
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Desktop.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Packages.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Diagnostics.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Performance.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Soak.ps1 -DurationSeconds 300 -Accounts 8
```

Test 覆盖读取失败保持、部分字段/集合更新、冷启动、并发、会话失效、输入异常清理、持续输入优先级、动作条件、模块依赖/生命周期、异步读取隔离、通道声明、账号重连、配置迁移、跨运行日志清理、实际发布记录回放和共享连接。

TemplateSmoke 在独立目录生成新名字的工程，加入仅供验收的合成数据与模块，再构建、测试和运行 Mock Console。Desktop smoke 使用独立配置目录验证加载、启动、暂停、重新启动和退出。

模板生成检查通过 Add-ConsumerFixture.ps1，仅向 artifacts 内新生成的 SmokeProject 写入验收夹具：Domain 不可变数据、Infrastructure 合成读取、Application 强类型通道和模块、Bootstrap 实际注册，以及调用真实 ProjectHost 的集成测试。两个账号使用不同模块参数；检查正式快照、读取失败后的保持、动作成功反馈及停止后输入租约可重获。它不加载真实硬件，也不代表物理按键释放验收。

源模板保持无业务。“目录和模块为空”只约束模板源；生成项目注册自己的功能后仍需通过装配、分层和原始读取检查。其 TemplateSmoke 显示 TEMPLATE_GENERATION_NOT_APPLICABLE，避免业务消费者的 CI 再次安装不存在的模板源。新项目应继续添加自己的业务回归测试。

Test-Diagnostics 独立验证三个通用诊断工具：NativeSmoke 的 PE 边界及批内失败隔离、SnapshotSmoke 的正式快照契约、KmBoxProbe 的 Connect 握手及超时/取消/租约。测试仅使用替身传输和本机回环 UDP，不加载真实设备；已加入 CI。真实运行必须另行提供明确的设备和目标参数，结果见 VALIDATION.md。

Smart.Isolation.Tests 在 Windows x64 启动单独编译的 Fake worker，复用真实管道服务端、客户端和 Windows Job；替换的仅是连接工厂。覆盖卡死、崩溃、租约、父进程死亡和重新连接，不使用 VMM DLL。测试专用故障入口不进入正式 worker。5 秒无节流 IPC 样本单独记录父子 CPU、分配和尾延迟，不能代替真实 DMA 吞吐。

0.4.1 到当前包的固定外部消费夹具由 Test-Upgrade.ps1 执行，分别验证同源码重编及旧消费者 DLL 原样升级，并比对旧公开 API 结构和实际包哈希。Test-PackageWorker.ps1 检查 NuGet 安装、build/publish 传播的 worker 文件及无硬件 help 启动；发布流程见 [应用交付](DELIVERY.md)。

## 性能门槛

benchmarks/Smart.Benchmarks 测量缓存读取、真实 provider 捕获/合并/发布路径的 P95/P99 与分配量、2000 对象集合的 1000 次 Partial 合并，以及 1/4/8 个账号各 4 个有界合成模块。

Test-Performance.ps1 依据 benchmarks/budgets.json 检查捕获次数、结果对象数量、尾延迟、CPU 时间和最低调度进展。输出 artifacts/benchmark.json。门槛用于发现明显回归，不是跨机器的实时性能保证。

三秒合成多账号样本不能证明真实 DMA 延迟、复杂项目算法效率或长时间无泄漏。正式项目应增加自己的规模边界、故障频率和持续运行负载，保留硬件型号及 native 版本。

Test-Soak 使用 8 个账号各 4 个模块共享一个真实 DmaDispatcher，底层为会阻塞、短读和抛异常的合成传输。启用快照记录，持续竞争 Mock 输入，注入输入失败，并每约 3 秒重建会话。检查队列上限、会话恢复、计算进展、停止后输入全部释放、native 队列排空、日志无丢弃，以及 GC 后保留内存和句柄增长。结果写入 artifacts/soak.json。

参数支持 10..86400 秒和 1..32 个账号，可在空闲机器执行数小时验收。默认 60 秒适合 CI；本轮测量时长和指标以 VALIDATION.md 为准。CPU 使用 Process.TotalProcessorTime 换算为占用的 CPU 核数，不冒充整机任务管理器百分比。

如果已有宿主或持续负载正在运行，`Pack.ps1`、`Test-Packages.ps1`、`Test-Performance.ps1` 和 `Test-Soak.ps1` 支持 `-ArtifactsPath artifacts/<本次目录>`。编译输出和中间文件都放到该目录；包验证另外隔离框架、消费者和缓存，确保读取本次打出的包；持续负载的诊断文件也隔离。每个并行验证使用不同目录，不覆盖运行中的程序集。省略参数保持默认路径。

## 架构检查

自动遍历非产物目录的 *.Domain.csproj / *.Application.csproj，检查依赖方向和禁止原始读取/硬件直连；新业务工程沿用命名约定即可被覆盖。运行时另以 RequiredChannels 限制模块实际读取，防止只注册通道却忘记声明消费者。
