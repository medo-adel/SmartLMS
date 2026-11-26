using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Domain.Exceptions;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Infrastructure.Services.Lms;

public sealed record CategoryRequest(string Name, Guid? ParentCategoryId, int SortOrder = 0, string? IconName = null);
public sealed record CourseRequest(string Title, string? ShortDescription, string? Description, Guid? CategoryId, string? Level, string? Language, double? EstimatedHours, string? DripMode);
public sealed record RejectRequest(string Comment);
public sealed record SectionRequest(string Title, string? Description, int SortOrder = 0, DateTime? DripUnlockAtUtc = null, int? DripDaysAfterEnrollment = null);
public sealed record LessonRequest(string Title, string LessonType, string? ContentMarkdown, string? ExternalUrl, int? DurationSeconds, int SortOrder = 0, bool IsFreePreview = false, Guid? PrerequisiteLessonId = null, DateTime? DripUnlockAtUtc = null, int? DripDaysAfterEnrollment = null, bool IsPublished = true);
/// <param name="RecordedAtUtc">Set by the app when it replays progress it saved while offline. An older record never moves the position back.</param>
public sealed record ProgressRequest(int LastPositionSeconds, int WatchedSecondsDelta, bool Completed, double? PlaybackSpeed, DateTime? RecordedAtUtc = null);

/// <summary>
/// Catalog, course builder, enrollment, lesson access and progress. Every query runs through the tenant query filter.
/// Write access: the course owner (an Instructor) or a TenantAdmin. Anyone else gets 404, so course existence is not disclosed.
/// Lesson access: Students need an active enrollment, or the lesson must be a free preview of a published course. Drip rules and
/// lesson prerequisites are enforced on the server. Co-instructors / teaching assistants are NOT built yet (CourseStaff has no endpoint).
/// </summary>
public sealed class LmsService(ApplicationDbContext db, ICurrentUser me, GamificationService xp, CertificateService certs, TransactionalMailer mail)
{
    private static DateTime Now => DateTime.UtcNow;
    private Guid Uid => me.UserId ?? throw new UnauthorizedAppException();
    private bool IsAdmin => me.IsInRole(RoleNames.TenantAdmin) || me.IsInRole(RoleNames.SuperAdmin);

    private static string Slugify(string s)
    {
        var x = Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return (x.Length == 0 ? "item" : x[..Math.Min(x.Length, 60)]) + "-" + Guid.NewGuid().ToString("N")[..6];
    }
    private static T ParseEnum<T>(string? v, T fallback) where T : struct, Enum =>
        v is null ? fallback : Enum.TryParse<T>(v, true, out var r) ? r : throw new ValidationAppException($"Unknown {typeof(T).Name}: {v}");

