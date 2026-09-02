using System.Text.RegularExpressions;
using Wes.Invoice.Ocr.Abstractions;

namespace Wes.Invoice.Ocr.Detect;

/// <summary>票据类型判定：基于 OCR/PDF 提取文本的启发式分类。</summary>
public static class InvoiceKindDetector
{
    // netstandard2.0 无 [GeneratedRegex]（.NET 7+ 源生成器），改静态 Regex 实例，行为一致
    private static readonly Regex ReTrainNoLine = new(
        @"(?m)^\s*[GCDZYKL]\s*[0-9]{1,4}\s*$",
        RegexOptions.Compiled);

    public static InvoiceKind DetectKind(string text)
    {
        var t = text.Trim();

        // 车辆通行费发票（通行费/入口站/出口站特征）：属增值税发票的一种。
        // 放在最前判定——该类票右上角代码/号码标签常因红字被印章处理抹掉，
        // 但标题"车辆通行费"与"入口站/出口站"几乎总能识别；若漏判会因
        // 流水号（如 K737）被误判成火车票。
        if (t.IndexOf("车辆通行费", StringComparison.Ordinal) >= 0
            || (t.IndexOf("入口站", StringComparison.Ordinal) >= 0
                && t.IndexOf("出口站", StringComparison.Ordinal) >= 0))
        {
            return InvoiceKind.VatInvoice;
        }

        // netstandard2.0 的 Contains 无 StringComparison 重载，用 IndexOf 等价替换
        if (t.IndexOf("发票代码", StringComparison.Ordinal) >= 0
            || t.IndexOf("发票号码", StringComparison.Ordinal) >= 0
            || (t.IndexOf("价税合计", StringComparison.Ordinal) >= 0 && t.IndexOf("税额", StringComparison.Ordinal) >= 0))
        {
            return InvoiceKind.VatInvoice;
        }

        // 车次独立成行（如 "G1024"），或出现车次/乘车日期锚点
        if (t.IndexOf("车次", StringComparison.Ordinal) >= 0
            || t.IndexOf("乘车日期", StringComparison.Ordinal) >= 0
            || ReTrainNoLine.IsMatch(t))
        {
            return InvoiceKind.TrainTicket;
        }

        if (t.IndexOf("行程单", StringComparison.Ordinal) >= 0
            || t.IndexOf("航班号", StringComparison.Ordinal) >= 0
            || t.IndexOf("客票号", StringComparison.Ordinal) >= 0
            || t.IndexOf("承运人", StringComparison.Ordinal) >= 0
            || t.IndexOf("民航发展基金", StringComparison.Ordinal) >= 0)
        {
            return InvoiceKind.FlightItinerary;
        }

        return InvoiceKind.Unknown;
    }
}
