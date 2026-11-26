using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartLMS.Application.Common;
using SmartLMS.Domain.Enums;
using SmartLMS.Infrastructure.Services.Lms;

namespace SmartLMS.API.Controllers;

[Route("api/v1")]
public sealed class CommunityController(CommunityService s) : ApiControllerBase
{
    private const string Mgr = $"{RoleNames.TenantAdmin},{RoleNames.Instructor}";
    private const string Any = $"{RoleNames.Student},{RoleNames.Instructor},{RoleNames.TenantAdmin},{RoleNames.TeachingAssistant}";
    private const string Grd = $"{RoleNames.TenantAdmin},{RoleNames.Instructor},{RoleNames.TeachingAssistant}";
    private const string St = RoleNames.Student;

    [HttpGet("lessons/{id:guid}/notes"), Authorize(Roles = St)] public async Task<ActionResult<ApiResponse<object>>> Notes(Guid id, CancellationToken ct) => Success(await s.NotesAsync(id, ct));
    [HttpPost("lessons/{id:guid}/notes"), Authorize(Roles = St)] public async Task<ActionResult<ApiResponse<object>>> AddNote(Guid id, NoteRequest r, CancellationToken ct) => Success((object)new { id = await s.AddNoteAsync(id, r, ct) });
    [HttpPut("notes/{id:guid}"), Authorize(Roles = St)] public async Task<ActionResult<ApiResponse<object?>>> EditNote(Guid id, NoteRequest r, CancellationToken ct) { await s.EditNoteAsync(id, r, ct); return Done("Updated."); }
    [HttpDelete("notes/{id:guid}"), Authorize(Roles = St)] public async Task<ActionResult<ApiResponse<object?>>> DelNote(Guid id, CancellationToken ct) { await s.DeleteNoteAsync(id, ct); return Done("Deleted."); }
    [HttpGet("lessons/{id:guid}/bookmarks"), Authorize(Roles = St)] public async Task<ActionResult<ApiResponse<object>>> Bookmarks(Guid id, CancellationToken ct) => Success(await s.BookmarksAsync(id, ct));
    [HttpPost("lessons/{id:guid}/bookmarks"), Authorize(Roles = St)] public async Task<ActionResult<ApiResponse<object>>> AddBookmark(Guid id, BookmarkRequest r, CancellationToken ct) => Success((object)new { id = await s.AddBookmarkAsync(id, r, ct) });
    [HttpDelete("bookmarks/{id:guid}"), Authorize(Roles = St)] public async Task<ActionResult<ApiResponse<object?>>> DelBookmark(Guid id, CancellationToken ct) { await s.DeleteBookmarkAsync(id, ct); return Done("Deleted."); }
    [HttpGet("courses/{id:guid}/reviews"), AllowAnonymous] public async Task<ActionResult<ApiResponse<object>>> Reviews(Guid id, CancellationToken ct) => Success(await s.ReviewsAsync(id, ct));
    [HttpPut("courses/{id:guid}/review"), Authorize(Roles = St)] public async Task<ActionResult<ApiResponse<object>>> UpsertReview(Guid id, ReviewUpsertRequest r, CancellationToken ct) => Success((object)new { id = await s.UpsertReviewAsync(id, r, ct) });
    [HttpDelete("courses/{id:guid}/review"), Authorize(Roles = St)] public async Task<ActionResult<ApiResponse<object?>>> DeleteReview(Guid id, CancellationToken ct) { await s.DeleteReviewAsync(id, ct); return Done("Deleted."); }
    [HttpPost("reviews/{id:guid}/reports"), Authorize(Roles = St)] public async Task<ActionResult<ApiResponse<object>>> ReportReview(Guid id, ReviewReportRequest r, CancellationToken ct) => Success((object)new { id = await s.ReportReviewAsync(id, r, ct) });
    [HttpGet("reviews/reported"), Authorize(Roles = Mgr)] public async Task<ActionResult<ApiResponse<object>>> ReportedReviews(CancellationToken ct) => Success(await s.ReportedReviewsAsync(ct));
    [HttpPost("reviews/{id:guid}/hide"), Authorize(Roles = Mgr)] public async Task<ActionResult<ApiResponse<object?>>> HideReview(Guid id, CancellationToken ct) { await s.HideReviewAsync(id, ct); return Done("Hidden."); }
    [HttpPost("reviews/{id:guid}/unhide"), Authorize(Roles = Mgr)] public async Task<ActionResult<ApiResponse<object?>>> UnhideReview(Guid id, CancellationToken ct) { await s.UnhideReviewAsync(id, ct); return Done("Restored."); }
    [HttpPost("review-reports/{id:guid}/dismiss"), Authorize(Roles = Mgr)] public async Task<ActionResult<ApiResponse<object?>>> DismissReport(Guid id, CancellationToken ct) { await s.DismissReportAsync(id, ct); return Done("Dismissed."); }
    [HttpPost("study-sessions"), Authorize(Roles = St)] public async Task<ActionResult<ApiResponse<object?>>> Study(StudySessionRequest r, CancellationToken ct) { await s.RecordStudyAsync(r, ct); return Done("Recorded."); }

