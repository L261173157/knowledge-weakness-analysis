using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KnowledgeWeakness.App.Services;
using KnowledgeWeakness.Core.AI;
using KnowledgeWeakness.Core.Abstractions;
using KnowledgeWeakness.Core.Analysis;
using KnowledgeWeakness.Core.Domain;
using KnowledgeWeakness.Infrastructure.AI;
using KnowledgeWeakness.Infrastructure.Analysis;

namespace KnowledgeWeakness.App.ViewModels;

public partial class AnalysisViewModel : ViewModelBase
{
    private readonly IPaperRepository _paperRepo;
    private readonly IStudentRepository _studentRepo;
    private readonly ISubjectRepository _subjectRepo;
    private readonly IKnowledgePointRepository _knowledgePointRepo;
    private readonly IWeaknessAnalyzer _weaknessAnalyzer;
    private readonly KnowledgeBaseReader _knowledgeBaseReader;
    private readonly ISettingsRepository _settings;
    private IReadOnlyList<Paper> _currentPapers = [];
    private IReadOnlyList<WeaknessCandidate> _currentCandidates = [];

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
        IKnowledgePointRepository knowledgePointRepo,
        IWeaknessAnalyzer weaknessAnalyzer,
        KnowledgeBaseReader knowledgeBaseReader,
        ISettingsRepository settings)
    {
        _paperRepo = paperRepo;
        _studentRepo = studentRepo;
        _subjectRepo = subjectRepo;
        _knowledgePointRepo = knowledgePointRepo;
        _weaknessAnalyzer = weaknessAnalyzer;
        _knowledgeBaseReader = knowledgeBaseReader;
        _settings = settings;
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task ExportAsync(string? formatOverride)
    {
        try
        {
            if (Points.Count == 0)
            {
                Status = "暂无可导出的薄弱点，请先生成分析结果";
                return;
            }
            var directory = await _settings.GetAsync(SettingsKeys.ExportDirectory);
            if (string.IsNullOrWhiteSpace(directory))
                directory = AppPaths.ExportDirectory;
            var format = formatOverride ?? await _settings.GetAsync(SettingsKeys.ExportFormat) ?? "Markdown";

            var report = new AnalysisReportData(
                SelectedStudent?.Name ?? "全部学生",
                SelectedSubject?.Name ?? "全部学科",
                Summary,
                DateTime.Now,
                Points.ToList());
            var path = await AnalysisReportExporter.ExportAsync(report, directory, format);
            Status = $"已导出至：{path}";
        }
        catch (Exception ex)
        {
            Status = "导出失败：" + ex.Message;
        }
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
            Status = "正在读取导入结果...";

            await LoadFiltersAsync();
            var papers = await _paperRepo.ListWithQuestionsAsync();
            if (SelectedStudent is not null)
                papers = papers.Where(p => p.StudentId == SelectedStudent.Id).ToList();
            if (SelectedSubject is not null)
                papers = papers.Where(p => p.SubjectId == SelectedSubject.Id).ToList();

            var candidates = WeaknessCandidateSelector.Select(papers);
            _currentPapers = papers;
            _currentCandidates = candidates;

            Points.Clear();
            Examples.Clear();
            if (candidates.Count == 0)
            {
                SelectedPoint = null;
                Summary = $"卷子 {papers.Count} 份，没有可进入分析的错题/扣分题。";
                Status = "未发现已确认的错题或扣分题。请先在卷子导入页保存已校对的卷子。";
                return;
            }

            var knowledgePoints = SelectedSubject is null
                ? await _knowledgePointRepo.ListAsync()
                : await _knowledgePointRepo.ListBySubjectAsync(SelectedSubject.Id);
            AddLocalFallbackRows(papers, knowledgePoints);
            SelectedPoint = Points.FirstOrDefault();
            Summary = $"卷子 {papers.Count} 份，已导入错题/扣分题 {candidates.Count} 道。";
            Status = Points.Count == 0
                ? "已显示导入错题，但本地规则未生成知识点分类。可点击 AI 分析薄弱点。"
                : $"已显示导入结果，并按本地规则生成 {Points.Count} 个薄弱点。需要 AI 归纳时请点击“AI 分析薄弱点”。";
        }
        catch (Exception ex)
        {
            Status = "读取导入结果失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AnalyzeAiAsync()
    {
        try
        {
            IsBusy = true;
            if (_currentCandidates.Count == 0)
            {
                await LoadAsync();
            }

            var papers = _currentPapers;
            var candidates = _currentCandidates;
            if (candidates.Count == 0)
            {
                Status = "没有可供 AI 分析的错题/扣分题。";
                return;
            }

            Status = "正在调用 AI 分析薄弱点...";
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

            if (result.Points.Count == 0)
            {
                SelectedPoint = Points.FirstOrDefault();
                Summary = $"卷子 {papers.Count} 份，已导入错题/扣分题 {candidates.Count} 道。AI 未返回薄弱点，保留当前导入结果。";
                Status = Points.Count == 0
                    ? "AI 未返回薄弱点，本地规则也未生成分类结果。"
                    : $"AI 未返回薄弱点，已保留 {Points.Count} 个本地薄弱点。";
                return;
            }

            Points.Clear();
            Examples.Clear();
            foreach (var point in result.Points)
            {
                var relatedCandidates = WeaknessCandidateMatcher
                    .MatchByQuestionNumbers(candidates, point.QuestionNumbers)
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
            Summary = $"卷子 {papers.Count} 份，已导入错题/扣分题 {candidates.Count} 道。AI 总结：{result.Summary}";
            Status = knowledgeBase is null
                ? $"AI 已生成 {Points.Count} 个薄弱点。未使用知识库。"
                : $"AI 已生成 {Points.Count} 个薄弱点。已接入知识库。";
        }
        catch (Exception ex)
        {
            Status = "AI 分析失败，当前仍保留导入结果：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AddLocalFallbackRows(IReadOnlyList<Paper> papers, IReadOnlyList<KnowledgePoint> knowledgePoints)
    {
        var local = WeaknessAnalysisService.Analyze(papers, knowledgePoints);
        foreach (var point in local.Points)
        {
            var row = new WeaknessPointRow
            {
                KnowledgePoint = point.KnowledgePoint,
                Severity = point.WrongCount >= 3 ? "高" : point.WrongCount == 2 ? "中" : "低",
                WeakReason = $"该知识点下有 {point.WrongCount} 道错题或扣分题，需要优先复盘题干、标准答案和自己的作答差异。",
                ReviewAdvice = "先重做关联错题，再对照标准答案补齐遗漏步骤或关键词，最后整理一条易错原因。",
                PracticeDirection = "补练同知识点的选择、填空和简答变式题，重点检查是否能独立说出判断依据。",
                QuestionNumbersText = string.Join(", ", point.Examples.Select(x => x.QuestionNumber)),
                WrongCount = point.WrongCount,
                TotalCount = point.TotalCount,
                WrongRate = point.WrongRate
            };

            foreach (var example in point.Examples.Take(5))
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
