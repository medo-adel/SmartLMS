using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Tests;

public sealed class EngagementTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    private readonly HttpClient _c;
    private const string PW = ApiFactory.Password;
    private static string Fp(string s) => "fingerprint-" + s + "-0123456789";
    private static string U() => Guid.NewGuid().ToString("N")[..8];
    private static object Device(string fp) => new { fingerprint = Fp(fp), deviceType = "Mobile", platform = "test", deviceName = "t" };
    public EngagementTests(ApiFactory f) { _f = f; _c = f.CreateClient(); }

    private sealed record Who(string Token, string Id, string Dev);
    private sealed record T(string Slug, Who Admin);

    private async Task<Who> Login(string slug, string email, string dev, string? id = null)
    {
        var r = await _c.Post("/auth/login", new { email, password = PW, device = Device(dev) }, tenant: slug);
        Assert.True(r.Success, r.Body.ToString());
        return new Who(r.Data.GetProperty("accessToken").GetString()!, id ?? "", Fp(dev));
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

    private async Task<(string Course, string Section, string Lesson)> Course(T t, Who inst, bool publish, string? drip = null, bool freePreview = false)
    {
        var c = Id(await Post("/courses", new { title = "Course " + U(), dripMode = drip }, inst, t));
        var s = Id(await Post($"/courses/{c}/sections", new { title = "S1" }, inst, t));
        var l = Id(await Post($"/sections/{s}/lessons", new { title = "L1", lessonType = "Article", contentMarkdown = "secret body", isFreePreview = freePreview }, inst, t));
        if (publish)
        {
            Assert.Equal(200, (await Post($"/courses/{c}/submit-for-approval", null, inst, t)).Status);
            Assert.Equal(200, (await Post($"/courses/{c}/approve", null, t.Admin, t)).Status);
        }
        return (c, s, l);
    }


    private async Task<Who> Parent(T t)
    {
        var email = $"pa{U()}@mail.test";
        var r = await _c.Post("/users", new { fullName = "Parent One", email, password = PW, roles = new[] { "Parent" } }, t.Admin.Token, t.Slug, t.Admin.Dev);
        Assert.Equal(201, r.Status);
        return await Login(t.Slug, email, "pa" + U(), r.Data.GetProperty("id").GetString());
    }
    private async Task<(string Course, string Lesson)> PublishedCourse(T t, Who inst)
    {
        var c = Id(await Post("/courses", new { title = "Intro Course" }, inst, t));
        var s = Id(await Post($"/courses/{c}/sections", new { title = "S" }, inst, t));
        var l = Id(await Post($"/sections/{s}/lessons", new { title = "L", lessonType = "Article", contentMarkdown = "x" }, inst, t));
        await Post($"/courses/{c}/submit-for-approval", null, inst, t); await Post($"/courses/{c}/approve", null, t.Admin, t);
        return (c, l);
    }
    private Task<Resp> Complete(string lesson, Who st, T t) => _c.Send(HttpMethod.Put, $"/lessons/{lesson}/progress", new { lastPositionSeconds = 1, watchedSecondsDelta = 1, completed = true }, st.Token, t.Slug, st.Dev);

    [Fact]
    public async Task Completion_awards_xp_once_and_issues_one_certificate()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t);
        var (c, l) = await PublishedCourse(t, inst);
        await Post($"/courses/{c}/enroll", null, st, t);
        var rs = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Complete(l, st, t)));
        Assert.All(rs, r => Assert.Equal(200, r.Status));
        var me = (await Get("/gamification/me", st, t)).Data;
        Assert.Equal(110, me.GetProperty("xp").GetInt32());            // 10 lesson + 100 course, each once
        Assert.Equal(1, me.GetProperty("currentStreakDays").GetInt32());
        var codes = me.GetProperty("badges").EnumerateArray().Select(b => b.GetProperty("code").GetString()).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "FIRST_COURSE", "FIRST_LESSON" }, codes);   // each awarded once, despite 6 parallel completions
        var certs = (await Get("/certificates/mine", st, t)).Data;
        Assert.Equal(1, certs.GetArrayLength());
        // public verification shows name, course and date only
        var code = certs[0].GetProperty("verificationCode").GetString();
        var v = await _c.Get($"/certificates/verify/{code}", null, t.Slug);
        Assert.Equal(200, v.Status); Assert.True(v.Data.GetProperty("valid").GetBoolean());
        var raw = v.Body.ToString();
        Assert.DoesNotContain("@", raw); Assert.DoesNotContain("email", raw, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("phone", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(404, (await _c.Get("/certificates/verify/not-a-real-code", null, t.Slug)).Status);
        // manual issue is idempotent and owner-only
        var again = await Post("/certificates/issue", new { userId = st.Id, courseId = c }, inst, t);
        Assert.Equal(200, again.Status); Assert.Equal(code, again.Data.GetProperty("verificationCode").GetString());
        var rival = await Instructor(t);
        Assert.Equal(404, (await Post("/certificates/issue", new { userId = st.Id, courseId = c }, rival, t)).Status);
        Assert.Equal(403, (await Post("/certificates/issue", new { userId = st.Id, courseId = c }, st, t)).Status);
        // leaderboard exposes a short name only
        var board = (await Get($"/gamification/leaderboard?courseId={c}", st, t)).Body.ToString();
        Assert.DoesNotContain(st.Id, board);
        var stranger = await Student(t);
        Assert.Equal(404, (await Get($"/gamification/leaderboard?courseId={c}", stranger, t)).Status);
    }

    [Fact]
    public async Task Certificate_pdf_is_a_real_pdf_with_qr_and_is_access_controlled()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t); var other = await Student(t);
        var (c, l) = await PublishedCourse(t, inst);
        await Post($"/courses/{c}/enroll", null, st, t); await Complete(l, st, t);
        var cert = (await Get("/certificates/mine", st, t)).Data[0].GetProperty("id").GetString();
        async Task<HttpResponseMessage> Pdf(Who w)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/certificates/{cert}/pdf");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", w.Token); req.Headers.Add("X-Tenant-Id", t.Slug); req.Headers.Add("X-Device-Id", w.Dev);
            return await _c.SendAsync(req);
        }
        var ok = await Pdf(st);
        Assert.Equal(200, (int)ok.StatusCode); Assert.Equal("application/pdf", ok.Content.Headers.ContentType!.MediaType);
        var bytes = await ok.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF-1.4", System.Text.Encoding.ASCII.GetString(bytes, 0, 8));
        Assert.Contains("%%EOF", System.Text.Encoding.Latin1.GetString(bytes));
        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "smartlms-test-certificate.pdf"), bytes);
        Assert.Equal(200, (int)(await Pdf(inst)).StatusCode);
        Assert.Equal(404, (int)(await Pdf(other)).StatusCode);
        Assert.Equal(404, (int)(await Pdf(await Instructor(t))).StatusCode);
        Assert.Equal(401, (int)(await _c.GetAsync($"/api/v1/certificates/{cert}/pdf")).StatusCode == 401 ? 401 : 0);
    }

    [Fact]
    public async Task Certificate_pdf_refuses_a_non_https_or_missing_verification_url_outside_development()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t);
        var (c, l) = await PublishedCourse(t, inst);
        await Post($"/courses/{c}/enroll", null, st, t); await Complete(l, st, t);
        var cert = (await Get("/certificates/mine", st, t)).Data[0].GetProperty("id").GetString();
        foreach (var bad in new[] { "http://lms.example.test", "not-a-url" })
        {
            using var f2 = _f.WithWebHostBuilder(b => b.UseSetting("Certificates:VerificationBaseUrl", bad));
            var c2 = f2.CreateClient();
            var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/certificates/{cert}/pdf");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", st.Token); req.Headers.Add("X-Tenant-Id", t.Slug); req.Headers.Add("X-Device-Id", st.Dev);
            Assert.Equal(500, (int)(await c2.SendAsync(req)).StatusCode);   // the Host header is never used to build the QR URL
        }
    }

    [Fact]
    public async Task Parent_link_flow_and_access_boundaries()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var rival = await Instructor(t);
        var st = await Student(t); var st2 = await Student(t); var p1 = await Parent(t); var p2 = await Parent(t);
        var (c, l) = await PublishedCourse(t, inst);
        await Post($"/courses/{c}/enroll", null, st, t);
        // roles: only students make codes, only parents redeem them
        Assert.Equal(403, (await Post("/students/me/link-codes", null, p1, t)).Status);
        Assert.Equal(403, (await Post("/parent/links", new { code = "x" }, st, t)).Status);
        var code = (await Post("/students/me/link-codes", null, st, t)).Data.GetProperty("code").GetString()!;
        // forged, edited and garbage codes all get the same refusal
        Assert.Equal(400, (await Post("/parent/links", new { code = "garbage" }, p1, t)).Status);
        var forged = code[..^2] + (code.EndsWith("AA") ? "BB" : "AA");
        Assert.Equal(400, (await Post("/parent/links", new { code = forged }, p1, t)).Status);
        var link = await Post("/parent/links", new { code }, p1, t);
        Assert.Equal(200, link.Status); Assert.Equal("Pending", link.Data.GetProperty("status").GetString());
        var lid = link.Data.GetProperty("linkId").GetString();
        // pending link shows nothing
        Assert.Equal(0, (await Get("/parent/children", p1, t)).Data.GetArrayLength());
        Assert.Equal(404, (await Get($"/parent/children/{st.Id}/summary", p1, t)).Status);
        // parent, student and an unrelated instructor cannot approve
        Assert.Equal(403, (await Put($"/parent/links/{lid}", new { approve = true }, p1, t)).Status);
        Assert.Equal(403, (await Put($"/parent/links/{lid}", new { approve = true }, st, t)).Status);
        Assert.Equal(404, (await Put($"/parent/links/{lid}", new { approve = true }, rival, t)).Status);
        Assert.Equal(200, (await Put($"/parent/links/{lid}", new { approve = true }, inst, t)).Status);
        // the same code cannot activate a second parent
        var link2 = await Post("/parent/links", new { code }, p2, t);
        Assert.Equal(200, link2.Status);
        var r2 = await Put($"/parent/links/{link2.Data.GetProperty("linkId").GetString()}", new { approve = true }, t.Admin, t);
        Assert.Equal(409, r2.Status); Assert.Equal("CODE_ALREADY_USED", r2.Code);
        Assert.Equal(0, (await Get("/parent/children", p2, t)).Data.GetArrayLength());
        // the active parent sees the child and only summary data; another student and the other parent get 404
        Assert.Equal(1, (await Get("/parent/children", p1, t)).Data.GetArrayLength());
        var sum = await Get($"/parent/children/{st.Id}/summary", p1, t);
        Assert.Equal(200, sum.Status);
        var raw = sum.Body.ToString();
        Assert.DoesNotContain("@", raw); Assert.DoesNotContain("deviceId", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(404, (await Get($"/parent/children/{st2.Id}/summary", p1, t)).Status);
        Assert.Equal(404, (await Get($"/parent/children/{st.Id}/summary", p2, t)).Status);
        Assert.Equal(403, (await Get($"/parent/children/{st.Id}/summary", st, t)).Status);
        // an inactive student gets the inactivity alert; revoking removes access and the alerts
        var alerts = (await Get("/parent/alerts", p1, t)).Data;
        Assert.Contains("Inactivity", alerts.ToString());
        Assert.Equal(200, (await Put($"/parent/links/{lid}", new { approve = false }, inst, t)).Status);
        Assert.Equal(404, (await Get($"/parent/children/{st.Id}/summary", p1, t)).Status);
        Assert.Equal(0, (await Get("/parent/alerts", p1, t)).Data.GetArrayLength());
        Assert.Equal(409, (await Put($"/parent/links/{lid}", new { approve = true }, inst, t)).Status);
        // cross-tenant: a parent token for another tenant cannot use a link
        var t2 = await NewTenant(); var pOther = await Parent(t2);
        Assert.Equal(400, (await Post("/parent/links", new { code }, pOther, t2)).Status);
    }

    [Fact]
    public async Task Jobs_are_visible_to_the_owner_or_an_admin_only()
    {
        var t = await NewTenant(); var a = await Student(t); var b = await Student(t);
        var jobId = Guid.NewGuid();
        await using (var scope = _f.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenantId = await db.Tenants.IgnoreQueryFilters().Where(x => x.Slug == t.Slug).Select(x => x.Id).SingleAsync();
            scope.ServiceProvider.GetRequiredService<SmartLMS.Application.Interfaces.ITenantContext>().Set(tenantId, false);
            db.BackgroundJobs.Add(new BackgroundJob { Id = jobId, TenantId = tenantId, Type = Enum.GetValues<BackgroundJobType>()[0], Status = BackgroundJobStatus.Pending, RequestedBy = Guid.Parse(a.Id) });
            await db.SaveChangesAsync();
        }
        Assert.Equal(200, (await Get($"/jobs/{jobId}", a, t)).Status);
        Assert.Equal(200, (await Get($"/jobs/{jobId}", t.Admin, t)).Status);
        Assert.Equal(404, (await Get($"/jobs/{jobId}", b, t)).Status);
    }
}
