# Wes.Invoice.Test

无第三方测试框架的轻量测试/冒烟项目：单测 + 端到端冒烟共用 `Program.cs` 入口，
通过退出码（0 通过 / 1 失败）可直接接入 CI。

## 运行方式

### 1. 单测（纯逻辑，不依赖模型/图片）

```bash
dotnet run --project Wes.Invoice.Test
```

覆盖：发票解析器 8 个、引擎配置 2 个（`ModelDir` 回退/缺失）、二维码校验 6 个，共 16 个用例。

### 2. 冒烟（端到端，真实模型 + 图片）

```bash
dotnet run --project Wes.Invoice.Test -- smoke [模型目录] [图片路径] [--debug]
```

三个参数**均可省略**：

| 参数 | 省略时默认 |
|------|-----------|
| 模型目录 | 运行目录下 `models/`（构建时自动从仓库根 `models/` 复制）|
| 图片路径 | 运行目录下 `Assets/test_invoice.png`（构建时自动复制）|
| `--debug` | 不打印诊断（见下）|

示例：

```bash
# 最简：全部用默认值
dotnet run --project Wes.Invoice.Test -- smoke

# 自定义模型目录
dotnet run --project Wes.Invoice.Test -- smoke D:/models/ppocrv5

# 自定义模型 + 自定义图片
dotnet run --project Wes.Invoice.Test -- smoke models D:/samples/another_invoice.jpg

# 诊断模式：打印 det/rec 输入输出 shape、概率统计、分段计时、批量 vs 逐行对比
dotnet run --project Wes.Invoice.Test -- smoke --debug
```

### 3. 直接运行已构建产物

```bash
dotnet build Wes.Invoice.slnx -c Release
.\Wes.Invoice.Test\bin\Release\net10.0\Wes.Invoice.Test.exe smoke
```

## 图片读取优先级

1. 命令行第二个位置参数 —— `smoke <模型目录> <图片路径>`
2. 环境变量 `INVOICE_OCR_SMOKE_IMAGE`
3. 运行目录下的 `Assets/test_invoice.png`

## 常用环境变量（完整列表见根目录 README）

| 变量 | 默认 | 说明 |
|------|------|------|
| `INVOICE_OCR_SMOKE_IMAGE` | 无 | 冒烟图片路径（优先级高于 Assets 默认图）|
| `INVOICE_OCR_ROI` | `0` | `1` 开启 ROI 排序裁剪 |
| `INVOICE_OCR_REC_BATCH` | `0` | `1` 强制批量 rec（对照用，实测更慢）|

```powershell
$env:INVOICE_OCR_SMOKE_IMAGE = "D:/samples/another_invoice.jpg"
dotnet run --project Wes.Invoice.Test -- smoke
```

## IDE 启动配置（launchSettings.json）

| Profile | 作用 |
|---------|------|
| `UnitTest` | 单测 |
| `Smoke` | 冒烟（默认参数）|
| `Smoke-Debug` | 冒烟 + 诊断 |
| `Smoke-NoBatch` | 强制逐行 rec |
| `Smoke-NoRoi` | 关闭 ROI |

## 目录结构

```
Wes.Invoice.Test/
├─ Program.cs          # 入口：参数分流（smoke / 单测）+ 测试清单
├─ TestCases.cs        # 16 个单测用例
├─ Harness.cs          # 测试辅助（NewService/Field/Equal/MakeVatInvoice/NoopEngine）
├─ Smoke.cs            # 端到端冒烟 + --debug 诊断工具
├─ Assets/             # 测试图片（构建复制到输出目录；不入库，见下）
└─ Properties/launchSettings.json
```

## 敏感数据注意

**不要提交含真实信息的票据图片。** 发票含企业税号、银行账号、人名、车牌等敏感字段，
`Assets/` 下的图片默认已被 `.gitignore` 排除，不会入库；模型文件（仓库根 `models/`）同样不入库。

如需团队共享测试集，请先脱敏（涂改或替换为虚构数据）。
