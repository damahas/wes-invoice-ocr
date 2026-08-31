namespace Wes.Invoice.Ocr.Abstractions;

/// <summary>二维码校验状态。</summary>
public enum QrStatus
{
    /// <summary>未扫描到二维码 / 无共同字段可比对（不视为异常）。</summary>
    NotScanned,

    /// <summary>二维码与 OCR 字段交叉一致。</summary>
    Verified,

    /// <summary>二维码与 OCR 字段存在冲突（疑点，需人工复核）。</summary>
    Mismatch,

    /// <summary>检测到二维码区域但解码失败（图片质量问题等）。</summary>
    DecodeFailed,
}

/// <summary>从二维码内容解析出的结构化字段（key 与 <see cref="FieldValue"/> 的 Key 对齐）。</summary>
public sealed record QrData(
    /// <summary>二维码原始内容。</summary>
    string Raw,

    /// <summary>规范化字段：invoice_code / invoice_no / invoice_date(YYYYMMDD) / total_amount_with_tax。</summary>
    IReadOnlyDictionary<string, string> Fields);

/// <summary>二维码与 OCR 交叉一致的字段。</summary>
public sealed record QrFieldMatch(string Key, string QrValue, string OcrValue);

/// <summary>二维码与 OCR 冲突的字段。</summary>
public sealed record QrConflict(string Key, string QrValue, string OcrValue);

/// <summary>二维码校验结果（附加在 Invoice 上，可为 null 表示未启用解码器）。</summary>
public sealed record Verification(
    QrStatus Status,
    string? RawContent,
    IReadOnlyList<QrFieldMatch> Matched,
    IReadOnlyList<QrConflict> Conflicts);
