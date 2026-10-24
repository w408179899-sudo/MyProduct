# 自动登录集成验证：2026-10-24

本轮完成代码、模拟/归档回放、模型转换与更新包验证。没有连接真实 DMA 或发送目标机键鼠输入，没有点击实际登录；集成后的实机验证由用户执行。运行中的旧多账号程序未更新、未停止。

## 实现范围

- 设备/角色界面增加游戏账号、密码；密码以 Windows CurrentUser DPAPI 密文保存。
- 顶部“登录所选”按左侧勾选账号登录，到达配置角色的游戏世界后结束。
- “启动”先接续当前登录阶段，确认配置角色进入世界后进入原有身份检查和挂机启动流程。
- 支持停止取消、重复请求合并、独立账号后台和进度显示。登录失败不会自动开启挂机。
- 世界状态仍存在角色数据时，先检查可见消息框，避免将退出提示误判为在线。未知弹窗停止；连续在线过久弹窗的精确身份仍需实机补证，未按固定小时数退出游戏。
- 完整边界见 [功能说明](AUTO_LOGIN.md)。本次未新增挂机期间持续监视游戏掉线的轮询器。

## 验证结果

| 验证 | 结果 |
| --- | --- |
| 最终 Release 编译 | 0 警告、0 错误；输出到 `.tmp/login-final-build`，构建复制目标为 `.tmp/login-final-sink` |
| 新增登录测试 | 13 组通过；涵盖完整流程、中途接续、弹窗/旧页面、角色不符、取消、验证码重试、DPAPI、IPC 隔离、UI 勾选及可信快照故障 |
| 模型转换 | 100 张图片的 C# 结果与 PyTorch 一致；输入张量逐值误差小于 1e-6；不代表 100% 验证码准确率 |
| DMA 通道登记回归 | 2 组通过 |
| NPC 失败续跑回归 | 修正测试数据后 3 组通过；未修改 NPC 生产逻辑 |
| 更新包 | 30 个运行文件逐个 SHA256 验证；测试旧文件备份、配置/日志保留、文件占用拒绝；安装后的文件集合通过模型加载验证和登录测试 |

全量回归在本轮中间构建上运行，共 1210 项：1209 项通过，1 项旧 NPC 测试失败。该失败已在未加入本次登录改动的 `474574a` 基线上复现：测试的“完整路线”断言与已有 100 米就近接续规则冲突，导致预期的出售故障注入之前就中止路径。

随后仅调整 `GroceryShopTests.Continuation.cs` 的测试路线，使其进入预期出售分支，3 组续跑测试通过。最终小改之后重跑受影响的登录与通道测试；没有把中间全量运行表述为最终二进制的另一次全量运行。原始失败和修正后的通过日志均保留。

## 日志和重现

主要工作区日志：

- `.tmp/auto-login-final-build.log`
- `.tmp/auto-login-final-full-tests.log`（中间全量，含旧用例失败）
- `.tmp/login-baseline-grocery-detail.log`（基线复现与路径错误定位）
- `.tmp/auto-login-final-focused-tests.log`（最终 13 组，使用更新包文件集合）
- `.tmp/auto-login-final-catalog-tests.log`
- `.tmp/auto-login-final-grocery-tests.log`
- `.tmp/auto-login-update-test.log`、`.tmp/auto-login-update-locked-test.log`
- `.tmp/auto-login-installed-model-tests.log`

```powershell
dotnet build Roadhog.Tests/Roadhog.Tests.csproj -c Release --no-restore -p:OutDir=C:\D\Develop\MyCode\Tool\.tmp\login-final-build\ -p:RoadhogDesktopScriptRoot=C:\D\Develop\MyCode\Tool\.tmp\login-final-sink
& .tmp\login-final-build\Roadhog.Tests.exe --test-filter=auto-login
& .tmp\login-final-build\Roadhog.Tests.exe '--test-filter=dma snapshot catalog'
& .tmp\login-final-build\Roadhog.Tests.exe '--test-filter=grocery continuation'
```

## 实机前更新

桌面 `Roadhog_自动登录更新_20261024` 内有完整运行文件、模型、许可、清单和更新脚本。先在旧主界面点击“退出程序”，再双击“更新并打开.cmd”。更新脚本保留 `桌面/script/多账号客户端` 的账号配置、日志和授权信息，先备份被替换文件，遇到文件占用停止。

