using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Tests;

public sealed class ContentTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    private readonly HttpClient _c;
    private const string PW = ApiFactory.Password;
    private static string Fp(string s) => "fingerprint-" + s + "-0123456789";
    private static string U() => Guid.NewGuid().ToString("N")[..8];
    private static object Device(string fp) => new { fingerprint = Fp(fp), deviceType = "Mobile", platform = "test", deviceName = "t" };
    public ContentTests(ApiFactory f) { _f = f; _c = f.CreateClient(); }

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
    private static object Opt(string t, bool ok) => new { text = t, isCorrect = ok, matchKey = (string?)null };

    [Fact]
    public async Task Prerequisites_ordering_chapters_slides_and_interactive_quizzes()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var other = await Instructor(t); var st = await Student(t); var st2 = await Student(t);
        var (pre, _, _) = await Course(t, inst, true);
        var (c, sec, l1) = await Course(t, inst, true);
        // prerequisites: authoring, cycle refusal, enforcement at enroll
        Assert.Equal(404, (await Put($"/courses/{c}/prerequisites", new { courseIds = new[] { pre } }, other, t)).Status);
        Assert.Equal(403, (await Put($"/courses/{c}/prerequisites", new { courseIds = new[] { pre } }, st, t)).Status);
        Assert.Equal(400, (await Put($"/courses/{c}/prerequisites", new { courseIds = new[] { c } }, inst, t)).Status);
        Assert.Equal(200, (await Put($"/courses/{c}/prerequisites", new { courseIds = new[] { pre } }, inst, t)).Status);
        Assert.Equal(409, (await Put($"/courses/{pre}/prerequisites", new { courseIds = new[] { c } }, inst, t)).Status);
        var blocked = await Post($"/courses/{c}/enroll", null, st, t); Assert.Equal(403, blocked.Status); Assert.Contains("PREREQUISITE_NOT_MET", blocked.Body.ToString());
        Assert.Equal(200, (await Post($"/courses/{pre}/enroll", null, st, t)).Status);
        Assert.Equal(403, (await Post($"/courses/{c}/enroll", null, st, t)).Status);   // enrolled but not completed
        using (var scope = _f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tid = (await db.Tenants.IgnoreQueryFilters().FirstAsync(x => x.Slug == t.Slug)).Id;
            scope.ServiceProvider.GetRequiredService<SmartLMS.Application.Interfaces.ITenantContext>().Set(tid, false);
            var e = await db.Enrollments.IgnoreQueryFilters().FirstAsync(x => x.CourseId == Guid.Parse(pre));
            e.Status = EnrollmentStatus.Completed; await db.SaveChangesAsync();
        }
        Assert.Equal(200, (await Post($"/courses/{c}/enroll", null, st, t)).Status);
        // ordering
        var l2 = Id(await Post($"/sections/{sec}/lessons", new { title = "L2", lessonType = "Article", contentMarkdown = "x" }, inst, t));
        Assert.Equal(400, (await Put($"/sections/{sec}/lessons/order", new { ids = new[] { l2 } }, inst, t)).Status);
        Assert.Equal(404, (await Put($"/sections/{sec}/lessons/order", new { ids = new[] { l2, l1 } }, other, t)).Status);
        Assert.Equal(200, (await Put($"/sections/{sec}/lessons/order", new { ids = new[] { l2, l1 } }, inst, t)).Status);
        var s2 = Id(await Post($"/courses/{c}/sections", new { title = "S2" }, inst, t));
        Assert.Equal(400, (await Put($"/courses/{c}/sections/order", new { ids = new[] { s2, s2 } }, inst, t)).Status);
        Assert.Equal(200, (await Put($"/courses/{c}/sections/order", new { ids = new[] { s2, sec } }, inst, t)).Status);
        // chapters and slides
        Assert.Equal(200, (await Put($"/lessons/{l1}/chapters", new { chapters = new[] { new { title = "B", startSeconds = 60 }, new { title = "A", startSeconds = 0 } } }, inst, t)).Status);
        Assert.Equal(400, (await Put($"/lessons/{l1}/chapters", new { chapters = new[] { new { title = "", startSeconds = -1 } } }, inst, t)).Status);
        Assert.Equal(404, (await Put($"/lessons/{l1}/chapters", new { chapters = new object[0] }, other, t)).Status);
        Assert.Equal(200, (await Put($"/lessons/{l1}/slides", new { slides = new[] { new { title = "s1" } } }, inst, t)).Status);
        Assert.Equal(400, (await Put($"/lessons/{l1}/slides", new { slides = new[] { "notobject" } }, inst, t)).Status);
        // interactive quizzes
        var bank = Id(await Post("/question-banks", new { courseId = c, name = "B", topic = "T" }, inst, t));
        var q = Id(await Post($"/question-banks/{bank}/questions", new { questionType = "McqSingle", text = "Q?", points = 1.0, explanation = "because", options = new[] { Opt("right", true), Opt("wrong", false) } }, inst, t));
        var otherCourse = Id(await Post("/courses", new { title = "Other" }, inst, t));
        var bank2 = Id(await Post("/question-banks", new { courseId = otherCourse, name = "B2", topic = "T" }, inst, t));
        var q2 = Id(await Post($"/question-banks/{bank2}/questions", new { questionType = "McqSingle", text = "Q2?", points = 1.0, options = new[] { Opt("a", true), Opt("b", false) } }, inst, t));
        Assert.Equal(404, (await Put($"/lessons/{l1}/interactive-quizzes", new { points = new[] { new { questionId = q2, timestampSeconds = 5, isRequired = true, allowSkipAfterAttempts = (int?)null } } }, inst, t)).Status);   // another course's question
        Assert.Equal(403, (await Put($"/lessons/{l1}/interactive-quizzes", new { points = new object[0] }, st, t)).Status);
        Assert.Equal(200, (await Put($"/lessons/{l1}/interactive-quizzes", new { points = new[] { new { questionId = q, timestampSeconds = 5, isRequired = true, allowSkipAfterAttempts = (int?)2 } } }, inst, t)).Status);
        var author = await Get($"/lessons/{l1}/interactive-quizzes", inst, t); Assert.Contains("isCorrect", author.Body.ToString());
        var stu = await Get($"/lessons/{l1}/interactive-quizzes", st, t); Assert.Equal(200, stu.Status); Assert.DoesNotContain("isCorrect", stu.Body.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(403, (await Get($"/lessons/{l1}/interactive-quizzes", st2, t)).Status);   // not enrolled
        var qz = stu.Data[0].GetProperty("id").GetString(); var opts = stu.Data[0].GetProperty("options");
        var wrong = opts.EnumerateArray().First(o => o.GetProperty("text").GetString() == "wrong").GetProperty("id").GetString();
        var right = opts.EnumerateArray().First(o => o.GetProperty("text").GetString() == "right").GetProperty("id").GetString();
        Assert.Equal(403, (await Post($"/interactive-quizzes/{qz}/answer", new { answer = new { optionId = wrong } }, st2, t)).Status);
        var a1 = await Post($"/interactive-quizzes/{qz}/answer", new { answer = new { optionId = wrong } }, st, t);
        Assert.False(a1.Data.GetProperty("correct").GetBoolean()); Assert.False(a1.Data.GetProperty("canContinue").GetBoolean()); Assert.Equal(JsonValueKind.Null, a1.Data.GetProperty("explanation").ValueKind);
        var a2 = await Post($"/interactive-quizzes/{qz}/answer", new { answer = new { optionId = wrong } }, st, t);
        Assert.True(a2.Data.GetProperty("canContinue").GetBoolean());   // skip allowed after 2 attempts
        var a3 = await Post($"/interactive-quizzes/{qz}/answer", new { answer = new { optionId = right } }, st, t);
        Assert.True(a3.Data.GetProperty("correct").GetBoolean()); Assert.Equal("because", a3.Data.GetProperty("explanation").GetString());
    }

    private async Task<(int Status, byte[] Body, string? Type)> Raw(string path, Who w, T t, Range? range = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1" + path);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", w.Token); req.Headers.Add("X-Tenant-Id", t.Slug); req.Headers.Add("X-Device-Id", w.Dev);
        var res = await _c.SendAsync(req); return ((int)res.StatusCode, await res.Content.ReadAsByteArrayAsync(), res.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Lesson_media_and_resources_are_validated_and_access_controlled()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var other = await Instructor(t); var st = await Student(t); var st2 = await Student(t);
        var (c, sec, _) = await Course(t, inst, true);
        var vid = Id(await Post($"/sections/{sec}/lessons", new { title = "V", lessonType = "Video", isPublished = true }, inst, t));
        var mp4 = new byte[] { 0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0, 0, 2, 0, 1, 2, 3, 4, 5, 6, 7, 8 };
        var exe = new byte[] { (byte)'M', (byte)'Z', 0x90, 0, 3, 0, 0, 0, 4, 0, 0, 0 };
        Assert.Equal(400, (await _c.Upload($"/lessons/{vid}/media", exe, "evil.mp4", inst.Token, t.Slug, inst.Dev)).Status);          // extension is ignored, bytes decide
        Assert.Equal(404, (await _c.Upload($"/lessons/{vid}/media", mp4, "a.mp4", other.Token, t.Slug, other.Dev)).Status);
        Assert.Equal(403, (await _c.Upload($"/lessons/{vid}/media", mp4, "a.mp4", st.Token, t.Slug, st.Dev)).Status);
        var up = await _c.Upload($"/lessons/{vid}/media", mp4, "../../x.mp4", inst.Token, t.Slug, inst.Dev); Assert.Equal(200, up.Status);
        Assert.Equal(403, (await Get($"/lessons/{vid}/stream", st, t)).Status);   // not enrolled
        Assert.Equal(200, (await Post($"/courses/{c}/enroll", null, st, t)).Status);
        var s = await Raw($"/lessons/{vid}/stream", st, t); Assert.Equal(200, s.Status); Assert.Equal("video/mp4", s.Type); Assert.Equal(mp4, s.Body);
        Assert.Equal(404, (await Raw($"/lessons/{vid}/stream", other, t)).Status);
        // resources
        var pdf = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 hello");
        Assert.Equal(400, (await _c.Upload($"/lessons/{vid}/resources", exe, "r.pdf", inst.Token, t.Slug, inst.Dev)).Status);
        var r = await _c.Upload($"/lessons/{vid}/resources", pdf, "notes.pdf", inst.Token, t.Slug, inst.Dev); Assert.Equal(200, r.Status);
        var rid = r.Data.GetProperty("id").GetString();
        Assert.Equal(404, (await _c.Upload($"/lessons/{vid}/resources", pdf, "n.pdf", other.Token, t.Slug, other.Dev)).Status);
        var d = await Raw($"/lessons/{vid}/resources/{rid}/download", st, t); Assert.Equal(200, d.Status); Assert.Equal(pdf, d.Body);
        Assert.Equal(403, (await Get($"/lessons/{vid}/resources/{rid}/download", st2, t)).Status);
        Assert.Equal(404, (await Get($"/lessons/{vid}/resources/{Guid.NewGuid()}/download", st, t)).Status);
        // a non-downloadable resource is for authors only
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/lessons/{vid}/resources") { Content = new MultipartFormDataContent { { new ByteArrayContent(pdf), "file", "hidden.pdf" }, { new StringContent("false"), "isDownloadable" } } };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", inst.Token); req.Headers.Add("X-Tenant-Id", t.Slug); req.Headers.Add("X-Device-Id", inst.Dev);
        var hid = JsonDocument.Parse(await (await _c.SendAsync(req)).Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetProperty("id").GetString();
        Assert.Equal(403, (await Get($"/lessons/{vid}/resources/{hid}/download", st, t)).Status);
        Assert.Equal(200, (await Raw($"/lessons/{vid}/resources/{hid}/download", inst, t)).Status);
    }

    private async Task<Who> WithRole(T t, string role)
    {
        var email = $"x{U()}@mail.test";
        var r = await _c.Post("/users", new { fullName = role, email, password = PW, roles = new[] { role } }, t.Admin.Token, t.Slug, t.Admin.Dev);
        Assert.Equal(201, r.Status);
        return await Login(t.Slug, email, "x" + U(), r.Data.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Course_staff_co_instructor_and_teaching_assistant_access()
    {
        var t = await NewTenant(); var owner = await Instructor(t); var co = await WithRole(t, "Instructor"); var outsider = await WithRole(t, "Instructor");
        var ta = await WithRole(t, "TeachingAssistant"); var ta2 = await WithRole(t, "TeachingAssistant"); var st = await Student(t);
        var (c, sec, l) = await Course(t, owner, true);
        // staff are managed by the owner or admin only, and the roles must match
        Assert.Equal(404, (await Put($"/courses/{c}/staff", new { staff = new object[0] }, outsider, t)).Status);
        Assert.Equal(403, (await Put($"/courses/{c}/staff", new { staff = new object[0] }, st, t)).Status);
        Assert.Equal(400, (await Put($"/courses/{c}/staff", new { staff = new[] { new { userId = ta.Id, staffRole = "CoInstructor" } } }, owner, t)).Status);   // TA user cannot be a co-instructor
        Assert.Equal(400, (await Put($"/courses/{c}/staff", new { staff = new[] { new { userId = co.Id, staffRole = "Boss" } } }, owner, t)).Status);
        Assert.Equal(400, (await Put($"/courses/{c}/staff", new { staff = new[] { new { userId = owner.Id, staffRole = "CoInstructor" } } }, owner, t)).Status);
        Assert.Equal(404, (await Put($"/courses/{c}/staff", new { staff = new[] { new { userId = Guid.NewGuid().ToString(), staffRole = "CoInstructor" } } }, owner, t)).Status);
        // before being added, nobody else can touch the course
        Assert.Equal(404, (await Put($"/courses/{c}/prerequisites", new { courseIds = new Guid[0] }, co, t)).Status);
        Assert.Equal(404, (await Get($"/courses/{c}/question-banks", co, t)).Status);
        Assert.Equal(200, (await Put($"/courses/{c}/staff", new { staff = new[] { new { userId = co.Id, staffRole = "CoInstructor" }, new { userId = ta.Id, staffRole = "TeachingAssistant" } } }, owner, t)).Status);
        Assert.Equal(2, (await Get($"/courses/{c}/staff", owner, t)).Data.GetArrayLength());
        // co-instructor authors and sees the course in "mine", but cannot delete it, submit it or change staff
        Assert.Equal(200, (await Get($"/courses/{c}/question-banks", co, t)).Status);
        Assert.Equal(200, (await Put($"/courses/{c}", new { title = "Edited by co" }, co, t)).Status);
        Assert.Equal(1, (await Get("/courses/mine", co, t)).Data.EnumerateArray().Count(x => x.GetProperty("id").GetString() == c));
        Assert.Equal(404, (await _c.Send(HttpMethod.Delete, $"/courses/{c}", null, co.Token, t.Slug, co.Dev)).Status);
        Assert.Equal(404, (await Put($"/courses/{c}/staff", new { staff = new object[0] }, co, t)).Status);
        Assert.Equal(200, (await Get($"/courses/{c}/staff", co, t)).Status);
        // teaching assistant: reads and grades, never authors
        Assert.Equal(403, (await Get($"/courses/{c}/question-banks", ta, t)).Status);
        Assert.Equal(403, (await Put($"/courses/{c}/prerequisites", new { courseIds = new Guid[0] }, ta, t)).Status);
        Assert.Equal(403, (await Post($"/courses/{c}/assignments", new { title = "A", maxPoints = 10 }, ta, t)).Status);
        Assert.Equal(200, (await Get($"/lessons/{l}", ta, t)).Status);
        // a TA of another course sees nothing here
        Assert.Equal(404, (await Get($"/lessons/{l}", ta2, t)).Status);
        // assignment flow: owner creates, student submits, TA grades; the unrelated TA cannot
        var a = Id(await Post($"/courses/{c}/assignments", new { title = "A", instructions = "do it", maxPoints = 10, isPublished = true }, owner, t));
        Assert.Equal(200, (await Post($"/courses/{c}/enroll", null, st, t)).Status);
        var sub = Id(await Post($"/assignments/{a}/submissions", new { textContent = "my work" }, st, t));
        Assert.Equal(200, (await Get($"/assignments/{a}/submissions", ta, t)).Status);
        Assert.Equal(404, (await Get($"/assignments/{a}/submissions", ta2, t)).Status);
        Assert.Equal(404, (await Get($"/assignments/{a}/submissions", outsider, t)).Status);
        Assert.Equal(200, (await Put($"/submissions/{sub}/grade", new { totalScore = 7.0, feedback = "ok" }, ta, t)).Status);
        Assert.Equal(404, (await Put($"/submissions/{sub}/grade", new { totalScore = 1.0, feedback = "no" }, ta2, t)).Status);
        // removing the staff entry removes the access at once
        Assert.Equal(200, (await Put($"/courses/{c}/staff", new { staff = new object[0] }, owner, t)).Status);
        Assert.Equal(404, (await Get($"/assignments/{a}/submissions", ta, t)).Status);
        Assert.Equal(404, (await Get($"/courses/{c}/question-banks", co, t)).Status);
    }

    [Fact]
    public async Task Chapters_slides_and_resources_can_be_read_with_lesson_access_rules()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t); var outsider = await Student(t);
        var (c, _, l) = await Course(t, inst, true);
        Assert.Equal(200, (await Put($"/lessons/{l}/chapters", new { chapters = new[] { new { title = "B", startSeconds = 30 }, new { title = "A", startSeconds = 0 } } }, inst, t)).Status);
        Assert.Equal(200, (await Put($"/lessons/{l}/slides", new { slides = new object[] { new { title = "One", body = "x" } } }, inst, t)).Status);
        // not enrolled: refused like the lesson itself
        Assert.Equal(403, (await Get($"/lessons/{l}/chapters", outsider, t)).Status);
        Assert.Equal(403, (await Get($"/lessons/{l}/slides", outsider, t)).Status);
        Assert.Equal(403, (await Get($"/lessons/{l}/resources", outsider, t)).Status);
        Assert.Equal(200, (await Post($"/courses/{c}/enroll", null, st, t)).Status);
        var ch = await Get($"/lessons/{l}/chapters", st, t); Assert.Equal(200, ch.Status);
        Assert.Equal("A", ch.Data[0].GetProperty("title").GetString()); Assert.Equal(30, ch.Data[1].GetProperty("startSeconds").GetInt32());
        var sl = await Get($"/lessons/{l}/slides", st, t); Assert.Equal("One", sl.Data[0].GetProperty("content").GetProperty("title").GetString());
        var rs = await Get($"/lessons/{l}/resources", st, t); Assert.Equal(200, rs.Status); Assert.Equal(0, rs.Data.GetArrayLength());
        Assert.Equal(200, (await Get($"/lessons/{l}/chapters", inst, t)).Status);
    }

    [Fact]
    public async Task Course_and_lesson_pricing_can_be_set_by_the_owner_only_and_makes_the_course_purchasable()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var other = await Instructor(t); var st = await Student(t);
        var (c, _, l) = await Course(t, inst, true);
        Assert.Equal(404, (await Put($"/courses/{c}/pricing", new { pricingModel = "OneTime", price = 100 }, other, t)).Status);
        Assert.Equal(403, (await Put($"/courses/{c}/pricing", new { pricingModel = "OneTime", price = 100 }, st, t)).Status);
        Assert.Equal(400, (await Put($"/courses/{c}/pricing", new { pricingModel = "OneTime", price = 0 }, inst, t)).Status);
        Assert.Equal(400, (await Put($"/courses/{c}/pricing", new { pricingModel = "Nope", price = 5 }, inst, t)).Status);
        Assert.Equal(200, (await Put($"/courses/{c}/pricing", new { pricingModel = "OneTime", price = 120.5, currency = "egp" }, inst, t)).Status);
        var d = await _c.Get($"/courses/{c}", st.Token, t.Slug, st.Dev);
        Assert.Equal("OneTime", d.Data.GetProperty("pricingModel").GetString()); Assert.Equal(120.5m, d.Data.GetProperty("price").GetDecimal());
        Assert.Equal(200, (await Post("/cart/items", new { itemType = "Course", itemId = c }, st, t)).Status);
        Assert.Equal(404, (await Put($"/lessons/{l}/price", new { price = 10 }, other, t)).Status);
        Assert.Equal(400, (await Put($"/lessons/{l}/price", new { price = -1 }, inst, t)).Status);
        Assert.Equal(200, (await Put($"/lessons/{l}/price", new { price = 15 }, inst, t)).Status);
    }
}
