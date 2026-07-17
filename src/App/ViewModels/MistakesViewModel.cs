using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KnowledgeWeakness.Core.Abstractions;
using KnowledgeWeakness.Core.Analysis;
using KnowledgeWeakness.Core.Domain;

namespace KnowledgeWeakness.App.ViewModels;

public partial class MistakesViewModel : ViewModelBase
{
    private readonly IPaperRepository _paperRepo;
    private readonly IStudentRepository _studentRepo;
    private readonly ISubjectRepository _subjectRepo;
    private readonly IKnowledgePointRepository _knowledgePointRepo;
    private List<MistakeRow> _all = new();

    public ObservableCollection<Student> Students { get; } = new();
    public ObservableCollection<Subject> Subjects { get; } = new();
    public ObservableCollection<string> KnowledgePoints { get; } = new();
    public ObservableCollection<MistakeRow> Mistakes { get; } = new();

    [ObservableProperty] private Student? _selectedStudent;
    [ObservableProperty] private Subject? _selectedSubject;
    [ObservableProperty] private string? _selectedKnowledgePoint;
    [ObservableProperty] private bool _onlyWithComment;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isBusy;

    public MistakesViewModel(
        IPaperRepository paperRepo,
        IStudentRepository studentRepo,
        ISubjectRepository subjectRepo,
        IKnowledgePointRepository knowledgePointRepo)
    {
        _paperRepo = paperRepo;
        _studentRepo = studentRepo;
        _subjectRepo = subjectRepo;
        _knowledgePointRepo = knowledgePointRepo;
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        Students.Clear();
        foreach (var s in await _studentRepo.ListAsync()) Students.Add(s);
        Subjects.Clear();
        foreach (var s in await _subjectRepo.ListAsync()) Subjects.Add(s);
        await LoadAsync();
    }

    partial void OnSelectedStudentChanged(Student? value) => _ = LoadAsync();
    partial void OnSelectedSubjectChanged(Subject? value) => _ = LoadAsync();
    partial void OnSelectedKnowledgePointChanged(string? value) => ApplyFilter();
    partial void OnOnlyWithCommentChanged(bool value) => ApplyFilter();

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            var all = await _paperRepo.ListWithQuestionsAsync();
            IEnumerable<Paper> filtered = all;
            if (SelectedStudent is not null) filtered = filtered.Where(p => p.StudentId == SelectedStudent.Id);
            if (SelectedSubject is not null) filtered = filtered.Where(p => p.SubjectId == SelectedSubject.Id);
            var papers = filtered.ToList();

            var knowledgePoints = SelectedSubject is null
                ? await _knowledgePointRepo.ListAsync()
                : await _knowledgePointRepo.ListBySubjectAsync(SelectedSubject.Id);
            var local = WeaknessAnalysisService.Analyze(papers, knowledgePoints);

            var pointByQuestionKey = local.Points
                .SelectMany(p => p.Examples.Select(e => (Key: (e.PaperId, e.QuestionNumber), p.KnowledgePoint)))
                .GroupBy(x => x.Key)
                .ToDictionary(g => g.Key, g => g.First().KnowledgePoint);

            _all = papers
                .SelectMany(p => p.Questions
                    .Where(q => q.StudentAnswer is not null && !q.StudentAnswer.IsCorrect)
                    .Select(q => new MistakeRow
                    {
                        PaperId = p.Id,
                        PaperDate = p.Date,
                        PaperTitle = p.Title,
                        StudentName = p.Student?.Name ?? "",
                        SubjectName = p.Subject?.Name ?? "",
                        QuestionNumber = q.Number,
                        Stem = q.Stem,
                        StudentAnswer = q.StudentAnswer!.AnswerText,
                        StandardAnswer = q.StandardAnswer,
                        PartialScore = q.StudentAnswer.PartialScore,
                        TeacherComment = q.StudentAnswer.TeacherComment,
                        KnowledgePoint = pointByQuestionKey.GetValueOrDefault((p.Id, q.Number), "未归类")
                    }))
                .OrderByDescending(m => m.PaperDate)
                .ThenBy(m => m.QuestionNumber)
                .ToList();

            KnowledgePoints.Clear();
            foreach (var k in _all.Select(m => m.KnowledgePoint).Distinct().OrderBy(x => x))
                KnowledgePoints.Add(k);

            ApplyFilter();
        }
        catch (Exception ex)
        {
            Status = "加载失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFilter()
    {
        IEnumerable<MistakeRow> q = _all;
        if (!string.IsNullOrEmpty(SelectedKnowledgePoint))
            q = q.Where(m => m.KnowledgePoint == SelectedKnowledgePoint);
        if (OnlyWithComment)
            q = q.Where(m => !string.IsNullOrWhiteSpace(m.TeacherComment));

        Mistakes.Clear();
        foreach (var m in q) Mistakes.Add(m);
        Summary = $"共 {_all.Count} 道错题，当前显示 {Mistakes.Count} 道，涉及知识点 {KnowledgePoints.Count} 个";
        Status = Mistakes.Count == 0 ? "没有匹配的错题" : "";
    }

    [RelayCommand]
    private async Task ClearFilterAsync()
    {
        SelectedStudent = null;
        SelectedSubject = null;
        SelectedKnowledgePoint = null;
        OnlyWithComment = false;
        await LoadAsync();
    }
}
