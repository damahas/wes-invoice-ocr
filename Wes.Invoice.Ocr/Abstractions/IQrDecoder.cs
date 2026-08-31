namespace Wes.Invoice.Ocr.Abstractions;

/// <summary>
/// 二维码解码器抽象。实现必须可跨线程并发调用。
/// 解码失败返回 null（不抛异常），保证 QR 分支异常不影响 OCR 主流程。
/// </summary>
public interface IQrDecoder
{
    /// <summary>从灰度图像解码二维码，返回解析后的结构化数据；无法解码返回 null。</summary>
    QrData? TryDecode(GrayImage image);
}
