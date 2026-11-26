using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Entities;
using SmartLMS.Domain.Enums;
using SmartLMS.Domain.Exceptions;
using SmartLMS.Infrastructure.Persistence;

namespace SmartLMS.Infrastructure.Services.Lms;

public sealed record NoteRequest(int? TimestampSeconds, string Body);
public sealed record BookmarkRequest(int TimestampSeconds, string? Label);
public sealed record StudySessionRequest(Guid? CourseId, Guid? LessonId, DateTime StartedAtUtc, int DurationSeconds);
public sealed record PathRequest(string Title, string? Description, string? Status, bool IssuesCertificate, List<Guid>? CourseIds);
public sealed record ReviewUpsertRequest(int Rating, string? Body);
public sealed record ReviewReportRequest(string Reason);
public sealed record ThreadRequest(string Title, string Body, Guid? LessonId);
public sealed record PostRequest(string Body, Guid? ParentPostId);
public sealed record AnnouncementRequest(Guid? CourseId, string Title, string Body);
public sealed record ChatSendRequest(string Body);
public sealed record PushTokenRequest(string FcmToken, string Platform);
public sealed record LiveSessionRequest(string Title, string Provider, string JoinUrl, DateTime StartsAtUtc, DateTime? EndsAtUtc);

/// <summary>
/// Notes, bookmarks, study sessions, learning paths, discussions, announcements, notifications, chat and live-class scheduling.
/// Course-scoped data is reachable only by the course owner, a TenantAdmin, or a student with a live enrollment. Personal data
/// (notes, bookmarks, notifications) is always filtered by the caller's own user id. Not built: real-time delivery (SignalR hubs), FCM sending, Zoom/Meet APIs.
/// </summary>
public sealed class CommunityService(ApplicationDbContext db, ICurrentUser me, IRealtime rt, Notifier notifier)
{
    private static DateTime Now => DateTime.UtcNow;
    private Guid Uid => me.UserId ?? throw new UnauthorizedAppException();
    private bool IsAdmin => me.IsInRole(RoleNames.TenantAdmin) || me.IsInRole(RoleNames.SuperAdmin);
    private Guid Tid => db.CurrentTenantId ?? throw new ValidationAppException("A tenant is required.");
    private static string Clean(string s, int max, string field)
    {
        s = s?.Trim() ?? "";
        if (s.Length == 0) throw new ValidationAppException($"{field} is required.");
        if (s.Length > max) throw new ValidationAppException($"{field} is too long (max {max}).");
        return s;
    }

    /// <summary>Owner/admin staff, or a student with a live enrollment. Everyone else gets 404 so the course is not disclosed.</summary>
    private async Task<(Course C, bool Staff)> CourseAccessAsync(Guid courseId, CancellationToken ct)
    {
        var c = await db.Courses.FirstOrDefaultAsync(x => x.Id == courseId, ct) ?? throw new NotFoundException("Course not found.");
        if (await new CourseAccess(db, me).IsGraderAsync(c, ct)) return (c, true);
        var e = await db.Enrollments.FirstOrDefaultAsync(x => x.CourseId == courseId && x.UserId == Uid, ct);
        if (e is null || e.Status is EnrollmentStatus.Revoked or EnrollmentStatus.Expired || (e.ExpiresAtUtc is { } x && x <= Now)) throw new NotFoundException("Course not found.");
        return (c, false);
    }
    private async Task<Lesson> LessonForStudentAsync(Guid lessonId, CancellationToken ct)
    {
        var l = await db.Lessons.FirstOrDefaultAsync(x => x.Id == lessonId, ct) ?? throw new NotFoundException("Lesson not found.");
        await CourseAccessAsync(l.CourseId, ct); return l;
    }

