namespace KnowledgeWeakness.Core.Analysis;

public record AiWeaknessPoint(
    string KnowledgePoint,
    string Severity,
    string WeakReason,
    IReadOnlyList<string> QuestionNumbers,
    string ReviewAdvice,
    string PracticeDirection);

public record AiWeaknessAnalysisResult(
    string Summary,
    IReadOnlyList<AiWeaknessPoint> Points,
    string RawJson);

public record AiWeaknessAnalysisRequest(
    string StudentName,
    string StudentGrade,
    string SubjectName,
    string? KnowledgeBase,
    IReadOnlyList<WeaknessCandidate> Candidates);
