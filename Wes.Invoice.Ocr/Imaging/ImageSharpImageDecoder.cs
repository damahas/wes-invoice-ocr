using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Wes.Invoice.Ocr.Abstractions;

namespace Wes.Invoice.Ocr.Imaging;

/// <summary>ImageSharp 图像解码实现：任意支持格式 → 灰度图。</summary>
public static class ImageSharpImageDecoder
{
    /// <summary>解码字节数组为灰度图（jpg/png/bmp/webp 等）。</summary>
    public static GrayImage DecodeGray(byte[] data)
    {
        try
        {
            using var img = Image.Load<L8>(data);
            return ToGray(img);
        }
        catch (Exception ex)
        {
            throw new OcrException($"图像解码失败: {ex.Message}", ex, OcrErrorKind.Decode);
        }
    }

    /// <summary>ImageSharp 图像 → 灰度图（供引擎复用，避免重复解码）。</summary>
    public static GrayImage ToGray(Image<L8> img)
    {
        int w = img.Width, h = img.Height;
        var pixels = new byte[w * h];
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                    pixels[y * w + x] = row[x].PackedValue;
            }
        });
        return new GrayImage(w, h, pixels);
    }
}
