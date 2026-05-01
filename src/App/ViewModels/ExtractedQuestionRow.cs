using CommunityToolkit.Mvvm.ComponentModel;
using KnowledgeWeakness.Core.Domain;

namespace KnowledgeWeakness.App.ViewModels;

public partial class ExtractedQuestionRow : ObservableObject
{
    [ObservableProperty] private string _number = "";
    [ObservableProperty] private QuestionType _type = QuestionType.Unknown;
    [ObservableProperty] private string _stem = "";
    [ObservableProperty] private string? _standardAnswer;
    [ObservableProperty] private string _studentAnswer = "";
    [ObservableProperty] private bool _isCorrect;
    [ObservableProperty] private double? _partialScore;
    [ObservableProperty] private string? _teacherComment;
}
