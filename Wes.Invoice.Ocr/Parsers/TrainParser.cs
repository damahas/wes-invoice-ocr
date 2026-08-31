using System.Text.RegularExpressions;
using Wes.Invoice.Ocr.Abstractions;

namespace Wes.Invoice.Ocr.Parsers;

/// <summary>火车票解析器。</summary>
public sealed class TrainParser : IInvoiceParser
{
    public InvoiceKind Kind => InvoiceKind.TrainTicket;

    // netstandard2.0 无 [GeneratedRegex]（.NET 7+ 源生成器），改静态 Regex 实例，行为一致
    private static readonly Regex ReTrainNo = new(
        @"(?:车次\s*[:：]?\s*)?([GCDZYKL])\s*([0-9]{1,4})",
        RegexOptions.Compiled);
    private static readonly Regex ReDate = new(
        @"([0-9]{4})\s*年\s*([0-9]{1,2})\s*月\s*([0-9]{1,2})\s*日",
        RegexOptions.Compiled);
    private static readonly Regex RePrice = new(
        @"票\s*价\s*[:：]?\s*[¥￥]?\s*([0-9][0-9,]*(?:\.[0-9]{1,2})?)",
        RegexOptions.Compiled);
    private static readonly Regex ReName = new(
        @"姓\s*名\s*[:：]?\s*([\u4e00-\u9fa5]{2,4})",
        RegexOptions.Compiled);
    private static readonly Regex ReIdNo = new(
        @"([0-9]{17}[0-9Xx])",
        RegexOptions.Compiled);
    private static readonly Regex ReSeat = new(
        @"(商务座|特等座|一等座|二等座|高级软卧|软卧|硬卧|软座|硬座|无座|动卧)",
        RegexOptions.Compiled);

    public IReadOnlyList<FieldValue> Parse(string text)
    {
        var fields = new List<FieldValue>();

        var tn = ReTrainNo.Match(text);
        if (tn.Success)
            ParserHelpers.Push(fields, "train_no", "车次", $"{tn.Groups[1].Value}{tn.Groups[2].Value}");

        var dm = ReDate.Match(text);
        if (dm.Success)
            ParserHelpers.Push(fields, "travel_date", "乘车日期", $"{dm.Groups[1].Value}年{dm.Groups[2].Value}月{dm.Groups[3].Value}日");

        ParserHelpers.Push(fields, "price", "票价", ParserHelpers.Cap1(RePrice, text));
        ParserHelpers.Push(fields, "passenger_name", "姓名", ParserHelpers.Cap1(ReName, text));
        ParserHelpers.Push(fields, "passenger_id_no", "身份证号", ParserHelpers.Cap1(ReIdNo, text));

        var sm = ReSeat.Match(text);
        if (sm.Success)
            ParserHelpers.Push(fields, "seat_class", "席别", sm.Value);

        // 车站：取前两个以"站"结尾的中文片段（版式上发站在前、到站在后）
        var stations = ParserHelpers.ChineseRuns(text, 1).Where(s => s.EndsWith("站", StringComparison.Ordinal)).ToList();
        ParserHelpers.Push(fields, "from_station", "出发站", stations.Count > 0 ? stations[0] : null);
        ParserHelpers.Push(fields, "to_station", "到达站", stations.Count > 1 ? stations[1] : null);

        return fields;
    }
}
