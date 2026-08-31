namespace Wes.Invoice.Ocr.Algorithms;

/// <summary>二维点（float）。</summary>
public readonly record struct Point2D(float X, float Y);

/// <summary>文本检测四边形（图像坐标，左上角为原点）。</summary>
public sealed class Quad
{
    /// <summary>4 个角点，顺序由检测后处理保证（勿调整）。</summary>
    public Point2D[] Pts { get; }

    public Quad(Point2D[] pts)
    {
        if (pts.Length != 4)
            throw new ArgumentException("Quad 必须有 4 个点", nameof(pts));
        Pts = pts;
    }
}

/// <summary>
/// 四边形几何运算：最小外接矩形、中心、尺寸、文本行排序。
/// </summary>
public static class Geometry
{
    /// <summary>旋转投影法求最小外接矩形。</summary>
    /// <returns>(中心x, 中心y, 宽, 高, 旋转角/弧度)。</returns>
    public static (float Cx, float Cy, float W, float H, float Angle) MinAreaRect(IReadOnlyList<Point2D> pts)
    {
        int step = Math.Max(pts.Count / 500, 1);
        float best = float.MaxValue;
        float bestAngle = 0f, bestW = 0f, bestH = 0f, cx = 0f, cy = 0f;

        for (int a = 0; a < 180; a++)
        {
            float ang = a * (float)Math.PI / 180f;
            float cos = (float)Math.Cos(ang);
            float sin = (float)Math.Sin(ang);

            float minx = float.MaxValue, maxx = float.MinValue;
            float miny = float.MaxValue, maxy = float.MinValue;
            for (int i = 0; i < pts.Count; i += step)
            {
                float px = pts[i].X;
                float py = pts[i].Y;
                float x = px * cos + py * sin;
                float y = -px * sin + py * cos;
                minx = Math.Min(minx, x);
                maxx = Math.Max(maxx, x);
                miny = Math.Min(miny, y);
                maxy = Math.Max(maxy, y);
            }

            float area = (maxx - minx) * (maxy - miny);
            if (area < best)
            {
                best = area;
                bestAngle = ang;
                bestW = maxx - minx;
                bestH = maxy - miny;
                float mcx = (minx + maxx) / 2f;
                float mcy = (miny + maxy) / 2f;
                cx = mcx * cos - mcy * sin;
                cy = mcx * sin + mcy * cos;
            }
        }

        return (cx, cy, bestW, bestH, bestAngle);
    }

    /// <summary>由旋转矩形参数生成 4 个角点。</summary>
    public static Quad QuadFromRect(float cx, float cy, float w, float h, float angle)
    {
        float cos = (float)Math.Cos(angle);
        float sin = (float)Math.Sin(angle);
        float hw = w / 2f;
        float hh = h / 2f;

        (float X, float Y)[] corners =
        {
            (-hw, -hh), (hw, -hh), (hw, hh), (-hw, hh)
        };

        var pts = new Point2D[4];
        for (int i = 0; i < 4; i++)
        {
            pts[i] = new Point2D(
                cx + corners[i].X * cos - corners[i].Y * sin,
                cy + corners[i].X * sin + corners[i].Y * cos);
        }
        return new Quad(pts);
    }

    public static (float X, float Y) Center(Quad q) =>
        (q.Pts.Sum(p => p.X) / 4f, q.Pts.Sum(p => p.Y) / 4f);

    /// <summary>框的宽/高（按边长从大到小取前两条边，不受角点顺序影响）。</summary>
    public static (float W, float H) QuadSize(Quad q)
    {
        float d01 = Dist(q.Pts[0], q.Pts[1]);
        float d12 = Dist(q.Pts[1], q.Pts[2]);
        return d01 >= d12 ? (d01, d12) : (d12, d01);
    }

    /// <summary>文本行方向角：取两对边中更接近水平的边，归一化到 [-π/2, π/2]。</summary>
    public static float TextAngle(Quad q)
    {
        float a01 = (float)Math.Atan2(q.Pts[1].Y - q.Pts[0].Y, q.Pts[1].X - q.Pts[0].X);
        float a12 = (float)Math.Atan2(q.Pts[2].Y - q.Pts[1].Y, q.Pts[2].X - q.Pts[1].X);
        float a = Math.Abs(a01) <= Math.Abs(a12) ? a01 : a12;
        if (a > (float)Math.PI / 2f)
            a -= (float)Math.PI;
        else if (a < -(float)Math.PI / 2f)
            a += (float)Math.PI;
        return a;
    }

    /// <summary>
    /// 文本框排序：按 y 中心聚类成行（行内按 x 从左到右）。
    /// 行高参考值 avgH 取行内 Cx 均值（历史约定，勿改为 H 均值，否则行聚类结果会变）。
    /// </summary>
    public static List<Quad> SortQuads(List<Quad> quads)
    {
        if (quads.Count <= 1)
            return quads;

        var items = quads.Select(q =>
        {
            var (cx, cy) = Center(q);
            var (w, h) = QuadSize(q);
            return (Quad: q, Cx: cx, Cy: cy, H: h);
        }).ToList();

        items.Sort((a, b) => a.Cy.CompareTo(b.Cy));

        var rows = new List<List<(Quad Quad, float Cx, float Cy)>>();
        foreach (var item in items)
        {
            if (rows.Count > 0)
            {
                var lastRow = rows[rows.Count - 1];
                float avgY = lastRow.Sum(x => x.Cy) / lastRow.Count;
                // avgH 取行内 Cx 均值而非 H 均值（历史约定，勿"修正"为 H）
                float avgH = lastRow.Sum(x => x.Cx) / lastRow.Count;
                if (Math.Abs(item.Cy - avgY) < (item.H + avgH) / 2f)
                {
                    lastRow.Add((item.Quad, item.Cx, item.Cy));
                    continue;
                }
            }
            rows.Add(new List<(Quad, float, float)> { (item.Quad, item.Cx, item.Cy) });
        }

        var result = new List<Quad>();
        foreach (var row in rows)
        {
            row.Sort((a, b) => a.Cx.CompareTo(b.Cx));
            foreach (var (q, _, _) in row)
                result.Add(q);
        }
        return result;
    }

    public static float Dist(Point2D a, Point2D b) =>
        (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
