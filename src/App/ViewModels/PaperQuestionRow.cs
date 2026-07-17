namespace KnowledgeWeakness.App.ViewModels;

public class PaperQuestionRow
{
    public string Number { get; init; } = "";
    public string Type { get; init; } = "";
    public string Stem { get; init; } = "";
    public string? StandardAnswer { get; init; }
    public string? StudentAnswer { get; init; }
    public bool IsCorrect { get; init; }
    public string CorrectText => IsCorrect ? "正确" : "错误";
    public double? PartialScore { get; init; }
    public string? TeacherComment { get; init; }
}
