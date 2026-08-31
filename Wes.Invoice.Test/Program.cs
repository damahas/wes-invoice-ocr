using Wes.Invoice.Test;

// 轻量单测入口：无第三方测试框架，退出码 0/1 可入 CI。
// 运行:
//   单测   dotnet run --project Wes.Invoice.Test                    （退出码 0/1，可入 CI）
//   冒烟   dotnet run --project Wes.Invoice.Test -- smoke [模型目录] [图片路径] [--debug]
//         模型目录省略时用输出目录下 models/（构建自动从仓库根复制）；图片省略时用 Assets/test_invoice.png
//
// 新增测试：在下面的 Tests 数组中加一行即可（用例写在 TestCases.cs）。

if (args.Length > 0 && args[0] == "smoke")
    return Smoke.Run(args[1..]);

var tests = new (string Name, Action Run)[]
{
    // 解析器
    ("ParseVat", TestCases.ParseVat),
    ("ParseTrain", TestCases.ParseTrain),
    ("ParseFlight", TestCases.ParseFlight),
    ("DetectKind", TestCases.DetectKind),
    ("ParseVatRealInvoice", TestCases.ParseVatRealInvoice),
    ("ParseVatDateWithSpaces", TestCases.ParseVatDateWithSpaces),
    ("ParseTextEmptyThrows", TestCases.ParseTextEmptyThrows),
    ("ParserRegistryDefault", TestCases.ParserRegistryDefault),

    // 引擎配置
    ("EngineModelDirFallback", TestCases.EngineModelDirFallback),
    ("EngineModelDirMissing", TestCases.EngineModelDirMissing),

    // 二维码校验
    ("QrParseVerifyUrl", TestCases.QrParseVerifyUrl),
    ("QrParseFallbackDigits", TestCases.QrParseFallbackDigits),
    ("QrVerifyMatched", TestCases.QrVerifyMatched),
    ("QrVerifyMismatch", TestCases.QrVerifyMismatch),
    ("QrVerifyDateAmountNormalize", TestCases.QrVerifyDateAmountNormalize),
    ("QrVerifyNoCommonFields", TestCases.QrVerifyNoCommonFields),
};

int passed = 0, failed = 0;
foreach (var (name, run) in tests)
{
    try
    {
        run();
        passed++;
        Console.WriteLine($"  PASS  {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"  FAIL  {name}: {ex.Message}");
    }
}

Console.WriteLine($"\n通过 {passed} / {tests.Length}");
return failed == 0 ? 0 : 1;
