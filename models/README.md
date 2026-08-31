# 模型目录

PaddleOCR 的 ONNX 模型直接放在本目录下。
**本目录只提交说明文件，模型与词典不入库**（见根目录 `.gitignore`）。

## 目录约定

```
models/
├─ det.onnx             # 必需：文本检测（PP-OCRv4 det，动态 H/W，长边上限 960）
├─ rec.onnx             # 必需：文本识别（PP-OCRv6 rec，动态宽，上限 640）
├─ cls.onnx             # 可选：方向分类
└─ ppocrv6_dict.txt     # 条件必需：rec.onnx 未内嵌字符集时才需要
```

`PaddleOcrEngine` 的硬性要求：`det.onnx` 与 `rec.onnx` 必须存在，否则抛 `OcrErrorKind.EngineNotConfigured`。
词典优先取 `rec.onnx` 内嵌的字符集，取不到才回退 `ppocrv6_dict.txt`。

## 如何获取

从 PaddleOCR 官方下载 inference model（`*.pdmodel` + `*.pdiparams`），用
[paddle2onnx](https://github.com/PaddlePaddle/Paddle2ONNX) 转换：

```bash
paddle2onnx --model_dir ./ch_PP-OCRv4_det_infer \
            --model_filename inference.pdmodel \
            --params_filename inference.pdiparams \
            --save_file det.onnx --opset_version 11
```

rec、cls 同理。具体模型版本以 PaddleOCR 官方 release 为准。

## 使用

模型路径传相对路径即可，**无硬编码绝对路径**，任何人 clone 后把模型放到本目录就能跑：

```bash
dotnet run --project Wes.Invoice.Test -- smoke models
```
