using Microsoft.EntityFrameworkCore;
using SmartLMS.Infrastructure.Services.Lms;
using Microsoft.Extensions.DependencyInjection;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Tests;

public sealed class JobTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    private readonly HttpClient _c;
    private const string PW = ApiFactory.Password;
    private static string Fp(string s) => "fingerprint-" + s + "-0123456789";
    private static string U() => Guid.NewGuid().ToString("N")[..8];
    private static object Device(string fp) => new { fingerprint = Fp(fp), deviceType = "Mobile", platform = "test", deviceName = "t" };
    public JobTests(ApiFactory f) { _f = f; _c = f.CreateClient(); }

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
    public async Task Hangfire_runs_the_recurring_job_that_expires_due_enrollments_only()
    {
        var t = await NewTenant(); var inst = await Instructor(t); var st = await Student(t); var st2 = await Student(t);
        var c = Id(await Post("/courses", new { title = "Intro Course" }, inst, t));
        var s = Id(await Post($"/courses/{c}/sections", new { title = "S" }, inst, t));
        await Post($"/sections/{s}/lessons", new { title = "L", lessonType = "Article", contentMarkdown = "x" }, inst, t);
        await Post($"/courses/{c}/submit-for-approval", null, inst, t); await Post($"/courses/{c}/approve", null, t.Admin, t);
        await Post($"/courses/{c}/enroll", null, st, t); await Post($"/courses/{c}/enroll", null, st2, t);
        using var f2 = _f.WithWebHostBuilder(b => { b.UseSetting("Hangfire:Enabled", "true"); b.UseSetting("Hangfire:Storage", "InMemory"); });
        using var _ = f2.CreateClient();   // starts the host, the Hangfire server and registers the recurring job
        using (var scope = f2.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var mine = await db.Enrollments.IgnoreQueryFilters().Where(e => e.CourseId == Guid.Parse(c)).OrderBy(e => e.Id).ToListAsync();
            mine[0].ExpiresAtUtc = DateTime.UtcNow.AddDays(-1); mine[1].ExpiresAtUtc = DateTime.UtcNow.AddDays(30);
            using (scope.ServiceProvider.GetRequiredService<SmartLMS.Application.Interfaces.ITenantContext>().Use(mine[0].TenantId)) await db.SaveChangesAsync();
            scope.ServiceProvider.GetRequiredService<Hangfire.IRecurringJobManager>().Trigger(MaintenanceJobs.ExpireEnrollmentsId);
            var deadline = DateTime.UtcNow.AddSeconds(30); List<EnrollmentStatus> states = [];
            while (DateTime.UtcNow < deadline)
            {
                states = await db.Enrollments.IgnoreQueryFilters().AsNoTracking().Where(e => e.CourseId == Guid.Parse(c)).OrderBy(e => e.Id).Select(e => e.Status).ToListAsync();
                if (states[0] == EnrollmentStatus.Expired) break; await Task.Delay(500);
            }
            Assert.Equal(EnrollmentStatus.Expired, states[0]);   // the job ran inside the Hangfire server
            Assert.Equal(EnrollmentStatus.Active, states[1]);    // a not-yet-due enrollment is untouched
        }
        // running it again changes nothing
        using var s2 = f2.Services.CreateScope();
        Assert.Equal(0, await s2.ServiceProvider.GetRequiredService<MaintenanceJobs>().ExpireEnrollmentsAsync());
    }
}
