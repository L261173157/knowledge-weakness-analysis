namespace KnowledgeWeakness.Core.Domain;

public class StudentAnswer
{
    public int Id { get; set; }

    public int QuestionId { get; set; }
    public Question? Question { get; set; }

    public string AnswerText { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public double? PartialScore { get; set; }
    public string? TeacherComment { get; set; }
}
