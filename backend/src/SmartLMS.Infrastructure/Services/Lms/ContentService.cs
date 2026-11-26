using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Domain.Exceptions;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Infrastructure.Services.Lms;

public sealed record PrerequisitesRequest(List<Guid> CourseIds);
public sealed record OrderRequest(List<Guid> Ids);
public sealed record ChapterItem(string Title, int StartSeconds);
public sealed record ChaptersRequest(List<ChapterItem> Chapters);
public sealed record SlidesRequest(List<JsonElement> Slides);
public sealed record QuizPointItem(Guid QuestionId, int TimestampSeconds, bool IsRequired, int? AllowSkipAfterAttempts);
public sealed record PricingRequest(string PricingModel, decimal Price, string? Currency, int? AccessDays);
public sealed record LessonPriceRequest(decimal? Price);
public sealed record QuizPointsRequest(List<QuizPointItem> Points);
public sealed record QuizAnswerRequest(JsonElement Answer);
public sealed record StaffItem(Guid UserId, string StaffRole);
public sealed record StaffRequest(List<StaffItem> Staff);

/// <summary>Course structure and lesson enrichment: prerequisites, ordering, chapters, slides and interactive-video quiz pause points. Authors only on their own courses.</summary>
public sealed class ContentService(ApplicationDbContext db, ICurrentUser me, LmsService lms)
{
    private Guid Uid => me.UserId ?? throw new UnauthorizedAppException();
    private bool IsAdmin => me.IsInRole(RoleNames.TenantAdmin) || me.IsInRole(RoleNames.SuperAdmin);
    private Guid Tid => db.CurrentTenantId ?? throw new ValidationAppException("A tenant is required.");
    private static DateTime Now => DateTime.UtcNow;

