using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Entities;
using SmartLMS.Infrastructure.Persistence;
using SmartLMS.Infrastructure.Services;

namespace SmartLMS.Tests;

/// <summary>Real API on SQLite (as ApiFactory) but with the REAL EmailOtpSender + SMTP pickup-directory
/// transport instead of the capturing fake: the full register -> .eml outbox -> verify -> login flow,
/// plus the public-registration role rules (Student default, Parent behind the tenant flag, staff rejected).</summary>
public sealed class MailFlowFactory : ApiFactory
{
    public string MailDir { get; } = Path.Combine(Path.GetTempPath(), $"smartlms-pickup-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Email:Enabled", "true");
        builder.UseSetting("Email:FromAddress", "no-reply@smartlms.local");
        builder.UseSetting("Email:PickupDirectory", MailDir);
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<IOtpSender>();
            s.AddScoped<IOtpSender, EmailOtpSender>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(MailDir)) try { Directory.Delete(MailDir, true); } catch { }
    }
}

public sealed class MailFlowTests : IClassFixture<MailFlowFactory>
{
    private readonly MailFlowFactory _f;
    private readonly HttpClient _c;
    private const string PW = ApiFactory.Password;
    private static string Fp(string s) => "fingerprint-" + s + "-0123456789";
    private static string U() => Guid.NewGuid().ToString("N")[..8];
    private static object Device(string fp) => new { fingerprint = Fp(fp), deviceType = "Mobile", platform = "test", deviceName = "t" };
    public MailFlowTests(MailFlowFactory f) { _f = f; _c = f.CreateClient(); }

    private async Task<(string Slug, string Id)> NewTenant(string sa, string fp)
    {
        var slug = "t" + U();
        var r = await _c.Post("/tenants", new { name = slug, slug, adminFullName = "Admin", adminEmail = $"admin@{slug}.test", adminPassword = PW, maxMobileDevices = 5, maxWebDevices = 5 }, sa, null, fp);
        Assert.Equal(201, r.Status);
        return (slug, r.Data.GetProperty("id").GetString()!);
    }

    private async Task<string> CodeFromOutbox(string to)
    {
        // The register request awaited the send; the .eml is already on disk.
        var files = Directory.GetFiles(_f.MailDir, "*.eml");
        Assert.NotEmpty(files);
        var eml = await File.ReadAllTextAsync(files.OrderByDescending(File.GetCreationTimeUtc).First());
        Assert.Contains(to, eml);
        return Regex.Match(eml, @"\b\d{6}\b").Value;
    }

    [Fact]
    public async Task Register_writes_real_eml_then_verify_then_login_as_student()
    {
        var sd = "sa" + U();
        var sa = (await _c.Post("/auth/login", new { email = ApiFactory.SaEmail, password = PW, device = Device(sd) })).Data.GetProperty("accessToken").GetString()!;
        var (slug, _) = await NewTenant(sa, Fp(sd));

        var email = $"s{U()}@mail.test";
        var reg = await _c.Post("/auth/register", new { fullName = "Flow Student", email, password = PW }, tenant: slug);
        Assert.True(reg.Success, reg.Body.ToString());

        var code = await CodeFromOutbox(email);
        var verify = await _c.Post("/auth/verify-email", new { email, code }, tenant: slug);
        Assert.True(verify.Success, verify.Body.ToString());

        var login = await _c.Post("/auth/login", new { email, password = PW, device = Device("fs" + U()) }, tenant: slug);
        Assert.Equal(200, login.Status);
        Assert.Contains("Student", login.Data.GetProperty("user").GetProperty("roles").EnumerateArray().Select(r => r.GetString()));

        var mode = await _c.Get("/auth/mail-delivery");
        Assert.Equal("local-outbox", mode.Data.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task Public_registration_role_rules_are_enforced()
    {
        var sd = "sa" + U();
        var sa = (await _c.Post("/auth/login", new { email = ApiFactory.SaEmail, password = PW, device = Device(sd) })).Data.GetProperty("accessToken").GetString()!;
        var (slug, tid) = await NewTenant(sa, Fp(sd));

        // Staff roles are rejected by the request validator (400), not silently downgraded.
        var staff = await _c.Post("/auth/register", new { fullName = "Wannabe", email = $"w{U()}@mail.test", password = PW, role = "Instructor" }, tenant: slug);
        Assert.Equal(400, staff.Status);

        // Parent requires the tenant feature flag: without it the API says so plainly (403).
        var parentEmail = $"p{U()}@mail.test";
        var blocked = await _c.Post("/auth/register", new { fullName = "Parent One", email = parentEmail, password = PW, role = "Parent" }, tenant: slug);
        Assert.Equal(403, blocked.Status);
        Assert.Equal("PARENT_REGISTRATION_DISABLED", blocked.Code);

        // Enable the flag for THIS tenant, then Parent registration works and yields the Parent role.
        using (var scope = _f.Services.CreateScope())
        {
            using var _ = scope.ServiceProvider.GetRequiredService<ITenantContext>().Use(Guid.Parse(tid));
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var flag = await db.TenantFeatureFlags.SingleOrDefaultAsync(x => x.FeatureKey == "parent_registration");
            if (flag is null) db.TenantFeatureFlags.Add(new TenantFeatureFlag { TenantId = Guid.Parse(tid), FeatureKey = "parent_registration", IsEnabled = true });
            else flag.IsEnabled = true;
            await db.SaveChangesAsync();
        }
        var ok = await _c.Post("/auth/register", new { fullName = "Parent One", email = parentEmail, password = PW, role = "Parent" }, tenant: slug);
        Assert.True(ok.Success, ok.Body.ToString());
        Assert.Contains("Parent", ok.Data.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }
}
