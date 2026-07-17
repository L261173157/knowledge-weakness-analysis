using System;

namespace KnowledgeWeakness.App.ViewModels;

public class MistakeRow
{
    public int PaperId { get; init; }
    public DateOnly PaperDate { get; init; }
    public string PaperTitle { get; init; } = "";
    public string StudentName { get; init; } = "";
    public string SubjectName { get; init; } = "";
    public string KnowledgePoint { get; init; } = "";
    public string QuestionNumber { get; init; } = "";
    public string Stem { get; init; } = "";
    public string? StudentAnswer { get; init; }
    public string? StandardAnswer { get; init; }
    public double? PartialScore { get; init; }
    public string? TeacherComment { get; init; }
}
