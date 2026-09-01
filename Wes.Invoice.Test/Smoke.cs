using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Wes.Invoice.Ocr;
using Wes.Invoice.Ocr.Abstractions;
using Wes.Invoice.Ocr.Algorithms;
using Wes.Invoice.Ocr.Imaging;
using Wes.Invoice.Ocr.Paddle;

namespace Wes.Invoice.Test;

/// <summary>
/// 端到端冒烟：跑完整流水线（真实模型 + 图片），支持 --debug 打印 det/rec 的 shape 与概率统计。
/// 入口由 Program.cs 的 `-- smoke` 参数分流。
/// </summary>
internal static class Smoke
{
    public static int Run(string[] args)
    {
        // 模型目录可省略：缺省用运行目录下 models/（构建时已自动从仓库根复制）
        var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
        string modelDir = positional.Count > 0
            ? positional[0]
            : Path.Combine(AppContext.BaseDirectory, "models");
        string imagePath = ResolveImagePath(args);
        bool debug = args.Contains("--debug");

        if (!Directory.Exists(modelDir))
        {
            Console.Error.WriteLine($"模型目录不存在: {modelDir}");
            Console.Error.WriteLine("用法: dotnet run --project Wes.Invoice.Test -- smoke [模型目录] [图片路径] [--debug]");
            Console.Error.WriteLine("模型目录省略时默认取运行目录下 models/（构建自动复制）。");
            return 1;
        }

        if (!File.Exists(imagePath))
        {
            Console.Error.WriteLine($"图片不存在: {imagePath}");
            Console.Error.WriteLine("请传入图片路径，");
            Console.Error.WriteLine($"或将图片放置为运行目录下的 {Path.Combine("Assets", DefaultImageName)}。");
            return 1;
        }

        try
        {
            if (debug)
            {
                DebugRun(modelDir, imagePath);
                return 0;
            }

            using var engine = new PaddleOcrEngine(modelDir, new PaddleOcrConfig { Ep = EpPreference.Cuda });
            var svc = new InvoiceOcrService(engine, qrDecoder: new Wes.Invoice.Ocr.Qr.ZxingQrDecoder());

            Console.WriteLine($"引擎: {svc.EngineName}  生效 EP: {engine.EpName}");
            var bytes = File.ReadAllBytes(imagePath);

            var sw = Stopwatch.StartNew();
            var invoice = svc.RecognizeImageBytes(bytes);
            sw.Stop();
            Console.WriteLine($"识别耗时: {sw.Elapsed.TotalSeconds:F2} s");

            Console.WriteLine($"票据类型: {invoice.Kind.ToWireString()}");
            foreach (var f in invoice.Fields)
                Console.WriteLine($"  {f.Label,-8} [{f.Key}] = {f.Value}");

            var v = invoice.Verification;
            Console.WriteLine($"\n二维码校验: {v?.Status ?? Wes.Invoice.Ocr.Abstractions.QrStatus.NotScanned}");
            foreach (var m in v?.Matched ?? [])
                Console.WriteLine($"  ✓ {m.Key}: 二维码[{m.QrValue}] == OCR[{m.OcrValue}]");
            foreach (var c in v?.Conflicts ?? [])
                Console.WriteLine($"  ✗ {c.Key}: 二维码[{c.QrValue}] != OCR[{c.OcrValue}]");
            Console.WriteLine("\n--- 原始文本 ---");
            Console.WriteLine(invoice.RawText[..Math.Min(invoice.RawText.Length, 1500)]);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误: {ex.Message}");
            if (ex is Wes.Invoice.Ocr.Abstractions.OcrException oe)
                Console.Error.WriteLine($"分类: {oe.Kind}");
            return 1;
        }
    }

    const string DefaultImageName = "test_invoice.png";

    /// <summary>
    /// 图片路径解析：命令行参数 &gt; 运行目录下 Assets/test_invoice.png。
    /// 行为完全由命令行入参决定，避免隐式全局状态。
    /// 用运行目录（AppContext.BaseDirectory）拼接，避免硬编码绝对路径。
    /// </summary>
    static string ResolveImagePath(string[] args)
    {
        // 第一个非选项参数即图片路径（--debug 等选项可出现在任意位置，不能按固定下标取）
        var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
        return positional.Count > 1
            ? positional[1]
            : Path.Combine(AppContext.BaseDirectory, "Assets", DefaultImageName);
    }

