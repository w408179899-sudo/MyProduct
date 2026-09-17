# 本轮验证记录

日期：2026-09-16。公共版本：0.3.0。

## 范围

只实现公共框架，Domain/Application/Infrastructure 保留无业务扩展入口。不加入卡密或授权系统。Roadhog 的读取成功发布、部分字段合并、失败保持和会话隔离规则继续保留。

## 已完成验证

- Release 源码构建：0 警告、0 错误。
- 源码测试：103/103（数据 39、运行/宿主 51、架构 4、模板/连接池/端点 9），TRX 在 artifacts/test-results，本轮源码记录为 21:52:55/56。
- 模板生成：在 artifacts/template-smoke/8789a3698df34717b55ca1b5532a4d83 生成 SmokeProject；构建零警告，103/103 测试通过，空 Mock Console 成功启动和停止，Modules=0。完整输出在 artifacts/template-final.log。
- Desktop smoke：配置加载、启动、暂停、重新启动与退出通过，输出 DESKTOP_SMOKE_PASSED。
- 七个 0.3.0 公共 NuGet 包生成完成；包消费者 103/103 通过，使用独立缓存，并检查 project.assets.json 中公共依赖确实为 package。完整输出在 artifacts/package-final.log。
- 合成性能门槛通过，结果在 artifacts/benchmark.json。

## 本机性能样本

| 场景 | 观察值 |
|---|---|
| 100000 次缓存读取 | 43.97 ms；源捕获 1 次；累计分配 392 B；P99 0.4 μs |
| 10000 次捕获/合并/发布 | 8.17 ms；含预热捕获 10001 次；P99 4 μs |
| 2000 对象 × 1000 次 Partial 合并 | 329.22 ms；独立有效对象 1882 个 |
| 1/4/8 账号，各 4 个有界模块，运行 3 秒 | 均通过进展与 CPU 门槛 |

结果包含 JIT、GC、调度和本机负载影响。不能据此承诺物理 DMA/KMBox 延迟、跨机器 CPU 百分比或长期无泄漏。门槛定义在 benchmarks/budgets.json。

## 持续负载与发现的问题

先执行了 8 账号 × 4 模块的 60 秒合成负载。随后延长到 300 秒，第一次在约三分钟时暴露会话重建竞态：读请求通过取消检查后遇到快照失效，把正常取消报成 ObjectDisposedException，账号进入 Faulted。

失败证据保留在 artifacts/soak-logs/1897411de5a54795baf6e3bd29ea6ae9，事件时间为 2026-09-16 21:36:13。已将失效会话的读取统一为生命周期取消，并添加 ReaderEnteringAfterSessionInvalidationReportsLifecycleCancellation 回归。

第二次 300 秒运行结束后，又发现重复释放账号会对已释放的 CancellationTokenSource 调用 CancelAsync。已修复账号释放的幂等性，并用 16 个并发释放请求及后续重复停止验证清理仅执行一次。修复后的 15 秒短负载完整通过运行和停止检查。

最终 300 秒、8 账号复测完整通过，包括运行、停止和重复释放；结果在 artifacts/soak.json 和 artifacts/soak-final.log，日志目录为 artifacts/soak-logs/83dc0ccee84e468288632cd04308a667。

| 指标 | 本次结果 |
|---|---|
| 负载 | 8 个账号 × 4 个模块；预热 3 秒，测量 300.26 秒 |
| 进展与恢复 | 创建 791 个会话，201311 次模块决策；注入 508 次读取异常和 64 次输入失败 |
| CPU | 累计 968.75 ms，约 0.00323 个 CPU 核的平均占用 |
| 分配与保留内存 | 累计分配 789077736 B；GC 后托管堆相对预热基线减少 98232 B |
| 句柄 | 相对预热基线增加 3 个，通过增长门槛 |
| 停止 | 15.69 ms；HeldInputs=0，DMA Queued=0、Active=0 |
| 读取队列 | 最大排队 8，容量上限 16 |
| 日志 | DroppedEvents=0，无写入错误 |

决策、会话和故障计数包含预热阶段；CPU、分配、堆和句柄的基线取自预热后。此场景刻意包含等待、失败、频繁重连和完整诊断序列化，数据不能外推为具体游戏业务的资源消耗。累计分配不等于保留内存，五分钟堆未增长也不证明数小时无泄漏。

## 尚未进行的验证

- 没有连接现场 DMA、KMBox，没有使用游戏账号，没有初始化真实输入。
- native C ABI、scatter、目标身份和硬件重连需用指定 DLL/固件/设备做实机验收；代码编译与连接池替身通过不等于实机通过。
- 轮询无法保证发现两次检查之间同地址、同内容的瞬时卸载重载；同步 native 调用的最长返回时间也需实测。
- CI 文件已添加，但未推送到远程执行。
- 没有部署或改动现有 Roadhog 运行目录，没有提交或推送代码。
