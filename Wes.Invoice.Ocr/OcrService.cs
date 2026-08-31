using Wes.Invoice.Ocr.Abstractions;
using Wes.Invoice.Ocr.Detect;
using Wes.Invoice.Ocr.Parsers;
using Wes.Invoice.Ocr.Pdf;
using Wes.Invoice.Ocr.Qr;

// 注意：命名空间 Wes.Invoice.Ocr 使 Wes.Invoice 成为命名空间段，
// 简单名称 `Invoice` 在成员查找时会命中该命名空间（而非 Abstractions 的类型）。
// 因此这里用块式命名空间，并在命名空间声明空间内声明 using 别名来解析到类型。
namespace Wes.Invoice.Ocr
{
    using Invoice = Wes.Invoice.Ocr.Abstractions.Invoice;

    /// <summary>
    /// 门面流水线：输入图片/PDF → 引擎识别 → 类型判定 → 解析器 → 结构化结果。
    /// 可选并行分支：二维码解码 + 本地交叉校验（对总响应时间零影响）。
    /// </summary>
    public sealed class InvoiceOcrService
    {
        private readonly IOcrEngine _engine;
        private readonly IReadOnlyList<IInvoiceParser> _parsers;
        private readonly IQrDecoder? _qrDecoder;

        /// <summary>
        /// 使用指定 OCR 引擎（如 PaddleOcrEngine）。
        /// 传入 <paramref name="qrDecoder"/> 后，有图像输入时会并行解码二维码并与 OCR 字段交叉校验。
        /// </summary>
        public InvoiceOcrService(
            IOcrEngine engine,
            IEnumerable<IInvoiceParser>? parsers = null,
            IQrDecoder? qrDecoder = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _parsers = (parsers ?? ParserRegistry.Default()).ToList();
            _qrDecoder = qrDecoder;
        }

        public string EngineName => _engine.Name;

        /// <summary>识别图片（jpg/png/bmp/webp 等），返回结构化票据。</summary>
        public Invoice RecognizeImageBytes(byte[] data)
        {
            if (data.Length == 0)
                throw OcrException.EmptyInput();
            var img = Imaging.ImageSharpImageDecoder.DecodeGray(data);
            return RecognizeImage(img);
        }

        /// <summary>
        /// 识别灰度图像，返回结构化票据。
        /// 若配置了二维码解码器，解码与 OCR 引擎并行执行（解码时间被 OCR 耗时覆盖，不增加响应时间）。
        /// </summary>
        public Invoice RecognizeImage(GrayImage image)
        {
            // 并行 QR 分支：解码失败返回 null，绝不影响主流程
            var qrTask = _qrDecoder is null
                ? null
                : Task.Run(() => _qrDecoder.TryDecode(image));

            var boxes = _engine.RecognizeImage(image);
            var text = string.Join("\n",
                boxes.Select(b => b.Text.Trim()).Where(s => s.Length > 0));
            var invoice = ParseText(text);

            if (qrTask is not null)
            {
                QrData? qr = null;
                try
                {
                    qr = qrTask.Result; // TryDecode 保证不抛；此处再兜底一次
                }
                catch
                {
                    qr = null;
                }
                invoice = invoice with { Verification = VerificationService.Verify(invoice, qr) };
            }
            return invoice;
        }

        /// <summary>
        /// 识别 PDF：先尝试文本提取（电子发票/行程单通常直接可读）；
        /// 扫描件抛 <see cref="OcrErrorKind.RasterNotAvailable"/>，待接入渲染后端后走图片 OCR。
        /// </summary>
        public Invoice RecognizePdfBytes(byte[] data)
        {
            if (data.Length == 0)
                throw OcrException.EmptyInput();
            var text = PdfTextExtractor.ExtractText(data);
            if (text.Trim().Length < 20)
                throw OcrException.RasterNotAvailable();
            return ParseText(text);
        }

        /// <summary>直接解析已有文本（调试 / 单元测试 / 复用提取结果）。无图像输入，二维码校验为 NotScanned。</summary>
        public Invoice ParseText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw OcrException.EmptyInput();
            var kind = InvoiceKindDetector.DetectKind(text);
            var fields = _parsers.FirstOrDefault(p => p.Kind == kind)?.Parse(text)
                         ?? Array.Empty<FieldValue>();
            return new Invoice(kind, fields, text);
        }
    }
}
