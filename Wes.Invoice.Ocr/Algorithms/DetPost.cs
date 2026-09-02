namespace Wes.Invoice.Ocr.Algorithms;

/// <summary>
/// DB（Differentiable Binarization）文本检测后处理：
/// 概率图 → 二值化 → 连通域 → 最小外接矩形 → unclip 放大 → 文本框四边形。
/// 参数对齐 PaddleOCR PP-OCRv4 默认：thresh=0.3, box_thresh=0.6, unclip_ratio=1.5。
/// </summary>
public static class DetPost
{
    public const float UnclipRatio = 1.5f;

    /// <summary>从 det 模型输出的概率图提取文本框。</summary>
    /// <param name="score">概率图（大小 modelH * modelW，单通道）。</param>
    /// <param name="modelH">模型输出高。</param>
    /// <param name="modelW">模型输出宽。</param>
    /// <param name="scaleX">模型坐标→原图坐标的 X 缩放（原图宽 / 模型宽）。</param>
    /// <param name="scaleY">模型坐标→原图坐标的 Y 缩放。</param>
    /// <param name="offX">模型坐标系中的 pad 偏移 X（右下角 pad 时为 0）。</param>
    /// <param name="offY">模型坐标系中的 pad 偏移 Y。</param>
    /// <param name="dbThresh">DB 二值化阈值（默认 0.3）。</param>
    /// <param name="boxThresh">框最小平均概率阈值（默认 0.6）。</param>
    public static List<Quad> DetectBoxes(
        float[] score, int modelH, int modelW,
        float scaleX, float scaleY, int offX, int offY,
        float dbThresh = 0.3f, float boxThresh = 0.6f)
    {
        int total = modelH * modelW;
        if (score.Length < total)
            return [];

        // 二值化
        var mask = new bool[total];
        for (int i = 0; i < total; i++)
            mask[i] = score[i] > dbThresh;

        // 8 邻域连通域标记
        var labels = new int[total];
        int next = 1;
        for (int y = 0; y < modelH; y++)
        {
            for (int x = 0; x < modelW; x++)
            {
                int i = y * modelW + x;
                if (mask[i] && labels[i] == 0)
                {
                    FloodFill(mask, labels, modelW, modelH, x, y, next);
                    next++;
                }
            }
        }

        // 一次遍历按连通域分组（O(N)）
        var groups = new List<List<int>>(next);
        for (int g = 0; g < next; g++)
            groups.Add([]);
        var sums = new float[next];
        for (int i = 0; i < total; i++)
        {
            int l = labels[i];
            if (l > 0)
            {
                groups[l].Add(i);
                sums[l] += score[i];
            }
        }

        var boxes = new List<Quad>();
        for (int lab = 1; lab < next; lab++)
        {
            var pix = groups[lab];
            if (pix.Count < 3)
                continue;
            float mean = sums[lab] / pix.Count;
            if (mean < boxThresh)
                continue;

            // 惰性构造坐标点（仅对通过的框）
            var pts = new List<Point2D>(pix.Count);
            foreach (int i in pix)
                pts.Add(new Point2D(i % modelW, i / modelW));

            var (cx, cy, bw, bh, angle) = Geometry.MinAreaRect(pts);
            float w2 = bw * UnclipRatio;
            float h2 = bh * UnclipRatio;
            var quad = Geometry.QuadFromRect(cx, cy, w2, h2, angle);

            var scaled = new Point2D[4];
            for (int k = 0; k < 4; k++)
            {
                scaled[k] = new Point2D(
                    (quad.Pts[k].X - offX) * scaleX,
                    (quad.Pts[k].Y - offY) * scaleY);
            }
            boxes.Add(new Quad(scaled));
        }
        return boxes;
    }

    private static void FloodFill(
        bool[] mask, int[] labels, int w, int h, int sx, int sy, int label)
    {
        var stack = new Stack<(int X, int Y)>();
        stack.Push((sx, sy));
        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            int i = y * w + x;
            if (labels[i] != 0)
                continue;
            labels[i] = label;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                        continue;
                    int nx = x + dx;
                    int ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                        continue;
                    int ni = ny * w + nx;
                    if (mask[ni] && labels[ni] == 0)
                        stack.Push((nx, ny));
                }
            }
        }
    }
}
