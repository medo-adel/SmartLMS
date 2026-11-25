using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Infrastructure;
using SmartLMS.Infrastructure.Options;
using SmartLMS.Infrastructure.Persistence;
using SmartLMS.Infrastructure.Services;

namespace SmartLMS.Tests;

public sealed class SecurityTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    private readonly HttpClient _c;
    private const string PW = ApiFactory.Password;
    private static string Fp(string s) => "fingerprint-" + s + "-0123456789";
    private static string U() => Guid.NewGuid().ToString("N")[..8];
    private static object Device(string fp, string type = "Mobile") => new { fingerprint = Fp(fp), deviceType = type, platform = "test", deviceName = "t" };

    public SecurityTests(ApiFactory f) { _f = f; _c = f.CreateClient(); }

    private async Task<string> SaToken() =>
        (await _c.Post("/auth/login", new { email = ApiFactory.SaEmail, password = PW, device = Device("sa") })).Data.GetProperty("accessToken").GetString()!;

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

    private async Task<(string Id, string Email)> RegisterStudent(string slug)
    {
        var email = $"s{U()}@mail.test";
        var r = await _c.Post("/auth/register", new { fullName = "Student", email, password = PW }, tenant: slug);
        await _c.Post("/auth/verify-email", new { email, code = _f.Otp.Last(email, "EmailVerification") }, tenant: slug);
        return (r.Data.GetProperty("id").GetString()!, email);
    }

    private async Task<(string Id, string Email)> CreateStaff(string slug, string adminToken, string adminDevice, string role)
    {
        var email = $"{role.ToLowerInvariant()}{U()}@mail.test";
        var r = await _c.Post("/users", new { fullName = role, email, password = PW, roles = new[] { role } }, adminToken, slug, Fp(adminDevice));
        Assert.Equal(201, r.Status);
        return (r.Data.GetProperty("id").GetString()!, email);
    }

    // ---- device cap under concurrency ----------------------------------------------------------------------------------

    [Theory]
    [InlineData(1, 8)]
    [InlineData(2, 10)]
    public async Task Parallel_logins_never_exceed_the_device_cap(int cap, int attempts)
    {
        var t = await NewTenant(mobile: cap);
        var (uid, email) = await RegisterStudent(t.Slug);
        var results = await Task.WhenAll(Enumerable.Range(0, attempts).Select(i =>
            _c.Post("/auth/login", new { email, password = PW, device = Device("race" + i) }, tenant: t.Slug)));
        Assert.True(cap == results.Count(r => r.Success), string.Join(" | ", results.Select(r => $"{r.Status}:{r.Code}")));
        Assert.All(results.Where(r => !r.Success), r => Assert.Equal("DEVICE_LIMIT_REACHED", r.Code));
        using var scope = _f.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(Guid.Parse(t.Id), false);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(cap, await db.UserDevices.CountAsync(d => d.UserId == Guid.Parse(uid) && d.IsActive));
    }

    // ---- refresh rotation / revocation ---------------------------------------------------------------------------------

    [Fact]
    public async Task Parallel_refresh_with_one_token_lets_exactly_one_request_win()
    {
        var t = await NewTenant();
        var l = await _c.Post("/auth/login", new { email = t.Admin, password = PW, device = Device("pr") }, tenant: t.Slug);
        var rt = l.Data.GetProperty("refreshToken").GetString()!;
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => _c.Post("/auth/refresh", new { refreshToken = rt, deviceFingerprint = Fp("pr") }, tenant: t.Slug)));
        Assert.Equal(1, results.Count(r => r.Success));
        Assert.All(results.Where(r => !r.Success), r => Assert.Equal(401, r.Status));
        // The original token is spent for good.
        Assert.Equal(401, (await _c.Post("/auth/refresh", new { refreshToken = rt, deviceFingerprint = Fp("pr") }, tenant: t.Slug)).Status);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var t = await NewTenant();
        var l = await _c.Post("/auth/login", new { email = t.Admin, password = PW, device = Device("lo") }, tenant: t.Slug);
        var access = l.Data.GetProperty("accessToken").GetString()!;
        var rt = l.Data.GetProperty("refreshToken").GetString()!;
        Assert.Equal(200, (await _c.Post("/auth/logout", new { refreshToken = rt }, access, t.Slug, Fp("lo"))).Status);
        var after = await _c.Post("/auth/refresh", new { refreshToken = rt, deviceFingerprint = Fp("lo") }, tenant: t.Slug);
        Assert.Equal(401, after.Status);
    }

    [Fact]
    public async Task Logout_cannot_revoke_someone_elses_token()
    {
        var t = await NewTenant();
        var (_, email) = await RegisterStudent(t.Slug);
        var victim = await _c.Post("/auth/login", new { email, password = PW, device = Device("v") }, tenant: t.Slug);
        var attacker = await Login(t.Slug, t.Admin, "adm");
        await _c.Post("/auth/logout", new { refreshToken = victim.Data.GetProperty("refreshToken").GetString() }, attacker, t.Slug, Fp("adm"));
        var still = await _c.Post("/auth/refresh", new { refreshToken = victim.Data.GetProperty("refreshToken").GetString(), deviceFingerprint = Fp("v") }, tenant: t.Slug);
        Assert.True(still.Success, still.Body.ToString());
    }

    [Fact]
    public async Task Device_reset_revokes_refresh_tokens_and_access_tokens()
    {
        var t = await NewTenant();
        var (uid, email) = await RegisterStudent(t.Slug);
        var l = await _c.Post("/auth/login", new { email, password = PW, device = Device("ph") }, tenant: t.Slug);
        var access = l.Data.GetProperty("accessToken").GetString()!;
        var rt = l.Data.GetProperty("refreshToken").GetString()!;
        var admin = await Login(t.Slug, t.Admin, "adm");
        Assert.Equal(200, (await _c.Post($"/users/{uid}/devices/reset", new { reason = "x" }, admin, t.Slug, Fp("adm"))).Status);
        Assert.Equal(403, (await _c.Get("/me", access, t.Slug, Fp("ph"))).Status);
        var r = await _c.Post("/auth/refresh", new { refreshToken = rt, deviceFingerprint = Fp("ph") }, tenant: t.Slug);
        Assert.False(r.Success);
        Assert.Contains(r.Status, new[] { 401, 403 });
    }

    [Fact]
    public async Task Password_change_revokes_other_devices_refresh_tokens()
    {
        var t = await NewTenant();
        var a = await _c.Post("/auth/login", new { email = t.Admin, password = PW, device = Device("a1") }, tenant: t.Slug);
        var b = await _c.Post("/auth/login", new { email = t.Admin, password = PW, device = Device("a2") }, tenant: t.Slug);
        var ch = await _c.Post("/me/change-password", new { currentPassword = PW, newPassword = "Chang3d-Passw0rd-x" }, a.Data.GetProperty("accessToken").GetString(), t.Slug, Fp("a1"));
        Assert.Equal(200, ch.Status);
        Assert.Equal(401, (await _c.Post("/auth/refresh", new { refreshToken = b.Data.GetProperty("refreshToken").GetString(), deviceFingerprint = Fp("a2") }, tenant: t.Slug)).Status);
        Assert.True((await _c.Post("/auth/refresh", new { refreshToken = a.Data.GetProperty("refreshToken").GetString(), deviceFingerprint = Fp("a1") }, tenant: t.Slug)).Success);
    }

    // ---- instructor scope (negative access) -----------------------------------------------------------------------------

    [Fact]
    public async Task Instructor_sees_only_students_enrolled_in_courses_they_own()
    {
        var t = await NewTenant();
        var admin = await Login(t.Slug, t.Admin, "adm");
        var ins1 = await CreateStaff(t.Slug, admin, "adm", "Instructor");
        var ins2 = await CreateStaff(t.Slug, admin, "adm", "Instructor");
        var s1 = await RegisterStudent(t.Slug);
        var s2 = await RegisterStudent(t.Slug);
        await _c.Post("/auth/login", new { email = s1.Email, password = PW, device = Device("s1") }, tenant: t.Slug);
        var tok1 = await Login(t.Slug, ins1.Email, "i1");
        var tok2 = await Login(t.Slug, ins2.Email, "i2");

        // Before any enrollment exists: no relationship, no access, and no disclosure (404, not 403).
        var empty = await _c.Get("/users", tok1, t.Slug, Fp("i1"));
        Assert.Equal(0, empty.Data.GetProperty("totalCount").GetInt32());
        Assert.Equal(404, (await _c.Get($"/users/{s1.Id}", tok1, t.Slug, Fp("i1"))).Status);
        Assert.Equal(404, (await _c.Get($"/users/{s1.Id}/devices", tok1, t.Slug, Fp("i1"))).Status);
        Assert.Equal(404, (await _c.Post($"/users/{s1.Id}/devices/reset", new { reason = "x" }, tok1, t.Slug, Fp("i1"))).Status);

        // Instructor 1 owns a course and student 1 is enrolled.
        using (var scope = _f.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(Guid.Parse(t.Id), false);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var course = new Course { TenantId = Guid.Parse(t.Id), OwnerInstructorId = Guid.Parse(ins1.Id), Title = "C", Slug = "c-" + U(), Level = CourseLevel.Beginner };
            db.Courses.Add(course);
            await db.SaveChangesAsync();
            db.Enrollments.Add(new Enrollment { TenantId = Guid.Parse(t.Id), UserId = Guid.Parse(s1.Id), CourseId = course.Id, Source = EnrollmentSource.Admin, EnrolledAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var mine = await _c.Get("/users", tok1, t.Slug, Fp("i1"));
        Assert.Equal(1, mine.Data.GetProperty("totalCount").GetInt32());
        Assert.Equal(s1.Id, mine.Data.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal(200, (await _c.Get($"/users/{s1.Id}", tok1, t.Slug, Fp("i1"))).Status);
        Assert.Equal(200, (await _c.Get($"/users/{s1.Id}/devices", tok1, t.Slug, Fp("i1"))).Status);
        // Unrelated student stays invisible to the owner; the other instructor sees nobody.
        Assert.Equal(404, (await _c.Get($"/users/{s2.Id}", tok1, t.Slug, Fp("i1"))).Status);
        Assert.Equal(404, (await _c.Post($"/users/{s2.Id}/devices/reset", new { reason = "x" }, tok1, t.Slug, Fp("i1"))).Status);
        Assert.Equal(0, (await _c.Get("/users", tok2, t.Slug, Fp("i2"))).Data.GetProperty("totalCount").GetInt32());
        Assert.Equal(404, (await _c.Get($"/users/{s1.Id}", tok2, t.Slug, Fp("i2"))).Status);
        // Staff accounts are never visible to an instructor, even their own admin.
        var staffView = await _c.Get($"/users/{ins2.Id}", tok1, t.Slug, Fp("i1"));
        Assert.Equal(404, staffView.Status);
        // Instructors cannot manage accounts.
        Assert.Equal(403, (await _c.Post("/users", new { fullName = "x", email = $"x{U()}@m.test", password = PW, roles = new[] { "Student" } }, tok1, t.Slug, Fp("i1"))).Status);
        Assert.Equal(403, (await _c.Send(HttpMethod.Put, $"/users/{s1.Id}/roles", new { roles = new[] { "TenantAdmin" } }, tok1, t.Slug, Fp("i1"))).Status);
        Assert.Equal(403, (await _c.Post($"/users/{s1.Id}/deactivate", null, tok1, t.Slug, Fp("i1"))).Status);
        // The related instructor can reset that student's devices.
        Assert.Equal(200, (await _c.Post($"/users/{s1.Id}/devices/reset", new { reason = "lost" }, tok1, t.Slug, Fp("i1"))).Status);
    }

    [Fact]
    public async Task Other_roles_are_locked_out_of_admin_endpoints()
    {
        var t = await NewTenant();
        var admin = await Login(t.Slug, t.Admin, "adm");
        var ta = await CreateStaff(t.Slug, admin, "adm", "TeachingAssistant");
        var taTok = await Login(t.Slug, ta.Email, "ta");
        foreach (var path in new[] { "/users", "/tenants", "/tenant/feature-flags" })
            Assert.Equal(403, (await _c.Get(path, taTok, t.Slug, Fp("ta"))).Status);
        Assert.Equal(401, (await _c.Get("/me")).Status);
        Assert.Equal(401, (await _c.Get("/users", null, t.Slug)).Status);
        Assert.Equal(401, (await _c.Get("/me", "not-a-jwt", t.Slug, Fp("x"))).Status);
        // Tenant admin cannot call platform endpoints or grant themselves support access elsewhere.
        Assert.Equal(403, (await _c.Get("/tenants", admin, t.Slug, Fp("adm"))).Status);
        Assert.Equal(403, (await _c.Post($"/tenants/{t.Id}/support-access", new { minutes = 5 }, admin, t.Slug, Fp("adm"))).Status);
    }

    // ---- logo upload ----------------------------------------------------------------------------------------------------

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52, 1, 2, 3, 4];

    [Fact]
    public async Task Logo_upload_is_tenant_scoped_and_validated()
    {
        var a = await NewTenant();
        var b = await NewTenant();
        var admin = await Login(a.Slug, a.Admin, "adm");
        var (_, sEmail) = await RegisterStudent(a.Slug);
        var student = await Login(a.Slug, sEmail, "st");

        Assert.Equal(404, (await _c.Get("/tenants/current/logo", null, a.Slug)).Status);
        var ok = await _c.Upload("/tenant/logo", Png, "logo.png", admin, a.Slug, Fp("adm"));
        Assert.Equal(200, ok.Status);
        Assert.Equal("/api/v1/tenants/current/logo", ok.Data.GetProperty("logoUrl").GetString());
        var branding = await _c.Get("/tenants/current/branding", null, a.Slug);
        Assert.Equal("/api/v1/tenants/current/logo", branding.Data.GetProperty("logoUrl").GetString());

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tenants/current/logo");
        req.Headers.Add("X-Tenant-Id", a.Slug);
        var img = await _c.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, img.StatusCode);
        Assert.Equal("image/png", img.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Png, await img.Content.ReadAsByteArrayAsync());

        // Tenant B has no logo and cannot see A's.
        Assert.Equal(404, (await _c.Get("/tenants/current/logo", null, b.Slug)).Status);
        // Wrong roles / no auth.
        Assert.Equal(403, (await _c.Upload("/tenant/logo", Png, "logo.png", student, a.Slug, Fp("st"))).Status);
        Assert.Equal(401, (await _c.Upload("/tenant/logo", Png, "logo.png", null, a.Slug, null)).Status);
        var adminB = await Login(b.Slug, b.Admin, "admb");
        Assert.Equal(403, (await _c.Upload("/tenant/logo", Png, "logo.png", adminB, a.Slug, Fp("admb"))).Status); // token tenant B, header A
        // Content validation: bytes decide, not the file name.
        Assert.Equal(400, (await _c.Upload("/tenant/logo", Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>"), "logo.png", admin, a.Slug, Fp("adm"))).Status);
        Assert.Equal(400, (await _c.Upload("/tenant/logo", Encoding.UTF8.GetBytes("hello"), "logo.png", admin, a.Slug, Fp("adm"))).Status);
        var big = new byte[2 * 1024 * 1024 + 10];
        Png.CopyTo(big, 0);
        Assert.Equal(400, (await _c.Upload("/tenant/logo", big, "logo.png", admin, a.Slug, Fp("adm"))).Status);
        // A rejected upload does not replace the good logo.
        Assert.Equal(HttpStatusCode.OK, (await _c.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/v1/tenants/current/logo") { Headers = { { "X-Tenant-Id", a.Slug } } })).StatusCode);
    }

    // ---- email transport -------------------------------------------------------------------------------------------------

    private sealed class FakeTransport(bool enabled) : IEmailTransport
    {
        public List<EmailMessage> Sent { get; } = [];
        public bool IsEnabled => enabled;
        public Task SendAsync(EmailMessage m, CancellationToken ct) { Sent.Add(m); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Phone_otp_request_fails_visibly_and_issues_no_code()
    {
        var t = await NewTenant();
        var r = await _c.Post("/auth/otp/request", new { destination = "+201000000000", purpose = "PhoneVerification" }, tenant: t.Slug);
        Assert.Equal(501, r.Status);
        Assert.Equal("SMS_NOT_CONFIGURED", r.Code);
        await using var scope = _f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.OtpCodes.IgnoreQueryFilters().AnyAsync(o => o.Destination == "+201000000000"));
    }

    [Fact]
    public async Task Otp_email_is_built_for_email_destinations_only()
    {
        var fake = new FakeTransport(true);
        var sender = new EmailOtpSender(fake, NullLogger<EmailOtpSender>.Instance, Options.Create(new OtpOptions { ExpiryMinutes = 10 }));
        await sender.SendAsync("a@b.test", "EmailVerification", "123456", default);
        await sender.SendAsync("+201000000000", "PhoneVerification", "654321", default);
        var m = Assert.Single(fake.Sent);
        Assert.Equal("a@b.test", m.To);
        Assert.Contains("123456", m.Body);
        Assert.Contains("10 minutes", m.Body);
    }

    [Theory]
    [InlineData(false, "", "", typeof(DisabledEmailTransport))]
    [InlineData(true, "", "", typeof(DisabledEmailTransport))]
    [InlineData(true, "smtp.example.test", "", typeof(DisabledEmailTransport))]
    [InlineData(true, "smtp.example.test", "no-reply@example.test", typeof(SmtpEmailTransport))]
    public void Sending_stays_off_unless_enabled_and_fully_configured(bool enabled, string host, string from, Type expected)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = "test-only-signing-key-0123456789-abcdefghij",
            ["ConnectionStrings:DefaultConnection"] = "Server=.;Database=x;",
            ["Email:Enabled"] = enabled.ToString(), ["Email:Host"] = host, ["Email:FromAddress"] = from,
        }).Build();
        var services = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(cfg);
        services.AddScoped<ICurrentUser>(_ => null!);
        services.AddInfrastructure(cfg);
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        Assert.IsType(expected, scope.ServiceProvider.GetRequiredService<IEmailTransport>());
    }

    [Fact]
    public async Task Smtp_transport_speaks_smtp_to_a_local_fake_server()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var captured = new StringBuilder();
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 fake ready");
            while (await reader.ReadLineAsync() is { } line)
            {
                if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase) || line.StartsWith("HELO", StringComparison.OrdinalIgnoreCase)) await writer.WriteLineAsync("250 fake");
                else if (line.StartsWith("MAIL") || line.StartsWith("RCPT")) await writer.WriteLineAsync("250 ok");
                else if (line == "DATA")
                {
                    await writer.WriteLineAsync("354 go");
                    while (await reader.ReadLineAsync() is { } d && d != ".") captured.AppendLine(d);
                    await writer.WriteLineAsync("250 queued");
                }
                else if (line == "QUIT") { await writer.WriteLineAsync("221 bye"); break; }
                else await writer.WriteLineAsync("250 ok");
            }
        });
        var t = new SmtpEmailTransport(Options.Create(new EmailOptions { Enabled = true, Host = "127.0.0.1", Port = port, UseSsl = false, FromAddress = "no-reply@example.test" }));
        await t.SendAsync(new EmailMessage("user@example.test", "Subject line", "Code 424242"), default);
        await server.WaitAsync(TimeSpan.FromSeconds(10));
        listener.Stop();
        var text = captured.ToString();
        Assert.Contains("Subject: Subject line", text);
        Assert.Contains("424242", text);
    }
}
