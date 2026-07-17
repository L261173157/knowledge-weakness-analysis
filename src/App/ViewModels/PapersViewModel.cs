using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KnowledgeWeakness.App.Services;
using KnowledgeWeakness.Core.Abstractions;
using KnowledgeWeakness.Core.Domain;

namespace KnowledgeWeakness.App.ViewModels;

public partial class PapersViewModel : ViewModelBase
{
    private readonly IPaperRepository _paperRepo;
    private readonly IStudentRepository _studentRepo;
    private readonly ISubjectRepository _subjectRepo;

    public ObservableCollection<Student> Students { get; } = new();
    public ObservableCollection<Subject> Subjects { get; } = new();
    public ObservableCollection<PaperRow> Papers { get; } = new();
    public ObservableCollection<PaperQuestionRow> Questions { get; } = new();
    public ObservableCollection<string> ImagePaths { get; } = new();

    [ObservableProperty] private Student? _selectedStudent;
    [ObservableProperty] private Subject? _selectedSubject;
    [ObservableProperty] private PaperRow? _selectedPaper;
    [ObservableProperty] private string _detailTitle = "选择左侧卷子查看详情";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isBusy;

    // Two-step delete confirmation. First click arms; second click within
    // DeleteConfirmTimeoutMs actually deletes. Switching selection cancels.
    public const int DeleteConfirmTimeoutMs = 5000;
    [ObservableProperty] private bool _deletePending;
    [ObservableProperty] private string _deleteButtonText = "删除卷子";
    private int _deleteToken;

    public PapersViewModel(IPaperRepository paperRepo, IStudentRepository studentRepo, ISubjectRepository subjectRepo)
    {
        _paperRepo = paperRepo;
        _studentRepo = studentRepo;
        _subjectRepo = subjectRepo;
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

    partial void OnSelectedPaperChanged(PaperRow? value)
    {
        // Switching rows must reset any armed delete — otherwise the next
        // click could destroy the freshly selected paper.
        CancelPendingDelete();
        _ = LoadDetailAsync();
    }

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

            Papers.Clear();
            foreach (var p in filtered)
            {
                var wrong = p.Questions.Count(q => q.StudentAnswer is not null && !q.StudentAnswer.IsCorrect);
                Papers.Add(new PaperRow
                {
                    Id = p.Id,
                    StudentName = p.Student?.Name ?? "",
                    SubjectName = p.Subject?.Name ?? "",
                    Date = p.Date,
                    Title = string.IsNullOrWhiteSpace(p.Title) ? "(未命名)" : p.Title,
                    Status = p.Status,
                    QuestionCount = p.Questions.Count,
                    WrongCount = wrong
                });
            }

            SelectedPaper = Papers.FirstOrDefault();
            Status = $"共 {Papers.Count} 份卷子";
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

    private async Task LoadDetailAsync()
    {
        Questions.Clear();
        ImagePaths.Clear();
        if (SelectedPaper is null) { DetailTitle = "选择左侧卷子查看详情"; return; }

        var paper = await _paperRepo.GetWithQuestionsAsync(SelectedPaper.Id);
        if (paper is null) return;

        DetailTitle = $"{paper.Student?.Name} · {paper.Subject?.Name} · {paper.Date:yyyy-MM-dd} · {paper.Title}";
        foreach (var q in paper.Questions.OrderBy(q => q.Number))
        {
            var a = q.StudentAnswer;
            Questions.Add(new PaperQuestionRow
            {
                Number = q.Number,
                Type = q.Type.ToString(),
                Stem = q.Stem,
                StandardAnswer = q.StandardAnswer,
                StudentAnswer = a?.AnswerText,
                IsCorrect = a?.IsCorrect ?? false,
                PartialScore = a?.PartialScore,
                TeacherComment = a?.TeacherComment
            });
        }

        if (!string.IsNullOrEmpty(paper.OriginalImagePaths))
        {
            foreach (var raw in paper.OriginalImagePaths.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                var resolved = PaperImagePathResolver.Resolve(raw, AppPaths.PaperImageDirectory);
                if (File.Exists(resolved)) ImagePaths.Add(resolved);
            }
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedPaper is null) return;

        if (!DeletePending)
        {
            DeletePending = true;
            DeleteButtonText = $"再次点击确认删除「{SelectedPaper.Title}」";
            Status = $"删除将级联清除 {SelectedPaper.QuestionCount} 道题及答案。5 秒内再次点击「删除卷子」按钮确认；切换其他卷子或等待 5 秒自动取消。";
            var token = ++_deleteToken;
            _ = AutoCancelPendingDeleteAsync(token);
            return;
        }

        var paper = SelectedPaper;
        var id = paper.Id;
        await _paperRepo.DeleteAsync(id);
        CancelPendingDelete();
        Status = $"已删除卷子 #{id}「{paper.Title}」";
        await LoadAsync();
    }

    private async Task AutoCancelPendingDeleteAsync(int token)
    {
        await Task.Delay(DeleteConfirmTimeoutMs);
        if (token != _deleteToken || !DeletePending) return;
        CancelPendingDelete();
        Status = "删除已自动取消";
    }

    private void CancelPendingDelete()
    {
        _deleteToken++;
        DeletePending = false;
        DeleteButtonText = "删除卷子";
    }

    [RelayCommand]
    private async Task ClearFilterAsync()
    {
        SelectedStudent = null;
        SelectedSubject = null;
        await LoadAsync();
    }
}
