using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Tests;

public sealed class AssignmentTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    private readonly HttpClient _c;
    private const string PW = ApiFactory.Password;
    private static string Fp(string s) => "fingerprint-" + s + "-0123456789";
    private static string U() => Guid.NewGuid().ToString("N")[..8];
    private static object Device(string fp) => new { fingerprint = Fp(fp), deviceType = "Mobile", platform = "test", deviceName = "t" };
    public AssignmentTests(ApiFactory f) { _f = f; _c = f.CreateClient(); }

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
    private async Task<string> Published(T t, Who inst)
    {
        var c = Id(await Post("/courses", new { title = "Intro Course" }, inst, t));
        var s = Id(await Post($"/courses/{c}/sections", new { title = "S" }, inst, t));
        await Post($"/sections/{s}/lessons", new { title = "L", lessonType = "Article", contentMarkdown = "x" }, inst, t);
        await Post($"/courses/{c}/submit-for-approval", null, inst, t); await Post($"/courses/{c}/approve", null, t.Admin, t);
        return c;
    }
    private static object Rubric => new[] { new { name = "Clarity", description = "d", maxPoints = 6.0 }, new { name = "Depth", description = "d", maxPoints = 4.0 } };
    private async Task<string> NewAssignment(T t, Who inst, string c, object? extra = null)
    {
        var r = await Post($"/courses/{c}/assignments", new { title = "A1", rubric = Rubric, allowResubmission = true, maxResubmissions = 2, isPublished = true, isGroup = false, peerReviewEnabled = true, peerReviewsPerSubmission = 1 }, inst, t);
        Assert.True(r.Status == 200, r.Body.ToString()); return Id(r);
    }

    [Fact]
    public async Task Assignment_authoring_and_visibility_are_owner_and_enrollment_scoped()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var other = await Instructor(t); var a = await Student(t); var out1 = await Student(t);
        var c = await Published(t, inst); await Post($"/courses/{c}/enroll", null, a, t);
        Assert.Equal(404, (await Post($"/courses/{c}/assignments", new { title = "x", maxPoints = 5 }, other, t)).Status);
        Assert.Equal(403, (await Post($"/courses/{c}/assignments", new { title = "x", maxPoints = 5 }, a, t)).Status);
        Assert.Equal(400, (await Post($"/courses/{c}/assignments", new { title = "x" }, inst, t)).Status);       // no points and no rubric
        var id = await NewAssignment(t, inst, c);
        Assert.Equal(1, (await Get($"/courses/{c}/assignments", a, t)).Data.GetArrayLength());
        Assert.Equal(404, (await Get($"/courses/{c}/assignments", out1, t)).Status);
        var draft = Id(await Post($"/courses/{c}/assignments", new { title = "hidden", maxPoints = 5, isPublished = false }, inst, t));
        Assert.Equal(1, (await Get($"/courses/{c}/assignments", a, t)).Data.GetArrayLength());                      // drafts hidden from students
        Assert.Equal(404, (await Post($"/assignments/{draft}/submissions", new { textContent = "x" }, a, t)).Status);
        Assert.Equal(404, (await Post($"/assignments/{id}/submissions", new { textContent = "x" }, out1, t)).Status);
    }

    [Fact]
    public async Task Submission_grading_rubric_bounds_privacy_and_resubmission()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var other = await Instructor(t); var a = await Student(t); var b = await Student(t);
        var c = await Published(t, inst); await Post($"/courses/{c}/enroll", null, a, t); await Post($"/courses/{c}/enroll", null, b, t);
        var id = await NewAssignment(t, inst, c);
        Assert.Equal(400, (await Post($"/assignments/{id}/submissions", new { linkUrl = "javascript:alert(1)" }, a, t)).Status);
        var rs = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Post($"/assignments/{id}/submissions", new { textContent = "my work" }, a, t)));
        // MaxResubmissions=2 allows versions 1..3. Six parallel submits must produce distinct versions 1,2,3 and refuse the rest (no duplicate version, no 4th).
        var ok = rs.Where(r => r.Status == 200).ToList();
        Assert.Equal(new[] { 1, 2, 3 }, ok.Select(r => r.Data.GetProperty("version").GetInt32()).OrderBy(x => x).ToArray());
        Assert.All(rs.Where(r => r.Status != 200), r => Assert.Equal(409, r.Status));
        var sub = Id(ok.First(r => r.Data.GetProperty("version").GetInt32() == 3));
        Assert.Equal(404, (await Get($"/submissions/{sub}", b, t)).Status);                                          // another student cannot read it
        Assert.Equal(404, (await Get($"/submissions/{sub}", other, t)).Status);                                      // nor another instructor
        Assert.Equal(3, (await Get($"/assignments/{id}/submissions", inst, t)).Data.GetArrayLength());
        Assert.Equal(404, (await Get($"/assignments/{id}/submissions", other, t)).Status);
        var crit = (await Get($"/courses/{c}/assignments", inst, t)).Data[0].GetProperty("rubric");
        var c0 = crit[0].GetProperty("id").GetString(); var c1 = crit[1].GetProperty("id").GetString();
        Assert.Equal(400, (await Put($"/submissions/{sub}/grade", new { scores = new[] { new { criterionId = c0, points = 7.0 } } }, inst, t)).Status);   // above criterion max
        Assert.Equal(403, (await Put($"/submissions/{sub}/grade", new { scores = new[] { new { criterionId = c0, points = 1.0 } } }, a, t)).Status);
        Assert.Equal(404, (await Put($"/submissions/{sub}/grade", new { scores = new[] { new { criterionId = c0, points = 1.0 } } }, other, t)).Status);
        Assert.Equal(200, (await Put($"/submissions/{sub}/grade", new { scores = new[] { new { criterionId = c0, points = 5.0 }, new { criterionId = c1, points = 3.0 } }, feedback = "ok" }, inst, t)).Status);
        var mine = (await Get($"/submissions/{sub}", a, t)).Data; Assert.Equal(8.0, mine.GetProperty("totalScore").GetDouble());
        Assert.Equal(1, (await Get("/notifications", a, t)).Data.GetArrayLength());
        Assert.Equal(200, (await Post($"/submissions/{sub}/comments", new { body = "nice" }, inst, t)).Status);
        Assert.Equal(403, (await Post($"/submissions/{sub}/comments", new { body = "x" }, a, t)).Status);
        // Graded work cannot be silently replaced; the instructor must return it first.
        Assert.Equal(409, (await Post($"/assignments/{id}/submissions", new { textContent = "again" }, a, t)).Status);
        Assert.Equal(200, (await Post($"/submissions/{sub}/return", new { feedback = "fix" }, inst, t)).Status);
        Assert.Equal(409, (await Post($"/submissions/{sub}/return", new { feedback = "fix" }, inst, t)).Status);
        var v2 = await Post($"/assignments/{id}/submissions", new { textContent = "v2" }, a, t); Assert.Equal(200, v2.Status);
        Assert.Equal(4, v2.Data.GetProperty("version").GetInt32());   // a return reopens submission beyond the automatic limit
    }

    [Fact]
    public async Task Peer_review_assigns_nobody_to_their_own_work_and_scores_once()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var a = await Student(t); var b = await Student(t); var d = await Student(t);
        var c = await Published(t, inst); foreach (var s in new[] { a, b, d }) await Post($"/courses/{c}/enroll", null, s, t);
        var id = await NewAssignment(t, inst, c);
        var sa = Id(await Post($"/assignments/{id}/submissions", new { textContent = "A" }, a, t)); var sb = Id(await Post($"/assignments/{id}/submissions", new { textContent = "B" }, b, t));
        var sd = Id(await Post($"/assignments/{id}/submissions", new { textContent = "D" }, d, t));
        Assert.Equal(403, (await Post($"/assignments/{id}/peer-reviews/assign", null, a, t)).Status);
        var runs = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Post($"/assignments/{id}/peer-reviews/assign", null, inst, t)));
        Assert.All(runs, r => Assert.Equal(200, r.Status));
        var total = 0;
        foreach (var w in new[] { a, b, d })
        {
            var mine = (await Get("/peer-reviews/mine", w, t)).Data; total += mine.GetArrayLength();
            Assert.Equal(1, mine.GetArrayLength());                                                                 // exactly K=1 each, repeats and races did not add more
            var sub = mine[0].GetProperty("submissionId").GetString();
            Assert.NotEqual(w == a ? sa : w == b ? sb : sd, sub);                                                   // never their own work
            var view = (await Get($"/submissions/{sub}", w, t)).Data; Assert.Equal(JsonValueKind.Null, view.GetProperty("author").ValueKind);   // anonymous to the reviewer
            var crit = mine[0].GetProperty("rubric"); var cid = crit[0].GetProperty("id").GetString(); var cid2 = crit[1].GetProperty("id").GetString();
            var rid = mine[0].GetProperty("id").GetString();
            Assert.Equal(400, (await Put($"/peer-reviews/{rid}", new { scores = new[] { new { criterionId = cid, points = 99.0 } } }, w, t)).Status);
            var body = new { scores = new[] { new { criterionId = cid, points = 3.0 }, new { criterionId = cid2, points = 2.0 } }, comments = "good" };
            var two = await Task.WhenAll(Put($"/peer-reviews/{rid}", body, w, t), Put($"/peer-reviews/{rid}", body, w, t));
            Assert.Equal(1, two.Count(r => r.Status == 200)); Assert.Equal(1, two.Count(r => r.Status == 409));
        }
        Assert.Equal(3, total);
        Assert.Equal(404, (await Put($"/peer-reviews/{Guid.NewGuid()}", new { scores = new object[0] }, a, t)).Status);
    }

    [Fact]
    public async Task Team_assignment_requires_a_team_and_enforces_size()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var a = await Student(t); var b = await Student(t); var d = await Student(t); var e = await Student(t);
        var c = await Published(t, inst); foreach (var s in new[] { a, b, d, e }) await Post($"/courses/{c}/enroll", null, s, t);
        var id = Id(await Post($"/courses/{c}/assignments", new { title = "Team", maxPoints = 10, isGroup = true, maxGroupSize = 2, isPublished = true }, inst, t));
        Assert.Equal(409, (await Post($"/assignments/{id}/submissions", new { textContent = "x" }, a, t)).Status);       // no team yet
        var g = (await Post($"/assignments/{id}/groups", new { name = "Team A" }, a, t)).Data.GetProperty("groupId").GetString();
        Assert.Equal(200, (await Post($"/assignments/{id}/groups", new { groupId = g }, b, t)).Status);
        var joins = await Task.WhenAll(Post($"/assignments/{id}/groups", new { groupId = g }, d, t), Post($"/assignments/{id}/groups", new { groupId = g }, e, t));
        Assert.Equal(2, joins.Count(r => r.Status == 409));
        Assert.Equal(0, joins.Count(r => r.Status == 200));                                                             // team of 2 is already full
        var sub = Id(await Post($"/assignments/{id}/submissions", new { textContent = "ours" }, a, t));
        Assert.Equal(200, (await Get($"/submissions/{sub}", b, t)).Status);                                             // a teammate can read it
        Assert.Equal(404, (await Get($"/submissions/{sub}", d, t)).Status);                                             // a non-member cannot
        Assert.Equal(409, (await Post($"/assignments/{id}/submissions", new { textContent = "again" }, b, t)).Status);  // one submission per team
    }

    [Fact]
    public async Task Peer_review_assignment_runs_as_a_hangfire_job_and_reports_its_status()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var a = await Student(t); var b = await Student(t); var d = await Student(t); var other = await Instructor(t);
        var c = await Published(t, inst); foreach (var s in new[] { a, b, d }) await Post($"/courses/{c}/enroll", null, s, t);
        var id = await NewAssignment(t, inst, c);
        foreach (var w in new[] { a, b, d }) Assert.Equal(200, (await Post($"/assignments/{id}/submissions", new { textContent = "work" }, w, t)).Status);
        using var f2 = _f.WithWebHostBuilder(bld => { bld.UseSetting("Hangfire:Enabled", "true"); bld.UseSetting("Hangfire:Storage", "InMemory"); });
        var c2 = f2.CreateClient();
        Assert.Equal(404, (await c2.Post($"/assignments/{id}/peer-reviews/assign", null, other.Token, t.Slug, other.Dev)).Status);
        var q = await c2.Post($"/assignments/{id}/peer-reviews/assign", null, inst.Token, t.Slug, inst.Dev);
        Assert.Equal(200, q.Status); Assert.True(q.Data.GetProperty("queued").GetBoolean());          // returned before the work ran
        var job = q.Data.GetProperty("jobId").GetString();
        Assert.Equal(404, (await c2.Get($"/jobs/{job}", a.Token, t.Slug, a.Dev)).Status);              // a student cannot read the job
        string status = ""; JsonElement res = default;
        for (var i = 0; i < 80 && status != "Completed" && status != "Failed"; i++)
        {
            await Task.Delay(250); var j = await c2.Get($"/jobs/{job}", inst.Token, t.Slug, inst.Dev); Assert.Equal(200, j.Status); status = j.Data.GetProperty("status").GetString()!; res = j.Data;
        }
        Assert.Equal("Completed", status); Assert.Equal(100, res.GetProperty("progressPercent").GetInt32());
        foreach (var w in new[] { a, b, d }) Assert.Equal(1, (await Get("/peer-reviews/mine", w, t)).Data.GetArrayLength());   // exactly K=1 each
        // a repeat is a new job that finds everything done and creates nothing
        var q2 = await c2.Post($"/assignments/{id}/peer-reviews/assign", null, inst.Token, t.Slug, inst.Dev); var job2 = q2.Data.GetProperty("jobId").GetString();
        for (var i = 0; i < 80; i++) { await Task.Delay(250); status = (await c2.Get($"/jobs/{job2}", inst.Token, t.Slug, inst.Dev)).Data.GetProperty("status").GetString()!; if (status == "Completed") break; }
        Assert.Equal("Completed", status);
        foreach (var w in new[] { a, b, d }) Assert.Equal(1, (await Get("/peer-reviews/mine", w, t)).Data.GetArrayLength());
        // a job for an assignment whose peer review was switched off ends Failed with a clear message, not a crash
        using var scope = f2.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tid = (await db.Tenants.IgnoreQueryFilters().FirstAsync(x => x.Slug == t.Slug)).Id; scope.ServiceProvider.GetRequiredService<SmartLMS.Application.Interfaces.ITenantContext>().Set(tid, false);
        var aid = Guid.Parse(id); var asg = await db.Assignments.FirstAsync(x => x.Id == aid); asg.PeerReviewEnabled = false;
        var bad = new BackgroundJob { TenantId = tid, Type = BackgroundJobType.PeerReviewAssign, Status = BackgroundJobStatus.Pending, ReferenceType = "Assignment", ReferenceId = aid, CreatedAtUtc = DateTime.UtcNow };
        db.BackgroundJobs.Add(bad); await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<SmartLMS.Infrastructure.Services.Lms.PeerReviewJob>().RunAsync(bad.Id, tid);
        var after = await db.BackgroundJobs.AsNoTracking().FirstAsync(x => x.Id == bad.Id);
        Assert.Equal(BackgroundJobStatus.Failed, after.Status); Assert.Contains("not enabled", after.ErrorMessage);
    }
}
