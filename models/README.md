# 模型目录

PaddleOCR 的 ONNX 模型**已随仓库提交**，clone 后无需额外下载即可运行。

| 文件 | 体积 | 必需 | 说明 |
|------|------|------|------|
| `det.onnx` | 4.53 MB | 必需 | 文本检测（PP-OCRv4 det，动态 H/W，长边上限 960） |
| `rec.onnx` | 20.25 MB | 必需 | 文本识别（PP-OCRv6 rec，动态宽，上限 640；内嵌字符集） |
| `cls.onnx` | 0.56 MB | 可选 | 方向分类（存在时自动启用） |
| `ppocrv6_dict.txt` | 0.07 MB | 条件必需 | 中文词典；`rec.onnx` 内嵌字符集时非必需，此处作为兜底保留 |

合计约 **25.4 MB**。

## 引擎的加载规则

`PaddleOcrEngine(modelDir)` 按固定文件名在本目录中查找（`PaddleOcrEngine.cs`）：

- `det.onnx` 与 `rec.onnx` **必须存在**，否则抛 `OcrException { Kind = EngineNotConfigured }`
- `cls.onnx` 存在即启用方向分类，不存在则跳过（不报错）
- 词典优先取 `rec.onnx` 内嵌的 `character` metadata，取不到才回退 `ppocrv6_dict.txt`；两者都没有同样抛 `EngineNotConfigured`

## 使用

模型路径传相对路径即可，**无硬编码绝对路径**：

```bash
# 冒烟（模型目录省略时默认取运行目录下 models/，构建时自动复制）
dotnet run --project Wes.Invoice.Test -- smoke
```

代码中：

```csharp
using var engine = new PaddleOcrEngine("models", new PaddleOcrConfig { Ep = EpPreference.Cpu });
```

## 替换 / 升级模型

保持文件名不变直接覆盖即可，无需改代码。从 PaddleOCR 官方 inference model（`*.pdmodel` + `*.pdiparams`）用
[paddle2onnx](https://github.com/PaddlePaddle/Paddle2ONNX) 转换：

```bash
paddle2onnx --model_dir ./ch_PP-OCRv4_det_infer \
            --model_filename inference.pdmodel \
            --params_filename inference.pdiparams \
            --save_file det.onnx --opset_version 11
```

rec、cls 同理。换模型后建议跑一遍冒烟对照识别效果：

```bash
dotnet run --project Wes.Invoice.Test -- smoke models <图片路径>
```

注意 `PaddleOcrConfig.RecMaxW`（默认 320）是按 rec 模型的输入宽度上限设的，换成宽度上限不同的 rec 模型时需同步调整。

## 体积与仓库

模型已入库（约 25 MB），换来的是**开箱即用**：无需注册账号、无需外网、CI 与离线环境都能直接跑。代价是 clone 体积变大、模型迭代会体现在 git 历史中。

若日后模型体积增长到影响 clone 体验，可改用 Git LFS 或改回「不入库 + 首次运行下载」的方案。
