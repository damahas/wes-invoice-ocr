using System.Globalization;
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
    /// <summary>购/销方名称：排除"项目名称/货物名称/商品名称/服务名称"等表头误匹配；
    /// 要求"名称"后有冒号；"购名称"（OCR 拆字）等前缀不排除。</summary>
    private static readonly Regex ReName = new(
        @"(?<!项目)(?<!货物)(?<!商品)(?<!服务)名\s*称\s*[:：]\s*([^\s，,；;]{2,60})",
        RegexOptions.Compiled);
    private static readonly Regex ReTaxNo = new(
        @"(?:纳税人识别号|统一社会信用代码|纳税人识别\s*号)\s*[:：]?\s*([0-9A-Za-z]{15,20})",
        RegexOptions.Compiled);
    private static readonly Regex ReAmountWithDecimals = new(
        @"([0-9][0-9,]*\.[0-9]{1,2})",
        RegexOptions.Compiled);
    private static readonly Regex ReTotal = new(
        @"(?:价\s*税)?合\s*计\s*(?:金\s*额)?\s*[:：]?\s*[¥￥]?\s*([0-9][0-9,]*(?:\.[0-9]{1,2})?)",
        RegexOptions.Compiled);
    private static readonly Regex ReTax = new(
        @"税\s*额\s*[:：]?\s*[¥￥]?\s*([0-9][0-9,]*(?:\.[0-9]{1,2})?)",
        RegexOptions.Compiled);
    /// <summary>中文大写金额：如"叁佰圆整"（300.00）、"壹万贰仟叁佰肆拾伍元陆角柒分"（12345.67）。</summary>
    private static readonly Regex ReCnAmount = new(
        @"(?<int>[零壹贰叁肆伍陆柒捌玖一二三四五六七八九十百千万亿拾佰仟]*)(?:圆|元)" +
        @"(?:整|(?:(?<jiao>[零壹贰叁肆伍陆柒捌玖一二三四五六七八九])\s*角)?" +
        @"(?:(?<fen>[零壹贰叁肆伍陆柒捌玖一二三四五六七八九])\s*分)?)",
        RegexOptions.Compiled);
    private static readonly Dictionary<char, int> CnDigits = new()
    {
        ['零'] = 0, ['壹'] = 1, ['贰'] = 2, ['叁'] = 3, ['肆'] = 4,
        ['伍'] = 5, ['陆'] = 6, ['柒'] = 7, ['捌'] = 8, ['玖'] = 9,
        ['一'] = 1, ['二'] = 2, ['三'] = 3, ['四'] = 4, ['五'] = 5,
        ['六'] = 6, ['七'] = 7, ['八'] = 8, ['九'] = 9,
    };
    private static readonly Dictionary<char, long> CnUnits = new()
    {
        ['拾'] = 10, ['佰'] = 100, ['仟'] = 1000,
        ['十'] = 10, ['百'] = 100, ['千'] = 1000,
        ['万'] = 10000, ['亿'] = 100000000,
    };

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

    /// <summary>价税合计：取"小写"后一小段内第一个带两位小数的金额（"小写"容忍拆字）；
    /// 小写缺失时回退中文大写金额（如"叁佰圆整"→300.00，det 表格漏检时大写常能检出）。</summary>
    private static string? TotalAmountWithTax(string text)
    {
        var dm = Regex.Match(text, @"小\s*写");
        if (dm.Success)
        {
            int idx = dm.Index + dm.Length;
            var tail = text.Substring(idx, Math.Min(80, text.Length - idx));
            var m = ReAmountWithDecimals.Match(tail);
            if (m.Success)
                return m.Groups[1].Value;
        }
        return ParseCnAmount(text);
    }

    /// <summary>中文大写金额 → "0.00" 字符串；解析失败返回 null。</summary>
    private static string? ParseCnAmount(string text)
    {
        var m = ReCnAmount.Match(text);
        if (!m.Success)
            return null;
        long yuan = ParseCnInt(m.Groups["int"].Value);
        decimal jiao = m.Groups["jiao"].Success ? CnDigits[m.Groups["jiao"].Value[0]] : 0;
        decimal fen = m.Groups["fen"].Success ? CnDigits[m.Groups["fen"].Value[0]] : 0;
        decimal v = yuan + jiao / 10m + fen / 100m;
        return v.ToString("0.00", CultureInfo.InvariantCulture);
    }

    /// <summary>中文大写整数 → long（支持 拾佰仟 与 万/亿 节进位）。</summary>
    private static long ParseCnInt(string s)
    {
        long total = 0, section = 0, num = 0;
        foreach (char c in s)
        {
            if (CnDigits.TryGetValue(c, out int d))
            {
                num = d;
                continue;
            }
            if (!CnUnits.TryGetValue(c, out long u))
                continue;
            if (u == 100000000)
            {
                total = (total + section + num) * 100000000;
                section = 0;
                num = 0;
            }
            else if (u == 10000)
            {
                section = (section + num) * 10000;
                num = 0;
            }
            else
            {
                section += (num == 0 ? 1 : num) * u;
                num = 0;
            }
        }
        return total + section + num;
    }

    /// <summary>合计金额：匹配"合计 ¥X"，排除"价税合计 ¥X"（价税合计有专门锚点；"价"字开头即排除，容忍拆字）。</summary>
    private static string? TotalAmount(string text)
    {
        foreach (Match c in ReTotal.Matches(text))
        {
            if (c.Value.TrimStart().StartsWith("价", StringComparison.Ordinal))
                continue;
            return c.Groups[1].Value;
        }
        return null;
    }
}
