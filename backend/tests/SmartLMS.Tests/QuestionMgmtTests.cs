using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Tests;

public sealed class QuestionMgmtTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    private readonly HttpClient _c;
    private const string PW = ApiFactory.Password;
    private static string Fp(string s) => "fingerprint-" + s + "-0123456789";
    private static string U() => Guid.NewGuid().ToString("N")[..8];
    private static object Device(string fp) => new { fingerprint = Fp(fp), deviceType = "Mobile", platform = "test", deviceName = "t" };
    public QuestionMgmtTests(ApiFactory f) { _f = f; _c = f.CreateClient(); }

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
    public async Task Question_bank_management_import_and_exam_update()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var other = await Instructor(t); var st = await Student(t);
        var c = Id(await Post("/courses", new { title = "C" }, inst, t));
        var bank = Id(await Post("/question-banks", new { courseId = c, name = "Bank", topic = "T" }, inst, t));
        Assert.Equal(404, (await Get($"/courses/{c}/question-banks", other, t)).Status);
        Assert.Equal(403, (await Get($"/courses/{c}/question-banks", st, t)).Status);
        Assert.Equal(1, (await Get($"/courses/{c}/question-banks", inst, t)).Data.GetArrayLength());
        // JSON import, all or nothing
        var bad = await Post($"/question-banks/{bank}/import", new { format = "json", content = System.Text.Json.JsonSerializer.Serialize(new object[] {
            new { questionType = "McqSingle", text = "ok", points = 1.0, options = new[] { Opt("a", true), Opt("b", false) } },
            new { questionType = "McqSingle", text = "no correct option", points = 1.0, options = new[] { Opt("a", false), Opt("b", false) } } }) }, inst, t);
        Assert.Equal(400, bad.Status); Assert.Contains("Row 2", bad.Body.ToString());
        Assert.Equal(0, (await Get($"/question-banks/{bank}/questions", inst, t)).Data.GetArrayLength());   // nothing saved
        Assert.Equal(404, (await Post($"/question-banks/{bank}/import", new { format = "json", content = "[]" }, other, t)).Status);
        var csv = "type,text,points,difficulty,topic,explanation,options,answer\nMcqSingle,\"Capital, of Egypt?\",2,Easy,geo,,Cairo|Giza|Luxor,1\nTrueFalse,Nile is a river,1,,geo,,,true\nFillBlank,2+2=,1,,math,,,4\n";
        var ok = await Post($"/question-banks/{bank}/import", new { format = "csv", content = csv }, inst, t);
        Assert.Equal(3, ok.Data.GetProperty("imported").GetInt32());
        var list = (await Get($"/question-banks/{bank}/questions?topic=geo", inst, t)).Data; Assert.Equal(2, list.GetArrayLength());
        Assert.Equal(1, (await Get($"/question-banks/{bank}/questions?type=FillBlank", inst, t)).Data.GetArrayLength());
        Assert.Equal(404, (await Get($"/question-banks/{bank}/questions", other, t)).Status);
        var qid = list[0].GetProperty("id").GetString();
        Assert.Equal(200, (await Put($"/questions/{qid}", new { questionType = "McqSingle", text = "Edited?", points = 3.0, options = new[] { Opt("x", true), Opt("y", false) } }, inst, t)).Status);
        Assert.Equal(404, (await Put($"/questions/{qid}", new { questionType = "McqSingle", text = "hack", points = 3.0, options = new[] { Opt("x", true), Opt("y", false) } }, other, t)).Status);
        Assert.Equal(400, (await Put($"/questions/{qid}", new { questionType = "McqSingle", text = "bad", points = 3.0, options = new[] { Opt("x", false), Opt("y", false) } }, inst, t)).Status);
        var all = (await Get($"/question-banks/{bank}/questions", inst, t)).Data; var edited = all.EnumerateArray().First(x => x.GetProperty("id").GetString() == qid);
        Assert.Equal("Edited?", edited.GetProperty("text").GetString()); Assert.Equal(2, edited.GetProperty("options").GetArrayLength());
        // exam update, and a published exam's question cannot be deleted
        var e = Id(await Post($"/courses/{c}/exams", new { title = "E", kind = "Exam", durationMinutes = 10, maxAttempts = 1, passingScorePercent = 50.0, questionIds = new[] { qid } }, inst, t));
        Assert.Equal(200, (await Put($"/exams/{e}", new { title = "E2", kind = "Exam", durationMinutes = 20, maxAttempts = 2, passingScorePercent = 60.0 }, inst, t)).Status);
        Assert.Equal(404, (await Put($"/exams/{e}", new { title = "x", durationMinutes = 20, maxAttempts = 2, passingScorePercent = 60.0 }, other, t)).Status);
        Assert.Equal(400, (await Put($"/exams/{e}", new { title = "x", durationMinutes = 20, maxAttempts = 99, passingScorePercent = 60.0 }, inst, t)).Status);
        var del = await _c.Send(HttpMethod.Delete, $"/questions/{qid}", null, other.Token, t.Slug, other.Dev); Assert.Equal(404, del.Status);
        Assert.Equal(200, (await Post($"/exams/{e}/publish", null, inst, t)).Status);
        var del2 = await _c.Send(HttpMethod.Delete, $"/questions/{qid}", null, inst.Token, t.Slug, inst.Dev); Assert.Equal(409, del2.Status);
        var free = all.EnumerateArray().First(x => x.GetProperty("id").GetString() != qid).GetProperty("id").GetString();
        Assert.Equal(200, (await _c.Send(HttpMethod.Delete, $"/questions/{free}", null, inst.Token, t.Slug, inst.Dev)).Status);
    }
}
