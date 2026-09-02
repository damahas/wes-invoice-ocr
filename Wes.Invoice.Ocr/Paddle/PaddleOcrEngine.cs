using Microsoft.ML.OnnxRuntime;
using Wes.Invoice.Ocr.Abstractions;
using Wes.Invoice.Ocr.Algorithms;

namespace Wes.Invoice.Ocr.Paddle;

/// <summary>
/// PaddleOCR 引擎（ONNX Runtime 版）。
/// 流程：det(文本检测) → 文本框矫正 → cls(方向分类, 可选) → rec(文本识别, CTC 解码)。
/// 模型（PP-OCRv6 det medium + PP-OCRv6 rec，RapidOCR ONNX 版）：
/// - det.onnx：输入 [1,3,H,W]（动态 H/W，长边上限 1280），输出概率图（与输入同尺寸）
/// - rec.onnx：输入 [1,3,48,W]（动态宽，上限 640），输出 [1,T,C]（T=序列长, C=词典+blank）
/// - cls.onnx（可选）：输入 [1,3,48,192]，输出 2 类
/// - ppocrv6_dict.txt：中文词典（rec.onnx 内嵌字符集时非必需）
/// </summary>
public sealed class PaddleOcrEngine : IOcrEngine, IDisposable
{
    private const int DefaultDetLimit = 960;
    private const int DefaultRecH = 48;
    private const int DefaultRecMaxW = 320;
    private const int DefaultClsH = 48;
    private const int DefaultClsW = 192;

    private readonly InferenceSession _det;
    private readonly List<InferenceSession> _recPool;
    private readonly List<InferenceSession> _clsPool;
    private readonly List<string> _dict;

    private readonly bool _roiEnabled;
    private readonly bool _useBatch;

    private readonly bool _detDynamic;
    private readonly int _detH, _detW, _detLimit;
    private readonly float _dbThresh, _boxThresh;
    private readonly bool _recDynamicW;
    private readonly bool _recBatch;
    private readonly int _recH, _recW, _recMaxW;
    private readonly int _clsH, _clsW;

    private readonly ILogger? _logger;

    /// <summary>det / rec / cls 输入名（从模型 metadata 读取）。</summary>
    private readonly string _detInput, _recInput, _clsInput;

    public string Name => "paddleocr-onnx";

    /// <summary>
    /// 构造时实际生效的执行提供方（"CUDA" / "DirectML" / "CPU"）。
    /// 与请求的 <see cref="PaddleOcrConfig.Ep"/> 可能不同（CUDA 不可用时会静默回退 CPU），
    /// 用于确认 GPU 是否真的启用。
    /// </summary>
    public string EpName { get; }

    public PaddleOcrEngine(string modelDir, PaddleOcrConfig? config = null)
        : this(modelDir, config ?? new PaddleOcrConfig(), null) { }

