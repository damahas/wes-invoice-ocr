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
    public const int DefaultDetLimit = 960;
    public const int DefaultRecMaxW = 320;

    /// <summary>det 输入长边上限：越大对高分辨率/小字发票检测越准，但推理越慢。</summary>
    public int DetLimit { get; init; } = DefaultDetLimit;

    /// <summary>rec 输入最大宽：越大对超长文本行识别越准，但推理越慢。</summary>
    public int RecMaxW { get; init; } = DefaultRecMaxW;

    /// <summary>推理执行提供方偏好。默认 Auto。</summary>
    public EpPreference Ep { get; init; } = EpPreference.Auto;

    /// <summary>rec 会话池大小（1~16，默认 4）：CPU+SVTR 模型多会话并行识别可数倍提速。</summary>
    public int RecThreads { get; init; } = 4;

    /// <summary>
    /// 模型目录（需含 det.onnx / rec.onnx，可选 cls.onnx 与 ppocrv6_dict.txt）。
    /// 作为 <see cref="PaddleOcrEngine"/> 构造参数 modelDir 为空时的回退值；
    /// 两者皆空抛 <see cref="OcrErrorKind.EngineNotConfigured"/>。
    /// </summary>
    public string ModelDir { get; init; } = "";
}
