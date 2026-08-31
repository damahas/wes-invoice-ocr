using System.Text.RegularExpressions;
using Wes.Invoice.Ocr.Abstractions;

namespace Wes.Invoice.Ocr.Parsers;

/// <summary>航空运输电子客票行程单解析器。</summary>
public sealed class FlightParser : IInvoiceParser
{
    public InvoiceKind Kind => InvoiceKind.FlightItinerary;

    // netstandard2.0 无 [GeneratedRegex]（.NET 7+ 源生成器），改静态 Regex 实例，行为一致
    private static readonly Regex ReFlightNo = new(
        @"(?:航班号|航班)\s*[:：]?\s*([A-Z]{2}[0-9]{3,4})",
        RegexOptions.Compiled);
    private static readonly Regex ReDate = new(
        @"([0-9]{4})\s*年\s*([0-9]{1,2})\s*月\s*([0-9]{1,2})\s*日",
        RegexOptions.Compiled);
    private static readonly Regex RePassenger = new(
        @"姓\s*名\s*[:：]?\s*([\u4e00-\u9fa5]{2,4})",
        RegexOptions.Compiled);
    private static readonly Regex ReTicketNo = new(
        @"客票号\s*[:：]?\s*([0-9A-Za-z]{10,16})",
        RegexOptions.Compiled);
    private static readonly Regex RePrice = new(
        @"票\s*价\s*[:：]?\s*[¥￥]?\s*([0-9][0-9,]*(?:\.[0-9]{1,2})?)",
        RegexOptions.Compiled);
    private static readonly Regex ReFuel = new(
        @"燃油\s*附加(?:费)?\s*[:：]?\s*[¥￥]?\s*([0-9][0-9,]*(?:\.[0-9]{1,2})?)",
        RegexOptions.Compiled);
    private static readonly Regex ReAirportFee = new(
        @"(?:机场建设费|民航发展基金)\s*[:：]?\s*[¥￥]?\s*([0-9][0-9,]*(?:\.[0-9]{1,2})?)",
        RegexOptions.Compiled);
    private static readonly Regex ReArrow = new(
        @"[\u4e00-\u9fa5][\u4e00-\u9fa5]*\s*(?:[→\-—>]\s*)?[\u4e00-\u9fa5]",
        RegexOptions.Compiled);

    public IReadOnlyList<FieldValue> Parse(string text)
    {
        var fields = new List<FieldValue>();

        ParserHelpers.Push(fields, "flight_no", "航班号", ParserHelpers.Cap1(ReFlightNo, text));

        // 起降地：形如 "北京首都T3 → 上海虹桥T2"，箭头两侧中文片段
        var runs = ParserHelpers.ChineseRuns(text, 1);
        var am = ReArrow.Match(text);
        if (am.Success)
        {
            var parts = am.Value.Split(new[] { '→', '-', '—', '>' });
            if (parts.Length >= 2)
            {
                ParserHelpers.Push(fields, "departure", "出发地", parts[0].Trim());
                ParserHelpers.Push(fields, "arrival", "到达地", parts[1].Trim());
            }
        }
        else
        {
            ParserHelpers.Push(fields, "departure", "出发地", runs.Count > 0 ? runs[0] : null);
            ParserHelpers.Push(fields, "arrival", "到达地", runs.Count > 1 ? runs[1] : null);
        }

        var dm = ReDate.Match(text);
        if (dm.Success)
            ParserHelpers.Push(fields, "flight_date", "航班日期", $"{dm.Groups[1].Value}年{dm.Groups[2].Value}月{dm.Groups[3].Value}日");

        ParserHelpers.Push(fields, "passenger_name", "乘客姓名", ParserHelpers.Cap1(RePassenger, text));
        ParserHelpers.Push(fields, "ticket_no", "客票号", ParserHelpers.Cap1(ReTicketNo, text));
        ParserHelpers.Push(fields, "price", "票价", ParserHelpers.Cap1(RePrice, text));
        ParserHelpers.Push(fields, "fuel_surcharge", "燃油附加费", ParserHelpers.Cap1(ReFuel, text));
        ParserHelpers.Push(fields, "airport_fee", "机场建设费", ParserHelpers.Cap1(ReAirportFee, text));

        return fields;
    }
}
