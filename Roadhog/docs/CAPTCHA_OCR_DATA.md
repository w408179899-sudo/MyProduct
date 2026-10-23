# 启动器验证码：DMA 取图与 OCR 接入记录

记录日期：2026-10-23（Asia/Shanghai）。本文保留调查、离线识别证据和后续选型变更；当前接入状态以最近的变更记录为准，历史样本结论不覆盖后来的运行。

## 后续选型变更：lh.dll（2026-10-23）

用户后续要求以同事程序的 `lh.dll` 替换 ddddocr。新的 [x86 进程隔离适配器](../tools/captcha_ocr/README.md) 已实现，当前临时登录诊断入口已切换；历史样本和下方 ddddocr 对照记录保留用于复现。

实际加载当前 DLL 并调用模型初始化后返回 **-88 / license_required**。用户当前没有插件注册码，因此暂未得到 lh.dll 的真实识别结果，也未恢复验证码刷新、输入或提交。8项适配器测试通过不代表验证码识别成功。生产 Roadhog 自动登录尚未接入。

## 之前确定的 ddddocr 选型（历史记录）

用户明确选择 **ddddocr 默认模型**，`beta=False`。已实测环境为 Python 3.10.5、ddddocr 1.6.1、onnxruntime 1.23.2，CPU 推理。

```python
from pathlib import Path
import ddddocr

ocr = ddddocr.DdddOcr(beta=False, use_gpu=False, show_ad=False)
raw_text = ocr.classification(Path(image_path).read_bytes())
```

- 模型只接收图片字节；用户给出的答案仅在推理后用于核对。
- 用户确认验证码固定为 **6 位**。本登录弹窗显示字母不区分大小写，因此记录原始输出，同时按忽略大小写核对。
- 成功样本使用 DMA 还原的原始 PNG，没有额外放大、二值化、裁边或字符范围限制。
- 长度不等于 6 的结果应判为无效候选；长度等于 6 也不能证明内容正确。不可补位、猜字或把用户提供的答案写回识别结果。
- 已选定库不代表可靠率已确定，也不代表服务器验证通过。自动输入、刷新与失败重试规则仍待后续流程设计。

## 两张样本的实际结果

| 样本 | 用户提供的答案 | ddddocr 默认原始输出 | beta 原始输出 | 默认模型结论 |
| --- | --- | --- | --- | --- |
| [01](evidence/captcha-ocr-2026-10-23/samples/sample-01.png) | `WLP63M` | `wLpm` | `wL6M` | 错误，仅 4 位；推理约 19.50 ms |
| [02](evidence/captcha-ocr-2026-10-23/samples/sample-02.png) | `P3TMSK` | `p3tmsk` | `p3yMsk` | 忽略大小写与答案一致；推理约 9.30 ms |

时间为本机单次 CPU 推理，不包含 DMA 取图、初始化和登录操作。两张样本不足以估计总体准确率，当前事实是一次失败、一次匹配。

对照库也已运行：PaddleOCR 3.7.0 / PaddlePaddle 3.3.1，使用 `en_PP-OCRv5_mobile_rec`、`PP-OCRv6_medium_rec` 和 `PP-OCRv5_server_rec`；Tesseract 使用 tesserocr 2.12.0 与官方 `tessdata_best/eng`。

| 模型 | 样本 01 原图输出 | 样本 02 原图输出 |
| --- | --- | --- |
| PaddleOCR v5 英文 | `WP6M` | 空字符串 |
| PaddleOCR v6 medium | `WLP6M` | `PMSK` |
| PaddleOCR v5 server | `サが业` | `pMSK` |
| Tesseract | `50` / `Trp` | `S350` / `pA` |

对照测试包含放大、Otsu 二值化、前景裁边及 Tesseract 分割参数。样本 01 的 21 次对照识别均未匹配；样本 02 共 26 次识别（含两个 ddddocr 模型），只有 ddddocr 默认原图结果忽略大小写后匹配。以上为同一图片的不同配置结果，不能作为独立样本计算准确率。

## DMA 取图已证实的范围

实测目标为多账号客户端中的 **脚本1 / HiApple**。账号配置来源：

`C:\Users\GoldGiven\Desktop\script\多账号客户端\config\accounts.json`

实测绑定为 `HardwareKey=port:Port_#0005.Hub_#0009`、`VmmDeviceName=fpga://devindex=5`。旧 `script\1\config\accounts.json` 曾保存其他设备索引，不得代替当前多账号配置。后续运行应重新核对绑定并通过设备租约获得独占访问。

这两次取图均从目标机器 `sdologin.exe` 的内存读取编码图像，再在本机解码成 **120×60 PNG**；没有依赖目标机截图、PrintWindow、目标侧助手或远程输入。

只读探针的取证过程：

1. 获取脚本1实际绑定设备的租约，检查唯一活动登录进程、模块头和进程会话身份。
2. 从映射的非模块内存页发现 JPEG/PNG 候选，排除读取失败或不完整的页面。
3. 校验完整编码数据的长度、可解码性及尺寸，再次无缓存读取并比较字节，保留验证一致的图片。
4. 结束时复查进程身份，并释放设备租约。诊断失败不作为图片内容使用。

