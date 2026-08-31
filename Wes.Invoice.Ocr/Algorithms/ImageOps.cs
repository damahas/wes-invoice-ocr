using Wes.Invoice.Ocr.Abstractions;

namespace Wes.Invoice.Ocr.Algorithms;

/// <summary>
/// 图像操作：缩放、180° 旋转、旋转裁剪（文本行矫正）。
/// 纯 BCL 实现（操作 GrayImage）。
/// </summary>
public static class ImageOps
{
    /// <summary>三角形滤波缩放，两遍法：先水平后垂直（与 PaddleOCR 预处理保持一致）。</summary>
    public static GrayImage Resize(GrayImage img, int w, int h)
    {
        if (w <= 0 || h <= 0)
            throw new ArgumentException($"目标尺寸非法: {w}x{h}");
        if (w == img.Width && h == img.Height)
            return img;

        var tmp = ResizeHorizontal(img, w);
        return ResizeVertical(tmp, h);
    }

    /// <summary>180° 旋转（方向分类器判定后使用）。</summary>
    public static GrayImage Rotate180(GrayImage img)
    {
        int w = img.Width, h = img.Height;
        var outPixels = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            int srcRow = (h - 1 - y) * w;
            int dstRow = y * w;
            for (int x = 0; x < w; x++)
                outPixels[dstRow + x] = img.Pixels[srcRow + (w - 1 - x)];
        }
        return new GrayImage(w, h, outPixels);
    }

    /// <summary>
    /// 以 (cx, cy) 为中心、宽 w 高 h 的旋转矩形裁剪，angle 为旋转角（弧度）。
    /// 一次完成旋转 + 裁剪（双线性插值），用于把倾斜文本行矫正为水平。
    /// </summary>
    public static GrayImage RotatedCrop(GrayImage img, float cx, float cy, int w, int h, float angle)
    {
        int iw = img.Width, ih = img.Height;
        w = Math.Max(w, 1);
        h = Math.Max(h, 1);
        float cos = (float)Math.Cos(angle);
        float sin = (float)Math.Sin(angle);
        var outPixels = new byte[w * h];
        float cw = w / 2f;
        float ch = h / 2f;

        for (int y = 0; y < h; y++)
        {
            float ly = y - ch;
            for (int x = 0; x < w; x++)
            {
                float lx = x - cw;
                // 反旋转 + 平移回原图坐标
                float sx = cx + lx * cos + ly * sin;
                float sy = cy - lx * sin + ly * cos;
                if (sx >= 0f && sy >= 0f && sx <= iw - 1f && sy <= ih - 1f)
                {
                    int x0 = (int)sx;
                    int y0 = (int)sy;
                    int x1 = Math.Min(x0 + 1, iw - 1);
                    int y1 = Math.Min(y0 + 1, ih - 1);
                    float dx = sx - x0;
                    float dy = sy - y0;
                    float p00 = img.Pixels[y0 * iw + x0];
                    float p10 = img.Pixels[y0 * iw + x1];
                    float p01 = img.Pixels[y1 * iw + x0];
                    float p11 = img.Pixels[y1 * iw + x1];
                    float v = p00 * (1f - dx) * (1f - dy)
                            + p10 * dx * (1f - dy)
                            + p01 * (1f - dx) * dy
                            + p11 * dx * dy;
                    outPixels[y * w + x] = (byte)v;
                }
            }
        }
        return new GrayImage(w, h, outPixels);
    }

    // ---- 内部：三角形滤波两遍缩放 ----

    private static GrayImage ResizeHorizontal(GrayImage img, int newW)
    {
        if (newW == img.Width)
            return img;
        int ih = img.Height;
        var outPixels = new byte[newW * ih];
        float scale = img.Width / (float)newW;

        for (int y = 0; y < ih; y++)
        {
            int srcRow = y * img.Width;
            int dstRow = y * newW;
            for (int x = 0; x < newW; x++)
            {
                float center = (x + 0.5f) * scale - 0.5f;
                int left = (int)Math.Floor(center);
                float sum = 0f, wsum = 0f;
                for (int i = left; i <= left + 2; i++)
                {
                    float weight = TriangleWeight(center - i);
                    if (weight == 0f)
                        continue;
                    int idx = Math.Max(0, Math.Min(i, img.Width - 1));
                    sum += img.Pixels[srcRow + idx] * weight;
                    wsum += weight;
                }
                outPixels[dstRow + x] = wsum > 0f ? (byte)(sum / wsum) : (byte)0;
            }
        }
        return new GrayImage(newW, ih, outPixels);
    }

    private static GrayImage ResizeVertical(GrayImage img, int newH)
    {
        if (newH == img.Height)
            return img;
        int w = img.Width;
        var outPixels = new byte[w * newH];
        float scale = img.Height / (float)newH;

        for (int y = 0; y < newH; y++)
        {
            float center = (y + 0.5f) * scale - 0.5f;
            int top = (int)Math.Floor(center);
            for (int x = 0; x < w; x++)
            {
                float sum = 0f, wsum = 0f;
                for (int i = top; i <= top + 2; i++)
                {
                    float weight = TriangleWeight(center - i);
                    if (weight == 0f)
                        continue;
                    int idx = Math.Max(0, Math.Min(i, img.Height - 1));
                    sum += img.Pixels[idx * w + x] * weight;
                    wsum += weight;
                }
                outPixels[y * w + x] = wsum > 0f ? (byte)(sum / wsum) : (byte)0;
            }
        }
        return new GrayImage(w, newH, outPixels);
    }

    private static float TriangleWeight(float d)
    {
        d = Math.Abs(d);
        return d >= 1f ? 0f : 1f - d;
    }
}