    [HttpGet("learning-paths/manage"), Authorize(Roles = Mgr)] public async Task<ActionResult<ApiResponse<object>>> ManagePaths(CancellationToken ct) => Success(await s.ManagePathsAsync(ct));
    [HttpGet("learning-paths"), AllowAnonymous] public async Task<ActionResult<ApiResponse<object>>> Paths(CancellationToken ct) => Success(await s.PathsAsync(ct));
    [HttpGet("learning-paths/{id:guid}"), AllowAnonymous] public async Task<ActionResult<ApiResponse<object>>> GetPath(Guid id, CancellationToken ct) => Success(await s.PathAsync(id, ct));
    [HttpPost("learning-paths"), Authorize(Roles = Mgr)] public async Task<ActionResult<ApiResponse<object>>> CreatePath(PathRequest r, CancellationToken ct) => Success((object)new { id = await s.CreatePathAsync(r, ct) });
    [HttpPut("learning-paths/{id:guid}"), Authorize(Roles = Mgr)] public async Task<ActionResult<ApiResponse<object?>>> UpdatePath(Guid id, PathRequest r, CancellationToken ct) { await s.UpdatePathAsync(id, r, ct); return Done("Updated."); }
    [HttpDelete("learning-paths/{id:guid}"), Authorize(Roles = RoleNames.TenantAdmin)] public async Task<ActionResult<ApiResponse<object?>>> DeletePath(Guid id, CancellationToken ct) { await s.DeletePathAsync(id, ct); return Done("Deleted."); }
    [HttpPost("learning-paths/{id:guid}/enroll"), Authorize(Roles = St)] public async Task<ActionResult<ApiResponse<object>>> EnrollPath(Guid id, CancellationToken ct) => Success((object)new { id = await s.EnrollPathAsync(id, ct) });

    [HttpGet("courses/{courseId:guid}/threads"), Authorize(Roles = Any)] public async Task<ActionResult<ApiResponse<object>>> Threads(Guid courseId, [FromQuery] Guid? lessonId, CancellationToken ct) => Success(await s.ThreadsAsync(courseId, lessonId, ct));
    [HttpPost("courses/{courseId:guid}/threads"), Authorize(Roles = $"{St},{RoleNames.Instructor},{RoleNames.TeachingAssistant}")] public async Task<ActionResult<ApiResponse<object>>> NewThread(Guid courseId, ThreadRequest r, CancellationToken ct) => Success((object)new { id = await s.CreateThreadAsync(courseId, r, ct) });
    [HttpGet("threads/{id:guid}"), Authorize(Roles = Any)] public async Task<ActionResult<ApiResponse<object>>> Thread(Guid id, CancellationToken ct) => Success(await s.ThreadAsync(id, ct));
    [HttpPost("threads/{id:guid}/posts"), Authorize(Roles = $"{St},{RoleNames.Instructor},{RoleNames.TeachingAssistant}")] public async Task<ActionResult<ApiResponse<object>>> Reply(Guid id, PostRequest r, CancellationToken ct) => Success((object)new { id = await s.ReplyAsync(id, r, ct) });
    [HttpPost("threads/{id:guid}/upvote"), Authorize(Roles = $"{St},{RoleNames.Instructor},{RoleNames.TeachingAssistant}")] public async Task<ActionResult<ApiResponse<object>>> UpThread(Guid id, CancellationToken ct) => Success(await s.UpvoteThreadAsync(id, ct));
    [HttpPost("posts/{id:guid}/upvote"), Authorize(Roles = $"{St},{RoleNames.Instructor},{RoleNames.TeachingAssistant}")] public async Task<ActionResult<ApiResponse<object>>> UpPost(Guid id, CancellationToken ct) => Success(await s.UpvotePostAsync(id, ct));
    [HttpPost("threads/{id:guid}/pin"), Authorize(Roles = Grd)] public async Task<ActionResult<ApiResponse<object?>>> Pin(Guid id, CancellationToken ct) { await s.PinAsync(id, ct); return Done("Toggled."); }

