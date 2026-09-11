# 2026-09-09 偏移更新与账号 4 验证

已应用同事提供的全量对比数据，更新正式 VMM 读取器及项目内可对应的重复偏移，并通过账号 4 的当前设备做了只读实机验证。实机发现的地图读取链问题已一并修复。

## 源码更新

- `AionVmmGameApi.cs`：34 个直接 RVA 常量更新，另有 3 个别名/计算 RVA 随之更新；EntitySystem 保持 `0x94C7B0`，对象内成员偏移保持对比表值。
- 地图上下文：旧名称 `CurrentMapContextPointerRva` 改为 `CurrentMapContextRva`，地址为内嵌对象 `GameBase + 0xD647C0`。地图 ID 直接读取 `GameBase + 0xD647C0 + 0x20DC`，即 `GameBase + 0xD6689C`。
- 队伍探针同步 8 个 RVA，战术标记探针同步 3 个 RVA 及输出说明；频道测试同步 3 个 RVA。
- Tool 同步 32 个能与新清单明确对应的共享字段；掷点窗口首槽改为 `RollDialogTableRva + PartyRollDialogFirstId * 8`，当前结果 `0xD64100`。
- 地址探针加入当前地图 ID、频道索引和频道总数，从 39 项扩到 42 项。`game_api_probe --verbose` 输出 provider 诊断和全部地址检查，便于确认实际值。
- 原始对比文件按字节保留为 `OFFSETS_COMPARISON_2026-09-09.md/.csv`；当前清单为 `OFFSETS.md` 与 `OFFSETS_CURRENT_2026-09-09.csv`。当前表 199 个命名常量全部匹配源码；36 项补充数据包括本次新增的地图 ID 实际地址。

当前仓库没有对应拍卖行实现；同事附带的拍卖行函数 RVA 保留在原始对比 Markdown 中。Tool 专有的静态技能缓存、特殊镜头距离、额外物品字段不在同事这份 234 项数据中，未猜测新值，也不属于本次 Roadhog API 验证覆盖。

## 账号与设备

- 使用 `Desktop/script/4/config/accounts.json` 确认账号 4 原配置：`fpga://devindex=1`，保存的 USB 位置为 `Port_#0003.Hub_#0002`。
- 首次按原配置连接报 `VMM INIT FAILED`，失败发生在游戏进程与偏移读取之前。
- FTDI `FT_CreateDeviceInfoList` 返回成功、设备数量为 1；当前设备位于 `Port_#0005.Hub_#0006`。
- 用户确认当前唯一设备就是账号 4，随后仅通过 `fpga://devindex=0` 测试。没有修改桌面账号配置中的保存绑定。
- 实机角色 `ALAKA`，职业弓星；Aion.bin PID `3400`，Game.dll 基址 `0x4A240000`。
- 从当前加载模块 PE 头读取：Machine `0x8664`、TimeDateStamp `0x6A962841`、SizeOfImage `0x141D000`。没有获取更新后磁盘文件版本及 SHA-256。

## 实机结果

最终 `game_api_probe` 退出码为 0；18 个 API 项目通过，本地异常状态另重复两次读取通过；地址检查 **42/42 通过**。

| 数据 | 最终实机读数 |
|---|---|
| 本地角色 | ALAKA，EntityId 65535，弓星 |
| HP / MP | 4530/4530；4238/4238 |
| 世界坐标 | 1385.202, 1387.824, 209.298 |
| 当前目标 | EntityId 65462，ServerObjectId 2147555181，HP 1392/1392 |
| 地图 / 频道 | MapId 120010000；索引 0、总数 1，即显示频道 1 |
| 已学技能输出 | 30 项 |
| 背包 | 62 个输出物品，容量 81；树记录数 63 |
| 金币 | 25704370 |
| 背包读取完整性 | provider 报告 Complete，无 error |
| 第一条物品身份 | 节点 InstanceId 与物品 InstanceId 均为 3221494217；TemplateId 167000514；数量 2；slot 30；魔石:生命力+85 |
| 物品静态索引 | 56923 项；chunk 0 压缩长度 857、解压长度 16384 |
| 背包窗口 | Dialog27 可见，flags 0x418F；Dialog28 不可见 |
| 背包 Dialog27/28 方法 | 新 RVA 的函数体分别返回 27 / 28：B81B000000C3CCCC、B81C000000C3CCCC |
| 周围对象 | 最终采样 28 个；环境对象数量可能随玩家活动变化 |
| 队伍 / 标记 / 宠物 | 当前无队伍、无激活标记、无宠物；空状态读取通过 |
| 采集 / 尸体 / 丢弃确认框 | 当前没有采集节点、没有采集进度、没有尸体、没有丢弃弹窗；当前状态读取通过 |

