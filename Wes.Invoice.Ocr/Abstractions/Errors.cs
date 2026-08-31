namespace Wes.Invoice.Ocr.Abstractions;

/// <summary>错误分类。</summary>
public enum OcrErrorKind
{
    /// <summary>图像解码失败。</summary>
    Decode,

    /// <summary>PDF 文本提取失败。</summary>
    Pdf,

    /// <summary>扫描型 PDF 需要先渲染为图像，渲染后端尚未接入。</summary>
    RasterNotAvailable,

    /// <summary>OCR 引擎错误。</summary>
    Ocr,

    /// <summary>OCR 引擎未配置。</summary>
    EngineNotConfigured,

    /// <summary>输入为空或内容过少。</summary>
    EmptyInput,
}

/// <summary>OCR 引擎统一异常。</summary>
public class OcrException : Exception
{
    /// <summary>错误分类（默认 Ocr）。</summary>
    public OcrErrorKind Kind { get; }

    public OcrException(string message, OcrErrorKind kind = OcrErrorKind.Ocr) : base(message)
        => Kind = kind;

    public OcrException(string message, Exception inner, OcrErrorKind kind = OcrErrorKind.Ocr) : base(message, inner)
        => Kind = kind;

    public static OcrException EmptyInput() =>
        new("输入为空或内容过少", OcrErrorKind.EmptyInput);

    public static OcrException RasterNotAvailable() =>
        new("扫描型 PDF 需要先渲染为图像，渲染后端尚未接入", OcrErrorKind.RasterNotAvailable);
}

/// <summary>模型加载/初始化失败。</summary>
public sealed class OcrModelLoadException : OcrException
{
    public OcrModelLoadException(string message) : base(message, OcrErrorKind.EngineNotConfigured) { }
    public OcrModelLoadException(string message, Exception inner) : base(message, inner, OcrErrorKind.EngineNotConfigured) { }
}

/// <summary>推理失败。</summary>
public sealed class OcrInferenceException : OcrException
{
    public OcrInferenceException(string message) : base(message, OcrErrorKind.Ocr) { }
    public OcrInferenceException(string message, Exception inner) : base(message, inner, OcrErrorKind.Ocr) { }
}