    public PaddleOcrEngine(string modelDir, PaddleOcrConfig? config, ILogger? logger)
    {
        var cfg = config ?? new PaddleOcrConfig();
        _logger = logger;

        // 模型目录：优先取构造参数，为空时回退到 PaddleOcrConfig.ModelDir；
        // 两者皆空则明确报错（否则 Path.GetFullPath("") 会抛难懂的 ArgumentException）。
        var resolvedDir = string.IsNullOrWhiteSpace(modelDir) ? cfg.ModelDir : modelDir;
        if (string.IsNullOrWhiteSpace(resolvedDir))
            throw new OcrException(
                "未指定 PaddleOCR 模型目录：请传入 modelDir 参数，或设置 PaddleOcrConfig.ModelDir。",
                OcrErrorKind.EngineNotConfigured);

        var dir = Path.GetFullPath(resolvedDir);
        var detPath = Path.Combine(dir, "det.onnx");
        var recPath = Path.Combine(dir, "rec.onnx");
        var clsPath = Path.Combine(dir, "cls.onnx");
        var dictPath = Path.Combine(dir, "ppocrv6_dict.txt");

        if (!File.Exists(detPath) || !File.Exists(recPath))
            throw new OcrException(
                $"PaddleOCR 模型文件缺失。模型目录: {dir}（需要 det.onnx / rec.onnx{(File.Exists(clsPath) ? "，cls.onnx 可选" : "")}{(File.Exists(detPath) ? "" : "，det.onnx 缺失")}）",
                OcrErrorKind.EngineNotConfigured);

        // ROI / 批量：由配置在构造时确定并缓存为字段，避免每次 RecognizeImage 重复判断。
        // 全部行为只取决于传入的配置，支持同进程内多实例差异化。
        _roiEnabled = cfg.RoiEnabled;
        _dbThresh = cfg.DbThresh;
        _boxThresh = cfg.BoxThresh;

        // 会话线程配置：rec 会话池 N 个 × 每会话 intra 线程 = max(1, 核数/N)
        var recThreads = Math.Max(Math.Min(cfg.RecThreads, 16), 1);
        var cores = Math.Max(Environment.ProcessorCount, 1);
        var perSession = Math.Max(cores / recThreads, 1);

        _det = OnnxSessionFactory.Load(detPath, perSession, cfg.Ep);
        _recPool = Enumerable.Range(0, recThreads)
            .Select(_ => OnnxSessionFactory.Load(recPath, perSession, cfg.Ep))
            .ToList();
        _clsPool = File.Exists(clsPath)
            ? Enumerable.Range(0, Math.Min(recThreads, 4))
                .Select(_ => OnnxSessionFactory.Load(clsPath, 1, cfg.Ep))
                .ToList()
            : [];

        // 词典：rec.onnx 内嵌 character（PP-OCRv6）> ppocrv6_dict.txt（兜底）
        var metaChars = OnnxSessionFactory.MetadataCharacters(_recPool[0]);
        _dict = metaChars is { Count: > 0 }
            ? metaChars
            : File.Exists(dictPath)
                ? OnnxSessionFactory.LoadDictFile(dictPath)
                : throw new OcrException(
                    $"PaddleOCR 词典缺失: {dir}（需要 ppocrv6_dict.txt，或 rec.onnx 内嵌字符集）",
                    OcrErrorKind.EngineNotConfigured);

        // 维度探测
        var (detH, detW) = OnnxSessionFactory.InputHw(_det);
        var (recH, recW) = OnnxSessionFactory.InputHw(_recPool[0]);
        var (clsH, clsW) = _clsPool.Count > 0 ? OnnxSessionFactory.InputHw(_clsPool[0]) : (DefaultClsH, DefaultClsW);
        _detDynamic = detH is null || detW is null;
        _recDynamicW = recW is null;
        _recBatch = OnnxSessionFactory.InputBatchDynamic(_recPool[0]);
        _detH = detH ?? 0;
        _detW = detW ?? 0;
        _detLimit = cfg.DetLimit;
        _recH = recH ?? DefaultRecH;
        _recW = recW ?? 0;
        _recMaxW = cfg.RecMaxW;
        _clsH = clsH ?? DefaultClsH;
        _clsW = clsW ?? DefaultClsW;

        // 批量还需模型支持（batch 维度动态），静态 batch 模型即使配置了也不会启用
        _useBatch = _recBatch && cfg.RecBatch;

        _detInput = _det.InputMetadata.Keys.First();
        _recInput = _recPool[0].InputMetadata.Keys.First();
        _clsInput = _clsPool.Count > 0 ? _clsPool[0].InputMetadata.Keys.First() : "";

        // 会话全部创建完毕后读取：LastEpName 是静态值，构造串行故此处即本引擎真实生效的 EP
        EpName = OnnxSessionFactory.LastEpName;

        logger?.Info(
            $"PaddleOcrEngine 加载完成: EP={EpName}(请求 {cfg.Ep}), det={( _detDynamic ? $"动态(长边≤{_detLimit})" : $"固定{_detH}x{_detW}" )}, " +
            $"rec={( _recDynamicW ? $"{_recH}x动态(宽≤{_recMaxW}){( _recBatch ? ", 支持批量" : "" )}" : $"{_recH}x{_recW}" )}, " +
            $"cls={(_clsPool.Count > 0 ? "启用" : "未启用")}, 词典 {_dict.Count} 字符");
    }

