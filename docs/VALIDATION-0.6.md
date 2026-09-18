# 0.6.0 WPF 公共主页验证

日期：2026-09-17。范围为无游戏业务、无卡密的 WPF + MVVM 桌面模板及配套通用接口。未连接任何真实 DMA/KMBox，没有部署或操作现有账号脚本。

## 验证结果

### 2026-09-18 保存前角色验证补验

- 按 Roadhog 按设备读取玩家快照并显示角色的交互增加保存门槛；新增配置验证接口，不包含游戏偏移或业务读取实现。
- Hardware 要求明确物理绑定、当前测试会话的角色身份、KMBox 握手及用户确认。单项诊断不能解锁保存，导入也重新验证；配置变化、取消、迟到结果和失败不会复用旧批准。
- 源码 253/253、生成模板 254/254 通过，构建零警告零错误。新增 14 项测试覆盖保存绕过、角色缺失、重测失败、改回原值、Mock/Hardware 切换、拒绝错误角色、物理绑定缺失、全量导入失败回滚、正式快照冷启动取消、进程身份切换及重测会话隔离。
- WPF 三页实际渲染及绑定检查通过。证据：`artifacts/profile-verification-tests.log`、`profile-verification-template.log`、`profile-verification-home.png`。
- 未连接真实设备或游戏；空模板未接入 `IProjectCharacterVerification` 时明确拒绝硬件保存。以下 9 月 17 日的包、IDE 和发布记录属于此前版本状态，本次没有将旧发布产物当作角色验证交付。

### 2026-09-17 首轮 WPF 验收

| 检查 | 结果 | 本机证据 |
|---|---|---|
| Release 源码构建与测试 | 239/239，构建无警告、无错误 | `artifacts/wpf-tests-final.log` |
| VS 2026 实际 IDE 构建 | 失败项目 0，错误列表为空 | `artifacts/wpf-vs-build.log` |
| 新目录生成项目 | 240/240，包含独立消费者夹具，确认生成 WPF 宿主 | `artifacts/wpf-template-final.log` |
| 七个 0.6.0 包及独立消费者 | 239/239 | `artifacts/wpf-packages.log` |
| 固定旧包升级 | 0.4.1 → 0.6.0 通过 | `artifacts/wpf-upgrade.log` |
| 依赖系统运行时发布 | 清单校验、Console/Desktop Mock 启停、worker 离线启动通过 | `artifacts/wpf-publish.log` |
| 自包含发布 | 同上，随包携带运行时 | `artifacts/wpf-publish-sc.log` |
| WPF 页面 | 实际窗口渲染三页，绑定错误检查通过 | `artifacts/wpf-home.png`、`wpf-home-settings.png`、`wpf-home-diagnostics.png` |
| 合成性能门槛 | 通过 | `artifacts/wpf-performance/benchmark.json` |
| 8 账号、每账号 4 模块、60 秒故障演练 | 通过，退出时 HeldInputs=0、DMA Active=0/Queued=0 | `artifacts/wpf-soak/soak.json` |

新增测试覆盖单账号保存对其他会话的隔离、保存失败时保留旧配置对象、未保存编辑保护、取消后拒收迟到诊断结果、页面注册、无效数字输入、UDP 回环仅 Connect、独立枚举 worker 的正常退出/超时/取消/输出越界。

独立运行的 `--smoke` 使用独立 Mock 租约目录，可并行验收；正常硬件运行仍使用跨进程共享租约。取消测试等待取消通知，并在断言失败时也完成受控替身，避免测试清理自身挂起。

## 性能和硬件边界

本轮 60 秒合成演练累计 CPU 时间约 734 ms，停止约 15.6 ms，无日志丢失。这是本机合成底座测量，不是 WPF 界面 CPU 测量，不代表真实游戏、驱动或长时间运行结果。

界面采用 500 ms 状态展示刷新、未变化属性不通知、最小化暂停展示刷新、表格虚拟化及有界操作记录。正式读取和快照发布规则未改变。真实 DMA 枚举、进程选择、模块读取及真实 KMBox 按钮路径仍需另行实机验收；回环与 Mock 不能替代设备验收。

此前的实机和原生隔离结果保留在 [0.5 验证记录](VALIDATION.md) 与 [0.4.1 实机记录](VALIDATION-0.4.1.md)，不将历史实机结果算作本轮 UI 验收。
