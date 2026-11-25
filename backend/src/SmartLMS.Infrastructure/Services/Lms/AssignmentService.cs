using Microsoft.EntityFrameworkCore;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Domain.Exceptions;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Infrastructure.Services.Lms;

public sealed record RubricItem(string Name, string? Description, double MaxPoints);
public sealed record AssignmentRequest(string Title, string? Instructions, DateTime? DueAtUtc, double? MaxPoints, bool AllowResubmission, int? MaxResubmissions, bool IsGroup, int? MaxGroupSize, bool PeerReviewEnabled, int? PeerReviewsPerSubmission, bool IsPublished, List<RubricItem>? Rubric, bool? PlagiarismCheckEnabled = null, bool? AiProvisionalGradingEnabled = null);
public sealed record GroupRequest(Guid? GroupId, string? Name);
public sealed record SubmitRequest(string? TextContent, string? LinkUrl);
public sealed record ScoreItem(Guid CriterionId, double Points, string? Feedback);
public sealed record GradeSubmissionRequest(List<ScoreItem>? Scores, double? TotalScore, string? Feedback);
public sealed record SubmissionCommentRequest(string Body, string? AnchorJson);
public sealed record ReviewRequest(List<ScoreItem> Scores, string? Comments);
public sealed record ReturnRequest(string? Feedback);

/// <summary>
/// Assignments, teams, versioned submissions, rubric grading, comments, return-for-resubmission and peer review.
/// Not built: file attachments on submissions (text and https link only), plagiarism checks, AI provisional grading, and the "async job"
/// for peer-review assignment (it runs inside the request because Hangfire is not configured).
/// </summary>
public sealed class AssignmentService(ApplicationDbContext db, ICurrentUser me, Notifier notifier, IJobQueue queue)
{
    private static DateTime Now => DateTime.UtcNow;
    private Guid Uid => me.UserId ?? throw new UnauthorizedAppException();
    private bool IsAdmin => me.IsInRole(RoleNames.TenantAdmin) || me.IsInRole(RoleNames.SuperAdmin);
    private Guid Tid => db.CurrentTenantId ?? throw new ValidationAppException("A tenant is required.");

    private async Task<bool> IsStaffAsync(Guid courseId, CancellationToken ct)
    {
        var c = await db.Courses.FirstOrDefaultAsync(x => x.Id == courseId, ct) ?? throw new NotFoundException("Course not found.");
        return await new CourseAccess(db, me).IsGraderAsync(c, ct);
    }
    private async Task<bool> IsAuthorAsync(Guid courseId, CancellationToken ct)
    {
        var c = await db.Courses.FirstOrDefaultAsync(x => x.Id == courseId, ct) ?? throw new NotFoundException("Course not found.");
        return await new CourseAccess(db, me).IsAuthorAsync(c, ct);
    }
    private async Task EnsureCourseReaderAsync(Guid courseId, CancellationToken ct)
    {
        if (await IsStaffAsync(courseId, ct)) return;
        var e = await db.Enrollments.FirstOrDefaultAsync(x => x.CourseId == courseId && x.UserId == Uid, ct);
        if (e is null || e.Status is EnrollmentStatus.Revoked or EnrollmentStatus.Expired || (e.ExpiresAtUtc is { } x && x <= Now)) throw new NotFoundException("Course not found.");
    }
    private async Task<Assignment> StaffAssignmentAsync(Guid id, CancellationToken ct, bool authorOnly = false)
    {
        var a = await db.Assignments.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Assignment not found.");
        if (!(authorOnly ? await IsAuthorAsync(a.CourseId, ct) : await IsStaffAsync(a.CourseId, ct))) throw new NotFoundException("Assignment not found.");
        return a;
    }
    private async Task<Assignment> StudentAssignmentAsync(Guid id, CancellationToken ct)
    {
        var a = await db.Assignments.FirstOrDefaultAsync(x => x.Id == id && x.IsPublished, ct) ?? throw new NotFoundException("Assignment not found.");
        await EnsureCourseReaderAsync(a.CourseId, ct); return a;
    }
    /// <summary>Serialises writers on one assignment: the first statement of the transaction is a write to the assignment row, so a second writer waits for commit.</summary>
    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> LockAsync(Guid assignmentId, CancellationToken ct)
    {
        var tx = await db.Database.BeginTransactionAsync(ct); var when = Now;
        await db.Assignments.Where(x => x.Id == assignmentId).ExecuteUpdateAsync(u => u.SetProperty(x => x.UpdatedAtUtc, (DateTime?)when), ct);
        return tx;
    }
    private static string Need(string? s, int max, string f) { s = s?.Trim() ?? ""; if (s.Length == 0 || s.Length > max) throw new ValidationAppException($"{f} is required (max {max})."); return s; }

