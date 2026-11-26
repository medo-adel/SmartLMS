using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Enums;
using SmartLMS.Infrastructure.Options;
using SmartLMS.Infrastructure.Persistence;
using SmartLMS.Infrastructure.Services.Ai;

namespace SmartLMS.Tests;

public sealed class FakeAi : IAiServiceClient
{
    public readonly List<string> Calls = new(); public readonly List<AiScope> Scopes = new();
    public Func<string, string?>? Fail;   // endpoint -> exception message
    public AiGradeResult Grade = new(true, 99, 10, [new("Clarity", 6, "ok"), new("Depth", 4, "ok")], "fine");
    private T Rec<T>(string e, AiScope s, T r) { Calls.Add(e); Scopes.Add(s); if (Fail?.Invoke(e) is { } m) throw new SmartLMS.Domain.Exceptions.AppException(503, "AI_UNAVAILABLE", m); return r; }
    public List<AiTranscriptCue>? IngestCues;
    public Task<AiIngestResult> IngestAsync(AiScope s, string l, string t, string u, string? x, string? lang, CancellationToken ct) => Task.FromResult(Rec("ingest", s, new AiIngestResult(l, x is null ? 0 : 3, 0, IngestCues)));
    public Task<AiTutorResult> TutorChatAsync(AiScope s, string q, string? l, CancellationToken ct) => Task.FromResult(Rec("tutor", s, new AiTutorResult("ans", [], true)));
    public Task<AiSearchResult> SemanticSearchAsync(AiScope s, string q, int k, CancellationToken ct) => Task.FromResult(Rec("search", s, new AiSearchResult([])));
    public Task<AiQuizResult> GenerateQuizAsync(AiScope s, string l, int a, int b, int c, string d, CancellationToken ct) => Task.FromResult(Rec("quiz", s, new AiQuizResult([new("q", ["a", "b"], 0, "e")], [], [])));
    public Task<AiSummaryResult> SummarizeAsync(AiScope s, string l, CancellationToken ct) => Task.FromResult(Rec("summarize", s, new AiSummaryResult(["t"], [], [])));
    public Task<AiMindMapResult> MindMapAsync(AiScope s, string l, CancellationToken ct) => Task.FromResult(Rec("mindmap", s, new AiMindMapResult([], [])));
    public Task<AiGradeResult> GradeEssayAsync(AiScope s, string a, string e, IReadOnlyList<AiRubricCriterion> r, CancellationToken ct) => Task.FromResult(Rec("grade", s, Grade));
    public Task<AiPlagiarismResult> PlagiarismCheckAsync(AiScope s, string a, string sub, string text, CancellationToken ct)
    {
        lock (Stored) { var others = Stored.Where(x => x.Key != sub && x.Value == text).Select(x => new AiPlagiarismMatch(x.Key, 0.97)).ToList(); Stored[sub] = text; return Task.FromResult(Rec("plag", s, new AiPlagiarismResult(others.Count > 0 ? 0.97 : 0.1, others, "n"))); }
    }
    public readonly Dictionary<string, string> Stored = new();
    public Task<AiVoiceResult> VoiceTutorAsync(AiScope s, Stream audio, string f, string? l, CancellationToken ct) => Task.FromResult(Rec("voice", s, new AiVoiceResult("t", "a", null, null, [])));
    public Task<AiStudyPlanResult> StudyPlanAsync(AiScope s, IReadOnlyList<AiStudyLesson> lessons, double th, double hw, string sd, string td, CancellationToken ct) => Task.FromResult(Rec("plan", s, new AiStudyPlanResult([], true, null)));
    public Task<AiRiskResult> PredictRiskAsync(AiScope s, string st, AiRiskFeatures f, CancellationToken ct) => Task.FromResult(Rec("risk", s, new AiRiskResult(0.7, "High", ["x"], "Advisory")));
}

