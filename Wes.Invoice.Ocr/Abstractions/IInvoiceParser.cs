namespace Wes.Invoice.Ocr.Abstractions;

/// <summary>票据解析器：每种票据类型一个实现。</summary>
public interface IInvoiceParser
{
    /// <summary>本解析器处理的票据类型。</summary>
    InvoiceKind Kind { get; }

    /// <summary>把 OCR / PDF 提取出的文本解析为结构化字段。</summary>
    IReadOnlyList<FieldValue> Parse(string text);
}