    private async Task<Course> OwnedCourseAsync(Guid id, CancellationToken ct)
    {
        var c = await db.Courses.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null || !await new CourseAccess(db, me).IsAuthorAsync(c, ct)) throw new NotFoundException("Course not found.");
        return c;
    }
    private async Task<Lesson> OwnedLessonAsync(Guid id, CancellationToken ct)
    {
        var l = await db.Lessons.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Lesson not found.");
        await OwnedCourseAsync(l.CourseId, ct); return l;
    }

    public async Task<object> ListStaffAsync(Guid courseId, CancellationToken ct)
    {
        await OwnedCourseAsync(courseId, ct);
        return await (from s in db.CourseStaff join u in db.Users on s.UserId equals u.Id where s.CourseId == courseId orderby u.FullName select new { s.UserId, u.FullName, StaffRole = s.StaffRole.ToString() }).ToListAsync(ct);
    }
    /// <summary>Replaces the staff list. Only the owner or a TenantAdmin (a co-instructor cannot add or remove staff).</summary>
    public async Task SetStaffAsync(Guid courseId, StaffRequest r, CancellationToken ct)
    {
        var c = await db.Courses.FirstOrDefaultAsync(x => x.Id == courseId, ct);
        if (c is null || !new CourseAccess(db, me).IsOwnerOrAdmin(c)) throw new NotFoundException("Course not found.");
        var items = r.Staff ?? [];
        if (items.Count > 30 || items.Select(i => i.UserId).Distinct().Count() != items.Count) throw new ValidationAppException("Up to 30 staff, each user once.");
        var rows = new List<(Guid U, CourseStaffStaffRole Role)>();
        foreach (var i in items)
        {
            if (!Enum.TryParse<CourseStaffStaffRole>(i.StaffRole, false, out var role) || !Enum.IsDefined(role)) throw new ValidationAppException("StaffRole must be CoInstructor or TeachingAssistant.");
            if (i.UserId == c.OwnerInstructorId) throw new ValidationAppException("The owner is already the instructor.");
            if (!await db.UserTenantMemberships.AnyAsync(m => m.UserId == i.UserId && m.RevokedAtUtc == null, ct)) throw new NotFoundException("User not found.");
            var need = role == CourseStaffStaffRole.CoInstructor ? RoleNames.Instructor : RoleNames.TeachingAssistant;
            if (!await db.UserRoles.AnyAsync(ur => ur.UserId == i.UserId && db.Roles.Any(x => x.Id == ur.RoleId && x.Name == need), ct)) throw new ValidationAppException($"That user does not have the {need} role.");
            rows.Add((i.UserId, role));
        }
        db.CourseStaff.RemoveRange(await db.CourseStaff.Where(s => s.CourseId == courseId).ToListAsync(ct)); await db.SaveChangesAsync(ct);
        foreach (var (u, role) in rows) db.CourseStaff.Add(new CourseStaff { TenantId = c.TenantId, CourseId = courseId, UserId = u, StaffRole = role, CreatedAtUtc = Now });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>GET /courses/{id}/prerequisites: the current prerequisite courses with titles, so the editor can prefill instead of overwriting blind.</summary>
    public async Task<object> ListPrerequisitesAsync(Guid courseId, CancellationToken ct)
    {
        await OwnedCourseAsync(courseId, ct);
        return await (from pr in db.CoursePrerequisites join c in db.Courses on pr.PrerequisiteCourseId equals c.Id where pr.CourseId == courseId orderby c.Title select new { courseId = c.Id, title = c.Title }).ToListAsync(ct);
    }

    public async Task SetPrerequisitesAsync(Guid courseId, PrerequisitesRequest r, CancellationToken ct)
    {
        await OwnedCourseAsync(courseId, ct);
        var ids = (r.CourseIds ?? []).Distinct().ToList();
        if (ids.Count > 20) throw new ValidationAppException("At most 20 prerequisite courses.");
        if (ids.Contains(courseId)) throw new ValidationAppException("A course cannot be its own prerequisite.");
        foreach (var id in ids) if (!await db.Courses.AnyAsync(c => c.Id == id, ct)) throw new NotFoundException("Prerequisite course not found.");
        // A cycle would make every course in it impossible to start. Walk the existing graph from each new prerequisite and look for this course.
        var edges = await db.CoursePrerequisites.Where(p => p.CourseId != courseId).Select(p => new { p.CourseId, p.PrerequisiteCourseId }).ToListAsync(ct);
        var seen = new HashSet<Guid>(); var stack = new Stack<Guid>(ids);
        while (stack.Count > 0) { var n = stack.Pop(); if (n == courseId) throw new ConflictAppException("PREREQUISITE_CYCLE", "These prerequisites would create a cycle."); if (!seen.Add(n)) continue; foreach (var e in edges.Where(e => e.CourseId == n)) stack.Push(e.PrerequisiteCourseId); }
        db.CoursePrerequisites.RemoveRange(await db.CoursePrerequisites.Where(p => p.CourseId == courseId).ToListAsync(ct)); await db.SaveChangesAsync(ct);
        foreach (var id in ids) db.CoursePrerequisites.Add(new CoursePrerequisite { TenantId = Tid, CourseId = courseId, PrerequisiteCourseId = id, CreatedAtUtc = Now });
        await db.SaveChangesAsync(ct);
    }

    public async Task ReorderSectionsAsync(Guid courseId, OrderRequest r, CancellationToken ct)
    {
        await OwnedCourseAsync(courseId, ct);
        var rows = await db.Sections.Where(s => s.CourseId == courseId).ToListAsync(ct);
        if (r.Ids is null || r.Ids.Count != rows.Count || r.Ids.Distinct().Count() != rows.Count || !r.Ids.All(i => rows.Any(s => s.Id == i))) throw new ValidationAppException("Ids must list every section of the course exactly once.");
        for (var i = 0; i < r.Ids.Count; i++) rows.First(s => s.Id == r.Ids[i]).SortOrder = i;
        await db.SaveChangesAsync(ct);
    }
    public async Task ReorderLessonsAsync(Guid sectionId, OrderRequest r, CancellationToken ct)
    {
        var sec = await db.Sections.FirstOrDefaultAsync(s => s.Id == sectionId, ct) ?? throw new NotFoundException("Section not found.");
        await OwnedCourseAsync(sec.CourseId, ct);
        var rows = await db.Lessons.Where(l => l.SectionId == sectionId).ToListAsync(ct);
        if (r.Ids is null || r.Ids.Count != rows.Count || r.Ids.Distinct().Count() != rows.Count || !r.Ids.All(i => rows.Any(l => l.Id == i))) throw new ValidationAppException("Ids must list every lesson of the section exactly once.");
        for (var i = 0; i < r.Ids.Count; i++) rows.First(l => l.Id == r.Ids[i]).SortOrder = i;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetChaptersAsync(Guid lessonId, ChaptersRequest r, CancellationToken ct)
    {
        var l = await OwnedLessonAsync(lessonId, ct); var items = r.Chapters ?? [];
        if (items.Count > 200 || items.Any(c => string.IsNullOrWhiteSpace(c.Title) || c.Title.Length > 200 || c.StartSeconds < 0)) throw new ValidationAppException("Up to 200 chapters, each with a title (max 200) and StartSeconds >= 0.");
        db.LessonChapters.RemoveRange(await db.LessonChapters.Where(c => c.LessonId == lessonId).ToListAsync(ct)); await db.SaveChangesAsync(ct);
        foreach (var c in items.OrderBy(c => c.StartSeconds)) db.LessonChapters.Add(new LessonChapter { TenantId = l.TenantId, LessonId = lessonId, Title = c.Title.Trim(), StartSeconds = c.StartSeconds, Source = LessonChapterSource.Manual, CreatedAtUtc = Now });
        await db.SaveChangesAsync(ct);
    }
    public async Task SetSlidesAsync(Guid lessonId, SlidesRequest r, CancellationToken ct)
    {
        var l = await OwnedLessonAsync(lessonId, ct); var items = r.Slides ?? [];
        if (items.Count > 100 || items.Any(s => s.ValueKind != JsonValueKind.Object || s.GetRawText().Length > 20000)) throw new ValidationAppException("Up to 100 slides, each a JSON object of at most 20 KB.");
        db.LessonSlides.RemoveRange(await db.LessonSlides.Where(c => c.LessonId == lessonId).ToListAsync(ct)); await db.SaveChangesAsync(ct);
        var i = 0; foreach (var s in items) db.LessonSlides.Add(new LessonSlide { TenantId = l.TenantId, LessonId = lessonId, SortOrder = i++, ContentJson = s.GetRawText(), CreatedAtUtc = Now });
        await db.SaveChangesAsync(ct);
    }

    // ---- interactive video quizzes ---------------------------------------------------------------------------------------
    public async Task SetQuizPointsAsync(Guid lessonId, QuizPointsRequest r, CancellationToken ct)
    {
        var l = await OwnedLessonAsync(lessonId, ct); var items = r.Points ?? [];
        if (items.Count > 50 || items.Any(p => p.TimestampSeconds < 0 || p.AllowSkipAfterAttempts is < 1 or > 20)) throw new ValidationAppException("Up to 50 pause points, with TimestampSeconds >= 0 and AllowSkipAfterAttempts between 1 and 20.");
        foreach (var p in items)
        {
            // The question must be in a bank of this course, so one course cannot borrow another's questions.
            var ok = await (from q in db.Questions join b in db.QuestionBanks on q.QuestionBankId equals b.Id where q.Id == p.QuestionId && b.CourseId == l.CourseId select q.Id).AnyAsync(ct);
            if (!ok) throw new NotFoundException("Question not found in this course.");
            var type = await db.Questions.Where(q => q.Id == p.QuestionId).Select(q => q.QuestionType).FirstAsync(ct);
            if (type is QuestionType.ShortAnswer or QuestionType.Essay) throw new ValidationAppException("Only auto-graded question types can pause a video.");
        }
        db.InteractiveVideoQuizzes.RemoveRange(await db.InteractiveVideoQuizzes.Where(c => c.LessonId == lessonId).ToListAsync(ct)); await db.SaveChangesAsync(ct);
        foreach (var p in items.OrderBy(p => p.TimestampSeconds)) db.InteractiveVideoQuizzes.Add(new InteractiveVideoQuizze { TenantId = l.TenantId, LessonId = lessonId, QuestionId = p.QuestionId, TimestampSeconds = p.TimestampSeconds, IsRequired = p.IsRequired, AllowSkipAfterAttempts = p.AllowSkipAfterAttempts, CreatedAtUtc = Now });
        await db.SaveChangesAsync(ct);
    }
    /// <summary>Same access as opening the lesson: authors always, students only with enrollment, drip and prerequisites satisfied (or free preview or a pay-per-lesson grant).</summary>
    private async Task EnsureCanReadLessonAsync(Guid lessonId, CancellationToken ct)
    {
        var l = await db.Lessons.FirstOrDefaultAsync(x => x.Id == lessonId, ct) ?? throw new NotFoundException("Lesson not found.");
        var c = await db.Courses.FirstAsync(x => x.Id == l.CourseId, ct);
        if (!await new CourseAccess(db, me).IsGraderAsync(c, ct)) await lms.GetLessonAsync(lessonId, ct);
    }
    /// <summary>Sets how a course is sold. Orders already placed keep the price they were bought at.</summary>
    public async Task SetPricingAsync(Guid courseId, PricingRequest r, CancellationToken ct)
    {
        var c = await OwnedCourseAsync(courseId, ct);
        var model = Enum.TryParse<CoursePricingModel>(r.PricingModel, true, out var m) && Enum.IsDefined(m) ? m : throw new ValidationAppException("PricingModel must be Free, OneTime, PayPerLesson or SubscriptionOnly.");
        var cur = (r.Currency ?? c.Currency).ToUpperInvariant(); if (cur.Length != 3) throw new ValidationAppException("The currency must be a 3-letter code.");
        if (model == CoursePricingModel.OneTime && (r.Price <= 0 || r.Price > 1_000_000)) throw new ValidationAppException("A one-time price must be above 0 and at most 1,000,000.");
        if (r.AccessDays is < 1 or > 3650) throw new ValidationAppException("AccessDays must be between 1 and 3650, or empty for lifetime access.");
        c.PricingModel = model; c.Price = model == CoursePricingModel.OneTime ? decimal.Round(r.Price, 2) : 0m; c.Currency = cur; c.AccessDays = r.AccessDays;
        await db.SaveChangesAsync(ct);
    }
    public async Task SetLessonPriceAsync(Guid lessonId, LessonPriceRequest r, CancellationToken ct)
    {
        var l = await OwnedLessonAsync(lessonId, ct);
        if (r.Price is { } p && (p <= 0 || p > 1_000_000)) throw new ValidationAppException("The price must be above 0 and at most 1,000,000, or empty to stop selling this lesson alone.");
        l.PayPerLessonPrice = r.Price is { } v ? decimal.Round(v, 2) : null;
        await db.SaveChangesAsync(ct);
    }
    /// <summary>Timed transcript cues for the player (captions + seek). Same read guard as chapters.</summary>
    public async Task<object> TranscriptAsync(Guid lessonId, CancellationToken ct)
    {
        await EnsureCanReadLessonAsync(lessonId, ct);
        var t = await db.LessonTranscripts.AsNoTracking().FirstOrDefaultAsync(x => x.LessonId == lessonId, ct)
            ?? throw new NotFoundException("This lesson has no transcript yet.");
        var cues = System.Text.Json.JsonSerializer.Deserialize<List<SmartLMS.Application.Interfaces.AiTranscriptCue>>(t.CuesJson) ?? [];
        return new { t.CueCount, Cues = cues.OrderBy(c => c.Start).Select(c => new { c.Start, c.End, c.Text }).ToList(), t.GeneratedAtUtc };
    }

    public async Task<object> ChaptersAsync(Guid lessonId, CancellationToken ct)
    {
        await EnsureCanReadLessonAsync(lessonId, ct);
        return await db.LessonChapters.AsNoTracking().Where(c => c.LessonId == lessonId).OrderBy(c => c.StartSeconds).Select(c => new { c.Id, c.Title, c.StartSeconds, Source = c.Source.ToString() }).ToListAsync(ct);
    }
    public async Task<object> SlidesAsync(Guid lessonId, CancellationToken ct)
    {
        await EnsureCanReadLessonAsync(lessonId, ct);
        var rows = await db.LessonSlides.AsNoTracking().Where(c => c.LessonId == lessonId).OrderBy(c => c.SortOrder).Select(c => new { c.Id, c.SortOrder, c.ContentJson }).ToListAsync(ct);
        return rows.Select(r => new { r.Id, r.SortOrder, content = JsonSerializer.Deserialize<JsonElement>(r.ContentJson) }).ToList();
    }

    public async Task<object> QuizPointsAsync(Guid lessonId, CancellationToken ct)
    {
        var l = await db.Lessons.FirstOrDefaultAsync(x => x.Id == lessonId, ct) ?? throw new NotFoundException("Lesson not found.");
        var c = await db.Courses.FirstAsync(x => x.Id == l.CourseId, ct); var author = await new CourseAccess(db, me).IsGraderAsync(c, ct);
        if (!author) await lms.GetLessonAsync(lessonId, ct);   // same access rules as opening the lesson (enrollment, drip, prerequisites)
        var rows = await (from p in db.InteractiveVideoQuizzes join q in db.Questions on p.QuestionId equals q.Id where p.LessonId == lessonId orderby p.TimestampSeconds
                          select new { p.Id, p.TimestampSeconds, p.IsRequired, p.AllowSkipAfterAttempts, question = new { q.Id, q.Text, QuestionType = q.QuestionType.ToString(), points = q.DefaultPoints } }).ToListAsync(ct);
        var ids = rows.Select(x => x.Id).ToList();
        var options = await (from p in db.InteractiveVideoQuizzes join o in db.QuestionOptions on p.QuestionId equals o.QuestionId where p.LessonId == lessonId orderby o.SortOrder
                             select new { p.Id, OptId = o.Id, o.Text, o.IsCorrect, o.MatchKey }).ToListAsync(ct);
        return rows.Select(x => new { x.Id, x.TimestampSeconds, x.IsRequired, x.AllowSkipAfterAttempts, x.question,
            // Students never receive IsCorrect. Matching keys are shown to authors only.
            options = options.Where(o => o.Id == x.Id).Select(o => author ? (object)new { id = o.OptId, o.Text, o.IsCorrect, o.MatchKey } : new { id = o.OptId, o.Text }).ToList() });
    }
    public async Task<object> AnswerQuizAsync(Guid quizId, QuizAnswerRequest r, CancellationToken ct)
    {
        var p = await db.InteractiveVideoQuizzes.FirstOrDefaultAsync(x => x.Id == quizId, ct) ?? throw new NotFoundException("Quiz not found.");
        await lms.GetLessonAsync(p.LessonId, ct);
        var q = await db.Questions.FirstAsync(x => x.Id == p.QuestionId, ct);
        var opts = await db.QuestionOptions.Where(o => o.QuestionId == q.Id).ToListAsync(ct);
        var raw = r.Answer.ValueKind == JsonValueKind.Undefined ? null : r.Answer.GetRawText();
        if (raw is { Length: > 5000 }) throw new ValidationAppException("Answer is too large.");
        var ok = AssessmentService.IsCorrect(q.QuestionType, opts, raw);
        db.InteractiveQuizResponses.Add(new InteractiveQuizResponse { TenantId = Tid, UserId = Uid, InteractiveVideoQuizId = quizId, AnswerJson = raw ?? "{}", IsCorrect = ok, AnsweredAtUtc = Now, CreatedAtUtc = Now });
        await db.SaveChangesAsync(ct);
        var attempts = await db.InteractiveQuizResponses.CountAsync(x => x.InteractiveVideoQuizId == quizId && x.UserId == Uid, ct);
        var already = await db.InteractiveQuizResponses.AnyAsync(x => x.InteractiveVideoQuizId == quizId && x.UserId == Uid && x.IsCorrect, ct);
        var canContinue = already || !p.IsRequired || (p.AllowSkipAfterAttempts is { } n && attempts >= n);
        // The explanation is shown only once the answer is right or playback is allowed to continue, never the correct option itself.
        return new { correct = ok, canContinue, attempts, explanation = ok || canContinue ? q.Explanation : null };
    }
}
