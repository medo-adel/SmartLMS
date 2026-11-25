using SmartLMS.Infrastructure.Services.Lms;

namespace SmartLMS.Tests;

public sealed class CertificatePdfTests
{
    public const string Url = "https://lms.example.test/api/v1/certificates/verify/ABC123_-xyz";

    [Fact]
    public void Arabic_mixed_and_latin_names_render_to_a_pdf_with_an_embedded_font()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "smartlms-cert-samples"); Directory.CreateDirectory(outDir);
        var cases = new Dictionary<string, (string Name, string Course)>
        {
            ["arabic"] = ("محمد عادل الشريف", "مقدمة في البرمجة بلغة سي شارب"),
            ["mixed"] = ("Mohamed محمد عادل Adel", "دورة SQL Server للمبتدئين 2026"),
            ["latin"] = ("Mohamed Adel", "Intro to Programming"),
        };
        foreach (var (k, v) in cases)
        {
            var pdf = CertificatePdf.Build(v.Name, v.Course, new DateTime(2026, 11, 24, 0, 0, 0, DateTimeKind.Utc), Url);
            Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(pdf, 0, 5));
            Assert.True(pdf.Length > 5000);
            var text = System.Text.Encoding.Latin1.GetString(pdf);
            Assert.Contains("/FontFile2", text);                    // the font program is embedded, not referenced by name only
            File.WriteAllBytes(Path.Combine(outDir, $"cert-{k}.pdf"), pdf);
        }
    }

    [Fact]
    public void Visual_order_puts_runs_right_to_left_for_an_arabic_base_and_left_to_right_otherwise()
    {
        var rtl = CertificatePdf.VisualRuns("محمد Adel");
        Assert.Equal(new[] { "Adel", "محمد" }.Select(x => x.Trim()), rtl.Select(r => r.Text.Trim()));   // Arabic is first logically, so it lands on the right
        var ltr = CertificatePdf.VisualRuns("Adel محمد");
        Assert.Equal(new[] { "Adel", "محمد" }, ltr.Select(r => r.Text.Trim()));
        Assert.Single(CertificatePdf.VisualRuns("Mohamed Adel"));
        Assert.Contains(CertificatePdf.VisualRuns("دورة SQL 2026"), r => r.Text.Contains("SQL") && !r.Rtl);
    }
}
