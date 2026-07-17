using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KnowledgeWeakness.Core.Abstractions;
using KnowledgeWeakness.Core.Domain;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace KnowledgeWeakness.App.ViewModels;

public partial class TrendsViewModel : ViewModelBase
{
    private readonly IPaperRepository _paperRepo;
    private readonly IStudentRepository _studentRepo;
    private readonly ISubjectRepository _subjectRepo;

    public ObservableCollection<Student> Students { get; } = new();
    public ObservableCollection<Subject> Subjects { get; } = new();
    public ObservableCollection<TrendPoint> Points { get; } = new();

    [ObservableProperty] private Student? _selectedStudent;
    [ObservableProperty] private Subject? _selectedSubject;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _summary = "";

    [ObservableProperty] private ISeries[] _series = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _xAxes = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _yAxes = Array.Empty<Axis>();

    public TrendsViewModel(IPaperRepository paperRepo, IStudentRepository studentRepo, ISubjectRepository subjectRepo)
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

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            var all = await _paperRepo.ListWithQuestionsAsync();
            IEnumerable<Paper> filtered = all;
            if (SelectedStudent is not null) filtered = filtered.Where(p => p.StudentId == SelectedStudent.Id);
            if (SelectedSubject is not null) filtered = filtered.Where(p => p.SubjectId == SelectedSubject.Id);

            var ordered = filtered
                .OrderBy(p => p.Date)
                .ThenBy(p => p.Id)
                .ToList();

            Points.Clear();
            foreach (var p in ordered)
            {
                var total = p.Questions.Count;
                var wrong = p.Questions.Count(q => q.StudentAnswer is not null && !q.StudentAnswer.IsCorrect);
                Points.Add(new TrendPoint
                {
                    Date = p.Date,
                    PaperTitle = string.IsNullOrWhiteSpace(p.Title) ? $"#{p.Id}" : p.Title,
                    Total = total,
                    Wrong = wrong
                });
            }

            BuildChart();

            if (Points.Count == 0)
            {
                Summary = "暂无可绘制的卷子";
                Status = "请先在卷子导入完成保存，并切到本页查看趋势";
            }
            else
            {
                var avg = Points.Average(p => p.Accuracy);
                Summary = $"共 {Points.Count} 份卷子，平均正确率 {avg:P0}";
                Status = "";
            }
        }
        catch (Exception ex)
        {
            Status = "加载失败：" + ex.Message;
        }
    }

    private void BuildChart()
    {
        var values = Points.Select(p => p.Accuracy * 100).ToArray();
        var labels = Points.Select(p => $"{p.Date:MM-dd}\n{p.PaperTitle}").ToArray();

        Series = new ISeries[]
        {
            new LineSeries<double>
            {
                Values = values,
                Name = "正确率(%)",
                GeometrySize = 10,
                Stroke = new SolidColorPaint(SKColors.SteelBlue) { StrokeThickness = 3 },
                Fill = new SolidColorPaint(new SKColor(37, 99, 235, 30))
            }
        };
        XAxes = new[]
        {
            new Axis { Labels = labels, LabelsRotation = 0, TextSize = 11 }
        };
        YAxes = new[]
        {
            new Axis { MinLimit = 0, MaxLimit = 100, Name = "正确率 (%)" }
        };
    }

    [RelayCommand]
    private async Task ClearFilterAsync()
    {
        SelectedStudent = null;
        SelectedSubject = null;
        await LoadAsync();
    }
}