**旧验证码会残留在内存里。** 样本 01 由用户明确确认图片与当时界面一致；样本 02 依据新出现的完整图片哈希、两份相同编码副本以及与用户答案相符的视觉内容关联。尚未证明“当前可见验证码控件 → 当前图片缓冲区”的稳定引用链，不能随便选第一个图像候选或把本次内存地址当成固定偏移。

| 样本 | 保存 PNG 的 SHA256 | 源 JPEG 的 SHA256 / 长度 |
| --- | --- | --- |
| 01 | `8293e07fd90f0dc571438449e680348feb2b4091f61d7ce4ea7c8872f4c697dd` | `7770DF8D64A71B257B7DCC7C46B0A789E1DCEEE3BAF9DA85EC3A47AD69085164` / 3552 字节 |
| 02 | `0be8dc7c0337bebff399c5a6a74322dbd47b9cc2f11a5754d69fb54af531139a` | `8BAA297F85F6C261BAAE1FDF44B1C856782EFF7EE76F92B1CCBB5A682F91C126` / 3533 字节 |

样本 02 取证时，登录进程 PID 为 2292；发现的两个编码副本位于 `0x6CC1EA8` 与 `0x9EB012B`。这些仅是当次会话证据，不是可复用定位数据。正式接入仍需处理进程更换、刷新后图片更换和旧候选排除。

两次测试均没有刷新、填写、提交验证码或写入目标内存；尚未进行服务器校验。DMA 可还原图片、OCR 输出与人工答案匹配、服务器接受验证码是三个独立结论。

## 归档与复现

持久归档目录：[evidence/captcha-ocr-2026-10-23](evidence/captcha-ocr-2026-10-23)。

- [索引与哈希清单](evidence/captcha-ocr-2026-10-23/index.json)：选型、样本来源、原始路径、逐文件 SHA256 和已知边界。
- `samples/`：两张原始 PNG。
- `results/`：原始 OCR 输出、对照结果，以及只保留相关字段的 DMA 采集凭据。历史 JSON 中的 `.tmp` 路径保留原样，用于追溯；实际图片已归档。
- `replay/test_ddddocr.py`：历史样本运行默认与 beta 的对照脚本；当前适配器选型见本文开头。
- `replay/requirements-ddddocr.txt`：实测环境完整依赖版本。
- `replay/test_alternatives.py`、`requirements-alternatives.txt`：对照测试脚本和环境版本。需要显式指定新图片、哈希、参考答案与输出标签；模型缓存、Tesseract 英文数据未随归档复制。
- `probe-source/`：DMA 探针的 `Program.cs`、`ImageMemoryScan.cs`、项目文件原样快照，文件名加 `.txt` 避免被主项目编译。恢复到原 `.tmp\captcha-dma-20261023` 目录并去掉 `.txt` 后可按原相对引用重建；在其他目录需要调整引用。探针仍含本次脚本1设备绑定保护，不应直接用于其他设备。

在仓库根目录对归档样本复现 ddddocr（只读本地图片）：

```powershell
py -3.10 -m venv '.tmp\captcha-ddddocr-replay'
& '.tmp\captcha-ddddocr-replay\Scripts\python.exe' -m pip install -r 'Roadhog\docs\evidence\captcha-ocr-2026-10-23\replay\requirements-ddddocr.txt'
& '.tmp\captcha-ddddocr-replay\Scripts\python.exe' -X utf8 'Roadhog\docs\evidence\captcha-ocr-2026-10-23\replay\test_ddddocr.py' 'Roadhog\docs\evidence\captcha-ocr-2026-10-23\samples\sample-02.png' '.tmp\captcha-ddddocr-replay\result.json' --source-note 'Archived DMA sample from 2026-10-23; offline replay only'
```

本机现成环境（临时目录，可能被清理）：

- OCR Python：`.tmp\captcha-ocr-20261023\venv\Scripts\python.exe`。
- 替代模型 Python：`.tmp\captcha-ocr-20261023\venv-paddle\Scripts\python.exe`。
- DMA 探针：`.tmp\captcha-dma-20261023\bin\Release\net8.0-windows\CaptchaDmaProbe.exe`。
- 探针 `pages <新输出目录>` 用于重新发现图片；`capture <新输出目录> <已有扫描目录>` 要求会话与旧图片哈希仍一致，验证码刷新后不能拿旧扫描直接认定当前内容。

## 后续自动登录仍需补齐

1. 当前验证码控件与当前图片的稳定绑定及刷新生命周期，排除残留旧图。
2. 输入框和确认/刷新按钮的运行时定位、可见性与可用状态。
3. 验证失败、验证码过期、刷新后的状态识别，以及重试次数、等待与人工接管规则。
4. 启动器 → 账号登录 → 验证码 → 游戏登录界面的完整状态衔接。不能仅以 OCR 返回字符串或验证码按钮已点击认定登录成功。
5. 正式内存读取接入 provider 快照边界，隔离账号、设备和进程会话；本次独立诊断探针不是生产读取通道。

游戏内协议、选服、选角色与退出提示的数据继续见 [LOGIN_UI_DATA.md](LOGIN_UI_DATA.md)。本记录不改变“连续在线时间不固定，以实际强制重登弹窗为准”的规则。
