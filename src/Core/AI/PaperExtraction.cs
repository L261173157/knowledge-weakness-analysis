using System.Collections.Generic;
using KnowledgeWeakness.Core.Domain;

namespace KnowledgeWeakness.Core.AI;

public record ExtractedQuestion(
    string Number,
    QuestionType Type,
    string Stem,
    IReadOnlyDictionary<string, string> Options,
    string? StandardAnswerOption,
    string? StandardAnswerText,
    string? StudentAnswerOption,
    string StudentAnswerText,
    bool IsCorrect,
    double? PartialScore,
    string? TeacherComment,
    bool? TeacherIsCorrect = null,
    bool? AiIsCorrect = null);

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