    // ---- notes, bookmarks, study sessions --------------------------------------------------------------------------------
    public async Task<object> NotesAsync(Guid lessonId, CancellationToken ct) { await LessonForStudentAsync(lessonId, ct); return await db.LessonNotes.Where(n => n.LessonId == lessonId && n.UserId == Uid).OrderBy(n => n.TimestampSeconds).Select(n => new { n.Id, n.TimestampSeconds, n.Body, n.CreatedAtUtc }).ToListAsync(ct); }
    public async Task<Guid> AddNoteAsync(Guid lessonId, NoteRequest r, CancellationToken ct)
    {
        await LessonForStudentAsync(lessonId, ct);
        var n = new LessonNote { TenantId = Tid, UserId = Uid, LessonId = lessonId, TimestampSeconds = r.TimestampSeconds is < 0 ? null : r.TimestampSeconds, Body = Clean(r.Body, 4000, "Body"), CreatedAtUtc = Now };
        db.LessonNotes.Add(n); await db.SaveChangesAsync(ct); return n.Id;
    }
    public async Task EditNoteAsync(Guid id, NoteRequest r, CancellationToken ct)
    {
        var n = await db.LessonNotes.FirstOrDefaultAsync(x => x.Id == id && x.UserId == Uid, ct) ?? throw new NotFoundException("Note not found.");
        n.Body = Clean(r.Body, 4000, "Body"); n.TimestampSeconds = r.TimestampSeconds; await db.SaveChangesAsync(ct);
    }
    public async Task DeleteNoteAsync(Guid id, CancellationToken ct)
    {
        var n = await db.LessonNotes.FirstOrDefaultAsync(x => x.Id == id && x.UserId == Uid, ct) ?? throw new NotFoundException("Note not found.");
        db.LessonNotes.Remove(n); await db.SaveChangesAsync(ct);
    }
    public async Task<object> BookmarksAsync(Guid lessonId, CancellationToken ct) { await LessonForStudentAsync(lessonId, ct); return await db.LessonBookmarks.Where(n => n.LessonId == lessonId && n.UserId == Uid).OrderBy(n => n.TimestampSeconds).Select(n => new { n.Id, n.TimestampSeconds, n.Label }).ToListAsync(ct); }
    public async Task<Guid> AddBookmarkAsync(Guid lessonId, BookmarkRequest r, CancellationToken ct)
    {
        await LessonForStudentAsync(lessonId, ct);
        if (r.TimestampSeconds < 0) throw new ValidationAppException("TimestampSeconds must not be negative.");
        var b = new LessonBookmark { TenantId = Tid, UserId = Uid, LessonId = lessonId, TimestampSeconds = r.TimestampSeconds, Label = r.Label?.Trim(), CreatedAtUtc = Now };
        db.LessonBookmarks.Add(b); await db.SaveChangesAsync(ct); return b.Id;
    }
    public async Task DeleteBookmarkAsync(Guid id, CancellationToken ct)
    {
        var b = await db.LessonBookmarks.FirstOrDefaultAsync(x => x.Id == id && x.UserId == Uid, ct) ?? throw new NotFoundException("Bookmark not found.");
        db.LessonBookmarks.Remove(b); await db.SaveChangesAsync(ct);
    }
    public async Task RecordStudyAsync(StudySessionRequest r, CancellationToken ct)
    {
        // The client cannot claim more than 4 hours per session or a session that starts in the future or more than a day ago.
        if (r.DurationSeconds is <= 0 or > 4 * 3600) throw new ValidationAppException("DurationSeconds must be between 1 and 14400.");
        if (r.StartedAtUtc > Now.AddMinutes(5) || r.StartedAtUtc < Now.AddDays(-1)) throw new ValidationAppException("StartedAtUtc must be within the last 24 hours.");
        if (r.CourseId is { } c) await CourseAccessAsync(c, ct);
        db.StudySessions.Add(new StudySession { TenantId = Tid, UserId = Uid, CourseId = r.CourseId, LessonId = r.LessonId, StartedAtUtc = r.StartedAtUtc, DurationSeconds = r.DurationSeconds });
        await db.SaveChangesAsync(ct);
    }

