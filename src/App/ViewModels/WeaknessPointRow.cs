using System.Collections.ObjectModel;

namespace KnowledgeWeakness.App.ViewModels;

public class WeaknessPointRow
{
    public string KnowledgePoint { get; init; } = "";
    public int WrongCount { get; init; }
    public int TotalCount { get; init; }
    public double WrongRate { get; init; }
    public string WrongRateText => TotalCount == 0 ? "0%" : $"{WrongRate:P0}";
    public ObservableCollection<WeakQuestionExampleRow> Examples { get; } = new();
}
