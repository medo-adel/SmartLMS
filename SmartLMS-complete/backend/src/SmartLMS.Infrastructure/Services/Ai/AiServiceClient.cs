using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SmartLMS.Application.Interfaces;
using SmartLMS.Domain.Exceptions;
using SmartLMS.Infrastructure.Options;

namespace SmartLMS.Infrastructure.Services.Ai;

public sealed class AiServiceClient(HttpClient http, IOptions<AiServiceOptions> opt) : IAiServiceClient
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never };
    private static string Sn(JsonNamingPolicy p, string s) => p.ConvertName(s);

    private static object Body(AiScope s, object extra)
    {
        var d = new Dictionary<string, object?> { ["tenant_id"] = s.TenantId, ["course_id"] = s.CourseId };
        foreach (var pr in extra.GetType().GetProperties()) d[Sn(JsonNamingPolicy.SnakeCaseLower, pr.Name)] = pr.GetValue(extra);
        return d;
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(opt.Value.ApiKey)) throw new AppException(503, "AI_NOT_CONFIGURED", "The AI service key is not configured (AiService:ApiKey).");
        req.Headers.Add("X-AI-Service-Key", opt.Value.ApiKey);
        HttpResponseMessage r;
        try { r = await http.SendAsync(req, ct); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new AppException(504, "AI_TIMEOUT", "The AI service took too long to answer."); }
        catch (HttpRequestException) { throw new AppException(503, "AI_UNAVAILABLE", "The AI service is not reachable."); }
        using (r)
        {
            if (r.IsSuccessStatusCode) return (await r.Content.ReadFromJsonAsync<T>(Json, ct)) ?? throw new AppException(502, "AI_BAD_RESPONSE", "The AI service returned an empty answer.");
            // Upstream detail is never forwarded: it can carry course text or provider messages.
            throw r.StatusCode switch
            {
                HttpStatusCode.Unauthorized => new AppException(503, "AI_NOT_CONFIGURED", "The AI service rejected the API's key."),
                HttpStatusCode.Conflict => new ConflictAppException("AI_NOT_INDEXED", "This lesson has no indexed content. Ingest it first."),
                HttpStatusCode.UnprocessableEntity => new ValidationAppException("The AI service could not process this request."),
                HttpStatusCode.RequestEntityTooLarge => new ValidationAppException("The audio file is too large."),
                HttpStatusCode.BadGateway => new AppException(502, "AI_BAD_OUTPUT", "The AI model returned output that could not be validated. Try again."),
                _ => new AppException(503, "AI_UNAVAILABLE", "The AI service or its model provider is unavailable."),
            };
        }
    }

    private Task<T> Post<T>(string path, AiScope s, object extra, CancellationToken ct) =>
        SendAsync<T>(new HttpRequestMessage(HttpMethod.Post, "ai/" + path) { Content = JsonContent.Create(Body(s, extra), options: Json) }, ct);

    public Task<AiIngestResult> IngestAsync(AiScope s, string lessonId, string sourceType, string sourceUri, string? text, string? language, CancellationToken ct) =>
        Post<AiIngestResult>("ingest", s, new { LessonId = lessonId, SourceType = sourceType, SourceUri = sourceUri, Text = text, Language = language }, ct);
    public Task<AiTutorResult> TutorChatAsync(AiScope s, string question, string? language, CancellationToken ct) => Post<AiTutorResult>("tutor/chat", s, new { Question = question, Language = language }, ct);
    public Task<AiSearchResult> SemanticSearchAsync(AiScope s, string query, int topK, CancellationToken ct) => Post<AiSearchResult>("video/semantic-search", s, new { Query = query, TopK = topK }, ct);
    public Task<AiQuizResult> GenerateQuizAsync(AiScope s, string lessonId, int mcq, int open, int cards, string difficulty, CancellationToken ct) =>
        Post<AiQuizResult>("generate-quiz", s, new { LessonId = lessonId, McqCount = mcq, OpenEndedCount = open, FlashcardCount = cards, Difficulty = difficulty }, ct);
    public Task<AiSummaryResult> SummarizeAsync(AiScope s, string lessonId, CancellationToken ct) => Post<AiSummaryResult>("summarize", s, new { LessonId = lessonId }, ct);
    public Task<AiMindMapResult> MindMapAsync(AiScope s, string lessonId, CancellationToken ct) => Post<AiMindMapResult>("mindmap", s, new { LessonId = lessonId }, ct);
    public Task<AiGradeResult> GradeEssayAsync(AiScope s, string assignmentId, string essay, IReadOnlyList<AiRubricCriterion> rubric, CancellationToken ct) =>
        Post<AiGradeResult>("grade-essay", s, new { AssignmentId = assignmentId, EssayText = essay, Rubric = rubric }, ct);
    public Task<AiPlagiarismResult> PlagiarismCheckAsync(AiScope s, string assignmentId, string submissionId, string text, CancellationToken ct) =>
        Post<AiPlagiarismResult>("plagiarism-check", s, new { AssignmentId = assignmentId, SubmissionId = submissionId, Text = text }, ct);
    public Task<AiStudyPlanResult> StudyPlanAsync(AiScope s, IReadOnlyList<AiStudyLesson> lessons, double totalHours, double hoursPerWeek, string startDate, string targetDate, CancellationToken ct) =>
        Post<AiStudyPlanResult>("study-plan", s, new { Lessons = lessons, TotalLessonHours = totalHours, HoursPerWeekAvailable = hoursPerWeek, StartDate = startDate, TargetDate = targetDate }, ct);
    public Task<AiRiskResult> PredictRiskAsync(AiScope s, string studentId, AiRiskFeatures f, CancellationToken ct) => Post<AiRiskResult>("predict-risk", s, new { StudentId = studentId, Features = f }, ct);

    public Task<AiVoiceResult> VoiceTutorAsync(AiScope s, Stream audio, string fileName, string? language, CancellationToken ct)
    {
        var mp = new MultipartFormDataContent();
        var file = new StreamContent(audio); file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        mp.Add(file, "audio", Path.GetFileName(fileName)); mp.Add(new StringContent(s.TenantId), "tenant_id"); mp.Add(new StringContent(s.CourseId), "course_id");
        if (language is not null) mp.Add(new StringContent(language), "language");
        return SendAsync<AiVoiceResult>(new HttpRequestMessage(HttpMethod.Post, "ai/voice-tutor") { Content = mp }, ct);
    }
}
