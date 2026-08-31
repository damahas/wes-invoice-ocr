using UglyToad.PdfPig;
using Wes.Invoice.Ocr.Abstractions;

namespace Wes.Invoice.Ocr.Pdf;

/// <summary>PDF 文本层提取。</summary>
public static class PdfTextExtractor
{
    /// <summary>提取 PDF 全文。若为扫描件（无文本层），返回空字符串。</summary>
    public static string ExtractText(byte[] data)
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            using var doc = PdfDocument.Open(data);
            foreach (var page in doc.GetPages())
            {
                sb.AppendLine(page.Text);
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            throw new OcrException($"PDF 文本提取失败: {ex.Message}", ex, OcrErrorKind.Pdf);
        }
    }
}
