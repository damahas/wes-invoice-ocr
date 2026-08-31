# Wes.Invoice.Ocr

![NuGet](https://img.shields.io/nuget/v/Wes.Invoice.Ocr)
![.NET Standard](https://img.shields.io/badge/netstandard-2.0-blueviolet)
![License](https://img.shields.io/badge/license-MIT-green)
![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen)

基于 **PaddleOCR**（PP-OCRv4 det + PP-OCRv6 rec）的发票 / 票据 OCR 与结构化解析库，以 **netstandard2.0** 类库形式发布（兼容 .NET Framework 4.6.1+、.NET Core 2.0+、.NET 5+ 全系）。覆盖**增值税发票、火车票、航空运输电子客票行程单**三类票据，输出结构化字段，并内置二维码交叉校验。

## 特性

- **纯标准库算法**：CTC 贪心解码、DB 后处理（flood fill / NMS / min-area-rect）、图像三角形滤波缩放、双线性旋转裁剪、几何校正等全部使用 BCL 实现，无额外依赖。
- **推理仅必要三方包**：`Microsoft.ML.OnnxRuntime.GPU`（推理，含 CUDA EP；无 N 卡自动回退 CPU）、`SixLabors.ImageSharp` **2.1.x**（图像解码/预处理）、`PdfPig`（PDF 文本提取）、`ZXing.Net`（二维码解码）。
- **二维码交叉校验**：与 OCR 主流程并行解码二维码（ROI 优先 + 全图降级），与发票号码/日期/金额交叉比对，对响应时间零影响；支持数电票查验 URL 参数解析。
- **批量识别**：rec 阶段支持按文本行宽度分桶并行、多行一次推理；实测批量反而更慢 ~19%，故**默认关闭**（`INVOICE_OCR_REC_BATCH=1` 可开启对照）。
- **可扩展解析器**：基于 `IInvoiceParser` 契约，新票据类型只需新增一个解析器并在 `ParserRegistry` 注册。
- **解耦门面**：`InvoiceOcrService` 统一流水线（图片 / PDF / 纯文本 → 识别 → 类型判定 → 解析）。

## 安装

```bash
dotnet add package Wes.Invoice.Ocr
```

或通过 NuGet Package Manager：

```powershell
Install-Package Wes.Invoice.Ocr
```

> **模型不打进 NuGet 包**：`det.onnx` / `rec.onnx` / `cls.onnx` 及词典体积约 25MB，且随模型迭代变化，请按下方[模型](#模型)章节自行准备目录，构造 `PaddleOcrEngine` 时传入。

## 快速开始

### 引用与调用

```csharp
using Wes.Invoice.Ocr;
using Wes.Invoice.Ocr.Paddle;
using Wes.Invoice.Ocr.Qr;

// 1. 构造 OCR 引擎（指向包含 det.onnx / rec.onnx / cls.onnx / 字典 的模型目录）
using var engine = new PaddleOcrEngine(
    @"models",
    new PaddleOcrConfig { Ep = EpPreference.Cpu });

// 2. 构造门面（可选传入二维码解码器，启用交叉校验）
using var svc = new InvoiceOcrService(engine, qrDecoder: new ZxingQrDecoder());

// 3. 识别图片字节
var invoice = svc.RecognizeImageBytes(File.ReadAllBytes("invoice.png"));
Console.WriteLine(invoice.Kind.ToWireString());   // "vat_invoice"
foreach (var f in invoice.Fields)
    Console.WriteLine($"{f.Label} = {f.Value}");

// 4. 二维码校验结果（图片输入时自动并行校验）
var v = invoice.Verification;
Console.WriteLine(v.Status);      // NotScanned / Verified / Mismatch / DecodeFailed
foreach (var c in v.Conflicts)
    Console.WriteLine($"冲突 {c.Key}: 二维码[{c.QrValue}] vs OCR[{c.OcrValue}]");
```

### 直接解析文本

若已有 OCR / PDF 文本，可跳过引擎直接解析：

```csharp
var invoice = svc.ParseText(rawText);   // 返回 Invoice { Kind, Fields, RawText }
```

### PDF 识别

```csharp
var invoice = svc.RecognizePdfBytes(File.ReadAllBytes("invoice.pdf"));
// 电子发票/行程单通常含文本层，直接提取；扫描件抛 OcrErrorKind.RasterNotAvailable
```

## 模型

从 PaddleOCR 官方导出 `det.onnx` / `rec.onnx` / `cls.onnx`（可选）及字符字典，放入同一目录，例如：

```
models/
├─ det.onnx               # PP-OCRv4 det（动态 H/W，长边上限 960）
├─ rec.onnx               # PP-OCRv6 rec（动态宽，上限 640）
├─ cls.onnx               # 可选，方向分类
└─ ppocrv6_dict.txt       # 中文词典（rec.onnx 内嵌字符集时非必需）
```

`PaddleOcrConfig` 关键配置：

| 配置 | 默认值 | 说明 |
|------|--------|------|
| `DetLimit` | 960 | det 输入长边上限 |
| `RecMaxW` | 320 | rec 单段最大宽度（超长行自动滑窗切分） |
| `RecThreads` | 4 | rec 会话池线程数（分桶并行，1~16） |
| `Ep` | `Auto` | 执行提供方：`Cpu` / `DirectML` / `Cuda` / `Auto`（Auto 仅尝试 N 卡 CUDA，失败回退 CPU，不考虑核显） |

## 环境要求

- **运行时**：netstandard2.0，兼容 .NET Framework 4.6.1+、.NET Core 2.0+、.NET 5/6/7/8/9/10、Mono、Unity 2018.1+（消费方编译目标任意，运行时依赖由目标框架决定）
- **构建**：.NET SDK 8.0+（建议最新 LTS）
- **平台**：Windows / Linux / macOS（OnnxRuntime 跨平台）
- **GPU（可选）**：NVIDIA 独显 + CUDA 13 / cuDNN 9，仅 CUDA EP 需要，无则自动回退 CPU
  - **Linux GPU 部署**：安装 NVIDIA 驱动 + CUDA 13 Toolkit + cuDNN 9，并确保 `ldconfig` 或
    `LD_LIBRARY_PATH` 能找到 `libcublasLt.so.13` / `libcudnn.so.9`；macOS 无 CUDA EP，自动走 CPU
  - `Ep=Auto`（或 `Cuda`）在无 CUDA 环境会先探测 `cublasLt64_13.dll` / `cudnn64_9.dll`，
    探测不到则跳过 CUDA EP，stderr 输出一条 `未检测到 CUDA 13 运行库...回退 CPU` 提示，自动使用 CPU EP

## 环境变量（调试 / 对照）

| 变量 | 默认 | 说明 |
|------|------|------|
| `INVOICE_OCR_REC_BATCH` | `0` | `1` 强制批量 rec（实测比逐行慢 ~19%，仅用于对照排查） |
| `INVOICE_OCR_REC_THREADS` | `4` | rec 会话池线程数（1~16，覆盖 `PaddleOcrConfig.RecThreads`） |
| `INVOICE_OCR_ROI` | `0` | `1` 开启 ROI 排序裁剪（默认关闭，避免静默错字段；调试定位阶段问题用） |
| `INVOICE_OCR_SMOKE_IMAGE` | - | 冒烟测试图片路径（见[测试](#测试)） |

示例：

```powershell
$env:INVOICE_OCR_REC_BATCH = "1"
dotnet run --project Wes.Invoice.Test -- smoke models test_invoice.png
```

## 测试

```bash
# 解析器 / 类型判定单元测试（零依赖，退出码 0/1 可入 CI）
dotnet run --project Wes.Invoice.Test

# 端到端冒烟（--debug 打印 det 的 shape 与概率统计）
dotnet run --project Wes.Invoice.Test -- smoke <模型目录> [图片路径] [--debug]

# 全量构建
dotnet build Wes.Invoice.slnx -c Release

# 打包 NuGet 包（输出到 ../artifacts）
dotnet pack Wes.Invoice.Ocr -c Release -o ..\artifacts

# 依赖漏洞扫描（CI 建议加，发现漏洞时退出码非 0）
dotnet list package --vulnerable --include-transitive
```

冒烟图片路径按以下优先级解析：

1. 命令行参数 —— `smoke <模型目录> <图片路径>`
2. 环境变量 `INVOICE_OCR_SMOKE_IMAGE`
3. 运行目录下的 `Assets/test_invoice.png`

**不含任何硬编码的绝对路径**，任何人 clone 后都能直接跑。`Wes.Invoice.Test/Assets/` 下的图片默认被 `.gitignore` 排除（真实票据含敏感信息），详见 `Wes.Invoice.Test/Assets/README.md`。

## 目录结构

```
wes-invoice-ocr/
├─ Wes.Invoice.slnx                # 解决方案（类库 + 测试）
├─ Wes.Invoice.Ocr/                # 类库项目（netstandard2.0，可打包 NuGet）
│  ├─ Wes.Invoice.Ocr.csproj
│  ├─ Abstractions/                # 契约层：InvoiceKind / Invoice / FieldValue / OcrBox / IOcrEngine / 错误体系
│  ├─ Algorithms/                  # 纯 BCL 算法：Decode / Geometry / DetPost / ImageOps / Preprocess
│  ├─ Detect/                      # InvoiceKindDetector（启发式类型判定）
│  ├─ Imaging/                     # ImageSharpImageDecoder（灰度解码）
│  ├─ Pdf/                         # PdfTextExtractor（PdfPig 文本提取）
│  ├─ Parsers/                     # VatParser / TrainParser / FlightParser / ParserRegistry / ParserHelpers
│  ├─ Paddle/                      # PaddleOcrConfig / OnnxSessionFactory / PaddleOcrEngine
│  ├─ Qr/                          # 二维码：ZxingQrDecoder / QrDataParser / VerificationService
│  └─ OcrService.cs                # 门面流水线 InvoiceOcrService（含并行 QR 校验分支）
└─ Wes.Invoice.Test/               # 测试项目（单测 + 冒烟，`-- smoke` 分流）
   ├─ Wes.Invoice.Test.csproj
   ├─ Program.cs                   # 单测入口（零依赖，退出码 0/1 可入 CI）
   └─ Smoke.cs                     # 端到端冒烟（支持 --debug 诊断 det 输出）
```

## 票据字段

| 类型 | Kind（wire） | 主要字段 key |
|------|--------------|--------------|
| 增值税发票 | `vat_invoice` | `invoice_code` `invoice_no` `invoice_date` `buyer_name` `seller_name` `buyer_tax_no` `seller_tax_no` `total_amount` `total_tax` `total_amount_with_tax` |
| 火车票 | `train_ticket` | `train_no` `from_station` `to_station` `travel_date` `price` `passenger_name` `passenger_id_no` `seat_class` |
| 行程单 | `flight_itinerary` | `flight_no` `departure` `arrival` `flight_date` `passenger_name` `ticket_no` `price` `fuel_surcharge` `airport_fee` |

## 架构要点

```
图像/PDF
   │
   ▼
PaddleOcrEngine  ── det ─▶ 检测框 ─▶ ROI 排序 ─▶ 旋转裁剪 ─▶ cls 方向
   │                                                            │
   │                                                            ▼
   │                                                      rec（批量/逐行）──▶ CTC 解码
   ▼
InvoiceOcrService ── 类型判定（InvoiceKindDetector）──▶ 解析器（ParserRegistry）──▶ Invoice
   │
   └─ 并行 QR 分支（ZxingQrDecoder：ROI 右上/左上 → 全图降级）
        └─▶ QrDataParser（查验 URL / 结构性数字）──▶ VerificationService（交叉比对）──▶ Invoice.Verification
```

- **Abstractions** 定义全部契约与 DTO，不依赖任何推理/算法实现，便于替换为其它引擎或跨语言平移。
- **Algorithms** 与 **Paddle** 严格分层：算法层零三方依赖，Paddle 层负责 OnnxRuntime 会话管理与数据流编排。
- 新增票据类型：实现 `IInvoiceParser` → 在 `ParserRegistry.Default()` 注册 → 在 `InvoiceKindDetector` 补充判定锚点（如需）。

## 依赖版本约束

| 包 | 锁定版本 | 原因 |
|----|---------|------|
| `Microsoft.ML.OnnxRuntime.GPU` | 1.29.0 | 含 CUDA EP，同一包同时覆盖 GPU/CPU 场景（无 N 卡自动回退 CPU），避免运行时换包；体积较 CPU 包大 ~150MB |
| `SixLabors.ImageSharp` | **2.1.x** | v3+ 改为 Six Labors Split License，**闭源商用且营收 > 100 万美元需付费**；2.x 保持 Apache-2.0 且持续接收安全更新。此外 3.1.5 存在 high severity 漏洞，勿升。 |

ImageSharp 在本项目仅用于图片解码（见 `Imaging/ImageSharpImageDecoder.cs`），预处理与后处理均为纯 BCL 实现。

## 贡献

欢迎提交 Issue 与 PR：

- Bug / 新票据类型支持 → 先开 Issue 讨论再动手
- 代码风格与现有保持一致；新增解析器请附带单测
- 涉及依赖升级请参考[依赖版本约束](#依赖版本约束)，勿盲目升级 ImageSharp / OnnxRuntime

## License

[MIT](LICENSE)

本项目依赖的第三方组件遵循各自 License：`Microsoft.ML.OnnxRuntime.GPU`（MIT）、`SixLabors.ImageSharp` 2.x（Apache-2.0）、`PdfPig`（Apache-2.0）、`ZXing.Net`（MIT）、`System.Memory`（MIT）。
