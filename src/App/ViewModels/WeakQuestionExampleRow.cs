namespace KnowledgeWeakness.App.ViewModels;

public class WeakQuestionExampleRow
{
    public int PaperId { get; init; }
    public string PaperTitle { get; init; } = "";
    public string QuestionNumber { get; init; } = "";
    public string Stem { get; init; } = "";
    public string? StudentAnswer { get; init; }
    public string? StandardAnswer { get; init; }
    public string? TeacherComment { get; init; }
}
