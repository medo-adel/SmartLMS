using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Domain.Exceptions;
using SmartLMS.Infrastructure.Persistence;
using SmartLMS.Infrastructure.Services.Lms;

namespace SmartLMS.Infrastructure.Services.Ai;

public sealed record IngestSource(string SourceType, string SourceUri, string? Text, string Hash);

/// <summary>
/// The background half of AI ingestion and plagiarism checks. Runs in Hangfire without a user (the tenant comes from the job row) or inline when Hangfire is off.
/// Both jobs report Pending, Processing, Completed or Failed in BackgroundJobs and push a SignalR "ai-job" event plus a stored notification to the requester.
/// </summary>
public sealed class AiJobService(ApplicationDbContext db, IAiServiceClient ai, IRealtime rt, Notifier notifier)
{
    private static DateTime Now => DateTime.UtcNow;

    /// <summary>What the Python service should read for a lesson. Files are passed as a path relative to the shared storage root, never as a URL.</summary>
    public async Task<IngestSource> SourceForAsync(Lesson l, CancellationToken ct)
    {
        switch (l.LessonType)
        {
            case LessonType.Article:
                if (string.IsNullOrWhiteSpace(l.ContentMarkdown)) throw new ValidationAppException("This article has no text to index.");
                return new("text", "inline", l.ContentMarkdown, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(l.ContentMarkdown))).ToLowerInvariant());
            case LessonType.Pdf or LessonType.Video or LessonType.Audio:
                var f = l.ContentFileId is { } id ? await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct) : null;
                if (f is null) throw new ValidationAppException("This lesson has no uploaded file to index.");
                return new(l.LessonType.ToString().ToLowerInvariant(), f.StoragePath, null, f.Sha256 ?? f.Id.ToString("N"));
            default: throw new ValidationAppException("This lesson type has no text, audio or document that can be indexed.");
        }
    }

    private async Task<BackgroundJob> StartAsync(Guid jobId, BackgroundJobType type, CancellationToken ct)
    {
        var job = await db.BackgroundJobs.FirstAsync(j => j.Id == jobId && j.Type == type, ct);
        job.Status = BackgroundJobStatus.Processing; job.StartedAtUtc = Now; job.ProgressPercent = 5; await db.SaveChangesAsync(ct); await PushAsync(job);
        return job;
    }
    private async Task PushAsync(BackgroundJob job, string? summary = null)
    {
        if (job.RequestedBy is not { } u) return;
        try { await rt.ToUserAsync(job.TenantId, u, "ai-job", new { jobId = job.Id, type = job.Type.ToString(), status = job.Status.ToString(), progress = job.ProgressPercent, referenceId = job.ReferenceId, summary }); } catch (Exception) { }
    }
    private async Task FinishAsync(BackgroundJob job, string? error, string title, CancellationToken ct)
    {
        job.Status = error is null ? BackgroundJobStatus.Completed : BackgroundJobStatus.Failed; job.ProgressPercent = error is null ? 100 : job.ProgressPercent;
        job.ErrorMessage = error; job.CompletedAtUtc = Now; await db.SaveChangesAsync(ct); await PushAsync(job, error);
        if (job.RequestedBy is { } u) { try { await notifier.SendAsync(job.TenantId, [u], error is null ? "AiJobDone" : "AiJobFailed", title, error, ct); } catch (Exception) { db.ChangeTracker.Clear(); } }
    }

    // ---- ingest -----------------------------------------------------------------------------------------------------------
    public async Task RunIngestAsync(Guid jobId, CancellationToken ct)
    {
        var job = await db.BackgroundJobs.FirstAsync(j => j.Id == jobId, ct);
        if (job.Status is BackgroundJobStatus.Completed or BackgroundJobStatus.Failed) return;
        job = await StartAsync(jobId, BackgroundJobType.AiIngest, ct);
        var lessonId = job.ReferenceId!.Value;
        var state = await db.LessonIndexStates.FirstAsync(s => s.LessonId == lessonId, ct);
        state.Status = LessonIndexStateStatus.Processing; await db.SaveChangesAsync(ct);
        string? error = null;
        try
        {
            var l = await db.Lessons.FirstOrDefaultAsync(x => x.Id == lessonId && !x.IsDeleted, ct) ?? throw new NotFoundException("Lesson not found.");
            var src = await SourceForAsync(l, ct);
            var r = await ai.IngestAsync(new AiScope(job.TenantId.ToString(), l.CourseId.ToString()), lessonId.ToString(), src.SourceType, src.SourceUri, src.Text, null, ct);
            state.Status = r.ChunksIndexed > 0 ? LessonIndexStateStatus.Indexed : LessonIndexStateStatus.Failed;
            state.ChunkCount = r.ChunksIndexed; state.ContentHash = src.Hash; state.IndexedAtUtc = Now;
            state.LastError = r.ChunksIndexed > 0 ? null : "No text could be extracted from this lesson.";
            error = state.LastError;
            job.ResultJson = System.Text.Json.JsonSerializer.Serialize(new { r.ChunksIndexed, r.TranscriptSegments });
        }
        catch (Exception ex) when (ex is AppException or DomainException)
        {
            error = ex.Message.Length > 900 ? ex.Message[..900] : ex.Message; state.Status = LessonIndexStateStatus.Failed; state.LastError = error;
        }
        await FinishAsync(job, error, error is null ? "Lesson indexed for the AI tutor" : "Lesson indexing failed", ct);
    }

    // ---- plagiarism -------------------------------------------------------------------------------------------------------
    public async Task RunPlagiarismAsync(Guid jobId, CancellationToken ct)
    {
        var job = await db.BackgroundJobs.FirstAsync(j => j.Id == jobId, ct);
        if (job.Status is BackgroundJobStatus.Completed or BackgroundJobStatus.Failed) return;
        job = await StartAsync(jobId, BackgroundJobType.PlagiarismBatch, ct);
        string? error = null; var flagged = 0; var checkedCount = 0;
        try
        {
            var a = await db.Assignments.FirstOrDefaultAsync(x => x.Id == job.ReferenceId, ct) ?? throw new NotFoundException("Assignment not found.");
            // Latest text version of each submitter; the same text twice would only match itself.
            var all = await db.Submissions.AsNoTracking().Where(s => s.AssignmentId == a.Id && s.TextContent != null && s.TextContent != "").ToListAsync(ct);
            var subs = all.GroupBy(s => (s.UserId, s.AssignmentGroupId)).Select(g => g.OrderByDescending(x => x.Version).First()).ToList();
            var scope = new AiScope(job.TenantId.ToString(), a.CourseId.ToString());
            // Pass 1 stores every text so that pass 2 sees all of them. A single pass would only compare each submission with the ones before it.
            foreach (var s in subs) await ai.PlagiarismCheckAsync(scope, a.Id.ToString(), s.Id.ToString(), s.TextContent!, ct);
            job.ProgressPercent = 50; await db.SaveChangesAsync(ct); await PushAsync(job);
            var valid = subs.Select(s => s.Id.ToString()).ToHashSet();
            foreach (var s in subs)
            {
                var r = await ai.PlagiarismCheckAsync(scope, a.Id.ToString(), s.Id.ToString(), s.TextContent!, ct);
                var chk = new PlagiarismCheck { TenantId = job.TenantId, SubmissionId = s.Id, BackgroundJobId = job.Id, Status = PlagiarismCheckStatus.Completed, MaxSimilarity = r.MaxSimilarity, CheckedAtUtc = Now, CreatedAtUtc = Now };
                db.PlagiarismChecks.Add(chk); await db.SaveChangesAsync(ct);
                foreach (var m in r.Flagged.Where(m => valid.Contains(m.OtherSubmissionId) && Guid.TryParse(m.OtherSubmissionId, out _)))
                    db.PlagiarismMatches.Add(new PlagiarismMatche { TenantId = job.TenantId, PlagiarismCheckId = chk.Id, OtherSubmissionId = Guid.Parse(m.OtherSubmissionId), Similarity = m.Similarity, CreatedAtUtc = Now });
                var tracked = await db.Submissions.FirstAsync(x => x.Id == s.Id, ct); tracked.MaxSimilarity = r.MaxSimilarity;
                await db.SaveChangesAsync(ct); checkedCount++; if (r.Flagged.Count > 0) flagged++;
            }
            job.ResultJson = System.Text.Json.JsonSerializer.Serialize(new { submissionsChecked = checkedCount, submissionsWithFlaggedMatches = flagged });
        }
        catch (Exception ex) when (ex is AppException or DomainException) { error = ex.Message.Length > 900 ? ex.Message[..900] : ex.Message; }
        await FinishAsync(job, error, error is null ? $"Similarity check finished: {flagged} submission(s) need review" : "Similarity check failed", ct);
    }
}
