using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KnowledgeWeakness.App.ViewModels;
using KnowledgeWeakness.Core.Abstractions;
using KnowledgeWeakness.Core.Domain;

namespace KnowledgeWeakness.Tests;

public class ExtractedQuestionRowExplicitAdjudicationTests
{
    [Fact]
    public void AcceptCurrentJudgment_clears_NeedsReview_without_touching_IsCorrect()
    {
        // The scenario the previous fix could not handle: the imported IsCorrect
        // value already matches what the user wants, so flipping the checkbox
        // would record the wrong adjudication. The explicit command lets the
        // user accept the value as-is.
        var row = new ExtractedQuestionRow
        {
            Number = "7",
            IsCorrect = true,        // import says correct, user agrees
            NeedsReview = true,      // but AI/teacher/text-judgment disagreed
            AiIsCorrect = false,
            TeacherIsCorrect = true,
            AnswerTextIsCorrect = null
        };

        row.AcceptCurrentJudgmentCommand.Execute(null);

        row.NeedsReview.Should().BeFalse();
        row.IsCorrect.Should().BeTrue("explicit accept must not flip the user's intended value");
    }

    [Fact]
    public void AcceptCurrentJudgment_is_no_op_when_already_reviewed()
    {
        var row = new ExtractedQuestionRow { NeedsReview = false, IsCorrect = false };

        row.AcceptCurrentJudgmentCommand.Execute(null);

        row.NeedsReview.Should().BeFalse();
        row.IsCorrect.Should().BeFalse();
    }

    [Fact]
    public void ReviewActionText_tracks_NeedsReview_state()
    {
        var row = new ExtractedQuestionRow { NeedsReview = true };
        row.ReviewActionText.Should().Be("确认此判定");

        row.AcceptCurrentJudgmentCommand.Execute(null);
        row.ReviewActionText.Should().Be("已复核");
    }
}

public class PapersViewModelDeleteConfirmationTests
{
    private sealed class FakePaperRepo : IPaperRepository
    {
        public List<Paper> Papers { get; }
        public List<int> Deleted { get; } = new();
        public FakePaperRepo(IEnumerable<Paper> seed) { Papers = seed.ToList(); }
        public Task<IReadOnlyList<Paper>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult((IReadOnlyList<Paper>)Papers.ToList());
        public Task<IReadOnlyList<Paper>> ListWithQuestionsAsync(CancellationToken ct = default) =>
            Task.FromResult((IReadOnlyList<Paper>)Papers.ToList());
        public Task<Paper?> GetAsync(int id, CancellationToken ct = default) =>
            Task.FromResult(Papers.FirstOrDefault(p => p.Id == id));
        public Task<Paper?> GetWithQuestionsAsync(int id, CancellationToken ct = default) =>
            Task.FromResult(Papers.FirstOrDefault(p => p.Id == id));
        public Task<int> AddAsync(Paper paper, CancellationToken ct = default)
        { Papers.Add(paper); return Task.FromResult(paper.Id); }
        public Task UpdateAsync(Paper paper, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(int id, CancellationToken ct = default)
        {
            Deleted.Add(id);
            Papers.RemoveAll(p => p.Id == id);
            return Task.CompletedTask;
        }
        public Task ReplaceQuestionsAsync(int paperId, IEnumerable<Question> questions, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FakeStudentRepo : IStudentRepository
    {
        public Task<IReadOnlyList<Student>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult((IReadOnlyList<Student>)new List<Student>());
        public Task<Student?> GetAsync(int id, CancellationToken ct = default) => Task.FromResult<Student?>(null);
        public Task AddAsync(Student student, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Student student, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(int id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeSubjectRepo : ISubjectRepository
    {
        public Task<IReadOnlyList<Subject>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult((IReadOnlyList<Subject>)new List<Subject>());
        public Task<Subject?> GetAsync(int id, CancellationToken ct = default) => Task.FromResult<Subject?>(null);
        public Task<Subject?> GetByCodeAsync(string code, CancellationToken ct = default) => Task.FromResult<Subject?>(null);
        public Task AddAsync(Subject subject, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Subject subject, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(int id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static PapersViewModel BuildVm(out FakePaperRepo repo, params Paper[] papers)
    {
        repo = new FakePaperRepo(papers);
        return new PapersViewModel(repo, new FakeStudentRepo(), new FakeSubjectRepo());
    }

    private static async Task WaitForVmReadyAsync(PapersViewModel vm)
    {
        // The VM kicks off an async load in its ctor; give it a beat to settle.
        for (var i = 0; i < 50 && vm.SelectedPaper is null; i++)
            await Task.Delay(20);
    }

    [Fact]
    public async Task First_delete_click_only_arms_pending_state()
    {
        var vm = BuildVm(out var repo, new Paper { Id = 11, Title = "T", Date = System.DateOnly.FromDateTime(System.DateTime.Today) });
        await WaitForVmReadyAsync(vm);
        vm.SelectedPaper.Should().NotBeNull();

        await vm.DeleteCommand.ExecuteAsync(null);

        vm.DeletePending.Should().BeTrue();
        vm.DeleteButtonText.Should().Contain("再次点击确认");
        repo.Deleted.Should().BeEmpty("first click must NOT call the repository");
    }

    [Fact]
    public async Task Second_delete_click_within_window_actually_deletes()
    {
        var vm = BuildVm(out var repo, new Paper { Id = 22, Title = "T2", Date = System.DateOnly.FromDateTime(System.DateTime.Today) });
        await WaitForVmReadyAsync(vm);

        await vm.DeleteCommand.ExecuteAsync(null);
        await vm.DeleteCommand.ExecuteAsync(null);

        repo.Deleted.Should().ContainSingle().Which.Should().Be(22);
        vm.DeletePending.Should().BeFalse();
        vm.DeleteButtonText.Should().Be("删除卷子");
    }

    [Fact]
    public async Task Switching_selection_cancels_pending_delete()
    {
        var vm = BuildVm(out var repo,
            new Paper { Id = 1, Title = "A", Date = System.DateOnly.FromDateTime(System.DateTime.Today) },
            new Paper { Id = 2, Title = "B", Date = System.DateOnly.FromDateTime(System.DateTime.Today) });
        await WaitForVmReadyAsync(vm);

        await vm.DeleteCommand.ExecuteAsync(null);
        vm.DeletePending.Should().BeTrue();

        // Pick the other paper — this must cancel.
        vm.SelectedPaper = vm.Papers.First(p => p.Id != vm.SelectedPaper!.Id);

        vm.DeletePending.Should().BeFalse();
        repo.Deleted.Should().BeEmpty();
    }
}
