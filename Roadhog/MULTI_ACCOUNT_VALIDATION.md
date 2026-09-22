# 多账号实现验证记录

日期：2026-09-22。使用方式见 [MULTI_ACCOUNT.md](MULTI_ACCOUNT.md)。

## 六账号配置保留与合并验证

11:49 放置：按用户要求将已验证的 11:44 包放到 `C:\Users\GoldGiven\Desktop\script\多账号客户端`，共复制 898 个文件并逐文件验证哈希；只调整账号显示顺序，将“脚本2”置首。放置后再次通过 [生效设置核对](../.tmp/multi-account-tests/migration-placed-effective-audit.log) 与 [原件及合并完整性核对](../.tmp/multi-account-tests/migration-placed-integrity-audit.log)，相对资源路径正确，无自动启动意图。原 `script/1..6` 未修改，未启动或停止用户进程。

11:44 刷新：切换前复核发现 2 号在 11:41 修改了 `accounts.json`、名单、清包路径和当前方案。已生成新包 `C:\Users\GoldGiven\Desktop\Roadhog-六账号迁移-20260922-114425`，旧包保留作历史备份。新包再次通过 [完整性审计](../.tmp/multi-account-tests/migration-refresh-integrity-audit.log) 和 [Release 生效设置审计](../.tmp/multi-account-tests/migration-refresh-effective-audit.log)：640 份原件、162 项合并选择、六账号生效设置与 25 份路径均核对通过；程序和依赖 28 个文件仍匹配此前验证的 Release 哈希。未修改原客户端或启动新客户端。

迁移包：`C:\Users\GoldGiven\Desktop\Roadhog-六账号迁移-20260922-112859`。源目录 `Desktop/script/1..6` 的已保存配置快照时间为 2026-09-22 11:28，原客户端未停止、原配置未修改。

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| Python 迁移回归 | 11 项通过；覆盖原件不变、方案与六类路径保留、时间规则、缺失与占位账号处理、输出及授权来源边界 | [测试文件](tools/test_prepare_six_account_migration.py) |
| 最终 Debug 独立构建 | 0 警告，0 错误；使用独立输出，避免占用用户正在运行的 Debug 程序 | [构建日志](../.tmp/multi-account-tests/migration-debug-isolated-build.log) |
| 最终 Release 构建 | 0 警告，0 错误 | [构建日志](../.tmp/multi-account-tests/migration-release-build.log) |
| 最终 Debug 针对回归 | 58 通过，0 失败；用时 85.80 秒 | [测试日志](../.tmp/multi-account-tests/migration-debug58.log) |
| 最终 Release 针对回归 | 58 通过，0 失败；用时 84.00 秒 | [测试日志](../.tmp/multi-account-tests/migration-release58.log) |
| 实际副本生效设置 | Debug、Release 各自通过：六账号的最终设置及名单一致，25 份实际路径一致，1 条原缺失路径仍隔离保持缺失 | [Debug 比对](../.tmp/multi-account-tests/migration-package-effective-audit.log)、[Release 比对](../.tmp/multi-account-tests/migration-package-release-effective-audit.log) |
| 原件、备份与时间选择 | 640 份源文件与备份的哈希、修改时间一致；162 项最新版本选择独立重算通过；27 份独立资源原字节一致 | [完整性审计](../.tmp/multi-account-tests/migration-package-integrity-audit.log) |
| 启动意图及测试清理 | 加载实际六账号不会启动后台；迁移包无旧后台运行意图和 IPC 文件；11:40 测试进程数量为 0 | [清理记录](../.tmp/multi-account-tests/migration-process-cleanup.json) |

最终 58 项包括原多账号 53 项和新增 5 项配置迁移/资源隔离回归。本轮没有再次执行完整业务回归；之前全量记录保留如下。实际副本比对经过真实 C# 配置加载器，按原顺序应用方案与名单后比较，并反解保留方案、路径的别名。

37 处同名内容冲突按有效 `UpdatedAt`、原文件修改时间处理；每次选取及所有候选原件均有记录。2 号原 `25级守卫兵2号点` 战斗路径本就不存在，当前为定点战斗，未猜测补齐。4 号未绑定“账号2”只保留在原始备份。4、6 号缺少的 `license.dat` 没有补造，各自原 `owner-license.json` 原样保留，授权仍通过正常验证。

已将本轮验证的 Release 程序及依赖共 28 个文件复制到迁移包，并逐文件匹配构建输出哈希（见包内 `runtime-manifest.json`）。未启动新客户端、未连接实际硬件，未替换 `Desktop/script/1..6` 的程序。使用时先停止对应旧客户端，再在新客户端启动该账号。

## 稳定性专项复核结果

