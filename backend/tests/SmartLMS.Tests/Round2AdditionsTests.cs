using System.Text.Json;

namespace SmartLMS.Tests;

/// <summary>
/// Smoke tests for the routes added by the backend update package (round 2 + round 5):
/// GET /subscriptions/mine, GET /learning-paths/manage, GET /parent/links/pending,
/// GET /exams/{id}, GET /courses/{id}/prerequisites, GET /courses/{courseId}/learning-weight.
/// Each new read is checked for the same ownership/role guard as its corresponding mutation.
/// </summary>
public sealed class Round2AdditionsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    private readonly HttpClient _c;
    private const string PW = ApiFactory.Password;
    private static string Fp(string s) => "fingerprint-" + s + "-0123456789";
    private static string U() => Guid.NewGuid().ToString("N")[..8];
    private static object Device(string fp) => new { fingerprint = Fp(fp), deviceType = "Mobile", platform = "test", deviceName = "t" };
    public Round2AdditionsTests(ApiFactory f) { _f = f; _c = f.CreateClient(); }

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
    private async Task<Who> WithRole(T t, string role)
    {
        var email = $"{role.ToLowerInvariant()}{U()}@mail.test";
        var r = await _c.Post("/users", new { fullName = role, email, password = PW, roles = new[] { role } }, t.Admin.Token, t.Slug, t.Admin.Dev);
        Assert.Equal(201, r.Status);
        return await Login(t.Slug, email, role[..2] + U(), r.Data.GetProperty("id").GetString());
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

    private async Task<string> Course(T t, Who inst, bool publish = true)
    {
        var c = Id(await Post("/courses", new { title = "Course " + U() }, inst, t));
        var s = Id(await Post($"/courses/{c}/sections", new { title = "S1" }, inst, t));
        Assert.Equal(201, (await Post($"/sections/{s}/lessons", new { title = "L1", lessonType = "Article", contentMarkdown = "body" }, inst, t)).Status);
        if (publish)
        {
            Assert.Equal(200, (await Post($"/courses/{c}/submit-for-approval", null, inst, t)).Status);
            Assert.Equal(200, (await Post($"/courses/{c}/approve", null, t.Admin, t)).Status);
        }
        return c;
    }

    [Fact]
    public async Task GetExam_prefills_settings_hides_answers_and_enforces_ownership()
    {
        var t = await NewTenant(); var inst = await WithRole(t, "Instructor"); var other = await WithRole(t, "Instructor"); var st = await Student(t);
        var c = await Course(t, inst);
        var bank = Id(await Post("/question-banks", new { courseId = c, name = "Bank", topic = (string?)null }, inst, t));
        var qid = Id(await Post($"/question-banks/{bank}/questions", new { questionType = "McqSingle", text = "2+2?", points = 1.0, options = new[] { new { text = "4", isCorrect = true, matchKey = (string?)null }, new { text = "5", isCorrect = false, matchKey = (string?)null } } }, inst, t));
        var e = Id(await Post($"/courses/{c}/exams", new { title = "Midterm", kind = "Exam", instructions = "Read carefully", durationMinutes = 30, maxAttempts = 2, passingScorePercent = 60.0, questionIds = new[] { qid }, tabSwitchDetection = true, maxTabSwitches = 2 }, inst, t));

        var ok = await Get($"/exams/{e}", inst, t);
        Assert.Equal(200, ok.Status);
        Assert.Equal("Midterm", ok.Data.GetProperty("title").GetString());
        Assert.Equal("Read carefully", ok.Data.GetProperty("instructions").GetString());
        Assert.Equal(2, ok.Data.GetProperty("maxTabSwitches").GetInt32());
        Assert.False(ok.Data.GetProperty("locked").GetBoolean());
        // settings only: no questions, options or answers leak through this route
        var raw = ok.Data.ToString();
        Assert.DoesNotContain("2+2?", raw);
        Assert.DoesNotContain("options", raw);
        Assert.Equal(404, (await Get($"/exams/{e}", other, t)).Status);   // same guard as PUT /exams/{id}
        Assert.Equal(403, (await Get($"/exams/{e}", st, t)).Status);
    }

    [Fact]
    public async Task GetPrerequisites_returns_current_list_and_enforces_ownership()
    {
        var t = await NewTenant(); var inst = await WithRole(t, "Instructor"); var other = await WithRole(t, "Instructor"); var st = await Student(t);
        var pre = await Course(t, inst);
        var c = await Course(t, inst);
        Assert.Equal(200, (await Put($"/courses/{c}/prerequisites", new { courseIds = new[] { pre } }, inst, t)).Status);

        var ok = await Get($"/courses/{c}/prerequisites", inst, t);
        Assert.Equal(200, ok.Status);
        var row = Assert.Single(ok.Data.EnumerateArray());
        Assert.Equal(pre, row.GetProperty("courseId").GetString());
        Assert.StartsWith("Course ", row.GetProperty("title").GetString());
        Assert.Equal(404, (await Get($"/courses/{c}/prerequisites", other, t)).Status);   // same guard as the PUT
        Assert.Equal(403, (await Get($"/courses/{c}/prerequisites", st, t)).Status);
    }

    [Fact]
    public async Task LearningWeight_reads_null_then_written_value_admin_only()
    {
        var t = await NewTenant(); var inst = await WithRole(t, "Instructor"); var st = await Student(t);
        var c = await Course(t, inst);

        var unset = await Get($"/courses/{c}/learning-weight", t.Admin, t);
        Assert.Equal(200, unset.Status);
        Assert.Equal(JsonValueKind.Null, unset.Data.GetProperty("nonVideoCourseSeconds").ValueKind);
        Assert.Equal(200, (await Put($"/courses/{c}/learning-weight", new { nonVideoCourseSeconds = 1800 }, t.Admin, t)).Status);
        var set = await Get($"/courses/{c}/learning-weight", t.Admin, t);
        Assert.Equal(1800, set.Data.GetProperty("nonVideoCourseSeconds").GetInt32());
        Assert.Equal(403, (await Get($"/courses/{c}/learning-weight", inst, t)).Status);   // TenantAdmin route like the PUT
        Assert.Equal(403, (await Get($"/courses/{c}/learning-weight", st, t)).Status);
    }

    [Fact]
    public async Task Round2_routes_subscriptions_manage_paths_and_pending_links()
    {
        var t = await NewTenant(); var inst = await WithRole(t, "Instructor"); var st = await Student(t); var parent = await WithRole(t, "Parent");
        var c = await Course(t, inst);

        // GET /subscriptions/mine (Student): works and returns an empty list before any purchase
        var subs = await Get("/subscriptions/mine", st, t);
        Assert.Equal(200, subs.Status);
        Assert.Equal(0, subs.Data.GetArrayLength());
        Assert.Equal(403, (await Get("/subscriptions/mine", inst, t)).Status);

        // GET /learning-paths/manage: instructors see their own drafts, TenantAdmin sees all tenant paths, students are refused
        var p = await Post("/learning-paths", new { title = "Path", status = "Draft", courseIds = new[] { c } }, inst, t);
        Assert.Equal(200, p.Status);
        Assert.Equal(1, (await Get("/learning-paths/manage", inst, t)).Data.GetArrayLength());
        Assert.Equal(1, (await Get("/learning-paths/manage", t.Admin, t)).Data.GetArrayLength());
        Assert.Equal(403, (await Get("/learning-paths/manage", st, t)).Status);

        // GET /parent/links/pending: the pending request the approvals screen lists
        var code = (await Post("/students/me/link-codes", null, st, t)).Data.GetProperty("code").GetString()!;
        var link = await Post("/parent/links", new { code }, parent, t);
        Assert.Equal(200, link.Status);
        var pending = await Get("/parent/links/pending", t.Admin, t);
        Assert.Equal(200, pending.Status);
        var row = Assert.Single(pending.Data.EnumerateArray());
        Assert.Equal("Pending", row.GetProperty("status").GetString());
        Assert.Equal(403, (await Get("/parent/links/pending", st, t)).Status);
    }
}
