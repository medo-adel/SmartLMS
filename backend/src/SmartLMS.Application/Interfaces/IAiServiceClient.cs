namespace SmartLMS.Application.Interfaces;

// Typed mirror of ai-service/app/schemas.py. The wire format is snake_case JSON.
public sealed record AiScope(string TenantId, string CourseId);
public sealed record AiCitation(string LessonId, int? Page, double? StartSeconds, double? EndSeconds, string Snippet);
public sealed record AiTranscriptCue(double Start, double End, string Text);
public sealed record AiIngestResult(string LessonId, int ChunksIndexed, int TranscriptSegments, IReadOnlyList<AiTranscriptCue>? Cues);
public sealed record AiTutorResult(string Answer, List<AiCitation> Citations, bool Grounded);
public sealed record AiSearchHit(string LessonId, double StartSeconds, double EndSeconds, string Snippet, double Score);
public sealed record AiSearchResult(List<AiSearchHit> Hits);
public sealed record AiMcq(string Question, List<string> Options, int CorrectIndex, string Explanation);
public sealed record AiOpenQuestion(string Question, string ModelAnswer);
public sealed record AiFlashcard(string Front, string Back);
public sealed record AiQuizResult(List<AiMcq> Mcqs, List<AiOpenQuestion> OpenEnded, List<AiFlashcard> Flashcards);
public sealed record AiChapter(string Title, double StartSeconds);
public sealed record AiSummaryResult(List<string> Takeaways, List<string> KeyTerms, List<AiChapter> Chapters);
public sealed record AiMindNode(string Id, string Label);
public sealed record AiMindEdge(string Source, string Target);
public sealed record AiMindMapResult(List<AiMindNode> Nodes, List<AiMindEdge> Edges);
public sealed record AiRubricCriterion(string Name, double MaxPoints, string Description);
public sealed record AiCriterionScore(string Name, double Points, string Feedback);
public sealed record AiGradeResult(bool Provisional, double TotalPoints, double MaxPoints, List<AiCriterionScore> Criteria, string Feedback);
public sealed record AiPlagiarismMatch(string OtherSubmissionId, double Similarity);
public sealed record AiPlagiarismResult(double MaxSimilarity, List<AiPlagiarismMatch> Flagged, string Note);
public sealed record AiVoiceResult(string Transcript, string AnswerText, string? AudioBase64, string? AudioMime, List<AiCitation> Citations);
public sealed record AiStudyLesson(string Title, double Hours);
public sealed record AiStudyDay(string Date, string Focus, int Minutes);
public sealed record AiStudyPlanResult(List<AiStudyDay> Days, bool Feasible, string? Note);
public sealed record AiRiskFeatures(int DaysSinceLastLogin, double LessonsCompletedRatio, double? AverageQuizScore, double WeeklyStudyMinutes, int OverdueAssignments);
public sealed record AiRiskResult(double RiskScore, string Level, List<string> Interventions, string Note);

/// <summary>The only way the .NET API reaches the Python AI service. Adds the service key, a timeout and maps failures to AppExceptions with no upstream text.</summary>
public interface IAiServiceClient
{
    Task<AiIngestResult> IngestAsync(AiScope s, string lessonId, string sourceType, string sourceUri, string? text, string? language, CancellationToken ct);
    Task<AiTutorResult> TutorChatAsync(AiScope s, string question, string? language, CancellationToken ct);
    Task<AiSearchResult> SemanticSearchAsync(AiScope s, string query, int topK, CancellationToken ct);
    Task<AiQuizResult> GenerateQuizAsync(AiScope s, string lessonId, int mcq, int open, int cards, string difficulty, CancellationToken ct);
    Task<AiSummaryResult> SummarizeAsync(AiScope s, string lessonId, CancellationToken ct);
    Task<AiMindMapResult> MindMapAsync(AiScope s, string lessonId, CancellationToken ct);
    Task<AiGradeResult> GradeEssayAsync(AiScope s, string assignmentId, string essay, IReadOnlyList<AiRubricCriterion> rubric, CancellationToken ct);
    Task<AiPlagiarismResult> PlagiarismCheckAsync(AiScope s, string assignmentId, string submissionId, string text, CancellationToken ct);
    Task<AiVoiceResult> VoiceTutorAsync(AiScope s, Stream audio, string fileName, string? language, CancellationToken ct);
    Task<AiStudyPlanResult> StudyPlanAsync(AiScope s, IReadOnlyList<AiStudyLesson> lessons, double totalHours, double hoursPerWeek, string startDate, string targetDate, CancellationToken ct);
    Task<AiRiskResult> PredictRiskAsync(AiScope s, string studentId, AiRiskFeatures f, CancellationToken ct);
}
