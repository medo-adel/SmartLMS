using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Domain.Exceptions;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Infrastructure.Services.Lms;

public sealed record BankRequest(Guid CourseId, string Name, string? Topic);
public sealed record OptionRequest(string Text, bool IsCorrect, string? MatchKey);
public sealed record QuestionRequest(string QuestionType, string Text, double Points, string? Difficulty, string? Topic, string? Explanation, bool? TrueFalseAnswer, List<OptionRequest>? Options);
public sealed record ExamRequest(string Title, string? Kind, string? Instructions, int? DurationMinutes, int MaxAttempts, double PassingScorePercent, List<Guid>? QuestionIds, Guid? PickFromBankId, int? PickCount,
    DateTime? AvailableFromUtc, DateTime? AvailableUntilUtc, bool TabSwitchDetection, int? MaxTabSwitches);
public sealed record AnswerItem(Guid AttemptQuestionId, JsonElement Answer);
public sealed record AnswersRequest(List<AnswerItem> Answers);
public sealed record AttemptEventRequest(string EventType);
public sealed record GradeRequest(double Points, string? Feedback);

/// <summary>
/// Question banks, exams and attempts. Rules enforced on the server: only the course owner (or TenantAdmin) manages content;
/// students need a live enrollment; the question set is frozen at attempt start; the timer runs on the server clock; correct answers and
/// explanations are never sent while an attempt is open; the attempt limit is protected by the (ExamId, UserId, AttemptNumber) unique index.
/// Not built yet: CSV/JSON import, rule-based random pools beyond "pick N from a bank", option shuffling, assignments and peer review.
/// </summary>
public sealed class AssessmentService(ApplicationDbContext db, ICurrentUser me, Notifier notifier)
{
    private const int GraceSeconds = 15;
    private static DateTime Now => DateTime.UtcNow;
    private Guid Uid => me.UserId ?? throw new UnauthorizedAppException();
    private bool IsAdmin => me.IsInRole(RoleNames.TenantAdmin) || me.IsInRole(RoleNames.SuperAdmin);
    private static T ParseEnum<T>(string? v, T fallback) where T : struct, Enum => v is null ? fallback : Enum.TryParse<T>(v, true, out var r) ? r : throw new ValidationAppException($"Unknown {typeof(T).Name}: {v}");
    private Guid Tid => db.CurrentTenantId ?? throw new ValidationAppException("A tenant is required.");

