using System.Text.RegularExpressions;
using Wes.Invoice.Ocr.Abstractions;

namespace Wes.Invoice.Ocr.Qr;

/// <summary>
/// 二维码内容解析：从原始文本提取规范化字段（key 与 FieldValue 对齐）。
/// 支持两类：
/// 1. 数电票/电子发票查验 URL（inv-veri.chinatax.gov.cn?lx=&fphm=&kprq=&jshj=&bmxx=）——高置信，全字段。
/// 2. 非 URL 回退——只提取结构性数字（发票代码 10/12 位、发票号码 8 位），
///    日期/金额不猜测（避免误提取导致校验误报）。
/// </summary>
public static class QrDataParser
{
    private static readonly Regex UrlRx = new(
        @"https?://[^\s""']+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex InvoiceNoRx = new(
        @"(?:发票号码|号码|NO|No\.?)[:：]?\s*(\d{8})|(?<![0-9])\d{8}(?![0-9])",
        RegexOptions.Compiled);

    private static readonly Regex InvoiceCodeRx = new(
        @"(?:发票代码|代码|CODE)[:：]?\s*(\d{10,12})|(?<![0-9])\d{12}(?![0-9])|(?<![0-9])\d{10}(?![0-9])",
        RegexOptions.Compiled);

    public static QrData Parse(string content)
    {
        if (content is null) throw new ArgumentNullException(nameof(content));
        var text = content.Trim();
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        var url = ExtractVerifyUrl(text);
        if (url is not null)
        {
            // 高置信模式：查验平台参数直接映射
            foreach (var kvp in url)
            {
                var key = kvp.Key;
                var value = kvp.Value;
                if (string.IsNullOrWhiteSpace(value))
                    continue;
                switch (key.ToLowerInvariant())
                {
                    case "fphm": fields["invoice_no"] = value; break;
                    case "kprq": fields["invoice_date"] = NormalizeDate(value); break;
                    case "jshj": fields["total_amount_with_tax"] = value; break;
                    case "lx": fields["invoice_type"] = value; break;
                }
            }
        }
        else
        {
            // 回退模式：只提取高置信结构性数字
            var no = InvoiceNoRx.Match(text);
            if (no.Success)
                fields["invoice_no"] = no.Groups[1].Success ? no.Groups[1].Value : no.Value;
            var code = InvoiceCodeRx.Match(text);
            if (code.Success)
                fields["invoice_code"] = code.Groups[1].Success ? code.Groups[1].Value : code.Value;
        }

        return new QrData(text, fields);
    }

    /// <summary>提取查验 URL 的查询参数（已 URL 解码），非查验链接返回 null。</summary>
    static IReadOnlyDictionary<string, string>? ExtractVerifyUrl(string text)
    {
        var m = UrlRx.Match(text);
        if (!m.Success)
            return null;

        var url = m.Value;
        var q = url.IndexOf('?');
        if (q < 0)
            return null;

        // 只认税务局查验域名，避免任意 URL 参数被误用
        if (url.IndexOf("inv-veri.chinatax.gov.cn", StringComparison.OrdinalIgnoreCase) < 0
            && url.IndexOf("veri.chinatax.gov.cn", StringComparison.OrdinalIgnoreCase) < 0)
            return null;

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in url.Substring(q + 1).Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0)
                continue;
            var k = Uri.UnescapeDataString(pair.Substring(0, eq));
            var v = Uri.UnescapeDataString(pair.Substring(eq + 1));
            if (k.Length > 0)
                dict[k] = v;
        }
        return dict;
    }

    /// <summary>归一化日期：2024-05-20 / 2024年05月20日 / 20240520 → 20240520。</summary>
    static string NormalizeDate(string value)
    {
        var m = Regex.Match(value.Trim(), @"(\d{4})\D*(\d{1,2})\D*(\d{1,2})");
        return m.Success
            ? $"{m.Groups[1].Value}{m.Groups[2].Value.PadLeft(2, '0')}{m.Groups[3].Value.PadLeft(2, '0')}"
            : value.Trim();
    }
}
