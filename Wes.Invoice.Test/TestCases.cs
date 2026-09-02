using Wes.Invoice.Ocr;
using Wes.Invoice.Ocr.Abstractions;
using Wes.Invoice.Ocr.Detect;
using Wes.Invoice.Ocr.Paddle;
using Wes.Invoice.Ocr.Parsers;

using static Wes.Invoice.Test.Harness;

namespace Wes.Invoice.Test;

/// <summary>
/// 全部单测用例：解析器 + 引擎配置 + 二维码校验。
/// 均走 <see cref="InvoiceOcrService.ParseText"/> 或纯逻辑，用静态文本 / 构造数据锁定
/// 「输入 → 输出」契约，不经过模型推理（快且可复现）。
/// 注意：测的是解析逻辑正确性，不覆盖 OCR 识别准确度（后者需真实图片基准集）。
/// </summary>
internal static class TestCases
{
    // ---------- 解析器 ----------

    public static void ParseVat()
    {
        var svc = NewService();
        const string text = """
            发票代码: 011002200111
            发票号码: 12345678
            开票日期: 2024年05月20日
            名 称: 示例科技有限公司
            纳税人识别号: 91110000MA0000000A
            名 称: 北京供应商有限公司
            纳税人识别号: 91110000MA1111111B
            合计: 88.50
            税额: 11.50
            价税合计（大写）壹佰元整 （小写）¥100.00
            """;
        var inv = svc.ParseText(text);
        Equal(InvoiceKind.VatInvoice, inv.Kind, "kind");
        var get = Field(inv);
        Equal("011002200111", get("invoice_code"), "invoice_code");
        Equal("12345678", get("invoice_no"), "invoice_no");
        Equal("示例科技有限公司", get("buyer_name"), "buyer_name");
        Equal("北京供应商有限公司", get("seller_name"), "seller_name");
        Equal("100.00", get("total_amount_with_tax"), "total_amount_with_tax");
    }

    public static void ParseTrain()
    {
        var svc = NewService();
        const string text = """
            G1024
            北京南站 → 上海虹桥站
            2024年05月20日
            二等座
            票价 ¥553.00
            姓名 张三
            110101199001011234
            """;
        var inv = svc.ParseText(text);
        Equal(InvoiceKind.TrainTicket, inv.Kind, "kind");
        var get = Field(inv);
        Equal("G1024", get("train_no"), "train_no");
        Equal("北京南站", get("from_station"), "from_station");
        Equal("上海虹桥站", get("to_station"), "to_station");
        Equal("553.00", get("price"), "price");
    }

    public static void ParseFlight()
    {
        var svc = NewService();
        const string text = """
            航空运输电子客票行程单
            航班号: CA1234
            北京首都T3 → 上海虹桥T2
            2024年05月20日
            姓名 李四
            客票号: 9991234567890
            票价 ¥880.00
            燃油附加费 ¥40.00
            机场建设费 ¥50.00
            """;
        var inv = svc.ParseText(text);
        Equal(InvoiceKind.FlightItinerary, inv.Kind, "kind");
        var get = Field(inv);
        Equal("CA1234", get("flight_no"), "flight_no");
        Equal("40.00", get("fuel_surcharge"), "fuel_surcharge");
        Equal("50.00", get("airport_fee"), "airport_fee");
    }

    public static void DetectKind()
    {
        Equal(InvoiceKind.VatInvoice, InvoiceKindDetector.DetectKind("发票号码：12345678"), "vat");
        Equal(InvoiceKind.VatInvoice, InvoiceKindDetector.DetectKind("价税合计 ¥100.00\n税额 11.50"), "vat2");
        Equal(InvoiceKind.TrainTicket, InvoiceKindDetector.DetectKind("G1024\n北京南站 → 上海虹桥站"), "train");
        Equal(InvoiceKind.TrainTicket, InvoiceKindDetector.DetectKind("乘车日期 2024年05月20日"), "train2");
        Equal(InvoiceKind.FlightItinerary, InvoiceKindDetector.DetectKind("航空运输电子客票行程单\n航班号 CA1234"), "flight");
        Equal(InvoiceKind.FlightItinerary, InvoiceKindDetector.DetectKind("客票号: 9991234567890"), "flight2");
        Equal(InvoiceKind.Unknown, InvoiceKindDetector.DetectKind("随便一段无关文字 123456"), "unknown");
    }