    private CourseAccess acc => new(db, me);
    private async Task<Course> OwnedCourseAsync(Guid id, CancellationToken ct)
    {
        var c = await db.Courses.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null || !await acc.IsAuthorAsync(c, ct)) throw new NotFoundException("Course not found.");
        return c;
    }
    private async Task<Course> GraderCourseAsync(Guid id, CancellationToken ct)
    {
        var c = await db.Courses.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null || !await acc.IsGraderAsync(c, ct)) throw new NotFoundException("Course not found.");
        return c;
    }

    // ---- authoring -----------------------------------------------------------------------------------------------------
    public async Task<Guid> CreateBankAsync(BankRequest r, CancellationToken ct)
    {
        await OwnedCourseAsync(r.CourseId, ct);
        var b = new QuestionBank { TenantId = Tid, CourseId = r.CourseId, Name = r.Name.Trim(), Topic = r.Topic };
        db.QuestionBanks.Add(b); await db.SaveChangesAsync(ct); return b.Id;
    }
    private async Task<QuestionBank> OwnedBankAsync(Guid id, CancellationToken ct)
    {
        var b = await db.QuestionBanks.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Question bank not found.");
        if (b.CourseId is null) { if (!IsAdmin) throw new NotFoundException("Question bank not found."); }
        else await OwnedCourseAsync(b.CourseId.Value, ct);
        return b;
    }

    public async Task<Guid> AddQuestionAsync(Guid bankId, QuestionRequest r, CancellationToken ct)
    {
        var bank = await OwnedBankAsync(bankId, ct);
        var (type, opts) = ValidateQuestion(r);
        var q = new Question { TenantId = Tid, QuestionBankId = bank.Id, QuestionType = type, Text = r.Text.Trim(), DefaultPoints = r.Points, Difficulty = ParseEnum(r.Difficulty, QuestionDifficulty.Medium), Topic = r.Topic, Explanation = r.Explanation };
        db.Questions.Add(q);
        AddOptions(q, type, r, opts);
        await db.SaveChangesAsync(ct); return q.Id;
    }
    private void AddOptions(Question q, QuestionType type, QuestionRequest r, List<OptionRequest> opts)
    {
        if (type == QuestionType.TrueFalse) opts = [new("True", r.TrueFalseAnswer!.Value, null), new("False", !r.TrueFalseAnswer.Value, null)];
        if (type is QuestionType.ShortAnswer or QuestionType.Essay) opts = [];
        var i = 0;
        foreach (var o in opts) db.QuestionOptions.Add(new QuestionOption { TenantId = Tid, QuestionId = q.Id, Text = o.Text, IsCorrect = type == QuestionType.Matching || o.IsCorrect, MatchKey = o.MatchKey, SortOrder = i++ });
    }
    private (QuestionType Type, List<OptionRequest> Opts) ValidateQuestion(QuestionRequest r)
    {
        var type = ParseEnum<QuestionType>(r.QuestionType, QuestionType.McqSingle);
        var opts = r.Options ?? [];
        switch (type)
        {
            case QuestionType.McqSingle when opts.Count < 2 || opts.Count(o => o.IsCorrect) != 1: throw new ValidationAppException("A single-answer question needs at least 2 options and exactly 1 correct option.");
            case QuestionType.McqMulti when opts.Count < 2 || !opts.Any(o => o.IsCorrect): throw new ValidationAppException("A multi-answer question needs at least 2 options and 1 or more correct options.");
            case QuestionType.FillBlank when !opts.Any(o => o.IsCorrect && !string.IsNullOrWhiteSpace(o.Text)): throw new ValidationAppException("A fill-in-the-blank question needs at least one accepted answer (an option marked correct).");
            case QuestionType.Matching when opts.Count < 2 || opts.Any(o => string.IsNullOrWhiteSpace(o.MatchKey)): throw new ValidationAppException("A matching question needs 2 or more pairs, each with text and matchKey.");
            case QuestionType.TrueFalse when r.TrueFalseAnswer is null: throw new ValidationAppException("trueFalseAnswer is required.");
        }
        if (r.Points <= 0 || r.Points > 1000) throw new ValidationAppException("Points must be between 0 and 1000.");
        return (type, opts);
    }

    public async Task<Guid> CreateExamAsync(Guid courseId, ExamRequest r, CancellationToken ct)
    {
        await OwnedCourseAsync(courseId, ct);
        if (r.MaxAttempts is < 1 or > 20) throw new ValidationAppException("MaxAttempts must be 1 to 20.");
        if (r.PassingScorePercent is < 0 or > 100) throw new ValidationAppException("PassingScorePercent must be 0 to 100.");
        if (r.DurationMinutes is <= 0) throw new ValidationAppException("DurationMinutes must be positive.");
        var e = new Exam { TenantId = Tid, CourseId = courseId, Title = r.Title.Trim(), Kind = ParseEnum(r.Kind, ExamKind.Quiz), Instructions = r.Instructions, DurationMinutes = r.DurationMinutes, MaxAttempts = r.MaxAttempts,
            PassingScorePercent = r.PassingScorePercent, AvailableFromUtc = r.AvailableFromUtc, AvailableUntilUtc = r.AvailableUntilUtc, TabSwitchDetection = r.TabSwitchDetection, MaxTabSwitches = r.MaxTabSwitches };
        db.Exams.Add(e);
        var n = 0;
        foreach (var qid in r.QuestionIds ?? [])
        {
            // A question must live in a bank of THIS course, so an instructor cannot pull another course's questions into an exam.
            var ok = await (from q in db.Questions join b in db.QuestionBanks on q.QuestionBankId equals b.Id where q.Id == qid && b.CourseId == courseId select q.Id).AnyAsync(ct);
            if (!ok) throw new NotFoundException("Question not found in this course.");
            db.ExamQuestionRules.Add(new ExamQuestionRule { TenantId = Tid, ExamId = e.Id, QuestionId = qid, SortOrder = n++ });
        }
        if (r.PickFromBankId is { } bid)
        {
            if (!await db.QuestionBanks.AnyAsync(b => b.Id == bid && b.CourseId == courseId, ct)) throw new NotFoundException("Question bank not found in this course.");
            db.ExamQuestionRules.Add(new ExamQuestionRule { TenantId = Tid, ExamId = e.Id, QuestionBankId = bid, PickCount = Math.Max(1, r.PickCount ?? 1), SortOrder = n++ });
        }
        if (n == 0) throw new ValidationAppException("An exam needs questionIds or pickFromBankId.");
        await db.SaveChangesAsync(ct); return e.Id;
    }

    private async Task<Exam> GraderExamAsync(Guid id, CancellationToken ct)
    {
        var e = await db.Exams.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Exam not found.");
        await GraderCourseAsync(e.CourseId, ct); return e;
    }
    private async Task<Exam> OwnedExamAsync(Guid id, CancellationToken ct)
    {
        var e = await db.Exams.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Exam not found.");
        await OwnedCourseAsync(e.CourseId, ct); return e;
    }
    public async Task PublishAsync(Guid id, CancellationToken ct)
    {
        var e = await OwnedExamAsync(id, ct);
        var pool = await PoolSizeAsync(e.Id, ct);
        if (pool == 0) throw new ValidationAppException("The exam has no questions.");
        e.IsPublished = true; await db.SaveChangesAsync(ct);
    }
    private async Task<int> PoolSizeAsync(Guid examId, CancellationToken ct)
    {
        var rules = await db.ExamQuestionRules.Where(x => x.ExamId == examId).ToListAsync(ct);
        var n = rules.Count(x => x.QuestionId != null);
        foreach (var r in rules.Where(x => x.QuestionBankId != null)) n += Math.Min(r.PickCount ?? 1, await db.Questions.CountAsync(q => q.QuestionBankId == r.QuestionBankId, ct));
        return n;
    }

    public async Task<object> ListExamsAsync(Guid courseId, CancellationToken ct)
    {
        var c = await db.Courses.FirstOrDefaultAsync(x => x.Id == courseId, ct) ?? throw new NotFoundException("Course not found.");
        var staff = await acc.IsGraderAsync(c, ct);
        if (!staff)
        {
            if (me.IsInRole(RoleNames.Instructor) || me.IsInRole(RoleNames.TeachingAssistant)) throw new NotFoundException("Course not found.");
            await RequireEnrollmentAsync(courseId, ct);
        }
        return await db.Exams.Where(e => e.CourseId == courseId && (staff || e.IsPublished))
            .Select(e => new { e.Id, e.Title, Kind = e.Kind.ToString(), e.DurationMinutes, e.MaxAttempts, e.PassingScorePercent, e.IsPublished, e.AvailableFromUtc, e.AvailableUntilUtc }).ToListAsync(ct);
    }

    private async Task<Enrollment> RequireEnrollmentAsync(Guid courseId, CancellationToken ct)
    {
        var e = await db.Enrollments.FirstOrDefaultAsync(x => x.CourseId == courseId && x.UserId == Uid, ct);
        if (e is null || e.Status is EnrollmentStatus.Revoked or EnrollmentStatus.Expired || (e.ExpiresAtUtc is { } x && x <= Now)) throw new ForbiddenAppException("NOT_ENROLLED", "You are not enrolled in this course.");
        return e;
    }

    // ---- attempts ------------------------------------------------------------------------------------------------------
    public async Task<object> StartAsync(Guid examId, CancellationToken ct)
    {
        var e = await db.Exams.FirstOrDefaultAsync(x => x.Id == examId && x.IsPublished, ct) ?? throw new NotFoundException("Exam not found.");
        await RequireEnrollmentAsync(e.CourseId, ct);
        if (e.AvailableFromUtc is { } f && f > Now) throw new ForbiddenAppException("EXAM_NOT_OPEN", "The exam is not open yet.");
        if (e.AvailableUntilUtc is { } u && u < Now) throw new ForbiddenAppException("EXAM_CLOSED", "The exam is closed.");
        var attempts = await db.ExamAttempts.Where(a => a.ExamId == examId && a.UserId == Uid).ToListAsync(ct);
        var open = attempts.FirstOrDefault(a => a.Status == ExamAttemptStatus.InProgress && (a.ExpiresAtUtc == null || a.ExpiresAtUtc > Now));
        if (open is not null) return new { attemptId = open.Id, resumed = true };
        foreach (var stale in attempts.Where(a => a.Status == ExamAttemptStatus.InProgress)) await ExpireAsync(stale, ct); // timed out and never submitted
        if (attempts.Count >= e.MaxAttempts) throw new ConflictAppException("ATTEMPT_LIMIT", "No attempts left.");
        var now = Now;
        var attempt = new ExamAttempt { TenantId = Tid, ExamId = examId, UserId = Uid, AttemptNumber = attempts.Count + 1, StartedAtUtc = now, ExpiresAtUtc = e.DurationMinutes is { } m ? now.AddMinutes(m) : null, DeviceId = me.DeviceId };
        db.ExamAttempts.Add(attempt);
        var rules = await db.ExamQuestionRules.Where(x => x.ExamId == examId).OrderBy(x => x.SortOrder).ToListAsync(ct);
        var picked = new List<Question>();
        foreach (var r in rules)
        {
            if (r.QuestionId is { } qid) { var q = await db.Questions.FirstOrDefaultAsync(x => x.Id == qid, ct); if (q is not null) picked.Add(q); }
            else if (r.QuestionBankId is { } bid)
            {
                var pool = await db.Questions.Where(x => x.QuestionBankId == bid).ToListAsync(ct);
                picked.AddRange(pool.Where(x => picked.All(p => p.Id != x.Id)).OrderBy(_ => Random.Shared.Next()).Take(r.PickCount ?? 1));
            }
        }
        if (e.ShuffleQuestions) picked = picked.OrderBy(_ => Random.Shared.Next()).ToList();
        var order = 0;
        foreach (var q in picked)
        {
            var aq = new ExamAttemptQuestion { TenantId = Tid, ExamAttemptId = attempt.Id, QuestionId = q.Id, SortOrder = order++, Points = q.DefaultPoints };
            db.ExamAttemptQuestions.Add(aq);
            db.ExamAnswers.Add(new ExamAnswer { TenantId = Tid, ExamAttemptQuestionId = aq.Id });
        }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Two starts raced for the same attempt number. The unique index let exactly one through.
            throw new ConflictAppException("ATTEMPT_CONFLICT", "Another attempt was started at the same time. Retry to resume it.");
        }
        return new { attemptId = attempt.Id, resumed = false, expiresAtUtc = attempt.ExpiresAtUtc };
    }

    private async Task<ExamAttempt> MyAttemptAsync(Guid id, CancellationToken ct) =>
        await db.ExamAttempts.FirstOrDefaultAsync(a => a.Id == id && a.UserId == Uid, ct) ?? throw new NotFoundException("Attempt not found.");

    private async Task ExpireAsync(ExamAttempt a, CancellationToken ct)
    {
        if (a.Status != ExamAttemptStatus.InProgress) return;
        a.Status = ExamAttemptStatus.Expired; a.SubmittedAtUtc = a.ExpiresAtUtc ?? Now;
        await GradeAttemptAsync(a, ct);
    }

    public async Task<object> GetAttemptAsync(Guid id, CancellationToken ct)
    {
        var a = await MyAttemptAsync(id, ct);
        if (a.Status == ExamAttemptStatus.InProgress && a.ExpiresAtUtc is { } x && x < Now.AddSeconds(-GraceSeconds)) { await ExpireAsync(a, ct); }
        if (a.Status != ExamAttemptStatus.InProgress) throw new ConflictAppException("ATTEMPT_CLOSED", "This attempt is closed. Read /result.");
        var rows = await (from aq in db.ExamAttemptQuestions join q in db.Questions on aq.QuestionId equals q.Id join ans in db.ExamAnswers on aq.Id equals ans.ExamAttemptQuestionId
                          where aq.ExamAttemptId == id orderby aq.SortOrder select new { aq.Id, QId = q.Id, q.QuestionType, q.Text, aq.Points, ans.AnswerJson }).ToListAsync(ct);
        var qids = rows.Select(r => r.QId).ToList();
        var opts = await db.QuestionOptions.Where(o => qids.Contains(o.QuestionId)).OrderBy(o => o.SortOrder).ToListAsync(ct);
        return new
        {
            attemptId = a.Id, a.ExpiresAtUtc, remainingSeconds = a.ExpiresAtUtc is { } e ? (int?)Math.Max(0, (int)(e - Now).TotalSeconds) : null,
            questions = rows.Select(r => new
            {
                attemptQuestionId = r.Id, type = r.QuestionType.ToString(), r.Text, r.Points, savedAnswer = r.AnswerJson is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(r.AnswerJson),
                // IsCorrect and Explanation are never sent. For matching, the left side and a sorted list of right sides are sent.
                options = r.QuestionType is QuestionType.FillBlank ? null : opts.Where(o => o.QuestionId == r.QId).Select(o => new { o.Id, o.Text }),
                matchTargets = r.QuestionType == QuestionType.Matching ? opts.Where(o => o.QuestionId == r.QId).Select(o => o.MatchKey).OrderBy(x => x).ToList() : null
            })
        };
    }

    public async Task SaveAnswersAsync(Guid id, AnswersRequest r, CancellationToken ct)
    {
        var a = await MyAttemptAsync(id, ct);
        EnsureOpen(a);
        foreach (var item in r.Answers)
        {
            var row = await (from aq in db.ExamAttemptQuestions join ans in db.ExamAnswers on aq.Id equals ans.ExamAttemptQuestionId where aq.Id == item.AttemptQuestionId && aq.ExamAttemptId == id select ans).FirstOrDefaultAsync(ct)
                ?? throw new NotFoundException("Question is not part of this attempt.");
            row.AnswerJson = item.Answer.GetRawText();
        }
        await db.SaveChangesAsync(ct);
    }

    private static void EnsureOpen(ExamAttempt a)
    {
        if (a.Status != ExamAttemptStatus.InProgress) throw new ConflictAppException("ATTEMPT_CLOSED", "This attempt is closed.");
        if (a.ExpiresAtUtc is { } e && e.AddSeconds(GraceSeconds) < Now) throw new ConflictAppException("ATTEMPT_EXPIRED", "Time is up.");
    }

    public async Task<object> EventAsync(Guid id, AttemptEventRequest r, CancellationToken ct)
    {
        var a = await MyAttemptAsync(id, ct); EnsureOpen(a);
        var exam = await db.Exams.FirstAsync(x => x.Id == a.ExamId, ct);
        db.ProctoringEvents.Add(new ProctoringEvent { TenantId = Tid, ExamAttemptId = id, EventType = ParseEnum(r.EventType, ProctoringEventEventType.TabSwitch), OccurredAtUtc = Now });
        a.TabSwitchCount++;
        var voided = false;
        if (exam.TabSwitchDetection && exam.MaxTabSwitches is { } max && a.TabSwitchCount > max) { a.Status = ExamAttemptStatus.Voided; a.SubmittedAtUtc = Now; a.Passed = false; a.Score = 0; voided = true; }
        await db.SaveChangesAsync(ct);
        return new { a.TabSwitchCount, voided };
    }

    public async Task<object> SubmitAsync(Guid id, CancellationToken ct)
    {
        var a = await MyAttemptAsync(id, ct);
        if (a.Status == ExamAttemptStatus.InProgress && a.ExpiresAtUtc is { } e && e.AddSeconds(GraceSeconds) < Now)
        {
            await ExpireAsync(a, ct); // late: grade only what was saved before the deadline and say so
            throw new ConflictAppException("ATTEMPT_EXPIRED", "Time was up. The attempt was closed with the answers saved before the deadline.");
        }
        EnsureOpen(a);
        // Claim the submission atomically so two parallel submits cannot both grade.
        var claimed = await db.ExamAttempts.Where(x => x.Id == id && x.Status == ExamAttemptStatus.InProgress).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ExamAttemptStatus.Submitted).SetProperty(x => x.SubmittedAtUtc, Now), ct);
        if (claimed == 0) throw new ConflictAppException("ATTEMPT_CLOSED", "This attempt is already submitted.");
        await db.Entry(a).ReloadAsync(ct);
        await GradeAttemptAsync(a, ct);
        return await ResultAsync(a, forStudent: true, ct);
    }

    private async Task GradeAttemptAsync(ExamAttempt a, CancellationToken ct)
    {
        var rows = await (from aq in db.ExamAttemptQuestions join q in db.Questions on aq.QuestionId equals q.Id join ans in db.ExamAnswers on aq.Id equals ans.ExamAttemptQuestionId where aq.ExamAttemptId == a.Id select new { aq, q, ans }).ToListAsync(ct);
        var qids = rows.Select(r => r.q.Id).ToList();
        var opts = await db.QuestionOptions.Where(o => qids.Contains(o.QuestionId)).ToListAsync(ct);
        var manualPending = false;
        foreach (var r in rows)
        {
            var o = opts.Where(x => x.QuestionId == r.q.Id).ToList();
            if (r.q.QuestionType is QuestionType.ShortAnswer or QuestionType.Essay) { manualPending |= r.ans.GradedAtUtc is null; continue; }
            var ok = IsCorrect(r.q.QuestionType, o, r.ans.AnswerJson);
            r.ans.IsCorrect = ok; r.ans.PointsAwarded = ok ? r.aq.Points : 0; r.ans.GradedAtUtc = Now;
        }
        a.MaxScore = rows.Sum(r => r.aq.Points);
        a.Score = rows.Sum(r => r.ans.PointsAwarded ?? 0);
        if (!manualPending) Finish(a, a.Status == ExamAttemptStatus.Expired);
        await db.SaveChangesAsync(ct);
    }

    private void Finish(ExamAttempt a, bool expired)
    {
        var pass = db.Exams.Where(x => x.Id == a.ExamId).Select(x => x.PassingScorePercent).First();
        a.Passed = a.MaxScore > 0 && 100.0 * (a.Score ?? 0) / a.MaxScore >= pass;
        if (!expired) a.Status = ExamAttemptStatus.Graded;
    }

    internal static bool IsCorrect(QuestionType t, List<QuestionOption> o, string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            var d = JsonSerializer.Deserialize<JsonElement>(json);
            switch (t)
            {
                case QuestionType.McqSingle or QuestionType.TrueFalse:
                    return d.TryGetProperty("optionId", out var oid) && Guid.TryParse(oid.GetString(), out var g) && o.Any(x => x.Id == g && x.IsCorrect);
                case QuestionType.McqMulti:
                    if (!d.TryGetProperty("optionIds", out var arr) || arr.ValueKind != JsonValueKind.Array) return false;
                    var chosen = arr.EnumerateArray().Select(x => Guid.TryParse(x.GetString(), out var v) ? v : Guid.Empty).ToHashSet();
                    return chosen.SetEquals(o.Where(x => x.IsCorrect).Select(x => x.Id));
                case QuestionType.FillBlank:
                    var txt = d.TryGetProperty("text", out var tx) ? tx.GetString()?.Trim() : null;
                    return txt is { Length: > 0 } && o.Any(x => x.IsCorrect && string.Equals(x.Text.Trim(), txt, StringComparison.OrdinalIgnoreCase));
                case QuestionType.Matching:
                    if (!d.TryGetProperty("pairs", out var pairs) || pairs.ValueKind != JsonValueKind.Object) return false;
                    var given = pairs.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
                    return given.Count == o.Count && o.All(x => given.TryGetValue(x.Id.ToString(), out var v) && string.Equals(v, x.MatchKey, StringComparison.OrdinalIgnoreCase));
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
        return false;
    }

    // ---- results and manual grading ------------------------------------------------------------------------------------
    public async Task<object> ResultAsync(Guid attemptId, CancellationToken ct)
    {
        var a = await db.ExamAttempts.FirstOrDefaultAsync(x => x.Id == attemptId, ct) ?? throw new NotFoundException("Attempt not found.");
        var exam = await db.Exams.FirstAsync(x => x.Id == a.ExamId, ct);
        var course = await db.Courses.FirstAsync(x => x.Id == exam.CourseId, ct);
        if (await acc.IsGraderAsync(course, ct)) return await ResultAsync(a, false, ct);
        if (a.UserId != Uid) throw new NotFoundException("Attempt not found.");
        return await ResultAsync(a, true, ct);
    }

    private async Task<object> ResultAsync(ExamAttempt a, bool forStudent, CancellationToken ct)
    {
        if (forStudent && a.Status == ExamAttemptStatus.InProgress) throw new ConflictAppException("ATTEMPT_OPEN", "Submit the attempt first.");
        var pending = await (from aq in db.ExamAttemptQuestions join ans in db.ExamAnswers on aq.Id equals ans.ExamAttemptQuestionId where aq.ExamAttemptId == a.Id && ans.GradedAtUtc == null select ans.Id).AnyAsync(ct);
        // Students see the score and pass state. Per-question correctness and explanations are never returned to students in this build.
        var answers = forStudent ? null : await (from aq in db.ExamAttemptQuestions join ans in db.ExamAnswers on aq.Id equals ans.ExamAttemptQuestionId where aq.ExamAttemptId == a.Id
                                                  select new { answerId = ans.Id, aq.QuestionId, ans.AnswerJson, ans.IsCorrect, ans.PointsAwarded, ans.Feedback }).ToListAsync(ct);
        return new { attemptId = a.Id, a.UserId, status = a.Status.ToString(), a.Score, a.MaxScore, a.Passed, awaitingManualGrading = pending, a.TabSwitchCount, answers };
    }

    public async Task<object> ExamAttemptsAsync(Guid examId, CancellationToken ct)
    {
        var e = await GraderExamAsync(examId, ct);
        return await db.ExamAttempts.Where(a => a.ExamId == e.Id).OrderBy(a => a.StartedAtUtc)
            .Select(a => new { a.Id, a.UserId, a.AttemptNumber, Status = a.Status.ToString(), a.Score, a.MaxScore, a.Passed, a.TabSwitchCount }).ToListAsync(ct);
    }

    public async Task GradeAnswerAsync(Guid answerId, GradeRequest r, CancellationToken ct)
    {
        var row = await (from ans in db.ExamAnswers join aq in db.ExamAttemptQuestions on ans.ExamAttemptQuestionId equals aq.Id where ans.Id == answerId select new { ans, aq }).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Answer not found.");
        var a = await db.ExamAttempts.FirstAsync(x => x.Id == row.aq.ExamAttemptId, ct);
        var exam = await db.Exams.FirstAsync(x => x.Id == a.ExamId, ct);
        await GraderCourseAsync(exam.CourseId, ct);
        if (a.Status == ExamAttemptStatus.InProgress) throw new ConflictAppException("ATTEMPT_OPEN", "The attempt is still open.");
        if (r.Points < 0 || r.Points > row.aq.Points) throw new ValidationAppException($"Points must be between 0 and {row.aq.Points}.");
        row.ans.PointsAwarded = r.Points; row.ans.IsCorrect = r.Points >= row.aq.Points; row.ans.Feedback = r.Feedback; row.ans.GradedBy = Uid; row.ans.GradedAtUtc = Now;
        await db.SaveChangesAsync(ct);
        a.Score = await (from aq in db.ExamAttemptQuestions join ans in db.ExamAnswers on aq.Id equals ans.ExamAttemptQuestionId where aq.ExamAttemptId == a.Id select ans.PointsAwarded ?? 0).SumAsync(ct);
        var stillPending = await (from aq in db.ExamAttemptQuestions join ans in db.ExamAnswers on aq.Id equals ans.ExamAttemptQuestionId where aq.ExamAttemptId == a.Id && ans.GradedAtUtc == null select ans.Id).AnyAsync(ct);
        var done = !stillPending && a.Status is ExamAttemptStatus.Submitted or ExamAttemptStatus.Expired;
        if (done) Finish(a, a.Status == ExamAttemptStatus.Expired);
        await db.SaveChangesAsync(ct);
        if (done) await notifier.SendAsync(a.TenantId, [a.UserId], "ExamGraded", "Your exam was graded", exam.Title, ct);
    }
    // ---- bank and question management ----------------------------------------------------------------------------------
    public async Task<object> ListBanksAsync(Guid courseId, CancellationToken ct)
    {
        await OwnedCourseAsync(courseId, ct);
        return await db.QuestionBanks.Where(b => b.CourseId == courseId).OrderBy(b => b.Name).Select(b => new { b.Id, b.Name, b.Topic, questionCount = db.Questions.Count(q => q.QuestionBankId == b.Id) }).ToListAsync(ct);
    }
    public async Task<object> ListQuestionsAsync(Guid bankId, string? type, string? difficulty, string? topic, CancellationToken ct)
    {
        await OwnedBankAsync(bankId, ct);
        var q = db.Questions.Where(x => x.QuestionBankId == bankId);
        if (type is not null) { var t = ParseEnum<QuestionType>(type, QuestionType.McqSingle); q = q.Where(x => x.QuestionType == t); }
        if (difficulty is not null) { var d = ParseEnum(difficulty, QuestionDifficulty.Medium); q = q.Where(x => x.Difficulty == d); }
        if (!string.IsNullOrWhiteSpace(topic)) q = q.Where(x => x.Topic == topic);
        // Authors see correct answers; this route is staff-only.
        return await q.OrderBy(x => x.CreatedAtUtc).Take(500).Select(x => new { x.Id, QuestionType = x.QuestionType.ToString(), x.Text, points = x.DefaultPoints, Difficulty = x.Difficulty.ToString(), x.Topic, x.Explanation,
            options = db.QuestionOptions.Where(o => o.QuestionId == x.Id).OrderBy(o => o.SortOrder).Select(o => new { o.Id, o.Text, o.IsCorrect, o.MatchKey }).ToList() }).ToListAsync(ct);
    }
    private async Task<Question> OwnedQuestionAsync(Guid id, CancellationToken ct)
    {
        var q = await db.Questions.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Question not found.");
        await OwnedBankAsync(q.QuestionBankId, ct); return q;
    }
    public async Task UpdateQuestionAsync(Guid id, QuestionRequest r, CancellationToken ct)
    {
        var q = await OwnedQuestionAsync(id, ct);
        var (type, opts) = ValidateQuestion(r);
        if (r.Points <= 0 || r.Points > 1000) throw new ValidationAppException("Points must be between 0 and 1000.");
        // Attempts already started keep their frozen copy of the question; only future attempts see the change.
        q.QuestionType = type; q.Text = r.Text.Trim(); q.DefaultPoints = r.Points; q.Difficulty = ParseEnum(r.Difficulty, q.Difficulty); q.Topic = r.Topic; q.Explanation = r.Explanation;
        db.QuestionOptions.RemoveRange(await db.QuestionOptions.Where(o => o.QuestionId == id).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        AddOptions(q, type, r, opts); await db.SaveChangesAsync(ct);
    }
    public async Task DeleteQuestionAsync(Guid id, CancellationToken ct)
    {
        var q = await OwnedQuestionAsync(id, ct);
        if (await db.ExamQuestionRules.AnyAsync(x => x.QuestionId == id && db.Exams.Any(e => e.Id == x.ExamId && e.IsPublished), ct)) throw new ConflictAppException("QUESTION_IN_USE", "This question is part of a published exam.");
        db.Questions.Remove(q); await db.SaveChangesAsync(ct);   // soft delete through the audit interceptor
    }
    public sealed record ImportRequest(string Format, string Content);
    public async Task<object> ImportAsync(Guid bankId, ImportRequest r, CancellationToken ct)
    {
        await OwnedBankAsync(bankId, ct);
        if (string.IsNullOrWhiteSpace(r.Content) || r.Content.Length > 500_000) throw new ValidationAppException("Content is required (max 500 KB).");
        List<QuestionRequest> rows;
        if (string.Equals(r.Format, "json", StringComparison.OrdinalIgnoreCase))
        {
            try { rows = JsonSerializer.Deserialize<List<QuestionRequest>>(r.Content, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? []; }
            catch (JsonException) { throw new ValidationAppException("Content is not a valid JSON array of questions."); }
        }
        else if (string.Equals(r.Format, "csv", StringComparison.OrdinalIgnoreCase)) rows = ParseCsv(r.Content);
        else throw new ValidationAppException("Format must be json or csv.");
        if (rows.Count is 0 or > 500) throw new ValidationAppException("Import between 1 and 500 questions at a time.");
        // All-or-nothing: every row is validated before anything is saved, and the error names the row.
        var checkedRows = new List<(QuestionType, List<OptionRequest>, QuestionRequest)>();
        for (var i = 0; i < rows.Count; i++)
        {
            try { if (rows[i] is null || string.IsNullOrWhiteSpace(rows[i].Text) || rows[i].Points <= 0 || rows[i].Points > 1000) throw new ValidationAppException("Text and points (0 to 1000) are required."); var (t, o) = ValidateQuestion(rows[i]); checkedRows.Add((t, o, rows[i])); }
            catch (ValidationAppException ex) { throw new ValidationAppException($"Row {i + 1}: {ex.Message}"); }
        }
        foreach (var (t, o, row) in checkedRows)
        {
            var q = new Question { TenantId = Tid, QuestionBankId = bankId, QuestionType = t, Text = row.Text.Trim(), DefaultPoints = row.Points, Difficulty = ParseEnum(row.Difficulty, QuestionDifficulty.Medium), Topic = row.Topic, Explanation = row.Explanation };
            db.Questions.Add(q); AddOptions(q, t, row, o);
        }
        await db.SaveChangesAsync(ct);
        return new { imported = checkedRows.Count };
    }
    /// <summary>CSV columns: type,text,points,difficulty,topic,explanation,options,answer. Options are separated by |. Answer is the 1-based number of the correct option (several separated by ;), true/false for TrueFalse, or the accepted text for FillBlank. Matching is JSON only.</summary>
    private static List<QuestionRequest> ParseCsv(string csv)
    {
        var lines = csv.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries); var res = new List<QuestionRequest>();
        for (var i = 1; i < lines.Length; i++)   // line 0 is the header
        {
            var c = SplitCsv(lines[i]);
            if (c.Count < 8) throw new ValidationAppException($"Row {i}: expected 8 columns.");
            var type = c[0].Trim(); var opts = c[6].Split('|', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList(); var ans = c[7].Trim();
            if (!double.TryParse(c[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pts)) throw new ValidationAppException($"Row {i}: points is not a number.");
            List<OptionRequest>? o = null; bool? tf = null;
            if (type.Equals("TrueFalse", StringComparison.OrdinalIgnoreCase)) tf = bool.TryParse(ans, out var b) ? b : throw new ValidationAppException($"Row {i}: answer must be true or false.");
            else if (type.Equals("FillBlank", StringComparison.OrdinalIgnoreCase)) o = [new(ans, true, null)];
            else if (type.Equals("Matching", StringComparison.OrdinalIgnoreCase)) throw new ValidationAppException($"Row {i}: Matching is supported in JSON only.");
            else if (!type.Equals("ShortAnswer", StringComparison.OrdinalIgnoreCase) && !type.Equals("Essay", StringComparison.OrdinalIgnoreCase))
            {
                var right = ans.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, out var n) ? n : -1).ToHashSet();
                o = opts.Select((t, k) => new OptionRequest(t, right.Contains(k + 1), null)).ToList();
            }
            res.Add(new QuestionRequest(type, c[1].Trim(), pts, string.IsNullOrWhiteSpace(c[3]) ? null : c[3].Trim(), string.IsNullOrWhiteSpace(c[4]) ? null : c[4].Trim(), string.IsNullOrWhiteSpace(c[5]) ? null : c[5].Trim(), tf, o));
        }
        return res;
    }
    private static List<string> SplitCsv(string line)
    {
        var res = new List<string>(); var sb = new System.Text.StringBuilder(); var q = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (q) { if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else if (ch == '"') q = false; else sb.Append(ch); }
            else if (ch == '"') q = true; else if (ch == ',') { res.Add(sb.ToString()); sb.Clear(); } else sb.Append(ch);
        }
        res.Add(sb.ToString()); return res;
    }
    /// <summary>GET /exams/{id} for authors: the full editable settings so the edit form can prefill instead of overwriting fields it cannot see. UpdateExamAsync never touches question rules, and Kind is immutable after creation.</summary>
    public async Task<object> GetExamAsync(Guid id, CancellationToken ct)
    {
        var e = await OwnedExamAsync(id, ct);
        var hasAttempts = await db.ExamAttempts.AnyAsync(a => a.ExamId == id, ct);
        return new { e.Id, e.CourseId, e.Title, Kind = e.Kind.ToString(), e.Instructions, e.DurationMinutes, e.MaxAttempts, e.PassingScorePercent, e.AvailableFromUtc, e.AvailableUntilUtc, e.TabSwitchDetection, e.MaxTabSwitches, e.IsPublished, locked = e.IsPublished && hasAttempts };
    }

    public async Task UpdateExamAsync(Guid id, ExamRequest r, CancellationToken ct)
    {
        var e = await OwnedExamAsync(id, ct);
        if (e.IsPublished && await db.ExamAttempts.AnyAsync(a => a.ExamId == id, ct)) throw new ConflictAppException("EXAM_HAS_ATTEMPTS", "An exam with attempts cannot be changed.");
        if (r.MaxAttempts is < 1 or > 20) throw new ValidationAppException("MaxAttempts must be 1 to 20.");
        if (r.PassingScorePercent is < 0 or > 100) throw new ValidationAppException("PassingScorePercent must be 0 to 100.");
        if (r.DurationMinutes is <= 0) throw new ValidationAppException("DurationMinutes must be positive.");
        e.Title = r.Title.Trim(); e.Instructions = r.Instructions; e.DurationMinutes = r.DurationMinutes; e.MaxAttempts = r.MaxAttempts; e.PassingScorePercent = r.PassingScorePercent;
        e.AvailableFromUtc = r.AvailableFromUtc; e.AvailableUntilUtc = r.AvailableUntilUtc; e.TabSwitchDetection = r.TabSwitchDetection; e.MaxTabSwitches = r.MaxTabSwitches;
        await db.SaveChangesAsync(ct);
    }
}
