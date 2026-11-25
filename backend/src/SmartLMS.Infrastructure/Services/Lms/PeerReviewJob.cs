using Microsoft.EntityFrameworkCore;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Enums;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Infrastructure.Services.Lms;

public sealed class NullJobQueue : IJobQueue { public string? EnqueuePeerReviewAssignment(Guid jobId, Guid tenantId) => null; public string? Enqueue(Guid jobId, Guid tenantId) => null; }

/// <summary>Hangfire entry point for peer-review assignment. It runs with no user, so the tenant is set from the job row's tenant, and the work is idempotent (a retry or duplicate creates no extra reviews).</summary>
public sealed class PeerReviewJob(ApplicationDbContext db, ITenantContext tenant, AssignmentService assignments)
{
    [Hangfire.AutomaticRetry(Attempts = 2)]
    public async Task RunAsync(Guid jobId, Guid tenantId)
    {
        tenant.Set(tenantId, false);
        var job = await db.BackgroundJobs.FirstOrDefaultAsync(j => j.Id == jobId && j.Type == BackgroundJobType.PeerReviewAssign);
        if (job is null || job.Status is BackgroundJobStatus.Completed or BackgroundJobStatus.Failed) return;
        await assignments.RunPeerReviewJobAsync(job.Id, CancellationToken.None);
    }
}