    public static void ParseVatRealInvoice()
    {
        // 模拟高速公路通行费电子发票 OCR 文本（含识别噪声；数据均为虚构示例）
        var svc = NewService();
        const string text = """
            购
            6
            率区1前华
            10
            密
            密A*12/>34567890123456<>789012+34567>89>012+*34五
            发票号码：12345678
            发票弋码：011002200111
            开票日期：2024年05月20日
            方
            销
            售
            名
            收款人：王小明
            合
            ?.!?十
            信纳税人识别号：913200004321098765
            计
            称：示例高速运营有限公司
            开户行及账号：示例银行示例支行6222029999999999999
            、地址、电话：示例市示例区示例路88号0571-88886666
            壹佰圆
            复核：李小红
            威
            注
            开票人：赵大强
            备车型：一型客车
            …入口站：江苏示例西站
            车牌号：苏B88888(蓝色)
            一出口站：江苏示例东站…
            (小写)
            201
            注
            100,00
            100.00
            示例高速有限
            美一
            E
            """;
        var inv = svc.ParseText(text);
        Equal(InvoiceKind.VatInvoice, inv.Kind, "kind");
        var get = Field(inv);
        Equal("011002200111", get("invoice_code"), "invoice_code");
        Equal("12345678", get("invoice_no"), "invoice_no");
        Equal("2024年05月20日", get("invoice_date"), "invoice_date");
        Equal("913200004321098765", get("buyer_tax_no"), "buyer_tax_no");
        Equal("100.00", get("total_amount_with_tax"), "total_amount_with_tax");
    }

    public static void ParseVatDateWithSpaces()
    {
        // 逐行 rec 模式下 OCR 常在日期内插入空格，日期正则须容忍 \s*
        var svc = NewService();
        const string text = "发票号码：12345678\n开票日期：2024年 05 月20 日\n价税合计（小写）¥100.00";
        var inv = svc.ParseText(text);
        Equal(InvoiceKind.VatInvoice, inv.Kind, "kind");
        var get = Field(inv);
        Equal("2024年05月20日", get("invoice_date"), "invoice_date");
    }

    public static void ParseVatCnAmount()
    {
        // det 对表格线内"（小写）¥300.00"常漏检，但"叁佰圆整"（大写）可检出；
        // 价税合计须回退解析中文大写金额
        var svc = NewService();
        const string text = """
            发票号码: 26327000001034015576
            开票日期: 2026年07月17日
            价税合计（大写）叁佰圆整
            名 称: 苏州峰之鼎信息科技有限公司
            """;
        var inv = svc.ParseText(text);
        Equal(InvoiceKind.VatInvoice, inv.Kind, "kind");
        var get = Field(inv);
        Equal("300.00", get("total_amount_with_tax"), "total_amount_with_tax");

        // 更复杂的大写金额：万/元/角/分
        const string big = """
            发票号码: 12345678
            价税合计（大写）壹万贰仟叁佰肆拾伍元陆角柒分
            """;
        var inv2 = svc.ParseText(big);
        Equal("12345.67", Field(inv2)("total_amount_with_tax"), "total_amount_with_tax_cn");

        // 小写存在时仍优先小写
        const string both = "发票号码: 12345678\n价税合计（小写）¥100.00（大写）壹佰元整";
        var inv3 = svc.ParseText(both);
        Equal("100.00", Field(inv3)("total_amount_with_tax"), "total_amount_with_tax_priority");
    }

