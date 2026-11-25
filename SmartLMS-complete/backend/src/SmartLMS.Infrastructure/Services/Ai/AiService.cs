using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Domain.Exceptions;
using SmartLMS.Infrastructure.Persistence;
using SmartLMS.Infrastructure.Services.Commerce;
using SmartLMS.Infrastructure.Services.Lms;

namespace SmartLMS.Infrastructure.Services.Ai;

public sealed record TutorRequest(string Question, string? Language);
public sealed record SearchRequest(string Query, int? TopK);
public sealed record QuizRequest(int? McqCount, int? OpenEndedCount, int? FlashcardCount, string? Difficulty);
public sealed record StudyPlanBody(DateTime TargetDate, double HoursPerWeek);

/// <summary>
/// The .NET side of the AI features. Every call checks, in this order: the caller's role (controller), tenant (query filters), course access
/// (enrolled student or course staff), the per-user rate limit, and then forwards to the Python service. Prompt text and answers are never stored:
/// AiUsageLogs keeps endpoint, course, latency and status only; AuditLogs keeps who did what to which object.
/// </summary>
public sealed class AiService(ApplicationDbContext db, ICurrentUser me, IAiServiceClient ai, IJobQueue queue, AuditWriter audit, IConfiguration cfg, AiJobService jobs)
{
    private static DateTime Now => DateTime.UtcNow;
    private Guid Uid => me.UserId ?? throw new UnauthorizedAppException();
    private Guid Tid => db.CurrentTenantId ?? throw new ValidationAppException("A tenant is required.");
    private CourseAccess Access => new(db, me);

    // ---- access -----------------------------------------------------------------------------------------------------
    private async Task<Course> CourseAsync(Guid id, CancellationToken ct) => await db.Courses.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Course not found.");
    /// <summary>Student: actively enrolled. Staff: author or grader of the course. Anyone else gets 404, not 403.</summary>
    private async Task<Course> ReaderAsync(Guid courseId, CancellationToken ct)
    {
        var c = await CourseAsync(courseId, ct);
        if (await Access.IsGraderAsync(c, ct)) return c;
        var e = await db.Enrollments.FirstOrDefaultAsync(x => x.CourseId == courseId && x.UserId == Uid, ct);
        if (e is null || e.Status is EnrollmentStatus.Revoked or EnrollmentStatus.Expired || (e.ExpiresAtUtc is { } x && x <= Now)) throw new NotFoundException("Course not found.");
        return c;
    }
    private async Task<(Lesson L, Course C)> LessonAsync(Guid lessonId, bool author, bool grader, CancellationToken ct)
    {
        var l = await db.Lessons.FirstOrDefaultAsync(x => x.Id == lessonId && !x.IsDeleted, ct) ?? throw new NotFoundException("Lesson not found.");
        var c = await CourseAsync(l.CourseId, ct);
        var ok = author ? await Access.IsAuthorAsync(c, ct) : grader ? await Access.IsGraderAsync(c, ct) : await ReaderOk(c, ct);
        if (!ok) throw new NotFoundException("Lesson not found.");
        if (!author && !grader && !l.IsPublished && !await Access.IsGraderAsync(c, ct)) throw new NotFoundException("Lesson not found.");
        return (l, c);
    }
    private async Task<bool> ReaderOk(Course c, CancellationToken ct) { try { await ReaderAsync(c.Id, ct); return true; } catch (NotFoundException) { return false; } }
    private static AiScope Scope(Guid tenant, Guid course) => new(tenant.ToString(), course.ToString());

    // ---- rate limit, usage, audit ---------------------------------------------------------------------------------------
    private async Task<T> CallAsync<T>(string endpoint, Guid courseId, Func<Task<T>> call, CancellationToken ct)
    {
        var limit = cfg.GetValue("AiService:RequestsPerMinute", 20); var since = Now.AddMinutes(-1);
        if (await db.AiUsageLogs.CountAsync(x => x.UserId == Uid && x.CreatedAtUtc >= since, ct) >= limit)
            throw new AppException(429, "AI_RATE_LIMITED", "Too many AI requests. Wait a minute and try again.");
        var sw = Stopwatch.StartNew(); var status = 200;
        try { return await call(); }
        catch (AppException ex) { status = ex.StatusCode; throw; }
        catch (Exception) { status = 500; throw; }
        finally
        {
            db.AiUsageLogs.Add(new AiUsageLog { TenantId = Tid, UserId = Uid, Endpoint = endpoint, CourseId = courseId, Provider = cfg["AiService:ProviderLabel"], LatencyMs = (int)sw.ElapsedMilliseconds, StatusCode = status, CreatedAtUtc = Now });
            try { await db.SaveChangesAsync(CancellationToken.None); } catch (DbUpdateException) { }
        }
    }

