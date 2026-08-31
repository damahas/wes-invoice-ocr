using Wes.Invoice.Ocr.Abstractions;

namespace Wes.Invoice.Ocr.Algorithms;

/// <summary>
/// det/rec/cls 的图像预处理：resize + 归一化 → NCHW 扁平张量（float[]）。
/// </summary>
public static class Preprocess
{
    /// <summary>det 预处理：长边等比缩放到 det 输入，短边按比例，右下角 pad 到 dh×dw，ImageNet 归一化。</summary>
    /// <returns>(张量, ratio_x, ratio_y, off_x, off_y)。</returns>
    public static (float[] Tensor, float RatioX, float RatioY, int OffX, int OffY) PreprocessDet(
        GrayImage img, int dh, int dw)
    {
        int iw = img.Width, ih = img.Height;
        int limit = Math.Max(dh, dw);
        float ratio = Math.Max(iw, ih) > limit ? limit / (float)Math.Max(iw, ih) : 1.0f;
        int nw = Math.Max((int)Math.Round(iw * ratio), 1);
        int nh = Math.Max((int)Math.Round(ih * ratio), 1);

        var resized = ImageOps.Resize(img, nw, nh);
        float[] mean = [0.485f, 0.456f, 0.406f];
        float[] std = [0.229f, 0.224f, 0.225f];

        var result = new float[3 * dh * dw];
        int nhClamp = Math.Min(nh, dh);
        int nwClamp = Math.Min(nw, dw);
        for (int y = 0; y < nhClamp; y++)
        {
            for (int x = 0; x < nwClamp; x++)
            {
                float p = resized.GetPixel(x, y) / 255.0f;
                int baseIdx = y * dw + x;
                for (int c = 0; c < 3; c++)
                    result[c * dh * dw + baseIdx] = (p - mean[c]) / std[c];
            }
        }
        return (result, ratio, ratio, 0, 0);
    }

    /// <summary>det 预处理（动态输入版）：长边等比缩放到 limit，pad 到 32 的倍数。</summary>
    /// <returns>(张量, model_h, model_w, ratio_x, ratio_y, off_x, off_y)。</returns>
    public static (float[] Tensor, int ModelH, int ModelW, float RatioX, float RatioY, int OffX, int OffY)
        PreprocessDetDyn(GrayImage img, int limit)
    {
        int iw = img.Width, ih = img.Height;
        float ratio = Math.Max(iw, ih) > limit ? limit / (float)Math.Max(iw, ih) : 1.0f;
        int nw = Math.Max((int)Math.Round(iw * ratio), 1);
        int nh = Math.Max((int)Math.Round(ih * ratio), 1);

        var resized = ImageOps.Resize(img, nw, nh);
        int dh = ((nh + 31) / 32) * 32;
        int dw = ((nw + 31) / 32) * 32;
        float[] mean = [0.485f, 0.456f, 0.406f];
        float[] std = [0.229f, 0.224f, 0.225f];

        var result = new float[3 * dh * dw];
        for (int y = 0; y < nh; y++)
        {
            for (int x = 0; x < nw; x++)
            {
                float p = resized.GetPixel(x, y) / 255.0f;
                int baseIdx = y * dw + x;
                for (int c = 0; c < 3; c++)
                    result[c * dh * dw + baseIdx] = (p - mean[c]) / std[c];
            }
        }
        return (result, dh, dw, ratio, ratio, 0, 0);
    }

    /// <summary>rec 预处理（固定宽）：高=rh，宽上限 rw，mean/std=0.5。</summary>
    public static float[] PreprocessRec(GrayImage crop, int rh, int rw)
    {
        float ratio = Math.Max(crop.Height, 1) / (float)rh;
        int w = Math.Max((int)Math.Round(crop.Width / ratio), 1);
        w = Math.Min(w, rw);

        var resized = ImageOps.Resize(crop, w, rh);
        var result = new float[3 * rh * rw];
        for (int y = 0; y < rh; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float p = resized.GetPixel(x, y) / 255.0f;
                float v = (p - 0.5f) / 0.5f;
                int baseIdx = y * rw + x;
                result[baseIdx] = v;
                result[rh * rw + baseIdx] = v;
                result[2 * rh * rw + baseIdx] = v;
            }
        }
        return result;
    }

    /// <summary>rec 预处理（动态宽版）：高固定 rh，宽按比例计算，上限 max_w，pad 到 8 的倍数。</summary>
    /// <returns>(张量, 实际输入宽)。</returns>
    public static (float[] Tensor, int Width) PreprocessRecDyn(GrayImage crop, int rh, int maxW)
    {
        float ratio = Math.Max(crop.Height, 1) / (float)rh;
        int w = Math.Max((int)Math.Round(crop.Width / ratio), 1);
        w = Math.Min(w, maxW);
        w = Math.Max(w, 1);
        int wp = ((w + 7) / 8) * 8;

        var resized = ImageOps.Resize(crop, w, rh);
        var result = new float[3 * rh * wp];
        for (int y = 0; y < rh; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float p = resized.GetPixel(x, y) / 255.0f;
                float v = (p - 0.5f) / 0.5f;
                int baseIdx = y * wp + x;
                result[baseIdx] = v;
                result[rh * wp + baseIdx] = v;
                result[2 * rh * wp + baseIdx] = v;
            }
        }
        return (result, wp);
    }

    /// <summary>cls 预处理：直接 resize 到固定尺寸，mean/std=0.5。</summary>
    public static float[] PreprocessCls(GrayImage crop, int ch, int cw)
    {
        var resized = ImageOps.Resize(crop, cw, ch);
        var result = new float[3 * ch * cw];
        for (int y = 0; y < ch; y++)
        {
            for (int x = 0; x < cw; x++)
            {
                float p = resized.GetPixel(x, y) / 255.0f;
                float v = (p - 0.5f) / 0.5f;
                int baseIdx = y * cw + x;
                result[baseIdx] = v;
                result[ch * cw + baseIdx] = v;
                result[2 * ch * cw + baseIdx] = v;
            }
        }
        return result;
    }
}
