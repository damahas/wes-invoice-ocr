namespace Wes.Invoice.Ocr.Abstractions;

/// <summary>票据类型。</summary>
public enum InvoiceKind
{
    /// <summary>增值税发票（普通 / 专用 / 电子）</summary>
    VatInvoice,

    /// <summary>火车票</summary>
    TrainTicket,

    /// <summary>航空运输电子客票行程单</summary>
    FlightItinerary,

    /// <summary>暂无法判定的票据</summary>
    Unknown,
}

public static class InvoiceKindExtensions
{
    /// <summary>序列化用字符串（snake_case，跨语言 JSON 契约，勿改动）。</summary>
    public static string ToWireString(this InvoiceKind kind) => kind switch
    {
        InvoiceKind.VatInvoice => "vat_invoice",
        InvoiceKind.TrainTicket => "train_ticket",
        InvoiceKind.FlightItinerary => "flight_itinerary",
        _ => "unknown",
    };
}
