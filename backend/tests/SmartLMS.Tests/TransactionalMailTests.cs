using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartLMS.Domain.Enums;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Tests;

/// <summary>Transactional/learning emails captured through the real pipeline (TransactionalMailer -> IEmailTransport).</summary>
public sealed class TransactionalMailTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    private readonly HttpClient _c;
    private const string PW = ApiFactory.Password;
    private static string Fp(string s) => "fingerprint-" + s + "-0123456789";
    private static string U() => Guid.NewGuid().ToString("N")[..8];
    private static object Device(string fp) => new { fingerprint = Fp(fp), deviceType = "Mobile", platform = "test", deviceName = "t" };
    public TransactionalMailTests(ApiFactory f) { _f = f; _c = f.CreateClient(); }

    private sealed record Who(string Token, string Id, string Dev, string Email);
    private sealed record T(string Slug, Who Admin);

    private async Task<Who> Login(string slug, string email, string dev, string? id = null)
    {
        var r = await _c.Post("/auth/login", new { email, password = PW, device = Device(dev) }, tenant: slug);
        Assert.True(r.Success, r.Body.ToString());
        return new Who(r.Data.GetProperty("accessToken").GetString()!, id ?? "", Fp(dev), email);
    }
    private async Task<T> NewTenant()
    {
        var sa = (await _c.Post("/auth/login", new { email = ApiFactory.SaEmail, password = PW, device = Device("sa") })).Data.GetProperty("accessToken").GetString()!;
        var slug = "t" + U(); var admin = $"admin@{slug}.test";
        var r = await _c.Post("/tenants", new { name = slug, slug, adminFullName = "Admin", adminEmail = admin, adminPassword = PW, maxMobileDevices = 5, maxWebDevices = 5 }, sa, null, Fp("sa"));
        Assert.Equal(201, r.Status);
        return new T(slug, await Login(slug, admin, "adm" + slug));
    }
    private async Task<Who> Instructor(T t)
    {
        var email = $"in{U()}@mail.test";
        var r = await _c.Post("/users", new { fullName = "Inst", email, password = PW, roles = new[] { "Instructor" } }, t.Admin.Token, t.Slug, t.Admin.Dev);
        Assert.Equal(201, r.Status);
        return await Login(t.Slug, email, "in" + U(), r.Data.GetProperty("id").GetString());
    }
    private async Task<Who> Student(T t)
    {
        var email = $"s{U()}@mail.test";
        var r = await _c.Post("/auth/register", new { fullName = "Student", email, password = PW }, tenant: t.Slug);
        await _c.Post("/auth/verify-email", new { email, code = _f.Otp.Last(email, "EmailVerification") }, tenant: t.Slug);
        return await Login(t.Slug, email, "st" + U(), r.Data.GetProperty("id").GetString());
    }
    private Task<Resp> Post(string p, object? b, Who w, T t) => _c.Post(p, b, w.Token, t.Slug, w.Dev);
    private Task<Resp> Get(string p, Who w, T t) => _c.Get(p, w.Token, t.Slug, w.Dev);
    private Task<Resp> Put(string p, object? b, Who w, T t) => _c.Send(HttpMethod.Put, p, b, w.Token, t.Slug, w.Dev);
    private static string Id(Resp r) => r.Data.GetProperty("id").GetString()!;

    [Fact]
    public async Task Welcome_enrollment_receipt_and_completion_emails_are_sent()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t);

        // 1. welcome email after email verification (student was verified inside Student())
        var welcome = _f.Mail.For(st.Email);
        Assert.Single(welcome); Assert.Contains("Welcome", welcome[0].Subject); Assert.Contains("account is ready", welcome[0].Body);

        // 2. free-course enrollment confirmation
        var free = Id(await Post("/courses", new { title = "Free " + U() }, inst, t));
        var sec = Id(await Post($"/courses/{free}/sections", new { title = "S1" }, inst, t));
        var les = Id(await Post($"/sections/{sec}/lessons", new { title = "L1", lessonType = "Article", contentMarkdown = "body" }, inst, t));
        Assert.Equal(200, (await Post($"/courses/{free}/submit-for-approval", null, inst, t)).Status);
        Assert.Equal(200, (await Post($"/courses/{free}/approve", null, t.Admin, t)).Status);
        Assert.Equal(200, (await Post($"/courses/{free}/enroll", null, st, t)).Status);
        Assert.Contains(_f.Mail.For(st.Email), m => m.Subject.Contains("enrolled") && m.Subject.Contains("Free"));

        // 3. paid course: receipt + enrollment after wallet checkout
        var paid = Id(await Post("/courses", new { title = "Paid " + U() }, inst, t));
        var sec2 = Id(await Post($"/courses/{paid}/sections", new { title = "S1" }, inst, t));
        await Post($"/sections/{sec2}/lessons", new { title = "L1", lessonType = "Article", contentMarkdown = "body" }, inst, t);
        Assert.Equal(200, (await Post($"/courses/{paid}/submit-for-approval", null, inst, t)).Status);
        Assert.Equal(200, (await Post($"/courses/{paid}/approve", null, t.Admin, t)).Status);
        await using (var scope = _f.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Courses.IgnoreQueryFilters().Where(x => x.Id == Guid.Parse(paid)).ExecuteUpdateAsync(x => x.SetProperty(e => e.PricingModel, CoursePricingModel.OneTime).SetProperty(e => e.Price, 100m));
        }
        Assert.Equal(200, (await Post("/cart/items", new { itemType = "Course", itemId = paid }, st, t)).Status);
        var batch = await Post("/voucher-batches", new { name = "B" + U(), prefix = "TST", quantity = 1, voucherType = "WalletCredit", walletAmount = 150m }, t.Admin, t);
        Assert.True(batch.Success, batch.Body.ToString());
        // read the plain code from the export CSV
        var csvReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/voucher-batches/{batch.Data.GetProperty("batchId").GetString()}/export");
        csvReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", t.Admin.Token);
        csvReq.Headers.Add("X-Tenant-Id", t.Slug); csvReq.Headers.Add("X-Device-Id", t.Admin.Dev);
        var csv = await (await _c.SendAsync(csvReq)).Content.ReadAsStringAsync();
        var code = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Split(',')[0].Trim()).First(x => x.StartsWith("TST"));
        Assert.Equal(200, (await Post("/wallet/topup/voucher", new { code }, st, t)).Status);
        var order = await Post("/orders/checkout", new { paymentMethod = "Wallet" }, st, t);
        Assert.True(order.Success, order.Body.ToString());
        var mail3 = _f.Mail.For(st.Email);
        Assert.Contains(mail3, m => m.Subject.Contains("Receipt") && m.Body.Contains("100.00"));
        Assert.Contains(mail3, m => m.Subject.Contains("enrolled") && m.Subject.Contains("Paid"));

        // 4. completing every lesson of the free course sends the completion email once
        Assert.Equal(200, (await Put($"/lessons/{les}/progress", new { lastPositionSeconds = 0, watchedSecondsDelta = 0, completed = true, playbackSpeed = 1.0 }, st, t)).Status);
        var done = _f.Mail.For(st.Email).Where(m => m.Subject.Contains("Congratulations")).ToList();
        Assert.Single(done); Assert.Contains("certificate", done[0].Body);
        // an Arabic-speaking student gets Arabic subjects
        var ar = await Student(t);
        await using (var scope = _f.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Users.IgnoreQueryFilters().Where(u => u.Id == Guid.Parse(ar.Id)).ExecuteUpdateAsync(u => u.SetProperty(x => x.PreferredLanguage, "ar"));
        }
        Assert.Equal(200, (await Post($"/courses/{free}/enroll", null, ar, t)).Status);
        Assert.Contains(_f.Mail.For(ar.Email), m => m.Subject.Contains("تم تسجيلك"));
    }
}
