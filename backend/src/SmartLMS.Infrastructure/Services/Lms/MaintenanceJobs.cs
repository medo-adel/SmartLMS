using Microsoft.EntityFrameworkCore;
using SmartLMS.Domain.Enums;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Infrastructure.Services.Lms;

/// <summary>Background jobs run by Hangfire. They run without a user or a tenant, so every query here is explicit about crossing tenants and only changes rows that are already due.</summary>
public sealed class MaintenanceJobs(ApplicationDbContext db)
{
    public const string ExpireEnrollmentsId = "expire-enrollments";

    /// <summary>Marks Active enrollments whose ExpiresAtUtc has passed as Expired. Idempotent: a second run changes nothing. Returns the number changed.</summary>
    public async Task<int> ExpireEnrollmentsAsync()
    {
        var now = DateTime.UtcNow;
        return await db.Enrollments.IgnoreQueryFilters().Where(e => e.Status == EnrollmentStatus.Active && e.ExpiresAtUtc != null && e.ExpiresAtUtc <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, EnrollmentStatus.Expired));
    }
}
