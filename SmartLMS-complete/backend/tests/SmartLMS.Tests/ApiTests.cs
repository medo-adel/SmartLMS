using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Domain.Exceptions;
using SmartLMS.Infrastructure.Identity;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Tests;

public sealed class ApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    private readonly HttpClient _c;
    private const string PW = ApiFactory.Password;
    private static string Fp(string s) => "fingerprint-" + s + "-0123456789";

    public ApiTests(ApiFactory f) { _f = f; _c = f.CreateClient(); }

    private static string U() => Guid.NewGuid().ToString("N")[..8];

    private static object Device(string fp, string type = "Mobile") => new { fingerprint = Fp(fp), deviceType = type, platform = "test", deviceName = "t" };

    private async Task<string> SaToken()
    {
        var r = await _c.Post("/auth/login", new { email = ApiFactory.SaEmail, password = PW, device = Device("sa") });
        Assert.True(r.Success, r.Body.ToString());
        return r.Data.GetProperty("accessToken").GetString()!;
    }

    /// <summary>Creates a tenant through the API and returns (tenantId, slug, adminEmail).</summary>
    private async Task<(string Id, string Slug, string Admin)> NewTenant(int mobile = 1, int web = 1)
    {
        var slug = "t" + U();
        var admin = $"admin@{slug}.test";
        var r = await _c.Post("/tenants", new { name = slug, slug, adminFullName = "Admin", adminEmail = admin, adminPassword = PW, maxMobileDevices = mobile, maxWebDevices = web }, await SaToken(), null, Fp("sa"));
        Assert.Equal(201, r.Status);
        return (r.Data.GetProperty("id").GetString()!, slug, admin);
    }

    private async Task<string> Login(string slug, string email, string dev, string type = "Mobile")
    {
        var r = await _c.Post("/auth/login", new { email, password = PW, device = Device(dev, type) }, tenant: slug);
        Assert.True(r.Success, r.Body.ToString());
        return r.Data.GetProperty("accessToken").GetString()!;
    }

    private async Task<(string UserId, string Email)> RegisterStudent(string slug)
    {
        var email = $"s{U()}@mail.test";
        var r = await _c.Post("/auth/register", new { fullName = "Student", email, password = PW }, tenant: slug);
        Assert.Equal(201, r.Status);
        var v = await _c.Post("/auth/verify-email", new { email, code = _f.Otp.Last(email, "EmailVerification") }, tenant: slug);
        Assert.True(v.Success, v.Body.ToString());
        return (r.Data.GetProperty("id").GetString()!, email);
    }

    [Fact]
    public async Task Health_works_without_tenant()
    {
        var r = await _c.Get("/health");
        Assert.Equal(200, r.Status);
        Assert.True(r.Success);
    }

    [Fact]
    public async Task Invalid_body_returns_400_envelope()
    {
        var t = await NewTenant();
        var r = await _c.Post("/auth/register", new { fullName = "", email = "nope", password = "short" }, tenant: t.Slug);
        Assert.Equal(400, r.Status);
        Assert.False(r.Success);
        Assert.Equal("VALIDATION_FAILED", r.Code);
        Assert.True(r.Body.GetProperty("errors").GetArrayLength() >= 3);
    }

    [Fact]
    public async Task Tenant_is_required_for_tenant_routes()
    {
        var r = await _c.Post("/auth/register", new { fullName = "A", email = "a@b.test", password = PW });
        Assert.Equal(400, r.Status);
        Assert.Equal("TENANT_REQUIRED", r.Code);
    }

    [Fact]
    public async Task Register_requires_verification_then_login_and_me_work()
    {
        var t = await NewTenant();
        var email = $"s{U()}@mail.test";
        await _c.Post("/auth/register", new { fullName = "Stu", email, password = PW }, tenant: t.Slug);
        var early = await _c.Post("/auth/login", new { email, password = PW, device = Device("a") }, tenant: t.Slug);
        Assert.Equal(403, early.Status);
        Assert.Equal("EMAIL_NOT_CONFIRMED", early.Code);
        var bad = await _c.Post("/auth/verify-email", new { email, code = "000000" }, tenant: t.Slug);
        Assert.Equal(400, bad.Status);
        Assert.True((await _c.Post("/auth/verify-email", new { email, code = _f.Otp.Last(email, "EmailVerification") }, tenant: t.Slug)).Success);
        var token = await Login(t.Slug, email, "a");
        var me = await _c.Get("/me", token, t.Slug, Fp("a"));
        Assert.Equal(200, me.Status);
        Assert.Equal(email, me.Data.GetProperty("email").GetString());
        Assert.Equal("Student", me.Data.GetProperty("roles")[0].GetString());
    }

    [Fact]
    public async Task Public_registration_cannot_pick_staff_roles_and_parent_needs_flag()
    {
        var t = await NewTenant();
        var staff = await _c.Post("/auth/register", new { fullName = "X", email = $"x{U()}@m.test", password = PW, role = "TenantAdmin" }, tenant: t.Slug);
        Assert.Equal(400, staff.Status);
        var parent = await _c.Post("/auth/register", new { fullName = "P", email = $"p{U()}@m.test", password = PW, role = "Parent" }, tenant: t.Slug);
        Assert.Equal(403, parent.Status);
        Assert.Equal("PARENT_REGISTRATION_DISABLED", parent.Code);
    }

    [Fact]
    public async Task Wrong_or_missing_device_header_is_rejected()
    {
        var t = await NewTenant();
        var token = await Login(t.Slug, t.Admin, "adm");
        Assert.Equal(200, (await _c.Get("/me", token, t.Slug, Fp("adm"))).Status);
        var wrong = await _c.Get("/me", token, t.Slug, Fp("other"));
        Assert.Equal(403, wrong.Status);
        Assert.Equal("DEVICE_MISMATCH", wrong.Code);
        var none = await _c.Get("/me", token, t.Slug);
        Assert.Equal(403, none.Status);
        Assert.Equal("DEVICE_REQUIRED", none.Code);
    }

    [Fact]
    public async Task Lockout_after_five_failures()
    {
        var t = await NewTenant();
        for (var i = 0; i < 5; i++)
        {
            var r = await _c.Post("/auth/login", new { email = t.Admin, password = "Wrong-Passw0rd!", device = Device("l") }, tenant: t.Slug);
            Assert.Equal(401, r.Status);
        }
        var locked = await _c.Post("/auth/login", new { email = t.Admin, password = PW, device = Device("l") }, tenant: t.Slug);
        Assert.Equal(403, locked.Status);
        Assert.Equal("ACCOUNT_LOCKED", locked.Code);
    }

    [Fact]
    public async Task Refresh_rotates_and_reuse_revokes_the_chain()
    {
        var t = await NewTenant();
        var l = await _c.Post("/auth/login", new { email = t.Admin, password = PW, device = Device("r") }, tenant: t.Slug);
        var r1 = l.Data.GetProperty("refreshToken").GetString()!;
        var rot = await _c.Post("/auth/refresh", new { refreshToken = r1, deviceFingerprint = Fp("r") }, tenant: t.Slug);
        Assert.True(rot.Success, rot.Body.ToString());
        var r2 = rot.Data.GetProperty("refreshToken").GetString()!;
        Assert.NotEqual(r1, r2);
        var reuse = await _c.Post("/auth/refresh", new { refreshToken = r1, deviceFingerprint = Fp("r") }, tenant: t.Slug);
        Assert.Equal(401, reuse.Status);
        Assert.Equal("REFRESH_TOKEN_REUSED", reuse.Code);
        var after = await _c.Post("/auth/refresh", new { refreshToken = r2, deviceFingerprint = Fp("r") }, tenant: t.Slug);
        Assert.Equal(401, after.Status);
    }

    [Fact]
    public async Task Refresh_from_another_device_is_rejected()
    {
        var t = await NewTenant();
        var l = await _c.Post("/auth/login", new { email = t.Admin, password = PW, device = Device("d1") }, tenant: t.Slug);
        var r = await _c.Post("/auth/refresh", new { refreshToken = l.Data.GetProperty("refreshToken").GetString(), deviceFingerprint = Fp("d2") }, tenant: t.Slug);
        Assert.Equal(403, r.Status);
        Assert.Equal("DEVICE_MISMATCH", r.Code);
    }

    [Fact]
    public async Task Student_device_cap_and_admin_reset()
    {
        var t = await NewTenant(mobile: 1, web: 1);
        var (uid, email) = await RegisterStudent(t.Slug);
        var tok1 = await Login(t.Slug, email, "phone1");
        // Second mobile device is refused; a web device is a separate allowance.
        var second = await _c.Post("/auth/login", new { email, password = PW, device = Device("phone2") }, tenant: t.Slug);
        Assert.Equal(403, second.Status);
        Assert.Equal("DEVICE_LIMIT_REACHED", second.Code);
        Assert.True((await _c.Post("/auth/login", new { email, password = PW, device = Device("browser1", "Web") }, tenant: t.Slug)).Success);

        var admin = await Login(t.Slug, t.Admin, "adm");
        var reset = await _c.Post($"/users/{uid}/devices/reset", new { reason = "lost phone" }, admin, t.Slug, Fp("adm"));
        Assert.Equal(200, reset.Status);
        Assert.Equal(2, reset.Data.GetInt32());
        // Old session dies immediately, even though the access token has not expired.
        Assert.Equal(403, (await _c.Get("/me", tok1, t.Slug, Fp("phone1"))).Status);
        Assert.True((await _c.Post("/auth/login", new { email, password = PW, device = Device("phone2") }, tenant: t.Slug)).Success);

        using var scope = _f.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(Guid.Parse(t.Id), false);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.DeviceResetLogs.CountAsync(x => x.UserId == Guid.Parse(uid)));
        Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.Action == AuditLogAction.ResetDevice));
    }

    [Fact]
    public async Task Device_cap_does_not_apply_to_staff()
    {
        var t = await NewTenant();
        await Login(t.Slug, t.Admin, "adm1");
        await Login(t.Slug, t.Admin, "adm2");
    }

    [Fact]
    public async Task Token_for_one_tenant_cannot_be_used_with_another_tenant_header()
    {
        var a = await NewTenant();
        var b = await NewTenant();
        var token = await Login(a.Slug, a.Admin, "adm");
        var r = await _c.Get("/me", token, b.Slug, Fp("adm"));
        Assert.Equal(403, r.Status);
        Assert.Equal("TENANT_MISMATCH", r.Code);
    }

    [Fact]
    public async Task Same_email_in_two_tenants_stays_separate()
    {
        var a = await NewTenant();
        var b = await NewTenant();
        var email = $"shared{U()}@mail.test";
        foreach (var t in new[] { a, b })
        {
            await _c.Post("/auth/register", new { fullName = "Shared " + t.Slug, email, password = PW }, tenant: t.Slug);
            await _c.Post("/auth/verify-email", new { email, code = _f.Otp.Last(email, "EmailVerification") }, tenant: t.Slug);
        }
        var adminA = await Login(a.Slug, a.Admin, "adm");
        var list = await _c.Get($"/users?search={email}", adminA, a.Slug, Fp("adm"));
        Assert.Equal(1, list.Data.GetProperty("totalCount").GetInt32());
        Assert.Equal("Shared " + a.Slug, list.Data.GetProperty("items")[0].GetProperty("fullName").GetString());
        // Student of A is not reachable by id from B's admin.
        var idA = list.Data.GetProperty("items")[0].GetProperty("id").GetString();
        var adminB = await Login(b.Slug, b.Admin, "admb");
        Assert.Equal(404, (await _c.Get($"/users/{idA}", adminB, b.Slug, Fp("admb"))).Status);
    }

    [Fact]
    public async Task Student_cannot_manage_users_or_tenants()
    {
        var t = await NewTenant();
        var (_, email) = await RegisterStudent(t.Slug);
        var tok = await Login(t.Slug, email, "p");
        Assert.Equal(403, (await _c.Get("/users", tok, t.Slug, Fp("p"))).Status);
        Assert.Equal(403, (await _c.Get("/tenants", tok, t.Slug, Fp("p"))).Status);
    }

    [Fact]
    public async Task SuperAdmin_needs_support_access_for_tenant_routes()
    {
        var t = await NewTenant();
        var sa = await SaToken();
        var denied = await _c.Get("/users", sa, t.Id, Fp("sa"));
        Assert.Equal(403, denied.Status);
        Assert.Equal("SUPPORT_ACCESS_REQUIRED", denied.Code);
        Assert.Equal(200, (await _c.Post($"/tenants/{t.Id}/support-access", new { minutes = 30 }, sa, null, Fp("sa"))).Status);
        Assert.Equal(200, (await _c.Get("/users", sa, t.Id, Fp("sa"))).Status);
        Assert.Equal(200, (await _c.Send(HttpMethod.Delete, $"/tenants/{t.Id}/support-access", null, sa, null, Fp("sa"))).Status);
        Assert.Equal(403, (await _c.Get("/users", sa, t.Id, Fp("sa"))).Status);
    }

    [Fact]
    public async Task Admin_manages_users_roles_and_deactivation()
    {
        var t = await NewTenant();
        var admin = await Login(t.Slug, t.Admin, "adm");
        var email = $"ins{U()}@mail.test";
        var created = await _c.Post("/users", new { fullName = "Ins", email, password = PW, roles = new[] { "Instructor" } }, admin, t.Slug, Fp("adm"));
        Assert.Equal(201, created.Status);
        var id = created.Data.GetProperty("id").GetString();
        var su = await _c.Post("/users", new { fullName = "Bad", email = $"b{U()}@m.test", password = PW, roles = new[] { "SuperAdmin" } }, admin, t.Slug, Fp("adm"));
        Assert.Equal(400, su.Status);
        var roles = await _c.Send(HttpMethod.Put, $"/users/{id}/roles", new { roles = new[] { "TeachingAssistant" } }, admin, t.Slug, Fp("adm"));
        Assert.Equal("TeachingAssistant", roles.Data.GetProperty("roles")[0].GetString());
        var tok = await Login(t.Slug, email, "i1");
        Assert.Equal(200, (await _c.Post($"/users/{id}/deactivate", null, admin, t.Slug, Fp("adm"))).Status);
        Assert.Equal(401, (await _c.Get("/me", tok, t.Slug, Fp("i1"))).Status);
        Assert.Equal(403, (await _c.Post("/auth/login", new { email, password = PW, device = Device("i1") }, tenant: t.Slug)).Status);
    }

    [Fact]
    public async Task Forgot_password_never_reveals_accounts_and_reset_works()
    {
        var t = await NewTenant();
        var ghost = await _c.Post("/auth/forgot-password", new { email = "ghost@nowhere.test" }, tenant: t.Slug);
        Assert.Equal(200, ghost.Status);
        Assert.Equal(200, (await _c.Post("/auth/forgot-password", new { email = t.Admin }, tenant: t.Slug)).Status);
        var code = _f.Otp.Last(t.Admin, "PasswordReset");
        var np = "N3w-Passw0rd-value";
        Assert.True((await _c.Post("/auth/reset-password", new { email = t.Admin, code, newPassword = np }, tenant: t.Slug)).Success);
        Assert.Equal(401, (await _c.Post("/auth/login", new { email = t.Admin, password = PW, device = Device("x") }, tenant: t.Slug)).Status);
        Assert.True((await _c.Post("/auth/login", new { email = t.Admin, password = np, device = Device("x") }, tenant: t.Slug)).Success);
        // code is single use
        Assert.Equal(400, (await _c.Post("/auth/reset-password", new { email = t.Admin, code, newPassword = "An0ther-Passw0rd" }, tenant: t.Slug)).Status);
    }

    [Fact]
    public async Task Feature_flags_and_branding()
    {
        var t = await NewTenant();
        var admin = await Login(t.Slug, t.Admin, "adm");
        var set = await _c.Send(HttpMethod.Put, "/tenant/feature-flags/parent_registration", new { isEnabled = true }, admin, t.Slug, Fp("adm"));
        Assert.True(set.Data.GetProperty("isEnabled").GetBoolean());
        var parent = await _c.Post("/auth/register", new { fullName = "P", email = $"p{U()}@m.test", password = PW, role = "Parent" }, tenant: t.Slug);
        Assert.Equal(201, parent.Status);
        var b = await _c.Get("/tenants/current/branding", tenant: t.Slug);
        Assert.Equal(t.Slug, b.Data.GetProperty("slug").GetString());
        Assert.Equal(501, (await _c.Post("/auth/social/google", null, tenant: t.Slug)).Status);
    }

    // ---- database-level guarantees --------------------------------------------------------------------------------------

    private (IServiceScope Scope, ApplicationDbContext Db, ITenantContext Tc) Scoped(Guid? tenant, bool platform = false)
    {
        var scope = _f.Services.CreateScope();
        var tc = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tc.Set(tenant, platform);
        return (scope, scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), tc);
    }

    [Fact]
    public async Task Query_filters_fail_closed_and_isolate_tenants()
    {
        var a = await NewTenant();
        var b = await NewTenant();
        var (s0, none, _) = Scoped(null);
        using (s0) { Assert.Empty(await none.Users.ToListAsync()); Assert.Empty(await none.Tenants.ToListAsync()); Assert.Empty(await none.TenantFeatureFlags.ToListAsync()); }
        var (s1, dbA, _) = Scoped(Guid.Parse(a.Id));
        using (s1)
        {
            Assert.All(await dbA.TenantFeatureFlags.ToListAsync(), f => Assert.Equal(Guid.Parse(a.Id), f.TenantId));
            Assert.Single(await dbA.Tenants.ToListAsync());
            Assert.All(await dbA.Users.ToListAsync(), u => Assert.Equal(Guid.Parse(a.Id), u.TenantId));
            Assert.Empty(await dbA.Users.Where(u => u.TenantId == Guid.Parse(b.Id)).ToListAsync());
        }
        // Platform scope never widens tenant-owned data.
        var (s2, plat, _) = Scoped(null, platform: true);
        using (s2)
        {
            Assert.Empty(await plat.TenantFeatureFlags.ToListAsync());
            Assert.True((await plat.Tenants.CountAsync()) >= 2);
        }
    }

    [Fact]
    public async Task Interceptor_rejects_writes_into_another_tenant()
    {
        var a = await NewTenant();
        var b = await NewTenant();
        var (s, db, _) = Scoped(Guid.Parse(a.Id));
        using (s)
        {
            db.TenantFeatureFlags.Add(new TenantFeatureFlag { TenantId = Guid.Parse(b.Id), FeatureKey = "wallet" });
            var ex = await Assert.ThrowsAsync<ForbiddenAppException>(() => db.SaveChangesAsync());
            Assert.Equal("TENANT_MISMATCH", ex.Code);
        }
    }

    [Fact]
    public async Task Composite_foreign_key_blocks_cross_tenant_user_reference_at_the_database()
    {
        var a = await NewTenant();
        var b = await NewTenant();
        Guid userOfB, adminOfA;
        var (sb, dbB, _) = Scoped(Guid.Parse(b.Id));
        using (sb) userOfB = (await dbB.Users.FirstAsync()).Id;
        var (sa, dbA, _) = Scoped(Guid.Parse(a.Id));
        using (sa)
        {
            adminOfA = (await dbA.Users.FirstAsync()).Id;
            // Tenant column is correct (passes the interceptor) but the user belongs to tenant B: only the composite FK can stop this.
            dbA.DeviceResetLogs.Add(new DeviceResetLog { TenantId = Guid.Parse(a.Id), UserId = userOfB, ResetByUserId = adminOfA, ResetAtUtc = DateTime.UtcNow });
            await Assert.ThrowsAsync<DbUpdateException>(() => dbA.SaveChangesAsync());
        }
        var (sa2, dbA2, _) = Scoped(Guid.Parse(a.Id));
        using (sa2)
        {
            dbA2.DeviceResetLogs.Add(new DeviceResetLog { TenantId = Guid.Parse(a.Id), UserId = adminOfA, ResetByUserId = adminOfA, ResetAtUtc = DateTime.UtcNow });
            await dbA2.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Delete_becomes_soft_delete_and_audit_rows_are_written_without_secrets()
    {
        var t = await NewTenant();
        var (_, email) = await RegisterStudent(t.Slug);
        var (s, db, _) = Scoped(Guid.Parse(t.Id));
        using (s)
        {
            var user = await db.Users.FirstAsync(u => u.Email == email);
            Assert.NotEqual(default, user.CreatedAtUtc);
            db.Users.Remove(user);
            await db.SaveChangesAsync();
            Assert.Null(await db.Users.FirstOrDefaultAsync(u => u.Id == user.Id));
            var raw = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == user.Id);
            Assert.True(raw.IsDeleted);
            Assert.NotNull(raw.DeletedAtUtc);
            var logs = await db.AuditLogs.Where(l => l.EntityId == user.Id.ToString()).ToListAsync();
            Assert.Contains(logs, l => l.Action == AuditLogAction.Create);
            Assert.Contains(logs, l => l.Action == AuditLogAction.Delete);
            Assert.DoesNotContain(logs, l => (l.NewValuesJson ?? "").Contains(PW) || (l.NewValuesJson ?? "").Contains("AQAAAA"));
            var created = logs.First(l => l.Action == AuditLogAction.Create);
            Assert.Contains("[redacted]", created.NewValuesJson);
        }
    }
}
