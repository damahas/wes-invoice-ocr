using System.Text.RegularExpressions;
using Wes.Invoice.Ocr.Abstractions;

namespace Wes.Invoice.Ocr.Parsers;

/// <summary>增值税发票解析器（普通 / 专用 / 电子发票）。</summary>
public sealed class VatParser : IInvoiceParser
{
    public InvoiceKind Kind => InvoiceKind.VatInvoice;

    // netstandard2.0 无 [GeneratedRegex]（.NET 7+ 源生成器），改静态 Regex 实例，行为一致
    private static readonly Regex ReCode = new(
        @"发票\s*[代弋]\s*码\s*[:：]?\s*([0-9A-Za-z\-]{6,})",
        RegexOptions.Compiled);
    private static readonly Regex ReNo = new(
        @"发票\s*号码\s*[:：]?\s*([0-9A-Za-z\-]{5,})",
        RegexOptions.Compiled);
    private static readonly Regex ReDate = new(
        @"开票\s*[日目]\s*期\s*[:：]?\s*([0-9]{4})\s*[年\-./]\s*([0-9]{1,2})\s*[月\-./]\s*([0-9]{1,2})\s*[日目]?",
        RegexOptions.Compiled);
    private static readonly Regex ReName = new(
        @"名\s*称\s*[:：]?\s*([^\s，,；;]{2,60})",
        RegexOptions.Compiled);
    private static readonly Regex ReTaxNo = new(
        @"(?:纳税人识别号|统一社会信用代码|纳税人识别\s*号)\s*[:：]?\s*([0-9A-Za-z]{15,20})",
        RegexOptions.Compiled);
    private static readonly Regex ReAmountWithDecimals = new(
        @"([0-9][0-9,]*\.[0-9]{1,2})",
        RegexOptions.Compiled);
    private static readonly Regex ReTotal = new(
        @"(?:价税)?合计\s*(?:金额)?\s*[:：]?\s*[¥￥]?\s*([0-9][0-9,]*(?:\.[0-9]{1,2})?)",
        RegexOptions.Compiled);
    private static readonly Regex ReTax = new(
        @"税\s*额\s*[:：]?\s*[¥￥]?\s*([0-9][0-9,]*(?:\.[0-9]{1,2})?)",
        RegexOptions.Compiled);

    public IReadOnlyList<FieldValue> Parse(string text)
    {
        var fields = new List<FieldValue>();

        ParserHelpers.Push(fields, "invoice_code", "发票代码", ParserHelpers.Cap1(ReCode, text));
        ParserHelpers.Push(fields, "invoice_no", "发票号码", ParserHelpers.Cap1(ReNo, text));

        var dm = ReDate.Match(text);
        if (dm.Success)
            ParserHelpers.Push(fields, "invoice_date", "开票日期", $"{dm.Groups[1].Value}年{dm.Groups[2].Value}月{dm.Groups[3].Value}日");

        // 购方 / 销方名称：按出现顺序取前两个
        var names = ReName.Matches(text).Cast<Match>().Select(m => m.Groups[1].Value.Trim()).ToList();
        ParserHelpers.Push(fields, "buyer_name", "购买方名称", names.Count > 0 ? names[0] : null);
        ParserHelpers.Push(fields, "seller_name", "销售方名称", names.Count > 1 ? names[1] : null);

        // 纳税人识别号（购/销各一个，按顺序）
        var taxNos = ReTaxNo.Matches(text).Cast<Match>().Select(m => m.Groups[1].Value.Trim()).ToList();
        ParserHelpers.Push(fields, "buyer_tax_no", "购买方税号", taxNos.Count > 0 ? taxNos[0] : null);
        ParserHelpers.Push(fields, "seller_tax_no", "销售方税号", taxNos.Count > 1 ? taxNos[1] : null);

        ParserHelpers.Push(fields, "total_amount", "合计金额", TotalAmount(text));
        ParserHelpers.Push(fields, "total_tax", "税额", ParserHelpers.Cap1(ReTax, text));
        ParserHelpers.Push(fields, "total_amount_with_tax", "价税合计", TotalAmountWithTax(text));

        return fields;
    }

    /// <summary>价税合计：取"小写"后一小段内第一个带两位小数的金额。</summary>
    private static string? TotalAmountWithTax(string text)
    {
        int idx = text.IndexOf("小写", StringComparison.Ordinal);
        if (idx < 0)
            return null;
        idx += "小写".Length;
        var tail = text.Substring(idx, Math.Min(80, text.Length - idx));
        var m = ReAmountWithDecimals.Match(tail);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>合计金额：匹配"合计 ¥X"，排除"价税合计 ¥X"（价税合计有专门锚点）。</summary>
    private static string? TotalAmount(string text)
    {
        foreach (Match c in ReTotal.Matches(text))
        {
            if (c.Value.TrimStart().StartsWith("价税", StringComparison.Ordinal))
                continue;
            return c.Groups[1].Value;
        }
        return null;
    }
}
