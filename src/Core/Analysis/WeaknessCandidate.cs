namespace KnowledgeWeakness.Core.Analysis;

public record WeaknessCandidate(
    int PaperId,
    string PaperTitle,
    string StudentName,
    string StudentGrade,
    string SubjectName,
    DateOnly PaperDate,
    string QuestionNumber,
    string QuestionType,
    string Stem,
    string? StandardAnswer,
    string StudentAnswer,
    bool IsCorrect,
    double? PartialScore,
    string? TeacherComment);
