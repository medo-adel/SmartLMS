using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartLMS.Application.Common;
using SmartLMS.Domain.Enums;
using SmartLMS.Infrastructure.Services.Lms;

namespace SmartLMS.API.Controllers;

[Route("api/v1")]
public sealed class LmsController(LmsService lms) : ApiControllerBase
{
    private const string TA = RoleNames.TenantAdmin;
    private const string IN_TA = $"{RoleNames.Instructor},{RoleNames.TenantAdmin}";
    private const string ST = RoleNames.Student;
    private const string Learners = $"{RoleNames.Student},{RoleNames.Instructor},{RoleNames.TenantAdmin},{RoleNames.TeachingAssistant}";

    // categories
    [HttpGet("categories")] public async Task<ActionResult<ApiResponse<object>>> Categories(CancellationToken ct) => Success(await lms.CategoriesAsync(ct));
    [HttpPost("categories"), Authorize(Roles = TA)]
    public async Task<ActionResult<ApiResponse<object>>> CreateCategory(CategoryRequest r, CancellationToken ct) => Created("", ApiResponse<object>.Ok(new { id = await lms.CreateCategoryAsync(r, ct) }));
    [HttpPut("categories/{id:guid}"), Authorize(Roles = TA)]
    public async Task<ActionResult<ApiResponse<object?>>> UpdateCategory(Guid id, CategoryRequest r, CancellationToken ct) { await lms.UpdateCategoryAsync(id, r, ct); return Done("Updated."); }
    [HttpDelete("categories/{id:guid}"), Authorize(Roles = TA)]
    public async Task<ActionResult<ApiResponse<object?>>> DeleteCategory(Guid id, CancellationToken ct) { await lms.DeleteCategoryAsync(id, ct); return Done("Deleted."); }

    // courses
    [HttpGet("courses")]
    public async Task<ActionResult<ApiResponse<object>>> Search([FromQuery] string? q, [FromQuery] Guid? categoryId, [FromQuery] string? level, [FromQuery] string? language, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Success(await lms.SearchCoursesAsync(q, categoryId, level, language, page, pageSize, ct));
    [HttpGet("courses/mine"), Authorize(Roles = IN_TA)]
    public async Task<ActionResult<ApiResponse<object>>> Mine(CancellationToken ct) => Success(await lms.MyCoursesAsync(ct));
    [HttpGet("courses/{id:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> Detail(Guid id, CancellationToken ct) => Success(await lms.CourseDetailAsync(id, ct));
    [HttpPost("courses"), Authorize(Roles = IN_TA)]
    public async Task<ActionResult<ApiResponse<object>>> Create(CourseRequest r, CancellationToken ct) => Created("", ApiResponse<object>.Ok(new { id = await lms.CreateCourseAsync(r, ct) }));
    [HttpPut("courses/{id:guid}"), Authorize(Roles = IN_TA)]
    public async Task<ActionResult<ApiResponse<object?>>> Update(Guid id, CourseRequest r, CancellationToken ct) { await lms.UpdateCourseAsync(id, r, ct); return Done("Updated."); }
    [HttpDelete("courses/{id:guid}"), Authorize(Roles = IN_TA)]
    public async Task<ActionResult<ApiResponse<object?>>> Delete(Guid id, CancellationToken ct) { await lms.DeleteCourseAsync(id, ct); return Done("Deleted."); }
    [HttpPost("courses/{id:guid}/submit-for-approval"), Authorize(Roles = RoleNames.Instructor)]
    public async Task<ActionResult<ApiResponse<object?>>> Submit(Guid id, CancellationToken ct) { await lms.SubmitAsync(id, ct); return Done("Submitted."); }
    [HttpPost("courses/{id:guid}/approve"), Authorize(Roles = TA)]
    public async Task<ActionResult<ApiResponse<object?>>> Approve(Guid id, CancellationToken ct) { await lms.ReviewAsync(id, true, null, ct); return Done("Published."); }
    [HttpPost("courses/{id:guid}/reject"), Authorize(Roles = TA)]
    public async Task<ActionResult<ApiResponse<object?>>> Reject(Guid id, RejectRequest r, CancellationToken ct) { await lms.ReviewAsync(id, false, r.Comment, ct); return Done("Rejected."); }

    // sections and lessons
    [HttpPost("courses/{courseId:guid}/sections"), Authorize(Roles = IN_TA)]
    public async Task<ActionResult<ApiResponse<object>>> CreateSection(Guid courseId, SectionRequest r, CancellationToken ct) => Created("", ApiResponse<object>.Ok(new { id = await lms.CreateSectionAsync(courseId, r, ct) }));
    [HttpPut("sections/{id:guid}"), Authorize(Roles = IN_TA)]
    public async Task<ActionResult<ApiResponse<object?>>> UpdateSection(Guid id, SectionRequest r, CancellationToken ct) { await lms.UpdateSectionAsync(id, r, ct); return Done("Updated."); }
    [HttpDelete("sections/{id:guid}"), Authorize(Roles = IN_TA)]
    public async Task<ActionResult<ApiResponse<object?>>> DeleteSection(Guid id, CancellationToken ct) { await lms.DeleteSectionAsync(id, ct); return Done("Deleted."); }
    [HttpPost("sections/{sectionId:guid}/lessons"), Authorize(Roles = IN_TA)]
    public async Task<ActionResult<ApiResponse<object>>> CreateLesson(Guid sectionId, LessonRequest r, CancellationToken ct) => Created("", ApiResponse<object>.Ok(new { id = await lms.CreateLessonAsync(sectionId, r, ct) }));
    [HttpGet("lessons/{id:guid}"), Authorize(Roles = Learners)]
    public async Task<ActionResult<ApiResponse<object>>> Lesson(Guid id, CancellationToken ct) => Success(await lms.GetLessonAsync(id, ct));
    [HttpPut("lessons/{id:guid}"), Authorize(Roles = IN_TA)]
    public async Task<ActionResult<ApiResponse<object?>>> UpdateLesson(Guid id, LessonRequest r, CancellationToken ct) { await lms.UpdateLessonAsync(id, r, ct); return Done("Updated."); }
    [HttpDelete("lessons/{id:guid}"), Authorize(Roles = IN_TA)]
    public async Task<ActionResult<ApiResponse<object?>>> DeleteLesson(Guid id, CancellationToken ct) { await lms.DeleteLessonAsync(id, ct); return Done("Deleted."); }

    // enrollment and progress
    [HttpPost("courses/{id:guid}/enroll"), Authorize(Roles = ST)]
    public async Task<ActionResult<ApiResponse<object>>> Enroll(Guid id, CancellationToken ct) => Success<object>(new { enrollmentId = await lms.EnrollAsync(id, ct) });
    [HttpGet("me/enrollments"), Authorize(Roles = ST)]
    public async Task<ActionResult<ApiResponse<object>>> Enrollments(CancellationToken ct) => Success(await lms.MyEnrollmentsAsync(ct));
    [HttpPut("lessons/{id:guid}/progress"), Authorize(Roles = ST)]
    public async Task<ActionResult<ApiResponse<object>>> Progress(Guid id, ProgressRequest r, CancellationToken ct) => Success(await lms.SaveProgressAsync(id, r, ct));
    [HttpGet("courses/{id:guid}/progress"), Authorize(Roles = ST)]
    public async Task<ActionResult<ApiResponse<object>>> CourseProgress(Guid id, CancellationToken ct) => Success(await lms.CourseProgressAsync(id, ct));
}
