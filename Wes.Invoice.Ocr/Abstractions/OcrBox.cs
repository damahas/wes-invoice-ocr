namespace Wes.Invoice.Ocr.Abstractions;

/// <summary>OCR 识别出的单个文本框。</summary>
public sealed record OcrBox(
    /// <summary>识别文本。</summary>
    string Text,

    /// <summary>置信度 0.0 ~ 1.0。</summary>
    float Confidence,

    /// <summary>归一化坐标（0.0~1.0，相对图像宽高）。</summary>
    float X,

    float Y,
    float W,
    float H);