    /// <summary>识别灰度图像，返回文本检测框（归一化坐标）。</summary>
    public IReadOnlyList<OcrBox> RecognizeImage(GrayImage img)
    {
        int ih = img.Height, iw = img.Width;
        if (ih == 0 || iw == 0)
            return [];

        // ROI 预过滤：横版发票（宽高比 > 1.3）只识别关键字段区域。由 PaddleOcrConfig.RoiEnabled 控制。
        var roiEnabled = _roiEnabled && iw / (float)ih > 1.3f;

        // 分段计时：仅在传入 logger 时启用（生产传 null，零开销）
        var prof = _logger is null ? null : System.Diagnostics.Stopwatch.StartNew();
        long DetMs() => prof?.ElapsedMilliseconds ?? 0;

        // 1. 文本检测
        float[] detInput;
        int mh, mw;
        float rX, rY;
        int offX, offY;
        if (_detDynamic)
        {
            var (t, h, w, rx, ry, ox, oy) = Preprocess.PreprocessDetDyn(img, _detLimit);
            detInput = t; mh = h; mw = w; rX = rx; rY = ry; offX = ox; offY = oy;
        }
        else
        {
            var (t, rx, ry, ox, oy) = Preprocess.PreprocessDet(img, _detH, _detW);
            detInput = t; mh = _detH; mw = _detW; rX = rx; rY = ry; offX = ox; offY = oy;
        }

        var (score, _) = OnnxSessionFactory.RunSingle(_det, _detInput, detInput, new[] { 1, 3, mh, mw });
        var scoreMap = Decode.ExtractScoreMap(score, new[] { 1, 1, mh, mw }, mh, mw);
        var quads = DetPost.DetectBoxes(scoreMap, mh, mw, 1.0f / rX, 1.0f / rY, offX, offY, _dbThresh, _boxThresh);
        var detMs = DetMs();

        // ROI 过滤：中心落在任一 ROI 矩形内的框（归一化坐标）
        if (roiEnabled)
        {
            var rois = new (float X, float Y, float W, float H)[]
            {
                (0.55f, 0.00f, 0.45f, 0.22f), // 发票代码 / 号码 / 开票日期
                (0.00f, 0.18f, 0.50f, 0.12f), // 购买方（名称 / 税号）
                (0.50f, 0.18f, 0.50f, 0.12f), // 销售方（名称 / 税号）
                (0.00f, 0.48f, 1.00f, 0.22f), // 金额 / 税率 / 税额 / 明细合计
                (0.00f, 0.72f, 1.00f, 0.28f), // 价税合计（大小写）/ 备注 / 印章区文字
            };
            quads = quads.Where(q =>
            {
                var (cx, cy) = Geometry.Center(q);
                float nx = cx / iw, ny = cy / ih;
                return rois.Any(r => nx >= r.X && nx <= r.X + r.W && ny >= r.Y && ny <= r.Y + r.H);
            }).ToList();
        }
        quads = Geometry.SortQuads(quads);

        // 2. 逐块矫正（并行 rotated_crop）
        var jobs = new List<(int Idx, float Cx, float Cy, float W, float H, float Angle)>();
        foreach (var q in quads)
        {
            var (cx, cy) = Geometry.Center(q);
            var (w, h) = Geometry.QuadSize(q);
            if (w >= 2.0f && h >= 2.0f)
                jobs.Add((jobs.Count, cx, cy, w, h, Geometry.TextAngle(q)));
        }
        var nj = jobs.Count;
        var crops = new (float Cx, float Cy, float W, float H, GrayImage Img)?[nj];
        int nct = Math.Min(Math.Min(Math.Max(Environment.ProcessorCount, 1), Math.Max(nj, 1)), 8);
        if (nct <= 1)
        {
            for (int k = 0; k < nj; k++)
            {
                var j = jobs[k];
                crops[k] = (j.Cx, j.Cy, j.W, j.H, ImageOps.RotatedCrop(img, j.Cx, j.Cy, (int)j.W, (int)j.H, j.Angle));
            }
        }
        else
        {
            Parallel.For(0, nct, wi =>
            {
                int start = wi * nj / nct;
                int end = (wi + 1) * nj / nct;
                for (int k = start; k < end; k++)
                {
                    var j = jobs[k];
                    crops[k] = (j.Cx, j.Cy, j.W, j.H, ImageOps.RotatedCrop(img, j.Cx, j.Cy, (int)j.W, (int)j.H, j.Angle));
                }
            });
        }
        var cropList = crops.Select(c => c!.Value).ToList();

        // 3. 方向分类（cls 会话池并行）→ 180° 旋转
        var rotatedFlags = ClassifyParallel(cropList);
        for (int i = 0; i < cropList.Count; i++)
        {
            if (rotatedFlags[i])
            {
                var c = cropList[i];
                cropList[i] = (c.Cx, c.Cy, c.W, c.H, ImageOps.Rotate180(c.Img));
            }
        }

        var clsMs = prof is null ? 0 : prof.ElapsedMilliseconds - detMs;

        // 4. rec：批量（按宽度分桶、多行一次推理）或逐行，由 PaddleOcrConfig.RecBatch 决定
        var results = _useBatch
            ? RecognizeTextsBatch(cropList.Select(c => c.Img).ToList())
            : cropList.Select(c => RecognizeText(c.Img)).ToList();

        if (prof is not null)
        {
            var recMs = prof.ElapsedMilliseconds - detMs - clsMs;
            var total = prof.ElapsedMilliseconds;
            _logger!.Info(
                $"[分段] 总计 {total} ms | det {detMs} ms ({100.0 * detMs / total:F0}%) | " +
                $"cls {clsMs} ms ({100.0 * clsMs / total:F0}%) | rec {recMs} ms ({100.0 * recMs / total:F0}%) | " +
                $"框数 {quads.Count} | 批量={_useBatch}");
        }

        // 组装 OcrBox（归一化坐标）
        var boxes = new List<OcrBox>();
        for (int i = 0; i < cropList.Count; i++)
        {
            var (cx, cy, w, h, _) = cropList[i];
            var (text, conf) = results[i];
            if (text.Length > 0)
            {
                boxes.Add(new OcrBox(text, conf, cx / iw, cy / ih, w / iw, h / ih));
            }
        }
        return boxes;
    }

