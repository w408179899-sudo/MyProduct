# KMBox Connect-only 连通性诊断

此独立工具只发送 **一个 16 字节 Connect 握手报文**，不发送按键、鼠标、ReleaseAll、断开输入或重启命令。固定 UDP 对端后，仅接受该对端、正确 command/index 且至少 16 字节的响应。结束只关闭本地 socket 并释放 `Smart.Runtime.InputLeaseRegistry` 的端点租约。

Vendor `KmBoxNetDevice.Dispose/DisconnectAsync` 会发送 ReleaseAll；因此本工具使用独立 UDP socket，完全不构造 Vendor 设备类。C# 客户端能保证所发送的命令集合；**固件内部对 Connect 的处理及影响无法由客户端代码保证**。成功只证明配置端点返回了匹配的协议握手，不证明目标桌面实际收到输入。

离线验证仅启动本机 loopback UDP 替身，不会连接真实 KMBox：

```powershell
.tools\dotnet\dotnet.exe test tests/Smart.KmBoxProbe.Tests/Smart.KmBoxProbe.Tests.csproj -c Release --artifacts-path artifacts/kmbox-probe-verification
```

真实诊断必须显式提供已经授权的端点配置路径，例如：

```powershell
.tools\dotnet\dotnet.exe artifacts/kmbox-probe-verification/bin/Smart.KmBoxProbe/release/Smart.KmBoxProbe.dll --config C:\Authorized\kmbox-net.json --timeout-ms 1000
```

配置只读取 `IpAddress`、`Port`、`Mac` 三个字段，忽略其他字段，不复制配置，不输出 Mac 或原始握手报文。`--timeout-ms` 默认 1000、范围 1–30000；无参数与 `--help` 只显示帮助，不读取配置或发包。通过返回 0，取消返回 130，其他结果返回 2。

单键、鼠标移动、绝对定位、持续按下、优先级接管、取消后的真实释放和断连恢复仍需可控的目标测试桌面；握手结果不能替代这些验收。
