using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Wes.Invoice.Ocr.Abstractions;

namespace Wes.Invoice.Ocr.Paddle;

/// <summary>ONNX Runtime 会话工厂：EP 配置、intra 线程、维度探测、词典加载。</summary>
internal static class OnnxSessionFactory
{
    /// <summary>
    /// 诊断用：最近一次 <see cref="Load"/> 实际生效的 EP 名称。
    /// 原实现静默吞掉 EP 异常回退 CPU，导致无法判断 GPU 是否真的启用，故在此暴露。
    /// 会话均在引擎构造函数内串行创建，无并发写入。
    /// </summary>
    public static string LastEpName { get; private set; } = "CPU";

    /// <summary>加载推理会话，按 EP 偏好配置执行提供方（失败自动回退 CPU）。</summary>
    public static InferenceSession Load(string modelPath, int intraThreads, EpPreference ep)
    {
        // 先置为 CPU，由 TryAppend 成功后改写，确保记录的是真实生效的 EP
        LastEpName = "CPU";

        var opts = CreateOptions(intraThreads);
        switch (ep)
        {
            case EpPreference.DirectML:
                TryAppend(opts, s => s.AppendExecutionProvider_DML(), "DirectML");
                break;
            case EpPreference.Cuda:
            case EpPreference.Auto:
                // 只处理 NVIDIA 独显：先探测 CUDA 运行库，存在才尝试 CUDA EP，否则直接回退 CPU。
                // 避免无 CUDA 环境时 ORT 打印 "cublasLt64_13.dll missing" 红色报错（C 层日志无法抑制）。
                // 不考虑核显（DirectML/DX12 集成显卡），故 Auto 不再探测 DirectML。
                if (CudaAvailable)
                    TryAppend(opts, s => s.AppendExecutionProvider_CUDA(), "CUDA");
                else if (!_cudaWarned)
                {
                    _cudaWarned = true; // 只提示一次，避免每次建会话刷屏
                    Console.Error.WriteLine($"[OnnxSessionFactory] 未检测到 CUDA 13 运行库（{CudaLibs.Cublas} / {CudaLibs.Cudnn}），CUDA EP 跳过，回退 CPU。安装 CUDA 13 + cuDNN 9 后可启用 GPU。");
                }
                break;
        }

        try
        {
            return new InferenceSession(modelPath, opts);
        }
        catch (Exception ex) when (LastEpName != "CPU")
        {
            // CUDA EP 注册成功但会话创建失败（缺 CUDA/cuDNN 原生库、驱动不匹配等）→ 回退 CPU 重试
            Console.Error.WriteLine($"[OnnxSessionFactory] {LastEpName} EP 初始化失败，回退 CPU：{ex.Message}");
            LastEpName = "CPU";
            return new InferenceSession(modelPath, CreateOptions(intraThreads));
        }
    }

    private static SessionOptions CreateOptions(int intraThreads)
    {
        var opts = new SessionOptions();
        if (intraThreads >= 1)
            opts.AddSessionConfigEntry("session.intra_op.num_threads", intraThreads.ToString());
        return opts;
    }

    // ORT 1.29 GPU 包（Microsoft.ML.OnnxRuntime.GPU）的 CUDA EP 依赖 CUDA 13 与 cuDNN 9。
    // 库名平台相关：Windows 为 .dll，Linux 为 .so（带 soname 版本号）；macOS 无 CUDA EP。
    // 若 ORT 版本升级需同步这些库名。
    private static readonly (string Cublas, string Cudnn) CudaLibs = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? ("cublasLt64_13.dll", "cudnn64_9.dll")
        : ("libcublasLt.so.13", "libcudnn.so.9");

    /// <summary>CUDA 运行库探测结果，进程内只探测一次（引擎构造串行，无并发问题）。</summary>
    private static readonly bool CudaAvailable = TryProbeCuda();

    private static bool _cudaWarned;

