using System.Text.RegularExpressions;
using Wes.Invoice.Ocr.Abstractions;

namespace Wes.Invoice.Ocr.Parsers;

/// <summary>解析器公共辅助：字段入队、中文片段提取、首个捕获组。</summary>
internal static class ParserHelpers
{
    /// <summary>字段入队辅助：值为空则跳过（trim 后）。</summary>
    public static void Push(List<FieldValue> fields, string key, string label, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;
        var v = value!.Trim();
        if (v.Length == 0)
            return;
        fields.Add(new FieldValue(key, label, v, 0.8f)); // 规则解析置信度固定，后续结合 OCR box 置信度加权
    }

    /// <summary>提取中文连续片段（用于站名、城市名等启发式匹配）。</summary>
    public static List<string> ChineseRuns(string text, int minLen) =>
        ChineseRegex(minLen).Matches(text).Cast<Match>().Select(m => m.Value).ToList();

    /// <summary>取第一个捕获组（未匹配返回 null）。</summary>
    public static string? Cap1(Regex re, string text)
    {
        var m = re.Match(text);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static Regex ChineseRegex(int minLen) =>
        new($@"[\u4e00-\u9fa5]{{{minLen},}}", RegexOptions.Compiled);
}
