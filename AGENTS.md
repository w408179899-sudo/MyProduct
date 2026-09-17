# Smart 开发约定

本项目采用 C# / .NET 10，DMA/VMM 读取与 KMBox 输入由适配器负责。不要加入卡密系统、授权服务器、许可证校验或相关启动门禁。

- Domain 只表达业务规则，不引用 Smart.Data、Runtime、UI 或硬件。
- Application 仅引用 Domain 与 Smart.Contracts；新功能按模块注册，不向主循环添加业务分支。
- DMA 业务读取统一通过封闭的强类型通道目录；遵循 dma-read-quality-foundation 技能。
- 有效新值立即发布，Partial 合并有效字段，Failed 保持原正式快照及版本，冷启动在提供者内重试。
- 质量标志和原始读取结果只出现在 Data、Adapters 与项目 Infrastructure 中；Bootstrap 负责装配。native API 留在硬件适配器中。
- 版本、会话代次由唯一发布器管理；重连、进程/模块变化后重置会话，拒绝晚到结果。
- 模块只提交动作；执行器统一持有输入设备租约、取消、超时和释放。动作确认检查正式业务结果。
- 模块 Tick 必须短且可取消；长流程拆成状态机。算法使用 WorkBudget 并限制输入规模。
- Mock 模式同时替换读取与输入。默认示例不得初始化硬件。
- Build 不部署、不复制桌面文件、不启动或重启现有脚本。
- 核心修改补行为/故障测试；测试默认不依赖硬件。执行 scripts/Build.ps1 Test。
- 修改模板结构后执行 scripts/Build.ps1 TemplateSmoke。
- 不引用原 Tool/Roadhog 工作区的绝对路径；Vendor 的来源与变更写入 docs/ADAPTERS.md。
- 清楚区分源码测试、模板生成、性能样本与实机验证结果。
