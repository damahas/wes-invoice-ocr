using System.Text.RegularExpressions;
using Wes.Invoice.Ocr.Abstractions;

namespace Wes.Invoice.Ocr.Qr;

/// <summary>
/// 二维码内容解析：从原始文本提取规范化字段（key 与 FieldValue 对齐）。
/// 按置信度依次尝试三类：
/// 1. 数电票/电子发票查验 URL（inv-veri.chinatax.gov.cn?lx=&fphm=&kprq=&jshj=&bmxx=）——高置信，全字段。
/// 2. 增值税发票/数电票标准二维码：逗号分隔 5~8 段，位置固定——高置信，全字段。
/// 3. 非上述格式回退——只认带中文/英文锚点的字段（如"发票号码12345678"），
///    不猜测裸数字（裸 8 位数字可能是日期，误提取会导致校验误报）。
/// </summary>
public static class QrDataParser
{
    private static readonly Regex UrlRx = new(
        @"https?://[^\s""']+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// 增值税发票 / 数电票标准二维码（逗号分隔，位置固定）。两种布局：
    /// 布局 A（7~8 段）：0 保留(常见 01), 1 种类代码(1~2 位), 2 发票代码(0~12 位，数电票为空),
    ///   3 发票号码(8~20 位), 4 价税合计, 5 开票日期 yyyyMMdd [, 6 校验码] [, 7 随机码]
    ///   例：01,32,,26327000001034015576,300.00,20260717,,f8be（数电票）
    ///       01,10,011001605111,80100798,64.9,20161018,85342965681116380258（传统票）
    /// 布局 B（5~6 段，无种类代码）：0 保留, 1 发票代码(10~12 位), 2 发票号码(8~20 位),
    ///   3 价税合计, 4 开票日期 yyyyMMdd [, 5 校验码]
    ///   例：01,011002200111,12345678,100.00,20240520
    /// 注：`\d{n,m}` 后紧跟 `,` 时会被逗号锚死（贪婪吃满段内数字且无法让位），
    /// 故两种布局必须分开写，不能共用同一段位模板。
    /// </summary>
    private static readonly Regex StandardQrRx = new(
        @"^\s*\d{1,2}\s*,\s*(?:" +
        @"(?<kind>\d{1,2})\s*,\s*(?<code>\d{0,12})\s*,\s*(?<no>\d{8,20})\s*,\s*(?<amt>\d+(?:\.\d{1,2})?)\s*,\s*(?<date>\d{8})\s*(?:,\s*[^,\s]*\s*)?(?:,\s*[^,\s]*\s*)?" +
        @"|(?<codeB>\d{10,12})\s*,\s*(?<noB>\d{8,20})\s*,\s*(?<amtB>\d+(?:\.\d{1,2})?)\s*,\s*(?<dateB>\d{8})\s*(?:,\s*[^,\s]*\s*)?)" +
        @"\s*$",
        RegexOptions.Compiled);

    // 回退：只认带锚点的写法。发票号码 8~20 位（数电票为 20 位，传统票为 8 位）。
    private static readonly Regex InvoiceNoRx = new(
        @"(?:发票号码|号码|NO|No\.?)[:：]?\s*(\d{8,20})",
        RegexOptions.Compiled);

    private static readonly Regex InvoiceCodeRx = new(
        @"(?:发票代码|代码|CODE)[:：]?\s*(\d{10,12})",
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
            var std = StandardQrRx.Match(text);
            if (std.Success)
            {
                // 标准逗号分隔二维码。布局 A 走 kind/code/no/amt/date，
                // 布局 B 走 codeB/noB/amtB/dateB（段 1 即发票代码，无种类代码）。
                var no = std.Groups["no"].Success ? std.Groups["no"].Value : std.Groups["noB"].Value;
                var amt = std.Groups["amt"].Success ? std.Groups["amt"].Value : std.Groups["amtB"].Value;
                var date = std.Groups["date"].Success ? std.Groups["date"].Value : std.Groups["dateB"].Value;
                var code = std.Groups["code"].Success ? std.Groups["code"].Value : std.Groups["codeB"].Value;
                fields["invoice_no"] = no;
                fields["total_amount_with_tax"] = amt;
                fields["invoice_date"] = NormalizeDate(date);
                if (code.Length > 0)
                    fields["invoice_code"] = code;
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
