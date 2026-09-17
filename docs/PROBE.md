# DMA 验收的独立目标进程

`validation/Smart.ProbeTarget` 是可以放到 DMA **目标电脑**运行的 C# 测试夹具。它只分配并更新自己的 96 字节内存，不包含游戏数据、游戏偏移、卡密、DMA 初始化或键鼠输入。`Smart.ProbeProtocol` 定义这一小块内存的协议与校验方法；真实只读 DMA 探针在主控电脑使用同一个协议。

## 本机离线验证

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Probe.ps1
```

独立 `validation/Smart.Probe.slnx` 只包含协议、目标进程和测试，不引用或重编译主框架、VMM、KMBox。测试检查合法零值、长度/版本错误、混拼数据、校验和错误、会话错配、确定性的更新中窗口、取消和释放。脚本随后运行一秒目标进程，验证文件及 stdout 的 manifest 一致、计数确实变化并且出现过更新中窗口。该验证没有读取 DMA。

## 在目标电脑启动

目标电脑有匹配 .NET 10 x64 Runtime 时，可复制 `validation/Smart.ProbeTarget/bin/Release/net10.0` 整个目录并执行：

```powershell
dotnet Smart.ProbeTarget.dll --mode mixed --interval-ms 100 --torn-ms 1000 --manifest target.json
```

也可在开发电脑独立发布 Windows x64 自包含版本，再复制产物；此发布需要相应 .NET runtime 包可用：

```powershell
.tools\dotnet\dotnet.exe publish validation/Smart.ProbeTarget/Smart.ProbeTarget.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/probe-target-win-x64
```

该发布路径已完成本机验证。复制整个 `artifacts/probe-target-win-x64` 目录到目标电脑后，可以直接运行 `Smart.ProbeTarget.exe --mode mixed --interval-ms 100 --torn-ms 1000 --manifest target.json`，目标电脑无需预先安装 .NET。本机运行成功仍不能代替远端 DMA 读取验收。

默认一直运行，Ctrl+C 取消等待并释放内存。可使用 `--duration-ms 600000` 限时十分钟。`--interval-ms` 范围 10–60000，`--torn-ms` 范围 1–60000，`--duration-ms` 范围 100–2147483647；参数错误会在运行前失败。循环通过异步延迟等待，不忙等。

三种模式均先发布 `Counter=0, Value=0` 的完整初始帧：

- `stable`：每个间隔完成一次普通更新，不额外延长更新窗口。
- `mixed`（默认）：计数满足 `Counter % 8 == 4` 时，在半写位置停留指定的 `--torn-ms`。故障计划完全确定，不依赖随机种子。
- `torn`：每次更新都在半写位置停留指定时长，完成后仍留有一个正常间隔。它不是永久不可读模式。

首次启动仅向 stdout 输出一行 manifest；`--manifest` 同时以临时文件替换方式保存相同 JSON。停止摘要写 stderr。manifest 包含目标 PID、实际进程名/宿主模块名、启动时间、绝对地址、96 字节长度、64 位指针宽度、会话 GUID 与模式参数，不含密码。

将**仍在运行的目标电脑**生成的 manifest 传到主控电脑。不能把主控机本地启动的目标进程当作远端 DMA 目标。进程重启后 PID、地址和会话均可能变化，必须重新传 manifest；目标退出后原地址不再有效。使用 `dotnet xxx.dll` 启动时进程名可能是 `dotnet`，manifest 中记录的实际身份优先于文件名推测。

## 96 字节协议

全部整数使用 little-endian；GUID 使用 .NET `Guid.TryWriteBytes` 格式。序列字是对齐的 64 位整数。

| 偏移 | 长度 | 含义 |
|---|---|---|
| 0 | 8 | 固定签名 `SMRTPRB1` |
| 8 / 12 | 4 / 4 | 协议版本 1 / 总长度 96 |
| 16 | 8 | 开始序列 |
| 24 | 16 | 本次目标进程会话 GUID |
| 40 / 48 | 8 / 8 | Counter / Value（Counter % 8，0 是合法数据） |
| 56 | 8 | Counter 按位取反 |
| 64 | 8 | 发布时间 UTC ticks |
| 72 | 8 | Counter 派生的确定性校验图案 |
| 80 | 8 | Header、已提交的偶数首序列与 payload 的 FNV-1a 64 位校验和 |
| 88 | 8 | 结束序列 |

每次写入先用 `Volatile.Write` 将开始序列标记为奇数并建立内存屏障，然后设置尾序列为奇数、更新内容和校验和，最后按“尾序列、首序列”的顺序发布相同的偶数。测试窗口位于前半 payload 更新后。校验和覆盖前 80 字节，包含已提交的偶数首序列，因此“旧首尾序列 + 新完整 payload 及其校验和”的拼接也会失败。校验器拒绝长度/签名/版本错误、奇数/不匹配序列、错误会话、校验和不符以及 payload 不一致；只在全部通过后返回 `ProbeSample`。

这些错误分类只供验收适配器内部使用，不能成为业务读质量接口。接到 Smart 时仍应遵循提供者语义：成功立即发布，读失败或半写保留前份正式快照，冷启动等首份完整有效数据，会话变化失效旧快照。

序列加校验和用于发现本夹具的更新中和混拼数据，不宣称让 DMA 获得原子读能力，也不把校验和当密码学保证。单次 96 字节读取可能命中缓存中的旧完整帧，校验通过只能证明它符合协议。活性验收至少需要确认相同目标会话下 Counter 跨值变化，以及重启后新 SessionId 与新 manifest 匹配；不能用单次“读成功”证明正在读取活跃目标。

## 主控电脑的只读探针

`validation/Smart.HardwareProbe` 已接入同一协议、DmaDispatcher、设备租约和 SnapshotProvider。它必须显式指定 DLL、设备 URI、目标 manifest 与运行时长，不会读取现有脚本配置来自动选择设备，不初始化 KMBox。详细参数、指标及阈值见 [探针说明](../validation/Smart.HardwareProbe/README.md)。

离线验证使用托管替身传输，不打开设备：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-ProbeAdapter.ps1
```

此命令在独立 artifacts 目录构建 adapter 验证解决方案，避免覆盖正在运行的宿主程序集。真实运行前需要先确认空闲设备及目标电脑；不能用目标进程的本机 smoke 或替身测试代替真实读取。

探针把损坏 GUID 视为无效数据，只有完整校验通过的新会话才触发失效。持续活性是验收工具的判断：起始、帧间和结束前的最长无进展时间都计入门槛；长期失联会令报告不通过，但不会改变提供者失败保持正式快照的规则。
