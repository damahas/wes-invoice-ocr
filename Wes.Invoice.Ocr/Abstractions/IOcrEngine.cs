namespace Wes.Invoice.Ocr.Abstractions;

/// <summary>
/// OCR 引擎抽象。实现必须可跨线程并发调用。
/// </summary>
public interface IOcrEngine
{
    /// <summary>引擎名称，如 paddleocr-onnx。</summary>
    string Name { get; }

    /// <summary>识别灰度图像，返回文本检测框列表。</summary>
    IReadOnlyList<OcrBox> RecognizeImage(GrayImage image);
}
