using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Tests;

public sealed class LmsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    private readonly HttpClient _c;
    private const string PW = ApiFactory.Password;
    private static string Fp(string s) => "fingerprint-" + s + "-0123456789";
    private static string U() => Guid.NewGuid().ToString("N")[..8];
    private static object Device(string fp) => new { fingerprint = Fp(fp), deviceType = "Mobile", platform = "test", deviceName = "t" };
    public LmsTests(ApiFactory f) { _f = f; _c = f.CreateClient(); }

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

    [Fact]
    public async Task Course_workflow_enrollment_and_progress()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t);
        var (c, s, l) = await Course(t, inst, publish: false);
        // a draft is invisible to students and anonymous callers
        Assert.Equal(404, (await Get($"/courses/{c}", st, t)).Status);
        Assert.Equal(0, (await _c.Get("/courses", null, t.Slug)).Data.GetProperty("totalCount").GetInt32());
        // students cannot enroll in a draft
        Assert.Equal(404, (await Post($"/courses/{c}/enroll", null, st, t)).Status);
        Assert.Equal(200, (await Post($"/courses/{c}/submit-for-approval", null, inst, t)).Status);
        Assert.Equal(200, (await Post($"/courses/{c}/approve", null, t.Admin, t)).Status);
        Assert.Equal(1, (await _c.Get("/courses", null, t.Slug)).Data.GetProperty("totalCount").GetInt32());
        // not enrolled: lesson content refused, outline has no content
        var denied = await Get($"/lessons/{l}", st, t);
        Assert.Equal(403, denied.Status); Assert.Equal("NOT_ENROLLED", denied.Code);
        var outline = (await _c.Get($"/courses/{c}", null, t.Slug)).Body.ToString();
        Assert.DoesNotContain("secret body", outline);
        Assert.Equal(403, (await Put($"/lessons/{l}/progress", new { lastPositionSeconds = 5, watchedSecondsDelta = 5, completed = false }, st, t)).Status);
        // enroll and read
        Assert.Equal(200, (await Post($"/courses/{c}/enroll", null, st, t)).Status);
        var ok = await Get($"/lessons/{l}", st, t);
        Assert.Equal(200, ok.Status); Assert.Equal("secret body", ok.Data.GetProperty("contentMarkdown").GetString());
        // the first heartbeat is capped (60 s), completion finishes the course
        var hb = await Put($"/lessons/{l}/progress", new { lastPositionSeconds = 40, watchedSecondsDelta = 99999, completed = true }, st, t);
        Assert.Equal(100.0, hb.Data.GetProperty("progressPercent").GetDouble());
        Assert.Equal("Completed", hb.Data.GetProperty("status").GetString());
        await using var scope = _f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(60, await db.LessonProgress.IgnoreQueryFilters().Where(p => p.LessonId == Guid.Parse(l)).Select(p => p.WatchedSeconds).SingleAsync()); // first heartbeat has no baseline: capped at 60 s
        Assert.Equal(1, (await Get("/me/enrollments", st, t)).Data.GetArrayLength());
        // Phase 2 scope now has its first real relationship: the owner instructor sees this student, nobody else does
        var other = await Instructor(t);
        Assert.Equal(1, (await Get("/users?role=Student", inst, t)).Data.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, (await Get("/users?role=Student", other, t)).Data.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Role_and_ownership_boundaries()
    {
        var t = await NewTenant(); var a = await Instructor(t); var b = await Instructor(t); var st = await Student(t);
        var (c, s, l) = await Course(t, a, publish: false);
        Assert.Equal(404, (await Put($"/courses/{c}", new { title = "hijack" }, b, t)).Status);
        Assert.Equal(404, (await Post($"/courses/{c}/sections", new { title = "x" }, b, t)).Status);
        Assert.Equal(404, (await Post($"/sections/{s}/lessons", new { title = "x", lessonType = "Article" }, b, t)).Status);
        Assert.Equal(404, (await Put($"/lessons/{l}", new { title = "x", lessonType = "Article" }, b, t)).Status);
        Assert.Equal(404, (await _c.Send(HttpMethod.Delete, $"/courses/{c}", null, b.Token, t.Slug, b.Dev)).Status);
        Assert.Equal(404, (await Get($"/lessons/{l}", b, t)).Status);
        Assert.Equal(404, (await Get($"/courses/{c}", b, t)).Status);
        Assert.Equal(403, (await Post("/courses", new { title = "x" }, st, t)).Status);
        Assert.Equal(403, (await Post($"/courses/{c}/approve", null, a, t)).Status);
        Assert.Equal(403, (await Post($"/courses/{c}/reject", new { comment = "no" }, a, t)).Status);
        Assert.Equal(403, (await Post("/categories", new { name = "x" }, a, t)).Status);
        Assert.Equal(401, (await _c.Post("/courses", new { title = "x" }, null, t.Slug)).Status);
        // approving something that was never submitted is refused
        Assert.Equal(409, (await Post($"/courses/{c}/approve", null, t.Admin, t)).Status);
        // a rejected course goes back to the owner and can be resubmitted
        Assert.Equal(200, (await Post($"/courses/{c}/submit-for-approval", null, a, t)).Status);
        Assert.Equal(200, (await Post($"/courses/{c}/reject", new { comment = "needs work" }, t.Admin, t)).Status);
        Assert.Equal(0, (await _c.Get("/courses", null, t.Slug)).Data.GetProperty("totalCount").GetInt32());
        Assert.Equal(200, (await Post($"/courses/{c}/submit-for-approval", null, a, t)).Status);
    }

    [Fact]
    public async Task Tenants_cannot_see_each_others_courses()
    {
        var t1 = await NewTenant(); var t2 = await NewTenant();
        var i1 = await Instructor(t1); var s2 = await Student(t2);
        var (c, _, l) = await Course(t1, i1, publish: true);
        Assert.Equal(404, (await Get($"/courses/{c}", s2, t2)).Status);
        Assert.Equal(404, (await Post($"/courses/{c}/enroll", null, s2, t2)).Status);
        Assert.Equal(404, (await Get($"/lessons/{l}", s2, t2)).Status);
        Assert.Equal(0, (await _c.Get("/courses", null, t2.Slug)).Data.GetProperty("totalCount").GetInt32());
        // a token for tenant 2 cannot be replayed against tenant 1
        Assert.Equal(403, (await _c.Get($"/lessons/{l}", s2.Token, t1.Slug, s2.Dev)).Status);
    }

    [Fact]
    public async Task Prerequisite_and_drip_rules_are_enforced_on_the_server()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t);
        var (c, s, l1) = await Course(t, inst, publish: false, drip: "DaysAfterEnrollment");
        var l2 = Id(await Post($"/sections/{s}/lessons", new { title = "L2", lessonType = "Article", contentMarkdown = "two", prerequisiteLessonId = l1 }, inst, t));
        var l3 = Id(await Post($"/sections/{s}/lessons", new { title = "L3", lessonType = "Article", contentMarkdown = "three", dripDaysAfterEnrollment = 7 }, inst, t));
        // cycles and cross-course prerequisites are refused
        Assert.Equal(400, (await Put($"/lessons/{l1}", new { title = "L1", lessonType = "Article", prerequisiteLessonId = l2 }, inst, t)).Status);
        Assert.Equal(400, (await Put($"/lessons/{l1}", new { title = "L1", lessonType = "Article", prerequisiteLessonId = l1 }, inst, t)).Status);
        var (_, _, other) = await Course(t, inst, publish: false);
        Assert.Equal(400, (await Put($"/lessons/{l2}", new { title = "L2", lessonType = "Article", prerequisiteLessonId = other }, inst, t)).Status);
        await Post($"/courses/{c}/submit-for-approval", null, inst, t); await Post($"/courses/{c}/approve", null, t.Admin, t);
        await Post($"/courses/{c}/enroll", null, st, t);
        var locked = await Get($"/lessons/{l2}", st, t);
        Assert.Equal(403, locked.Status); Assert.Equal("PREREQUISITE_NOT_COMPLETED", locked.Code);
        var drip = await Get($"/lessons/{l3}", st, t);
        Assert.Equal(403, drip.Status); Assert.Equal("LESSON_LOCKED_DRIP", drip.Code);
        await Put($"/lessons/{l1}/progress", new { lastPositionSeconds = 1, watchedSecondsDelta = 1, completed = true }, st, t);
        Assert.Equal(200, (await Get($"/lessons/{l2}", st, t)).Status);
        // moving the clock past the drip date unlocks it
        await using (var scope = _f.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Enrollments.IgnoreQueryFilters().Where(e => e.CourseId == Guid.Parse(c)).ExecuteUpdateAsync(x => x.SetProperty(e => e.EnrolledAtUtc, DateTime.UtcNow.AddDays(-8)));
        }
        Assert.Equal(200, (await Get($"/lessons/{l3}", st, t)).Status);
    }

    [Fact]
    public async Task Free_preview_is_open_to_any_student_but_paid_courses_need_payment()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t);
        var (c, s, l) = await Course(t, inst, publish: true, freePreview: true);
        var l2 = Id(await Post($"/sections/{s}/lessons", new { title = "L2", lessonType = "Article", contentMarkdown = "paid body" }, inst, t));
        Assert.Equal(200, (await Get($"/lessons/{l}", st, t)).Status);
        Assert.Equal(403, (await Get($"/lessons/{l2}", st, t)).Status);
        await using var scope = _f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Courses.IgnoreQueryFilters().Where(x => x.Id == Guid.Parse(c)).ExecuteUpdateAsync(x => x.SetProperty(e => e.PricingModel, CoursePricingModel.OneTime).SetProperty(e => e.Price, 50m));
        var r = await Post($"/courses/{c}/enroll", null, st, t);
        Assert.Equal(402, r.Status); Assert.Equal("PAYMENT_REQUIRED", r.Code);
        Assert.False(await db.Enrollments.IgnoreQueryFilters().AnyAsync(e => e.CourseId == Guid.Parse(c)));
    }

    [Fact]
    public async Task Parallel_enroll_creates_one_enrollment()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t);
        var (c, _, _) = await Course(t, inst, publish: true);
        var rs = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Post($"/courses/{c}/enroll", null, st, t)));
        Assert.All(rs, r => Assert.Equal(200, r.Status));
        Assert.Single(rs.Select(r => r.Data.GetProperty("enrollmentId").GetString()).Distinct());
        await using var scope = _f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Enrollments.IgnoreQueryFilters().CountAsync(e => e.CourseId == Guid.Parse(c)));
    }

    [Fact]
    public async Task Revoked_and_expired_enrollments_lose_access()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t);
        var (c, _, l) = await Course(t, inst, publish: true);
        await Post($"/courses/{c}/enroll", null, st, t);
        Assert.Equal(200, (await Get($"/lessons/{l}", st, t)).Status);
        await using var scope = _f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Enrollments.IgnoreQueryFilters().Where(e => e.CourseId == Guid.Parse(c)).ExecuteUpdateAsync(x => x.SetProperty(e => e.ExpiresAtUtc, DateTime.UtcNow.AddDays(-1)));
        Assert.Equal(403, (await Get($"/lessons/{l}", st, t)).Status);
        await db.Enrollments.IgnoreQueryFilters().Where(e => e.CourseId == Guid.Parse(c)).ExecuteUpdateAsync(x => x.SetProperty(e => e.ExpiresAtUtc, (DateTime?)null).SetProperty(e => e.Status, EnrollmentStatus.Revoked));
        Assert.Equal(403, (await Get($"/lessons/{l}", st, t)).Status);
        Assert.Equal(403, (await Post($"/courses/{c}/enroll", null, st, t)).Status);
        // and the instructor no longer sees a revoked student
        Assert.Equal(0, (await Get("/users?role=Student", inst, t)).Data.GetProperty("totalCount").GetInt32());
    }

    // ---- assessments ---------------------------------------------------------------------------------------------------
    private async Task<(string Exam, List<string> Qs, string Course)> ExamFixture(T t, Who inst, int maxAttempts = 2, int? minutes = 30, int? maxTabs = null)
    {
        var (c, _, _) = await Course(t, inst, publish: true);
        var bank = Id(await Post("/question-banks", new { courseId = c, name = "Bank" }, inst, t));
        var q1 = await Post($"/question-banks/{bank}/questions", new { questionType = "McqSingle", text = "2+2?", points = 2.0, options = new[] { new { text = "3", isCorrect = false, matchKey = (string?)null }, new { text = "4", isCorrect = true, matchKey = (string?)null } } }, inst, t);
        var q2 = await Post($"/question-banks/{bank}/questions", new { questionType = "FillBlank", text = "Capital of Egypt?", points = 1.0, options = new[] { new { text = "Cairo", isCorrect = true, matchKey = (string?)null } } }, inst, t);
        var q3 = await Post($"/question-banks/{bank}/questions", new { questionType = "Essay", text = "Explain.", points = 3.0 }, inst, t);
        Assert.Equal(201, q1.Status); Assert.Equal(201, q2.Status); Assert.Equal(201, q3.Status);
        var qs = new List<string> { Id(q1), Id(q2), Id(q3) };
        var e = await Post($"/courses/{c}/exams", new { title = "Exam", kind = "Exam", durationMinutes = minutes, maxAttempts, passingScorePercent = 50.0, questionIds = qs, tabSwitchDetection = maxTabs != null, maxTabSwitches = maxTabs }, inst, t);
        Assert.Equal(201, e.Status);
        Assert.Equal(200, (await Post($"/exams/{Id(e)}/publish", null, inst, t)).Status);
        return (Id(e), qs, c);
    }

    [Fact]
    public async Task Exam_attempt_never_leaks_answers_grades_objectively_and_waits_for_manual_questions()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t);
        var (exam, _, c) = await ExamFixture(t, inst);
        Assert.Equal(403, (await Post($"/exams/{exam}/attempts", null, st, t)).Code == "NOT_ENROLLED" ? 403 : 0);
        await Post($"/courses/{c}/enroll", null, st, t);
        var start = await Post($"/exams/{exam}/attempts", null, st, t);
        Assert.Equal(200, start.Status);
        var att = start.Data.GetProperty("attemptId").GetString()!;
        var view = await Get($"/attempts/{att}", st, t);
        var raw = view.Body.ToString();
        Assert.DoesNotContain("isCorrect", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("explanation", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cairo", raw);
        var qs = view.Data.GetProperty("questions").EnumerateArray().ToList();
        string AqOf(string type) => qs.First(q => q.GetProperty("type").GetString() == type).GetProperty("attemptQuestionId").GetString()!;
        var mcq = qs.First(q => q.GetProperty("type").GetString() == "McqSingle");
        var four = mcq.GetProperty("options").EnumerateArray().First(o => o.GetProperty("text").GetString() == "4").GetProperty("id").GetString();
        var save = await _c.Send(HttpMethod.Put, $"/attempts/{att}/answers", new { answers = new object[] {
            new { attemptQuestionId = AqOf("McqSingle"), answer = new { optionId = four } },
            new { attemptQuestionId = AqOf("FillBlank"), answer = new { text = " cairo " } },
            new { attemptQuestionId = AqOf("Essay"), answer = new { text = "my essay" } } } }, st.Token, t.Slug, st.Dev);
        Assert.Equal(200, save.Status);
        // a second student cannot touch or read this attempt
        var other = await Student(t); await Post($"/courses/{c}/enroll", null, other, t);
        Assert.Equal(404, (await Get($"/attempts/{att}", other, t)).Status);
        Assert.Equal(404, (await Post($"/attempts/{att}/submit", null, other, t)).Status);
        Assert.Equal(404, (await _c.Send(HttpMethod.Put, $"/attempts/{att}/answers", new { answers = Array.Empty<object>() }, other.Token, t.Slug, other.Dev)).Status);
        // submit: objective parts graded, essay pending
        var sub = await Post($"/attempts/{att}/submit", null, st, t);
        Assert.Equal(200, sub.Status);
        Assert.Equal(3.0, sub.Data.GetProperty("score").GetDouble());
        Assert.True(sub.Data.GetProperty("awaitingManualGrading").GetBoolean());
        Assert.Equal(JsonValueKindNull, sub.Data.GetProperty("passed").ValueKind.ToString());
        Assert.Equal(JsonValueKindNull, "Null");
        // the student result carries no per-question detail
        Assert.Equal(System.Text.Json.JsonValueKind.Null, (await Get($"/attempts/{att}/result", st, t)).Data.GetProperty("answers").ValueKind);
        // a second submit is refused and the answers are frozen
        Assert.Equal(409, (await Post($"/attempts/{att}/submit", null, st, t)).Status);
        Assert.Equal(409, (await _c.Send(HttpMethod.Put, $"/attempts/{att}/answers", new { answers = Array.Empty<object>() }, st.Token, t.Slug, st.Dev)).Status);
        // instructor grades the essay; another instructor and students cannot
        var full = (await Get($"/attempts/{att}/result", inst, t)).Data.GetProperty("answers").EnumerateArray().First(a => a.GetProperty("isCorrect").ValueKind == System.Text.Json.JsonValueKind.Null).GetProperty("answerId").GetString();
        var rival = await Instructor(t);
        Assert.Equal(404, (await Put($"/attempt-answers/{full}/grade", new { points = 3.0 }, rival, t)).Status);
        Assert.Equal(403, (await Put($"/attempt-answers/{full}/grade", new { points = 3.0 }, st, t)).Status);
        Assert.Equal(400, (await Put($"/attempt-answers/{full}/grade", new { points = 99.0 }, inst, t)).Status);
        Assert.Equal(200, (await Put($"/attempt-answers/{full}/grade", new { points = 2.0, feedback = "ok" }, inst, t)).Status);
        var done = (await Get($"/attempts/{att}/result", st, t)).Data;
        Assert.Equal(5.0, done.GetProperty("score").GetDouble());
        Assert.True(done.GetProperty("passed").GetBoolean());
        Assert.Equal("Graded", done.GetProperty("status").GetString());
        Assert.Equal(404, (await Get($"/attempts/{att}/result", other, t)).Status);
    }
    private const string JsonValueKindNull = "Null";

    [Fact]
    public async Task Attempt_limit_holds_under_parallel_starts_and_timer_is_server_side()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t);
        var (exam, _, c) = await ExamFixture(t, inst, maxAttempts: 1);
        await Post($"/courses/{c}/enroll", null, st, t);
        var rs = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Post($"/exams/{exam}/attempts", null, st, t)));
        await using var scope = _f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.ExamAttempts.IgnoreQueryFilters().CountAsync(a => a.ExamId == Guid.Parse(exam)));
        Assert.All(rs, r => Assert.Contains(r.Status, new[] { 200, 409 }));
        var attempt = await db.ExamAttempts.IgnoreQueryFilters().SingleAsync(a => a.ExamId == Guid.Parse(exam));
        // push the deadline into the past: saving and submitting are refused and the attempt is closed with a score from saved answers
        await db.ExamAttempts.IgnoreQueryFilters().Where(a => a.Id == attempt.Id).ExecuteUpdateAsync(x => x.SetProperty(a => a.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-5)));
        Assert.Equal(409, (await _c.Send(HttpMethod.Put, $"/attempts/{attempt.Id}/answers", new { answers = Array.Empty<object>() }, st.Token, t.Slug, st.Dev)).Status);
        var late = await Post($"/attempts/{attempt.Id}/submit", null, st, t);
        Assert.Equal(409, late.Status); Assert.Equal("ATTEMPT_EXPIRED", late.Code);
        Assert.Equal(ExamAttemptStatus.Expired, await db.ExamAttempts.IgnoreQueryFilters().Where(a => a.Id == attempt.Id).Select(a => a.Status).SingleAsync());
        var again = await Post($"/exams/{exam}/attempts", null, st, t);
        Assert.Equal(409, again.Status); Assert.Equal("ATTEMPT_LIMIT", again.Code);
    }

    [Fact]
    public async Task Parallel_submits_grade_once_and_tab_switch_limit_voids()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t);
        var (exam, _, c) = await ExamFixture(t, inst, maxAttempts: 2, maxTabs: 1);
        await Post($"/courses/{c}/enroll", null, st, t);
        var att = (await Post($"/exams/{exam}/attempts", null, st, t)).Data.GetProperty("attemptId").GetString()!;
        var rs = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Post($"/attempts/{att}/submit", null, st, t)));
        Assert.Equal(1, rs.Count(r => r.Status == 200));
        Assert.All(rs.Where(r => r.Status != 200), r => Assert.Equal(409, r.Status));
        var att2 = (await Post($"/exams/{exam}/attempts", null, st, t)).Data.GetProperty("attemptId").GetString()!;
        Assert.False((await Post($"/attempts/{att2}/events", new { eventType = "TabSwitch" }, st, t)).Data.GetProperty("voided").GetBoolean());
        Assert.True((await Post($"/attempts/{att2}/events", new { eventType = "TabSwitch" }, st, t)).Data.GetProperty("voided").GetBoolean());
        Assert.Equal(409, (await Post($"/attempts/{att2}/submit", null, st, t)).Status);
    }

    [Fact]
    public async Task Assessment_authoring_is_owner_only_and_validates_questions()
    {
        var t = await NewTenant(); var a = await Instructor(t); var b = await Instructor(t); var st = await Student(t);
        var (c, _, _) = await Course(t, a, publish: true);
        Assert.Equal(404, (await Post("/question-banks", new { courseId = c, name = "x" }, b, t)).Status);
        Assert.Equal(403, (await Post("/question-banks", new { courseId = c, name = "x" }, st, t)).Status);
        var bank = Id(await Post("/question-banks", new { courseId = c, name = "x" }, a, t));
        Assert.Equal(404, (await Post($"/question-banks/{bank}/questions", new { questionType = "TrueFalse", text = "q", points = 1.0, trueFalseAnswer = true }, b, t)).Status);
        Assert.Equal(400, (await Post($"/question-banks/{bank}/questions", new { questionType = "McqSingle", text = "q", points = 1.0, options = new[] { new { text = "a", isCorrect = true, matchKey = (string?)null }, new { text = "b", isCorrect = true, matchKey = (string?)null } } }, a, t)).Status);
        Assert.Equal(400, (await Post($"/question-banks/{bank}/questions", new { questionType = "TrueFalse", text = "q", points = 0.0, trueFalseAnswer = true }, a, t)).Status);
        var q = Id(await Post($"/question-banks/{bank}/questions", new { questionType = "TrueFalse", text = "q", points = 1.0, trueFalseAnswer = true }, a, t));
        // another instructor's exam cannot borrow this question, and students cannot see unpublished exams
        var (c2, _, _) = await Course(t, b, publish: true);
        Assert.Equal(404, (await Post($"/courses/{c2}/exams", new { title = "x", maxAttempts = 1, passingScorePercent = 50.0, questionIds = new[] { q } }, b, t)).Status);
        var e = Id(await Post($"/courses/{c}/exams", new { title = "draft", maxAttempts = 1, passingScorePercent = 50.0, questionIds = new[] { q } }, a, t));
        await Post($"/courses/{c}/enroll", null, st, t);
        Assert.Equal(0, (await Get($"/courses/{c}/exams", st, t)).Data.GetArrayLength());
        Assert.Equal(404, (await Post($"/exams/{e}/attempts", null, st, t)).Status);
        Assert.Equal(404, (await Post($"/exams/{e}/publish", null, b, t)).Status);
        Assert.Equal(404, (await Get($"/exams/{e}/attempts", b, t)).Status);
    }

    [Fact]
    public async Task Watched_time_never_exceeds_elapsed_time_whether_heartbeats_are_live_or_replayed_and_retries_are_ignored()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t);
        var (c, _, l) = await Course(t, inst, publish: true);
        Assert.Equal(200, (await Post($"/courses/{c}/enroll", null, st, t)).Status);
        var lessonId = Guid.Parse(l);
        async Task<(int Pos, int Watched)> Row()
        {
            await using var scope = _f.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var p = await db.LessonProgress.IgnoreQueryFilters().Where(x => x.LessonId == lessonId).Select(x => new { x.LastPositionSeconds, x.WatchedSeconds }).SingleAsync();
            return (p.LastPositionSeconds, p.WatchedSeconds);
        }
        async Task LastAccessWas(DateTime when)
        {
            await using var scope = _f.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.LessonProgress.IgnoreQueryFilters().Where(x => x.LessonId == lessonId).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastAccessedAtUtc, when));
        }
        Task<Resp> Live(int pos, int delta) => Put($"/lessons/{l}/progress", new { lastPositionSeconds = pos, watchedSecondsDelta = delta, completed = false }, st, t);
        Task<Resp> Replay(int pos, int delta, DateTime at) => Put($"/lessons/{l}/progress", new { lastPositionSeconds = pos, watchedSecondsDelta = delta, completed = false, recordedAtUtc = at }, st, t);

        // first heartbeat: no baseline, credits what it claims up to 60 s
        Assert.Equal(200, (await Live(100, 10)).Status);
        Assert.Equal((100, 10), await Row());
        // a client hammering live heartbeats claims 60 s each, but almost no time passed: nothing more is credited
        for (var i = 0; i < 5; i++) Assert.Equal(200, (await Live(100 + i, 60)).Status);
        Assert.Equal(10, (await Row()).Watched); // sub-second gaps credit nothing and the fractions are carried, not rounded up
        // one hour later a heartbeat claiming 45 s is credited in full (it fits in the elapsed time)
        await LastAccessWas(DateTime.UtcNow.AddHours(-1));
        var before = (await Row()).Watched;
        Assert.Equal(200, (await Live(140, 45)).Status);
        Assert.Equal(before + 45, (await Row()).Watched);

        // an offline record from BEFORE the last heartbeat keeps the position and credits nothing
        var w0 = (await Row()).Watched;
        Assert.Equal(200, (await Replay(30, 20, DateTime.UtcNow.AddHours(-2))).Status);
        Assert.Equal((140, w0), await Row());

        // an offline entry recorded 1 minute after the last heartbeat credits its 45 s and moves the position
        var baseTime = DateTime.UtcNow.AddHours(-1); await LastAccessWas(baseTime);
        var entry = baseTime.AddMinutes(1);
        Assert.Equal(200, (await Replay(160, 45, entry)).Status);
        Assert.Equal((160, w0 + 45), await Row());
        // the SAME entry sent again (response lost, client retried) changes nothing
        for (var i = 0; i < 3; i++) Assert.Equal(200, (await Replay(160, 45, entry)).Status);
        Assert.Equal((160, w0 + 45), await Row());
        // an entry 10 s later claiming 300 s is credited at most the 10 s that passed
        Assert.Equal(200, (await Replay(170, 300, entry.AddSeconds(10))).Status);
        var (pos, watched) = await Row();
        Assert.Equal(170, pos); Assert.InRange(watched - (w0 + 45), 9, 10);

        // a record dated far in the future is not trusted: nothing credited, nothing moved, and a retry is ignored as well
        var w1 = (await Row()).Watched;
        for (var i = 0; i < 2; i++) Assert.Equal(200, (await Replay(999, 300, DateTime.UtcNow.AddDays(30))).Status);
        Assert.Equal((170, w1), await Row());
        // a clock only slightly ahead (within 2 minutes) is accepted once; the retry carries the same value and is ignored
        await LastAccessWas(DateTime.UtcNow.AddHours(-1));
        var ahead = DateTime.UtcNow.AddSeconds(60);
        Assert.Equal(200, (await Replay(180, 50, ahead)).Status);
        var w2 = (await Row()).Watched; Assert.Equal(w1 + 50, w2);
        for (var i = 0; i < 3; i++) Assert.Equal(200, (await Replay(180, 50, ahead)).Status);
        Assert.Equal((180, w2), await Row());

        // a stale replay can still complete the lesson, without moving the position or crediting time
        var done = await Put($"/lessons/{l}/progress", new { lastPositionSeconds = 5, watchedSecondsDelta = 0, completed = true, recordedAtUtc = DateTime.UtcNow.AddHours(-3) }, st, t);
        Assert.Equal("Completed", done.Data.GetProperty("status").GetString());
        Assert.Equal((180, w2), await Row());
    }

    [Fact]
    public async Task Many_subsecond_heartbeats_and_concurrent_requests_cannot_credit_more_than_the_elapsed_time()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t);
        var (c, _, l) = await Course(t, inst, publish: true);
        Assert.Equal(200, (await Post($"/courses/{c}/enroll", null, st, t)).Status);
        var lessonId = Guid.Parse(l);
        async Task<int> Watched()
        {
            await using var scope = _f.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await db.LessonProgress.IgnoreQueryFilters().Where(x => x.LessonId == lessonId).Select(x => x.WatchedSeconds).SingleAsync();
        }
        Task<Resp> Live(int delta) => Put($"/lessons/{l}/progress", new { lastPositionSeconds = 10, watchedSecondsDelta = delta, completed = false }, st, t);
        Assert.Equal(200, (await Live(1)).Status);
        var start = await Watched();
        // 1) many heartbeats sent back to back, each claiming 60 s: credit is bounded by the real elapsed seconds, fractions are carried
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 60; i++) Assert.Equal(200, (await Live(60)).Status);
        var credited = (await Watched()) - start;
        Assert.True(credited <= Math.Ceiling(sw.Elapsed.TotalSeconds), $"credited {credited}s in {sw.Elapsed.TotalSeconds:F2}s");
        // 2) the accounting clock is 100 s behind; 12 concurrent heartbeats each claim 60 s: together they can credit at most the 100 s (+ the time the test took)
        await using (var scope = _f.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.LessonProgress.IgnoreQueryFilters().Where(x => x.LessonId == lessonId).ExecuteUpdateAsync(u => u.SetProperty(x => x.LastAccessedAtUtc, DateTime.UtcNow.AddSeconds(-100)));
        }
        var before = await Watched(); var sw2 = System.Diagnostics.Stopwatch.StartNew();
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Live(60)));
        Assert.All(results, x => Assert.Equal(200, x.Status));
        var gained = (await Watched()) - before;
        Assert.True(gained <= 100 + Math.Ceiling(sw2.Elapsed.TotalSeconds), $"concurrent requests credited {gained}s");
        Assert.True(gained >= 60, "the legitimate time is still credited");
    }

    [Fact]
    public async Task Responses_carry_security_headers_and_api_responses_are_not_cached()
    {
        var res = await _f.CreateClient().GetAsync("/api/v1/health");
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", res.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", res.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("no-store", string.Join(",", res.Headers.GetValues("Cache-Control")));
    }
}
