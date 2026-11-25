using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QRCoder;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Domain.Exceptions;
using SmartLMS.Infrastructure.Identity;
using SmartLMS.Infrastructure.Options;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Infrastructure.Services.Lms;

public sealed record LinkRequest(string Code);
public sealed record IssueCertificateRequest(Guid UserId, Guid CourseId);
public sealed record ApproveLinkRequest(bool Approve);

/// <summary>XP, streaks and the leaderboard. XP rows are only written by the server when a one-time event is won atomically.</summary>
public sealed class GamificationService(ApplicationDbContext db, ICurrentUser me)
{
    public const int LessonXp = 10, CourseXp = 100;
    private static DateTime Now => DateTime.UtcNow;
    private Guid Uid => me.UserId ?? throw new UnauthorizedAppException();

    public async Task AwardAsync(Guid tenantId, Guid userId, int points, string reason, string sourceType, Guid sourceId, CancellationToken ct)
    {
        // Callers award only after winning an atomic state change, but a repeat of the same source is still ignored.
        if (await db.XpTransactions.AnyAsync(x => x.UserId == userId && x.SourceType == sourceType && x.SourceId == sourceId && x.Reason == reason, ct)) return;
        db.XpTransactions.Add(new XpTransaction { TenantId = tenantId, UserId = userId, Points = points, Reason = reason, SourceType = sourceType, SourceId = sourceId, AwardedAtUtc = Now });
        await db.SaveChangesAsync(ct);   // the XP row is saved on its own so a streak-row race can never lose it
        var today = DateOnly.FromDateTime(Now);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var s = await db.UserStreaks.FirstOrDefaultAsync(x => x.UserId == userId, ct);
            if (s is null) { s = new UserStreak { TenantId = tenantId, UserId = userId }; db.UserStreaks.Add(s); }
            if (s.LastActivityDate != today)
            {
                s.CurrentStreakDays = s.LastActivityDate == today.AddDays(-1) ? s.CurrentStreakDays + 1 : 1;
                s.LongestStreakDays = Math.Max(s.LongestStreakDays, s.CurrentStreakDays);
                s.LastActivityDate = today;
            }
            try { await db.SaveChangesAsync(ct); break; }
            catch (DbUpdateException) { db.Entry(s).State = EntityState.Detached; }   // a parallel award created or changed the streak row; reload and apply again
        }
        await AwardBadgesAsync(tenantId, userId, ct);
    }

    // Built-in badges, created per tenant on first use. Rules are fixed in code (not read from Badge.RuleJson).
    private static readonly (string Code, string Name, string Description)[] Defs =
    [
        ("FIRST_LESSON", "First lesson", "Completed a first lesson."), ("FIRST_COURSE", "Course complete", "Completed a first course."),
        ("STREAK_7", "7-day streak", "Learned on 7 days in a row."), ("XP_1000", "1000 XP", "Earned 1000 XP."),
    ];

    private async Task AwardBadgesAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        try
        {
            var xp = await db.XpTransactions.Where(x => x.UserId == userId).SumAsync(x => (int?)x.Points, ct) ?? 0;
            var lessons = await db.XpTransactions.AnyAsync(x => x.UserId == userId && x.Reason == "LessonCompleted", ct);
            var courses = await db.XpTransactions.AnyAsync(x => x.UserId == userId && x.Reason == "CourseCompleted", ct);
            var streak = await db.UserStreaks.Where(x => x.UserId == userId).Select(x => (int?)x.LongestStreakDays).FirstOrDefaultAsync(ct) ?? 0;
            var earned = new HashSet<string>();
            if (lessons) earned.Add("FIRST_LESSON"); if (courses) earned.Add("FIRST_COURSE"); if (streak >= 7) earned.Add("STREAK_7"); if (xp >= 1000) earned.Add("XP_1000");
            foreach (var code in earned)
            {
                var b = await db.Badges.FirstOrDefaultAsync(x => x.Code == code, ct);
                if (b is null) { var d = Defs.First(x => x.Code == code); b = new Badge { TenantId = tenantId, Code = d.Code, Name = d.Name, Description = d.Description }; db.Badges.Add(b); await db.SaveChangesAsync(ct); }
                if (await db.UserBadges.AnyAsync(x => x.UserId == userId && x.BadgeId == b.Id, ct)) continue;
                db.UserBadges.Add(new UserBadge { TenantId = tenantId, UserId = userId, BadgeId = b.Id, AwardedAtUtc = Now }); await db.SaveChangesAsync(ct);
            }
        }
        catch (DbUpdateException) { foreach (var e in db.ChangeTracker.Entries().Where(e => e.State is EntityState.Added).ToList()) e.State = EntityState.Detached; }   // a parallel award won the unique index; the badge exists
    }

    public static int LevelFor(int xp) => 1 + (int)Math.Floor(Math.Sqrt(xp / 50.0));

    public async Task<object> MeAsync(CancellationToken ct)
    {
        var xp = await db.XpTransactions.Where(x => x.UserId == Uid).SumAsync(x => (int?)x.Points, ct) ?? 0;
        var st = await db.UserStreaks.FirstOrDefaultAsync(x => x.UserId == Uid, ct);
        var badges = await (from ub in db.UserBadges join b in db.Badges on ub.BadgeId equals b.Id where ub.UserId == Uid select new { b.Code, b.Name, ub.AwardedAtUtc }).ToListAsync(ct);
        return new { xp, level = LevelFor(xp), currentStreakDays = st?.CurrentStreakDays ?? 0, longestStreakDays = st?.LongestStreakDays ?? 0, badges };
    }

    public async Task<object> LeaderboardAsync(Guid? courseId, CancellationToken ct)
    {
        IQueryable<Guid> users = db.Enrollments.Select(e => e.UserId);
        if (courseId is { } c)
        {
            var course = await db.Courses.FirstOrDefaultAsync(x => x.Id == c, ct) ?? throw new NotFoundException("Course not found.");
            var staff = await new CourseAccess(db, me).IsGraderAsync(course, ct);
            if (!staff && !await db.Enrollments.AnyAsync(e => e.CourseId == c && e.UserId == Uid && e.Status != EnrollmentStatus.Revoked, ct)) throw new NotFoundException("Course not found.");
            users = db.Enrollments.Where(e => e.CourseId == c && e.Status != EnrollmentStatus.Revoked).Select(e => e.UserId);
        }
        var rows = await (from x in db.XpTransactions where users.Contains(x.UserId) group x by x.UserId into g orderby g.Sum(v => v.Points) descending select new { UserId = g.Key, Xp = g.Sum(v => v.Points) }).Take(20).ToListAsync(ct);
        var ids = rows.Select(r => r.UserId).ToList();
        var names = await db.Users.Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.FullName }).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        // Only a short display name leaves the server (first name and last initial). No email, no id of other users.
        return rows.Select((r, i) => new { rank = i + 1, name = Short(names.GetValueOrDefault(r.UserId)), r.Xp, isMe = r.UserId == me.UserId });
    }
    private static string Short(string? n)
    {
        var p = (n ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return p.Length == 0 ? "Learner" : p.Length == 1 ? p[0] : $"{p[0]} {p[^1][0]}.";
    }
}

