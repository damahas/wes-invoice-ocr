using System.Buffers;

namespace Wes.Invoice.Ocr.Abstractions;

/// <summary>
/// 灰度图像（8-bit，row-major）。
/// 契约层定义，Imaging 层负责解码产生，Algorithms / Paddle 层消费。
/// </summary>
public sealed class GrayImage
{
    /// <summary>像素数据（Gray8，row-major，长度 = Width * Height）。</summary>
    public byte[] Pixels { get; }

    public int Width { get; }

    public int Height { get; }

    public GrayImage(int width, int height, byte[]? pixels = null)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException($"图像尺寸非法: {width}x{height}");

        Width = width;
        Height = height;
        Pixels = pixels ?? new byte[width * height];
        if (Pixels.Length != width * height)
            throw new ArgumentException($"像素数组长度 {Pixels.Length} 与 {width}x{height} 不匹配");
    }

    public byte GetPixel(int x, int y) => Pixels[y * Width + x];

    public void SetPixel(int x, int y, byte v) => Pixels[y * Width + x] = v;

    /// <summary>解码字节数组为灰度图（ImageSharp 实现，放 Imaging 层）。</summary>
    public static GrayImage FromBytes(byte[] data) =>
        Imaging.ImageSharpImageDecoder.DecodeGray(data);
}
