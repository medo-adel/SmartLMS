using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartLMS.Application.Common;
using SmartLMS.Domain.Enums;
using SmartLMS.Infrastructure.Services.Lms;

namespace SmartLMS.API.Controllers;

[Route("api/v1"), Authorize]
public sealed class AssessmentsController(AssessmentService svc) : ApiControllerBase
{
    private const string Authors = $"{RoleNames.Instructor},{RoleNames.TenantAdmin}";
    private const string Everyone_NoStudent = $"{RoleNames.Instructor},{RoleNames.TenantAdmin},{RoleNames.TeachingAssistant}";
    private const string ST = RoleNames.Student;
    private const string Everyone = $"{RoleNames.Student},{RoleNames.Instructor},{RoleNames.TenantAdmin},{RoleNames.TeachingAssistant}";

    [HttpPost("question-banks"), Authorize(Roles = Authors)]
    public async Task<ActionResult<ApiResponse<object>>> Bank(BankRequest r, CancellationToken ct) => Created("", ApiResponse<object>.Ok(new { id = await svc.CreateBankAsync(r, ct) }));
    [HttpPost("question-banks/{id:guid}/questions"), Authorize(Roles = Authors)]
    public async Task<ActionResult<ApiResponse<object>>> Question(Guid id, QuestionRequest r, CancellationToken ct) => Created("", ApiResponse<object>.Ok(new { id = await svc.AddQuestionAsync(id, r, ct) }));
    [HttpGet("courses/{courseId:guid}/exams"), Authorize(Roles = Everyone)]
    public async Task<ActionResult<ApiResponse<object>>> Exams(Guid courseId, CancellationToken ct) => Success(await svc.ListExamsAsync(courseId, ct));
    [HttpPost("courses/{courseId:guid}/exams"), Authorize(Roles = Authors)]
    public async Task<ActionResult<ApiResponse<object>>> CreateExam(Guid courseId, ExamRequest r, CancellationToken ct) => Created("", ApiResponse<object>.Ok(new { id = await svc.CreateExamAsync(courseId, r, ct) }));
    [HttpPost("exams/{id:guid}/publish"), Authorize(Roles = Authors)]
    public async Task<ActionResult<ApiResponse<object?>>> Publish(Guid id, CancellationToken ct) { await svc.PublishAsync(id, ct); return Done("Published."); }
    [HttpGet("exams/{id:guid}/attempts"), Authorize(Roles = Everyone_NoStudent)]
    public async Task<ActionResult<ApiResponse<object>>> Attempts(Guid id, CancellationToken ct) => Success(await svc.ExamAttemptsAsync(id, ct));

    [HttpPost("exams/{id:guid}/attempts"), Authorize(Roles = ST)]
    public async Task<ActionResult<ApiResponse<object>>> Start(Guid id, CancellationToken ct) => Success(await svc.StartAsync(id, ct));
    [HttpGet("attempts/{id:guid}"), Authorize(Roles = ST)]
    public async Task<ActionResult<ApiResponse<object>>> Attempt(Guid id, CancellationToken ct) => Success(await svc.GetAttemptAsync(id, ct));
    [HttpPut("attempts/{id:guid}/answers"), Authorize(Roles = ST)]
    public async Task<ActionResult<ApiResponse<object?>>> Answers(Guid id, AnswersRequest r, CancellationToken ct) { await svc.SaveAnswersAsync(id, r, ct); return Done("Saved."); }
    [HttpPost("attempts/{id:guid}/events"), Authorize(Roles = ST)]
    public async Task<ActionResult<ApiResponse<object>>> Event(Guid id, AttemptEventRequest r, CancellationToken ct) => Success(await svc.EventAsync(id, r, ct));
    [HttpPost("attempts/{id:guid}/submit"), Authorize(Roles = ST)]
    public async Task<ActionResult<ApiResponse<object>>> Submit(Guid id, CancellationToken ct) => Success(await svc.SubmitAsync(id, ct));
    [HttpGet("attempts/{id:guid}/result"), Authorize(Roles = Everyone)]
    public async Task<ActionResult<ApiResponse<object>>> Result(Guid id, CancellationToken ct) => Success(await svc.ResultAsync(id, ct));
    [HttpPut("attempt-answers/{id:guid}/grade"), Authorize(Roles = Everyone_NoStudent)]
    public async Task<ActionResult<ApiResponse<object?>>> Grade(Guid id, GradeRequest r, CancellationToken ct) { await svc.GradeAnswerAsync(id, r, ct); return Done("Graded."); }

    [HttpGet("courses/{courseId:guid}/question-banks"), Authorize(Roles = Authors)] public async Task<ActionResult<ApiResponse<object>>> Banks(Guid courseId, CancellationToken ct) => Success(await svc.ListBanksAsync(courseId, ct));
    [HttpGet("question-banks/{id:guid}/questions"), Authorize(Roles = Authors)] public async Task<ActionResult<ApiResponse<object>>> Questions(Guid id, [FromQuery] string? type, [FromQuery] string? difficulty, [FromQuery] string? topic, CancellationToken ct) => Success(await svc.ListQuestionsAsync(id, type, difficulty, topic, ct));
    [HttpPut("questions/{id:guid}"), Authorize(Roles = Authors)] public async Task<ActionResult<ApiResponse<object?>>> UpdateQuestion(Guid id, QuestionRequest r, CancellationToken ct) { await svc.UpdateQuestionAsync(id, r, ct); return Done("Updated."); }
    [HttpDelete("questions/{id:guid}"), Authorize(Roles = Authors)] public async Task<ActionResult<ApiResponse<object?>>> DeleteQuestion(Guid id, CancellationToken ct) { await svc.DeleteQuestionAsync(id, ct); return Done("Deleted."); }
    [HttpPost("question-banks/{id:guid}/import"), Authorize(Roles = Authors)] public async Task<ActionResult<ApiResponse<object>>> Import(Guid id, AssessmentService.ImportRequest r, CancellationToken ct) => Success(await svc.ImportAsync(id, r, ct));
    [HttpGet("exams/{id:guid}"), Authorize(Roles = Authors)] public async Task<ActionResult<ApiResponse<object>>> GetExam(Guid id, CancellationToken ct) => Success(await svc.GetExamAsync(id, ct));
    [HttpPut("exams/{id:guid}"), Authorize(Roles = Authors)] public async Task<ActionResult<ApiResponse<object?>>> UpdateExam(Guid id, ExamRequest r, CancellationToken ct) { await svc.UpdateExamAsync(id, r, ct); return Done("Updated."); }
}
