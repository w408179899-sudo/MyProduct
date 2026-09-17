# 只读 DMA 验收探针

本工具只读取正在运行的 `Smart.ProbeTarget` 夹具。目标电脑生成的 manifest 指定 PID、实际进程/模块名称、会话和 96 字节地址；不自动挑选设备、枚举挑选进程、读取现有脚本配置或发送键鼠输入。

离线构建和 fake transport 测试（所有输出放在独立目录）：

```powershell
.tools\dotnet\dotnet.exe test tests/Smart.HardwareProbe.Tests/Smart.HardwareProbe.Tests.csproj -c Release --artifacts-path artifacts/probe-verification
```

只有明确准备好主控电脑的设备与 DLL，以及仍运行中的目标电脑 manifest 后，才执行真实读取。例如以下参数中的路径和设备编号必须替换成实际提供的值：

```powershell
.tools\dotnet\dotnet.exe artifacts/probe-verification/bin/Smart.HardwareProbe/release/Smart.HardwareProbe.dll --library C:\Hardware\vmm.dll --device fpga://devindex=5 --manifest C:\Probe\target.json --duration-ms 30000 --max-progress-gap-ms 5000
```

`--library`、`--device`、`--manifest`、`--duration-ms` 必填。路径必须为绝对路径；`--duration-ms` 为 100–3600000，`--poll-ms` 默认为 5、范围 1–1000。不传必需参数只会报错，不会初始化 DMA。`--help` 仅显示用法。

`--max-progress-gap-ms` 是硬件验收的最大 Counter 停顿，范围 1–3600000。默认值为夹具普通间隔 + mixed/torn 的撕裂窗口 + max(250 ms, 4 × 轮询间隔)。停顿统计包括初始化到首帧、有效计数变化之间，以及最后变化到测量结束。验收同时要求：观察时长至少为该阈值的两倍、至少两个不同的正式 Counter、所有停顿不超过阈值、停止后资源清理成功。初始化较慢时应预先配置合适阈值和足够时长；报告会列出实际最长停顿与判定阈值。

长期读失败仍保留提供者的正式旧快照；验收报告会因 Counter 停顿超标而 `Passed=false`。该指标只属于硬件测试，不是业务数据的过期策略。`TornReads` 仅计算更新中/首尾序列不一致；校验和、结构及回退序列计入 `ValidationRejected`；短读和 I/O 异常计入 `ReadErrors`。损坏的会话字节必须先通过校验和，才可能触发真正会话变化。合法零值由正式发布观察器计数。

总体 deadline 从初始化前开始，覆盖初次绑定和冷启动。native 调用无法强制打断；发生软超时后仍等待已开始的 native 调用返回并关闭连接，期间保持物理设备租约。报告分别提供测量时间和停止时间。取消返回码为 130，通过为 0，其余为 2。

CPU 与分配指标包含此诊断工具的初始化和每次读取前后的精确进程/模块校验（VMM 每次校验可能读取模块头）；这些结果不能直接代表业务读取循环的性能。本目录的自动测试使用假传输和假时间，不会加载真实 DLL；未提供真实参数时，不代表完成硬件验收。
