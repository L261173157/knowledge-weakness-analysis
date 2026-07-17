using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KnowledgeWeakness.Core.Analysis;
using KnowledgeWeakness.Core.Domain;

namespace KnowledgeWeakness.App.ViewModels;

public partial class ExtractedQuestionRow : ObservableObject
{
    [ObservableProperty] private string _number = "";
    [ObservableProperty] private QuestionType _type = QuestionType.Unknown;
    [ObservableProperty] private string _stem = "";
    [ObservableProperty] private string? _standardAnswerOption;
    [ObservableProperty] private string? _standardAnswerText;
    [ObservableProperty] private string? _studentAnswerOption;
    [ObservableProperty] private string _studentAnswerText = "";
    [ObservableProperty] private bool _isCorrect;
    [ObservableProperty] private double? _partialScore;
    [ObservableProperty] private string? _teacherComment;
    [ObservableProperty] private bool? _teacherIsCorrect;
    [ObservableProperty] private bool? _aiIsCorrect;
    [ObservableProperty] private bool? _answerTextIsCorrect;
    [ObservableProperty] private bool _needsReview;

    public string CombinedStandardAnswer => AnswerTextHelper.Combine(StandardAnswerOption, StandardAnswerText);

    public string CombinedStudentAnswer => AnswerTextHelper.Combine(StudentAnswerOption, StudentAnswerText);

    public string TeacherJudgmentText => ToJudgmentText(TeacherIsCorrect);

    public string AiJudgmentText => ToJudgmentText(AiIsCorrect);

    public string AnswerTextJudgmentText => ToJudgmentText(AnswerTextIsCorrect);

    public string ReviewStatusText => NeedsReview ? "需复核" : "一致";

    public string ReviewActionText => NeedsReview ? "确认此判定" : "已复核";

    partial void OnTeacherIsCorrectChanged(bool? value) => OnPropertyChanged(nameof(TeacherJudgmentText));

    partial void OnAiIsCorrectChanged(bool? value) => OnPropertyChanged(nameof(AiJudgmentText));

    partial void OnAnswerTextIsCorrectChanged(bool? value) => OnPropertyChanged(nameof(AnswerTextJudgmentText));

    partial void OnNeedsReviewChanged(bool value)
    {
        OnPropertyChanged(nameof(ReviewStatusText));
        OnPropertyChanged(nameof(ReviewActionText));
    }

    // Treat a human edit of the IsCorrect cell as the user's adjudication —
    // clear NeedsReview so SaveAsync stops blocking on this row.
    // (Note: CommunityToolkit's partial OnXxxChanged only fires when the value
    // actually changes — if the imported value already matches the user's
    // intended correction, this hook won't run. The explicit
    // <see cref="AcceptCurrentJudgmentCommand"/> covers that case.)
    partial void OnIsCorrectChanged(bool value)
    {
        if (NeedsReview) NeedsReview = false;
    }

    /// <summary>
    /// Explicit per-row adjudication: clears <see cref="NeedsReview"/> without
    /// requiring the user to flip and unflip the correctness checkbox. Wired
    /// to the "确认此判定" button column so users can accept the imported
    /// judgment exactly as-is.
    /// </summary>
    [RelayCommand]
    private void AcceptCurrentJudgment()
    {
        if (NeedsReview) NeedsReview = false;
    }

    private static string ToJudgmentText(bool? value)
    {
        return value switch
        {
            true => "正确",
            false => "错误",
            null => "未知"
        };
    }
}