    [HttpGet("courses/{courseId:guid}/live-sessions"), Authorize(Roles = Any)] public async Task<ActionResult<ApiResponse<object>>> Live(Guid courseId, CancellationToken ct) => Success(await s.LiveSessionsAsync(courseId, ct));
    [HttpPost("courses/{courseId:guid}/live-sessions"), Authorize(Roles = RoleNames.Instructor)] public async Task<ActionResult<ApiResponse<object>>> NewLive(Guid courseId, LiveSessionRequest r, CancellationToken ct) => Success((object)new { id = await s.CreateLiveSessionAsync(courseId, r, ct) });
    [HttpPost("live-sessions/{id:guid}/join"), Authorize(Roles = St)] public async Task<ActionResult<ApiResponse<object>>> Join(Guid id, CancellationToken ct) => Success(await s.JoinLiveAsync(id, ct));
    [HttpGet("live-sessions/{id:guid}/attendance"), Authorize(Roles = Grd)] public async Task<ActionResult<ApiResponse<object>>> Attendance(Guid id, CancellationToken ct) => Success(await s.AttendanceAsync(id, ct));

    [HttpGet("announcements"), Authorize] public async Task<ActionResult<ApiResponse<object>>> Announcements(CancellationToken ct) => Success(await s.AnnouncementsAsync(ct));
    [HttpPost("announcements"), Authorize(Roles = Grd)] public async Task<ActionResult<ApiResponse<object>>> Announce(AnnouncementRequest r, CancellationToken ct) => Success((object)new { id = await s.AnnounceAsync(r, ct) });
    [HttpGet("notifications"), Authorize] public async Task<ActionResult<ApiResponse<object>>> Notifications(CancellationToken ct) => Success(await s.NotificationsAsync(ct));
    [HttpPost("notifications/{id:guid}/read"), Authorize] public async Task<ActionResult<ApiResponse<object?>>> Read(Guid id, CancellationToken ct) { await s.ReadNotificationAsync(id, ct); return Done("Read."); }
    [HttpPost("notifications/push-token"), Authorize] public async Task<ActionResult<ApiResponse<object?>>> Push(PushTokenRequest r, CancellationToken ct) { await s.RegisterPushTokenAsync(r, ct); return Done("Registered."); }
    [HttpDelete("notifications/push-token"), Authorize] public async Task<ActionResult<ApiResponse<object?>>> PushDelete(PushTokenDeleteRequest r, CancellationToken ct) { await s.UnregisterPushTokenAsync(r, ct); return Done("Removed."); }
    [HttpGet("notifications/push-config"), Authorize] public ActionResult<ApiResponse<object>> PushConfig() => Success((object)s.PushConfig());

    [HttpGet("chat/rooms"), Authorize] public async Task<ActionResult<ApiResponse<object>>> Rooms(CancellationToken ct) => Success(await s.RoomsAsync(ct));
    [HttpGet("chat/rooms/{id:guid}/messages"), Authorize] public async Task<ActionResult<ApiResponse<object>>> Messages(Guid id, [FromQuery] DateTime? before, CancellationToken ct) => Success(await s.MessagesAsync(id, before, ct));
    [HttpPost("chat/rooms/{id:guid}/messages"), Authorize] public async Task<ActionResult<ApiResponse<object>>> Send(Guid id, ChatSendRequest r, CancellationToken ct) => Success((object)new { id = await s.SendAsync(id, r, ct) });
}