    // ---- ingest ---------------------------------------------------------------------------------------------------------
    public async Task<object> QueueIngestAsync(Guid lessonId, CancellationToken ct)
    {
        var (l, c) = await LessonAsync(lessonId, author: true, grader: false, ct);
        await jobs.SourceForAsync(l, ct);   // fails with 422 now when the lesson has nothing to index
        var state = await db.LessonIndexStates.FirstOrDefaultAsync(s => s.LessonId == lessonId, ct);
        if (state is { Status: LessonIndexStateStatus.Pending or LessonIndexStateStatus.Processing }) throw new ConflictAppException("AI_INGEST_RUNNING", "This lesson is already being indexed.");
        if (state is null) { state = new LessonIndexState { TenantId = Tid, LessonId = lessonId, CreatedAtUtc = Now }; db.LessonIndexStates.Add(state); }
        state.Status = LessonIndexStateStatus.Pending; state.LastError = null;
        var job = new BackgroundJob { TenantId = Tid, Type = BackgroundJobType.AiIngest, Status = BackgroundJobStatus.Pending, ReferenceType = "Lesson", ReferenceId = lessonId, RequestedBy = Uid, CreatedAtUtc = Now };
        db.BackgroundJobs.Add(job); await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditLogAction.Generate, "AiIngest", lessonId, new { courseId = c.Id, jobId = job.Id }, ct);
        var hf = queue.Enqueue(job.Id, Tid);
        if (hf is null) await jobs.RunIngestAsync(job.Id, ct); else { job.HangfireJobId = hf; await db.SaveChangesAsync(ct); }
        var done = await db.BackgroundJobs.AsNoTracking().FirstAsync(j => j.Id == job.Id, ct);
        return new { jobId = job.Id, status = done.Status.ToString(), queued = hf is not null };
    }

    public async Task<object> IndexStatusAsync(Guid lessonId, CancellationToken ct)
    {
        await LessonAsync(lessonId, author: true, grader: false, ct);
        var s = await db.LessonIndexStates.AsNoTracking().FirstOrDefaultAsync(x => x.LessonId == lessonId, ct);
        var job = await db.BackgroundJobs.AsNoTracking().Where(j => j.Type == BackgroundJobType.AiIngest && j.ReferenceId == lessonId).OrderByDescending(j => j.CreatedAtUtc).Select(j => new { j.Id, Status = j.Status.ToString(), j.ProgressPercent, j.ErrorMessage }).FirstOrDefaultAsync(ct);
        return new { status = (s?.Status ?? LessonIndexStateStatus.NotIndexed).ToString(), s?.ChunkCount, s?.IndexedAtUtc, s?.LastError, job };
    }

    // ---- learning features -------------------------------------------------------------------------------------------------
    public async Task<AiTutorResult> TutorAsync(Guid courseId, TutorRequest r, CancellationToken ct)
    {
        Require(r.Question, 4000); await ReaderAsync(courseId, ct);
        return await CallAsync("tutor/chat", courseId, () => ai.TutorChatAsync(Scope(Tid, courseId), r.Question, r.Language, ct), ct);
    }
    public async Task<AiSearchResult> SearchAsync(Guid courseId, SearchRequest r, CancellationToken ct)
    {
        Require(r.Query, 500); await ReaderAsync(courseId, ct);
        var res = await CallAsync("video/semantic-search", courseId, () => ai.SemanticSearchAsync(Scope(Tid, courseId), r.Query, Math.Clamp(r.TopK ?? 5, 1, 20), ct), ct);
        // The AI index can lag behind: lessons that are unpublished or deleted now never reach a student.
        var visible = await ReaderIsStaff(courseId, ct) ? null : await db.Lessons.Where(l => l.CourseId == courseId && l.IsPublished && !l.IsDeleted).Select(l => l.Id.ToString()).ToListAsync(ct);
        return visible is null ? res : res with { Hits = res.Hits.Where(h => visible.Contains(h.LessonId)).ToList() };
    }
    private async Task<bool> ReaderIsStaff(Guid courseId, CancellationToken ct) => await Access.IsGraderAsync(await CourseAsync(courseId, ct), ct);

    public async Task<object> QuizAsync(Guid lessonId, QuizRequest r, CancellationToken ct)
    {
        var (l, _) = await LessonAsync(lessonId, author: false, grader: true, ct);
        var res = await CallAsync("generate-quiz", l.CourseId, () => ai.GenerateQuizAsync(Scope(Tid, l.CourseId), lessonId.ToString(), Math.Clamp(r.McqCount ?? 5, 0, 30), Math.Clamp(r.OpenEndedCount ?? 2, 0, 10), Math.Clamp(r.FlashcardCount ?? 5, 0, 30), Valid(r.Difficulty), ct), ct);
        await audit.WriteAsync(AuditLogAction.Generate, "AiQuizDraft", lessonId, new { mcq = res.Mcqs.Count, open = res.OpenEnded.Count, cards = res.Flashcards.Count }, ct);
        return new { draft = true, notice = "AI-generated draft. Review every question before using it; nothing was saved to the question bank.", result = res };
    }
    private static string Valid(string? d) => d?.ToLowerInvariant() is "easy" or "medium" or "hard" ? d.ToLowerInvariant() : d is null ? "medium" : throw new ValidationAppException("Difficulty must be easy, medium or hard.");
    private static void Require(string? s, int max) { if (string.IsNullOrWhiteSpace(s) || s.Length > max) throw new ValidationAppException($"Text is required and at most {max} characters."); }

    public async Task<AiSummaryResult> SummarizeAsync(Guid lessonId, CancellationToken ct)
    { var (l, _) = await LessonAsync(lessonId, false, false, ct); return await CallAsync("summarize", l.CourseId, () => ai.SummarizeAsync(Scope(Tid, l.CourseId), lessonId.ToString(), ct), ct); }
    public async Task<AiMindMapResult> MindMapAsync(Guid lessonId, CancellationToken ct)
    { var (l, _) = await LessonAsync(lessonId, false, false, ct); return await CallAsync("mindmap", l.CourseId, () => ai.MindMapAsync(Scope(Tid, l.CourseId), lessonId.ToString(), ct), ct); }

    public async Task<AiVoiceResult> VoiceAsync(Guid courseId, Stream audio, string fileName, string? language, CancellationToken ct)
    {
        await ReaderAsync(courseId, ct);
        return await CallAsync("voice-tutor", courseId, () => ai.VoiceTutorAsync(Scope(Tid, courseId), audio, fileName, language, ct), ct);
    }

    public async Task<AiStudyPlanResult> StudyPlanAsync(Guid courseId, StudyPlanBody r, CancellationToken ct)
    {
        await ReaderAsync(courseId, ct);
        var start = Now.Date; var target = r.TargetDate.Date;
        if (target < start || (target - start).TotalDays > 366) throw new ValidationAppException("The target date must be today or later, within a year.");
        if (r.HoursPerWeek <= 0 || r.HoursPerWeek > 112) throw new ValidationAppException("Weekly hours must be between 0 and 112.");
        var done = await db.LessonProgress.Where(p => p.UserId == Uid && p.CourseId == courseId && p.Status == LessonProgressStatus.Completed).Select(p => p.LessonId).ToListAsync(ct);
        var lessons = await db.Lessons.AsNoTracking().Where(l => l.CourseId == courseId && l.IsPublished && !l.IsDeleted && !done.Contains(l.Id)).OrderBy(l => l.SectionId).ThenBy(l => l.SortOrder).Select(l => new { l.Title, l.DurationSeconds }).ToListAsync(ct);
        if (lessons.Count == 0) throw new ConflictAppException("NOTHING_TO_PLAN", "There are no unfinished lessons in this course.");
        // Lessons without a stored duration count as 15 minutes, and the plan says so.
        var items = lessons.Select(l => new AiStudyLesson(l.Title, (l.DurationSeconds ?? 900) / 3600.0)).ToList();
        var guessed = lessons.Count(l => l.DurationSeconds is null);
        var res = await CallAsync("study-plan", courseId, () => ai.StudyPlanAsync(Scope(Tid, courseId), items, items.Sum(i => i.Hours), r.HoursPerWeek, start.ToString("yyyy-MM-dd"), target.ToString("yyyy-MM-dd"), ct), ct);
        return guessed == 0 ? res : res with { Note = (res.Note is null ? "" : res.Note + " ") + $"{guessed} lesson(s) have no stored duration and were counted as 15 minutes each." };
    }

    // ---- grading and plagiarism -----------------------------------------------------------------------------------------
    public async Task<object> GradeEssayAsync(Guid submissionId, CancellationToken ct)
    {
        var s = await db.Submissions.FirstOrDefaultAsync(x => x.Id == submissionId, ct) ?? throw new NotFoundException("Submission not found.");
        var a = await db.Assignments.FirstAsync(x => x.Id == s.AssignmentId, ct);
        if (!await Access.IsGraderAsync(await CourseAsync(a.CourseId, ct), ct)) throw new NotFoundException("Submission not found.");
        if (!a.AiProvisionalGradingEnabled) throw new ConflictAppException("AI_GRADING_DISABLED", "AI provisional grading is not enabled for this assignment.");
        if (string.IsNullOrWhiteSpace(s.TextContent)) throw new ValidationAppException("This submission has no text to grade.");
        var rubric = await db.AssignmentRubricCriteria.AsNoTracking().Where(c => c.AssignmentId == a.Id).OrderBy(c => c.SortOrder).ToListAsync(ct);
        if (rubric.Count == 0) throw new ConflictAppException("RUBRIC_REQUIRED", "Add a rubric to the assignment first.");
        var res = await CallAsync("grade-essay", a.CourseId, () => ai.GradeEssayAsync(Scope(Tid, a.CourseId), a.Id.ToString(), s.TextContent!, rubric.Select(c => new AiRubricCriterion(c.Name, c.MaxPoints, c.Description ?? "")).ToList(), ct), ct);
        // Only the provisional columns are written. TotalScore, Status and the criterion scores stay as the instructor left them.
        s.AiProvisionalScore = res.TotalPoints; s.AiFeedback = res.Feedback; await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditLogAction.Generate, "AiProvisionalGrade", submissionId, new { score = res.TotalPoints, max = res.MaxPoints }, ct);
        return new { provisional = true, notice = "Provisional AI suggestion. It is not the grade; an instructor must grade the submission.", result = res };
    }

    public async Task<object> QueuePlagiarismAsync(Guid assignmentId, CancellationToken ct)
    {
        var a = await db.Assignments.FirstOrDefaultAsync(x => x.Id == assignmentId, ct) ?? throw new NotFoundException("Assignment not found.");
        if (!await Access.IsAuthorAsync(await CourseAsync(a.CourseId, ct), ct)) throw new NotFoundException("Assignment not found.");
        if (!a.PlagiarismCheckEnabled) throw new ConflictAppException("PLAGIARISM_DISABLED", "Plagiarism checking is not enabled for this assignment.");
        if (await db.BackgroundJobs.AnyAsync(j => j.Type == BackgroundJobType.PlagiarismBatch && j.ReferenceId == assignmentId && (j.Status == BackgroundJobStatus.Pending || j.Status == BackgroundJobStatus.Processing), ct))
            throw new ConflictAppException("PLAGIARISM_RUNNING", "A check is already running for this assignment.");
        var job = new BackgroundJob { TenantId = Tid, Type = BackgroundJobType.PlagiarismBatch, Status = BackgroundJobStatus.Pending, ReferenceType = "Assignment", ReferenceId = assignmentId, RequestedBy = Uid, CreatedAtUtc = Now };
        db.BackgroundJobs.Add(job); await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditLogAction.Generate, "AiPlagiarismBatch", assignmentId, new { jobId = job.Id }, ct);
        var hf = queue.Enqueue(job.Id, Tid);
        if (hf is null) await jobs.RunPlagiarismAsync(job.Id, ct); else { job.HangfireJobId = hf; await db.SaveChangesAsync(ct); }
        var done = await db.BackgroundJobs.AsNoTracking().FirstAsync(j => j.Id == job.Id, ct);
        return new { jobId = job.Id, status = done.Status.ToString(), queued = hf is not null };
    }

    public async Task<object> PlagiarismResultAsync(Guid submissionId, CancellationToken ct)
    {
        var s = await db.Submissions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == submissionId, ct) ?? throw new NotFoundException("Submission not found.");
        var a = await db.Assignments.AsNoTracking().FirstAsync(x => x.Id == s.AssignmentId, ct);
        if (!await Access.IsAuthorAsync(await CourseAsync(a.CourseId, ct), ct)) throw new NotFoundException("Submission not found.");
        var chk = await db.PlagiarismChecks.AsNoTracking().Where(c => c.SubmissionId == submissionId).OrderByDescending(c => c.CreatedAtUtc).FirstOrDefaultAsync(ct);
        if (chk is null) return new { checkedAlready = false, notice = "No check has run for this submission." };
        var matches = await db.PlagiarismMatches.AsNoTracking().Where(m => m.PlagiarismCheckId == chk.Id).OrderByDescending(m => m.Similarity).Select(m => new { m.OtherSubmissionId, m.Similarity, Outcome = m.ReviewOutcome.ToString() }).ToListAsync(ct);
        return new { checkedAlready = true, status = chk.Status.ToString(), chk.MaxSimilarity, chk.CheckedAtUtc, matches, notice = "A similarity score is not proof of plagiarism. A person must review each match." };
    }

    // ---- risk -----------------------------------------------------------------------------------------------------------
    public async Task<object> RiskAsync(Guid studentId, Guid courseId, CancellationToken ct)
    {
        var c = await CourseAsync(courseId, ct);
        if (!await Access.IsAuthorAsync(c, ct)) throw new NotFoundException("Course not found.");
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == studentId && u.TenantId == Tid, ct);
        var enr = await db.Enrollments.AsNoTracking().FirstOrDefaultAsync(e => e.CourseId == courseId && e.UserId == studentId, ct);
        if (user is null || enr is null) throw new NotFoundException("Student not found in this course.");
        var total = await db.Lessons.CountAsync(l => l.CourseId == courseId && l.IsPublished && !l.IsDeleted, ct);
        var completed = await db.LessonProgress.CountAsync(p => p.UserId == studentId && p.CourseId == courseId && p.Status == LessonProgressStatus.Completed, ct);
        var weekAgo = Now.AddDays(-7);
        var watched = await db.LessonProgress.Where(p => p.UserId == studentId && p.CourseId == courseId && p.LastAccessedAtUtc >= weekAgo).SumAsync(p => (int?)p.WatchedSeconds, ct) ?? 0;
        var scores = await (from at in db.ExamAttempts join ex in db.Exams on at.ExamId equals ex.Id
                            where at.UserId == studentId && ex.CourseId == courseId && at.Score != null && at.MaxScore != null && at.MaxScore > 0 select at.Score!.Value / at.MaxScore!.Value * 100).ToListAsync(ct);
        var due = await db.Assignments.Where(a => a.CourseId == courseId && a.IsPublished && a.DueAtUtc != null && a.DueAtUtc < Now).Select(a => a.Id).ToListAsync(ct);
        var submitted = await db.Submissions.Where(s => s.UserId == studentId && due.Contains(s.AssignmentId)).Select(s => s.AssignmentId).Distinct().CountAsync(ct);
        var f = new AiRiskFeatures(user.LastLoginAtUtc is { } ll ? Math.Max(0, (int)(Now - ll).TotalDays) : 30, total == 0 ? 0 : Math.Min(1.0, completed / (double)total), scores.Count == 0 ? null : Math.Round(scores.Average(), 1), Math.Round(watched / 60.0, 1), due.Count - submitted);
        var res = await CallAsync("predict-risk", courseId, () => ai.PredictRiskAsync(Scope(Tid, courseId), studentId.ToString(), f, ct), ct);
        db.DropoutRiskScores.Add(new DropoutRiskScore { TenantId = Tid, UserId = studentId, CourseId = courseId, Score = res.RiskScore, Level = Enum.Parse<DropoutRiskScoreLevel>(res.Level, true), FeaturesJson = System.Text.Json.JsonSerializer.Serialize(f), ComputedAtUtc = Now });
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditLogAction.Generate, "AiRiskScore", studentId, new { courseId, level = res.Level }, ct);
        return new { advisory = true, features = f, result = res };
    }
}
