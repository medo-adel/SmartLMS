using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartLMS.Application.Common;
using SmartLMS.Domain.Enums;
using SmartLMS.Infrastructure.Services.Lms;

namespace SmartLMS.API.Controllers;

[Route("api/v1")]
public sealed class ContentController(ContentService s, LessonFileService files) : ApiControllerBase
{
    private const string Mgr = $"{RoleNames.TenantAdmin},{RoleNames.Instructor}";
    private const string Any = $"{RoleNames.Student},{RoleNames.Instructor},{RoleNames.TenantAdmin},{RoleNames.TeachingAssistant}";

    [HttpGet("courses/{id:guid}/staff"), Authorize(Roles = Mgr)] public async Task<ActionResult<ApiResponse<object>>> Staff(Guid id, CancellationToken ct) => Success(await s.ListStaffAsync(id, ct));
    [HttpPut("courses/{id:guid}/staff"), Authorize(Roles = Mgr)] public async Task<ActionResult<ApiResponse<object?>>> SetStaff(Guid id, StaffRequest r, CancellationToken ct) { await s.SetStaffAsync(id, r, ct); return Done("Updated."); }
    [HttpGet("courses/{id:guid}/prerequisites"), Authorize(Roles = Mgr)] public async Task<ActionResult<ApiResponse<object>>> PrereqList(Guid id, CancellationToken ct) => Success(await s.ListPrerequisitesAsync(id, ct));
    [HttpPut("courses/{id:guid}/prerequisites"), Authorize(Roles = Mgr)] public async Task<ActionResult<ApiResponse<object?>>> Prereq(Guid id, PrerequisitesRequest r, CancellationToken ct) { await s.SetPrerequisitesAsync(id, r, ct); return Done("Updated."); }
    [HttpPut("courses/{courseId:guid}/sections/order"), Authorize(Roles = Mgr)] public async Task<ActionResult<ApiResponse<object?>>> OrderSections(Guid courseId, OrderRequest r, CancellationToken ct) { await s.ReorderSectionsAsync(courseId, r, ct); return Done("Updated."); }
    [HttpPut("sections/{sectionId:guid}/lessons/order"), Authorize(Roles = Mgr)] public async Task<ActionResult<ApiResponse<object?>>> OrderLessons(Guid sectionId, OrderRequest r, CancellationToken ct) { await s.ReorderLessonsAsync(sectionId, r, ct); return Done("Updated."); }
    /// <summary>Extra routes (not in the first contract): the API had no way to price a course or a lesson.</summary>
    [HttpPut("courses/{id:guid}/pricing"), Authorize(Roles = Mgr)] public async Task<ActionResult<ApiResponse<object?>>> Pricing(Guid id, PricingRequest r, CancellationToken ct) { await s.SetPricingAsync(id, r, ct); return Done("Updated."); }
    [HttpPut("lessons/{id:guid}/price"), Authorize(Roles = Mgr)] public async Task<ActionResult<ApiResponse<object?>>> LessonPrice(Guid id, LessonPriceRequest r, CancellationToken ct) { await s.SetLessonPriceAsync(id, r, ct); return Done("Updated."); }
    /// <summary>Extra routes (not in the first contract): read what the write routes store, so the player can show chapters, slides and resources.</summary>
    [HttpGet("lessons/{id:guid}/chapters"), Authorize(Roles = Any)] public async Task<ActionResult<ApiResponse<object>>> GetChapters(Guid id, CancellationToken ct) => Success(await s.ChaptersAsync(id, ct));
    [HttpGet("lessons/{id:guid}/transcript"), Authorize(Roles = Any)] public async Task<ActionResult<ApiResponse<object>>> GetTranscript(Guid id, CancellationToken ct) => Success(await s.TranscriptAsync(id, ct));
    [HttpGet("lessons/{id:guid}/slides"), Authorize(Roles = Any)] public async Task<ActionResult<ApiResponse<object>>> GetSlides(Guid id, CancellationToken ct) => Success(await s.SlidesAsync(id, ct));
    [HttpGet("lessons/{id:guid}/resources"), Authorize(Roles = Any)] public async Task<ActionResult<ApiResponse<object>>> GetResources(Guid id, CancellationToken ct) => Success(await files.ListResourcesAsync(id, ct));
    [HttpPut("lessons/{id:guid}/chapters"), Authorize(Roles = Mgr)] public async Task<ActionResult<ApiResponse<object?>>> Chapters(Guid id, ChaptersRequest r, CancellationToken ct) { await s.SetChaptersAsync(id, r, ct); return Done("Updated."); }
    [HttpPut("lessons/{id:guid}/slides"), Authorize(Roles = Mgr)] public async Task<ActionResult<ApiResponse<object?>>> Slides(Guid id, SlidesRequest r, CancellationToken ct) { await s.SetSlidesAsync(id, r, ct); return Done("Updated."); }
    [HttpGet("lessons/{id:guid}/interactive-quizzes"), Authorize(Roles = Any)] public async Task<ActionResult<ApiResponse<object>>> Quizzes(Guid id, CancellationToken ct) => Success(await s.QuizPointsAsync(id, ct));
    [HttpPut("lessons/{id:guid}/interactive-quizzes"), Authorize(Roles = Mgr)] public async Task<ActionResult<ApiResponse<object?>>> SetQuizzes(Guid id, QuizPointsRequest r, CancellationToken ct) { await s.SetQuizPointsAsync(id, r, ct); return Done("Updated."); }
    [HttpPost("interactive-quizzes/{id:guid}/answer"), Authorize(Roles = RoleNames.Student)] public async Task<ActionResult<ApiResponse<object>>> Answer(Guid id, QuizAnswerRequest r, CancellationToken ct) => Success(await s.AnswerQuizAsync(id, r, ct));

    [HttpPost("lessons/{id:guid}/media"), Authorize(Roles = Mgr), Consumes("multipart/form-data"), DisableRequestSizeLimit]
    public async Task<ActionResult<ApiResponse<object>>> Media(Guid id, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new SmartLMS.Domain.Exceptions.ValidationAppException("A file is required (form field \"file\").");
        await using var st = file.OpenReadStream(); return Success(await files.UploadMediaAsync(id, st, file.FileName, ct));
    }
    [HttpGet("lessons/{id:guid}/stream"), Authorize(Roles = Any)]
    public async Task<IActionResult> Stream(Guid id, CancellationToken ct)
    {
        var (c, t) = await files.StreamAsync(id, ct); Response.Headers["X-Content-Type-Options"] = "nosniff"; Response.Headers["Cache-Control"] = "private, no-store";
        return File(c, t, enableRangeProcessing: true);
    }
    [HttpPost("lessons/{id:guid}/resources"), Authorize(Roles = Mgr), Consumes("multipart/form-data"), RequestSizeLimit(30 * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<object>>> Resource(Guid id, IFormFile? file, [FromForm] string? title, [FromForm] bool isDownloadable = true, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0) throw new SmartLMS.Domain.Exceptions.ValidationAppException("A file is required (form field \"file\").");
        await using var st = file.OpenReadStream(); return Success(await files.UploadResourceAsync(id, st, file.FileName, title, isDownloadable, ct));
    }
    [HttpGet("lessons/{id:guid}/resources/{resourceId:guid}/download"), Authorize(Roles = Any)]
    public async Task<IActionResult> Download(Guid id, Guid resourceId, CancellationToken ct)
    {
        var (c, t, n) = await files.DownloadResourceAsync(id, resourceId, ct); Response.Headers["X-Content-Type-Options"] = "nosniff"; Response.Headers["Cache-Control"] = "private, no-store";
        return File(c, t, n);
    }
}
