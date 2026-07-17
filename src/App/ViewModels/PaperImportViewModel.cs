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
using KnowledgeWeakness.Core.Analysis;
using KnowledgeWeakness.Core.Domain;
using KnowledgeWeakness.Infrastructure.AI;
using KnowledgeWeakness.Infrastructure.Imaging;
using Serilog;

namespace KnowledgeWeakness.App.ViewModels;

public partial class PaperImportViewModel : ViewModelBase
{
    private readonly IStudentRepository _studentRepo;
    private readonly ISubjectRepository _subjectRepo;
    private readonly IPaperRepository _paperRepo;
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
    [ObservableProperty] private bool _isReviewConfirmed;
    [ObservableProperty] private string _rawJson = "";
    [ObservableProperty] private int _savedInSession;
    [ObservableProperty] private bool _autoResetAfterSave;

    /// <summary>
    /// Becomes <c>false</c> after a successful Save until the user starts a
    /// new paper (NewPaper / ClearImages / Extract). Guards against accidental
    /// double-saves creating duplicate Paper rows + duplicate image copies.
    /// </summary>
    [ObservableProperty] private bool _hasUnsavedWork = true;

    /// <summary>
    /// True only when there's unsaved work AND no async operation is running.
    /// Save button binds here so it's disabled both after a successful save
    /// (waiting for the user to start a new paper) AND during extraction
    /// (so a mid-flight extraction can't be raced into persisting stale rows).
    /// </summary>
    public bool CanSave => HasUnsavedWork && !IsBusy;

