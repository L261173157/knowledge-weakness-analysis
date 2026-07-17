using System;
using KnowledgeWeakness.Core.Domain;

namespace KnowledgeWeakness.App.ViewModels;

public class PaperRow
{
    public int Id { get; init; }
    public string StudentName { get; init; } = "";
    public string SubjectName { get; init; } = "";
    public DateOnly Date { get; init; }
    public string Title { get; init; } = "";
    public ImportStatus Status { get; init; }
    public int QuestionCount { get; init; }
    public int WrongCount { get; init; }
    public string StatusText => Status switch
    {
        ImportStatus.Raw => "未识别",
        ImportStatus.Extracted => "已识别",
        ImportStatus.Reviewed => "已校对",
        ImportStatus.Analyzed => "已分析",
        _ => Status.ToString()
    };
    public string ScoreText => QuestionCount == 0
        ? "—"
        : $"{QuestionCount - WrongCount}/{QuestionCount}";
    public double Accuracy => QuestionCount == 0 ? 0 : 1.0 * (QuestionCount - WrongCount) / QuestionCount;
    public string AccuracyText => QuestionCount == 0 ? "—" : $"{Accuracy:P0}";
}