    public static void ParseTextEmptyThrows()
    {
        var svc = NewService();
        try
        {
            svc.ParseText("   \n  ");
            throw new Exception("应抛 OcrException.EmptyInput，但未抛");
        }
        catch (OcrException ex)
        {
            Equal(OcrErrorKind.EmptyInput, ex.Kind, "empty kind");
        }
    }

    public static void ParserRegistryDefault()
    {
        var parsers = ParserRegistry.Default();
        Equal(3, parsers.Count, "parser count");
        Equal(InvoiceKind.VatInvoice, parsers[0].Kind, "p0");
        Equal(InvoiceKind.TrainTicket, parsers[1].Kind, "p1");
        Equal(InvoiceKind.FlightItinerary, parsers[2].Kind, "p2");
    }

    // ---------- 引擎配置 ----------

    public static void EngineModelDirFallback()
    {
        // 构造参数为空时，应回退到 PaddleOcrConfig.ModelDir（用不存在的目录验证路径确实被采用）
        const string dir = "C:/no-such-models-dir";
        try
        {
            using var _ = new PaddleOcrEngine("", new PaddleOcrConfig { ModelDir = dir });
            throw new Exception("应抛 OcrException.EngineNotConfigured，但未抛");
        }
        catch (OcrException ex)
        {
            // 引擎内部走 Path.GetFullPath，Windows 下会把 "/" 规范化为 "\"，比较前先归一化
            var expected = Path.GetFullPath(dir);
            Equal(OcrErrorKind.EngineNotConfigured, ex.Kind, "kind");
            Equal(true, ex.Message.Contains(expected), $"消息应含配置的目录 {expected}，实际: {ex.Message}");
        }
    }

    public static void EngineModelDirMissing()
    {
        // 构造参数与配置都为空时，应明确提示未指定目录（而非 Path.GetFullPath 的 ArgumentException）
        try
        {
            using var _ = new PaddleOcrEngine("", new PaddleOcrConfig());
            throw new Exception("应抛 OcrException.EngineNotConfigured，但未抛");
        }
        catch (OcrException ex)
        {
            Equal(OcrErrorKind.EngineNotConfigured, ex.Kind, "kind");
            Equal(true, ex.Message.Contains("未指定"), $"消息应含“未指定”，实际: {ex.Message}");
        }
    }

    // ---------- 二维码校验（Qr） ----------

    public static void QrParseVerifyUrl()
    {
        // 数电票查验 URL（模拟含 URL 编码参数）
        const string url =
            "https://inv-veri.chinatax.gov.cn/xxcx?lx=10&fphm=12345678&kprq=2024-05-20&jshj=100.00&bmxx=%E7%A4%BA%E4%BE%8B%E5%8F%91%E7%A5%A8";
        var qr = Wes.Invoice.Ocr.Qr.QrDataParser.Parse(url);
        Equal("12345678", qr.Fields["invoice_no"], "invoice_no");
        Equal("20240520", qr.Fields["invoice_date"], "invoice_date");
        Equal("100.00", qr.Fields["total_amount_with_tax"], "jshj");
        Equal("10", qr.Fields["invoice_type"], "lx");
    }

    public static void QrParseStandardCompact()
    {
        // 紧凑版标准二维码（无种类代码，段 1 即发票代码）：命中 StandardQrRx，全字段提取
        var qr = Wes.Invoice.Ocr.Qr.QrDataParser.Parse("01,011002200111,12345678,100.00,20240520");
        Equal("12345678", qr.Fields["invoice_no"], "invoice_no");
        Equal("011002200111", qr.Fields["invoice_code"], "invoice_code");
        Equal("100.00", qr.Fields["total_amount_with_tax"], "total_amount_with_tax");
        Equal("20240520", qr.Fields["invoice_date"], "invoice_date");
    }

