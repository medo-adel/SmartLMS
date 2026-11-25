using System.Globalization;
using System.Reflection;
using QRCoder;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace SmartLMS.Infrastructure.Services.Lms;

/// <summary>
/// One-page landscape certificate PDF. Text is shaped with HarfBuzz and drawn with the embedded Amiri font (SIL OFL 1.1, see Assets/Fonts/Amiri-OFL.txt),
/// so Arabic letters join correctly. Mixed Arabic/Latin text is split into direction runs and ordered visually. This is a simplified bidi
/// (strong-direction runs, neutrals take the surrounding direction), not full UAX #9: nested embeddings and explicit marks are not handled.
/// The QR is vector squares, not an image.
/// </summary>
public static class CertificatePdf
{
    private static readonly Lazy<SKTypeface> Face = new(() =>
    {
        using var s = typeof(CertificatePdf).Assembly.GetManifestResourceStream("Fonts.Amiri-Regular.ttf") ?? throw new InvalidOperationException("Embedded font missing.");
        return SKTypeface.FromStream(s) ?? throw new InvalidOperationException("Embedded font could not be loaded.");
    });

    private static bool IsRtl(int cp) => cp is (>= 0x0590 and <= 0x08FF) or (>= 0xFB1D and <= 0xFDFF) or (>= 0xFE70 and <= 0xFEFF);
    private enum Dir { Neutral, Ltr, Rtl }
    private static Dir Classify(string s, int i)
    {
        var cp = char.ConvertToUtf32(s, i);
        if (IsRtl(cp)) return Dir.Rtl;
        var cat = CharUnicodeInfo.GetUnicodeCategory(cp);
        return cat is UnicodeCategory.DecimalDigitNumber or UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.OtherLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter ? Dir.Ltr : Dir.Neutral;
    }

    /// <summary>Returns the text split into runs in visual (left to right) order.</summary>
    public static List<(string Text, bool Rtl)> VisualRuns(string text)
    {
        var cls = new List<(string C, Dir D)>();
        for (var i = 0; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1) cls.Add((char.ConvertFromUtf32(char.ConvertToUtf32(text, i)), Classify(text, i)));
        var baseRtl = cls.FirstOrDefault(c => c.D != Dir.Neutral).D == Dir.Rtl;
        var dirs = cls.Select(c => c.D).ToArray();
        for (var i = 0; i < dirs.Length; i++)
        {
            if (dirs[i] != Dir.Neutral) continue;
            var j = i; while (j < dirs.Length && dirs[j] == Dir.Neutral) j++;
            var prev = i > 0 ? dirs[i - 1] : Dir.Neutral; var next = j < dirs.Length ? dirs[j] : Dir.Neutral;
            var d = prev != Dir.Neutral && prev == next ? prev : (baseRtl ? Dir.Rtl : Dir.Ltr);
            for (var k = i; k < j; k++) dirs[k] = d;
            i = j - 1;
        }
        var runs = new List<(string, bool)>();
        for (var i = 0; i < cls.Count;)
        {
            var j = i; var sb = new System.Text.StringBuilder();
            while (j < cls.Count && dirs[j] == dirs[i]) sb.Append(cls[j++].C);
            runs.Add((sb.ToString(), dirs[i] == Dir.Rtl)); i = j;
        }
        if (baseRtl) runs.Reverse();
        return runs;
    }

    private static float Measure(string text, SKFont font, SKPaint paint)
    {
        float w = 0;
        foreach (var (t, _) in VisualRuns(text)) { using var sh = new SKShaper(font.Typeface); w += sh.Shape(t, font).Width; }
        return w;
    }

