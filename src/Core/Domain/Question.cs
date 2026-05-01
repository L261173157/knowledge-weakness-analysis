namespace KnowledgeWeakness.Core.Domain;

public enum QuestionType
{
    Choice = 0,
    FillBlank = 1,
    ShortAnswer = 2,
    Essay = 3,
    Unknown = 99
}

public class Question
{
    public int Id { get; set; }
    public int PaperId { get; set; }
    public Paper? Paper { get; set; }

    public string Number { get; set; } = string.Empty;
    public QuestionType Type { get; set; } = QuestionType.Unknown;

    public string Stem { get; set; } = string.Empty;
    public string? StandardAnswer { get; set; }

    public double? MaxScore { get; set; }

    public StudentAnswer? StudentAnswer { get; set; }
}
