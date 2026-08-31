namespace Wes.Invoice.Ocr.Abstractions;

/// <summary>单个字段抽取结果。</summary>
public sealed record FieldValue(
    /// <summary>字段 key，如 invoice_no、train_no（供上层程序化读取）。</summary>
    string Key,

    /// <summary>字段中文名，如 发票号码（供展示）。</summary>
    string Label,

    /// <summary>识别值（字符串）。</summary>
    string Value,

    /// <summary>置信度 0.0 ~ 1.0。</summary>
    float Confidence);