    private static void ValidateRequest(AssignmentRequest r)
    {
        Need(r.Title, 300, "Title");
        if (r.Rubric is { } rb) { if (rb.Count > 30 || rb.Any(i => string.IsNullOrWhiteSpace(i.Name) || i.MaxPoints <= 0 || i.MaxPoints > 1000)) throw new ValidationAppException("Each rubric item needs a name and 0 < MaxPoints <= 1000."); }
        else if (r.MaxPoints is not > 0 or > 100000) throw new ValidationAppException("MaxPoints is required when there is no rubric.");
        if (r.IsGroup && r.MaxGroupSize is not (>= 2 and <= 50)) throw new ValidationAppException("MaxGroupSize must be between 2 and 50 for a group assignment.");
        if (r.AllowResubmission && r.MaxResubmissions is < 1 or > 20) throw new ValidationAppException("MaxResubmissions must be between 1 and 20.");
        if (r.PeerReviewEnabled && r.PeerReviewsPerSubmission is not (>= 1 and <= 10)) throw new ValidationAppException("PeerReviewsPerSubmission must be between 1 and 10.");
    }

    public async Task<object> ListAsync(Guid courseId, CancellationToken ct)
    {
        await EnsureCourseReaderAsync(courseId, ct); var staff = await IsStaffAsync(courseId, ct);
        var q = db.Assignments.Where(a => a.CourseId == courseId); if (!staff) q = q.Where(a => a.IsPublished);
        var uid = Uid;
        // The caller's newest submission per assignment (their own, or their team's). Students use it to reopen their work and see grades; it is null when they never submitted.
        return await q.OrderBy(a => a.DueAtUtc).Select(a => new { a.Id, a.Title, a.Instructions, a.DueAtUtc, a.MaxPoints, a.IsGroup, a.MaxGroupSize, a.AllowResubmission, a.PeerReviewEnabled, a.IsPublished,
            rubric = db.AssignmentRubricCriteria.Where(c => c.AssignmentId == a.Id).OrderBy(c => c.SortOrder).Select(c => new { c.Id, c.Name, c.Description, c.MaxPoints }).ToList(),
            mine = db.Submissions.Where(s => s.AssignmentId == a.Id && (s.UserId == uid || s.AssignmentGroupId != null && db.AssignmentGroupMembers.Any(m => m.AssignmentGroupId == s.AssignmentGroupId && m.UserId == uid)))
                .OrderByDescending(s => s.Version).Select(s => new { s.Id, s.Version, Status = s.Status.ToString(), s.TotalScore, s.IsLate, s.SubmittedAtUtc }).FirstOrDefault() }).ToListAsync(ct);
    }
    public async Task<Guid> CreateAsync(Guid courseId, AssignmentRequest r, CancellationToken ct)
    {
        if (!await IsAuthorAsync(courseId, ct)) throw new NotFoundException("Course not found.");
        ValidateRequest(r);
        var a = new Assignment { TenantId = Tid, CourseId = courseId, Title = r.Title.Trim(), Instructions = r.Instructions, DueAtUtc = r.DueAtUtc, MaxPoints = r.Rubric is { Count: > 0 } ? r.Rubric.Sum(i => i.MaxPoints) : r.MaxPoints!.Value,
            AllowResubmission = r.AllowResubmission, MaxResubmissions = r.AllowResubmission ? r.MaxResubmissions : null, IsGroup = r.IsGroup, MaxGroupSize = r.IsGroup ? r.MaxGroupSize : null,
            PeerReviewEnabled = r.PeerReviewEnabled, PeerReviewsPerSubmission = r.PeerReviewEnabled ? r.PeerReviewsPerSubmission : null, IsPublished = r.IsPublished, PlagiarismCheckEnabled = r.PlagiarismCheckEnabled ?? false, AiProvisionalGradingEnabled = r.AiProvisionalGradingEnabled ?? false, CreatedAtUtc = Now, CreatedBy = Uid };
        db.Assignments.Add(a); await db.SaveChangesAsync(ct);
        var i = 0; foreach (var it in r.Rubric ?? []) db.AssignmentRubricCriteria.Add(new AssignmentRubricCriteria { TenantId = a.TenantId, AssignmentId = a.Id, Name = it.Name.Trim(), Description = it.Description, MaxPoints = it.MaxPoints, SortOrder = i++, CreatedAtUtc = Now });
        await db.SaveChangesAsync(ct); return a.Id;
    }
    public async Task UpdateAsync(Guid id, AssignmentRequest r, CancellationToken ct)
    {
        var a = await StaffAssignmentAsync(id, ct, true); ValidateRequest(r);
        // Once anyone has submitted, the shape of grading cannot change under them.
        var locked = await db.Submissions.AnyAsync(s => s.AssignmentId == id, ct);
        if (locked && (r.Rubric is not null || r.IsGroup != a.IsGroup)) throw new ConflictAppException("ASSIGNMENT_LOCKED", "The rubric and team mode cannot change after submissions exist.");
        a.Title = r.Title.Trim(); a.Instructions = r.Instructions; a.DueAtUtc = r.DueAtUtc; a.AllowResubmission = r.AllowResubmission; a.MaxResubmissions = r.AllowResubmission ? r.MaxResubmissions : null;
        a.PeerReviewEnabled = r.PeerReviewEnabled; a.PeerReviewsPerSubmission = r.PeerReviewEnabled ? r.PeerReviewsPerSubmission : null; a.IsPublished = r.IsPublished; a.PlagiarismCheckEnabled = r.PlagiarismCheckEnabled ?? a.PlagiarismCheckEnabled; a.AiProvisionalGradingEnabled = r.AiProvisionalGradingEnabled ?? a.AiProvisionalGradingEnabled; a.UpdatedAtUtc = Now; a.UpdatedBy = Uid;
        if (!locked)
        {
            a.IsGroup = r.IsGroup; a.MaxGroupSize = r.IsGroup ? r.MaxGroupSize : null;
            if (r.Rubric is not null)
            {
                db.AssignmentRubricCriteria.RemoveRange(await db.AssignmentRubricCriteria.Where(c => c.AssignmentId == id).ToListAsync(ct));
                var i = 0; foreach (var it in r.Rubric) db.AssignmentRubricCriteria.Add(new AssignmentRubricCriteria { TenantId = a.TenantId, AssignmentId = id, Name = it.Name.Trim(), Description = it.Description, MaxPoints = it.MaxPoints, SortOrder = i++, CreatedAtUtc = Now });
                a.MaxPoints = r.Rubric.Count > 0 ? r.Rubric.Sum(x => x.MaxPoints) : r.MaxPoints ?? a.MaxPoints;
            }
            else if (r.MaxPoints is > 0) a.MaxPoints = r.MaxPoints.Value;
        }
        await db.SaveChangesAsync(ct);
    }

