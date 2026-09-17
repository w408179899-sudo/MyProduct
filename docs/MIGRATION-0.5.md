# 升级至 0.5.0

业务模块、强类型快照接口、六参数 DmaSettings 构造及解构保持兼容。
新增 DmaSettings.Binding 与 DmaSettings.Worker 为可选 init 属性；已有账号 JSON 可直接读取。
固定 0.4.1 包的旧项目源码和旧消费者 DLL 会分别对新包验证，见升级验收脚本及本轮记录；这不能替代具体项目自己的业务回归。

## 运行方式变化

正式 DMA 连接池改用独立原生 worker。部署时必须一起携带 `native-worker/`，不能只替换一个宿主 DLL。
框架依赖发布需要匹配的 .NET 10 x64 运行时；自包含发布由 Publish-Applications.ps1 同时发布宿主和 worker。
worker 缺失、身份校验不符等配置错误明确失败；不会偷偷切回进程内路径。

原来的 DLL 路径、设备 URI、进程选择与输入配置继续有效。
Binding 为空的旧配置仍为人工确认模式，不因此自动获得物理设备绑定保护。
启用绑定需从 D3XX 原始设备清单明确选定身份，保存完整拓扑和驱动指纹；重复序列号和枚举不完整必须解决后才能绑定。
详情见 [设备绑定](DEVICE-BINDING.md)。

高级 worker 设置示例：

```json
"Worker": {
  "WorkerPath": null,
  "StartupTimeoutMs": 30000,
  "OperationTimeoutMs": 5000,
  "ShutdownTimeoutMs": 3000
}
```

不要把非常短的时限当作性能优化；它可能让正常但较慢的设备频繁重连。
先用只读 probe 和自己的负载验证时限，再运行实际业务。
无业务模块、无卡密系统、读取失败由 provider 保持快照的规则不变。
