using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KnowledgeWeakness.Core.Abstractions;
using KnowledgeWeakness.Core.Analysis;
using KnowledgeWeakness.Core.Domain;

namespace KnowledgeWeakness.App.ViewModels;

public partial class AnalysisViewModel : ViewModelBase
{
    private readonly IPaperRepository _paperRepo;
    private readonly IStudentRepository _studentRepo;
    private readonly ISubjectRepository _subjectRepo;

    public ObservableCollection<Student> Students { get; } = new();
    public ObservableCollection<Subject> Subjects { get; } = new();
    public ObservableCollection<WeaknessPointRow> Points { get; } = new();
    public ObservableCollection<WeakQuestionExampleRow> Examples { get; } = new();

    [ObservableProperty] private Student? _selectedStudent;
    [ObservableProperty] private Subject? _selectedSubject;
    [ObservableProperty] private WeaknessPointRow? _selectedPoint;
    [ObservableProperty] private string _summary = "暂无分析数据";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isBusy;

    public AnalysisViewModel(
        IPaperRepository paperRepo,
        IStudentRepository studentRepo,
        ISubjectRepository subjectRepo)
    {
        _paperRepo = paperRepo;
        _studentRepo = studentRepo;
        _subjectRepo = subjectRepo;
        _ = LoadAsync();
    }

    partial void OnSelectedPointChanged(WeaknessPointRow? value)
    {
        Examples.Clear();
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

            var result = WeaknessAnalysisService.Analyze(papers);
            Points.Clear();
            Examples.Clear();

            foreach (var point in result.Points)
            {
                var row = new WeaknessPointRow
                {
                    KnowledgePoint = point.KnowledgePoint,
                    WrongCount = point.WrongCount,
                    TotalCount = point.TotalCount,
                    WrongRate = point.WrongRate
                };

                foreach (var example in point.Examples)
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
            Summary = $"卷子 {result.TotalPapers} 份，题目 {result.TotalQuestions} 道，薄弱题 {result.TotalWeakQuestions} 道";
            Status = Points.Count == 0 ? "暂无薄弱点。请先导入并保存已批改卷子。" : $"已生成 {Points.Count} 个薄弱点";
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