public sealed class CertificateService(ApplicationDbContext db, ICurrentUser me, Microsoft.Extensions.Configuration.IConfiguration config, Microsoft.Extensions.Hosting.IHostEnvironment env)
{
    private static DateTime Now => DateTime.UtcNow;
    private Guid Uid => me.UserId ?? throw new UnauthorizedAppException();
    private bool IsAdmin => me.IsInRole(RoleNames.TenantAdmin) || me.IsInRole(RoleNames.SuperAdmin);

    /// <summary>Issues one certificate per (user, course). Safe to call twice: the second call returns the existing one.</summary>
    public async Task<Certificate> EnsureIssuedAsync(Guid tenantId, Guid userId, Guid courseId, CancellationToken ct)
    {
        var existing = await db.Certificates.FirstOrDefaultAsync(c => c.UserId == userId && c.CourseId == courseId && c.RevokedAtUtc == null, ct);
        if (existing is not null) return existing;
        var cert = new Certificate { TenantId = tenantId, UserId = userId, CourseId = courseId, IssuedAtUtc = Now, VerificationCode = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(18)) };
        db.Certificates.Add(cert); await db.SaveChangesAsync(ct); return cert;
    }

    public async Task<object> IssueManualAsync(IssueCertificateRequest r, CancellationToken ct)
    {
        var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == r.CourseId, ct);
        if (course is null || !await new CourseAccess(db, me).IsAuthorAsync(course, ct)) throw new NotFoundException("Course not found.");
        var enr = await db.Enrollments.FirstOrDefaultAsync(e => e.UserId == r.UserId && e.CourseId == r.CourseId && e.Status != EnrollmentStatus.Revoked, ct) ?? throw new NotFoundException("The student is not enrolled in this course.");
        var cert = await EnsureIssuedAsync(course.TenantId, enr.UserId, course.Id, ct);
        return new { cert.Id, cert.VerificationCode };
    }

    public async Task<object> MineAsync(CancellationToken ct) =>
        await (from c in db.Certificates join co in db.Courses on c.CourseId equals co.Id where c.UserId == Uid && c.RevokedAtUtc == null select new { c.Id, c.CourseId, CourseTitle = co.Title, c.IssuedAtUtc, c.VerificationCode }).ToListAsync(ct);

    /// <summary>Public. Returns only holder name, course, issue date and status. Never email or phone.</summary>
    public async Task<object> VerifyAsync(string code, CancellationToken ct)
    {
        var row = await (from c in db.Certificates join u in db.Users on c.UserId equals u.Id join co in db.Courses on c.CourseId equals co.Id where c.VerificationCode == code select new { u.FullName, co.Title, c.IssuedAtUtc, c.RevokedAtUtc }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Certificate not found.");
        return new { valid = row.RevokedAtUtc is null, holderName = row.FullName, course = row.Title, issuedAtUtc = row.IssuedAtUtc, status = row.RevokedAtUtc is null ? "Valid" : "Revoked" };
    }

    public async Task<byte[]> PdfAsync(Guid id, string? requestBaseUrl, CancellationToken ct)
    {
        var row = await (from c in db.Certificates join u in db.Users on c.UserId equals u.Id join co in db.Courses on c.CourseId equals co.Id where c.Id == id && c.RevokedAtUtc == null
                         select new { c.UserId, c.VerificationCode, c.IssuedAtUtc, u.FullName, co.Title, co.OwnerInstructorId, CourseId = co.Id }).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Certificate not found.");
        if (!IsAdmin && row.UserId != Uid && row.OwnerInstructorId != Uid && !await db.CourseStaff.AnyAsync(s => s.CourseId == row.CourseId && s.UserId == Uid && s.StaffRole == CourseStaffStaffRole.CoInstructor && me.IsInRole(RoleNames.Instructor), ct)) throw new NotFoundException("Certificate not found.");
        // The URL printed in the QR comes from configuration, never from the request Host header (which a caller controls).
        var baseUrl = config["Certificates:VerificationBaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            if (!(env.EnvironmentName == "Development") || requestBaseUrl is null) throw new AppException(500, "VERIFICATION_URL_NOT_CONFIGURED", "Certificates:VerificationBaseUrl is not configured.");
            baseUrl = requestBaseUrl;   // Development only: http://localhost:... is fine for a developer, never for a deployed certificate.
        }
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var bu) || (bu.Scheme != Uri.UriSchemeHttps && !(env.EnvironmentName == "Development"))) throw new AppException(500, "VERIFICATION_URL_INVALID", "Certificates:VerificationBaseUrl must be an absolute https URL outside Development.");
        return CertificatePdf.Build(row.FullName, row.Title, row.IssuedAtUtc, $"{baseUrl.TrimEnd('/')}/api/v1/certificates/verify/{row.VerificationCode}");
    }
}

