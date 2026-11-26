using Microsoft.EntityFrameworkCore;
using SmartLMS.Domain.Enums;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Infrastructure.Services.Lms;

/// <summary>Background jobs run by Hangfire. They run without a user or a tenant, so every query here is explicit about crossing tenants and only changes rows that are already due.</summary>
public sealed class MaintenanceJobs(ApplicationDbContext db, TransactionalMailer mail)
{
    public const string ExpireEnrollmentsId = "expire-enrollments";
    public const string LiveRemindersId = "live-session-reminders";

    /// <summary>Emails enrolled students once, about an hour before a live session starts. Idempotent via ReminderSentAtUtc.</summary>
    public async Task<int> SendLiveRemindersAsync()
    {
        var now = DateTime.UtcNow; var until = now.AddHours(1);
        var sessions = await db.LiveSessions.IgnoreQueryFilters()
            .Where(x => x.ReminderSentAtUtc == null && x.Status == LiveSessionStatus.Scheduled && x.StartsAtUtc > now && x.StartsAtUtc <= until).ToListAsync();
        var sent = 0;
        foreach (var s in sessions)
        {
            var claimed = await db.LiveSessions.IgnoreQueryFilters().Where(x => x.Id == s.Id && x.ReminderSentAtUtc == null)
                .ExecuteUpdateAsync(x => x.SetProperty(y => y.ReminderSentAtUtc, now));
            if (claimed == 0) continue; // another instance got there first
            var courseTitle = await db.Courses.IgnoreQueryFilters().Where(c => c.Id == s.CourseId).Select(c => c.Title).FirstOrDefaultAsync() ?? "";
            var recipients = await (from e in db.Enrollments.IgnoreQueryFilters()
                                    join u in db.Users.IgnoreQueryFilters() on e.UserId equals u.Id
                                    where e.CourseId == s.CourseId && e.Status != EnrollmentStatus.Revoked && e.Status != EnrollmentStatus.Expired && u.Email != null
                                    select new { u.Email, u.FullName, u.PreferredLanguage }).Distinct().ToListAsync();
            foreach (var r in recipients) await mail.LiveSessionReminderAsync(r.Email!, r.FullName, r.PreferredLanguage, s.Title, courseTitle, s.StartsAtUtc, CancellationToken.None);
            sent++;
        }
        return sent;
    }

    /// <summary>Marks Active enrollments whose ExpiresAtUtc has passed as Expired. Idempotent: a second run changes nothing. Returns the number changed.</summary>
    public async Task<int> ExpireEnrollmentsAsync()
    {
        var now = DateTime.UtcNow;
        return await db.Enrollments.IgnoreQueryFilters().Where(e => e.Status == EnrollmentStatus.Active && e.ExpiresAtUtc != null && e.ExpiresAtUtc <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, EnrollmentStatus.Expired));
    }
}
