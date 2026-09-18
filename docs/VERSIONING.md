# 公共版本维护

默认引用 src 中的公共源码，便于调试。多个业务项目共用底座时，可使用 NuGet 包模式。

## 打包与消费

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Pack.ps1
.tools\dotnet\dotnet.exe build Smart.slnx -c Release -p:SmartUsePackages=true
```

七个公共包输出到 artifacts/packages。Directory.Build.targets 仅把消费者对公共工程的引用切换为包；Domain/Application/Bootstrap 等项目间引用保持原结构。包模式下固定引用 Directory.Build.props 中的 SmartFrameworkVersion，不使用浮动版本。

新项目可把七个 nupkg 复制到自己的 artifacts/packages，或通过 `-p:SmartPackageFeed=<共享包目录或内部源>` 指定公共源。设置 SmartUsePackages=true 后，旧项目无需逐份修改 src；升级版本前运行其自身业务回归。

`Test-Packages.ps1` 使用独立包缓存验证实际包消费者，避免本机同版本缓存掩盖重新打包的内容。正式发布应版本递增，不能覆盖已发布版本。

## 发布规则

1. 公共底座修改先通过 Test、TemplateSmoke、Test-Performance、Test-Soak 和 Test-Packages。
2. 兼容修复递增补丁版本；新增兼容能力递增次版本；破坏接口修改需要迁移说明。
3. 保存包及验证记录，再由项目显式升级 SmartFrameworkVersion。
4. 未提供 native DLL，硬件库和固件版本需按部署环境单独管理。

CI 工作流在 Windows 上执行上述检查并保存 artifacts。工作流随源码维护；远程是否执行和通过，以 GitHub Actions 的实际结果为准，不能用本机测试代替。

0.3.0 调整了模块装配接口，从 0.2.0 升级需要按 MIGRATION-0.3.md 修改组合根；数据发布的核心语义保持一致。

0.4.0 增加异步快照收束、分区通道回放与控制台常驻宿主，修复取消异常和模板消费者检查；同时对根/账号 JSON 未知字段明确报错。升级事项见 MIGRATION-0.4.md。

0.4.1 修复实机发现的 VMM 第 0 项参数错误，统一通过 `VmmTransport.CreateArguments` 构建参数；设备 URI、重复设备参数和非法字符串在调用原生库前校验。读取与正式快照语义保持不变。

0.5.0 增加原生 worker 隔离、可选设备身份绑定、固定旧包升级验收和应用发布清单。公开配置保持兼容，但部署需要完整 worker 目录；见 [0.5 升级说明](MIGRATION-0.5.md)。

0.6.0 增加 WPF/MVVM 公共主页、按账号保存、实际会话目标展示、隔离设备枚举和纯握手诊断。配置格式及正式快照规则保持兼容；桌面视图模型命名空间与生成入口变化见 [桌面说明](DESKTOP.md)。