/// <summary>
/// Parent portal. A parent sees a student only through an Active link. A student creates a short-lived signed code; a parent redeems it
/// and creates a Pending link; a TenantAdmin or an instructor of that student's courses activates it. Activation is atomic and one
/// code can activate only one parent. The code itself carries no data a parent can read.
/// </summary>
public sealed class ParentService(ApplicationDbContext db, ICurrentUser me, IOptions<JwtOptions> jwt)
{
    private static DateTime Now => DateTime.UtcNow;
    private Guid Uid => me.UserId ?? throw new UnauthorizedAppException();
    private bool IsAdmin => me.IsInRole(RoleNames.TenantAdmin) || me.IsInRole(RoleNames.SuperAdmin);
    private byte[] Key => SHA256.HashData(Encoding.UTF8.GetBytes("parent-link|" + jwt.Value.SigningKey));
    private static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    public object CreateCode()
    {
        var exp = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds();
        var payload = $"{Uid:N}.{exp}.{Convert.ToHexString(RandomNumberGenerator.GetBytes(6))}";
        var sig = Convert.ToHexString(HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(payload)))[..24];
        return new { code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(payload + "." + sig)), expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime };
    }

    private Guid? ReadCode(string code)
    {
        try
        {
            var raw = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code)); var p = raw.Split('.');
            if (p.Length != 4) return null;
            var expect = Convert.ToHexString(HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(string.Join('.', p[..3]))))[..24];
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expect), Encoding.ASCII.GetBytes(p[3]))) return null;
            if (!long.TryParse(p[1], out var exp) || DateTimeOffset.FromUnixTimeSeconds(exp) < DateTimeOffset.UtcNow) return null;
            return Guid.TryParseExact(p[0], "N", out var sid) ? sid : null;
        }
        catch (FormatException) { return null; }
    }

    public async Task<object> RequestLinkAsync(LinkRequest r, CancellationToken ct)
    {
        // One answer for every bad code (forged, expired, unknown student) so codes cannot be probed.
        var sid = ReadCode(r.Code);
        var student = sid is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Id == sid && u.IsActive, ct);
        if (student is null || !await IsStudentAsync(student.Id, ct)) throw new ValidationAppException("The link code is not valid or has expired.");
        var hash = Hash(r.Code);
        var existing = await db.ParentStudentLinks.FirstOrDefaultAsync(l => l.ParentUserId == Uid && l.StudentUserId == student.Id, ct);
        if (existing is not null)
        {
            if (existing.Status == ParentStudentLinkStatus.Revoked) throw new ForbiddenAppException("LINK_REVOKED", "This link was revoked.");
            return new { linkId = existing.Id, status = existing.Status.ToString() };
        }
        var link = new ParentStudentLink { TenantId = student.TenantId ?? throw new ValidationAppException("The link code is not valid or has expired."), ParentUserId = Uid, StudentUserId = student.Id, Status = ParentStudentLinkStatus.Pending, LinkCodeHash = hash };
        db.ParentStudentLinks.Add(link);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { db.Entry(link).State = EntityState.Detached; link = await db.ParentStudentLinks.FirstAsync(l => l.ParentUserId == Uid && l.StudentUserId == student.Id, ct); }
        return new { linkId = link.Id, status = link.Status.ToString() };
    }

    private Task<bool> IsStudentAsync(Guid userId, CancellationToken ct) =>
        db.UserRoles.AnyAsync(ur => ur.UserId == userId && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == RoleNames.Student), ct);

    public async Task SetLinkAsync(Guid id, bool approve, CancellationToken ct)
    {
        var l = await db.ParentStudentLinks.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Link not found.");
        // An instructor may only act on students who are enrolled in one of their courses.
        if (!IsAdmin && !await db.StudentsOfInstructor(Uid).AnyAsync(s => s == l.StudentUserId, ct)) throw new NotFoundException("Link not found.");
        if (!approve)
        {
            await db.ParentStudentLinks.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ParentStudentLinkStatus.Revoked), ct);
            return;
        }
        if (l.Status == ParentStudentLinkStatus.Revoked) throw new ConflictAppException("LINK_REVOKED", "A revoked link cannot be approved. Ask the parent to request again.");
        Guid? approver = Uid; DateTime? now = Now; var hash = l.LinkCodeHash;
        // Atomic: only the first request holding a given code can become Active. A second parent with the same code is refused.
        var n = await db.ParentStudentLinks.Where(x => x.Id == id && x.Status == ParentStudentLinkStatus.Pending && (hash == null || !db.ParentStudentLinks.Any(o => o.Id != id && o.LinkCodeHash == hash && o.Status == ParentStudentLinkStatus.Active)))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ParentStudentLinkStatus.Active).SetProperty(x => x.ApprovedBy, approver).SetProperty(x => x.ApprovedAtUtc, now), ct);
        if (n == 0 && l.Status != ParentStudentLinkStatus.Active) throw new ConflictAppException("CODE_ALREADY_USED", "This link code already activated another parent.");
    }

    private async Task<ParentStudentLink> ActiveLinkAsync(Guid studentId, CancellationToken ct) =>
        await db.ParentStudentLinks.FirstOrDefaultAsync(l => l.ParentUserId == Uid && l.StudentUserId == studentId && l.Status == ParentStudentLinkStatus.Active, ct) ?? throw new NotFoundException("Student not found.");

    /// <summary>Pending link requests for academy staff (same Mgr roles as SetLink). Tenant-scoped by the global filter; returns display names only.</summary>
    public async Task<object> PendingLinksAsync(CancellationToken ct) =>
        await (from l in db.ParentStudentLinks
               join pu in db.Users on l.ParentUserId equals pu.Id
               join su in db.Users on l.StudentUserId equals su.Id
               where l.Status == ParentStudentLinkStatus.Pending
               orderby l.CreatedAtUtc
               select new { linkId = l.Id, parentName = pu.FullName, studentName = su.FullName, l.CreatedAtUtc, status = l.Status.ToString() }).ToListAsync(ct);

    public async Task<object> ChildrenAsync(CancellationToken ct) =>
        await (from l in db.ParentStudentLinks join u in db.Users on l.StudentUserId equals u.Id where l.ParentUserId == Uid && l.Status == ParentStudentLinkStatus.Active select new { studentId = u.Id, u.FullName }).ToListAsync(ct);

    public async Task<object> SummaryAsync(Guid studentId, CancellationToken ct)
    {
        await ActiveLinkAsync(studentId, ct);
        var since = Now.AddDays(-7);
        var secs = await db.StudySessions.Where(s => s.UserId == studentId && s.StartedAtUtc >= since).SumAsync(s => (int?)s.DurationSeconds, ct) ?? 0;
        var courses = await (from e in db.Enrollments join c in db.Courses on e.CourseId equals c.Id where e.UserId == studentId && e.Status != EnrollmentStatus.Revoked select new { c.Title, Status = e.Status.ToString(), e.ProgressPercent }).ToListAsync(ct);
        var graded = await db.ExamAttempts.Where(a => a.UserId == studentId && a.MaxScore > 0 && (a.Status == ExamAttemptStatus.Graded)).Select(a => 100.0 * a.Score!.Value / a.MaxScore!.Value).ToListAsync(ct);
        var last = await db.LessonProgress.Where(p => p.UserId == studentId).MaxAsync(p => (DateTime?)p.LastAccessedAtUtc, ct);
        var alerts = Risks(graded, last);
        // Only summary numbers are returned: no answers, no notes, no email or phone, no device data. Attendance needs live sessions (not built) so it is null.
        return new { studentId, courses, averageExamScorePercent = graded.Count == 0 ? (double?)null : Math.Round(graded.Average(), 1), weeklyStudyHours = Math.Round(secs / 3600.0, 2), attendancePercent = (double?)null, riskAlerts = alerts.Select(a => a.Type.ToString()) };
    }

    private static List<(ParentAlertAlertType Type, string Message)> Risks(List<double> graded, DateTime? last)
    {
        var r = new List<(ParentAlertAlertType, string)>();
        if (graded.Count > 0 && graded.Average() < 50) r.Add((ParentAlertAlertType.LowQuizScore, $"Average exam score is {graded.Average():0}%."));
        if (last is null || last < Now.AddDays(-14)) r.Add((ParentAlertAlertType.Inactivity, "No learning activity in the last 14 days."));
        return r;
    }

    public async Task<object> AlertsAsync(CancellationToken ct)
    {
        var kids = await db.ParentStudentLinks.Where(l => l.ParentUserId == Uid && l.Status == ParentStudentLinkStatus.Active).Select(l => l.StudentUserId).ToListAsync(ct);
        foreach (var k in kids)
        {
            var graded = await db.ExamAttempts.Where(a => a.UserId == k && a.MaxScore > 0 && a.Status == ExamAttemptStatus.Graded).Select(a => 100.0 * a.Score!.Value / a.MaxScore!.Value).ToListAsync(ct);
            var last = await db.LessonProgress.Where(p => p.UserId == k).MaxAsync(p => (DateTime?)p.LastAccessedAtUtc, ct);
            foreach (var (type, msg) in Risks(graded, last))
                if (!await db.ParentAlerts.AnyAsync(a => a.ParentUserId == Uid && a.StudentUserId == k && a.AlertType == type && a.ReadAtUtc == null, ct))
                    db.ParentAlerts.Add(new ParentAlert { TenantId = db.CurrentTenantId!.Value, ParentUserId = Uid, StudentUserId = k, AlertType = type, Message = msg, CreatedAtUtc = Now });
        }
        await db.SaveChangesAsync(ct);
        // Alerts of a link that is no longer Active are not returned.
        return await db.ParentAlerts.Where(a => a.ParentUserId == Uid && kids.Contains(a.StudentUserId)).OrderByDescending(a => a.CreatedAtUtc).Select(a => new { a.Id, a.StudentUserId, AlertType = a.AlertType.ToString(), a.Message, a.CreatedAtUtc, a.ReadAtUtc }).ToListAsync(ct);
    }
}

public sealed class JobService(ApplicationDbContext db, ICurrentUser me)
{
    public async Task<object> GetAsync(Guid id, CancellationToken ct)
    {
        var j = await db.BackgroundJobs.FirstOrDefaultAsync(x => x.Id == id, ct);
        var admin = me.IsInRole(RoleNames.TenantAdmin) || me.IsInRole(RoleNames.SuperAdmin);
        if (j is null || (!admin && j.RequestedBy != me.UserId)) throw new NotFoundException("Job not found.");
        return new { j.Id, Type = j.Type.ToString(), Status = j.Status.ToString(), j.ProgressPercent, j.ErrorMessage, j.StartedAtUtc, j.CompletedAtUtc };
    }
}
