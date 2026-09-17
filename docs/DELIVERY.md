# 应用交付与离线验收

公共模板提供固定的 Windows x64 Console、Desktop 发布入口。发布只写入独立的 `artifacts` 子目录，不复制桌面脚本目录、不上传文件、不制作安装器。当前空模板不包含游戏业务或卡密系统；生成工程以后，项目自己的业务程序集随其正常项目引用发布。

## 发布应用

在 Windows x64 PowerShell 中执行：

```powershell
# 默认框架依赖发布；目标机器需要兼容的 .NET 10 Windows Desktop x64 Runtime。
./scripts/Publish-Applications.ps1 -Runtime win-x64 -Configuration Release

# 自包含发布；Console、Desktop 与 native worker 使用相同的部署模式。
./scripts/Publish-Applications.ps1 -SelfContained
```

版本读取 `Directory.Build.props` 的 `SmartFrameworkVersion`，脚本不另维护版本。每次发布目录是：

```text
artifacts/application-publish/<version>/<unique-run>/
  build/                       本次独立编译输出，不用于交付
  applications/
    publish-manifest.json      版本、RID、部署模式、文件大小与 SHA256
    console/                   Console exe、dll、deps、runtimeconfig 和依赖
      native-worker/           Smart.Dma.Worker 及其运行依赖
    desktop/                   Desktop exe、dll、deps、runtimeconfig 和依赖
      native-worker/
  smoke/                       离线运行副本与结果，不用于交付
```

交付 `applications` 中所需的完整宿主目录及清单。不要只复制 exe 或单个公共 DLL。默认从 `src/Smart.Dma.Worker/Smart.Dma.Worker.csproj` 发布 worker；仅当工程布局改变时才通过 `-NativeWorkerProject` 显式指定项目。`-ArtifactsPath` 可指定另一处产物根目录，脚本仍在其中创建版本与唯一运行子目录，不覆盖旧发布。

发布不裁剪、不合并单文件，便于排查依赖与核对版本。框架依赖版本和自包含版本都生成 apphost exe。自包含发布会包含 .NET 自身的原生运行库；VMM、LeechCore、FTDI 等硬件供应商 DLL 不随此流程分发，需要在硬件部署时单独提供并配置。

`Assert-ApplicationPublish.ps1` 会检查清单中的每个文件及额外文件，拒绝 `data`、`logs`、`config`、`configuration`、`settings`、`external`、账号及 DMA/KMBox 配置、设备租约、日志、硬件 DLL 和 nupkg。两个宿主及其 worker 都必须提供 exe/dll/deps/runtimeconfig，版本与部署模式必须一致。发布出现这些内容时直接失败，不静默删文件掩盖输入污染。

## 验证实际发布产物

```powershell
# 发布后，在独立副本中启动真实 apphost，使用纯 Mock 数据与输入。
./scripts/Test-Publish.ps1
./scripts/Test-Publish.ps1 -SelfContained

# 复核某次已有发布，不重建。
./scripts/Test-Publish.ps1 -ManifestPath <applications/publish-manifest.json>

# 只校验发布清单，不启动应用。
./scripts/Assert-ApplicationPublish.ps1 -ManifestPath <applications/publish-manifest.json>
```

Console 必须完成一个 Mock 会话并报告停止、无错误；Desktop 以隐藏 smoke 模式验证启动、暂停、恢复和停止。随后分别启动两个宿主附带的 worker `--help`，验证实际 apphost 和依赖可运行；该入口在创建管道或初始化 native 库之前结束。仅测试副本可以产生 Mock 配置与日志，交付目录在运行前后都重新校验 SHA256。脚本不会传入硬件运行或只读设备探测参数，也不会启动 native worker 的硬件会话。

CI 同时执行两种发布模式、原始公共包消费者测试，以及固定旧版本基线的升级测试。发布 smoke 验证可交付文件与生命周期，不等同于目标游戏、DMA 或 KMBox 的硬件验收。

`Test-PackageWorker.ps1 -PackageFeed artifacts/packages` 额外创建只通过 NuGet 引用 DMA 适配器的独立消费者，限制为指定本地包源。它确认包内四个 worker 文件及 `buildTransitive` 规则存在，核对 build/publish 产物与还原包的 SHA256，并分别运行 worker 的无硬件 `--help`。因此宿主 Mock 测试不能掩盖 worker 漏包或复制失败。该检查也已接入 CI。

## 诊断与探针统一 Release 构建

```powershell
./scripts/Test-Probe.ps1
./scripts/Test-ProbeAdapter.ps1
./scripts/Test-Diagnostics.ps1
```

三个脚本都支持 `-ArtifactsPath`，并在其下创建本次独立目录。诊断和适配器 solution 显式列出 Core 和 native worker 项目，避免只有诊断工具使用 Release、未列入 solution 的传递依赖退回 Debug。

每次测试之后，`Assert-ReleaseOutput.ps1` 遍历 solution 及其传递项目引用：必须找到每个项目的 Release DLL、对应编译生成的 `AssemblyConfiguration("Release")`，且此次目录不能出现 Debug 产物。`release-build.json` 记录实际 DLL 的 SHA256，日志输出 `RELEASE_DEPENDENCIES_VERIFIED`。因此新增未正确映射的引用会直接导致 CI 失败。

协议目标测试只创建有限时长的本机测试进程；适配器与诊断测试只使用托管 fake transport 和回环 UDP。运行这些脚本不会打开 DMA 或真实 KMBox 设备。
