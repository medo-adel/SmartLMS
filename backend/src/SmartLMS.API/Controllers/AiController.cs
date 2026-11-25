using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartLMS.Application.Common;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Enums;
using SmartLMS.Domain.Exceptions;
using SmartLMS.Infrastructure.Services.Ai;

namespace SmartLMS.API.Controllers;

/// <summary>The only door to the AI service. The browser and the Ionic app never see the Python service or its key.</summary>
[Route("api/v1/ai")]
public sealed class AiController(AiService s) : ApiControllerBase
{
    private const string Mgr = $"{RoleNames.TenantAdmin},{RoleNames.Instructor}";
    private const string Learn = $"{RoleNames.Student},{RoleNames.Instructor}";
    private const string Grd = $"{RoleNames.TenantAdmin},{RoleNames.Instructor},{RoleNames.TeachingAssistant}";
    private static readonly string[] AudioTypes = [".wav", ".mp3", ".m4a", ".ogg", ".webm", ".mp4", ".flac"];

    private ObjectResult QueuedJob(object job) => StatusCode(202, ApiResponse<object>.Ok(job, "Queued."));

    [HttpPost("lessons/{lessonId:guid}/ingest"), Authorize(Roles = Mgr)] public async Task<ObjectResult> Ingest(Guid lessonId, CancellationToken ct) => QueuedJob(await s.QueueIngestAsync(lessonId, ct));
    [HttpGet("lessons/{lessonId:guid}/index-status"), Authorize(Roles = Mgr)] public async Task<ActionResult<ApiResponse<object>>> IndexStatus(Guid lessonId, CancellationToken ct) => Success(await s.IndexStatusAsync(lessonId, ct));
    [HttpPost("courses/{courseId:guid}/tutor/chat"), Authorize(Roles = Learn)] public async Task<ActionResult<ApiResponse<AiTutorResult>>> Tutor(Guid courseId, TutorRequest r, CancellationToken ct) => Success(await s.TutorAsync(courseId, r, ct));
    [HttpPost("courses/{courseId:guid}/video/semantic-search"), Authorize(Roles = Learn)] public async Task<ActionResult<ApiResponse<AiSearchResult>>> Search(Guid courseId, SearchRequest r, CancellationToken ct) => Success(await s.SearchAsync(courseId, r, ct));
    [HttpPost("lessons/{lessonId:guid}/generate-quiz"), Authorize(Roles = Grd)] public async Task<ActionResult<ApiResponse<object>>> Quiz(Guid lessonId, QuizRequest? r, CancellationToken ct) => Success(await s.QuizAsync(lessonId, r ?? new(null, null, null, null), ct));
    [HttpPost("lessons/{lessonId:guid}/summarize"), Authorize(Roles = Learn)] public async Task<ActionResult<ApiResponse<AiSummaryResult>>> Summarize(Guid lessonId, CancellationToken ct) => Success(await s.SummarizeAsync(lessonId, ct));
    [HttpPost("lessons/{lessonId:guid}/mindmap"), Authorize(Roles = Learn)] public async Task<ActionResult<ApiResponse<AiMindMapResult>>> MindMap(Guid lessonId, CancellationToken ct) => Success(await s.MindMapAsync(lessonId, ct));
    [HttpPost("submissions/{id:guid}/grade-essay"), Authorize(Roles = Grd)] public async Task<ActionResult<ApiResponse<object>>> Grade(Guid id, CancellationToken ct) => Success(await s.GradeEssayAsync(id, ct));
    [HttpPost("assignments/{id:guid}/plagiarism-check"), Authorize(Roles = Mgr)] public async Task<ObjectResult> Plagiarism(Guid id, CancellationToken ct) => QueuedJob(await s.QueuePlagiarismAsync(id, ct));
    [HttpGet("submissions/{id:guid}/plagiarism"), Authorize(Roles = Mgr)] public async Task<ActionResult<ApiResponse<object>>> PlagiarismResult(Guid id, CancellationToken ct) => Success(await s.PlagiarismResultAsync(id, ct));
    [HttpPost("courses/{courseId:guid}/study-plan"), Authorize(Roles = RoleNames.Student)] public async Task<ActionResult<ApiResponse<AiStudyPlanResult>>> StudyPlan(Guid courseId, StudyPlanBody r, CancellationToken ct) => Success(await s.StudyPlanAsync(courseId, r, ct));
    [HttpGet("students/{studentId:guid}/risk"), Authorize(Roles = Mgr)] public async Task<ActionResult<ApiResponse<object>>> Risk(Guid studentId, [FromQuery] Guid courseId, CancellationToken ct) => Success(await s.RiskAsync(studentId, courseId, ct));

    [HttpPost("courses/{courseId:guid}/voice-tutor"), Authorize(Roles = RoleNames.Student), RequestSizeLimit(26 * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<AiVoiceResult>>> Voice(Guid courseId, IFormFile audio, [FromForm] string? language, CancellationToken ct)
    {
        if (audio is null || audio.Length == 0) throw new ValidationAppException("An audio file is required.");
        if (audio.Length > 25 * 1024 * 1024) throw new ValidationAppException("The audio file is too large (25 MB at most).");
        var ext = Path.GetExtension(audio.FileName).ToLowerInvariant();
        if (!AudioTypes.Contains(ext)) throw new ValidationAppException("Unsupported audio type.");
        await using var st = audio.OpenReadStream();
        return Success(await s.VoiceAsync(courseId, st, "audio" + ext, language, ct));
    }
}
