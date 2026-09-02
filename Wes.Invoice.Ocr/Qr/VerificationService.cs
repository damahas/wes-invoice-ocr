using System.Globalization;
using System.Text.RegularExpressions;
using Wes.Invoice.Ocr.Abstractions;

namespace Wes.Invoice.Ocr.Qr;

/// <summary>
/// 本地交叉校验：二维码字段 vs OCR 结构化字段。
/// 纯逻辑（无 I/O），可单测。校验语义：
/// - qr 为 null                     → NotScanned
/// - 无共同字段可比对               → NotScanned
/// - 全部一致                       → Verified
/// - 任一共同字段冲突               → Mismatch（附冲突明细）
/// </summary>
public static class VerificationService
{
    public static Verification Verify(Wes.Invoice.Ocr.Abstractions.Invoice invoice, QrData? qr)
    {
        if (qr is null)
            return new Verification(QrStatus.NotScanned, null, [], []);

        var fields = invoice.Fields
            .Where(f => !string.IsNullOrWhiteSpace(f.Value))
            .ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);

        var matched = new List<QrFieldMatch>();
        var conflicts = new List<QrConflict>();

        foreach (var kvp in qr.Fields)
        {
            var key = kvp.Key;
            var qv = kvp.Value;
            if (!fields.TryGetValue(key, out var ov))
                continue;
            if (Normalize(key, qv) == Normalize(key, ov))
                matched.Add(new QrFieldMatch(key, qv, ov));
            else
                conflicts.Add(new QrConflict(key, qv, ov));
        }

        if (matched.Count == 0 && conflicts.Count == 0)
            return new Verification(QrStatus.NotScanned, qr.Raw, [], []);

        return new Verification(
            conflicts.Count == 0 ? QrStatus.Verified : QrStatus.Mismatch,
            qr.Raw, matched, conflicts);
    }

    /// <summary>字段值规范化，容忍 OCR 与二维码的格式差异（日期分隔符、金额符号/逗号小数点）。</summary>
    static string Normalize(string key, string value)
    {
        var v = value.Trim();
        return key switch
        {
            // 2024年05月20日 / 2024-05-20 / 20240520 → 20240520
            "invoice_date" => NormalizeDate(v),
            // ¥100.00 / 100.00 / 81,00（OCR 逗号误识别为小数点）→ 纯数字串
            "total_amount_with_tax" or "total_amount" or "total_tax" => NormalizeAmount(v),
            _ => v,
        };
    }

    static string NormalizeDate(string value)
    {
        var m = Regex.Match(value, @"(\d{4})\D*(\d{1,2})\D*(\d{1,2})");
        return m.Success
            ? $"{m.Groups[1].Value}{m.Groups[2].Value.PadLeft(2, '0')}{m.Groups[3].Value.PadLeft(2, '0')}"
            : value;
    }

    static string NormalizeAmount(string value)
    {
        var v = Regex.Replace(value.Trim(), @"[^0-9.,]", "");
        if (v.Length == 0)
            return value.Trim();
        // OCR 把小数点误识别为逗号（100,00）：无小数点时逗号当小数点
        if (v.IndexOf('.') < 0 && v.IndexOf(',') >= 0)
            v = v.Replace(',', '.');
        // 千分位（1,234.56）：既有逗号又有小数点时去掉逗号
        else if (v.IndexOf(',') >= 0)
            v = v.Replace(",", "");
        // 按数值比较并统一为两位小数，避免 300.00 vs 300 因小数位不同误报冲突
        return decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? d.ToString("0.00", CultureInfo.InvariantCulture)
            : value.Trim();
    }
}
