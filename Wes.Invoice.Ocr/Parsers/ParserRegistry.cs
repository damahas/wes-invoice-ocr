using Wes.Invoice.Ocr.Abstractions;

namespace Wes.Invoice.Ocr.Parsers;

/// <summary>默认注册的全部解析器（顺序即匹配优先级）。</summary>
public static class ParserRegistry
{
    public static IReadOnlyList<IInvoiceParser> Default() =>
    [
        new VatParser(),
        new TrainParser(),
        new FlightParser(),
    ];
}
