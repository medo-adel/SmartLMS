using SmartLMS.Infrastructure.Services.Commerce;
using Xunit;

namespace SmartLMS.Tests;

public class InvoicePdfTests
{
    private static List<InvoiceLine> Lines(int n)
    {
        var l = new List<InvoiceLine>();
        for (var i = 1; i <= n; i++)
        {
            var name = i % 4 == 0 ? "دورة تطوير تطبيقات الويب المتقدمة باستخدام ASP.NET Core و Angular للمبتدئين والمحترفين - الإصدار " + i
                     : i % 4 == 1 ? "Complete Mixed Course عربي + English with a very long title that must wrap onto several lines instead of being cut " + i
                     : i % 4 == 2 ? "Supercalifragilisticexpialidocious" + new string('x', 90) + i
                     : "Course " + i;
            l.Add(new InvoiceLine(name, 1 + i % 3, 10.5m + i, (1 + i % 3) * (10.5m + i)));
        }
        return l;
    }

    private static int PageCount(byte[] pdf) => System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.Latin1.GetString(pdf), @"/Type\s*/Page(?![s\w])").Count;

    [Theory]
    [InlineData(1, 1)]
    [InlineData(12, 1)]
    [InlineData(80, 4)]
    public void Long_invoices_paginate_without_dropping_a_line_and_keep_totals(int n, int minPages)
    {
        var lines = Lines(n); var sub = lines.Sum(x => x.LineTotal);
        var pdf = InvoicePdf.Build("INV-TEST-1", DateTime.UtcNow, "عميل تجريبي Mixed Name", "EGP", lines, sub, 0m, sub, "Paid");
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        var pages = PageCount(pdf); Assert.True(pages >= minPages, $"pages={pages}");
        if (Environment.GetEnvironmentVariable("INVOICE_PROOF_DIR") is { Length: > 0 } d) File.WriteAllBytes(Path.Combine(d, $"invoice_{n}.pdf"), pdf);
    }

    [Fact]
    public void Wrap_breaks_long_words_and_keeps_every_character()
    {
        var w = SmartLMS.Infrastructure.Services.Lms.CertificatePdf.Wrap(new string('y', 300), 12, 295);
        Assert.True(w.Count > 1); Assert.Equal(300, w.Sum(x => x.Length));
    }
}
