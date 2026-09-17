# DMA/VMM 与 KMBox

默认配置为 Mock，空通道、空模块、Mock 输入。真实运行必须配置设备、进程选择器、模块和 KMBox，模板不携带原生 DLL 或目标程序偏移。

## DMA

VmmTransport 使用 [MemProcFS 官方 C 头文件](https://github.com/ufrisk/MemProcFS/blob/master/includes/vmmdll.h) 的 Initialize/Close、PidList、ProcessGetInformation、ProcessGetModuleBaseU，以及 Scatter_Initialize/Prepare/ExecuteRead/Read/CloseHandle。缺少 scatter 入口的旧版本回退 MemReadEx。使用 NOCACHE 读取；正式快照缓存与失败保留仍只由 provider 管理。

初始化参数统一使用 `VmmTransport.CreateArguments(deviceUri, extraArguments)`。VMMDLL 从第 0 项开始解析选项，不能将程序名放在该位置；helper 使用空首项，再附加设备及额外选项。0.4.1 已修复连接池、Console 与探针入口，并在加载 DLL 前拒绝程序名首项、重复设备和非法字符串。

一次调度批次最多 256 段、总计 1 MiB。支持 scatter 不代表一次物理 DMA 事务，也不保证跨字段原子性。scatter 和 MemReadEx 的短块都可能有空洞；成功字节总数不证明连续有效前缀。两条路径均只在完整成功时暴露字节，否则返回空字节块及 Complete=false。需要部分字段更新时，应把可独立验证的字段拆成请求，由项目 decoder/merger 合并。

该行为依据 [MemProcFS 的跨页读取实现](https://github.com/ufrisk/MemProcFS/blob/master/vmm/vmm.c#L1770-L1834)，回归覆盖首个页失败而后续页成功、失败返回完整计数和越界计数。

HardwareSessionFactory 根据 PID 或唯一进程名绑定；进程重名必须指定 PID。目标模块必须已加载。模块基址和 4 KiB 头部摘要参与身份；进程采用 EPROCESS/DTB/PEB 组合。检查使用同一连接锁；正常轮询只检查已选中的 PID，不扫描所有进程模块。

正式绑定/心跳得到模块地址 0 时，视为身份检查失败，不构造伪模块身份。因为 [ModuleBaseU 返回 0 也表示解析失败](https://github.com/ufrisk/MemProcFS/blob/master/vmm/vmmdll.c#L2585-L2604)，不能仅凭 0 宣称已确认卸载。诊断清单仍允许某进程没有解析到可选模块。

这些身份是可观测指纹，不是操作系统提供的完整启动时间。极短的卸载重载、同地址同内容复用发生在两次轮询之间时，轮询无法保证发现。项目拥有更强的会话标识时应继续扩展身份检查。

同一 DMA URI 的账号共享 VmmConnectionPool；库路径和连接参数必须一致。连接退役会促使旧会话退出，所有引用释放后才能重新打开。不同进程通过公共文件锁排他。

USB 清单仅作选择参考，不根据枚举顺序自动分配 devindex。配置前核实真实设备映射，重插后再次核对。跨 Windows 用户运行应指定相同且有权限的租约目录。

直接使用 VmmTransport 的 native 同步调用不能用 CancellationToken 强行中断。0.5.0 正式连接池和 Console probe 已改用独立 worker；超时终止自己的子进程并确认退出后才允许重连，详见 [原生隔离](NATIVE-ISOLATION.md)。可选的 [设备绑定](DEVICE-BINDING.md) 校验实际驱动枚举身份；旧配置仍须人工确认 devindex。

## KMBox

Vendor/Hardware.KmBox 的通信源文件保持原实现。KmBoxInputDevice 负责适配统一命令，包括相对移动和基于底层归零流程的绝对移动；后者仍需现场坐标校准，不能等同于已验证屏幕定位。

输入端点按 IP/端口独占，同一端点不能通过不同 MAC 配置绕开租约。硬件会话在获得租约后连接并释放残留输入。持续按键由 ActionExecutor 的所有者和优先级管理，清理失败时保留租约并允许重试。

IP 地址在适配器中规范化，同一 IPv4 地址的不同写法共享同一租约标识。native 连接关闭或文件租约清理失败时保留未完成状态，不能把“开始关闭”当作“已经关闭”。

## 配置和检查

桌面端编辑硬件配置，或导入带 SchemaVersion/Settings 的账号 JSON。ProcessId=0 在界面中表示按进程名选择；VMM 参数不重复传入 -device。

实时会话禁止 -norefresh：该选项关闭 [MemProcFS 的后台缓存刷新](https://github.com/ufrisk/MemProcFS/blob/master/includes/vmmdll.h#L96-L97)，与依赖实时进程/模块变化的会话约束冲突。账号配置和 VMM 构造入口都会在连接前拒绝它。

Console 的 `--probe <accountId> --config <file>` 只打开 DMA、列出进程和模块，不初始化 KMBox。`--run-hardware` 才运行硬件会话；当前空项目没有业务动作。

0.4.1 已使用明确指定的空闲设备完成真实 DMA 初始化、进程/模块识别及 KMBox Connect 握手；测试范围与后续批量读取结果见 [验证记录](VALIDATION.md)。握手不能证明目标桌面的实际输入。受控动态数据、物理输入、断连恢复和长时间运行仍需分别验收。