    static void DebugRun(string modelDir, string imagePath)
    {
        var cfg = new PaddleOcrConfig { Ep = EpPreference.Cpu };
        using var det = new InferenceSession(Path.Combine(modelDir, "det.onnx"), new SessionOptions());
        Console.WriteLine("det 输入名: " + string.Join(", ", det.InputMetadata.Keys));
        foreach (var kv in det.InputMetadata)
            Console.WriteLine($"  {kv.Key}: dims=[{string.Join(", ", kv.Value.Dimensions)}]");
        Console.WriteLine("det 输出名: " + string.Join(", ", det.OutputMetadata.Keys));
        foreach (var kv in det.OutputMetadata)
            Console.WriteLine($"  {kv.Key}: dims=[{string.Join(", ", kv.Value.Dimensions)}]");

        var img = ImageSharpImageDecoder.DecodeGray(File.ReadAllBytes(imagePath));
        Console.WriteLine($"图片: {img.Width}x{img.Height}");

        var (t, dh, dw, rx, ry, ox, oy) = Preprocess.PreprocessDetDyn(img, 960);
        Console.WriteLine($"det 输入: {dh}x{dw}, ratio={rx}");
        var detInput = det.InputMetadata.Keys.First();
        using var results = det.Run(new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(detInput,
                new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<float>(t, new[] { 1, 3, dh, dw }))
        });
        var output = results.First().AsTensor<float>();
        var dims = output.Dimensions.ToArray();
        Console.WriteLine($"det 输出 shape: [{string.Join(", ", dims)}]");
        var arr = output.ToArray();
        float min = arr.Min(), max = arr.Max(), sum = 0;
        foreach (var v in arr) sum += v;
        Console.WriteLine($"概率值: min={min:F4} max={max:F4} mean={sum / arr.Length:F4}");

        // 用输出 shape 取概率图
        var shape = dims;
        int mh = shape[^2], mw = shape[^1];
        var map = Decode.ExtractScoreMap(arr, shape, mh, mw);
        var quads = DetPost.DetectBoxes(map, mh, mw, 1.0f / rx, 1.0f / ry, ox, oy);
        Console.WriteLine($"检测框数: {quads.Count}");

        // 全量框尺寸分析：为“框过滤”阈值提供数据依据（不是拍脑袋定值）
        AnalyzeBoxes(quads, img.Width, img.Height);

