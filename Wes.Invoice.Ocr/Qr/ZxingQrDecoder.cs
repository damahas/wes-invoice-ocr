using Wes.Invoice.Ocr.Abstractions;
using ZXing;
using ZXing.Common;

namespace Wes.Invoice.Ocr.Qr;

/// <summary>
/// 基于 ZXing.Net 的二维码解码器。
/// 策略（均为毫秒级，且在 OcrService 中并行执行，不阻塞 OCR 主流程）：
/// 1. ROI 优先：右上角（数电票/电子发票）→ 左上角（传统纸质票），只解码局部区域；
/// 2. 全图降级：ROI 均失败时对整图解码兜底。
/// 任一步骤抛异常都被吞掉并返回 null，保证 QR 分支不影响主流程。
/// </summary>
public sealed class ZxingQrDecoder : IQrDecoder
{
    /// <summary>BarcodeReaderGeneric 非线程安全，按线程隔离实例。</summary>
    private readonly ThreadLocal<BarcodeReaderGeneric> _reader = new(CreateReader);

    public QrData? TryDecode(GrayImage image)
    {
        if (image is null || image.Width <= 0 || image.Height <= 0)
            return null;

        try
        {
            // 1. ROI 优先：右上角 → 左上角
            foreach (var roi in CandidateRois(image))
            {
                var crop = Crop(image, roi.X, roi.Y, roi.W, roi.H);
                if (TryDecodeOnce(crop, out var text))
                    return QrDataParser.Parse(text);
            }

            // 2. 全图降级
            if (TryDecodeOnce(image, out var fullText))
                return QrDataParser.Parse(fullText);
        }
        catch
        {
            // QR 分支绝不抛异常影响 OCR 主流程
        }
        return null;
    }

    // ---------- 内部 ----------

    static BarcodeReaderGeneric CreateReader() => new()
    {
        AutoRotate = true,
        Options = new DecodingOptions
        {
            TryHarder = true,
            PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
        },
    };

    bool TryDecodeOnce(GrayImage img, out string text)
    {
        text = "";
        var luminance = ToLuminance(img);
        var result = _reader.Value!.Decode(luminance);
        if (result is null || string.IsNullOrWhiteSpace(result.Text))
            return false;
        text = result.Text;
        return true;
    }

    /// <summary>候选 ROI：右上角（x∈[0.5w,w)，y∈[0,0.35h)）与左上角（x∈[0,0.5w)，y∈[0,0.35h)）。</summary>
    static IEnumerable<(int X, int Y, int W, int H)> CandidateRois(GrayImage img)
    {
        int w = img.Width, h = img.Height;
        int rw = Math.Max(64, (int)Math.Ceiling(w * 0.5f));
        int rh = Math.Max(64, (int)Math.Ceiling(h * 0.35f));
        yield return (w - rw, 0, rw, rh); // 右上
        yield return (0, 0, rw, rh);      // 左上
    }

    static GrayImage Crop(GrayImage img, int x, int y, int cw, int ch)
    {
        cw = Math.Min(cw, img.Width - x);
        ch = Math.Min(ch, img.Height - y);
        if (cw <= 0 || ch <= 0)
            return img;

        var outPixels = new byte[cw * ch];
        for (int row = 0; row < ch; row++)
        {
            int src = (y + row) * img.Width + x;
            Array.Copy(img.Pixels, src, outPixels, row * cw, cw);
        }
        return new GrayImage(cw, ch, outPixels);
    }

    /// <summary>灰度像素 → ZXing RGB24 LuminanceSource（R=G=B=灰度值）。</summary>
    static RGBLuminanceSource ToLuminance(GrayImage img)
    {
        // 不池化：RGBLuminanceSource 构造后可能持有数组引用，归还复用有数据覆盖风险
        var src = img.Pixels;
        var rgb = new byte[src.Length * 3];
        for (int i = 0; i < src.Length; i++)
        {
            rgb[i * 3] = src[i];
            rgb[i * 3 + 1] = src[i];
            rgb[i * 3 + 2] = src[i];
        }
        return new RGBLuminanceSource(rgb, img.Width, img.Height, RGBLuminanceSource.BitmapFormat.RGB24);
    }
}