    // ---- categories ----------------------------------------------------------------------------------------------------
    public async Task<object> CategoriesAsync(CancellationToken ct) =>
        await db.Categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).Select(c => new { c.Id, c.ParentCategoryId, c.Name, c.Slug, c.SortOrder, c.IconName }).ToListAsync(ct);

    public async Task<Guid> CreateCategoryAsync(CategoryRequest r, CancellationToken ct)
    {
        if (r.ParentCategoryId is { } p && !await db.Categories.AnyAsync(c => c.Id == p, ct)) throw new NotFoundException("Parent category not found.");
        var c = new Category { TenantId = (db.CurrentTenantId ?? throw new ValidationAppException("A tenant is required.")), ParentCategoryId = r.ParentCategoryId, Name = r.Name.Trim(), Slug = Slugify(r.Name), SortOrder = r.SortOrder, IconName = r.IconName };
        db.Categories.Add(c); await db.SaveChangesAsync(ct); return c.Id;
    }
    public async Task UpdateCategoryAsync(Guid id, CategoryRequest r, CancellationToken ct)
    {
        var c = await db.Categories.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Category not found.");
        if (r.ParentCategoryId == id) throw new ValidationAppException("A category cannot be its own parent.");
        c.Name = r.Name.Trim(); c.ParentCategoryId = r.ParentCategoryId; c.SortOrder = r.SortOrder; c.IconName = r.IconName;
        await db.SaveChangesAsync(ct);
    }
    public async Task DeleteCategoryAsync(Guid id, CancellationToken ct)
    {
        var c = await db.Categories.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Category not found.");
        db.Categories.Remove(c); await db.SaveChangesAsync(ct);
    }

    // ---- courses -------------------------------------------------------------------------------------------------------
    private CourseAccess acc => new(db, me);
    /// <summary>Authoring access: owner, TenantAdmin or a CoInstructor. ownerOnly is for deleting or submitting the course.</summary>
    private async Task<Course> OwnedCourseAsync(Guid id, CancellationToken ct, bool ownerOnly = false)
    {
        var c = await db.Courses.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null || !(ownerOnly ? acc.IsOwnerOrAdmin(c) : await acc.IsAuthorAsync(c, ct))) throw new NotFoundException("Course not found.");
        return c;
    }

    public async Task<object> SearchCoursesAsync(string? q, Guid? categoryId, string? level, string? language, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Courses.Where(c => c.Status == CourseStatus.Published);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(c => c.Title.Contains(q) || (c.ShortDescription != null && c.ShortDescription.Contains(q)));
        if (categoryId is { } cat) query = query.Where(c => c.CategoryId == cat);
        if (level is not null) { var l = ParseEnum<CourseLevel>(level, CourseLevel.AllLevels); query = query.Where(c => c.Level == l); }
        if (language is not null) query = query.Where(c => c.Language == language);
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(c => c.PublishedAtUtc).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new { c.Id, c.Title, c.Slug, c.ShortDescription, c.CategoryId, Level = c.Level.ToString(), c.Language, c.EstimatedHours, PricingModel = c.PricingModel.ToString(), c.Price, c.Currency }).ToListAsync(ct);
        return new { totalCount = total, page, pageSize, items };
    }

    public async Task<object> CourseDetailAsync(Guid id, CancellationToken ct)
    {
        var c = await db.Courses.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Course not found.");
        var privileged = me.IsAuthenticated && await acc.IsGraderAsync(c, ct);
        if (c.Status != CourseStatus.Published && !privileged) throw new NotFoundException("Course not found.");
        var sections = await db.Sections.Where(s => s.CourseId == id).OrderBy(s => s.SortOrder).ToListAsync(ct);
        var lessons = await db.Lessons.Where(l => l.CourseId == id && (privileged || l.IsPublished)).OrderBy(l => l.SortOrder).ToListAsync(ct);
        var ratingsCount = await db.CourseReviews.CountAsync(r => r.CourseId == id && r.Status == CourseReviewStatus.Published, ct);
        double? averageRating = ratingsCount == 0 ? null : Math.Round(await db.CourseReviews.Where(r => r.CourseId == id && r.Status == CourseReviewStatus.Published).AverageAsync(r => (double)r.Rating, ct), 1);
        // The outline never carries lesson content or file ids. Content only comes from GET /lessons/{id}, which checks access.
        return new
        {
            c.Id, c.Title, c.Slug, c.ShortDescription, c.Description, Status = c.Status.ToString(), Level = c.Level.ToString(), c.Language, c.EstimatedHours,
            PricingModel = c.PricingModel.ToString(), c.Price, c.Currency, c.OwnerInstructorId, AverageRating = averageRating, RatingsCount = ratingsCount,
            Sections = sections.Select(s => new
            {
                s.Id, s.Title, s.Description, s.SortOrder,
                Lessons = lessons.Where(l => l.SectionId == s.Id).Select(l => new { l.Id, l.Title, LessonType = l.LessonType.ToString(), l.DurationSeconds, l.SortOrder, l.IsFreePreview })
            })
        };
    }

    public async Task<object> MyCoursesAsync(CancellationToken ct)
    {
        var q = db.Courses.AsQueryable();
        if (!IsAdmin) { var mineIds = acc.CourseIds(true); q = q.Where(c => mineIds.Contains(c.Id)); }
        return await q.OrderByDescending(c => c.CreatedAtUtc).Select(c => new { c.Id, c.Title, Status = c.Status.ToString(), c.OwnerInstructorId }).ToListAsync(ct);
    }

    public async Task<Guid> CreateCourseAsync(CourseRequest r, CancellationToken ct)
    {
        if (r.CategoryId is { } cat && !await db.Categories.AnyAsync(x => x.Id == cat, ct)) throw new NotFoundException("Category not found.");
        var c = new Course
        {
            TenantId = (db.CurrentTenantId ?? throw new ValidationAppException("A tenant is required.")), OwnerInstructorId = Uid, Title = r.Title.Trim(), Slug = Slugify(r.Title), ShortDescription = r.ShortDescription, Description = r.Description,
            CategoryId = r.CategoryId, Level = ParseEnum(r.Level, CourseLevel.AllLevels), Language = r.Language ?? "en", EstimatedHours = r.EstimatedHours,
            DripMode = ParseEnum(r.DripMode, CourseDripMode.None), Status = CourseStatus.Draft, PricingModel = CoursePricingModel.Free, Price = 0m
        };
        db.Courses.Add(c); await db.SaveChangesAsync(ct); return c.Id;
    }

    public async Task UpdateCourseAsync(Guid id, CourseRequest r, CancellationToken ct)
    {
        var c = await OwnedCourseAsync(id, ct);
        if (r.CategoryId is { } cat && !await db.Categories.AnyAsync(x => x.Id == cat, ct)) throw new NotFoundException("Category not found.");
        c.Title = r.Title.Trim(); c.ShortDescription = r.ShortDescription; c.Description = r.Description; c.CategoryId = r.CategoryId;
        c.Level = ParseEnum(r.Level, c.Level); c.Language = r.Language ?? c.Language; c.EstimatedHours = r.EstimatedHours; c.DripMode = ParseEnum(r.DripMode, c.DripMode);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteCourseAsync(Guid id, CancellationToken ct)
    {
        var c = await OwnedCourseAsync(id, ct, true);
        db.Courses.Remove(c); await db.SaveChangesAsync(ct);
    }

    public async Task SubmitAsync(Guid id, CancellationToken ct)
    {
        var c = await OwnedCourseAsync(id, ct, true);
        if (c.Status is not (CourseStatus.Draft or CourseStatus.Rejected)) throw new ConflictAppException("INVALID_STATE", "Only a draft or rejected course can be submitted.");
        if (!await db.Lessons.AnyAsync(l => l.CourseId == id, ct)) throw new ValidationAppException("Add at least one lesson before submitting.");
        c.Status = CourseStatus.PendingApproval;
        db.CourseApprovals.Add(new CourseApproval { TenantId = c.TenantId, CourseId = id, SubmittedBy = Uid, SubmittedAtUtc = Now, Decision = CourseApprovalDecision.Pending });
        await db.SaveChangesAsync(ct);
    }

    public async Task ReviewAsync(Guid id, bool approve, string? comment, CancellationToken ct)
    {
        var c = await db.Courses.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Course not found.");
        if (c.Status != CourseStatus.PendingApproval) throw new ConflictAppException("INVALID_STATE", "The course is not waiting for approval.");
        var a = await db.CourseApprovals.Where(x => x.CourseId == id && x.Decision == CourseApprovalDecision.Pending).OrderByDescending(x => x.SubmittedAtUtc).FirstOrDefaultAsync(ct);
        if (a is not null) { a.Decision = approve ? CourseApprovalDecision.Approved : CourseApprovalDecision.Rejected; a.ReviewedBy = Uid; a.ReviewedAtUtc = Now; a.Comment = comment; }
        c.Status = approve ? CourseStatus.Published : CourseStatus.Rejected;
        if (approve) c.PublishedAtUtc = Now;
        await db.SaveChangesAsync(ct);
        await mail.ApprovalResultAsync(c.OwnerInstructorId, c.Id, approve, comment, ct); // best-effort, never throws
    }

    // ---- sections and lessons ------------------------------------------------------------------------------------------
    public async Task<Guid> CreateSectionAsync(Guid courseId, SectionRequest r, CancellationToken ct)
    {
        var c = await OwnedCourseAsync(courseId, ct);
        var s = new Section { TenantId = c.TenantId, CourseId = courseId, Title = r.Title.Trim(), Description = r.Description, SortOrder = r.SortOrder, DripUnlockAtUtc = r.DripUnlockAtUtc, DripDaysAfterEnrollment = r.DripDaysAfterEnrollment };
        db.Sections.Add(s); await db.SaveChangesAsync(ct); return s.Id;
    }
    private async Task<(Section S, Course C)> OwnedSectionAsync(Guid id, CancellationToken ct)
    {
        var s = await db.Sections.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Section not found.");
        return (s, await OwnedCourseAsync(s.CourseId, ct));
    }
    public async Task UpdateSectionAsync(Guid id, SectionRequest r, CancellationToken ct)
    {
        var (s, _) = await OwnedSectionAsync(id, ct);
        s.Title = r.Title.Trim(); s.Description = r.Description; s.SortOrder = r.SortOrder; s.DripUnlockAtUtc = r.DripUnlockAtUtc; s.DripDaysAfterEnrollment = r.DripDaysAfterEnrollment;
        await db.SaveChangesAsync(ct);
    }
    public async Task DeleteSectionAsync(Guid id, CancellationToken ct)
    {
        var (s, _) = await OwnedSectionAsync(id, ct);
        db.Sections.Remove(s); await db.SaveChangesAsync(ct);
    }

    public async Task<Guid> CreateLessonAsync(Guid sectionId, LessonRequest r, CancellationToken ct)
    {
        var (s, c) = await OwnedSectionAsync(sectionId, ct);
        await CheckPrerequisiteAsync(r.PrerequisiteLessonId, c.Id, null, ct);
        var l = new Lesson
        {
            TenantId = c.TenantId, CourseId = c.Id, SectionId = s.Id, Title = r.Title.Trim(), LessonType = ParseEnum(r.LessonType, LessonType.Article), ContentMarkdown = r.ContentMarkdown,
            ExternalUrl = r.ExternalUrl, DurationSeconds = r.DurationSeconds, SortOrder = r.SortOrder, IsFreePreview = r.IsFreePreview, PrerequisiteLessonId = r.PrerequisiteLessonId,
            DripUnlockAtUtc = r.DripUnlockAtUtc, DripDaysAfterEnrollment = r.DripDaysAfterEnrollment, IsPublished = r.IsPublished
        };
        db.Lessons.Add(l); await db.SaveChangesAsync(ct); return l.Id;
    }
    private async Task CheckPrerequisiteAsync(Guid? prereq, Guid courseId, Guid? selfId, CancellationToken ct)
    {
        if (prereq is null) return;
        if (prereq == selfId) throw new ValidationAppException("A lesson cannot be its own prerequisite.");
        if (!await db.Lessons.AnyAsync(x => x.Id == prereq && x.CourseId == courseId, ct)) throw new ValidationAppException("The prerequisite must be a lesson of the same course.");
        // Walk the chain so a cycle can never lock students out.
        var seen = new HashSet<Guid>(); var cur = prereq;
        while (cur is { } id)
        {
            if (id == selfId || !seen.Add(id)) throw new ValidationAppException("Prerequisite chain would form a cycle.");
            cur = await db.Lessons.Where(x => x.Id == id).Select(x => x.PrerequisiteLessonId).FirstOrDefaultAsync(ct);
        }
    }
    public async Task UpdateLessonAsync(Guid id, LessonRequest r, CancellationToken ct)
    {
        var l = await db.Lessons.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Lesson not found.");
        await OwnedCourseAsync(l.CourseId, ct);
        await CheckPrerequisiteAsync(r.PrerequisiteLessonId, l.CourseId, l.Id, ct);
        l.Title = r.Title.Trim(); l.LessonType = ParseEnum(r.LessonType, l.LessonType); l.ContentMarkdown = r.ContentMarkdown; l.ExternalUrl = r.ExternalUrl; l.DurationSeconds = r.DurationSeconds;
        l.SortOrder = r.SortOrder; l.IsFreePreview = r.IsFreePreview; l.PrerequisiteLessonId = r.PrerequisiteLessonId; l.DripUnlockAtUtc = r.DripUnlockAtUtc; l.DripDaysAfterEnrollment = r.DripDaysAfterEnrollment; l.IsPublished = r.IsPublished;
        await db.SaveChangesAsync(ct);
    }
    public async Task DeleteLessonAsync(Guid id, CancellationToken ct)
    {
        var l = await db.Lessons.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Lesson not found.");
        await OwnedCourseAsync(l.CourseId, ct);
        db.Lessons.Remove(l); await db.SaveChangesAsync(ct);
    }

    // ---- enrollment ----------------------------------------------------------------------------------------------------
    private static bool Live(Enrollment e) => e.Status is EnrollmentStatus.Active or EnrollmentStatus.Completed && (e.ExpiresAtUtc is null || e.ExpiresAtUtc > Now);

    public async Task<Guid> EnrollAsync(Guid courseId, CancellationToken ct)
    {
        var c = await db.Courses.FirstOrDefaultAsync(x => x.Id == courseId && x.Status == CourseStatus.Published, ct) ?? throw new NotFoundException("Course not found.");
        // Paid courses: purchases, vouchers and wallet payments create the enrollment at fulfilment. Here only an unexpired subscription that covers the course can enroll.
        DateTime? subEnd = null;
        if (c.PricingModel != CoursePricingModel.Free)
        {
            var now = Now;
            var subs = await (from s in db.UserSubscriptions join p in db.SubscriptionPlans on s.PlanId equals p.Id
                              where s.UserId == Uid && s.CurrentPeriodEndUtc > now && (s.Status == UserSubscriptionStatus.Active || s.Status == UserSubscriptionStatus.Cancelled)
                              select new { s.CurrentPeriodEndUtc, p.Id, p.Scope }).ToListAsync(ct);
            foreach (var x in subs)
                if (x.Scope == SubscriptionPlanScope.AllCourses || await db.SubscriptionPlanCourses.AnyAsync(pc => pc.PlanId == x.Id && pc.CourseId == courseId, ct))
                    subEnd = subEnd is null || x.CurrentPeriodEndUtc > subEnd ? x.CurrentPeriodEndUtc : subEnd;
        }
        var existingPaid = c.PricingModel != CoursePricingModel.Free ? await db.Enrollments.FirstOrDefaultAsync(e => e.CourseId == courseId && e.UserId == Uid, ct) : null;
        if (c.PricingModel != CoursePricingModel.Free && subEnd is null && !(existingPaid is not null && Live(existingPaid)))
            throw new AppException(402, "PAYMENT_REQUIRED", "This course is not free. Buy it from the cart, redeem a voucher, or subscribe to a plan that includes it.");
        var existing = await db.Enrollments.FirstOrDefaultAsync(e => e.CourseId == courseId && e.UserId == Uid, ct);
        if (existing is null)
        {
            // Course prerequisites: every prerequisite course must be Completed by this student before a new enrollment.
            var need = await db.CoursePrerequisites.Where(p => p.CourseId == courseId).Select(p => p.PrerequisiteCourseId).ToListAsync(ct);
            if (need.Count > 0)
            {
                var done = await db.Enrollments.Where(e => e.UserId == Uid && need.Contains(e.CourseId) && e.Status == EnrollmentStatus.Completed).Select(e => e.CourseId).Distinct().CountAsync(ct);
                if (done < need.Count) throw new ForbiddenAppException("PREREQUISITE_NOT_MET", "Complete the prerequisite courses first.");
            }
        }
        if (existing is not null)
        {
            if (existing.Status == EnrollmentStatus.Revoked) throw new ForbiddenAppException("ENROLLMENT_REVOKED", "Your enrollment in this course was revoked.");
            if (subEnd is not null && !Live(existing) && existing.Status != EnrollmentStatus.Completed) { existing.Status = EnrollmentStatus.Active; existing.ExpiresAtUtc = subEnd; existing.Source = EnrollmentSource.Subscription; await db.SaveChangesAsync(ct); }
            return existing.Id;
        }
        var e = new Enrollment { TenantId = c.TenantId, UserId = Uid, CourseId = courseId, Source = subEnd is null ? EnrollmentSource.Free : EnrollmentSource.Subscription, ExpiresAtUtc = subEnd, Status = EnrollmentStatus.Active, EnrolledAtUtc = Now };
        db.Enrollments.Add(e);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) // a concurrent request created it first (unique index)
        {
            db.Entry(e).State = EntityState.Detached;
            return (await db.Enrollments.FirstAsync(x => x.CourseId == courseId && x.UserId == Uid, ct)).Id;
        }
        await mail.EnrollmentAsync(Uid, courseId, ct); // best-effort, never throws
        return e.Id;
    }

    public async Task<object> MyEnrollmentsAsync(CancellationToken ct) =>
        await (from e in db.Enrollments join c in db.Courses on e.CourseId equals c.Id where e.UserId == Uid && e.Status != EnrollmentStatus.Revoked
               select new { e.Id, e.CourseId, c.Title, Status = e.Status.ToString(), e.ProgressPercent, e.EnrolledAtUtc, e.ExpiresAtUtc }).ToListAsync(ct);

    // ---- lesson access -------------------------------------------------------------------------------------------------
    public async Task<object> GetLessonAsync(Guid id, CancellationToken ct)
    {
        var l = await db.Lessons.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Lesson not found.");
        var c = await db.Courses.FirstAsync(x => x.Id == l.CourseId, ct);
        if (await acc.IsGraderAsync(c, ct)) return LessonDto(l);
        if (me.IsInRole(RoleNames.Instructor) || me.IsInRole(RoleNames.TeachingAssistant)) throw new NotFoundException("Lesson not found.");
        if (c.Status != CourseStatus.Published || !l.IsPublished) throw new NotFoundException("Lesson not found.");
        var enr = await db.Enrollments.FirstOrDefaultAsync(e => e.CourseId == c.Id && e.UserId == Uid, ct);
        var enrolled = enr is not null && Live(enr);
        if (!enrolled && !l.IsFreePreview)
        {
            var nowG = Now;
            if (await db.LessonAccessGrants.AnyAsync(g => g.UserId == Uid && g.LessonId == id && (g.ExpiresAtUtc == null || g.ExpiresAtUtc > nowG), ct)) return LessonDto(l); // pay-per-lesson
        }
        if (!enrolled && !l.IsFreePreview) throw new ForbiddenAppException("NOT_ENROLLED", "Enroll in this course to open this lesson.");
        if (enrolled)
        {
            var section = await db.Sections.FirstAsync(s => s.Id == l.SectionId, ct);
            var unlock = UnlockAt(c, section, l, enr!.EnrolledAtUtc);
            if (unlock is { } u && u > Now) throw new ForbiddenAppException("LESSON_LOCKED_DRIP", $"This lesson unlocks at {u:O}.");
            if (l.PrerequisiteLessonId is { } p)
            {
                var done = await db.LessonProgress.AnyAsync(x => x.UserId == Uid && x.LessonId == p && x.Status == LessonProgressStatus.Completed, ct);
                if (!done) throw new ForbiddenAppException("PREREQUISITE_NOT_COMPLETED", "Complete the prerequisite lesson first.");
            }
        }
        return LessonDto(l);
    }

    private static DateTime? UnlockAt(Course c, Section s, Lesson l, DateTime enrolledAt)
    {
        DateTime? latest = null;
        void Take(DateTime? d) { if (d is not null && (latest is null || d > latest)) latest = d; }
        if (c.DripMode == CourseDripMode.ByDate) { Take(s.DripUnlockAtUtc); Take(l.DripUnlockAtUtc); }
        if (c.DripMode == CourseDripMode.DaysAfterEnrollment)
        {
            if (s.DripDaysAfterEnrollment is { } sd) Take(enrolledAt.AddDays(sd));
            if (l.DripDaysAfterEnrollment is { } ld) Take(enrolledAt.AddDays(ld));
        }
        return latest;
    }

    private static object LessonDto(Lesson l) => new { l.Id, l.CourseId, l.SectionId, l.Title, LessonType = l.LessonType.ToString(), l.ContentMarkdown, l.ExternalUrl, l.DurationSeconds, l.IsFreePreview, l.PrerequisiteLessonId };

    // ---- progress ------------------------------------------------------------------------------------------------------
    public async Task<object> SaveProgressAsync(Guid lessonId, ProgressRequest r, CancellationToken ct)
    {
        await GetLessonAsync(lessonId, ct); // same access rules as reading the lesson
        var l = await db.Lessons.FirstAsync(x => x.Id == lessonId, ct);
        var enr = await db.Enrollments.FirstOrDefaultAsync(e => e.CourseId == l.CourseId && e.UserId == Uid, ct);
        if (enr is null || !Live(enr)) throw new ForbiddenAppException("NOT_ENROLLED", "Progress is only recorded for enrolled students.");
        // Watched seconds decide which lessons qualify for subscription revenue (PHASE4.md: qualified learning time), so every heartbeat, live or replayed, is validated on the server:
        //  - credit is whole seconds and never more than the wall-clock time since the lesson's accounting clock (LastAccessedAtUtc), which advances only by the seconds
        //    actually credited, so fractions of a second are carried to the next heartbeat instead of being rounded up or thrown away. Sum of credits <= real elapsed time, however many requests arrive;
        //  - if the client claims fewer seconds than elapsed the rest was idle time and the clock jumps to the heartbeat's time;
        //  - the first heartbeat of a lesson has no baseline and credits at most FirstHeartbeatSeconds (the player sends 60 s at most);
        //  - an offline replay is an idempotent event: RecordedAtUtc must be newer than the clock or it credits and moves nothing, so a retry after a lost response is ignored.
        //    A recorded time more than 2 minutes in the future is not trusted (credits and moves nothing); up to 2 minutes ahead it is stored unchanged so its retry is ignored too;
        //  - the update is a compare-and-swap on the clock, so concurrent requests cannot both credit the same elapsed time: the loser re-reads and recomputes.
        const int FirstHeartbeatSeconds = 60, ClockToleranceMinutes = 2, MaxAttempts = 6;
        var now = Now;
        var replay = r.RecordedAtUtc is not null;
        var trusted = !replay || r.RecordedAtUtc!.Value <= now.AddMinutes(ClockToleranceMinutes);
        var recorded = replay && trusted ? r.RecordedAtUtc!.Value : now;
        var claimed = Math.Clamp(r.WatchedSecondsDelta, 0, 300); // a single heartbeat cannot claim more than 5 minutes
        Guid progressId = Guid.Empty;
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var p = await db.LessonProgress.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == Uid && x.LessonId == lessonId, ct);
            if (p is null)
            {
                if (replay && !trusted) break; // an untrusted record cannot create the row
                var fresh = new LessonProgress { TenantId = l.TenantId, UserId = Uid, LessonId = lessonId, CourseId = l.CourseId, Status = LessonProgressStatus.InProgress,
                    WatchedSeconds = Math.Min(claimed, FirstHeartbeatSeconds), LastPositionSeconds = Math.Max(0, r.LastPositionSeconds), PlaybackSpeed = r.PlaybackSpeed, LastAccessedAtUtc = recorded };
                db.LessonProgress.Add(fresh);
                try { await db.SaveChangesAsync(ct); progressId = fresh.Id; break; }
                catch (DbUpdateException) { db.Entry(fresh).State = EntityState.Detached; continue; } // two first heartbeats raced: re-read and take the normal path
            }
            progressId = p.Id;
            var old = p.LastAccessedAtUtc;
            var stale = replay && (!trusted || recorded <= old);
            if (stale) break;
            var elapsed = Math.Max(0, (recorded - old).TotalSeconds);
            var allowed = (int)Math.Floor(elapsed);
            var credit = Math.Min(claimed, allowed);
            var newClock = credit >= allowed ? old.AddSeconds(credit) : recorded; // claimed >= allowed: keep the fraction; claimed < allowed: the rest was idle
            var pos = Math.Max(0, r.LastPositionSeconds); var speed = r.PlaybackSpeed;
            var n = await db.LessonProgress.Where(x => x.Id == p.Id && x.LastAccessedAtUtc == old)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.WatchedSeconds, x => x.WatchedSeconds + credit).SetProperty(x => x.LastPositionSeconds, pos)
                    .SetProperty(x => x.PlaybackSpeed, speed).SetProperty(x => x.LastAccessedAtUtc, newClock), ct);
            if (n == 1) break; // otherwise another request changed the clock first: loop, re-read and recompute against the new clock
        }
        if (progressId != Guid.Empty)
            await db.LessonProgress.Where(x => x.Id == progressId && x.Status == LessonProgressStatus.NotStarted)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, LessonProgressStatus.InProgress), ct);
        else
        {
            // no row yet and the record was untrusted: nothing credited; completion below still needs a row, so create an empty one
            var empty = new LessonProgress { TenantId = l.TenantId, UserId = Uid, LessonId = lessonId, CourseId = l.CourseId, Status = LessonProgressStatus.InProgress, LastAccessedAtUtc = now };
            db.LessonProgress.Add(empty);
            try { await db.SaveChangesAsync(ct); progressId = empty.Id; }
            catch (DbUpdateException) { db.Entry(empty).State = EntityState.Detached; progressId = (await db.LessonProgress.AsNoTracking().FirstAsync(x => x.UserId == Uid && x.LessonId == lessonId, ct)).Id; }
        }
        if (r.Completed)
        {
            // Completion is claimed atomically, so XP is awarded once even if the heartbeat is sent twice at the same time.
            var when = Now;
            var won = await db.LessonProgress.Where(x => x.Id == progressId && x.Status != LessonProgressStatus.Completed)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, LessonProgressStatus.Completed).SetProperty(x => x.CompletedAtUtc, when), ct);
            if (won == 1) await xp.AwardAsync(l.TenantId, Uid, GamificationService.LessonXp, "LessonCompleted", "Lesson", lessonId, ct);
        }
        return await RecalculateAsync(enr.Id, ct);
    }

    private async Task<object> RecalculateAsync(Guid enrollmentId, CancellationToken ct)
    {
        var enr = await db.Enrollments.AsNoTracking().FirstAsync(x => x.Id == enrollmentId, ct);
        var total = await db.Lessons.CountAsync(x => x.CourseId == enr.CourseId && x.IsPublished, ct);
        var done = await db.LessonProgress.CountAsync(x => x.UserId == enr.UserId && x.CourseId == enr.CourseId && x.Status == LessonProgressStatus.Completed, ct);
        var pct = total == 0 ? 0 : Math.Round(100.0 * Math.Min(done, total) / total, 1);
        await db.Enrollments.Where(x => x.Id == enrollmentId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ProgressPercent, pct), ct);
        var status = enr.Status;
        if (total > 0 && done >= total && enr.Status == EnrollmentStatus.Active)
        {
            var when = Now;
            // Only the request that flips Active to Completed issues the certificate and the course XP.
            var won = await db.Enrollments.Where(x => x.Id == enrollmentId && x.Status == EnrollmentStatus.Active)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, EnrollmentStatus.Completed).SetProperty(x => x.CompletedAtUtc, when), ct);
            status = EnrollmentStatus.Completed;
            if (won == 1)
            {
                await xp.AwardAsync(enr.TenantId, enr.UserId, GamificationService.CourseXp, "CourseCompleted", "Course", enr.CourseId, ct);
                await certs.EnsureIssuedAsync(enr.TenantId, enr.UserId, enr.CourseId, ct);
                await mail.CourseCompletedAsync(enr.UserId, enr.CourseId, ct); // best-effort, never throws
            }
        }
        return new { courseId = enr.CourseId, completedLessons = done, totalLessons = total, progressPercent = pct, status = status.ToString() };
    }

    public async Task<object> CourseProgressAsync(Guid courseId, CancellationToken ct)
    {
        var enr = await db.Enrollments.FirstOrDefaultAsync(e => e.CourseId == courseId && e.UserId == Uid, ct) ?? throw new NotFoundException("You are not enrolled in this course.");
        var lessons = await db.LessonProgress.Where(x => x.UserId == Uid && x.CourseId == courseId)
            .Select(x => new { x.LessonId, Status = x.Status.ToString(), x.LastPositionSeconds, x.WatchedSeconds }).ToListAsync(ct);
        return new { courseId, enr.ProgressPercent, Status = enr.Status.ToString(), lessons };
    }
}