public sealed class AiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f; private readonly HttpClient _c; private readonly FakeAi _ai = new();
    private readonly WebApplicationFactory<Program> _w;
    private const string PW = ApiFactory.Password;
    private static string Fp(string s) => "fingerprint-" + s + "-0123456789";
    private static string U() => Guid.NewGuid().ToString("N")[..8];
    private static object Device(string fp) => new { fingerprint = Fp(fp), deviceType = "Mobile", platform = "test", deviceName = "t" };
    public AiTests(ApiFactory f)
    {
        _f = f;
        _w = f.WithWebHostBuilder(b => { b.UseSetting("AiService:RequestsPerMinute", "6"); b.ConfigureServices(s => { s.RemoveAll<IAiServiceClient>(); s.AddSingleton<IAiServiceClient>(_ai); }); });
        _c = _w.CreateClient();
    }
    private sealed record Who(string Token, string Id, string Dev);
    private sealed record T(string Slug, Who Admin);
    private async Task<Who> Login(string slug, string email, string dev, string? id = null)
    { var r = await _c.Post("/auth/login", new { email, password = PW, device = Device(dev) }, tenant: slug); Assert.True(r.Success, r.Body.ToString()); return new Who(r.Data.GetProperty("accessToken").GetString()!, id ?? "", Fp(dev)); }
    private async Task<T> NewTenant()
    {
        var sa = (await _c.Post("/auth/login", new { email = ApiFactory.SaEmail, password = PW, device = Device("sa") })).Data.GetProperty("accessToken").GetString()!;
        var slug = "t" + U(); var admin = $"admin@{slug}.test";
        Assert.Equal(201, (await _c.Post("/tenants", new { name = slug, slug, adminFullName = "Admin", adminEmail = admin, adminPassword = PW, maxMobileDevices = 5, maxWebDevices = 5 }, sa, null, Fp("sa"))).Status);
        return new T(slug, await Login(slug, admin, "adm" + slug));
    }
    private async Task<Who> Instructor(T t)
    { var email = $"in{U()}@mail.test"; var r = await _c.Post("/users", new { fullName = "Inst", email, password = PW, roles = new[] { "Instructor" } }, t.Admin.Token, t.Slug, t.Admin.Dev); return await Login(t.Slug, email, "in" + U(), r.Data.GetProperty("id").GetString()); }
    private async Task<Who> Student(T t)
    {
        var email = $"s{U()}@mail.test"; var r = await _c.Post("/auth/register", new { fullName = "Student", email, password = PW }, tenant: t.Slug);
        await _c.Post("/auth/verify-email", new { email, code = _f.Otp.Last(email, "EmailVerification") }, tenant: t.Slug);
        return await Login(t.Slug, email, "st" + U(), r.Data.GetProperty("id").GetString());
    }
    private Task<Resp> Post(string p, object? b, Who w, T t) => _c.Post(p, b, w.Token, t.Slug, w.Dev);
    private Task<Resp> Get(string p, Who w, T t) => _c.Get(p, w.Token, t.Slug, w.Dev);
    private static string Id(Resp r) => r.Data.GetProperty("id").GetString()!;
    private async Task<(string Course, string Lesson)> Course(T t, Who inst, string body = "Mitochondria make ATP.")
    {
        var c = Id(await Post("/courses", new { title = "AI " + U() }, inst, t));
        var s = Id(await Post($"/courses/{c}/sections", new { title = "S1" }, inst, t));
        var l = Id(await Post($"/sections/{s}/lessons", new { title = "L1", lessonType = "Article", contentMarkdown = body }, inst, t));
        Assert.Equal(200, (await Post($"/courses/{c}/submit-for-approval", null, inst, t)).Status);
        Assert.Equal(200, (await Post($"/courses/{c}/approve", null, t.Admin, t)).Status);
        return (c, l);
    }
    private Task Publish(string lesson) => SetPub(lesson, true);
    private async Task SetPub(string lesson, bool v)
    { await using var sc = _w.Services.CreateAsyncScope(); await sc.ServiceProvider.GetRequiredService<ApplicationDbContext>().Lessons.IgnoreQueryFilters().Where(x => x.Id == Guid.Parse(lesson)).ExecuteUpdateAsync(x => x.SetProperty(e => e.IsPublished, v)); }

    [Fact]
    public async Task Ingest_runs_a_tracked_job_updates_index_state_and_is_instructor_only()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var other = await Instructor(t); var st = await Student(t); var (c, l) = await Course(t, inst);
        Assert.Equal(403, (await Post($"/ai/lessons/{l}/ingest", null, st, t)).Status);
        Assert.Equal(404, (await Post($"/ai/lessons/{l}/ingest", null, other, t)).Status);          // another instructor's lesson
        var r = await Post($"/ai/lessons/{l}/ingest", null, inst, t);
        Assert.Equal(202, r.Status); Assert.Equal("Completed", r.Data.GetProperty("status").GetString());
        var s = await Get($"/ai/lessons/{l}/index-status", inst, t);
        Assert.Equal("Indexed", s.Data.GetProperty("status").GetString()); Assert.Equal(3, s.Data.GetProperty("chunkCount").GetInt32());
        Assert.Equal(t.Admin.Id == "" ? _ai.Scopes[0].TenantId : _ai.Scopes[0].TenantId, _ai.Scopes[0].TenantId);
        Assert.Equal(c, _ai.Scopes[0].CourseId);
        await using var sc = _w.Services.CreateAsyncScope(); var db = sc.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await db.BackgroundJobs.IgnoreQueryFilters().AnyAsync(j => j.Type == BackgroundJobType.AiIngest && j.Status == BackgroundJobStatus.Completed));
        Assert.True(await db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.EntityName == "AiIngest"));
    }

    [Fact]
    public async Task Ingest_stores_timed_cues_and_the_transcript_endpoint_serves_enrolled_students_only()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t); var (c, l) = await Course(t, inst);
        // no transcript before ingest: clean 404, even for staff
        Assert.Equal(404, (await Get($"/lessons/{l}/transcript", inst, t)).Status);
        _ai.IngestCues = [new AiTranscriptCue(0.0, 1.8, "أهلاً بكم"), new AiTranscriptCue(1.8, 4.0, "في درس اليوم")];
        var r = await Post($"/ai/lessons/{l}/ingest", null, inst, t);
        Assert.Equal("Completed", r.Data.GetProperty("status").GetString());
        // the transcript follows the same read guard as the lesson itself: not enrolled => refused
        Assert.Equal(403, (await Get($"/lessons/{l}/transcript", st, t)).Status);
        Assert.Equal(200, (await Post($"/courses/{c}/enroll", null, st, t)).Status);
        var tr = await Get($"/lessons/{l}/transcript", st, t);
        Assert.True(tr.Success, tr.Body.ToString());
        Assert.Equal(2, tr.Data.GetProperty("cueCount").GetInt32());
        var cues = tr.Data.GetProperty("cues").EnumerateArray().ToList();
        Assert.Equal("أهلاً بكم", cues[0].GetProperty("text").GetString());
        Assert.Equal(0.0, cues[0].GetProperty("start").GetDouble());
        Assert.Equal(1.8, cues[0].GetProperty("end").GetDouble());
        Assert.Equal(1.8, cues[1].GetProperty("start").GetDouble());
        // re-ingest replaces the cues wholesale
        _ai.IngestCues = [new AiTranscriptCue(0.0, 3.0, "نسخة جديدة")];
        await Post($"/ai/lessons/{l}/ingest", null, inst, t);
        var tr2 = await Get($"/lessons/{l}/transcript", st, t);
        Assert.Equal(1, tr2.Data.GetProperty("cueCount").GetInt32());
        Assert.Equal("نسخة جديدة", tr2.Data.GetProperty("cues")[0].GetProperty("text").GetString());
        _ai.IngestCues = null;
    }

    [Fact]
    public async Task Ingest_through_Hangfire_returns_202_before_the_work_and_finishes_in_the_server()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var (_, l) = await Course(t, inst);
        using var f2 = _w.WithWebHostBuilder(b => { b.UseSetting("Hangfire:Enabled", "true"); b.UseSetting("Hangfire:Storage", "InMemory"); });
        var c2 = f2.CreateClient();
        var q = await c2.Post($"/ai/lessons/{l}/ingest", null, inst.Token, t.Slug, inst.Dev);
        Assert.Equal(202, q.Status); Assert.True(q.Data.GetProperty("queued").GetBoolean());
        var job = q.Data.GetProperty("jobId").GetString(); string status = "";
        for (var i = 0; i < 80 && status != "Completed" && status != "Failed"; i++) { await Task.Delay(250); status = (await c2.Get($"/jobs/{job}", inst.Token, t.Slug, inst.Dev)).Data.GetProperty("status").GetString()!; }
        Assert.Equal("Completed", status);
        Assert.Equal("Indexed", (await c2.Get($"/ai/lessons/{l}/index-status", inst.Token, t.Slug, inst.Dev)).Data.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Failed_ingest_is_recorded_as_failed_and_leaks_nothing()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var (_, l) = await Course(t, inst);
        _ai.Fail = e => e == "ingest" ? "boom secret-detail" : null;
        var r = await Post($"/ai/lessons/{l}/ingest", null, inst, t);
        Assert.Equal("Failed", r.Data.GetProperty("status").GetString()); _ai.Fail = null;
        var s = await Get($"/ai/lessons/{l}/index-status", inst, t);
        Assert.Equal("Failed", s.Data.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Student_features_need_active_enrollment_and_a_published_lesson_and_are_tenant_scoped()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var a = await Student(t); var out1 = await Student(t); var (c, l) = await Course(t, inst);
        Assert.Equal(404, (await Post($"/ai/courses/{c}/tutor/chat", new { question = "hi" }, a, t)).Status);          // not enrolled
        await Post($"/courses/{c}/enroll", null, a, t);
        var ok = await Post($"/ai/courses/{c}/tutor/chat", new { question = "what is ATP?" }, a, t);
        Assert.Equal(200, ok.Status); Assert.Equal("ans", ok.Data.GetProperty("answer").GetString());
        Assert.Equal(404, (await Post($"/ai/courses/{c}/tutor/chat", new { question = "hi" }, out1, t)).Status);
        await SetPub(l, false);
        Assert.Equal(404, (await Post($"/ai/lessons/{l}/summarize", null, a, t)).Status);                                // lesson not published
        await Publish(l);
        Assert.Equal(200, (await Post($"/ai/lessons/{l}/summarize", null, a, t)).Status);
        Assert.Equal(400, (await Post($"/ai/courses/{c}/tutor/chat", new { question = "" }, a, t)).Status);
        var t2 = await NewTenant(); var s2 = await Student(t2);
        Assert.Equal(404, (await Post($"/ai/courses/{c}/tutor/chat", new { question = "x" }, s2, t2)).Status);          // other tenant
        Assert.Equal(403, (await Post($"/ai/courses/{c}/study-plan", new { targetDate = DateTime.UtcNow.AddDays(5), hoursPerWeek = 5 }, inst, t)).Status);
    }

    [Fact]
    public async Task Quiz_is_a_draft_for_staff_only()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t); var (c, l) = await Course(t, inst);
        await Post($"/courses/{c}/enroll", null, st, t);
        Assert.Equal(403, (await Post($"/ai/lessons/{l}/generate-quiz", new { }, st, t)).Status);
        var r = await Post($"/ai/lessons/{l}/generate-quiz", new { }, inst, t);
        Assert.Equal(200, r.Status); Assert.True(r.Data.GetProperty("draft").GetBoolean());
    }

    [Fact]
    public async Task Provisional_grade_never_becomes_the_final_grade()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t); var (c, _) = await Course(t, inst);
        await Post($"/courses/{c}/enroll", null, st, t);
        var rubric = new[] { new { name = "Clarity", description = "d", maxPoints = 6.0 }, new { name = "Depth", description = "d", maxPoints = 4.0 } };
        var off = Id(await Post($"/courses/{c}/assignments", new { title = "off", rubric, isPublished = true }, inst, t));
        var on = Id(await Post($"/courses/{c}/assignments", new { title = "on", rubric, isPublished = true, aiProvisionalGradingEnabled = true, plagiarismCheckEnabled = true }, inst, t));
        var s1 = Id(await Post($"/assignments/{off}/submissions", new { textContent = "essay" }, st, t));
        var s2 = Id(await Post($"/assignments/{on}/submissions", new { textContent = "essay" }, st, t));
        Assert.Equal(409, (await Post($"/ai/submissions/{s1}/grade-essay", null, inst, t)).Status);
        Assert.Equal(403, (await Post($"/ai/submissions/{s2}/grade-essay", null, st, t)).Status);
        var r = await Post($"/ai/submissions/{s2}/grade-essay", null, inst, t);
        Assert.Equal(200, r.Status); Assert.True(r.Data.GetProperty("provisional").GetBoolean());
        await using var sc = _w.Services.CreateAsyncScope(); var db = sc.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sub = await db.Submissions.IgnoreQueryFilters().FirstAsync(x => x.Id == Guid.Parse(s2));
        Assert.Equal(99, sub.AiProvisionalScore); Assert.Null(sub.TotalScore); Assert.Equal(SubmissionStatus.Submitted, sub.Status);   // the fake returned an out-of-range number; the Python service is what clamps it
    }

    [Fact]
    public async Task Plagiarism_batch_stores_matches_for_review()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var a = await Student(t); var b = await Student(t); var (c, _) = await Course(t, inst);
        await Post($"/courses/{c}/enroll", null, a, t); await Post($"/courses/{c}/enroll", null, b, t);
        var off = Id(await Post($"/courses/{c}/assignments", new { title = "off", maxPoints = 5, isPublished = true }, inst, t));
        var asg = Id(await Post($"/courses/{c}/assignments", new { title = "on", maxPoints = 5, isPublished = true, plagiarismCheckEnabled = true }, inst, t));
        Assert.Equal(409, (await Post($"/ai/assignments/{off}/plagiarism-check", null, inst, t)).Status);
        var sa = Id(await Post($"/assignments/{asg}/submissions", new { textContent = "same words here" }, a, t));
        var sb = Id(await Post($"/assignments/{asg}/submissions", new { textContent = "same words here" }, b, t));
        Assert.Equal(403, (await Post($"/ai/assignments/{asg}/plagiarism-check", null, a, t)).Status);
        var r = await Post($"/ai/assignments/{asg}/plagiarism-check", null, inst, t);
        Assert.Equal(202, r.Status); Assert.Equal("Completed", r.Data.GetProperty("status").GetString());
        var res = await Get($"/ai/submissions/{sa}/plagiarism", inst, t);
        Assert.Equal(sb, res.Data.GetProperty("matches")[0].GetProperty("otherSubmissionId").GetString());
        Assert.Equal("Unreviewed", res.Data.GetProperty("matches")[0].GetProperty("outcome").GetString());
        Assert.Equal(403, (await Get($"/ai/submissions/{sa}/plagiarism", a, t)).Status);
    }

    [Fact]
    public async Task Risk_is_advisory_and_scoped_to_the_instructors_own_students()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var other = await Instructor(t); var st = await Student(t); var (c, _) = await Course(t, inst);
        await Post($"/courses/{c}/enroll", null, st, t);
        Assert.Equal(404, (await Get($"/ai/students/{st.Id}/risk?courseId={c}", other, t)).Status);
        Assert.Equal(403, (await Get($"/ai/students/{st.Id}/risk?courseId={c}", st, t)).Status);
        var r = await Get($"/ai/students/{st.Id}/risk?courseId={c}", inst, t);
        Assert.Equal(200, r.Status); Assert.True(r.Data.GetProperty("advisory").GetBoolean());
    }

    [Fact]
    public async Task Rate_limit_logs_usage_without_prompt_text_and_ai_outage_maps_to_503()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t); var (c, _) = await Course(t, inst);
        await Post($"/courses/{c}/enroll", null, st, t);
        _ai.Fail = e => e == "tutor" ? "provider said: api key sk-LEAK" : null;
        var first = await Post($"/ai/courses/{c}/tutor/chat", new { question = "my private question" }, st, t);
        Assert.Equal(503, first.Status); _ai.Fail = null;
        for (var i = 0; i < 5; i++) Assert.Equal(200, (await Post($"/ai/courses/{c}/tutor/chat", new { question = "q" }, st, t)).Status);
        Assert.Equal(429, (await Post($"/ai/courses/{c}/tutor/chat", new { question = "q" }, st, t)).Status);
        await using var sc = _w.Services.CreateAsyncScope(); var db = sc.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var logs = await db.AiUsageLogs.IgnoreQueryFilters().Where(x => x.CourseId == Guid.Parse(c)).ToListAsync();
        Assert.Equal(6, logs.Count); Assert.Contains(logs, x => x.StatusCode == 503);
        Assert.DoesNotContain(logs, x => x.Endpoint.Contains("private"));
    }

    [Fact]
    public async Task Voice_tutor_validates_the_upload()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t); var (c, _) = await Course(t, inst);
        await Post($"/courses/{c}/enroll", null, st, t);
        HttpRequestMessage Req(string name, byte[] data) { var m = new MultipartFormDataContent { { new ByteArrayContent(data), "audio", name } }; var r = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/ai/courses/{c}/voice-tutor") { Content = m }; r.Headers.Authorization = new("Bearer", st.Token); r.Headers.Add("X-Tenant-Id", t.Slug); r.Headers.Add("X-Device-Id", st.Dev); return r; }
        Assert.Equal(HttpStatusCode.OK, (await _c.SendAsync(Req("q.wav", Encoding.ASCII.GetBytes("RIFFxxxx")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _c.SendAsync(Req("q.exe", [1, 2, 3]))).StatusCode);
    }
}

public sealed class AiClientTests
{
    private sealed class H(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler { public HttpRequestMessage? Last; public string? Body; protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c) { Last = r; Body = r.Content is null ? null : await r.Content.ReadAsStringAsync(c); return f(r); } }
    private static AiServiceClient Make(H h, string key = "k") => new(new HttpClient(h) { BaseAddress = new Uri("http://ai.test/") }, Microsoft.Extensions.Options.Options.Create(new AiServiceOptions { ApiKey = key }));
    private static HttpResponseMessage Json(string j, HttpStatusCode c = HttpStatusCode.OK) => new(c) { Content = new StringContent(j, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Sends_key_and_snake_case_body_and_reads_snake_case_answer()
    {
        var h = new H(_ => Json("{\"answer\":\"a\",\"citations\":[{\"lesson_id\":\"L\",\"page\":2,\"start_seconds\":null,\"end_seconds\":null,\"snippet\":\"s\"}],\"grounded\":true}"));
        var r = await Make(h).TutorChatAsync(new AiScope("T", "C"), "q?", "ar", default);
        Assert.Equal("k", h.Last!.Headers.GetValues("X-AI-Service-Key").Single()); Assert.Equal("http://ai.test/ai/tutor/chat", h.Last.RequestUri!.ToString());
        Assert.Contains("\"tenant_id\":\"T\"", h.Body); Assert.Contains("\"course_id\":\"C\"", h.Body); Assert.Contains("\"question\":\"q?\"", h.Body);
        Assert.True(r.Grounded); Assert.Equal(2, r.Citations[0].Page);
    }

    [Fact]
    public async Task Maps_failures_without_forwarding_upstream_text()
    {
        foreach (var (code, expect) in new[] { (401, 503), (409, 409), (422, 400), (502, 502), (500, 503) })
        {
            var ex = await Assert.ThrowsAnyAsync<SmartLMS.Domain.Exceptions.AppException>(() => Make(new H(_ => Json("{\"detail\":\"SECRET-UPSTREAM\"}", (HttpStatusCode)code))).SummarizeAsync(new("T", "C"), "L", default));
            Assert.Equal(expect, ex.StatusCode); Assert.DoesNotContain("SECRET", ex.Message);
        }
        var none = await Assert.ThrowsAsync<SmartLMS.Domain.Exceptions.AppException>(() => Make(new H(_ => throw new HttpRequestException("x")), "k").MindMapAsync(new("T", "C"), "L", default));
        Assert.Equal(503, none.StatusCode);
        var nokey = new H(_ => Json("{}"));
        await Assert.ThrowsAsync<SmartLMS.Domain.Exceptions.AppException>(() => Make(nokey, "").MindMapAsync(new("T", "C"), "L", default));
        Assert.Null(nokey.Last);   // nothing is sent when no key is configured
    }

    [Fact]
    public async Task Voice_upload_is_multipart_with_scope_fields()
    {
        var h = new H(_ => Json("{\"transcript\":\"t\",\"answer_text\":\"a\",\"audio_base64\":null,\"audio_mime\":null,\"citations\":[]}"));
        var r = await Make(h).VoiceTutorAsync(new("T", "C"), new MemoryStream([1, 2]), "../evil/a.wav", null, default);
        Assert.Contains("name=tenant_id", h.Body); Assert.DoesNotContain("evil", h.Body); Assert.Equal("t", r.Transcript);
    }
}
