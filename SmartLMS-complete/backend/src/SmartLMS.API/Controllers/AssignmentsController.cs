using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartLMS.Application.Common;
using SmartLMS.Domain.Enums;
using SmartLMS.Infrastructure.Services.Lms;

namespace SmartLMS.API.Controllers;

[Route("api/v1")]
public sealed class AssignmentsController(AssignmentService s) : ApiControllerBase
{
    private const string Mgr = $"{RoleNames.TenantAdmin},{RoleNames.Instructor}";
    private const string Any = $"{RoleNames.Student},{RoleNames.Instructor},{RoleNames.TenantAdmin},{RoleNames.TeachingAssistant}";
    private const string Grd = $"{RoleNames.TenantAdmin},{RoleNames.Instructor},{RoleNames.TeachingAssistant}";

    [HttpGet("courses/{courseId:guid}/assignments"), Authorize(Roles = Any)] public async Task<ActionResult<ApiResponse<object>>> List(Guid courseId, CancellationToken ct) => Success(await s.ListAsync(courseId, ct));
    [HttpPost("courses/{courseId:guid}/assignments"), Authorize(Roles = Mgr)] public async Task<ActionResult<ApiResponse<object>>> Create(Guid courseId, AssignmentRequest r, CancellationToken ct) => Success((object)new { id = await s.CreateAsync(courseId, r, ct) });
    [HttpPut("assignments/{id:guid}"), Authorize(Roles = Mgr)] public async Task<ActionResult<ApiResponse<object?>>> Update(Guid id, AssignmentRequest r, CancellationToken ct) { await s.UpdateAsync(id, r, ct); return Done("Updated."); }
    [HttpPost("assignments/{id:guid}/groups"), Authorize(Roles = RoleNames.Student)] public async Task<ActionResult<ApiResponse<object>>> Group(Guid id, GroupRequest r, CancellationToken ct) => Success(await s.GroupAsync(id, r, ct));
    [HttpPost("assignments/{id:guid}/submissions"), Authorize(Roles = RoleNames.Student)] public async Task<ActionResult<ApiResponse<object>>> Submit(Guid id, SubmitRequest r, CancellationToken ct) => Success(await s.SubmitAsync(id, r, ct));
    [HttpGet("assignments/{id:guid}/submissions"), Authorize(Roles = Grd)] public async Task<ActionResult<ApiResponse<object>>> Submissions(Guid id, CancellationToken ct) => Success(await s.SubmissionsAsync(id, ct));
    [HttpGet("submissions/{id:guid}"), Authorize(Roles = Any)] public async Task<ActionResult<ApiResponse<object>>> Submission(Guid id, CancellationToken ct) => Success(await s.SubmissionAsync(id, ct));
    [HttpPut("submissions/{id:guid}/grade"), Authorize(Roles = Grd)] public async Task<ActionResult<ApiResponse<object?>>> Grade(Guid id, GradeSubmissionRequest r, CancellationToken ct) { await s.GradeAsync(id, r, ct); return Done("Graded."); }
    [HttpPost("submissions/{id:guid}/comments"), Authorize(Roles = Grd)] public async Task<ActionResult<ApiResponse<object>>> Comment(Guid id, SubmissionCommentRequest r, CancellationToken ct) => Success((object)new { id = await s.CommentAsync(id, r, ct) });
    [HttpPost("submissions/{id:guid}/return"), Authorize(Roles = Grd)] public async Task<ActionResult<ApiResponse<object?>>> Return(Guid id, ReturnRequest r, CancellationToken ct) { await s.ReturnAsync(id, r, ct); return Done("Returned."); }
    [HttpPost("assignments/{id:guid}/peer-reviews/assign"), Authorize(Roles = Mgr)] public async Task<ActionResult<ApiResponse<object>>> Assign(Guid id, CancellationToken ct) => Success(await s.AssignPeerReviewsAsync(id, ct));
    [HttpGet("peer-reviews/mine"), Authorize(Roles = RoleNames.Student)] public async Task<ActionResult<ApiResponse<object>>> Mine(CancellationToken ct) => Success(await s.MyReviewsAsync(ct));
    [HttpPut("peer-reviews/{id:guid}"), Authorize(Roles = RoleNames.Student)] public async Task<ActionResult<ApiResponse<object?>>> Review(Guid id, ReviewRequest r, CancellationToken ct) { await s.SubmitReviewAsync(id, r, ct); return Done("Submitted."); }
}
