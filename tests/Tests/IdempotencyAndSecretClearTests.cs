using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KnowledgeWeakness.App.ViewModels;
using KnowledgeWeakness.App.Services;
using KnowledgeWeakness.Core.Abstractions;
using KnowledgeWeakness.Core.Analysis;
using KnowledgeWeakness.Core.AI;
using KnowledgeWeakness.Core.Domain;
using KnowledgeWeakness.Infrastructure.AI;
using KnowledgeWeakness.Infrastructure.Imaging;
using KnowledgeWeakness.Infrastructure.Analysis;
using KnowledgeWeakness.Infrastructure.Persistence;
using KnowledgeWeakness.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeWeakness.Tests;

#region Fakes shared by the import idempotency tests

internal sealed class CountingPaperRepo : IPaperRepository
{
    public List<Paper> Saved { get; } = new();
    public Task<IReadOnlyList<Paper>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult((IReadOnlyList<Paper>)Saved.ToList());
    public Task<IReadOnlyList<Paper>> ListWithQuestionsAsync(CancellationToken ct = default) =>
        Task.FromResult((IReadOnlyList<Paper>)Saved.ToList());
    public Task<Paper?> GetAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Saved.FirstOrDefault(p => p.Id == id));
    public Task<Paper?> GetWithQuestionsAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Saved.FirstOrDefault(p => p.Id == id));
    public Task<int> AddAsync(Paper paper, CancellationToken ct = default)
    { paper.Id = Saved.Count + 1; Saved.Add(paper); return Task.FromResult(paper.Id); }
    public Task UpdateAsync(Paper paper, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteAsync(int id, CancellationToken ct = default)
    { Saved.RemoveAll(p => p.Id == id); return Task.CompletedTask; }
    public Task ReplaceQuestionsAsync(int paperId, IEnumerable<Question> questions, CancellationToken ct = default)
        => Task.CompletedTask;
}

