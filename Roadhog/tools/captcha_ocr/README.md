# lh.dll OCR 适配器

2026-10-23：用户要求以同事程序附带的 lh.dll 替换 ddddocr。适配器已实现；当前实际初始化返回 **-88 / license_required**，尚未完成真实图片识别验证。用户确认目前没有插件注册码。不得把接口接通或 mock 测试通过描述成验证码识别成功。

此目录是本地诊断工具，不包含生产自动登录服务。当前 `.tmp/sdo-login-test-20261023` 的 `continue_login.py`、`seek_six.py`、`recognize_fresh.py` 已改用该适配器；前两个在任何 DMA/UI 操作之前初始化 OCR，初始化失败即退出。没有恢复登录、刷新或提交测试。

## 架构与已验证边界

- Python 适配器通过标准输入/输出调用独立 x86 .NET Framework 主机。现有 Roadhog x64 主进程无需加载32位 DLL。
- 主机经 DllGetClassObject / IClassFactory 创建 ILhObject，按当前类型库确认的公共 COM ABI 调用。没有全局 COM 注册、二进制修改、目标进程 hook 或授权状态修改。
- `OcrInitModelFile` 空路径选择内置模型，CPU模式；也支持显式传入外部模型路径。
- `OcrDetectPic` 使用本机已固定的图片字节缓冲，不使用目标机内存指针。
- 纯文本 BSTR、详细 VARIANT/BSTR 分开保留并释放。只移除库输出的末尾 CRLF，不拼接多框文字、不补位或猜字。六位 ASCII 字母/数字仅表示可以尝试提交，不能证明正确。
- 详细结果内的 score 是文本框检测分数，不作为验证码字符正确率。
- 正常 `Reg/RegEx` 入口仅在调用方提供注册值时调用。注册值来自 `LH_OCR_REG_CODE`（可选 `LH_OCR_REG_IP`），经 stdin 传给主机，不写进命令行、配置、结果或异常文本。
- DLL SHA256 固定为 `e6b7283f97e75f5aeeb0394fcd9343ae9f2142f1f3ef9d3557b9400dac856c5d`。版本改变应重新核对 COM 合约，不沿用未知槽位。
- 初始化、识别或子进程异常会明确报错；不静默回退 ddddocr。超时会结束本适配器创建的子进程。

## 构建与离线调用

在仓库根目录运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Roadhog\tools\captcha_ocr\build_host.ps1
```

默认输出 `.tmp/lh-ocr-host/LhOcrHost.exe`。需要 Windows 已安装的 .NET Framework x86 编译器；不触发 Roadhog 构建或桌面脚本部署。C#文件保留 `.cs.txt` 后缀，避免进入 Roadhog 的默认源码编译集合。

复制 `config.example.json` 到 `.tmp/lh-ocr-host/config.json`，填写 DLL 绝对路径；主机路径可相对于配置文件。也可通过 `LH_OCR_CONFIG` 指定其他配置。禁止在配置中保存注册码。

```powershell
python Roadhog\tools\captcha_ocr\lh_ocr.py Roadhog\docs\evidence\captcha-ocr-2026-10-23\samples\sample-02.png
python -m unittest discover -s Roadhog\tools\captcha_ocr -p test_lh_ocr.py -v
```

接口示例：

```python
from lh_ocr import create_ocr
with create_ocr() as recognizer:
    result = recognizer.recognize(image_bytes)
    # result.raw_text / raw_details 保留原始返回。
    # result.text 是单行候选；result.eligible_to_submit 只检查格式。
```

## 当前验证结果

- x86 主机编译成功，无警告。
- 8项测试通过，覆盖原始结果保留、多行/非六位拒绝、插件错误、持久子进程、异常协议、超时清理和注册码配置保护。
- 使用当前原始 DLL 实际创建对象成功，模型初始化返回 -88。没有任何真实 OCR 推理结果，因此没有声称 WLP63M/P3TMSK/EMNQ58 识别正确。
- 还需要可用的正常插件注册方式。仅提供该 DLL 的路径不足以完成模型初始化。

静态接口证据位于本机 `.tmp/vm-aion-reference-20261023/COM_INTEGRATION_CONTRACT.md`、`LH_OCR_FINDINGS.md` 及对应局部指令核对记录。未将同事的 DLL、EXE 或任何授权值复制进仓库。

## 离线数据集与训练工具

这些入口只处理调用者指定的本地图片，不采集、刷新或提交验证码。Python 3.10 及以上可运行；图片检查和数据集测试需要 Pillow，本次验证环境为 Python 3.10.5 / Pillow 12.3.0。

- `import_filename_labels.py <数据目录> [--export <labels.jsonl>]`：按 `_metadata/sample-*.json` 的图片哈希核对人工改名，接受 `ANSWER_000001.png`、未标注的 `000001.png` 和 `SKIP_000001.png`。保留人工标签原文，不复用元数据中的旧答案。
- `verify_capture_dataset.py <数据目录> --expected <数量>`：检查图片可解码、120×60 尺寸、重复内容、标注对应关系及基本画面内容，将报告写到该目录的 `_metadata/validation.json`。
- `training_dataset.py`：冻结训练图片和清单；六位数字文件名作为未标注查询图片，六位字母数字标签用于训练，其他名称排除。重复图片或冻结过程中内容变化会报错。
- `train_parseq_trial.py`：使用指定的 PARSeq 源码与独立训练环境微调，输出原始预测、HTML/CSV 报告、最终检查点和环境记录。训练另需匹配的 PyTorch、torchvision、PyTorch Lightning、NumPy 和 PARSeq 依赖；首次加载预训练模型可能下载权重。

```powershell
python -m unittest discover -s Roadhog\tools\captcha_ocr -p "test_*.py" -v
python Roadhog\tools\captcha_ocr\train_parseq_trial.py --source <图片目录> --output <新输出目录> --parseq-repo <PARSeq源码目录> --device cpu
```

训练输出目录必须尚不存在。所有有效人工标签都用于训练，未标注查询图不参与训练或模型选择；训练集拟合结果不代表独立测试准确率。数据集、第三方源码、模型权重及训练输出不随本工具提交。
