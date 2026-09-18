# Smart 公共工程模板

C# / .NET 10，面向 DMA/VMM 读取与 KMBox 输入。当前公共版本为 0.6.0，桌面宿主采用 WPF + MVVM。

模板不包含游戏业务、偏移表、战斗/恢复/寻路规则或卡密系统。Domain 和 Application 保留空扩展位置；默认启动空的 Mock 会话，不发送硬件输入。

桌面配置保存必须先验证。Hardware 需要明确设备绑定、实际角色快照及 KMBox 握手，并确认读到的角色；修改配置后须重测。空模板未提供游戏角色读取器，接入方式见 [桌面说明](docs/DESKTOP.md)。

## 启动与验证

在 Visual Studio 中开发需要 **Visual Studio 2026（18.0 或以上）**、“.NET 桌面开发”工作负载，以及系统安装的 **.NET SDK 10.0.401**（或同一 10.0.4xx 功能带的更新补丁）。打开 `Smart.slnx`，运行桌面端时将 `SampleProject.Desktop` 设为启动项目。

`Install-Sdk.ps1` 仅安装供构建脚本使用的 `.tools/dotnet`，不会升级 Visual Studio 或安装系统 SDK；VS 2022 无法构建本项目的 .NET 10 目标。

仓库中的模板位于 `Smart` 分支。首次获取并安装本项目固定的 SDK：

```powershell
git clone --branch Smart --single-branch https://github.com/w408179899-sudo/MyProduct.git Smart
cd Smart
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Install-Sdk.ps1
```

在本目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build.ps1 Test
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build.ps1 Desktop
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build.ps1 TemplateSmoke
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Performance.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Packages.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Diagnostics.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Upgrade.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Publish-Applications.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Soak.ps1 -DurationSeconds 300 -Accounts 8
```

脚本优先使用 `.tools/dotnet/dotnet.exe`，否则使用 PATH 中匹配 global.json 的 SDK。构建不部署现有脚本，不复制原生驱动。

源码不携带本机配置、日志、SDK 和构建产物。验收文档中的 `artifacts` 路径是本机证据，远端构建结果以 GitHub Actions 为准；历史 0.4.1 的七个小型框架包作为固定升级测试夹具随仓库保存。

桌面端提供脚本主页、账号配置和运行诊断三页，支持 DMA 设备枚举与绑定、目标进程选择、只读模块测试、KMBox 纯握手测试、多账号启停、导入导出和诊断包。修改某个账号前需停止该账号，其他账号可继续运行；整份配置导入仍要求全部停止。默认 Mock 不连接硬件，没有账号密码字段。

设备枚举和正式 VMM 连接都使用独立 worker。主页测试使用临时连接，完成后释放；正在使用的设备受租约保护。连接测试不包含游戏读取业务，也不发送键鼠操作。页面扩展与验证见 [WPF 公共主页](docs/DESKTOP.md)。

Console 默认运行约一秒的空 Mock 会话。使用 `--config <path>` 指定账号配置；`--list-devices` 显示 USB 设备清单；`--probe <accountId>` 只读检查配置中的 DMA 目标；硬件执行需显式传入 `--run-hardware`。USB 清单顺序不能当作 VMM devindex。

使用 `--run` 常驻运行，Ctrl+C 后等待所有选中账号停止并释放资源；`--duration-ms` 可指定有限运行时长。参数拼写、账号筛选和运行故障会明确报错。

## 公共能力

- 强类型封闭通道目录、单一读取发布路径、Partial 字段/对象合并、Failed 保持原正式快照、首次读取重试、会话隔离。
- 每会话独立装配通道和模块，模块 JSON 自动绑定强类型配置并校验，Mock/Hardware 共用模块注册入口。
- 慢刷新有界等待，已有正式快照继续可用，同一通道只有一次在途采集；会话更换取消旧采集并拒绝旧结果。
- 每模块最多一个进行中的 Tick，异步等待读取不阻塞其他模块；初始化依赖排序、逆序停止、通道声明检查、协作式预算。
- 动作取消、超时、执行前条件、提案到期、持续输入优先级、显式释放、失败隔离和可重试清理。
- 框架时限异步取消并收束回调；超时回调异常不会跳过输入释放，停止错误汇总后继续清理其他模块。
- 真实会话绑定/重连；相同 DMA 设备跨账号复用连接与有界读取队列，跨宿主进程用文件锁排他；KMBox 输入独占。
- 不同 DMA 设备分别初始化和清理；连接关闭失败保留所有权并可重试。配置/模块错误停止账号，可恢复连接错误才重连。
- 正式 DMA 连接在独立 worker 进程执行；初始化、读取和关闭有时限，worker 退出立即失效快照，确认其终止后才能重连。
- 可选的物理设备绑定校验驱动原始索引、唯一序列号、位置、拓扑及 DLL 指纹；不明确时拒绝受保护连接。
- MemProcFS scatter 读取及旧版逐项读取兼容路径、进程/模块身份检查。
- 原子配置保存和一致的配置切换、显式版本迁移、异步有界日志、快照后台序列化、跨运行轮转清理、共享时间轴回放及动作命令记录。
- 自动发现业务层工程的依赖检查、故障测试、模板生成测试、公共 NuGet 包模式及 CI 工作流。
- 固定旧版包及真实外部消费者做升级回归；Console/Desktop 发布包含 worker，校验 Release 来源、清单 SHA 和 Mock 启停。

## 生成新项目

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/New-Project.ps1 -Name MyProject -OutputPath ..\MyProject
```

生成入口会创建隔离的源码副本和模板注册目录，避免验证产物中的旧同名模板被误选。生成项目不携带 `.tools`、运行配置、日志或验证产物。默认源码引用方便调试；跨项目统一维护时切换到公共包引用，见 [版本维护](docs/VERSIONING.md)。

## 阅读顺序

1. [架构与边界](docs/ARCHITECTURE.md)
2. [数据契约](docs/DATA.md)
3. [扩展公共接口](docs/EXTENDING.md)
4. [硬件配置与已知限制](docs/ADAPTERS.md)
5. [测试和性能门槛](docs/TESTING.md)
6. [0.6 WPF 验证记录](docs/VALIDATION-0.6.md)、[0.5 验证记录](docs/VALIDATION.md)
7. [Roadhog 借鉴与改进对照](docs/ROADHOG-REFERENCE.md)
8. [原生进程隔离](docs/NATIVE-ISOLATION.md)、[设备绑定](docs/DEVICE-BINDING.md)、[0.5 升级说明](docs/MIGRATION-0.5.md)
8. [从 0.2 升级](docs/MIGRATION-0.3.md)
9. [实机验收步骤](docs/HARDWARE-ACCEPTANCE.md)
10. [0.4 生命周期与宿主升级](docs/MIGRATION-0.4.md)
11. [四个质量目标的验收依据](docs/QUALITY-GATES.md)
12. [无游戏业务的 DMA 验收夹具](docs/PROBE.md)

新项目按四个位置扩展：Domain 定义数据和规则，Application 写模块，Infrastructure 写目标程序的读取映射与 Mock，Bootstrap 的 ProjectComposition 注册模块。公共 src 不包含具体项目分支。不同目标程序仍需各自的读取映射、校验规则和设备配置。

0.4.1 已在指定设备上通过真实 DMA 初始化、PE 批量读取及坏块隔离、KMBox Connect 握手；详细范围见验证记录。真实动态数据、物理键鼠输入、断连恢复和长时间运行仍需分别验收，Mock 与包测试不能代替这些项目。
