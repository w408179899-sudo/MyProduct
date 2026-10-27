# 2026-10-28：UU 类型读取与脚本2 Windows 符号修复

用户授权同时修复两处登录阻塞，并使用脚本2配置执行一次自动登录。实测使用隔离构建中的生产 `AutoLoginRunner`、`VmmAutoLoginSession` 和原账号配置；不替换运行中的主客户端，不启动挂机。日志和结果不包含账号密码或识别文本。

## 修复范围

1. UU 的静态类型身份读取短缺时，不再提前打断后续完整证明。依次尝试已有目录、目标方法和 RTTI；明确身份冲突仍拒绝采集。同一连接、账号、进程及模块会话可保留已经校验的模块身份，实际控件、可见性、位置和输入前绑定仍重新读取。
2. 脚本2的 `win32kbase.sys` 头页无法读取，原生 PDB 注册因此失败，但驻留页包含完整 CodeView，匹配 PDB 已在本机缓存。新增按当前模块逐页查找 CodeView、复核 GUID/age、解析匹配缓存内两个公开数据符号的适配路径。符号地址按当前加载基址计算，不写死 Windows 版本偏移。

原生 PDB 路径优先；替代路径只接受现存且身份匹配的本地 PDB，不下载或猜测其他版本。PE 头和 CodeView 均不可读，或没有匹配缓存时，仍由 provider 等待重试并遵守既有超时。前台所有者及鼠标坐标始终现场读取，静态元数据缓存不能代替实时窗口校验。

## 实机证据

- 配置：脚本2，角色 Tone，设备 `fpga://devindex=1`。
- [只读恢复证据](symbol-recovery.json)：头页请求 4096/64 字节均返回 0；模块扫描中 386 页可读、340 页不可读。CodeView 指向 `6F63F6164996847EB668B0098D184F4B1/win32kbase.pdb`，恢复前台和鼠标读取。
- [修正版操作前快照](before.json)：05:53:40 确认桌面和前台 explorer，未发送输入。
- [完整登录日志](login.log)、[结果](login.json)：05:54:12 开始，05:54:30 点击 UU 启动游戏，05:54:55 点击开始游戏，05:55:49 提交账号登录；之后依次同意协议、选服、确认种族限制、选择 Tone、勾选并确认入场，**05:57:52 正式快照确认 InWorld/Tone，Aion PID 10856**。
- 本次未出现验证码。窗口切换和游戏加载期间发生的短读、前台复核失败均重试后继续；启动游戏后的 20 秒等待保留。
- 登录成功后输入及设备租约释放；`HangupStarted=false`。未停止其他账号，未更新正式运行目录，未执行 git push。

## 测试与构建

Release 构建 0 警告、0 错误。修复后的登录相关 75 项通过；随后补全 ASCII 安装目录的原生 DLL 实际位置查找，重新构建并通过 4 项 native 相关测试，再用最终 DLL 完成上述实机登录。未运行全量测试。

新增覆盖匹配/错误 GUID 和 age、损坏或越界 PDB、重复符号、地址重映射拒绝、不可读头页、跨页 CodeView、复查变化、多记录歧义、会话隔离、静态数据短读保留、UU 替代证明和身份冲突拒绝。原有可信快照发布及输入前复核边界保留。

哈希和执行范围见 [验证清单](validation.json)。桌面 `Roadhog_登录双修复_20261028` 保存与实测相同的 `Roadhog.dll` 和更新说明；已有完整登录客户端退出后覆盖 DLL，账号配置无需修改。

## 格式依据

- [MemProcFS PDB 实现](https://github.com/ufrisk/MemProcFS/blob/master/vmm/pdb.c)：常规模块符号注册需要 PE/CodeView 信息。
- [Microsoft PDB OpenValidate](https://github.com/microsoft/microsoft-pdb/blob/master/PDB/dbi/pdb.cpp)：公开 PDB 的 Info age 可大于映像 age；DBI age 与映像 age匹配。本例分别为 3、1、1。
- [Microsoft CodeView 定义](https://github.com/microsoft/microsoft-pdb/blob/master/include/cvinfo.h)、[LLVM DBI Stream](https://llvm.org/docs/PDB/DbiStream.html)：公开符号、节表与调试流格式。