    public static void QrParseFallbackNoAnchor()
    {
        // 非 URL 非标准逗号格式：无锚点裸数字不猜测，避免把日期/号码当金额等假字段
        var qr = Wes.Invoice.Ocr.Qr.QrDataParser.Parse("张三 12345678 100.00 20240520");
        Equal(0, qr.Fields.Count, "不提取任何字段");

        // 带锚点文本走回退提取，仍不猜金额/日期
        var qr2 = Wes.Invoice.Ocr.Qr.QrDataParser.Parse("发票号码：12345678 发票代码：011002200111");
        Equal("12345678", qr2.Fields["invoice_no"], "invoice_no");
        Equal("011002200111", qr2.Fields["invoice_code"], "invoice_code");
        Equal(false, qr2.Fields.ContainsKey("total_amount_with_tax"), "不猜金额");
        Equal(false, qr2.Fields.ContainsKey("invoice_date"), "不猜日期");
    }

    public static void QrParseStandardDian()
    {
        // 数电票标准二维码：8 段，发票代码段为空（用户实测格式）
        const string raw = "01,32,,26327000001034015576,300.00,20260717,,f8be";
        var qr = Wes.Invoice.Ocr.Qr.QrDataParser.Parse(raw);
        Equal("26327000001034015576", qr.Fields["invoice_no"], "invoice_no");
        Equal("300.00", qr.Fields["total_amount_with_tax"], "total_amount_with_tax");
        Equal("20260717", qr.Fields["invoice_date"], "invoice_date");
        Equal(false, qr.Fields.ContainsKey("invoice_code"), "数电票无发票代码");
    }

    public static void QrParseStandardTraditional()
    {
        // 传统增值税发票二维码：7 段
        const string raw = "01,10,011001605111,80100798,64.9,20161018,85342965681116380258";
        var qr = Wes.Invoice.Ocr.Qr.QrDataParser.Parse(raw);
        Equal("80100798", qr.Fields["invoice_no"], "invoice_no");
        Equal("011001605111", qr.Fields["invoice_code"], "invoice_code");
        Equal("64.9", qr.Fields["total_amount_with_tax"], "total_amount_with_tax");
        Equal("20161018", qr.Fields["invoice_date"], "invoice_date");
    }

    public static void QrVerifyMatched()
    {
        var invoice = MakeVatInvoice("12345678", "2024年05月20日", "100.00");
        var qr = new QrData("url", new Dictionary<string, string>
        {
            ["invoice_no"] = "12345678",
            ["invoice_date"] = "20240520",
            ["total_amount_with_tax"] = "100.00",
        });
        var v = Wes.Invoice.Ocr.Qr.VerificationService.Verify(invoice, qr);
        Equal(QrStatus.Verified, v.Status, "status");
        Equal(3, v.Matched.Count, "matched count");
        Equal(0, v.Conflicts.Count, "conflicts count");
    }

    public static void QrVerifyMismatch()
    {
        var invoice = MakeVatInvoice("12345678", "2024年05月20日", "100.00");
        var qr = new QrData("url", new Dictionary<string, string>
        {
            ["invoice_no"] = "12345679", // 号码不同
            ["invoice_date"] = "20240520",
        });
        var v = Wes.Invoice.Ocr.Qr.VerificationService.Verify(invoice, qr);
        Equal(QrStatus.Mismatch, v.Status, "status");
        Equal(1, v.Conflicts.Count, "conflicts count");
        Equal("invoice_no", v.Conflicts[0].Key, "conflict key");
    }

    public static void QrVerifyDateAmountNormalize()
    {
        // OCR 金额"100,00"（逗号误识别为小数点）与二维码"100.00"应判一致；日期分隔符差异应忽略
        var invoice = MakeVatInvoice("12345678", "2024年05月20日", "100,00");
        var qr = new QrData("url", new Dictionary<string, string>
        {
            ["invoice_no"] = "12345678",
            ["invoice_date"] = "2024-05-20",
            ["total_amount_with_tax"] = "¥100.00",
        });
        var v = Wes.Invoice.Ocr.Qr.VerificationService.Verify(invoice, qr);
        Equal(QrStatus.Verified, v.Status, "status");
        Equal(3, v.Matched.Count, "matched count");
    }