本轮修复启动、停止、强杀、接管和窗口退出的边界问题，详见 [稳定性审查](MULTI_ACCOUNT_STABILITY.md)。下表为最新验证结果。

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| Debug 完整回归 | 766 通过，0 失败，退出码 0；用时 639.96 秒 | [完整日志](../.tmp/multi-account-tests/robustness-final-debug-full766.log) |
| 最终源码 Debug 构建 | 0 警告，0 错误 | [构建日志](../.tmp/multi-account-tests/robustness-latest-debug-build.log) |
| 最终源码 Release 构建 | 0 警告，0 错误 | [构建日志](../.tmp/multi-account-tests/robustness-latest-release-build.log) |
| 最终源码 Debug 针对性回归 | 53 通过，0 失败；用时 86.59 秒 | [测试日志](../.tmp/multi-account-tests/robustness-latest-debug53.log) |
| 最终源码 Release 针对性回归 | 53 通过，0 失败；用时 88.01 秒 | [测试日志](../.tmp/multi-account-tests/robustness-latest-release53.log) |
| 测试进程清理 | 11:10 核对工作区测试进程数量为 0，无测试后台遗留 | [核对记录](../.tmp/multi-account-tests/robustness-latest-process-cleanup.json) |

完整 766 项在最后补充输入冷连接重置、授权拒绝防止反复创建进程、嵌套窗口退出及系统关机晚到回调保护之前运行。最终源码注册总数为 770 项；随后重新构建 Debug、Release，并分别运行 53 项针对性回归，覆盖这些末尾修改，没有再次运行完整 770 项。

本轮增加 20 项测试：7 项管理器故障与并发测试、3 项后台关闭测试、6 项窗口生命周期测试和 4 项 KMBox 冷连接重置测试。前 16 项包含在完整 766 项中，全部 20 项包含在最终 53 项中；同时扩展原有授权拒绝、硬件验证窗口取消和系统关机测试。

故障测试使用真实子进程执行并发启停、随机强杀、主界面管理器被杀、初始化不返回和关闭断连。KMBox 协议测试仅访问 `127.0.0.1` 的临时 UDP 端口，不连接真实设备。

## 首轮实现验证结果（历史）

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| Debug 完整回归 | 750 通过，0 失败，退出码 0；用时 578.66 秒 | [完整日志](../.tmp/multi-account-tests/final-debug-tests.log) |
| 最终源码 Debug 构建 | 0 警告，0 错误 | [构建日志](../.tmp/multi-account-tests/final-debug-build.log) |
| 最终源码 Release 构建 | 0 警告，0 错误 | [构建日志](../.tmp/multi-account-tests/final-release-build.log) |
| 最终源码 Debug 新功能回归 | 33 通过，0 失败 | [测试日志](../.tmp/multi-account-tests/final-debug-feature-tests.log) |
| 最终源码 Release 新功能回归 | 33 通过，0 失败 | [测试日志](../.tmp/multi-account-tests/final-release-feature-tests.log) |
| 测试进程清理 | 测试主进程和账号子进程均无残留 | [核对记录](../.tmp/multi-account-tests/final-process-cleanup.json) |

完整回归之后补充了退出期间禁止晚到请求重新创建后台的保护，以及后台关闭失败的原因日志；最终源码重新构建，并在 Debug、Release 各复测上述 33 项。原有完整回归没有在这两处收尾后重复运行。

## 首轮新增覆盖

- 10 组真实子进程测试：账号独立 PID、重复启动、故障恢复、关闭自动恢复、重连接管、停止超时、启动取消、单账号卡住不影响其他账号、运行意图保存失败、设备租约、跨目录 KMBox 冲突、授权隔离、先释放输入再等待、退出期间拒绝启动与晚到读取。
- 7 组 IPC 测试：业务接口与数据类型、结果和进度、取消、断开、并发状态查询、认证、无效数据帧、重连、保存通知不启动后台。
- 6 组导入测试：保留业务设置和独立授权、路径/方案/地图/名单共享、同名冲突预检、设备与授权身份去重、明确物理 DMA 绑定、不发生部分导入覆盖。
- 5 组共享配置测试：并发读取完整文档、打开文件时原子替换、编码、取消与失败保留原文件、启动及恢复读取最新方案和名单、后台拒绝写入共享账号配置。
- 5 组 WinForms 测试：账号列表和独立操作、启动期间停止、硬件验证及确认、取消验证后释放后台、无授权时保存脚本不拉起后台、旧版空硬件账号迁移。

[实际 WinForms 截图](../.tmp/multi-account-tests/console.png) 使用三个模拟账号渲染；不是概念图。

原基线有一项复活路径拾取测试缺少当前快捷栏绑定夹具。本次补齐该测试夹具和前置条件同步，未修改战斗或拾取业务逻辑；完整回归中已通过。

## 验证范围

真实进程、命名管道和文件读写均已执行；DMA、KMBox 和授权后端使用测试注入。没有连接真实游戏、发送硬件输入或测量真实运行时 CPU/内存及长期稳定性。

构建的桌面复制目标统一覆盖为工作区临时目录，未替换 `Desktop/script` 的现用程序。Release 程序位于 `Roadhog/bin/Release/net8.0-windows/Roadhog.exe`；部署时需要整个运行目录的依赖文件。未执行 Git 提交或推送。
# 2026-09-22 设备编辑、角色保存及重启验证补充