    // ---- teams -----------------------------------------------------------------------------------------------------------
    public async Task<object> GroupAsync(Guid assignmentId, GroupRequest r, CancellationToken ct)
    {
        var a = await StudentAssignmentAsync(assignmentId, ct);
        if (!a.IsGroup) throw new ConflictAppException("NOT_A_GROUP_ASSIGNMENT", "This assignment is individual.");
        var already = await (from m in db.AssignmentGroupMembers join g in db.AssignmentGroups on m.AssignmentGroupId equals g.Id where g.AssignmentId == assignmentId && m.UserId == Uid select g.Id).FirstOrDefaultAsync(ct);
        if (already != default) return new { groupId = already };
        if (await db.Submissions.AnyAsync(s => s.AssignmentId == assignmentId && s.UserId == Uid, ct)) throw new ConflictAppException("ALREADY_SUBMITTED", "You already submitted individually.");
        Guid gid;
        if (r.GroupId is { } existing)
        {
            var g = await db.AssignmentGroups.FirstOrDefaultAsync(x => x.Id == existing && x.AssignmentId == assignmentId, ct) ?? throw new NotFoundException("Team not found.");
            gid = g.Id;
        }
        else { var g = new AssignmentGroup { TenantId = a.TenantId, AssignmentId = assignmentId, Name = Need(r.Name, 100, "Name"), CreatedAtUtc = Now, CreatedBy = Uid }; db.AssignmentGroups.Add(g); await db.SaveChangesAsync(ct); gid = g.Id; }
        var mem = new AssignmentGroupMember { TenantId = a.TenantId, AssignmentGroupId = gid, UserId = Uid, CreatedAtUtc = Now };
        db.AssignmentGroupMembers.Add(mem);
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { db.Entry(mem).State = EntityState.Detached; }
        // Team size is enforced after the insert so two simultaneous joins cannot both squeeze in: the later rows (by id) are removed.
        var members = await db.AssignmentGroupMembers.Where(m => m.AssignmentGroupId == gid).OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.Id).Select(m => m.UserId).ToListAsync(ct);
        if (members.Count > a.MaxGroupSize)
        {
            if (members.Take(a.MaxGroupSize!.Value).Contains(Uid)) { /* we are within the limit */ }
            else { await db.AssignmentGroupMembers.Where(m => m.AssignmentGroupId == gid && m.UserId == Uid).ExecuteDeleteAsync(ct); throw new ConflictAppException("TEAM_FULL", "This team is full."); }
        }
        return new { groupId = gid };
    }

    // ---- submissions -----------------------------------------------------------------------------------------------------
    private async Task<(Guid? UserId, Guid? GroupId)> OwnerKeyAsync(Assignment a, CancellationToken ct)
    {
        if (!a.IsGroup) return (Uid, null);
        var g = await (from m in db.AssignmentGroupMembers join gr in db.AssignmentGroups on m.AssignmentGroupId equals gr.Id where gr.AssignmentId == a.Id && m.UserId == Uid select gr.Id).FirstOrDefaultAsync(ct);
        if (g == default) throw new ConflictAppException("TEAM_REQUIRED", "Join or create a team first.");
        return (null, g);
    }
    public async Task<object> SubmitAsync(Guid assignmentId, SubmitRequest r, CancellationToken ct)
    {
        var a = await StudentAssignmentAsync(assignmentId, ct);
        if (string.IsNullOrWhiteSpace(r.TextContent) && string.IsNullOrWhiteSpace(r.LinkUrl)) throw new ValidationAppException("Provide text or a link.");
        if (r.TextContent?.Length > 50000) throw new ValidationAppException("TextContent is too long.");
        if (!string.IsNullOrWhiteSpace(r.LinkUrl) && (!Uri.TryCreate(r.LinkUrl, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps)) throw new ValidationAppException("LinkUrl must be an https link.");
        await using var tx = await LockAsync(assignmentId, ct);
        var (uid, gid) = await OwnerKeyAsync(a, ct);
        IQueryable<Submission> mine = db.Submissions.Where(s => s.AssignmentId == assignmentId && (gid != null ? s.AssignmentGroupId == gid : s.UserId == uid && s.AssignmentGroupId == null));
        var last = await mine.OrderByDescending(s => s.Version).FirstOrDefaultAsync(ct);
        var version = 1;
        if (last is not null)
        {
            if (last.Status != SubmissionStatus.ReturnedForResubmission && !(a.AllowResubmission && last.Status == SubmissionStatus.Submitted && last.Version <= (a.MaxResubmissions ?? 0)))
                throw new ConflictAppException("RESUBMISSION_NOT_ALLOWED", "This assignment does not accept another submission now.");
            if (a.AllowResubmission && last.Version > (a.MaxResubmissions ?? 0) && last.Status != SubmissionStatus.ReturnedForResubmission) throw new ConflictAppException("RESUBMISSION_LIMIT", "Resubmission limit reached.");
            version = last.Version + 1;
        }
        var s = new Submission { TenantId = a.TenantId, AssignmentId = assignmentId, UserId = gid is null ? uid : null, AssignmentGroupId = gid, Version = version, TextContent = r.TextContent, LinkUrl = r.LinkUrl, SubmittedAtUtc = Now, IsLate = a.DueAtUtc is { } d && Now > d, Status = SubmissionStatus.Submitted, CreatedAtUtc = Now, CreatedBy = Uid };
        db.Submissions.Add(s); await db.SaveChangesAsync(ct);
        // No unique key on (owner, version): if a parallel request produced the same version, the smaller id survives and this one is withdrawn.
        var dup = await mine.Where(x => x.Version == version).OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(ct);
        if (dup.Count > 1 && dup[0] != s.Id) { db.Submissions.Remove(s); await db.SaveChangesAsync(ct); throw new ConflictAppException("DUPLICATE_SUBMISSION", "Another submission of the same version was received first."); }
        await tx.CommitAsync(ct);
        return new { s.Id, s.Version, s.IsLate };
    }
    public async Task<object> SubmissionsAsync(Guid assignmentId, CancellationToken ct)
    {
        await StaffAssignmentAsync(assignmentId, ct);
        return await db.Submissions.Where(s => s.AssignmentId == assignmentId).OrderByDescending(s => s.SubmittedAtUtc).Take(500)
            .Select(s => new { s.Id, s.UserId, s.AssignmentGroupId, s.Version, s.IsLate, Status = s.Status.ToString(), s.TotalScore, s.SubmittedAtUtc, studentName = db.Users.Where(u => u.Id == s.UserId).Select(u => u.FullName).FirstOrDefault() }).ToListAsync(ct);
    }
    private async Task<(Submission S, Assignment A, bool Staff)> SubmissionAccessAsync(Guid id, CancellationToken ct)
    {
        var s = await db.Submissions.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Submission not found.");
        var a = await db.Assignments.FirstAsync(x => x.Id == s.AssignmentId, ct);
        if (await IsStaffAsync(a.CourseId, ct)) return (s, a, true);
        var own = s.UserId == Uid || (s.AssignmentGroupId is { } g && await db.AssignmentGroupMembers.AnyAsync(m => m.AssignmentGroupId == g && m.UserId == Uid, ct));
        // A peer reviewer may read the work they were assigned, and nothing else.
        var reviewer = await db.PeerReviews.AnyAsync(p => p.SubmissionId == id && p.ReviewerId == Uid, ct);
        if (!own && !reviewer) throw new NotFoundException("Submission not found.");
        return (s, a, false);
    }
    public async Task<object> SubmissionAsync(Guid id, CancellationToken ct)
    {
        var (s, a, staff) = await SubmissionAccessAsync(id, ct);
        var own = staff || s.UserId == Uid || s.AssignmentGroupId != null && await db.AssignmentGroupMembers.AnyAsync(m => m.AssignmentGroupId == s.AssignmentGroupId && m.UserId == Uid, ct);
        var comments = own ? await db.SubmissionComments.Where(c => c.SubmissionId == id).OrderBy(c => c.CreatedAtUtc).Select(c => new { c.Id, c.Body, c.AnchorJson, c.CreatedAtUtc }).ToListAsync(ct) : [];
        var scores = own ? await db.SubmissionCriterionScores.Where(c => c.SubmissionId == id).Select(c => new { c.CriterionId, c.Points, c.Feedback }).ToListAsync(ct) : [];
        // A reviewer sees the work but not whose it is and not the instructor's grade.
        return new { s.Id, s.Version, s.TextContent, s.LinkUrl, s.IsLate, s.SubmittedAtUtc, Status = own ? s.Status.ToString() : null, TotalScore = own ? s.TotalScore : null, Feedback = own ? s.InstructorFeedback : null, author = own ? s.UserId : null, comments, scores, a.MaxPoints };
    }
    public async Task GradeAsync(Guid id, GradeSubmissionRequest r, CancellationToken ct)
    {
        var (s, a, staff) = await SubmissionAccessAsync(id, ct); if (!staff) throw new NotFoundException("Submission not found.");
        var rubric = await db.AssignmentRubricCriteria.Where(c => c.AssignmentId == a.Id).ToListAsync(ct);
        double total;
        if (rubric.Count > 0)
        {
            var scores = r.Scores ?? throw new ValidationAppException("Scores are required for a rubric assignment.");
            if (scores.Select(x => x.CriterionId).Distinct().Count() != scores.Count) throw new ValidationAppException("A criterion can be scored once.");
            foreach (var x in scores) { var c = rubric.FirstOrDefault(c => c.Id == x.CriterionId) ?? throw new ValidationAppException("Unknown criterion."); if (x.Points < 0 || x.Points > c.MaxPoints) throw new ValidationAppException($"Points for '{c.Name}' must be between 0 and {c.MaxPoints}."); }
            total = scores.Sum(x => x.Points);
            db.SubmissionCriterionScores.RemoveRange(await db.SubmissionCriterionScores.Where(x => x.SubmissionId == id).ToListAsync(ct)); await db.SaveChangesAsync(ct);
            foreach (var x in scores) db.SubmissionCriterionScores.Add(new SubmissionCriterionScore { TenantId = s.TenantId, SubmissionId = id, CriterionId = x.CriterionId, Points = x.Points, Feedback = x.Feedback, CreatedAtUtc = Now });
        }
        else { total = r.TotalScore ?? throw new ValidationAppException("TotalScore is required."); if (total < 0 || total > a.MaxPoints) throw new ValidationAppException($"TotalScore must be between 0 and {a.MaxPoints}."); }
        s.TotalScore = total; s.Status = SubmissionStatus.Graded; s.GradedBy = Uid; s.GradedAtUtc = Now; s.InstructorFeedback = r.Feedback;
        await db.SaveChangesAsync(ct);
        var owners = s.UserId is { } su ? [su] : await db.AssignmentGroupMembers.Where(m => m.AssignmentGroupId == s.AssignmentGroupId).Select(m => m.UserId).ToListAsync(ct);
        await notifier.SendAsync(s.TenantId, owners, "Graded", "Your assignment was graded", a.Title, ct);
    }
    public async Task<Guid> CommentAsync(Guid id, SubmissionCommentRequest r, CancellationToken ct)
    {
        var (s, _, staff) = await SubmissionAccessAsync(id, ct); if (!staff) throw new NotFoundException("Submission not found.");
        var c = new SubmissionComment { TenantId = s.TenantId, SubmissionId = id, AuthorId = Uid, Body = Need(r.Body, 4000, "Body"), AnchorJson = r.AnchorJson, CreatedAtUtc = Now };
        db.SubmissionComments.Add(c); await db.SaveChangesAsync(ct); return c.Id;
    }
    public async Task ReturnAsync(Guid id, ReturnRequest r, CancellationToken ct)
    {
        var (s, a, staff) = await SubmissionAccessAsync(id, ct); if (!staff) throw new NotFoundException("Submission not found.");
        if (!a.AllowResubmission) throw new ConflictAppException("RESUBMISSION_DISABLED", "This assignment does not allow resubmission.");
        var n = await db.Submissions.Where(x => x.Id == id && x.Status != SubmissionStatus.ReturnedForResubmission).ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, SubmissionStatus.ReturnedForResubmission).SetProperty(x => x.InstructorFeedback, r.Feedback), ct);
        if (n == 0) throw new ConflictAppException("ALREADY_RETURNED", "Already returned.");
        var owners = s.UserId is { } su ? [su] : await db.AssignmentGroupMembers.Where(m => m.AssignmentGroupId == s.AssignmentGroupId).Select(m => m.UserId).ToListAsync(ct);
        await notifier.SendAsync(s.TenantId, owners, "Returned", "Your assignment was returned for changes", a.Title, ct);
    }

    // ---- peer review -----------------------------------------------------------------------------------------------------
    /// <summary>Queues peer-review assignment as a background job and returns its job id (poll GET /jobs/{id}). With Hangfire off (tests, development) the same work runs inside the request and the job row is already Completed.</summary>
    public async Task<object> AssignPeerReviewsAsync(Guid assignmentId, CancellationToken ct)
    {
        var a = await StaffAssignmentAsync(assignmentId, ct, true);
        if (!a.PeerReviewEnabled) throw new ConflictAppException("PEER_REVIEW_DISABLED", "Peer review is not enabled for this assignment.");
        var open = await db.BackgroundJobs.FirstOrDefaultAsync(j => j.Type == BackgroundJobType.PeerReviewAssign && j.ReferenceId == assignmentId && (j.Status == BackgroundJobStatus.Pending || j.Status == BackgroundJobStatus.Processing), ct);
        if (open is not null) return new { jobId = open.Id, status = open.Status.ToString(), alreadyQueued = true };
        var job = new BackgroundJob { TenantId = a.TenantId, Type = BackgroundJobType.PeerReviewAssign, Status = BackgroundJobStatus.Pending, ReferenceType = "Assignment", ReferenceId = assignmentId, RequestedBy = Uid, CreatedAtUtc = Now };
        db.BackgroundJobs.Add(job); await db.SaveChangesAsync(ct);
        var hf = queue.EnqueuePeerReviewAssignment(job.Id, a.TenantId);
        if (hf is not null) { job.HangfireJobId = hf; await db.SaveChangesAsync(ct); return new { jobId = job.Id, status = "Pending", queued = true }; }
        await RunPeerReviewJobAsync(job.Id, ct);
        var done = await db.BackgroundJobs.AsNoTracking().FirstAsync(j => j.Id == job.Id, ct);
        return new { jobId = job.Id, status = done.Status.ToString(), queued = false, result = done.ResultJson is null ? (System.Text.Json.JsonElement?)null : System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(done.ResultJson) };
    }

    /// <summary>Runs one peer-review job to its end state. Safe to repeat: reviews already created are counted, never duplicated.</summary>
    public async Task RunPeerReviewJobAsync(Guid jobId, CancellationToken ct)
    {
        var job = await db.BackgroundJobs.FirstAsync(j => j.Id == jobId, ct);
        job.Status = BackgroundJobStatus.Processing; job.StartedAtUtc = Now; await db.SaveChangesAsync(ct);
        try
        {
            var result = await AssignCoreAsync(job.ReferenceId!.Value, ct);
            job.Status = BackgroundJobStatus.Completed; job.ProgressPercent = 100; job.ResultJson = System.Text.Json.JsonSerializer.Serialize(result); job.CompletedAtUtc = Now;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.ChangeTracker.Clear(); job = await db.BackgroundJobs.FirstAsync(j => j.Id == jobId, ct);
            job.Status = BackgroundJobStatus.Failed; job.ErrorMessage = ex is AppException ? ex.Message : "Peer-review assignment failed."; job.CompletedAtUtc = Now;
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task<object> AssignCoreAsync(Guid assignmentId, CancellationToken ct)
    {
        var a = await db.Assignments.FirstOrDefaultAsync(x => x.Id == assignmentId, ct) ?? throw new NotFoundException("Assignment not found.");
        if (!a.PeerReviewEnabled) throw new ConflictAppException("PEER_REVIEW_DISABLED", "Peer review is not enabled for this assignment.");
        var latest = (await db.Submissions.Where(s => s.AssignmentId == assignmentId).ToListAsync(ct)).GroupBy(s => (s.UserId, s.AssignmentGroupId)).Select(g => g.OrderByDescending(x => x.Version).First()).OrderBy(s => s.Id).ToList();
        await using var tx = await LockAsync(assignmentId, ct);
        var k = a.PeerReviewsPerSubmission ?? 1; var created = 0;
        if (latest.Count < 2) return new { created, note = "At least two submissions are needed." };
        var groupMembers = await (from m in db.AssignmentGroupMembers join g in db.AssignmentGroups on m.AssignmentGroupId equals g.Id where g.AssignmentId == assignmentId select new { m.AssignmentGroupId, m.UserId }).ToListAsync(ct);
        // Reviewers are the submitting owners (individual users, or the members of a team). Rotate through the list so load is even. Nobody reviews their own or their team's work.
        var reviewers = latest.Where(s => s.UserId != null && s.AssignmentGroupId == null).Select(s => s.UserId!.Value).Concat(groupMembers.Where(m => latest.Any(l => l.AssignmentGroupId == m.AssignmentGroupId)).Select(m => m.UserId)).Distinct().OrderBy(x => x).ToList();
        for (var i = 0; i < latest.Count; i++)
        {
            var sub = latest[i]; var authors = sub.AssignmentGroupId is { } g ? groupMembers.Where(m => m.AssignmentGroupId == g).Select(m => m.UserId).ToHashSet() : new HashSet<Guid> { sub.UserId!.Value };
            var have = await db.PeerReviews.CountAsync(p => p.SubmissionId == sub.Id, ct); var need = k - have;
            for (var j = 1; j <= reviewers.Count && need > 0; j++)
            {
                var rv = reviewers[(i + j) % reviewers.Count]; if (authors.Contains(rv)) continue;
                db.PeerReviews.Add(new PeerReview { TenantId = a.TenantId, SubmissionId = sub.Id, ReviewerId = rv, CreatedAtUtc = Now });
                try { await db.SaveChangesAsync(ct); created++; need--; } catch (DbUpdateException) { db.ChangeTracker.Clear(); }   // unique (submission, reviewer): a repeat call is safe
            }
        }
        await tx.CommitAsync(ct);
        if (created > 0) await notifier.SendAsync(a.TenantId, await db.PeerReviews.Where(p => db.Submissions.Any(s => s.Id == p.SubmissionId && s.AssignmentId == assignmentId) && p.Status != PeerReviewStatus.Submitted).Select(p => p.ReviewerId).Distinct().ToListAsync(ct), "PeerReview", "You have a peer review to write", a.Title, ct);
        return new { created };
    }
    public async Task<object> MyReviewsAsync(CancellationToken ct) =>
        await (from p in db.PeerReviews join s in db.Submissions on p.SubmissionId equals s.Id join a in db.Assignments on s.AssignmentId equals a.Id where p.ReviewerId == Uid
               select new { p.Id, p.SubmissionId, Status = p.Status.ToString(), assignmentTitle = a.Title, a.MaxPoints, rubric = db.AssignmentRubricCriteria.Where(c => c.AssignmentId == a.Id).Select(c => new { c.Id, c.Name, c.MaxPoints }).ToList() }).ToListAsync(ct);
    public async Task SubmitReviewAsync(Guid id, ReviewRequest r, CancellationToken ct)
    {
        var p = await db.PeerReviews.FirstOrDefaultAsync(x => x.Id == id && x.ReviewerId == Uid, ct) ?? throw new NotFoundException("Review not found.");
        if (p.Status == PeerReviewStatus.Submitted) throw new ConflictAppException("ALREADY_SUBMITTED", "This review was already submitted.");
        var s = await db.Submissions.FirstAsync(x => x.Id == p.SubmissionId, ct);
        var rubric = await db.AssignmentRubricCriteria.Where(c => c.AssignmentId == s.AssignmentId).ToListAsync(ct);
        if (r.Scores is null || r.Scores.Select(x => x.CriterionId).Distinct().Count() != r.Scores.Count) throw new ValidationAppException("Scores are required, one per criterion.");
        foreach (var x in r.Scores) { var c = rubric.FirstOrDefault(c => c.Id == x.CriterionId) ?? throw new ValidationAppException("Unknown criterion."); if (x.Points < 0 || x.Points > c.MaxPoints) throw new ValidationAppException("Points out of range."); }
        // Claim the review first so a double submit cannot write scores twice.
        var total = r.Scores.Sum(x => x.Points); var when = Now;
        var n = await db.PeerReviews.Where(x => x.Id == id && x.Status == PeerReviewStatus.Assigned).ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, PeerReviewStatus.Submitted).SetProperty(x => x.TotalScore, (double?)total).SetProperty(x => x.Comments, r.Comments).SetProperty(x => x.SubmittedAtUtc, (DateTime?)when), ct);
        if (n == 0) throw new ConflictAppException("ALREADY_SUBMITTED", "This review was already submitted.");
        foreach (var x in r.Scores) db.PeerReviewScores.Add(new PeerReviewScore { TenantId = p.TenantId, PeerReviewId = id, CriterionId = x.CriterionId, Points = x.Points, Comment = x.Feedback, CreatedAtUtc = Now });
        await db.SaveChangesAsync(ct);
    }
}