    /// <summary>单行文本识别：裁剪图 → rec 模型 → CTC 解码。</summary>
    private (string Text, float Conf) RecognizeText(GrayImage crop)
    {
        float[] input;
        int w;
        if (_recDynamicW)
            (input, w) = Preprocess.PreprocessRecDyn(crop, _recH, _recMaxW);
        else
        {
            input = Preprocess.PreprocessRec(crop, _recH, _recW);
            w = _recW;
        }
        var (data, shape) = OnnxSessionFactory.RunSingle(_recPool[0], _recInput, input, new[] { 1, 3, _recH, w });
        return Decode.DecodeCtc(data, shape, _dict);
    }

    /// <summary>
    /// 批量文本识别：超长行自动切分 + 按宽度分桶 + rec 会话池并行，多行一次推理。
    /// PP-OCRv5 mobile 训练宽度为 320，默认 max_w=320；超长行（开户行、地址、税号）
    /// 按 320 滑动窗口切分识别再合并，避免字符被压扁。
    /// </summary>
    private List<(string Text, float Conf)> RecognizeTextsBatch(List<GrayImage> crops)
    {
        int rh = _recH;
        int maxW = _recMaxW;

        // 1. 超长行切分：保持 rec 输入宽度 ≤ max_w，切分点选在字符间隙（垂直投影为 0 的列）
        var segs = new List<(int OrigIdx, GrayImage Crop, int TargetW)>();
        for (int i = 0; i < crops.Count; i++)
        {
            var crop = crops[i];
            float ratio = Math.Max(crop.Height, 1) / (float)rh;
            int tw = Math.Max((int)Math.Round(crop.Width / ratio), 1);
            if (tw <= maxW)
            {
                segs.Add((i, ImageOps.Resize(crop, tw, rh), tw));
                continue;
            }

            // 超长行：垂直投影找字符间隙，贪心切段（每段 ≤ max_orig 原图宽）
            int h = crop.Height, w = crop.Width;
            int maxOrig = (int)Math.Round(maxW * ratio);
            var proj = new int[w];
            for (int x = 0; x < w; x++)
            {
                int cnt = 0;
                for (int y = 0; y < h; y++)
                {
                    if (crop.Pixels[y * w + x] < 128)
                        cnt++;
                }
                proj[x] = cnt;
            }
            var gapCenters = new List<int>();
            int gi = 0;
            while (gi < w)
            {
                if (proj[gi] == 0)
                {
                    int s = gi;
                    while (gi < w && proj[gi] == 0)
                        gi++;
                    if (gi - s >= 2)
                        gapCenters.Add((s + gi - 1) / 2);
                }
                else
                {
                    gi++;
                }
            }

            int start = 0;
            while (true)
            {
                int windowEnd = Math.Min(start + maxOrig, w);
                if (windowEnd >= w)
                {
                    var sub = CropRaw(crop, start, w - start);
                    int subTw = Math.Max((int)Math.Round(sub.Width / ratio), 1);
                    segs.Add((i, ImageOps.Resize(sub, Math.Min(subTw, maxW), rh), Math.Min(subTw, maxW)));
                    break;
                }
                int? cut = null;
                foreach (var g in gapCenters)
                {
                    if (g > start && g <= windowEnd)
                        cut = g;
                }
                int end = cut ?? windowEnd;
                var sub2 = CropRaw(crop, start, end - start);
                int subTw2 = Math.Max((int)Math.Round(sub2.Width / ratio), 1);
                segs.Add((i, ImageOps.Resize(sub2, Math.Min(subTw2, maxW), rh), Math.Min(subTw2, maxW)));
                if (end >= w)
                    break;
                start = end;
            }
        }

        // 2. 按宽度分桶（桶内宽度差 ≤ 16px、行数 ≤ 24，避免 SVTR O(T²) 被最长行拖慢）
        var items = segs.Select((s, idx) => (Idx: idx, Crop: s.Crop, W: s.TargetW))
            .OrderBy(x => x.W)
            .ToList();
        var batches = new List<List<(int Idx, GrayImage Crop, int W)>>();
        foreach (var it in items)
        {
            bool placed = false;
            foreach (var b in batches)
            {
                int wmax = b.Max(x => x.W);
                if (b.Count < 24 && Math.Abs(it.W - wmax) <= 16)
                {
                    b.Add(it);
                    placed = true;
                    break;
                }
            }
            if (!placed)
                batches.Add(new List<(int, GrayImage, int)> { it });
        }

        // 3. 桶分发给 rec 会话池并行处理；桶按宽度降序 round-robin 分发
        int nt = Math.Min(_recPool.Count, Math.Max(batches.Count, 1));
        batches.Sort((a, b) => b.Max(x => x.W).CompareTo(a.Max(x => x.W)));
        var segResults = new (string Text, float Conf)[segs.Count];
        var tasks = new List<Task<List<(int Idx, string Text, float Conf)>>>();
        for (int wi = 0; wi < nt; wi++)
        {
            int wIndex = wi;
            var chunk = batches.Where((_, i) => i % nt == wIndex).ToList();
            var session = _recPool[wIndex];
            tasks.Add(Task.Run(() =>
            {
                // 每线程独立局部列表，线程结束统一合并
                var local = new List<(int Idx, string Text, float Conf)>();
                foreach (var b in chunk)
                {
                    int bw = Math.Max(b.Max(x => x.W), 1);
                    int wp = ((bw + 7) / 8) * 8;
                    int n = b.Count;
                    var input = new float[3 * rh * wp * n];
                    for (int k = 0; k < n; k++)
                    {
                        var (_, resized, w) = b[k];
                        int baseIdx = k * 3 * rh * wp;
                        for (int y = 0; y < rh; y++)
                        {
                            for (int x = 0; x < w; x++)
                            {
                                float p = resized.Pixels[y * resized.Width + x] / 255.0f;
                                float v = (p - 0.5f) / 0.5f;
                                input[baseIdx + y * wp + x] = v;
                                input[baseIdx + rh * wp + y * wp + x] = v;
                                input[baseIdx + 2 * rh * wp + y * wp + x] = v;
                            }
                        }
                    }
                    var (data, shape) = OnnxSessionFactory.RunSingle(session, _recInput, input, new[] { n, 3, rh, wp });
                    if (shape.Length < 3)
                        continue;
                    int t = shape[1];
                    int c = shape[2];
                    for (int k = 0; k < n; k++)
                    {
                        var (idx, _, _) = b[k];
                        int baseIdx = k * t * c;
                        if (baseIdx + t * c > data.Length)
                            continue;
                        var (text, conf) = Decode.DecodeCtcFrom(data, t, c, _dict, baseIdx);
                        local.Add((idx, text, conf));
                    }
                }
                return local;
            }));
        }
        Task.WaitAll(tasks.Cast<Task>().ToArray());
        foreach (var t in tasks)
        {
            foreach (var (idx, text, conf) in t.Result)
                segResults[idx] = (text, conf);
        }

        // 4. 合并同一原始行的分段结果
        var parts = new List<List<string>>();
        for (int i = 0; i < crops.Count; i++)
            parts.Add([]);
        for (int si = 0; si < segs.Count; si++)
            parts[segs[si].OrigIdx].Add(segResults[si].Text);

        return parts.Select(p => (string.Concat(p), 0.8f)).ToList();
    }

