using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Tests;

public sealed class CommunityTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    private readonly HttpClient _c;
    private const string PW = ApiFactory.Password;
    private static string Fp(string s) => "fingerprint-" + s + "-0123456789";
    private static string U() => Guid.NewGuid().ToString("N")[..8];
    private static object Device(string fp) => new { fingerprint = Fp(fp), deviceType = "Mobile", platform = "test", deviceName = "t" };
    public CommunityTests(ApiFactory f) { _f = f; _c = f.CreateClient(); }

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
    private async Task<(string Course, string Lesson)> Published(T t, Who inst)
    {
        var c = Id(await Post("/courses", new { title = "Intro Course" }, inst, t));
        var s = Id(await Post($"/courses/{c}/sections", new { title = "S" }, inst, t));
        var l = Id(await Post($"/sections/{s}/lessons", new { title = "L", lessonType = "Article", contentMarkdown = "x" }, inst, t));
        await Post($"/courses/{c}/submit-for-approval", null, inst, t); await Post($"/courses/{c}/approve", null, t.Admin, t);
        return (c, l);
    }

    [Fact]
    public async Task Notes_are_private_and_need_enrollment()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var a = await Student(t); var b = await Student(t);
        var (c, l) = await Published(t, inst);
        Assert.Equal(404, (await Post($"/lessons/{l}/notes", new { body = "x" }, a, t)).Status);   // not enrolled
        await Post($"/courses/{c}/enroll", null, a, t); await Post($"/courses/{c}/enroll", null, b, t);
        var n = await Post($"/lessons/{l}/notes", new { timestampSeconds = 5, body = "mine" }, a, t); Assert.Equal(200, n.Status);
        Assert.Equal(0, (await Get($"/lessons/{l}/notes", b, t)).Data.GetArrayLength());
        Assert.Equal(404, (await Put($"/notes/{Id(n)}", new { body = "hack" }, b, t)).Status);
        Assert.Equal(404, (await _c.Send(HttpMethod.Delete, $"/notes/{Id(n)}", null, b.Token, t.Slug, b.Dev)).Status);
        Assert.Equal(1, (await Get($"/lessons/{l}/notes", a, t)).Data.GetArrayLength());
        Assert.Equal(400, (await Post("/study-sessions", new { startedAtUtc = DateTime.UtcNow.AddMinutes(-5), durationSeconds = 999999 }, a, t)).Status);
    }

    [Fact]
    public async Task Discussions_are_course_scoped_and_votes_cannot_inflate()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var a = await Student(t); var out1 = await Student(t);
        var (c, _) = await Published(t, inst); await Post($"/courses/{c}/enroll", null, a, t);
        Assert.Equal(404, (await Get($"/courses/{c}/threads", out1, t)).Status);
        var th = Id(await Post($"/courses/{c}/threads", new { title = "Q", body = "why" }, a, t));
        Assert.Equal(404, (await Get($"/threads/{th}", out1, t)).Status);
        Assert.Equal(404, (await Post($"/threads/{th}/posts", new { body = "x" }, out1, t)).Status);
        var rs = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Post($"/threads/{th}/upvote", null, a, t)));
        Assert.All(rs, r => Assert.Equal(200, r.Status));
        var n = (await Get($"/threads/{th}", a, t)).Data.GetProperty("upvoteCount").GetInt32();
        Assert.InRange(n, 0, 1);                                   // one user can never count more than once
        Assert.Equal(403, (await Post($"/threads/{th}/pin", null, a, t)).Status);   // students cannot pin
        Assert.Equal(200, (await Post($"/threads/{th}/pin", null, inst, t)).Status);
    }

    [Fact]
    public async Task Announcements_and_notifications_respect_roles_and_owners()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var other = await Instructor(t); var a = await Student(t); var b = await Student(t);
        var (c, _) = await Published(t, inst); await Post($"/courses/{c}/enroll", null, a, t);
        Assert.Equal(403, (await Post("/announcements", new { courseId = c, title = "t", body = "b" }, a, t)).Status);
        Assert.Equal(403, (await Post("/announcements", new { title = "all", body = "b" }, inst, t)).Status);          // tenant-wide: admin only
        Assert.Equal(404, (await Post("/announcements", new { courseId = c, title = "t", body = "b" }, other, t)).Status); // not the owner
        Assert.Equal(200, (await Post("/announcements", new { courseId = c, title = "Hello", body = "b" }, inst, t)).Status);
        var na = (await Get("/notifications", a, t)).Data; Assert.Equal(1, na.GetArrayLength());
        Assert.Equal(0, (await Get("/notifications", b, t)).Data.GetArrayLength());
        var nid = na[0].GetProperty("id").GetString();
        Assert.Equal(404, (await Post($"/notifications/{nid}/read", null, b, t)).Status);
        Assert.Equal(200, (await Post($"/notifications/{nid}/read", null, a, t)).Status);
        Assert.Equal(1, (await Get("/announcements", a, t)).Data.GetArrayLength());
        Assert.Equal(0, (await Get("/announcements", b, t)).Data.GetArrayLength());
        var tk = new { fcmToken = "tok-" + U(), platform = "Android" };
        var pr = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Post("/notifications/push-token", tk, a, t)));
        Assert.All(pr, r => Assert.Equal(200, r.Status));
    }

    [Fact]
    public async Task Chat_needs_membership_and_does_not_cross_tenants()
    {
        var t = await NewTenant(); var t2 = await NewTenant(); var inst = await Instructor(t); var a = await Student(t); var out1 = await Student(t); var foreign = await Student(t2);
        var (c, _) = await Published(t, inst); await Post($"/courses/{c}/enroll", null, a, t);
        var rooms = (await Get("/chat/rooms", a, t)).Data; Assert.Equal(1, rooms.GetArrayLength());
        var room = rooms[0].GetProperty("id").GetString();
        Assert.Equal(0, (await Get("/chat/rooms", out1, t)).Data.GetArrayLength());
        Assert.Equal(404, (await Post($"/chat/rooms/{room}/messages", new { body = "hi" }, out1, t)).Status);
        Assert.Equal(404, (await Get($"/chat/rooms/{room}/messages", out1, t)).Status);
        Assert.Equal(404, (await Get($"/chat/rooms/{room}/messages", foreign, t2)).Status);
        Assert.Equal(200, (await Post($"/chat/rooms/{room}/messages", new { body = "hello" }, a, t)).Status);
        Assert.Equal(400, (await Post($"/chat/rooms/{room}/messages", new { body = "  " }, a, t)).Status);
        // revoking the enrollment closes the room even though the membership row remains
        var rr = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Get("/chat/rooms", a, t)));
        Assert.All(rr, r => Assert.Equal(1, r.Data.GetArrayLength()));   // concurrent lazy creation yields one room
    }

    [Fact]
    public async Task Live_sessions_validate_links_and_record_attendance_once()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var a = await Student(t); var out1 = await Student(t);
        var (c, _) = await Published(t, inst); await Post($"/courses/{c}/enroll", null, a, t);
        var bad = await Post($"/courses/{c}/live-sessions", new { title = "x", provider = "Zoom", joinUrl = "javascript:alert(1)", startsAtUtc = DateTime.UtcNow.AddHours(1) }, inst, t);
        Assert.Equal(400, bad.Status);
        var ok = await Post($"/courses/{c}/live-sessions", new { title = "Live", provider = "Jitsi", joinUrl = "https://meet.example.test/room", startsAtUtc = DateTime.UtcNow.AddHours(1) }, inst, t);
        Assert.Equal(200, ok.Status); var id = Id(ok);
        Assert.Equal(404, (await Post($"/live-sessions/{id}/join", null, out1, t)).Status);
        var js = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Post($"/live-sessions/{id}/join", null, a, t)));
        Assert.All(js, r => Assert.Equal(200, r.Status));
        Assert.Equal(1, (await Get($"/live-sessions/{id}/attendance", inst, t)).Data.GetArrayLength());
        Assert.Equal(403, (await Get($"/live-sessions/{id}/attendance", a, t)).Status);
        var list = (await Get($"/courses/{c}/live-sessions", a, t)).Data[0];
        Assert.Equal(JsonValueKind.Null, list.GetProperty("joinUrl").ValueKind);   // link only via join (which records attendance)
    }

    [Fact]
    public async Task Learning_paths_publish_is_admin_only_and_enroll_is_idempotent()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var a = await Student(t);
        var (c, _) = await Published(t, inst);
        Assert.Equal(403, (await Post("/learning-paths", new { title = "P", status = "Published", courseIds = new[] { c } }, inst, t)).Status);
        var p = await Post("/learning-paths", new { title = "Path", status = "Draft", courseIds = new[] { c } }, inst, t); Assert.Equal(200, p.Status);
        Assert.Equal(404, (await Post($"/learning-paths/{Id(p)}/enroll", null, a, t)).Status);           // draft not enrollable
        Assert.Equal(200, (await Put($"/learning-paths/{Id(p)}", new { title = "Path", status = "Published", courseIds = new[] { c } }, t.Admin, t)).Status);
        var es = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Post($"/learning-paths/{Id(p)}/enroll", null, a, t)));
        Assert.All(es, r => Assert.Equal(200, r.Status));
        Assert.Equal(1, await CountPathEnrollments(t));
        Assert.Equal(1, (await _c.Get("/learning-paths", tenant: t.Slug)).Data.GetArrayLength());
    }
    private async Task<int> CountPathEnrollments(T t)
    {
        using var s = _f.Services.CreateScope(); var db = s.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.LearningPathEnrollments.IgnoreQueryFilters().CountAsync();
    }

    private Task<Resp> Del(string p, Who w, T t) => _c.Send(HttpMethod.Delete, p, null, w.Token, t.Slug, w.Dev);

    [Fact]
    public async Task Reviews_enrollment_gate_aggregates_reporting_and_moderation()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var (c, _, _) = await Course(t, inst, publish: true);
        var st = await Student(t); var st2 = await Student(t);
        // not enrolled yet: 403
        Assert.Equal(403, (await Put($"/courses/{c}/review", new { rating = 5, body = "Great" }, st, t)).Status);
        Assert.Equal(200, (await Post($"/courses/{c}/enroll", null, st, t)).Status);
        Assert.Equal(200, (await Post($"/courses/{c}/enroll", null, st2, t)).Status);
        // invalid rating: 400
        Assert.Equal(400, (await Put($"/courses/{c}/review", new { rating = 0 }, st, t)).Status);
        // upsert own review; aggregate + myReview
        var r1 = await Put($"/courses/{c}/review", new { rating = 5, body = "Great course" }, st, t); Assert.True(r1.Success, r1.Body.ToString());
        var v = await Get($"/courses/{c}/reviews", st, t); Assert.True(v.Success, v.Body.ToString());
        Assert.Equal(1, v.Data.GetProperty("count").GetInt32()); Assert.Equal(5.0, v.Data.GetProperty("average").GetDouble());
        Assert.Equal(5, v.Data.GetProperty("myReview").GetProperty("rating").GetInt32());
        // upsert again updates the same row
        var r1b = await Put($"/courses/{c}/review", new { rating = 4, body = "Still good" }, st, t);
        Assert.Equal(Id(r1), Id(r1b)); Assert.Equal(1, (await Get($"/courses/{c}/reviews", st, t)).Data.GetProperty("count").GetInt32());
        // course detail carries the aggregate
        var d = await Get($"/courses/{c}", st, t); Assert.Equal(4.0, d.Data.GetProperty("averageRating").GetDouble()); Assert.Equal(1, d.Data.GetProperty("ratingsCount").GetInt32());
        // second student reviews: average recomputed
        await Put($"/courses/{c}/review", new { rating = 2, body = "Not for me" }, st2, t);
        Assert.Equal(3.0, (await Get($"/courses/{c}/reviews", st, t)).Data.GetProperty("average").GetDouble());
        // st cannot report their own review; st2 reports st's review once (idempotent)
        Assert.Equal(400, (await Post($"/reviews/{Id(r1)}/reports", new { reason = "spam" }, st, t)).Status);
        var rep = await Post($"/reviews/{Id(r1)}/reports", new { reason = "spam link" }, st2, t); Assert.True(rep.Success, rep.Body.ToString());
        Assert.Equal(Id(rep), Id(await Post($"/reviews/{Id(r1)}/reports", new { reason = "spam again" }, st2, t)));
        // students cannot moderate
        Assert.Equal(403, (await Get("/reviews/reported", st, t)).Status);
        Assert.Equal(403, (await Post($"/reviews/{Id(r1)}/hide", null, st, t)).Status);
        // admin sees the open report; an unrelated instructor does not
        var mods = await Get("/reviews/reported", t.Admin, t);
        Assert.Single(mods.Data.GetProperty("items").EnumerateArray());
        var other = await Instructor(t); Assert.Empty((await Get("/reviews/reported", other, t)).Data.GetProperty("items").EnumerateArray());
        // the course owner instructor can moderate: hide removes it from public list + aggregate, report is actioned
        Assert.Equal(200, (await Post($"/reviews/{Id(r1)}/hide", null, inst, t)).Status);
        var v2 = await Get($"/courses/{c}/reviews", st2, t);
        Assert.Equal(1, v2.Data.GetProperty("count").GetInt32()); Assert.Equal(2.0, v2.Data.GetProperty("average").GetDouble());
        Assert.Empty((await Get("/reviews/reported", t.Admin, t)).Data.GetProperty("items").EnumerateArray());
        // the hidden author sees their own review status; unhide restores it
        Assert.Equal("Hidden", (await Get($"/courses/{c}/reviews", st, t)).Data.GetProperty("myReview").GetProperty("status").GetString());
        Assert.Equal(200, (await Post($"/reviews/{Id(r1)}/unhide", null, t.Admin, t)).Status);
        Assert.Equal(2, (await Get($"/courses/{c}/reviews", st, t)).Data.GetProperty("count").GetInt32());
        // delete own review
        Assert.Equal(200, (await Del($"/courses/{c}/review", st2, t)).Status);
        Assert.Equal(1, (await Get($"/courses/{c}/reviews", st, t)).Data.GetProperty("count").GetInt32());
    }
}
