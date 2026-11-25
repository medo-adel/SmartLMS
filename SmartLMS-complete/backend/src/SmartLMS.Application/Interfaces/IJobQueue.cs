namespace SmartLMS.Application.Interfaces;

/// <summary>Queues background work. Returns the queue's job id, or null when background processing is off (the caller then runs the work itself).</summary>
public interface IJobQueue
{
    string? EnqueuePeerReviewAssignment(Guid jobId, Guid tenantId);
    /// <summary>Queues any BackgroundJobs row by id; the runner picks the handler from the row's Type.</summary>
    string? Enqueue(Guid jobId, Guid tenantId);
}
