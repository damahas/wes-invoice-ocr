# 模型目录

PaddleOCR ONNX 模型**随仓库提交**（Git LFS 管理），clone 即用、无需下载。

## 档位与选型

`models/` 按「家族 / 档位」归档，每档为**自包含完整模型集**（含 `det.onnx` / `rec.onnx` / `cls.onnx`，
v6 档另附 `ppocrv6_dict.txt` 兜底词典）。引擎按固定文件名加载，**指向哪档即用哪档**：

```
models/
├─ ppocrv6/                  PP-OCRv6 家族（RapidOCR v3.9.0 官方 ONNX）
│  ├─ small/                 轻量档（默认）：det medium(59.2M) + rec small
│  └─ medium/                高精度档：det medium + rec medium(73.1M)
└─ ppocrv4/                  PP-OCRv4 家族（RapidOCR 官方 ONNX，官方分 mobile/server）
   └─ mobile/                快速档：det mobile(4.5M) + rec mobile(10.4M)
```

实测（1080×704 通行费发票，4 核 CPU，CPU EP）：

| 档位 | det | rec | 端到端 | 体积 | 结论 |
|------|-----|-----|--------|------|------|
| `mobile` | PP-OCRv4 det mobile | PP-OCRv4 rec mobile | 约 3.6 s/张 | 约 15 MB | 税号字符最准（读出 `JLK6N`）+ 最快，适合简单版式 |
| `small`（默认） | det medium | rec small | 约 9 s/张 | 约 80 MB | 通用；名称准但税号可能丢字符 |
| `medium` | det medium | rec medium | 约 33 s/张 | 约 133 MB | 全字段最准 |

> **怎么选（结论）**：全字段准确用 `medium`；复杂版式通用用 `small`；追求速度且票面简单（通行费/普票）用 `mobile`。
> **坑**：`mobile` 的 det 较弱（仅 4.5 MB），复杂版式可能漏检整行导致字段错位——**不报错、看起来正常但值是错的**，比乱码更危险，故默认档取 `small`。若票面版式与样本差异大，先跑冒烟实测通过率再定档。
> `mobile` 的 rec 会把 `1` 读成 `I`、混入 `/` 等噪声——已由 `VatParser.CleanTaxNo`（噪声分隔容忍 + GB32100 `I→1`/`O→0` 纠正）兜底，各档通用。
> 表中为稳态耗时，首张含模型加载约 +10 s；GPU（CUDA）下各档均秒级、差距缩小。

## 使用

```csharp
using var mobile  = new PaddleOcrEngine("models/ppocrv4/mobile", cfg);  // 快速（PP-OCRv4）
using var fast    = new PaddleOcrEngine("models/ppocrv6/small", cfg);   // 轻量（默认）
using var precise = new PaddleOcrEngine("models/ppocrv6/medium", cfg);  // 高精度
```

- `det.onnx` / `rec.onnx` 缺失抛 `OcrException { Kind = EngineNotConfigured }`；`cls.onnx` 缺失则跳过方向分类
- 词典优先取 `rec.onnx` 内嵌的 character metadata，取不到才回退 `ppocrv6_dict.txt`（mobile 档内嵌，无 dict 亦可）
- 冒烟缺省模型目录时，构建自动把 `models/ppocrv6/small/` 复制到运行目录 `models/`（见 `Wes.Invoice.Test.csproj`）

## 替换 / 升级

保持文件名不变直接覆盖即可，无需改代码。官方来源：ModelScope `RapidAI/RapidOCR`（v6 为 v3.9.0 分支，v4 为 master 分支）：

```powershell
# PP-OCRv6（small / medium 档）
curl.exe -L -o small/det.onnx  "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.0/onnx/PP-OCRv6/det/PP-OCRv6_det_medium.onnx"
curl.exe -L -o small/rec.onnx  "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.0/onnx/PP-OCRv6/rec/PP-OCRv6_rec_small.onnx"
curl.exe -L -o medium/rec.onnx "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.0/onnx/PP-OCRv6/rec/PP-OCRv6_rec_medium.onnx"
# PP-OCRv4 mobile（ppocrv4/mobile 档）
curl.exe -L -o ppocrv4/mobile/det.onnx "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/master/onnx/PP-OCRv4/det/ch_PP-OCRv4_det_mobile.onnx"
curl.exe -L -o ppocrv4/mobile/rec.onnx "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/master/onnx/PP-OCRv4/rec/ch_PP-OCRv4_rec_mobile.onnx"
curl.exe -L -o ppocrv4/mobile/cls.onnx "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/master/onnx/PP-OCRv4/cls/ch_ppocr_mobile_v2.0_cls_mobile.onnx"
```

`cls.onnx` / 词典极小且少迭代，需更新时从 RapidOCR 官方 `default_models.yaml` 取。

换模型后跑冒烟对照：`dotnet run --project Wes.Invoice.Test -- smoke <图片路径> [模型目录]`。

注意：`PaddleOcrConfig.DetLimit`（默认 1280）与 `RecMaxW`（默认 640）按当前模型的输入上限设定，
换成上限不同的模型时需同步调整。
