# 账号列表：本次运行统计

账号列表在“状态”与“杀怪/h”之间展示“死亡次数”和“累计击杀”。

- 统计以每个账号本次脚本启动为范围，`AccountRuntimeState.MarkStarting` 清零；不从历史日志累计，不写入账号配置。
- 死亡次数消费已有角色快照的观察回调。同一次死亡的连续快照只记一次，读到存活状态后才允许记录下一次死亡；未知血量不会重置死亡状态。旧启动代次、过期快照和其他角色的回调沿用现有隔离检查。
- 累计击杀展示当前运行的 `KillCount`，与“杀怪/h”使用同一份击杀计数。
- 两项数字由后台 `AccountRuntimeManager` 统一加锁维护，随 `AccountRuntimeSnapshot` 经现有 worker 状态 IPC 传输；界面刷新不会增加游戏读取，也不改变战斗、复活或拾取决策。
- 没有当前运行快照时显示 `—`。启动后尚未发生事件显示 `0`。旧 worker 缺少死亡次数字段时，该列显示 `—`。
- 停止后内存状态保留本轮最后数值，worker 退出后列表显示 `—`。重新启动、自动恢复中重新建立脚本运行时，均开始新一轮统计。

相关测试：`AccountSessionStatisticsTests`、`LootTouchTests.SessionStatistics`、`MultiAccountUiTests.SessionStatistics`。原有界面用例只调整新增两列导致的总列数和按钮起始位置，并用按钮名称定位点击，其余行为标准保持不变。
