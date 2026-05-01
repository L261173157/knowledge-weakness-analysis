using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KnowledgeWeakness.Core.AI;
using KnowledgeWeakness.Core.Abstractions;
using KnowledgeWeakness.Core.Analysis;
using KnowledgeWeakness.Core.Domain;
using KnowledgeWeakness.Infrastructure.Analysis;

namespace KnowledgeWeakness.App.ViewModels;

public partial class AnalysisViewModel : ViewModelBase
{
    private readonly IPaperRepository _paperRepo;
    private readonly IStudentRepository _studentRepo;
    private readonly ISubjectRepository _subjectRepo;
    private readonly IWeaknessAnalyzer _weaknessAnalyzer;
    private readonly KnowledgeBaseReader _knowledgeBaseReader;

    public ObservableCollection<Student> Students { get; } = new();
    public ObservableCollection<Subject> Subjects { get; } = new();
    public ObservableCollection<WeaknessPointRow> Points { get; } = new();
    public ObservableCollection<WeakQuestionExampleRow> Examples { get; } = new();

    [ObservableProperty] private Student? _selectedStudent;
    [ObservableProperty] private Subject? _selectedSubject;
    [ObservableProperty] private WeaknessPointRow? _selectedPoint;
    [ObservableProperty] private string _selectedWeakReason = "";
    [ObservableProperty] private string _selectedReviewAdvice = "";
    [ObservableProperty] private string _selectedPracticeDirection = "";
    [ObservableProperty] private string _summary = "暂无分析数据";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isBusy;

    public AnalysisViewModel(
        IPaperRepository paperRepo,
        IStudentRepository studentRepo,
        ISubjectRepository subjectRepo,
        IWeaknessAnalyzer weaknessAnalyzer,
        KnowledgeBaseReader knowledgeBaseReader)
    {
        _paperRepo = paperRepo;
        _studentRepo = studentRepo;
        _subjectRepo = subjectRepo;
        _weaknessAnalyzer = weaknessAnalyzer;
        _knowledgeBaseReader = knowledgeBaseReader;
        _ = LoadAsync();
    }

    partial void OnSelectedPointChanged(WeaknessPointRow? value)
    {
        Examples.Clear();
        SelectedWeakReason = value?.WeakReason ?? "";
        SelectedReviewAdvice = value?.ReviewAdvice ?? "";
        SelectedPracticeDirection = value?.PracticeDirection ?? "";
        if (value is null) return;
        foreach (var example in value.Examples)
            Examples.Add(example);
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            Status = "分析中...";

            await LoadFiltersAsync();
            var papers = await _paperRepo.ListWithQuestionsAsync();
            if (SelectedStudent is not null)
                papers = papers.Where(p => p.StudentId == SelectedStudent.Id).ToList();
            if (SelectedSubject is not null)
                papers = papers.Where(p => p.SubjectId == SelectedSubject.Id).ToList();

            var candidates = WeaknessCandidateSelector.Select(papers);
            Points.Clear();
            Examples.Clear();
            if (candidates.Count == 0)
            {
                Summary = $"卷子 {papers.Count} 份，没有可进入分析的薄弱题";
                Status = "未发现已人工确认且明确批改的错题/扣分题。";
                return;
            }

            var studentName = SelectedStudent?.Name ?? candidates.FirstOrDefault()?.StudentName ?? "";
            var studentGrade = SelectedStudent?.Grade ?? candidates.FirstOrDefault()?.StudentGrade ?? "";
            var subjectName = SelectedSubject?.Name ?? candidates.FirstOrDefault()?.SubjectName ?? "";
            var knowledgeBase = await _knowledgeBaseReader.ReadAsync(SelectedSubject?.KnowledgeBasePath);
            var result = await _weaknessAnalyzer.AnalyzeAsync(new AiWeaknessAnalysisRequest(
                studentName,
                studentGrade,
                subjectName,
                knowledgeBase,
                candidates));

            foreach (var point in result.Points)
            {
                var relatedCandidates = candidates
                    .Where(x => point.QuestionNumbers.Contains(x.QuestionNumber))
                    .ToList();

                var row = new WeaknessPointRow
                {
                    KnowledgePoint = point.KnowledgePoint,
                    Severity = point.Severity,
                    WeakReason = point.WeakReason,
                    ReviewAdvice = point.ReviewAdvice,
                    PracticeDirection = point.PracticeDirection,
                    QuestionNumbersText = string.Join(", ", point.QuestionNumbers),
                    WrongCount = relatedCandidates.Count,
                    TotalCount = candidates.Count,
                    WrongRate = candidates.Count == 0 ? 0 : (double)relatedCandidates.Count / candidates.Count
                };

                foreach (var example in relatedCandidates.Take(5))
                {
                    row.Examples.Add(new WeakQuestionExampleRow
                    {
                        PaperId = example.PaperId,
                        PaperTitle = example.PaperTitle,
                        QuestionNumber = example.QuestionNumber,
                        Stem = example.Stem,
                        StudentAnswer = example.StudentAnswer,
                        StandardAnswer = example.StandardAnswer,
                        TeacherComment = example.TeacherComment
                    });
                }

                Points.Add(row);
            }

            SelectedPoint = Points.FirstOrDefault();
            Summary = $"卷子 {papers.Count} 份，候选薄弱题 {candidates.Count} 道。AI 总结：{result.Summary}";
            Status = Points.Count == 0
                ? "AI 未返回薄弱点，请重试或检查候选题内容。"
                : knowledgeBase is null
                    ? $"已生成 {Points.Count} 个薄弱点。未使用知识库。"
                    : $"已生成 {Points.Count} 个薄弱点。已接入知识库。";
        }
        catch (Exception ex)
        {
            Status = "分析失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ClearFilterAsync()
    {
        SelectedStudent = null;
        SelectedSubject = null;
        await LoadAsync();
    }

    private async Task LoadFiltersAsync()
    {
        var students = await _studentRepo.ListAsync();
        var subjects = await _subjectRepo.ListAsync();

        Students.Clear();
        foreach (var student in students)
            Students.Add(student);
        if (SelectedStudent is not null && Students.All(s => s.Id != SelectedStudent.Id))
            SelectedStudent = null;

        Subjects.Clear();
        foreach (var subject in subjects)
            Subjects.Add(subject);
        if (SelectedSubject is not null && Subjects.All(s => s.Id != SelectedSubject.Id))
            SelectedSubject = null;
    }
}