    /// <summary>并行方向分类（cls 会话池）。无 cls 模型返回全 false。</summary>
    private List<bool> ClassifyParallel(List<(float Cx, float Cy, float W, float H, GrayImage Img)> crops)
    {
        int n = crops.Count;
        if (_clsPool.Count == 0)
            return Enumerable.Repeat(false, n).ToList();

        if (n < 4 || _clsPool.Count <= 1)
            return crops.Select(c => ClassifyWithSession(_clsPool[0], c.Img)).ToList();

        int nt = Math.Min(_clsPool.Count, n);
        var outFlags = new bool[n];
        var tasks = new List<Task>();
        for (int wi = 0; wi < nt; wi++)
        {
            int wIndex = wi;
            int start = wi * n / nt;
            int end = (wi + 1) * n / nt;
            var session = _clsPool[wIndex];
            tasks.Add(Task.Run(() =>
            {
                for (int k = start; k < end; k++)
                    outFlags[k] = ClassifyWithSession(session, crops[k].Img);
            }));
        }
        Task.WaitAll(tasks.ToArray());
        return outFlags.ToList();
    }

    /// <summary>方向分类核心：softmax 后判断类别 1（旋转 180°）概率 &gt; 0.9。</summary>
    private bool ClassifyWithSession(InferenceSession session, GrayImage crop)
    {
        var input = Preprocess.PreprocessCls(crop, _clsH, _clsW);
        var (data, _) = OnnxSessionFactory.RunSingle(session, _clsInput, input, new[] { 1, 3, _clsH, _clsW });
        if (data.Length < 2)
            return false;
        float max0 = Math.Max(data[0], 0f);
        float max1 = Math.Max(data[1], 0f);
        float e0 = (float)Math.Exp(max0);
        float e1 = (float)Math.Exp(max1);
        float p1 = e1 / (e0 + e1);
        return p1 > 0.9f;
    }

    private static GrayImage CropRaw(GrayImage src, int x0, int width)
    {
        int h = src.Height;
        var pixels = new byte[width * h];
        for (int y = 0; y < h; y++)
            Array.Copy(src.Pixels, y * src.Width + x0, pixels, y * width, width);
        return new GrayImage(width, h, pixels);
    }

    public void Dispose()
    {
        _det?.Dispose();
        foreach (var s in _recPool)
            s?.Dispose();
        foreach (var s in _clsPool)
            s?.Dispose();
    }
}

/// <summary>极简日志接口（默认空实现；可接入宿主日志）。</summary>
public interface ILogger
{
    void Info(string message);
}

public sealed class NullLogger : ILogger
{
    public static readonly NullLogger Instance = new();
    public void Info(string message) { }
}
