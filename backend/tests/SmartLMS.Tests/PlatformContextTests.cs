using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Tests;

/// <summary>
/// Real-API coverage (SQLite TestHost) for the platform/tenant boundary the frontend fix relies on:
/// platform login with a stale tenant header vs headerless, tenant failed-count semantics on the
/// wrong-password + headerless retry pair, and the expired/revoked support-access response envelope.
/// </summary>
public sealed class PlatformContextTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    private readonly HttpClient _c;
    private const string PW = ApiFactory.Password;
    private static string Fp(string s) => "fingerprint-" + s + "-0123456789";
    private static string U() => Guid.NewGuid().ToString("N")[..8];
    private static object Device(string fp) => new { fingerprint = Fp(fp), deviceType = "Mobile", platform = "test", deviceName = "t" };
    public PlatformContextTests(ApiFactory f) { _f = f; _c = f.CreateClient(); }

    private sealed record T(string Slug, string Id, string AdminEmail);
    private async Task<T> NewTenant(string sa, string fp)
    {
        var slug = "t" + U(); var admin = $"admin@{slug}.test";
        var r = await _c.Post("/tenants", new { name = slug, slug, adminFullName = "Admin", adminEmail = admin, adminPassword = PW, maxMobileDevices = 5, maxWebDevices = 5 }, sa, null, fp);
        Assert.Equal(201, r.Status);
        return new T(slug, r.Data.GetProperty("id").GetString()!, admin);
    }

    [Fact]
    public async Task Platform_login_stale_header_401_then_headerless_200()
    {
        // The retry pair must be: 401 INVALID_CREDENTIALS under a stale (but EXISTING) academy key, success without it.
        var sa1 = "sa1" + U();
        var first = await _c.Post("/auth/login", new { email = ApiFactory.SaEmail, password = PW, device = Device(sa1) });
        Assert.Equal(200, first.Status);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, first.Data.GetProperty("user").GetProperty("tenantId").ValueKind);
        var sa = first.Data.GetProperty("accessToken").GetString()!;
        var t = await NewTenant(sa, Fp(sa1));

        var withHeader = await _c.Post("/auth/login", new { email = ApiFactory.SaEmail, password = PW, device = Device("sa1" + U()) }, tenant: t.Slug);
        Assert.Equal(401, withHeader.Status);
        Assert.Equal("INVALID_CREDENTIALS", withHeader.Code);

        var headerless = await _c.Post("/auth/login", new { email = ApiFactory.SaEmail, password = PW, device = Device("sa1" + U()) });
        Assert.Equal(200, headerless.Status);
        Assert.True(headerless.Success);
    }

    [Fact]
    public async Task Tenant_wrong_password_pair_counts_exactly_one_failure()
    {
        var sa2 = "sa2" + U();
        var sa = (await _c.Post("/auth/login", new { email = ApiFactory.SaEmail, password = PW, device = Device(sa2) })).Data.GetProperty("accessToken").GetString()!;
        var t = await NewTenant(sa, Fp(sa2));

        // Attempt 1, WITH the academy header (what the app sends first): user matched, password wrong -> 401 and +1.
        var wrong = await _c.Post("/auth/login", new { email = t.AdminEmail, password = "Wr0ngPassw0rd!", device = Device("tp" + U()) }, tenant: t.Slug);
        Assert.Equal(401, wrong.Status);
        Assert.Equal("INVALID_CREDENTIALS", wrong.Code);
        Assert.Equal(1, await FailedCount(t.Id, t.AdminEmail));

        // Attempt 2, headerless (the frontend's retry): /auth is a platform route, and user names are
        // tenant-scoped, so the tenant admin matches NO account (platform:<email> does not exist) ->
        // 401 INVALID_CREDENTIALS, and no failed-attempt increment: still 1.
        var headerless = await _c.Post("/auth/login", new { email = t.AdminEmail, password = "Wr0ngPassw0rd!", device = Device("tp" + U()) });
        Assert.Equal(401, headerless.Status);
        Assert.Equal("INVALID_CREDENTIALS", headerless.Code);
        Assert.Equal(1, await FailedCount(t.Id, t.AdminEmail));

        // Correct password with the header: succeeds and resets the counter.
        var ok = await _c.Post("/auth/login", new { email = t.AdminEmail, password = PW, device = Device("tp" + U()) }, tenant: t.Slug);
        Assert.Equal(200, ok.Status);
        Assert.Equal(0, await FailedCount(t.Id, t.AdminEmail));
    }

    [Fact]
    public async Task Mail_delivery_endpoint_reports_disabled_without_email_config()
    {
        // The register UI reads this so it never promises an inbox this server cannot deliver to.
        var r = await _c.Get("/auth/mail-delivery");
        Assert.True(r.Status == 200, r.Body.ToString());
        Assert.Equal("disabled", r.Data.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task Support_access_granted_revoked_and_expired_envelopes()
    {
        var sa3 = "sa3" + U();
        var saLogin = await _c.Post("/auth/login", new { email = ApiFactory.SaEmail, password = PW, device = Device(sa3) });
        var sa = saLogin.Data.GetProperty("accessToken").GetString()!;
        var saId = saLogin.Data.GetProperty("user").GetProperty("id").GetString()!;
        var t = await NewTenant(sa, Fp(sa3));

        // No grant yet: 403 with the machine-readable envelope the frontend watches for.
        var noGrant = await _c.Get("/audit-logs", sa, t.Slug, Fp(sa3));
        Assert.Equal(403, noGrant.Status);
        Assert.Equal("SUPPORT_ACCESS_REQUIRED", noGrant.Code);

        // Grant through the real endpoint: the tenant route now answers.
        var grant = await _c.Post($"/tenants/{t.Id}/support-access", new { minutes = 60 }, sa, null, Fp(sa3));
        Assert.True(grant.Success, grant.Body.ToString());
        var inContext = await _c.Get("/audit-logs", sa, t.Slug, Fp(sa3));
        Assert.Equal(200, inContext.Status);

        // Revoked through the real endpoint: back to 403 SUPPORT_ACCESS_REQUIRED.
        var revoke = await _c.Send(HttpMethod.Delete, $"/tenants/{t.Id}/support-access", null, sa, null, Fp(sa3));
        Assert.True(revoke.Success, revoke.Body.ToString());
        var afterRevoke = await _c.Get("/audit-logs", sa, t.Slug, Fp(sa3));
        Assert.Equal(403, afterRevoke.Status);
        Assert.Equal("SUPPORT_ACCESS_REQUIRED", afterRevoke.Code);

        // Expired (the grant endpoint clamps minutes to >= 1, so insert a lapsed row directly): same 403 envelope.
        using (var scope = _f.Services.CreateScope())
        {
            using var _ = scope.ServiceProvider.GetRequiredService<SmartLMS.Application.Interfaces.ITenantContext>().Use(Guid.Parse(t.Id));
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var m = await db.UserTenantMemberships.SingleAsync(x => x.UserId == Guid.Parse(saId));
            m.Kind = UserTenantMembershipKind.SupportAccess; m.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5); m.RevokedAtUtc = null;
            await db.SaveChangesAsync();
        }
        var expired = await _c.Get("/audit-logs", sa, t.Slug, Fp(sa3));
        Assert.Equal(403, expired.Status);
        Assert.Equal("SUPPORT_ACCESS_REQUIRED", expired.Code);
    }

    private async Task<int> FailedCount(string tenantId, string email)
    {
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Users.IgnoreQueryFilters().Where(u => u.Email == email).Select(u => u.AccessFailedCount).SingleAsync();
    }
}