internal sealed class StubStudentRepo : IStudentRepository
{
    public List<Student> Students { get; } = new();
    public Task<IReadOnlyList<Student>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult((IReadOnlyList<Student>)Students);
    public Task<Student?> GetAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Students.FirstOrDefault(s => s.Id == id));
    public Task AddAsync(Student student, CancellationToken ct = default) { Students.Add(student); return Task.CompletedTask; }
    public Task UpdateAsync(Student student, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteAsync(int id, CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class StubSubjectRepo : ISubjectRepository
{
    public List<Subject> Subjects { get; } = new();
    public Task<IReadOnlyList<Subject>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult((IReadOnlyList<Subject>)Subjects);
    public Task<Subject?> GetAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Subjects.FirstOrDefault(s => s.Id == id));
    public Task<Subject?> GetByCodeAsync(string code, CancellationToken ct = default) =>
        Task.FromResult(Subjects.FirstOrDefault(s => s.Code == code));
    public Task AddAsync(Subject subject, CancellationToken ct = default) { Subjects.Add(subject); return Task.CompletedTask; }
    public Task UpdateAsync(Subject subject, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteAsync(int id, CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class InMemorySettingsRepo : ISettingsRepository
{
    private readonly Dictionary<string, string> _store = new();
    public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(_store.TryGetValue(key, out var v) ? v : null);
    public Task<string?> GetSecretAsync(string key, CancellationToken ct = default) => GetAsync(key, ct);
    public Task SetAsync(string key, string value, CancellationToken ct = default)
    { _store[key] = value; return Task.CompletedTask; }
    public Task SetSecretAsync(string key, string value, CancellationToken ct = default)
    { _store[key] = value; return Task.CompletedTask; }
    public Task DeleteAsync(string key, CancellationToken ct = default)
    { _store.Remove(key); return Task.CompletedTask; }
    public bool Has(string key) => _store.ContainsKey(key);
}

internal sealed class StubVisionFactory : IVisionModelFactory
{
    public Task<IVisionModel> CreateAsync(string providerCode, CancellationToken ct = default)
        => throw new NotSupportedException();
    public IReadOnlyList<string> AvailableProviders => Array.Empty<string>();
}

internal sealed class StubFilePicker : IFilePickerService
{
    public Task<IReadOnlyList<PickedImage>> PickImagesAsync()
        => Task.FromResult((IReadOnlyList<PickedImage>)new List<PickedImage>());
    public Task<string?> PickBackupZipAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickBackupSaveAsync(string suggestedName) => Task.FromResult<string?>(null);
    public Task<string?> PickDirectoryAsync(string title) => Task.FromResult<string?>(null);
}

#endregion

public class PaperImportSaveIdempotencyTests
{
    private static PaperImportViewModel BuildVm(out CountingPaperRepo paperRepo)
    {
        paperRepo = new CountingPaperRepo();
        var students = new StubStudentRepo();
        students.Students.Add(new Student { Id = 1, Name = "S", Grade = "G" });
        var subjects = new StubSubjectRepo();
        subjects.Subjects.Add(new Subject { Id = 1, Code = "ge", Name = "地理" });
        var settings = new InMemorySettingsRepo();
        var preprocessor = new ImagePreprocessor();
        var picker = new StubFilePicker();
        return new PaperImportViewModel(
            students, subjects, paperRepo, settings,
            new StubVisionFactory(), preprocessor, picker);
    }

    [Fact]
    public async Task Second_Save_click_after_success_does_not_create_duplicate_paper()
    {
        var vm = BuildVm(out var repo);
        // Wait for the constructor's async InitAsync to set defaults.
        for (var i = 0; i < 50 && vm.SelectedStudent is null; i++) await Task.Delay(20);

        // Seed reviewed questions directly (skip image processing).
        vm.Questions.Add(new ExtractedQuestionRow
        {
            Number = "1",
            Type = QuestionType.Choice,
            Stem = "Q",
            IsCorrect = true,
            NeedsReview = false
        });
        vm.IsReviewConfirmed = true;
        vm.HasUnsavedWork = true;

        await vm.SaveCommand.ExecuteAsync(null);
        repo.Saved.Should().HaveCount(1);
        vm.HasUnsavedWork.Should().BeFalse();

        await vm.SaveCommand.ExecuteAsync(null);
        repo.Saved.Should().HaveCount(1, "duplicate Save click must be rejected by HasUnsavedWork guard");
        vm.Status.Should().Contain("已保存");
    }

    [Fact]
    public async Task NewPaper_reenables_Save_for_next_import()
    {
        var vm = BuildVm(out var repo);
        for (var i = 0; i < 50 && vm.SelectedStudent is null; i++) await Task.Delay(20);

        vm.Questions.Add(new ExtractedQuestionRow { Number = "1", IsCorrect = true });
        vm.IsReviewConfirmed = true;
        vm.HasUnsavedWork = true;
        await vm.SaveCommand.ExecuteAsync(null);
        vm.HasUnsavedWork.Should().BeFalse();

        vm.NewPaperCommand.Execute(null);
        vm.HasUnsavedWork.Should().BeTrue();
    }

    /// <summary>
    /// Regression for the "Save races extraction" finding: SaveAsync must
    /// reject the call when an extraction (IsBusy) is in flight, so the user
    /// can't persist a stale question grid as a new paper.
    /// </summary>
    [Fact]
    public async Task SaveAsync_is_rejected_while_extraction_is_busy()
    {
        var vm = BuildVm(out var repo);
        for (var i = 0; i < 50 && vm.SelectedStudent is null; i++) await Task.Delay(20);

        vm.Questions.Add(new ExtractedQuestionRow { Number = "1", IsCorrect = true });
        vm.IsReviewConfirmed = true;
        vm.HasUnsavedWork = true;
        vm.IsBusy = true; // simulate extraction in flight

        vm.CanSave.Should().BeFalse("CanSave must compose HasUnsavedWork && !IsBusy");
        await vm.SaveCommand.ExecuteAsync(null);

        repo.Saved.Should().BeEmpty("save during extraction must NOT reach the repository");
        vm.Status.Should().Contain("识别");
    }

    [Fact]
    public async Task CanSave_recomputes_when_HasUnsavedWork_or_IsBusy_changes()
    {
        var vm = BuildVm(out _);
        for (var i = 0; i < 50 && vm.SelectedStudent is null; i++) await Task.Delay(20);

        vm.HasUnsavedWork = true;
        vm.IsBusy = false;
        vm.CanSave.Should().BeTrue();

        vm.IsBusy = true;
        vm.CanSave.Should().BeFalse();

        vm.IsBusy = false;
        vm.HasUnsavedWork = false;
        vm.CanSave.Should().BeFalse();
    }
}

public class SettingsSecretClearTests
{
    private static SettingsViewModel BuildVm(out InMemorySettingsRepo settings)
    {
        settings = new InMemorySettingsRepo();
        return new SettingsViewModel(settings, new StubFilePicker());
    }

    [Fact]
    public async Task Saving_blank_VisionKey_clears_stored_secret()
    {
        var vm = BuildVm(out var settings);
        for (var i = 0; i < 50 && vm.Status != "已加载"; i++) await Task.Delay(20);

        vm.VisionGlmApiKey = "real-key-123";
        await vm.SaveCommand.ExecuteAsync(null);
        settings.Has(SettingsKeys.VisionGlmApiKey).Should().BeTrue();

        // User clears the field and saves again.
        vm.VisionGlmApiKey = "";
        await vm.SaveCommand.ExecuteAsync(null);

        settings.Has(SettingsKeys.VisionGlmApiKey).Should().BeFalse(
            "blank save must delete the secret, not silently skip and leave the old value behind");
    }

    [Fact]
    public async Task Clearing_both_keys_also_clears_legacy_GlmApiKey()
    {
        var vm = BuildVm(out var settings);
        for (var i = 0; i < 50 && vm.Status != "已加载"; i++) await Task.Delay(20);

        await settings.SetSecretAsync(SettingsKeys.GlmApiKey, "legacy-fallback");
        vm.VisionGlmApiKey = "";
        vm.LanguageGlmApiKey = "";
        await vm.SaveCommand.ExecuteAsync(null);

        settings.Has(SettingsKeys.GlmApiKey).Should().BeFalse();
    }

    /// <summary>
    /// Regression for the "legacy fallback survives single-key clear" finding:
    /// clearing only VisionGlmApiKey must revoke legacy so VisionModelFactory's
    /// fallback chain can't quietly reach the old credential.
    /// </summary>
    [Fact]
    public async Task Clearing_only_VisionKey_also_revokes_legacy_GlmApiKey()
    {
        var vm = BuildVm(out var settings);
        for (var i = 0; i < 50 && vm.Status != "已加载"; i++) await Task.Delay(20);

        await settings.SetSecretAsync(SettingsKeys.GlmApiKey, "legacy-fallback");
        vm.VisionGlmApiKey = "";
        vm.LanguageGlmApiKey = "language-still-valid";
        await vm.SaveCommand.ExecuteAsync(null);

        settings.Has(SettingsKeys.VisionGlmApiKey).Should().BeFalse(
            "blank Vision key must be deleted");
        (await settings.GetSecretAsync(SettingsKeys.LanguageGlmApiKey)).Should().Be("language-still-valid");
        settings.Has(SettingsKeys.GlmApiKey).Should().BeFalse(
            "legacy must be revoked even when only one of the split keys was cleared");
    }

    [Fact]
    public async Task Clearing_only_LanguageKey_also_revokes_legacy_GlmApiKey()
    {
        var vm = BuildVm(out var settings);
        for (var i = 0; i < 50 && vm.Status != "已加载"; i++) await Task.Delay(20);

        await settings.SetSecretAsync(SettingsKeys.GlmApiKey, "legacy-fallback");
        vm.VisionGlmApiKey = "vision-still-valid";
        vm.LanguageGlmApiKey = "";
        await vm.SaveCommand.ExecuteAsync(null);

        (await settings.GetSecretAsync(SettingsKeys.VisionGlmApiKey)).Should().Be("vision-still-valid");
        settings.Has(SettingsKeys.LanguageGlmApiKey).Should().BeFalse();
        settings.Has(SettingsKeys.GlmApiKey).Should().BeFalse();
    }
}

public class SettingsRepositorySecretReadTests
{
    [Fact]
    public async Task GetSecretAsync_returns_null_when_encrypted_value_cannot_be_unprotected()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var factory = new TestDbContextFactory(options);
        await using (var db = factory.CreateDbContext())
        {
            db.AppSettings.Add(new AppSetting
            {
                Key = "secret",
                Value = "dpapi:not-valid-base64",
                IsEncrypted = true
            });
            await db.SaveChangesAsync();
        }

        var repo = new SettingsRepository(factory);

        var value = await repo.GetSecretAsync("secret");

        value.Should().BeNull("restored backups may contain secrets encrypted for another Windows user");
    }

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
