using Wes.Invoice.Ocr;
using Wes.Invoice.Ocr.Abstractions;

// 注意：命名空间 Wes.Invoice.Ocr 使 Wes.Invoice 成为命名空间段，
// 简单名称 `Invoice` 在成员查找时会命中该命名空间（而非 Abstractions 的类型）导致 CS0118。
// 因此这里用块式命名空间，并在命名空间声明空间内声明 using 别名来解析到类型。
namespace Wes.Invoice.Test
{
    using Invoice = Wes.Invoice.Ocr.Abstractions.Invoice;

    /// <summary>
    /// 单测基础设施：构造服务、按 key 取字段、极简断言、构造示例票据。
    /// </summary>
    internal static class Harness
    {
        /// <summary>构造挂载空引擎的服务（单测只走 ParseText，不触碰真实模型）。</summary>
        public static InvoiceOcrService NewService() => new(NoopEngine.Instance);

        /// <summary>按 key 取字段值的便捷函数，缺失返回空串。</summary>
        public static Func<string, string> Field(Invoice inv) =>
            key => inv.Fields.FirstOrDefault(f => f.Key == key)?.Value ?? "";

        /// <summary>极简断言：不等则抛异常，由 TestRunner 捕获统计。</summary>
        public static void Equal<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception($"{what}: 期望 [{expected}] 实际 [{actual}]");
        }

        /// <summary>断言抛指定异常；类型不符或未抛都算失败。</summary>
        public static void Throws<T>(Action action, string what) where T : Exception
        {
            try
            {
                action();
            }
            catch (T)
            {
                return;
            }
            catch (Exception ex)
            {
                throw new Exception($"{what}: 期望抛 {typeof(T).Name}，实际抛 {ex.GetType().Name}", ex);
            }
            throw new Exception($"{what}: 期望抛 {typeof(T).Name}，实际未抛");
        }

        /// <summary>构造最小增值税票据（3 个字段，置信度 1f），供二维码交叉校验测试使用。</summary>
        public static Invoice MakeVatInvoice(string no, string date, string amount) => new(
            InvoiceKind.VatInvoice,
            new FieldValue[]
            {
                new("invoice_no", "发票号码", no, 1f),
                new("invoice_date", "开票日期", date, 1f),
                new("total_amount_with_tax", "价税合计", amount, 1f),
            },
            "发票号码：" + no);
    }

    /// <summary>测试用空引擎：不加载模型，仅供解析器/流水线单测。</summary>
    internal sealed class NoopEngine : IOcrEngine
    {
        public static readonly NoopEngine Instance = new();

        public string Name => "noop";

        public IReadOnlyList<OcrBox> RecognizeImage(GrayImage image) => [];
    }

    /// <summary>测试用固定文本引擎：识别任何图像都返回同一行文本，供流水线输入方式测试。</summary>
    internal sealed class TextEngine : IOcrEngine
    {
        private readonly string _text;

        public TextEngine(string text) => _text = text;

        public string Name => "text";

        public IReadOnlyList<OcrBox> RecognizeImage(GrayImage image) =>
            [new OcrBox(_text, 1f, 0f, 0f, 1f, 1f)];
    }
}