    public static void QrVerifyNoCommonFields()
    {
        var invoice = MakeVatInvoice("12345678", "2024年05月20日", "100.00");
        var qr = new QrData("加密串无结构", new Dictionary<string, string>());
        var v = Wes.Invoice.Ocr.Qr.VerificationService.Verify(invoice, qr);
        Equal(QrStatus.NotScanned, v.Status, "status");

        var v2 = Wes.Invoice.Ocr.Qr.VerificationService.Verify(invoice, null);
        Equal(QrStatus.NotScanned, v2.Status, "null status");
    }

    public static void QrVerifyAmountDecimalEqual()
    {
        // OCR 金额"300"（识别丢小数位）与二维码"300.00"按数值一致，不应误报冲突
        var invoice = MakeVatInvoice("26327000001034015576", "2026年07月17日", "300");
        var qr = new QrData("raw", new Dictionary<string, string>
        {
            ["invoice_no"] = "26327000001034015576",
            ["invoice_date"] = "20260717",
            ["total_amount_with_tax"] = "300.00",
        });
        var v = Wes.Invoice.Ocr.Qr.VerificationService.Verify(invoice, qr);
        Equal(QrStatus.Verified, v.Status, "status");
        Equal(3, v.Matched.Count, "matched count");

        // 千分位与两位小数也应一致
        var invoice2 = MakeVatInvoice("12345678", "2024年05月20日", "1,234.56");
        var qr2 = new QrData("raw", new Dictionary<string, string>
        {
            ["invoice_no"] = "12345678",
            ["total_amount_with_tax"] = "1234.56",
        });
        var v2 = Wes.Invoice.Ocr.Qr.VerificationService.Verify(invoice2, qr2);
        Equal(QrStatus.Verified, v2.Status, "千分位 status");
    }

    public static void ParseVatTotalSplitLines()
    {
        // 逐行 rec 模式 OCR 把"合计/税额"拆成两行，正则须容忍字间换行
        var svc = NewService();
        const string text = """
            发票号码：12345678
            开票日期：2024年05月20日
            合
            计：88.50
            税
            额：11.50
            价税合计（大写）壹佰元整 （小写）¥100.00
            """;
        var inv = svc.ParseText(text);
        Equal(InvoiceKind.VatInvoice, inv.Kind, "kind");
        var get = Field(inv);
        Equal("88.50", get("total_amount"), "total_amount");
        Equal("11.50", get("total_tax"), "total_tax");
        Equal("100.00", get("total_amount_with_tax"), "total_amount_with_tax");
    }

    public static void RecognizeImageInputs()
    {
        // 最小合法 PNG（1x1 白像素），仅验证流水线输入通道，不涉及真实解码质量
        byte[] png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        var svc = new InvoiceOcrService(new TextEngine("发票号码：12345678"));

        var inv1 = svc.RecognizeImageBytes(png);
        Equal(InvoiceKind.VatInvoice, inv1.Kind, "bytes kind");
        Equal("12345678", Field(inv1)("invoice_no"), "bytes invoice_no");

        using var ms = new MemoryStream(png);
        var inv2 = svc.RecognizeImage(ms);
        Equal("12345678", Field(inv2)("invoice_no"), "stream invoice_no");

        var path = Path.Combine(Path.GetTempPath(), "wes_ocr_input_probe.png");
        File.WriteAllBytes(path, png);
        try
        {
            var inv3 = svc.RecognizeImage(path);
            Equal("12345678", Field(inv3)("invoice_no"), "path invoice_no");
        }
        finally
        {
            File.Delete(path);
        }

        // 空输入守卫
        Throws<OcrException>(() => svc.RecognizeImageBytes(Array.Empty<byte>()), "empty bytes");
        Throws<ArgumentNullException>(() => svc.RecognizeImage((Stream)null!), "null stream");
        Throws<ArgumentNullException>(() => svc.RecognizeImage((string)null!), "null path");
    }
}
