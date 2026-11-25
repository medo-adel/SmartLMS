using Hangfire;
using SmartLMS.Application.Interfaces;
using SmartLMS.Infrastructure.Services.Lms;

namespace SmartLMS.API.Infrastructure;

public sealed class HangfireJobQueue(IBackgroundJobClient client) : IJobQueue
{
    public string? EnqueuePeerReviewAssignment(Guid jobId, Guid tenantId) => client.Enqueue<PeerReviewJob>(j => j.RunAsync(jobId, tenantId));
    public string? Enqueue(Guid jobId, Guid tenantId) => client.Enqueue<SmartLMS.Infrastructure.Services.Commerce.BackgroundJobRunner>(j => j.RunAsync(jobId, tenantId));
}
