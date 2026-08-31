namespace Wes.Invoice.Ocr.Abstractions;

/// <summary>一张票据的识别结果。</summary>
public sealed record Invoice(
    /// <summary>票据类型。</summary>
    InvoiceKind Kind,

    /// <summary>按解析器模板抽取出的结构化字段。</summary>
    IReadOnlyList<FieldValue> Fields,

    /// <summary>原始识别/提取文本（调试与后处理使用）。</summary>
    string RawText,

    /// <summary>
    /// 二维码交叉校验结果（启用 IQrDecoder 且有图像输入时非 null）。
    /// </summary>
    Verification? Verification = null);