填写脚本1的游戏账号、密码并保存，仅勾选脚本1后点“登录所选”。目标为配置中的 HiApple；进入世界后显示“已登录 · 待启动”。用户确认实机流程后可点“启动”挂机。

## 追加：脚本6登录在 PDB 加载阶段退出

2026-10-24 16:58:34、16:58:53、16:59:26，脚本6均记录“正在检查目标电脑的游戏与登录状态”，随后成功创建 DMA 连接。管理器在 16:58:36、16:58:54、16:59:27 分别记录 `account_process.failed: Failed to load PDB for module`，没有进入点击或输入步骤。当前运行程序已包含登录功能，配置已有账号及加密密码。

前台窗口与鼠标位置的读取依赖 `win32kbase.sys` 符号。`VmmPdb` 抛出的 `Vmmsharp.VmmException` 原先不在登录 provider 的读取异常处理中，因此直接退出账号后台。MemProcFS 默认异步初始化 PDB 子系统，初始化未完成时相关功能可能尚不可用，参见 [官方命令行文档的 waitinitialize 说明](https://github.com/ufrisk/MemProcFS/wiki/_CommandLine#-waitinitialize)。日志只能证明符号加载失败，不能单凭错误文本判定是初始化时序还是下载问题。

本次在脚本6设备租约内进行一次只读探测：常规连接参数下，VMM 初始化耗时 1175 ms，符号加载耗时 31602 ms，成功取得前台窗口及鼠标符号。没有键鼠输入、没有目标内存写入，探测后释放租约。这证明该目标的符号可以解析，不代表完整自动登录已实机通过。

修正内容：登录场景发布及输入适配器的内部读取重试均接住 `VmmException`；首次读取失败继续等待，同一会话读取失败保留官方快照，进程会话切换立即失效旧快照。新增 `vmm.login.read_retry` 诊断，同类连续异常最多每 10 秒记录一次。保留取消和整个登录流程的超时边界，不修改其他挂机流程的 VMM 初始化参数。

验证：Release 构建 0 警告、0 错误；13 组登录测试全部通过，故障注入包含无会话时的原生初始化失败、PDB 加载失败、同会话保留及新会话禁止复用旧控件。本次未重跑全量测试，未执行实际登录点击。

本次证据：`.tmp/login-pdb-fix-build.log`、`.tmp/login-pdb-fix-tests.log`、`.tmp/script6-login-symbols-20261024/bin/Release/net8.0-windows/report.json`。修正版输出到 `.tmp/login-pdb-fix-build`，构建复制目标为 `.tmp/login-pdb-fix-sink`。桌面更新包已刷新；正在运行的正式目录未替换、其他账号未停止。

## 追加：中文安装路径导致符号持续加载失败

随后用户重试仍无动作。17:13:21～17:14:13、17:14:32～17:15:23 的脚本6日志持续记录 `vmm.login.read_retry`，说明前一轮异常处理已经生效，但等待没有解决实际的符号加载问题。前一轮只读工具位于英文目录，未覆盖正式程序所在的中文路径，是先前验证的缺口。

本次使用同一设备租约做路径对照，未发送键鼠输入、未写目标内存：

- 原英文目录：成功读取 `gptiForeground`、`gptCursorAsync`。
- 将同一套程序、原生 DLL 以及已下载的符号一起复制到中文临时目录：复现 `Failed to load PDB for module`，原生诊断提示无法将内核符号下载到缓存。已有符号文件仍不能解决。
- 中文目录程序显式绑定英文目录中的 `vmm.dll` 和 `leechcore.dll`：符号成功读取。仅预加载而不统一绑定会混入两份 LeechCore；正式修复同时绑定两者。

源码参考：[MemProcFS 的符号路径及失败状态处理](https://github.com/ufrisk/MemProcFS/blob/master/vmm/pdb.c)、[vmmsharp 的 LoadNativeLibrary 加载顺序](https://github.com/ufrisk/MemProcFS/blob/master/vmmsharp/vmmsharp/vmmsharp/Vmm.cs)。根因结论来自上述本机对照，不只依据当前上游源码。原生层存在失败记忆，重复调用同一个 PDB 入口不能保证重新初始化。

正式修复只在非 ASCII 安装路径时准备原生运行目录，按 DLL 内容哈希隔离版本并校验文件，跨账号进程串行准备，完整写入后再加载。保留原程序与配置位置，不更新全局符号注册表。英文默认安装的加载路径保持原行为。

修正后的正式 `AionVmmGameApi`、`LoginNativeReader` 和 `LoginSnapshotPublisher` 从中文临时目录对脚本6进行只读验证：首次创建新的英文原生缓存并下载符号后，约 15 秒发布完整场景 `Desktop / OpenAccelerator`，说明下一步是“将 UU 加速器置于前台”。这次已验证越过原先的 PDB 阻塞；没有执行该动作，不表示完整登录已实机通过。设备租约在结束时释放。

Release 构建为 0 警告、0 错误。16 组登录测试通过，新增覆盖中文来源路径、六个并发准备者、损坏缓存修复、版本隔离及缺少符号依赖时不发布半成品。

该轮二进制完成全量回归：1213 项全部通过，进程退出码 0。该轮只读实机探测、测试构建和更新包内的 `Roadhog.dll` SHA256 一致：`69D9305B1EBC52FBBD3D24D5DE2BF92BF4803DEBD4E2F80905AD5427438C0FB8`。更新包中的 30 个运行文件逐项校验通过。

证据：`.tmp/login-native-path-build.log`、`.tmp/login-native-path-tests.log`、`.tmp/login-native-path-full-tests.log`、`.tmp/script6-symbol-unicode-path.log`、`.tmp/script6-symbol-resolver.log`、`.tmp/script6-login-native-scene.log`。正式运行目录尚未替换，其他账号未停止。

## 追加：UU 置前后窗口列表被后台线程读取阻塞

17:46:30，脚本6记录“将 UU 加速器置于前台”；17:46:35 起持续报 `Incomplete window inventory read 2AB800`，本轮已不再是符号加载故障。只读复现定位到 `WindowInventory` 读取一个后台线程的 `TEB + 0x800` 失败，旧代码因此使整个窗口列表失败；UU 主窗口所在桌面的共享映射仍可从其他线程正常核对。

修正为在 provider 内按桌面身份建立本次采集的共享映射索引，再完整读取同桌面的每个窗口。没有直接跳过无法读取 TEB 的线程所拥有的窗口，也没有把失败解释为窗口不存在。映射缺失、不唯一、属于其他桌面，或窗口正文不完整时仍拒绝发布本次数据；线程表冷启动为空时等待初始化。

验证使用脚本6设备租约，未发送键鼠、未写目标内存，结束后释放租约。修正后可完整枚举窗口并读取 UU 的 695 个控件，其中第一张游戏卡片为“永恒之塔”。核查时 UU 已最小化，前台为 Explorer，因此只核对窗口结构、卡片和控件状态，没有把最小化窗口的负坐标用于点击，也没有声称已完成实际登录。

按用户本轮要求，仅运行登录相关测试：19 组全部通过，覆盖共享桌面映射、不可读后台线程、跨桌面拒绝、窗口字段缺失、冷启动线程表及映射歧义。Release 构建 0 警告、0 错误；本轮未跑全量。

证据：`.tmp/login-window-map-build.log`、`.tmp/login-window-map-tests.log`、`.tmp/script6-login-window-inventory-detail.log`、`.tmp/script6-login-window-map-scene.log`、`.tmp/script6-login-window-map-uu-page.log`。更新包已刷新，运行中的正式目录未替换，其他账号未停止。

## 追加：提交内容的隔离验证

从暂存区导出独立源码目录 `.tmp/login-publish-verify/src`，仅使用待提交文件构建，未借用工作区未提交的战斗或 NPC 测试改动。Release 构建为 0 警告、0 错误，构建复制目录也隔离在临时目录内。

`--test-filter=auto-login` 的 19 项全部通过；模型数据标注来源与训练/查询隔离测试 5 项通过，训练和 ONNX 导出脚本通过语法检查。本次没有重新训练模型、运行全量测试或执行实机输入。模型 SHA256 与元数据一致，100 对图片/输入张量测试文件齐全。

发布包含登录代码、模型及许可、回放与专项测试、训练数据来源校验和 ONNX 导出工具。前文所述旧 NPC 测试路线调整保留在本地，不纳入此次登录提交；IDE 状态、个人启动配置、编译输出、账号凭据与运行日志也未纳入。

隔离验证日志为 `.tmp/login-publish-verify/build.log`、`login-tests.log`、`ocr-dataset-tests.log`。桌面完整客户端包使用之前已验证的登录修正版二进制；源码推送不会替换运行中的客户端。

## 追加：点击启动游戏后等待 UU 最小化过渡

用户确认 UU 点击“启动游戏”后会自动最小化，而启动器进程可能稍后出现。新增回归在旧代码上复现了重复拉起 UU、重复启动及取消时仍追加 UU 操作的问题。

登录 runner 现在只在“启动游戏”输入实际发出后记录本次启动请求。此后遇到桌面或加速器状态仅等待，前台和进程会话变化不清除这个状态；启动器、通行证、游戏窗口出现后继续既有流程。原有 100 秒步骤等待和停止取消继续生效，新登录请求重新初始化状态；点击前复查失败、输入未发出时不会提前进入等待。此次未修改 DMA 读取或窗口坐标逻辑。

Release 构建为 0 警告、0 错误，23 项登录专项测试全部通过，没有运行全量。新增 4 项覆盖最小化和多次进程过渡、等待超时后重新发起、等待中取消，以及未发出的点击不阻塞重试。证据为 `.tmp/login-launch-wait-before.log`、`.tmp/login-launch-wait-build.log`、`.tmp/login-launch-wait-tests.log`。

单独命名的“UU等待修复”完整客户端包使用此次通过测试的二进制，未替换运行中的客户端、未执行实机键鼠输入。

## 追加：自动登录临时故障恢复

修正的范围包括 UU/通行证子树部分读取被静默丢弃、窗口身份变化被解释为不存在、过渡页面直接结束登录、输入中途变窗或丢焦点抛出异常终止、点击复核使用旧快照、旧 PID 重试不接续，以及动作持续拒绝绕过步骤超时。

登录读取和输入目标绑定共享同一 provider 发布管线。失败保留官方快照，但不会建立物理输入目标；输入中断后释放设备，再由当前页面决定下一动作。最终启动/登录/入场按下成功后即使松键确认异常，也先释放并等待结果，避免重复操作。

Release 构建 0 警告、0 错误；33 项登录专项测试通过。新增 10 项覆盖每个登录操作首次中断后恢复、设备释放失败和取消、旧快照不能绑定输入、PID/桌面/窗口/位置/动作变化、持续拒绝的步骤超时、未生效控件点击重试、最终提交去重、DuiLib 子数组故障、从历史 UU 控件树重建的原始内存回放及页面过渡。原有窗口枚举测试追加了 HWND 替换和主窗口 manager 缺失。100 张 OCR 测试只验证原模型转换一致性，未训练或在线提交验证码。

最终验证命令只使用 `--test-filter=auto-login`，证据为 `.tmp/login-resilience-build.log`、`.tmp/login-resilience-tests.log`。首次运行误用了不受支持的 `--filter` 参数，已停止额外测试进程；中止日志 `.tmp/login-resilience-tests-first.log` 不作为最终验证结论。

编译输出和复制目标隔离在 `.tmp/login-resilience-build`、`.tmp/login-resilience-sink`。单独生成“登录健壮性修复”完整客户端包，包含同一已测试二进制、依赖和原模型，空配置不含个人凭据。未覆盖或重启正在运行的客户端，完整实机点击仍由用户验证。以上为提交前的验证记录，源码发布以 Git 提交为准。

尝试使用脚本6做只读窗口采集时，设备租约返回已占用，探针在连接 DMA 前退出，未抢占账号设备、未发送输入。记录为 `.tmp/login-resilience-readonly.log`，不能当作实机读取通过。最终构建 `Roadhog.dll` SHA256：`9953FD2DEB289EE4A1F7D34EB1DE23FEA3CE9C58AFC0F2A05E8A490ECAE843E8`。

最终复核同时覆盖父容器禁用向 UU 子控件传递，以及 UU、通行证、游戏按钮尚无有效宽高时拒绝点击。33 项专项在这些补充修改后重新通过。