    // netstandard2.0 无 NativeLibrary，用平台 P/Invoke 探测（与 ORT 相同的系统库搜索路径）
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string lpFileName);
    [DllImport("kernel32", SetLastError = true)]
    private static extern int FreeLibrary(IntPtr hModule);
    [DllImport("libdl", SetLastError = true)]
    private static extern IntPtr dlopen(string filename, int flags);
    [DllImport("libdl", SetLastError = true)]
    private static extern int dlclose(IntPtr handle);
    private const int RtlLazy = 1; // RTLD_LAZY

    private static IntPtr TryLoadNative(string lib) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? LoadLibrary(lib) : dlopen(lib, RtlLazy);

    private static void FreeNative(IntPtr h)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            FreeLibrary(h);
        else
            dlclose(h);
    }

    /// <summary>探测 CUDA 运行库（cublas + cuDNN）是否可加载；与 ORT 相同的搜索路径，探测通过即 ORT 可用。</summary>
    private static bool TryProbeCuda()
    {
        // macOS 无 CUDA EP（Apple 已弃用 CUDA），直接回退 CPU
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return false;

        var h1 = TryLoadNative(CudaLibs.Cublas);
        if (h1 == IntPtr.Zero)
            return false;
        var h2 = TryLoadNative(CudaLibs.Cudnn);
        if (h2 == IntPtr.Zero)
        {
            FreeNative(h1);
            return false;
        }
        FreeNative(h1);
        FreeNative(h2);
        return true;
    }

    private static void TryAppend(SessionOptions opts, Action<SessionOptions> append, string epName)
    {
        try
        {
            append(opts);
            LastEpName = epName; // append 成功才记录；抛异常则保持 CPU
        }
        catch
        {
            // EP 不可用（缺驱动/缺原生库）时静默回退 CPU EP
        }
    }

    /// <summary>探测模型输入维度；返回 (h, w)，null 表示动态。</summary>
    public static (int? H, int? W) InputHw(InferenceSession session)
    {
        foreach (var meta in session.InputMetadata.Values)
        {
            var dims = meta.Dimensions; // int[]，动态维度为 -1
            if (dims.Length >= 3)
            {
                int? h = dims[dims.Length - 2] < 0 ? null : dims[dims.Length - 2];
                int? w = dims[dims.Length - 1] < 0 ? null : dims[dims.Length - 1];
                return (h, w);
            }
        }
        return (null, null);
    }

    /// <summary>探测输入 batch 维度是否动态（支持批量推理）。</summary>
    public static bool InputBatchDynamic(InferenceSession session)
    {
        foreach (var meta in session.InputMetadata.Values)
        {
            var dims = meta.Dimensions;
            if (dims.Length >= 1 && dims[0] < 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 从模型 metadata 读取内嵌字符集（PP-OCRv6 的 "character" 字段），无则返回 null。
    /// RapidOCR 写入时按行拼接（PaddleOCR read_txt 按行读，含首行空行 blank），需按 '\n' 切分。
    /// </summary>
    public static List<string>? MetadataCharacters(InferenceSession session)
    {
        try
        {
            if (session.ModelMetadata.CustomMetadataMap.TryGetValue("character", out var chars))
                return chars.Split('\n').ToList();
        }
        catch
        {
            // 某些模型无 metadata，忽略
        }
        return null;
    }

    /// <summary>从字典文件加载字符集（每行一个字符，index 0 为 blank）。</summary>
    public static List<string> LoadDictFile(string path)
    {
        var lines = File.ReadAllLines(path);
        var list = new List<string> { "" }; // index 0 = blank
        list.AddRange(lines.Where(l => !string.IsNullOrWhiteSpace(l)));
        return list;
    }

    /// <summary>构造输入张量并打包为 NamedOnnxValue。</summary>
    public static NamedOnnxValue MakeInput(string inputName, float[] data, int[] dims) =>
        NamedOnnxValue.CreateFromTensor(inputName,
            new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<float>(data, dims));

    /// <summary>运行推理并取第一个输出的 float 数组与形状。</summary>
    public static (float[] Data, int[] Shape) RunSingle(InferenceSession session, string inputName, float[] data, int[] dims)
    {
        var inputs = new List<NamedOnnxValue> { MakeInput(inputName, data, dims) };
        using var results = session.Run(inputs);
        using var output = results.First();
        return (output.AsTensor<float>().ToArray(), output.AsTensor<float>().Dimensions.ToArray());
    }
}
