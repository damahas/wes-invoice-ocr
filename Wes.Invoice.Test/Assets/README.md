# Assets（冒烟测试图片）

本目录存放冒烟测试使用的**真实票据图片**（`test_invoice.png` 等），**默认不入库**（见根目录 `.gitignore`）：

```gitignore
Wes.Invoice.Test/Assets/*.png
Wes.Invoice.Test/Assets/*.jpg
# ...（bmp / webp / tif / pdf 同理）
```

## 为什么不入库

真实票据图片包含发票代码/号码、税号、车牌、开票人姓名等敏感信息，不宜随开源仓库分发。请勿将真实票据提交到仓库，也不要把它复制进 NuGet 包。

## clone 后如何自备图片

仓库不包含任何真实票据图片。本地开发/验证时：

1. 将你的测试图片放到本目录，命名为 `test_invoice.png`
2. 运行冒烟测试：

```bash
dotnet run --project Wes.Invoice.Test -- smoke models
```

图片路径解析优先级：

1. 命令行参数 —— `smoke <模型目录> <图片路径>`
2. 环境变量 `INVOICE_OCR_SMOKE_IMAGE`
3. 运行目录下 `Assets/test_invoice.png`

如果你需要用自己合成的虚构票据图片，可在本地生成后按上述方式放置；**不要**用 git 强制加入（`git add -f`）的方式绕过 `.gitignore`。

## 构建行为

`Wes.Invoice.Test.csproj` 会把本目录下的图片复制到输出目录（`bin/Release/net10.0/Assets/`），仅当图片存在时生效；仓库中没有图片时构建不受影响。
