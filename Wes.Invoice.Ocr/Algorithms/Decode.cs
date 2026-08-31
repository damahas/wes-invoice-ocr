using System.Text;

namespace Wes.Invoice.Ocr.Algorithms;

/// <summary>
/// 模型输出解码：det 概率图提取、CTC 贪心解码。
/// </summary>
public static class Decode
{
    /// <summary>从 det 输出提取单通道概率图，必要时最近邻缩放到 (detH, detW)。</summary>
    /// <param name="data">模型输出连续数据（row-major，可能含 batch/多通道维）。</param>
    /// <param name="shape">输出张量形状，至少 2 维，末两维为 (oh, ow)。</param>
    public static float[] ExtractScoreMap(float[] data, int[] shape, int detH, int detW)
    {
        int n = shape.Length;
        if (n < 2)
            throw new InvalidOperationException($"det 输出维度异常: [{string.Join(", ", shape)}]");

        int oh = shape[n - 2];
        int ow = shape[n - 1];

        if (oh == detH && ow == detW)
        {
            var direct = new float[oh * ow];
            Array.Copy(data, direct, oh * ow);
            return direct;
        }

        // 最近邻缩放
        var map = new float[detH * detW];
        for (int y = 0; y < detH; y++)
        {
            int sy = Math.Min(y * oh / detH, oh - 1);
            for (int x = 0; x < detW; x++)
            {
                int sx = Math.Min(x * ow / detW, ow - 1);
                map[y * detW + x] = data[sy * ow + sx];
            }
        }
        return map;
    }

    /// <summary>CTC 贪心解码：合并重复、跳过 blank（索引 0）。</summary>
    /// <param name="data">输出连续数据 [T, C] row-major。</param>
    /// <param name="shape">输出张量形状，至少 2 维，末两维为 (T, C)。</param>
    /// <param name="dict">字符字典（index 0 为 blank）。</param>
    public static (string Text, float Conf) DecodeCtc(float[] data, int[] shape, IReadOnlyList<string> dict)
    {
        int n = shape.Length;
        if (n < 2)
            return ("", 0f);

        int t = shape[n - 2];
        int c = shape[n - 1];
        return DecodeCtcFrom(data, t, c, dict);
    }

    /// <summary>
    /// 从连续数据解码一行（数据为 [T, C] row-major；offset 用于批量输出的行偏移）。
    /// </summary>
    public static (string Text, float Conf) DecodeCtcFrom(float[] data, int t, int c, IReadOnlyList<string> dict, int offset = 0)
    {
        var text = new StringBuilder();
        float confSum = 0f;
        int cnt = 0;
        int last = 0;

        for (int i = 0; i < t; i++)
        {
            int baseIdx = offset + i * c;
            if (baseIdx + c > data.Length)
                break;

            int best = 0;
            float bestV = data[baseIdx];
            for (int j = 1; j < c; j++)
            {
                float v = data[baseIdx + j];
                if (v > bestV)
                {
                    bestV = v;
                    best = j;
                }
            }

            if (best != 0 && best != last)
            {
                // 模型输出布局：索引 0 = blank，1..N = 字符集，N+1 = space（PP-OCRv4/v5 一致）
                int idx = best - 1;
                string ch = idx < dict.Count ? dict[idx] : " "; // 末尾补充字符（space）
                if (!string.IsNullOrEmpty(ch))
                {
                    text.Append(ch);
                    confSum += bestV;
                    cnt++;
                }
            }
            last = best;
        }

        float conf = cnt > 0 ? confSum / cnt : 0f;
        return (text.ToString(), conf);
    }
}