        ProfilePipeline(modelDir, img, quads.Count);
    }

    /// <summary>输出全部检测框的尺寸分布，并按候选阈值估算可过滤比例。</summary>
    static void AnalyzeBoxes(IReadOnlyList<Quad> quads, int imgW, int imgH)
    {
        Console.WriteLine("\n--- 检测框尺寸分析 ---");
        var rows = quads.Select(q =>
        {
            var (cx, cy) = Geometry.Center(q);
            var (w, h) = Geometry.QuadSize(q);
            return (Cx: cx, Cy: cy, W: w, H: h);
        }).OrderBy(r => r.W).ToList();

        Console.WriteLine("  按宽度升序（前 20 个最小框，多为噪声碎片）:");
        foreach (var r in rows.Take(20))
            Console.WriteLine($"    w={r.W,6:F1} h={r.H,5:F1} cx={r.Cx,6:F0} cy={r.Cy,5:F0}");

        Console.WriteLine($"  宽度分布: min={rows.Min(r => r.W):F1} 中位数={Median(rows.Select(r => (long)r.W).ToList())} " +
                          $"max={rows.Max(r => r.W):F1}");
        Console.WriteLine($"  高度分布: min={rows.Min(r => r.H):F1} 中位数={Median(rows.Select(r => (long)r.H).ToList())} " +
                          $"max={rows.Max(r => r.H):F1}");

        // 候选阈值：估算各阈值下会被过滤掉多少框（用于权衡收益与风险）
        Console.WriteLine("  候选阈值 -> 可过滤框数（占比）:");
        foreach (var (minW, minH) in new[] { (8f, 8f), (12f, 10f), (16f, 12f), (20f, 14f), (24f, 16f) })
        {
            int dropped = rows.Count(r => r.W < minW || r.H < minH);
            Console.WriteLine($"    w>{minW:F0} 且 h>{minH:F0} -> 过滤 {dropped}/{rows.Count} ({100.0 * dropped / rows.Count:F0}%)");
        }
    }

    /// <summary>分段计时：定位端到端耗时的构成（仅 --debug 模式执行，不影响生产路径）。</summary>
    static void ProfilePipeline(string modelDir, GrayImage img, int boxCount)
    {
        Console.WriteLine("\n--- 分段计时（CPU）---");
        // 传入 logger 让引擎输出 det/cls/rec 各阶段占比（生产传 null 时零开销）
        using var engine = new PaddleOcrEngine(modelDir, new PaddleOcrConfig { Ep = EpPreference.Cpu }, new ConsoleLogger());

        // 预热：首次推理含会话初始化与 JIT，单独跑一次排除
        var warm = Stopwatch.StartNew();
        engine.RecognizeImage(img);
        warm.Stop();
        Console.WriteLine($"  预热（首次，含初始化）: {warm.ElapsedMilliseconds} ms  [框数 {boxCount}]");

        // EP 对比：GPU(DirectML) vs CPU，判断 GPU 是否可用及提速幅度
        ProfileEp(modelDir, img);

        // 批量 vs 逐行交替测量：避免系统负载波动把结论带偏（两种模式各跑 3 次）。
        // 两种模式由 PaddleOcrConfig.RecBatch 决定（构造时固定），故各建一个引擎实例。
        var batch = new List<long>();
        var single = new List<long>();

        using var engineSingle = new PaddleOcrEngine(modelDir, new PaddleOcrConfig { Ep = EpPreference.Cpu, RecBatch = false });
        using var engineBatch = new PaddleOcrEngine(modelDir, new PaddleOcrConfig { Ep = EpPreference.Cpu, RecBatch = true });
        engineSingle.RecognizeImage(img); // 预热
        engineBatch.RecognizeImage(img);  // 预热

        for (int i = 0; i < 3; i++)
        {
            var swS = Stopwatch.StartNew();
            engineSingle.RecognizeImage(img);
            swS.Stop();
            single.Add(swS.ElapsedMilliseconds);

            var swB = Stopwatch.StartNew();
            engineBatch.RecognizeImage(img);
            swB.Stop();
            batch.Add(swB.ElapsedMilliseconds);
        }

        var medS = Median(single);
        var medB = Median(batch);
        Console.WriteLine($"  逐行 rec (RecBatch=false): {string.Join(", ", single)} ms  中位数 {medS} ms");
        Console.WriteLine($"  批量 rec (RecBatch=true):  {string.Join(", ", batch)} ms  中位数 {medB} ms");
        // 注意用浮点比较：整数除法会把 28636/35243 直接截断为 0
        var ratio = medS > 0 ? (double)medB / medS : 0;
        var verdict = ratio > 1.1 ? "批量反而更慢" : ratio < 0.9 ? "批量有效提速" : "基本持平（差异在噪声内）";
        Console.WriteLine($"  批量/逐行 = {ratio:F2}x  -> {verdict}");
        Console.WriteLine($"  离散度: 逐行 {Spread(single)}  批量 {Spread(batch)}  (若过大说明测量受系统负载干扰，结论不可靠)");
    }

    static long Median(List<long> v)
    {
        var s = v.OrderBy(x => x).ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }

    /// <summary>离散度（最大/最小），用于判断测量是否可信。</summary>
    static string Spread(List<long> v) => v.Count == 0 ? "n/a" : $"{(double)v.Max() / Math.Max(v.Min(), 1):F2}x";

    /// <summary>对比 CPU 与 GPU(DirectML) 的端到端耗时，判断 GPU EP 是否可用且值得启用。</summary>
    static void ProfileEp(string modelDir, GrayImage img)
    {
        Console.WriteLine("\n--- EP 对比（Auto vs CPU）---");

        // Auto 只尝试 N 卡 CUDA，失败回退 CPU；实际生效的 EP 由引擎加载日志输出
        using var gpu = new PaddleOcrEngine(modelDir, new PaddleOcrConfig { Ep = EpPreference.Auto }, new ConsoleLogger());

        gpu.RecognizeImage(img); // 预热
        var g = new List<long>();
        for (int i = 0; i < 2; i++)
        {
            var sw = Stopwatch.StartNew();
            gpu.RecognizeImage(img);
            sw.Stop();
            g.Add(sw.ElapsedMilliseconds);
        }
        var gMed = Median(g);
        Console.WriteLine($"  Ep=Auto 实际生效 EP: {gpu.EpName}（CUDA 表示已走 N 卡，CPU 表示已回退）");
        Console.WriteLine($"  Ep=Auto 耗时: {string.Join(", ", g)} ms  中位数 {gMed} ms");
        Console.WriteLine("  （对比上方 CPU 分段计时的中位数值）");
    }

    private sealed class ConsoleLogger : Wes.Invoice.Ocr.Paddle.ILogger
    {
        public void Info(string message) => Console.WriteLine("    " + message);
    }
}
