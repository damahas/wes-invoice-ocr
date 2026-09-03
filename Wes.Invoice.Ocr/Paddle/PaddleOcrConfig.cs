namespace Wes.Invoice.Ocr.Paddle;

/// <summary>ONNX Runtime 执行提供方（EP）偏好。</summary>
public enum EpPreference
{
    /// <summary>固定使用 CPU EP（最稳定，默认）。</summary>
    Cpu,

    /// <summary>Windows DirectML EP（含核显的 DX12 设备；仅显式指定时使用，Auto 不探测）。</summary>
    DirectML,

    /// <summary>NVIDIA CUDA EP（需 N 卡独显 + CUDA/cuDNN 环境；不可用回退 CPU）。</summary>
    Cuda,

    /// <summary>自动探测：仅尝试 NVIDIA CUDA，成功用 GPU、失败回退 CPU（不考虑核显）。</summary>
    Auto,
}

/// <summary>PaddleOCR 引擎可调参数。</summary>
public sealed record PaddleOcrConfig
{
    public const int DefaultDetLimit = 1280;
    public const int DefaultRecMaxW = 640;
    public const float DefaultDbThresh = 0.3f;
    public const float DefaultBoxThresh = 0.5f;

    /// <summary>det 输入长边上限：越大对高分辨率/小字发票检测越准，但推理越慢。默认 1280（发票小字多）。</summary>
    public int DetLimit { get; init; } = DefaultDetLimit;

    /// <summary>rec 输入最大宽：越大对超长文本行识别越准，但推理越慢。默认 640（20 位发票号需要）。</summary>
    public int RecMaxW { get; init; } = DefaultRecMaxW;

    /// <summary>DB 后处理二值化阈值（0~1，默认 0.3）。越低保留越多候选区域，误检增加；越高漏检增加。</summary>
    public float DbThresh { get; init; } = DefaultDbThresh;

    /// <summary>检测框最小像素占比阈值（0~1，默认 0.5）。越低对表格线/印章遮挡越容忍，框越碎；越高漏框越多。</summary>
    public float BoxThresh { get; init; } = DefaultBoxThresh;

    /// <summary>推理执行提供方偏好。默认 Auto。</summary>
    public EpPreference Ep { get; init; } = EpPreference.Auto;

    /// <summary>
    /// rec 会话池大小（1~16，默认 4）。**仅在批量模式（<see cref="RecBatch"/> 为 true
    /// 且模型 batch 维度动态）下生效**：批量时多个会话并行处理宽度分桶，每会话按核数分摊线程。
    /// 非批量模式（默认）只建 1 个会话并独占全部核心——非批量路径只用池中的第一个会话，
    /// 多建纯属浪费内存（rec 模型越大越明显）。
    /// </summary>
    public int RecThreads { get; init; } = 4;

    /// <summary>
    /// rec 批量推理（按文本行宽度分桶、多行一次推理）。
    /// 默认 <c>false</c>：实测比逐行慢约 19%（桶内按最宽行 padding，宽度 5~730px 差异大，
    /// padding 浪费的算力超过批处理收益，且 SVTR 为 O(T²)）。
    /// 置 <c>true</c> 仅在模型 batch 维度动态时才真正生效。
    /// </summary>
    public bool RecBatch { get; init; }

    /// <summary>
    /// ROI 区域裁剪：横版发票（宽高比 &gt; 1.3）只识别关键字段区域，可提速。
    /// 默认 <c>false</c>：ROI 按版式硬编码，版式不匹配时会静默丢字段
    /// （实测将销售方税号误填为购买方税号，比漏字段更危险）。仅供调试定位。
    /// </summary>
    public bool RoiEnabled { get; init; }

    /// <summary>
    /// 模型目录（需含 det.onnx / rec.onnx，可选 cls.onnx 与 ppocrv6_dict.txt）。
    /// 作为 <see cref="PaddleOcrEngine"/> 构造参数 modelDir 为空时的回退值；
    /// 两者皆空抛 <see cref="OcrErrorKind.EngineNotConfigured"/>。
    /// </summary>
    public string ModelDir { get; init; } = "";
}