地图修正的直接证据：

```text
GameBase = 0x4A240000
GameBase + 0xD647C0 = 0x4AFA47C0         // 内嵌地图上下文
*(uint64*)0x4AFA47C0 = 0x4AA60678       // 指向模块内虚表
*(uint32*)(0x4AA60678 + 0x20DC) = 0x0070006F = 7340143
附近 UTF-16 原文：Unable to open a socket for the direct game server!

*(uint32*)(0x4AFA47C0 + 0x20DC) = 120010000
Address.CurrentMapId: RVA=0xD6689C, address=0x4AFA689C; value=120010000
```

本地 CEntity `vtable + 0xB8` 的 getter 字节为 `488B81C0020000C3...`，仍符合现有 `48 8B 81 disp32` 解析规则，读出的 proxy manager 成员偏移为 `0x2C0`。本次没有改变 Actor 的解析策略。

实机验证只读，不发送键鼠操作。队伍有成员、非空异常/宠物/战术标记、正在采集的两条进度条、尸体拾取、真正打开的丢弃弹窗、特殊镜头、root widget 实验 Rect 等需要对应场景，不能用本次空状态通过替代这些场景验证。

## 构建与回归

- Roadhog.Tests Debug 构建：0 warning / 0 error。
- Roadhog Release 构建：0 warning / 0 error。
- Tool Debug 构建：0 warning / 0 error。
- 最终全量回归：**550 通过，1 失败**。唯一失败为 `window title formats character identity`，预期 `GreenPlayer 路哥`，实际 `路哥`。开工时已存在 `RoadhogWindowTitleFormatter.cs` 将追加角色名改为直接使用角色名的未提交改动；本次没有改动该文件或其测试预期。
- 本次涉及的频道布局和 API 探针回归均通过。
- 199/199 当前常量、共享探针/Tool 字段、地图实际地址、42 项地址探针登记与返回项均完成静态一致性检查。
- `git diff --check` 通过。

日志：

- [最终账号 4 API 与地址探针](../../.tmp/offset-update-20260909/account4-live-final.log)
- [地图原始读取与 PE 头证据](../../.tmp/offset-update-20260909/account4-raw-map-probe.log)
- [最终全量回归](../../.tmp/offset-update-20260909/tests-full-final.log)
- [Debug 构建](../../.tmp/offset-update-20260909/build-debug-final.log)
- [Release 构建](../../.tmp/offset-update-20260909/build-release.log)
- [Tool 构建](../../.tmp/offset-update-20260909/build-tool-final.log)

复现命令（在仓库根目录运行，设备编号仅适用于本次用户确认的一块设备）：

```powershell
& '.\.tmp\offset-update-20260909\bin\Debug\net8.0-windows\Roadhog.Tests.exe' game_api_probe '--device=fpga://devindex=0' '--process=Aion.bin' '--module=Game.dll' --snapshot-repeat=3 --verbose
```

## 2026-09-09 验证时的部署与提交状态

当天的新构建只写入工作区 `.tmp/offset-update-20260909`；构建后的自动复制目标也重定向到该临时目录。当时未覆盖任何 `Desktop/script/N` 的程序，未改账号配置，未提交或推送代码。

## 2026-09-11 发布前复核

- 将本次待提交文件从 Git 暂存树导出到独立目录，验证实际待发布源码。本机启动参数、IDE 缓存、日志和编译产物未纳入提交。
- 窗口标题“只显示角色名”的独立修改已同步测试预期，并覆盖空角色名回退和空白裁剪；上面的 9 月 9 日标题测试失败已解决。
- Roadhog 全量回归 **559 通过、0 失败**；Roadhog.Tests Debug、Roadhog Release、Tool Debug 构建均通过，无警告、无错误。
- 199 个常量声明与当前 CSV 一致；共享字段更新清单、42 项地址探针登记与输出、地图 ID 实际地址均通过静态核对。
- 源码散列按 LF 换行规范化后核对，避免 Git 的 CRLF 转换造成原始字节散列差异。原始对比 Markdown 的两处行尾双空格为主动保留的换行格式。
- 本次未重新连接实机，实机证据仍为上面的 9 月 9 日记录。构建及自动复制目标均位于 `.tmp/remaining-publish-20260911-221642`，未部署桌面账号程序。