    /// <summary>Greedy word wrap (character wrap for a word wider than the line) measured with the embedded font. Max lines is enforced by the caller.</summary>
    public static List<string> Wrap(string text, float size, float maxWidth)
    {
        using var font = new SKFont(Face.Value, size) { Subpixel = true };
        using var paint = new SKPaint();
        var lines = new List<string>(); var cur = "";
        foreach (var word in (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var w = word;
            while (Measure(w, font, paint) > maxWidth && w.Length > 1)
            {   // break an over-long word at the longest prefix that fits
                var n = w.Length - 1; while (n > 1 && Measure(w[..n], font, paint) > maxWidth) n--;
                if (cur.Length > 0) { lines.Add(cur); cur = ""; }
                lines.Add(w[..n]); w = w[n..];
            }
            var trial = cur.Length == 0 ? w : cur + " " + w;
            if (cur.Length > 0 && Measure(trial, font, paint) > maxWidth) { lines.Add(cur); cur = w; } else cur = trial;
        }
        if (cur.Length > 0) lines.Add(cur);
        if (lines.Count == 0) lines.Add("");
        return lines;
    }

    internal static void DrawLeft(SKCanvas cv, string text, float x, float y, float size, float maxWidth, SKPaint paint, bool bold = false)
    {
        using var font = new SKFont(Face.Value, size) { Subpixel = true, Embolden = bold };
        var w = Measure(text, font, paint);
        if (w > maxWidth) { size *= maxWidth / w; font.Size = size; }
        foreach (var (t, _) in VisualRuns(text))
        {
            using var sh = new SKShaper(font.Typeface);
            var r = sh.Shape(t, font);
            cv.DrawShapedText(sh, t, x, y, font, paint);
            x += r.Width;
        }
    }

    private static void DrawCentered(SKCanvas cv, string text, float cx, float y, float size, float maxWidth, SKPaint paint, bool bold = false)
    {
        using var font = new SKFont(Face.Value, size) { Subpixel = true, Embolden = bold };
        var w = Measure(text, font, paint);
        if (w > maxWidth) { size *= maxWidth / w; font.Size = size; w = Measure(text, font, paint); }
        var x = cx - w / 2;
        foreach (var (t, _) in VisualRuns(text))
        {
            using var sh = new SKShaper(font.Typeface);
            var r = sh.Shape(t, font);
            cv.DrawShapedText(sh, t, x, y, font, paint);
            x += r.Width;
        }
    }

    public static byte[] Build(string name, string course, DateTime issued, string verifyUrl)
    {
        using var ms = new MemoryStream();
        using (var doc = SKDocument.CreatePdf(ms, new SKDocumentPdfMetadata { Title = "Certificate of Completion", Creator = "SmartLMS", RasterDpi = 150 }))
        using (var cv = doc.BeginPage(842, 595))
        {
            using var ink = new SKPaint { Color = new SKColor(26, 51, 115), IsAntialias = true };
            using var black = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            using var grey = new SKPaint { Color = new SKColor(90, 90, 90), IsAntialias = true };
            using var border = new SKPaint { Color = ink.Color, Style = SKPaintStyle.Stroke, StrokeWidth = 3, IsAntialias = true };
            cv.DrawRect(30, 30, 782, 535, border);
            DrawCentered(cv, "Certificate of Completion", 421, 120, 40, 700, ink, bold: true);
            DrawCentered(cv, "This certifies that", 421, 190, 20, 700, black);
            DrawCentered(cv, name, 421, 250, 38, 700, black, bold: true);
            DrawCentered(cv, "has successfully completed the course", 421, 305, 20, 700, black);
            DrawCentered(cv, course, 421, 360, 30, 700, ink, bold: true);
            DrawCentered(cv, $"Issued: {issued:yyyy-MM-dd} UTC", 421 - 130, 480, 16, 300, black);
            DrawCentered(cv, "Verify: " + verifyUrl, 421, 548, 9, 740, grey);
            var qr = new QRCodeGenerator().CreateQrCode(verifyUrl, QRCodeGenerator.ECCLevel.M).ModuleMatrix;
            var n = qr.Count; var cell = 4f; var size = n * cell; var x0 = 842 - 60 - size; var y0 = 535 - size;
            using var white = new SKPaint { Color = SKColors.White };
            cv.DrawRect(x0 - 8, y0 - 8, size + 16, size + 16, white);   // quiet zone
            for (var r = 0; r < n; r++) for (var c = 0; c < n; c++) if (qr[r][c]) cv.DrawRect(x0 + c * cell, y0 + r * cell, cell, cell, black);
            doc.EndPage();
            doc.Close();
        }
        return ms.ToArray();
    }
}
