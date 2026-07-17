using System;

namespace KnowledgeWeakness.App.ViewModels;

public class TrendPoint
{
    public DateOnly Date { get; init; }
    public string PaperTitle { get; init; } = "";
    public int Total { get; init; }
    public int Wrong { get; init; }
    public double Accuracy => Total == 0 ? 0 : 1.0 * (Total - Wrong) / Total;
    public string AccuracyText => $"{Accuracy:P0}";
}