    partial void OnHasUnsavedWorkChanged(bool value) => OnPropertyChanged(nameof(CanSave));
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanSave));

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
        _visionFactory = visionFactory;
        _preprocessor = preprocessor;
        _filePicker = filePicker;
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            Students.Clear();
            foreach (var s in await _studentRepo.ListAsync()) Students.Add(s);
            SelectedStudent = Students.FirstOrDefault();

            Subjects.Clear();
            foreach (var s in await _subjectRepo.ListAsync()) Subjects.Add(s);
            SelectedSubject = Subjects.FirstOrDefault();

            Status = "Ready";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize paper import view");
            Status = "初始化失败：" + ex.Message;
        }
    }
    [RelayCommand]
    private async Task PickImagesAsync()
    {
        var picked = await _filePicker.PickImagesAsync();
        foreach (var p in picked) Images.Add(p);
        Status = $"已选择 {Images.Count} 张图片";
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
        IsReviewConfirmed = false;
        RawJson = "";
        Status = "已清空图片和识别结果";
    }

    [RelayCommand]
    private void NewPaper()
    {
        ResetForNextPaper();
        Status = $"已重置，可录入下一卷（本次会话累计 {SavedInSession} 卷）";
    }

    private void ResetForNextPaper()
    {
        Images.Clear();
        Questions.Clear();
        IsReviewConfirmed = false;
        RawJson = "";
        Title = "";
        PaperDate = DateTime.Today;
        HasUnsavedWork = true;
    }

    [RelayCommand]
    private async Task ExtractAsync()
    {
        if (Images.Count == 0) { Status = "请先选择至少一张图片"; return; }
        if (SelectedSubject is null) { Status = "请选择学科"; return; }
        if (IsBusy) { Status = "已有任务在执行，请稍候"; return; }

        // Clear stale rows at the very start of extraction, BEFORE the long
        // network call. If a user race-clicks Save while the model is still
        // running, there's no leftover question grid to accidentally persist
        // as a new paper.
        Questions.Clear();
        RawJson = "";
        IsReviewConfirmed = false;
        HasUnsavedWork = true;

        try
        {
            IsBusy = true;
            Status = "正在预处理图像...";
            var normalized = Images.Select(i => _preprocessor.NormalizeToJpeg(i.Bytes)).ToList();

            Status = "正在调用智谱 GLM 识别...";
            var model = _visionFactory.Create(VisionModelFactory.GlmCode);
            var context = new SubjectContext(
                SelectedSubject.Code,
                SelectedSubject.Name,
                SelectedStudent?.Grade ?? "",
                null);

            var result = await model.ExtractPaperAsync(normalized, context);
            RawJson = result.RawJson;
            IsReviewConfirmed = false;

            Questions.Clear();
            foreach (var q in result.Questions)
            {
                var answerTextJudgment = AnswerTextHelper.Judge(
                    q.StandardAnswerOption,
                    q.StandardAnswerText,
                    q.StudentAnswerOption,
                    q.StudentAnswerText);
                var teacherJudgment = q.TeacherIsCorrect ?? q.IsCorrect;
                var resolvedIsCorrect = GradingDecisionHelper.ResolveForImport(
                    q.AiIsCorrect,
                    teacherJudgment,
                    answerTextJudgment);
                var needsReview = GradingDecisionHelper.NeedsReview(
                    q.AiIsCorrect,
                    teacherJudgment,
                    answerTextJudgment);

                Questions.Add(new ExtractedQuestionRow
                {
                    Number = q.Number,
                    Type = q.Type,
                    Stem = AppendOptionsToStem(q.Stem, q.Options),
                    StandardAnswerOption = q.StandardAnswerOption,
                    StandardAnswerText = q.StandardAnswerText,
                    StudentAnswerOption = q.StudentAnswerOption,
                    StudentAnswerText = q.StudentAnswerText,
                    IsCorrect = resolvedIsCorrect,
                    PartialScore = q.PartialScore,
                    TeacherComment = q.TeacherComment,
                    TeacherIsCorrect = teacherJudgment,
                    AiIsCorrect = q.AiIsCorrect,
                    AnswerTextIsCorrect = answerTextJudgment,
                    NeedsReview = needsReview
                });
            }

            if (!string.IsNullOrWhiteSpace(result.Title)) Title = result.Title!;
            var reviewCount = Questions.Count(q => q.NeedsReview);
            Status = reviewCount == 0
                ? $"识别完成，共 {Questions.Count} 题。老师、AI 和答案判定一致，请逐题校对后保存。"
                : $"识别完成，共 {Questions.Count} 题。有 {reviewCount} 题判定不一致或信息不足，请复核后保存。";
        }
        catch (OperationCanceledException ex)
        {
            Log.Error(ex, "GLM vision call timed out / canceled");
            Status = "识别失败：请求超时。GLM 多页识别耗时较长，请稍后重试，或减少一次上传的页数。";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "GLM vision call failed");
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
        if (IsBusy)
        {
            Status = "正在识别中，请等待识别完成后再保存";
            return;
        }
        if (!HasUnsavedWork)
        {
            Status = "本卷已保存。请点「新建下一卷」开始下一份，避免重复入库。";
            return;
        }
        if (SelectedStudent is null) { Status = "请选择学生"; return; }
        if (SelectedSubject is null) { Status = "请选择学科"; return; }
        if (Questions.Count == 0) { Status = "没有题目可保存，请先识别"; return; }

        // Row-level guard: every question with conflicting AI/teacher/answer
        // judgments must be human-adjudicated before we persist it as truth.
        // Editing the IsCorrect cell on a row clears NeedsReview automatically
        // (see ExtractedQuestionRow.OnIsCorrectChanged).
        var unresolved = Questions.Where(q => q.NeedsReview).ToList();
        if (unresolved.Count > 0)
        {
            var nums = string.Join(", ", unresolved.Take(8).Select(q => q.Number));
            var more = unresolved.Count > 8 ? $"…等 {unresolved.Count} 题" : "";
            Status = $"还有 {unresolved.Count} 道题判定不一致需人工裁决（题号 {nums}{more}）。" +
                     "请点击对应题的「确认此判定」按钮接受当前结果，或编辑「正确」列后再保存。";
            return;
        }

        if (!IsReviewConfirmed) { Status = "请先确认已完成逐题校对，再保存入库"; return; }

        var savedFilePaths = new List<string>();
        try
        {
            IsBusy = true;
            Status = "正在保存...";

            var paperDir = AppPaths.PaperImageDirectory;
            Directory.CreateDirectory(paperDir);

            // Persist FILENAMES (not absolute paths) so backup/restore can
            // move the DB across machines and still find the images under
            // the new machine's PaperImageDirectory. PaperImagePathResolver
            // handles legacy rows that still hold absolute paths.
            var savedFilenames = new List<string>();
            foreach (var img in Images)
            {
                var name = $"{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}_{img.DisplayName}";
                var path = Path.Combine(paperDir, name);
                await File.WriteAllBytesAsync(path, img.Bytes);
                savedFilePaths.Add(path);
                savedFilenames.Add(name);
            }

            var paper = new Paper
            {
                StudentId = SelectedStudent.Id,
                SubjectId = SelectedSubject.Id,
                Date = DateOnly.FromDateTime(PaperDate ?? DateTime.Today),
                Title = Title,
                OriginalImagePaths = string.Join("|", savedFilenames),
                Provider = VisionModelFactory.GlmCode,
                RawExtractionJson = RawJson,
                Status = ImportStatus.Reviewed,
                ReviewedAt = DateTime.UtcNow,
                Questions = Questions.Select(r => new Question
                {
                    Number = r.Number,
                    Type = r.Type,
                    Stem = r.Stem,
                    StandardAnswer = r.CombinedStandardAnswer,
                    StudentAnswer = new StudentAnswer
                    {
                        AnswerText = r.CombinedStudentAnswer,
                        IsCorrect = r.IsCorrect,
                        PartialScore = r.PartialScore,
                        TeacherComment = r.TeacherComment
                    }
                }).ToList()
            };

            var id = await _paperRepo.AddAsync(paper);
            SavedInSession++;
            HasUnsavedWork = false;  // disable Save until user explicitly starts next paper
            var hint = AutoResetAfterSave ? "已自动清空，可继续导入下一卷。" : "保存按钮已禁用；点「新建下一卷」开始下一份。";
            Status = $"已保存卷子 #{id}，共 {Questions.Count} 题。本次会话累计 {SavedInSession} 卷。{hint}";

            if (AutoResetAfterSave)
                ResetForNextPaper();
        }
        catch (Exception ex)
        {
            foreach (var path in savedFilePaths)
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
            Status = "保存失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string AppendOptionsToStem(string stem, IReadOnlyDictionary<string, string> options)
    {
        if (options.Count == 0) return stem;

        var optionText = string.Join(" ", options
            .OrderBy(x => x.Key)
            .Select(x => $"{x.Key}. {x.Value}"));

        if (string.IsNullOrWhiteSpace(optionText)) return stem;
        return string.IsNullOrWhiteSpace(stem) ? optionText : $"{stem} {optionText}";
    }
}
