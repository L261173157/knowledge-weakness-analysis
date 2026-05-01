using System.Collections.Generic;
using KnowledgeWeakness.Core.Domain;

namespace KnowledgeWeakness.Core.AI;

public record ExtractedQuestion(
    string Number,
    QuestionType Type,
    string Stem,
    string? StandardAnswer,
    string StudentAnswer,
    bool IsCorrect,
    double? PartialScore,
    string? TeacherComment);

public record PaperExtraction(
    string? Title,
    string? PaperDateText,
    IReadOnlyList<ExtractedQuestion> Questions,
    string RawJson);

public record SubjectContext(
    string Code,
    string Name,
    string Grade,
    string? ExtractionHints);
