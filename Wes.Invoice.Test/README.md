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
dotnet run --project Wes.Invoice.Test -- smoke [图片路径] [模型目录] [--debug]
```

三个参数**均可省略**：

| 参数 | 省略时默认 |
|------|-----------|
| 图片路径 | 运行目录下 `Assets/test_invoice.png`（构建时自动复制）|
| 模型目录 | 运行目录下 `models/`（构建时自动从仓库根 `models/` 复制）|
| `--debug` | 不打印诊断（见下）|

示例：

```bash
# 最简：全部用默认值
dotnet run --project Wes.Invoice.Test -- smoke

# 自定义图片（模型用默认）
dotnet run --project Wes.Invoice.Test -- smoke D:/samples/another_invoice.jpg

# 自定义图片 + 自定义模型
dotnet run --project Wes.Invoice.Test -- smoke D:/samples/another_invoice.jpg D:/models/ppocrv5

# 诊断模式：打印 det/rec 输入输出 shape、概率统计、分段计时、批量 vs 逐行对比
dotnet run --project Wes.Invoice.Test -- smoke --debug

# 调参模式：提高分辨率 + 加大 rec 宽度 + 降低框阈值（适合长号码/表格密集发票）
dotnet run --project Wes.Invoice.Test -- smoke invoice.png --det-limit 1280 --rec-max-w 640 --box-thresh 0.45 --db-thresh 0.25
```

可用选项（不区分顺序，位置参数始终在前）：

| 选项 | 说明 | 默认值 |
|------|------|--------|
| `--debug` | 诊断模式（det shape / 概率统计 / 分段计时） | 关闭 |
| `--det-limit N` | det 输入长边上限 | `1280` |
| `--db-thresh F` | DB 二值化阈值（0~1） | `0.3` |
| `--box-thresh F` | 框最小像素占比阈值（0~1） | `0.5` |
| `--rec-max-w N` | rec 输入最大宽 | `640` |
| `--ep cpu\|cuda\|directml\|auto` | 执行提供方 | `cuda` |

### 3. 直接运行已构建产物

```bash
dotnet build Wes.Invoice.slnx -c Release
.\Wes.Invoice.Test\bin\Release\net10.0\Wes.Invoice.Test.exe smoke
```

## 冒烟图片路径

优先级：命令行第一个位置参数 &gt; 运行目录下 `Assets/test_invoice.png`。

```bash
# 显式指定图片（模型目录为第二个位置参数，可省略）
dotnet run --project Wes.Invoice.Test -- smoke D:/samples/another_invoice.jpg

# 或把图片放到 Wes.Invoice.Test/Assets/test_invoice.png，直接跑
dotnet run --project Wes.Invoice.Test -- smoke
```

全项目行为均由显式入参决定：类库看 `PaddleOcrConfig`，测试看命令行参数。

## IDE 启动配置（launchSettings.json）

| Profile | 作用 |
|---------|------|
| `UnitTest` | 单测 |
| `Smoke` | 冒烟（默认参数）|
| `Smoke-Debug` | 冒烟 + 诊断（含逐行/批量 rec 耗时对比）|

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
`Assets/` 下的图片默认已被 `.gitignore` 排除，不会入库。

模型文件（仓库根 `models/`）**已入库**，不含敏感信息，clone 后即可直接跑冒烟。

如需团队共享测试集，请先脱敏（涂改或替换为虚构数据）。