- 修复真实迁移阻塞：脚本 2 保存 `devindex=0`，Windows USB 显示排序为 `1`。后台现保留保存的映射，并核对在线物理设备身份。六台本机 USB 身份只读核对通过。
- “硬件”按钮改为“设备/角色”；失败/闲置后台先回收后可编辑，运行中账号须先确认停止。支持编辑、读取真实角色及 KMBox 检查、勾选确认、保存；重新读取保存会更新角色名。
- 电脑重启后全部账号显示“待验证”，清除旧自动恢复意图。单账号完成本次开机的读取、确认、保存后才能启动；仅重开主界面仍可接管同一次开机内已验证的后台。角色与保存值不符时拒绝启动业务。
- 两个停止账号编号互换可逐个保存，运行设备仍受租约保护；保存不会自动修改其他账号。
- 最终 Debug / Release 构建均为 0 警告、0 错误；[Debug 70 项](../.tmp/multi-account-tests/hardware-ui-debug-tests.log) / [Release 70 项](../.tmp/multi-account-tests/hardware-ui-release-tests.log) 全部通过，分别用时 102.87 / 102.70 秒。模拟旧开机会话、六账号恢复意图、逐个确认、取消读取、角色错配、12 轮停止与自行退出同时发生均覆盖。
- 收尾前补做 [10 轮、90 次模拟崩溃恢复](../.tmp/multi-account-tests/hardware-ui-stop-stress.log)，全部通过。早期一轮曾返回停止失败，当时断言未输出详细原因，无法确认根因；现已补齐断言原因，并修补进程在身份检查期间自行退出会被误报停止失败的边界。后续未再复现；不能据此承诺零崩溃。
- 最终发布代码通过[原迁移包生效审计](../.tmp/multi-account-tests/hardware-ui-config-effective-audit.log)：六账号设置一致、25 条实际引用路径一致，原缺失路径仍保持隔离缺失。审计工具已适配新账号“待验证”的预期提示。
- 已部署至 `C:\Users\GoldGiven\Desktop\script\多账号客户端`，替换 4 个程序集/符号文件，28 个运行文件 SHA256 与最终 Release 一致；部署时没有覆盖配置。旧文件备份在 `.updates/hardware-20260922-123550`。
- 原新客户端与失败后台通过“退出程序”正常退出；其余五个旧客户端和源码 Debug 进程的 PID / 启动时间保持不变。新主界面 PID 23516。打开设备窗口后，交互期间用户完成脚本 2 读取、确认、保存并启动：已保存角色 `Tone`、读取编号 `0`、本次开机确认有效，后台 PID 23820；日志包含授权通过、VMM 连接、角色读取及业务运行。助手没有自动勾选角色确认或点击业务启动。
- 实际整机重启未执行；重启失效使用 Windows 易失注册表机制并通过隔离会话测试验证。真实长时间运行、设备重插和下次整机重启仍需现场确认。没有残留测试进程，也未执行 Git 提交或推送。
# 客户旧配置同名冲突修复（2026-09-22）

客户截图显示 `config/paths/人马1.json` 同名不同内容，严格导入因此中止。界面改用保留来源的导入计划：冲突路径及方案生成唯一名称，同步修改账号/方案中的全部路径引用；名单、地图按来源复制到 `config/imported`，已有文件不覆盖。旧默认机器码授权文件继承其原路径，仍使用现有签名及设备校验；导入后重新验证硬件。

新增 8 项模拟回归覆盖：同名路径的实际坐标与六类引用、相同路径复用与方案差异、来源/目标名称碰撞、原来缺失的资料不被别的账号补齐、名单/地图/机器码授权来源、无效 JSON 预检、后期写入失败回滚、外部资源及硬件冲突、两个同名客户账号连续导入。和旧导入、迁移快照、资源隔离、硬件界面及机器码授权相关测试合计 31 项通过；发布 DLL 上重跑其中 17 项通过。Release 构建零警告零错误。完整包及基于上一客户版的两 DLL 补丁另行做解压、文件哈希和空账号启动/退出检查；没有在客户机实测硬件，也没有替换本机正在运行的客户端。

# 账号列表等级与职业（2026-09-22）

角色名下增加等级和职业，保持原列数、行高和操作按钮。后台在既有可信角色读取完成后投影两个展示字段，经原有低频状态 IPC 传到主界面；不新增 DMA 查询，不将完整角色快照传到主进程。展示投影按账号和本轮启动隔离，停止/失败清空，旧读取器不能更新新一轮状态。

Release 构建零警告零错误。25 项相关测试通过：展示状态的升级、乱序、角色/设备错配及重启隔离，失败/不完整读取不发布，IPC 新字段与旧消息兼容，10 项真实 WinForms 模拟界面回归，原可信读取与架构边界，反复启停，8 项导入保留回归。模拟截图已核对两行角色信息与全部操作按钮；未更换现用六账号程序，真实设备显示待更新后验证。