    // ---- learning paths --------------------------------------------------------------------------------------------------
    public async Task<object> PathsAsync(CancellationToken ct) => await db.LearningPaths.Where(p => p.Status == LearningPathStatus.Published).OrderBy(p => p.Title).Select(p => new { p.Id, p.Title, p.Slug, p.Description }).ToListAsync(ct);
    /// <summary>Staff list including drafts. Admin sees all tenant paths; an instructor only paths they created (same ownership rule as UpdatePathAsync).</summary>
    public async Task<object> ManagePathsAsync(CancellationToken ct)
    {
        var q = db.LearningPaths.AsQueryable();
        if (!IsAdmin) q = q.Where(p => p.CreatedBy == Uid);
        return await q.OrderBy(p => p.Title).Select(p => new { p.Id, p.Title, p.Slug, p.Description, Status = p.Status.ToString(), p.IssuesCertificate }).ToListAsync(ct);
    }
    public async Task<object> PathAsync(Guid id, CancellationToken ct)
    {
        var p = await db.LearningPaths.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Path not found.");
        if (p.Status != LearningPathStatus.Published && !(me.IsAuthenticated && (IsAdmin || me.IsInRole(RoleNames.Instructor)))) throw new NotFoundException("Path not found.");
        var courses = await (from lc in db.LearningPathCourses join c in db.Courses on lc.CourseId equals c.Id where lc.LearningPathId == id && c.Status == CourseStatus.Published orderby lc.SortOrder select new { c.Id, c.Title, lc.SortOrder, lc.IsMandatory }).ToListAsync(ct);
        return new { p.Id, p.Title, p.Description, Status = p.Status.ToString(), p.IssuesCertificate, courses };
    }
    private async Task ReplacePathCoursesAsync(LearningPath p, List<Guid>? ids, CancellationToken ct)
    {
        if (ids is null) return;
        if (ids.Distinct().Count() != ids.Count) throw new ValidationAppException("A course can appear once per path.");
        foreach (var cid in ids)
        {
            var c = await db.Courses.FirstOrDefaultAsync(x => x.Id == cid, ct);
            if (c is null || !await new CourseAccess(db, me).IsAuthorAsync(c, ct)) throw new NotFoundException("Course not found.");
        }
        var existing = await db.LearningPathCourses.Where(x => x.LearningPathId == p.Id).ToListAsync(ct);
        var mandatory = existing.ToDictionary(x => x.CourseId, x => x.IsMandatory); // the DTO carries no per-course flag; keep the previous one on re-linked courses
        db.LearningPathCourses.RemoveRange(existing);
        await db.SaveChangesAsync(ct);
        var i = 0; foreach (var cid in ids) db.LearningPathCourses.Add(new LearningPathCourse { TenantId = p.TenantId, LearningPathId = p.Id, CourseId = cid, SortOrder = i++, IsMandatory = mandatory.GetValueOrDefault(cid), CreatedAtUtc = Now });
    }
    public async Task<Guid> CreatePathAsync(PathRequest r, CancellationToken ct)
    {
        var status = Enum.TryParse<LearningPathStatus>(r.Status ?? "Draft", true, out var s) ? s : throw new ValidationAppException("Unknown status.");
        if (status == LearningPathStatus.Published && !IsAdmin) throw new ForbiddenAppException("ADMIN_ONLY", "Only a TenantAdmin can publish a path.");
        var p = new LearningPath { TenantId = Tid, Title = Clean(r.Title, 200, "Title"), Slug = Regex.Replace(r.Title.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-') + "-" + Guid.NewGuid().ToString("N")[..6], Description = r.Description, Status = status, IssuesCertificate = r.IssuesCertificate, CreatedAtUtc = Now };
        db.LearningPaths.Add(p); await db.SaveChangesAsync(ct);
        await ReplacePathCoursesAsync(p, r.CourseIds, ct); await db.SaveChangesAsync(ct); return p.Id;
    }
    public async Task UpdatePathAsync(Guid id, PathRequest r, CancellationToken ct)
    {
        var p = await db.LearningPaths.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Path not found.");
        if (!IsAdmin && !(await db.LearningPathCourses.Where(x => x.LearningPathId == id).AnyAsync(ct) is false && p.Status == LearningPathStatus.Draft && p.CreatedBy == Uid) && p.CreatedBy != Uid) throw new NotFoundException("Path not found.");
        var status = Enum.TryParse<LearningPathStatus>(r.Status ?? p.Status.ToString(), true, out var s) ? s : throw new ValidationAppException("Unknown status.");
        if (status == LearningPathStatus.Published && !IsAdmin) throw new ForbiddenAppException("ADMIN_ONLY", "Only a TenantAdmin can publish a path.");
        p.Title = Clean(r.Title, 200, "Title"); p.Description = r.Description; p.Status = status; p.IssuesCertificate = r.IssuesCertificate;
        await ReplacePathCoursesAsync(p, r.CourseIds, ct); await db.SaveChangesAsync(ct);
    }
    public async Task DeletePathAsync(Guid id, CancellationToken ct)
    {
        var p = await db.LearningPaths.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Path not found.");
        db.LearningPaths.Remove(p); await db.SaveChangesAsync(ct);
    }
    public async Task<Guid> EnrollPathAsync(Guid id, CancellationToken ct)
    {
        var p = await db.LearningPaths.FirstOrDefaultAsync(x => x.Id == id && x.Status == LearningPathStatus.Published, ct) ?? throw new NotFoundException("Path not found.");
        if (p.Price > 0) throw new AppException(402, "PAYMENT_REQUIRED", "This path is not free. Paid enrollment arrives with the payment phase.");
        var existing = await db.LearningPathEnrollments.FirstOrDefaultAsync(e => e.LearningPathId == id && e.UserId == Uid, ct);
        if (existing is not null) { if (existing.Status == LearningPathEnrollmentStatus.Revoked) throw new ForbiddenAppException("ENROLLMENT_REVOKED", "Your enrollment was revoked."); return existing.Id; }
        var pe = new LearningPathEnrollment { TenantId = p.TenantId, UserId = Uid, LearningPathId = id, EnrolledAtUtc = Now, CreatedAtUtc = Now };
        db.LearningPathEnrollments.Add(pe);
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { db.Entry(pe).State = EntityState.Detached; return (await db.LearningPathEnrollments.FirstAsync(e => e.LearningPathId == id && e.UserId == Uid, ct)).Id; }
        // Course access for a path is granted only for free courses here. Paid courses in the path still need their own entitlement (Phase 4).
        foreach (var cid in await (from lc in db.LearningPathCourses join c in db.Courses on lc.CourseId equals c.Id where lc.LearningPathId == id && c.Status == CourseStatus.Published && c.PricingModel == CoursePricingModel.Free select c.Id).ToListAsync(ct))
            if (!await db.Enrollments.AnyAsync(e => e.UserId == Uid && e.CourseId == cid, ct)) db.Enrollments.Add(new Enrollment { TenantId = p.TenantId, UserId = Uid, CourseId = cid, Source = EnrollmentSource.Path, EnrolledAtUtc = Now });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { db.ChangeTracker.Clear(); }
        return pe.Id;
    }

    // ---- discussions -----------------------------------------------------------------------------------------------------
    public async Task<object> ThreadsAsync(Guid courseId, Guid? lessonId, CancellationToken ct)
    {
        await CourseAccessAsync(courseId, ct);
        var q = db.DiscussionThreads.Where(t => t.CourseId == courseId); if (lessonId is { } l) q = q.Where(t => t.LessonId == l);
        return await q.OrderByDescending(t => t.IsPinned).ThenByDescending(t => t.CreatedAtUtc).Select(t => new { t.Id, t.Title, t.LessonId, ThreadType = t.ThreadType.ToString(), t.IsPinned, t.IsLocked, t.UpvoteCount, t.CreatedAtUtc, authorName = db.Users.Where(u => u.Id == t.AuthorId).Select(u => u.FullName).FirstOrDefault() }).ToListAsync(ct);
    }
    public async Task<Guid> CreateThreadAsync(Guid courseId, ThreadRequest r, CancellationToken ct)
    {
        await CourseAccessAsync(courseId, ct);
        if (r.LessonId is { } l && !await db.Lessons.AnyAsync(x => x.Id == l && x.CourseId == courseId, ct)) throw new NotFoundException("Lesson not found.");
        var t = new DiscussionThread { TenantId = Tid, CourseId = courseId, LessonId = r.LessonId, AuthorId = Uid, ThreadType = r.LessonId is null ? DiscussionThreadThreadType.Forum : DiscussionThreadThreadType.LessonQA, Title = Clean(r.Title, 300, "Title"), Body = Clean(r.Body, 8000, "Body"), CreatedAtUtc = Now };
        db.DiscussionThreads.Add(t); await db.SaveChangesAsync(ct); return t.Id;
    }
    private async Task<(DiscussionThread T, bool Staff)> ThreadAccessAsync(Guid id, CancellationToken ct)
    {
        var t = await db.DiscussionThreads.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Thread not found.");
        var (_, staff) = await CourseAccessAsync(t.CourseId, ct); return (t, staff);
    }
    public async Task<object> ThreadAsync(Guid id, CancellationToken ct)
    {
        var (t, _) = await ThreadAccessAsync(id, ct);
        var posts = await db.DiscussionPosts.Where(p => p.ThreadId == id).OrderBy(p => p.CreatedAtUtc).Select(p => new { p.Id, p.ParentPostId, p.Body, p.IsInstructorPinned, p.UpvoteCount, p.CreatedAtUtc, authorName = db.Users.Where(u => u.Id == p.AuthorId).Select(u => u.FullName).FirstOrDefault() }).ToListAsync(ct);
        return new { t.Id, t.Title, t.Body, t.IsPinned, t.IsLocked, t.UpvoteCount, t.CreatedAtUtc, posts };
    }
    public async Task<Guid> ReplyAsync(Guid threadId, PostRequest r, CancellationToken ct)
    {
        var (t, staff) = await ThreadAccessAsync(threadId, ct);
        if (t.IsLocked && !staff) throw new ForbiddenAppException("THREAD_LOCKED", "This thread is locked.");
        if (r.ParentPostId is { } pp && !await db.DiscussionPosts.AnyAsync(x => x.Id == pp && x.ThreadId == threadId, ct)) throw new NotFoundException("Parent post not found.");
        var p = new DiscussionPost { TenantId = Tid, ThreadId = threadId, AuthorId = Uid, ParentPostId = r.ParentPostId, Body = Clean(r.Body, 8000, "Body"), CreatedAtUtc = Now };
        db.DiscussionPosts.Add(p); await db.SaveChangesAsync(ct);
        if (t.AuthorId != Uid) await notifier.SendAsync(Tid, [t.AuthorId], "ThreadReply", "New reply to your thread", t.Title.Length > 100 ? t.Title[..100] : t.Title, ct);
        return p.Id;
    }
    // Two simultaneous toggles by one user can both try to add (unique index) or both remove the same row.
    // The other request already did the work, so the loser is not an error: the count is recomputed from the rows afterwards.
    private async Task SaveVoteAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException) { db.ChangeTracker.Clear(); }
    }
    public async Task<object> UpvoteThreadAsync(Guid id, CancellationToken ct)
    {
        var (t, _) = await ThreadAccessAsync(id, ct);
        var v = await db.DiscussionVotes.FirstOrDefaultAsync(x => x.ThreadId == id && x.UserId == Uid, ct);
        if (v is null) db.DiscussionVotes.Add(new DiscussionVote { TenantId = Tid, UserId = Uid, ThreadId = id, CreatedAtUtc = Now }); else db.DiscussionVotes.Remove(v);
        await SaveVoteAsync(ct);
        // Recount from the votes so a double click or a race cannot make the counter drift.
        var n = await db.DiscussionVotes.Where(x => x.ThreadId == id).Select(x => x.UserId).Distinct().CountAsync(ct);
        await db.DiscussionThreads.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.UpvoteCount, n), ct);
        return new { upvotes = n, voted = v is null };
    }
    public async Task<object> UpvotePostAsync(Guid id, CancellationToken ct)
    {
        var p = await db.DiscussionPosts.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Post not found.");
        await ThreadAccessAsync(p.ThreadId, ct);
        var v = await db.DiscussionVotes.FirstOrDefaultAsync(x => x.PostId == id && x.UserId == Uid, ct);
        if (v is null) db.DiscussionVotes.Add(new DiscussionVote { TenantId = Tid, UserId = Uid, PostId = id, CreatedAtUtc = Now }); else db.DiscussionVotes.Remove(v);
        await SaveVoteAsync(ct);
        var n = await db.DiscussionVotes.Where(x => x.PostId == id).Select(x => x.UserId).Distinct().CountAsync(ct);
        await db.DiscussionPosts.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.UpvoteCount, n), ct);
        return new { upvotes = n, voted = v is null };
    }
    public async Task PinAsync(Guid threadId, CancellationToken ct)
    {
        var (t, staff) = await ThreadAccessAsync(threadId, ct);
        if (!staff) throw new NotFoundException("Thread not found.");
        t.IsPinned = !t.IsPinned; await db.SaveChangesAsync(ct);
    }

    // ---- announcements and notifications ---------------------------------------------------------------------------------
    public async Task<Guid> AnnounceAsync(AnnouncementRequest r, CancellationToken ct)
    {
        if (r.CourseId is { } cid) { var (_, staff) = await CourseAccessAsync(cid, ct); if (!staff) throw new NotFoundException("Course not found."); }
        else if (!IsAdmin) throw new ForbiddenAppException("ADMIN_ONLY", "Only a TenantAdmin can post tenant-wide announcements.");
        var a = new Announcement { TenantId = Tid, CourseId = r.CourseId, AuthorId = Uid, Title = Clean(r.Title, 300, "Title"), Body = Clean(r.Body, 8000, "Body"), PublishedAtUtc = Now, CreatedAtUtc = Now };
        db.Announcements.Add(a);
        var recipients = r.CourseId is { } c
            ? await db.Enrollments.Where(e => e.CourseId == c && e.Status != EnrollmentStatus.Revoked && e.Status != EnrollmentStatus.Expired).Select(e => e.UserId).Distinct().Take(5000).ToListAsync(ct)
            : await db.Users.Where(u => u.IsActive).Select(u => u.Id).Take(5000).ToListAsync(ct);
        await db.SaveChangesAsync(ct);
        await notifier.SendAsync(Tid, recipients.Where(x => x != Uid), "Announcement", a.Title, a.Body.Length > 200 ? a.Body[..200] : a.Body, ct);
        return a.Id;
    }
    public async Task<object> AnnouncementsAsync(CancellationToken ct)
    {
        var mine = db.Enrollments.Where(e => e.UserId == Uid && e.Status != EnrollmentStatus.Revoked && e.Status != EnrollmentStatus.Expired).Select(e => e.CourseId);
        var owned = new CourseAccess(db, me).CourseIds(true);
        var admin = IsAdmin;
        return await db.Announcements.Where(a => a.CourseId == null || admin || mine.Contains(a.CourseId!.Value) || owned.Contains(a.CourseId!.Value)).OrderByDescending(a => a.PublishedAtUtc).Take(100)
            .Select(a => new { a.Id, a.CourseId, a.Title, a.Body, a.PublishedAtUtc }).ToListAsync(ct);
    }
    public async Task<object> NotificationsAsync(CancellationToken ct) => await db.Notifications.Where(n => n.UserId == Uid).OrderByDescending(n => n.CreatedAtUtc).Take(100).Select(n => new { n.Id, n.Type, n.Title, n.Body, n.IsRead, n.CreatedAtUtc }).ToListAsync(ct);
    public async Task ReadNotificationAsync(Guid id, CancellationToken ct)
    {
        var when = Now;
        var n = await db.Notifications.Where(x => x.Id == id && x.UserId == Uid).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsRead, true).SetProperty(x => x.ReadAtUtc, (DateTime?)when), ct);
        if (n == 0) throw new NotFoundException("Notification not found.");
    }
    public async Task RegisterPushTokenAsync(PushTokenRequest r, CancellationToken ct)
    {
        var token = Clean(r.FcmToken, 400, "FcmToken");
        var platform = Enum.TryParse<PushTokenPlatform>(r.Platform, true, out var p) ? p : throw new ValidationAppException("Unknown platform.");
        var existing = await db.PushTokens.FirstOrDefaultAsync(x => x.UserId == Uid && x.FcmToken == token, ct);
        if (existing is not null) { existing.LastSeenAtUtc = Now; existing.DeviceId = me.DeviceId; }
        else db.PushTokens.Add(new PushToken { TenantId = Tid, UserId = Uid, DeviceId = me.DeviceId, FcmToken = token, Platform = platform, LastSeenAtUtc = Now, CreatedAtUtc = Now });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { db.ChangeTracker.Clear(); } // a parallel registration of the same token won
    }

    // ---- chat ------------------------------------------------------------------------------------------------------------
    public async Task<object> RoomsAsync(CancellationToken ct)
    {
        // Every course the caller owns or is enrolled in has one course room. It is created on first use and the caller is added to it.
        var courseIds = await db.Enrollments.Where(e => e.UserId == Uid && e.Status != EnrollmentStatus.Revoked && e.Status != EnrollmentStatus.Expired).Select(e => e.CourseId).ToListAsync(ct);
        courseIds.AddRange(await new CourseAccess(db, me).CourseIds(true).ToListAsync(ct));
        foreach (var cid in courseIds.Distinct())
        {
            var room = await db.ChatRooms.FirstOrDefaultAsync(r => r.CourseId == cid && r.RoomType == ChatRoomRoomType.Course, ct);
            if (room is null)
            {
                var title = await db.Courses.Where(c => c.Id == cid).Select(c => c.Title).FirstAsync(ct);
                room = new ChatRoom { TenantId = Tid, CourseId = cid, RoomType = ChatRoomRoomType.Course, Name = title, CreatedAtUtc = Now };
                db.ChatRooms.Add(room); await db.SaveChangesAsync(ct);
            }
            if (!await db.ChatRoomMembers.AnyAsync(m => m.ChatRoomId == room.Id && m.UserId == Uid, ct))
            {
                db.ChatRoomMembers.Add(new ChatRoomMember { TenantId = Tid, ChatRoomId = room.Id, UserId = Uid, JoinedAtUtc = Now, CreatedAtUtc = Now });
                try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { db.ChangeTracker.Clear(); }
            }
        }
        return await (from m in db.ChatRoomMembers join r in db.ChatRooms on m.ChatRoomId equals r.Id where m.UserId == Uid select new { r.Id, r.Name, RoomType = r.RoomType.ToString(), r.CourseId }).ToListAsync(ct);
    }
    private async Task<ChatRoom> MemberRoomAsync(Guid id, CancellationToken ct)
    {
        var r = await db.ChatRooms.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null || !await db.ChatRoomMembers.AnyAsync(m => m.ChatRoomId == id && m.UserId == Uid, ct)) throw new NotFoundException("Room not found.");
        // Membership is not enough for a course room if the enrollment was since revoked.
        if (r.CourseId is { } cid) await CourseAccessAsync(cid, ct);
        return r;
    }
    /// <summary>Throws 404 unless the caller is a member of the room and still has access to its course. Used by the chat hub before a connection joins a room group.</summary>
    public async Task AssertRoomMemberAsync(Guid roomId, CancellationToken ct) => await MemberRoomAsync(roomId, ct);
    /// <summary>Records attendance and returns the join link; the live hub calls this before adding a connection to a session group.</summary>
    public async Task AssertLiveAccessAsync(Guid sessionId, CancellationToken ct) => await JoinLiveAsync(sessionId, ct);
    public async Task<object> MessagesAsync(Guid roomId, DateTime? before, CancellationToken ct)
    {
        await MemberRoomAsync(roomId, ct);
        var q = db.ChatMessages.Where(m => m.ChatRoomId == roomId); if (before is { } b) q = q.Where(m => m.SentAtUtc < b);
        var rows = await q.OrderByDescending(m => m.SentAtUtc).Take(50).Select(m => new { m.Id, m.SenderId, senderName = db.Users.Where(u => u.Id == m.SenderId).Select(u => u.FullName).FirstOrDefault(), m.Body, m.SentAtUtc }).ToListAsync(ct);
        return new { messages = rows, nextBefore = rows.Count == 50 ? rows[^1].SentAtUtc : (DateTime?)null };
    }
    public async Task<Guid> SendAsync(Guid roomId, ChatSendRequest r, CancellationToken ct)
    {
        await MemberRoomAsync(roomId, ct);
        var m = new ChatMessage { TenantId = Tid, ChatRoomId = roomId, SenderId = Uid, Body = Clean(r.Body, 2000, "Body"), SentAtUtc = Now };
        db.ChatMessages.Add(m); await db.SaveChangesAsync(ct);
        await rt.ToRoomAsync(Tid, roomId, "message", new { m.Id, roomId, m.SenderId, m.Body, m.SentAtUtc });
        return m.Id;
    }

    // ---- live sessions ---------------------------------------------------------------------------------------------------
    public async Task<object> LiveSessionsAsync(Guid courseId, CancellationToken ct)
    {
        var (_, staff) = await CourseAccessAsync(courseId, ct);
        // The join link is only returned by the join endpoint, so it is recorded as attendance. Staff see it in the list.
        return await db.LiveSessions.Where(s => s.CourseId == courseId).OrderBy(s => s.StartsAtUtc).Select(s => new { s.Id, s.Title, Provider = s.Provider.ToString(), s.StartsAtUtc, s.EndsAtUtc, Status = s.Status.ToString(), joinUrl = staff ? s.JoinUrl : null }).ToListAsync(ct);
    }
    public async Task<Guid> CreateLiveSessionAsync(Guid courseId, LiveSessionRequest r, CancellationToken ct)
    {
        var (cc, staff) = await CourseAccessAsync(courseId, ct); if (!staff || !await new CourseAccess(db, me).IsAuthorAsync(cc, ct)) throw new NotFoundException("Course not found.");
        if (!Uri.TryCreate(r.JoinUrl, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) throw new ValidationAppException("JoinUrl must be an https link.");
        var provider = Enum.TryParse<LiveSessionProvider>(r.Provider, true, out var p) ? p : throw new ValidationAppException("Unknown provider.");
        if (r.EndsAtUtc is { } e && e <= r.StartsAtUtc) throw new ValidationAppException("EndsAtUtc must be after StartsAtUtc.");
        var s = new LiveSession { TenantId = Tid, CourseId = courseId, HostId = Uid, Title = Clean(r.Title, 300, "Title"), Provider = provider, JoinUrl = r.JoinUrl, StartsAtUtc = r.StartsAtUtc, EndsAtUtc = r.EndsAtUtc, CreatedAtUtc = Now };
        db.LiveSessions.Add(s); await db.SaveChangesAsync(ct);
        var students = await db.Enrollments.Where(e => e.CourseId == courseId && e.Status != EnrollmentStatus.Revoked && e.Status != EnrollmentStatus.Expired).Select(e => e.UserId).Distinct().Take(5000).ToListAsync(ct);
        await notifier.SendAsync(Tid, students.Where(x => x != Uid), "LiveSession", "Live session scheduled: " + s.Title, s.StartsAtUtc.ToString("u"), ct, System.Text.Json.JsonSerializer.Serialize(new { sessionId = s.Id, courseId }));
        return s.Id;
    }
    public async Task<object> JoinLiveAsync(Guid id, CancellationToken ct)
    {
        var s = await db.LiveSessions.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Live session not found.");
        await CourseAccessAsync(s.CourseId, ct);
        if (s.Status is LiveSessionStatus.Cancelled or LiveSessionStatus.Ended) throw new ConflictAppException("SESSION_CLOSED", "This session is not open.");
        if (!await db.LiveSessionAttendance.AnyAsync(a => a.LiveSessionId == id && a.UserId == Uid, ct))
        {
            db.LiveSessionAttendance.Add(new LiveSessionAttendance { TenantId = Tid, LiveSessionId = id, UserId = Uid, JoinedAtUtc = Now, CreatedAtUtc = Now });
            try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { db.ChangeTracker.Clear(); }
        }
        // Everyone connected to the session's live group sees only a head count, never who joined.
        try { await rt.ToLiveAsync(Tid, id, "attendance", new { sessionId = id, attendees = await db.LiveSessionAttendance.CountAsync(a => a.LiveSessionId == id, ct) }); } catch (Exception ex) when (ex is not OperationCanceledException) { }
        return new { joinUrl = s.JoinUrl };
    }
    public async Task<object> AttendanceAsync(Guid id, CancellationToken ct)
    {
        var s = await db.LiveSessions.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Live session not found.");
        var (_, staff) = await CourseAccessAsync(s.CourseId, ct); if (!staff) throw new NotFoundException("Live session not found.");
        return await (from a in db.LiveSessionAttendance join u in db.Users on a.UserId equals u.Id where a.LiveSessionId == id select new { a.UserId, u.FullName, a.JoinedAtUtc }).ToListAsync(ct);
    }
    // ---- course reviews ----------------------------------------------------------------------------------------------
    /// <summary>Published reviews for a course (newest first, capped), plus the aggregate every catalog card shows.</summary>
    public async Task<object> ReviewsAsync(Guid courseId, CancellationToken ct)
    {
        var reviews = await db.CourseReviews.Where(r => r.CourseId == courseId && r.Status == CourseReviewStatus.Published).OrderByDescending(r => r.CreatedAtUtc).Take(50).ToListAsync(ct);
        var names = await db.Users.Where(u => reviews.Select(r => r.UserId).Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var count = await db.CourseReviews.CountAsync(r => r.CourseId == courseId && r.Status == CourseReviewStatus.Published, ct);
        double? avg = count == 0 ? null : await db.CourseReviews.Where(r => r.CourseId == courseId && r.Status == CourseReviewStatus.Published).AverageAsync(r => (double)r.Rating, ct);
        CourseReview? mine = null;
        if (me.UserId is { } u) mine = await db.CourseReviews.FirstOrDefaultAsync(r => r.CourseId == courseId && r.UserId == u, ct);
        return new
        {
            items = reviews.Select(r => new { r.Id, userName = names.GetValueOrDefault(r.UserId, ""), r.Rating, r.Body, r.CreatedAtUtc, mine = r.UserId == me.UserId }),
            count, average = avg is null ? (double?)null : Math.Round(avg.Value, 1),
            myReview = mine is null ? null : new { mine.Id, mine.Rating, mine.Body, status = mine.Status.ToString() },
        };
    }

    /// <summary>Creates or updates the caller's own review. Only a student with a live enrollment may review.</summary>
    public async Task<Guid> UpsertReviewAsync(Guid courseId, ReviewUpsertRequest r, CancellationToken ct)
    {
        if (r.Rating is < 1 or > 5) throw new ValidationAppException("Rating must be between 1 and 5.");
        var c = await db.Courses.FirstOrDefaultAsync(x => x.Id == courseId && x.Status == CourseStatus.Published, ct) ?? throw new NotFoundException("Course not found.");
        var e = await db.Enrollments.FirstOrDefaultAsync(x => x.CourseId == courseId && x.UserId == Uid, ct);
        if (e is null || e.Status is EnrollmentStatus.Revoked or EnrollmentStatus.Expired || (e.ExpiresAtUtc is { } x && x <= Now)) throw new ForbiddenAppException("NOT_ENROLLED", "Only enrolled students can review a course.");
        var existing = await db.CourseReviews.FirstOrDefaultAsync(x => x.CourseId == courseId && x.UserId == Uid, ct);
        if (existing is not null) { existing.Rating = r.Rating; existing.Body = r.Body; existing.Status = CourseReviewStatus.Published; await db.SaveChangesAsync(ct); return existing.Id; }
        var review = new CourseReview { TenantId = c.TenantId, CourseId = courseId, UserId = Uid, Rating = r.Rating, Body = r.Body, Status = CourseReviewStatus.Published, CreatedAtUtc = Now };
        db.CourseReviews.Add(review);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { db.Entry(review).State = EntityState.Detached; var again = await db.CourseReviews.FirstOrDefaultAsync(x => x.CourseId == courseId && x.UserId == Uid, ct); if (again is not null) return again.Id; throw; }
        return review.Id;
    }

    public async Task DeleteReviewAsync(Guid courseId, CancellationToken ct)
    {
        var r = await db.CourseReviews.FirstOrDefaultAsync(x => x.CourseId == courseId && x.UserId == Uid, ct) ?? throw new NotFoundException("Review not found.");
        db.CourseReviews.Remove(r); await db.SaveChangesAsync(ct);
    }

    /// <summary>Flags a published review for moderation. One open report per user per review; you cannot report your own.</summary>
    public async Task<Guid> ReportReviewAsync(Guid reviewId, ReviewReportRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Reason)) throw new ValidationAppException("A reason is required.");
        var review = await db.CourseReviews.FirstOrDefaultAsync(x => x.Id == reviewId && x.Status == CourseReviewStatus.Published, ct) ?? throw new NotFoundException("Review not found.");
        if (review.UserId == Uid) throw new ValidationAppException("You cannot report your own review.");
        var existing = await db.CourseReviewReports.FirstOrDefaultAsync(x => x.ReviewId == reviewId && x.UserId == Uid, ct);
        if (existing is not null) return existing.Id;
        var report = new CourseReviewReport { TenantId = review.TenantId, ReviewId = reviewId, UserId = Uid, Reason = r.Reason.Trim(), Status = CourseReviewReportStatus.Open, CreatedAtUtc = Now };
        db.CourseReviewReports.Add(report);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { db.Entry(report).State = EntityState.Detached; var again = await db.CourseReviewReports.FirstOrDefaultAsync(x => x.ReviewId == reviewId && x.UserId == Uid, ct); if (again is not null) return again.Id; throw; }
        return report.Id;
    }

    /// <summary>Open reports for moderation. Instructors see only their own courses; TenantAdmin sees the tenant.</summary>
    public async Task<object> ReportedReviewsAsync(CancellationToken ct)
    {
        var access = new CourseAccess(db, me);
        var reports = await db.CourseReviewReports.Where(r => r.Status == CourseReviewReportStatus.Open).OrderByDescending(r => r.CreatedAtUtc).Take(100).ToListAsync(ct);
        var rows = new List<object>();
        foreach (var r in reports)
        {
            var review = await db.CourseReviews.FirstOrDefaultAsync(x => x.Id == r.ReviewId, ct);
            if (review is null) continue;
            var c = await db.Courses.FirstOrDefaultAsync(x => x.Id == review.CourseId, ct);
            if (c is null || !access.IsOwnerOrAdmin(c)) continue;
            var reviewer = await db.Users.FirstOrDefaultAsync(u => u.Id == review.UserId, ct);
            rows.Add(new { reportId = r.Id, r.Reason, reportedAtUtc = r.CreatedAtUtc, review = new { review.Id, courseId = c.Id, courseTitle = c.Title, userName = reviewer?.FullName ?? "", review.Rating, review.Body, status = review.Status.ToString() } });
        }
        return new { items = rows };
    }

    /// <summary>Hides a review (it stops counting in aggregates) and actions its open reports.</summary>
    public async Task HideReviewAsync(Guid reviewId, CancellationToken ct)
    {
        var review = await db.CourseReviews.FirstOrDefaultAsync(x => x.Id == reviewId, ct) ?? throw new NotFoundException("Review not found.");
        var c = await db.Courses.FirstOrDefaultAsync(x => x.Id == review.CourseId, ct) ?? throw new NotFoundException("Review not found.");
        if (!new CourseAccess(db, me).IsOwnerOrAdmin(c)) throw new NotFoundException("Review not found.");
        review.Status = CourseReviewStatus.Hidden;
        foreach (var rep in await db.CourseReviewReports.Where(r => r.ReviewId == reviewId && r.Status == CourseReviewReportStatus.Open).ToListAsync(ct)) rep.Status = CourseReviewReportStatus.Actioned;
        await db.SaveChangesAsync(ct);
    }

    public async Task UnhideReviewAsync(Guid reviewId, CancellationToken ct)
    {
        var review = await db.CourseReviews.FirstOrDefaultAsync(x => x.Id == reviewId, ct) ?? throw new NotFoundException("Review not found.");
        var c = await db.Courses.FirstOrDefaultAsync(x => x.Id == review.CourseId, ct) ?? throw new NotFoundException("Review not found.");
        if (!new CourseAccess(db, me).IsOwnerOrAdmin(c)) throw new NotFoundException("Review not found.");
        review.Status = CourseReviewStatus.Published; await db.SaveChangesAsync(ct);
    }

    public async Task DismissReportAsync(Guid reportId, CancellationToken ct)
    {
        var report = await db.CourseReviewReports.FirstOrDefaultAsync(x => x.Id == reportId && x.Status == CourseReviewReportStatus.Open, ct) ?? throw new NotFoundException("Report not found.");
        var review = await db.CourseReviews.FirstOrDefaultAsync(x => x.Id == report.ReviewId, ct) ?? throw new NotFoundException("Report not found.");
        var c = await db.Courses.FirstOrDefaultAsync(x => x.Id == review.CourseId, ct) ?? throw new NotFoundException("Report not found.");
        if (!new CourseAccess(db, me).IsOwnerOrAdmin(c)) throw new NotFoundException("Report not found.");
        report.Status = CourseReviewReportStatus.Dismissed; await db.SaveChangesAsync(ct);
    }

}
