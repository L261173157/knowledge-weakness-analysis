using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KnowledgeWeakness.App.Services;
using KnowledgeWeakness.Core.AI;
using KnowledgeWeakness.Core.Abstractions;
using KnowledgeWeakness.Core.Domain;
using KnowledgeWeakness.Infrastructure.AI;
using KnowledgeWeakness.Infrastructure.Imaging;

namespace KnowledgeWeakness.App.ViewModels;

public partial class PaperImportViewModel : ViewModelBase
{
    private readonly IStudentRepository _studentRepo;
    private readonly ISubjectRepository _subjectRepo;
    private readonly IPaperRepository _paperRepo;
    private readonly ISettingsRepository _settings;
    private readonly IVisionModelFactory _visionFactory;
    private readonly ImagePreprocessor _preprocessor;
    private readonly IFilePickerService _filePicker;

    public ObservableCollection<Student> Students { get; } = new();
    public ObservableCollection<Subject> Subjects { get; } = new();
    public ObservableCollection<PickedImage> Images { get; } = new();
    public ObservableCollection<ExtractedQuestionRow> Questions { get; } = new();

    [ObservableProperty] private Student? _selectedStudent;
    [ObservableProperty] private Subject? _selectedSubject;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private DateTime? _paperDate = DateTime.Today;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _rawJson = "";

    public Array QuestionTypes { get; } = Enum.GetValues(typeof(QuestionType));

    public PaperImportViewModel(
        IStudentRepository studentRepo,
        ISubjectRepository subjectRepo,
        IPaperRepository paperRepo,
        ISettingsRepository settings,
        IVisionModelFactory visionFactory,
        ImagePreprocessor preprocessor,
        IFilePickerService filePicker)
    {
        _studentRepo = studentRepo;
        _subjectRepo = subjectRepo;
        _paperRepo = paperRepo;
        _settings = settings;
        _visionFactory = visionFactory;
        _preprocessor = preprocessor;
        _filePicker = filePicker;
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        Students.Clear();
        foreach (var s in await _studentRepo.ListAsync()) Students.Add(s);
        SelectedStudent = Students.FirstOrDefault();

        Subjects.Clear();
        foreach (var s in await _subjectRepo.ListAsync()) Subjects.Add(s);
        SelectedSubject = Subjects.FirstOrDefault();

        Status = "就绪";
    }

    [RelayCommand]
    private async Task PickImagesAsync()
    {
        var picked = await _filePicker.PickImagesAsync();
        foreach (var p in picked) Images.Add(p);
        Status = $"已选 {Images.Count} 张图片";
    }

    [RelayCommand]
    private void RemoveImage(PickedImage? item)
    {
        if (item is not null) Images.Remove(item);
    }

    [RelayCommand]
    private void ClearImages()
    {
        Images.Clear();
        Questions.Clear();
        RawJson = "";
    }

    [RelayCommand]
    private async Task ExtractAsync()
    {
        if (Images.Count == 0) { Status = "请先选择至少一张图片"; return; }
        if (SelectedSubject is null) { Status = "请选择学科"; return; }

        try
        {
            IsBusy = true;
            Status = "预处理图像...";
            var normalized = Images.Select(i => _preprocessor.NormalizeToJpeg(i.Bytes)).ToList();

            Status = "调用智谱 GLM 识别中...";
            var model = _visionFactory.Create(VisionModelFactory.GlmCode);
            var context = new SubjectContext(
                SelectedSubject.Code,
                SelectedSubject.Name,
                SelectedStudent?.Grade ?? "",
                null);

            var result = await model.ExtractPaperAsync(normalized, context);
            RawJson = result.RawJson;

            Questions.Clear();
            foreach (var q in result.Questions)
            {
                Questions.Add(new ExtractedQuestionRow
                {
                    Number = q.Number,
                    Type = q.Type,
                    Stem = q.Stem,
                    StandardAnswer = q.StandardAnswer,
                    StudentAnswer = q.StudentAnswer,
                    IsCorrect = q.IsCorrect,
                    PartialScore = q.PartialScore,
                    TeacherComment = q.TeacherComment
                });
            }
            if (!string.IsNullOrWhiteSpace(result.Title)) Title = result.Title!;
            Status = $"识别完成，共 {Questions.Count} 题。请逐题校对后保存。";
        }
        catch (Exception ex)
        {
            Status = "识别失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (SelectedStudent is null) { Status = "请选择学生"; return; }
        if (SelectedSubject is null) { Status = "请选择学科"; return; }
        if (Questions.Count == 0) { Status = "没有题目可保存，请先识别"; return; }

        try
        {
            IsBusy = true;
            Status = "保存中...";

            var paperDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KnowledgeWeakness", "papers");
            Directory.CreateDirectory(paperDir);

            var savedPaths = new List<string>();
            foreach (var img in Images)
            {
                var name = $"{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}_{img.DisplayName}";
                var path = Path.Combine(paperDir, name);
                await File.WriteAllBytesAsync(path, img.Bytes);
                savedPaths.Add(path);
            }

            var paper = new Paper
            {
                StudentId = SelectedStudent.Id,
                SubjectId = SelectedSubject.Id,
                Date = DateOnly.FromDateTime(PaperDate ?? DateTime.Today),
                Title = Title,
                OriginalImagePaths = string.Join("|", savedPaths),
                Provider = VisionModelFactory.GlmCode,
                RawExtractionJson = RawJson,
                Status = ImportStatus.Reviewed,
                ReviewedAt = DateTime.UtcNow,
                Questions = Questions.Select(r => new Question
                {
                    Number = r.Number,
                    Type = r.Type,
                    Stem = r.Stem,
                    StandardAnswer = r.StandardAnswer,
                    StudentAnswer = new StudentAnswer
                    {
                        AnswerText = r.StudentAnswer,
                        IsCorrect = r.IsCorrect,
                        PartialScore = r.PartialScore,
                        TeacherComment = r.TeacherComment
                    }
                }).ToList()
            };

            var id = await _paperRepo.AddAsync(paper);
            Status = $"已保存卷子 #{id}，共 {Questions.Count} 题。";
        }
        catch (Exception ex)
        {
            Status = "保存失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
