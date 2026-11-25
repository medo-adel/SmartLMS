using SkiaSharp;
using SmartLMS.Infrastructure.Services.Lms;

namespace SmartLMS.Infrastructure.Services.Commerce;

public sealed record InvoiceLine(string Description, int Quantity, decimal UnitPrice, decimal LineTotal);

/// <summary>One-page A4 invoice. Uses the same embedded Amiri font and shaping as the certificate, so Arabic course names render correctly.</summary>
public static class InvoicePdf
{
    public static byte[] Build(string number, DateTime issued, string customer, string currency, IReadOnlyList<InvoiceLine> lines, decimal subtotal, decimal discount, decimal total, string status)
    {
        using var ms = new MemoryStream();
        using (var doc = SKDocument.CreatePdf(ms, new SKDocumentPdfMetadata { Title = "Invoice " + number, Creator = "SmartLMS", RasterDpi = 150 }))
        {
            using var ink = new SKPaint { Color = new SKColor(26, 51, 115), IsAntialias = true };
            using var black = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            using var grey = new SKPaint { Color = new SKColor(90, 90, 90), IsAntialias = true };
            // Wrap every description first (max 4 lines of 12pt each, so a very long name still ends with an ellipsis, never a silent cut of the whole line item).
            var rows = lines.Select(l => (Line: l, Text: Fit(l.Description))).ToList();
            const float bottom = 790f, rowGap = 15f, pad = 8f;
            var pages = new List<List<(InvoiceLine Line, List<string> Text)>>(); var curPage = new List<(InvoiceLine, List<string>)>(); var used = 0f;
            var start = 270f; var limit = bottom - 90f;       // keep 90pt free on the last page for the totals
            foreach (var r in rows)
            {
                var h = r.Text.Count * rowGap + pad;
                if (start + used + h > bottom) { pages.Add(curPage); curPage = new(); used = 0; start = 130f; }
                curPage.Add(r); used += h;
            }
            if (start + used > limit) { pages.Add(curPage); curPage = new(); start = 130f; used = 0; }
            pages.Add(curPage);
            for (var pi = 0; pi < pages.Count; pi++)
            {
                using var cv = doc.BeginPage(595, 842);
                float y;
                if (pi == 0)
                {
                    CertificatePdf.DrawLeft(cv, "Invoice", 50, 80, 32, 300, ink, bold: true);
                    CertificatePdf.DrawLeft(cv, number, 50, 110, 16, 300, black);
                    CertificatePdf.DrawLeft(cv, $"Issued: {issued:yyyy-MM-dd} UTC", 50, 134, 12, 300, grey);
                    CertificatePdf.DrawLeft(cv, "Status: " + status, 50, 152, 12, 300, grey);
                    var bill = CertificatePdf.Wrap("Billed to: " + customer, 14, 495).Take(2).ToList();
                    for (var i = 0; i < bill.Count; i++) CertificatePdf.DrawLeft(cv, bill[i], 50, 186 + i * 18, 14, 495, black);
                    y = 240f;
                }
                else
                {
                    CertificatePdf.DrawLeft(cv, $"Invoice {number} (continued)", 50, 70, 14, 495, ink, bold: true);
                    y = 100f;
                }
                CertificatePdf.DrawLeft(cv, "Item", 50, y, 12, 300, ink, bold: true);
                CertificatePdf.DrawLeft(cv, "Qty", 360, y, 12, 40, ink, bold: true);
                CertificatePdf.DrawLeft(cv, "Price", 410, y, 12, 70, ink, bold: true);
                CertificatePdf.DrawLeft(cv, "Total", 490, y, 12, 60, ink, bold: true);
                y += 10; cv.DrawLine(50, y, 545, y, grey); y += 22;
                foreach (var (l, text) in pages[pi])
                {
                    for (var i = 0; i < text.Count; i++) CertificatePdf.DrawLeft(cv, text[i], 50, y + i * rowGap, 12, 295, black);
                    CertificatePdf.DrawLeft(cv, l.Quantity.ToString(), 360, y, 12, 40, black);
                    CertificatePdf.DrawLeft(cv, l.UnitPrice.ToString("0.00"), 410, y, 12, 70, black);
                    CertificatePdf.DrawLeft(cv, l.LineTotal.ToString("0.00"), 490, y, 12, 60, black);
                    y += text.Count * rowGap + pad;
                }
                if (pi == pages.Count - 1)
                {
                    y += 2; cv.DrawLine(50, y, 545, y, grey); y += 24;
                    CertificatePdf.DrawLeft(cv, $"Subtotal: {subtotal:0.00} {currency}", 360, y, 13, 190, black); y += 20;
                    if (discount > 0) { CertificatePdf.DrawLeft(cv, $"Discount: -{discount:0.00} {currency}", 360, y, 13, 190, black); y += 20; }
                    CertificatePdf.DrawLeft(cv, $"Total: {total:0.00} {currency}", 360, y, 16, 190, ink, bold: true);
                }
                CertificatePdf.DrawLeft(cv, $"Page {pi + 1} of {pages.Count}", 480, 822, 10, 65, grey);
                doc.EndPage();
            }
            doc.Close();
        }
        return ms.ToArray();

        static List<string> Fit(string d)
        {
            var w = CertificatePdf.Wrap(d, 12, 295);
            if (w.Count <= 4) return w;
            var cut = w.Take(4).ToList(); cut[3] = cut[3].TrimEnd() + "…"; return cut;
        }
    }
}
