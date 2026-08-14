using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KnowledgeWeakness.App.ViewModels;
using KnowledgeWeakness.Core.Abstractions;
using KnowledgeWeakness.Core.Domain;
using Xunit;

namespace KnowledgeWeakness.Tests;

/// <summary>
/// Regression contract for the shared two-click delete confirmation used by
/// the students/subjects/knowledge-points pages: the first click only arms the
/// guard (warning button label), the second click within the timeout deletes,
/// and switching selection or the timeout itself cancels the pending state.
/// </summary>
public class TwoStepDeleteGuardTests
{
    [Fact]
    public void First_request_returns_false_and_second_returns_true()
    {
        var armedStates = new List<bool>();
        var guard = new TwoStepDeleteGuard(armedStates.Add);

        guard.RequestConfirmed().Should().BeFalse("first click must only arm the guard");
        guard.IsArmed.Should().BeTrue();
        guard.RequestConfirmed().Should().BeTrue("second click confirms");
        guard.IsArmed.Should().BeFalse();
        armedStates.Should().Equal(true, false);
    }

    [Fact]
    public void Cancel_resets_so_next_click_only_arms_again()
    {
        var guard = new TwoStepDeleteGuard(_ => { });
        guard.RequestConfirmed().Should().BeFalse();

        guard.Cancel();

        guard.IsArmed.Should().BeFalse();
        guard.RequestConfirmed().Should().BeFalse("a cancelled arm must not carry over to the next click");
    }

    [Fact]
    public async Task Timeout_auto_cancels_and_fires_callback()
    {
        var autoCancelled = false;
        var guard = new TwoStepDeleteGuard(_ => { }, () => autoCancelled = true, TimeSpan.FromMilliseconds(50));
        guard.RequestConfirmed().Should().BeFalse();

        await Task.Delay(300);

        guard.IsArmed.Should().BeFalse();
        autoCancelled.Should().BeTrue();
        guard.RequestConfirmed().Should().BeFalse("after auto-cancel the next click only arms again");
    }
}

public class StudentsViewModelDeleteConfirmationTests
{
    private sealed class RecordingStudentRepo : IStudentRepository
    {
        public List<Student> Students { get; } = new();
        public List<int> DeletedIds { get; } = new();
        public Exception? ThrowOnDelete { get; set; }

        public Task<IReadOnlyList<Student>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult((IReadOnlyList<Student>)Students);
        public Task<Student?> GetAsync(int id, CancellationToken ct = default) =>
            Task.FromResult(Students.FirstOrDefault(s => s.Id == id));
        public Task AddAsync(Student student, CancellationToken ct = default)
        { Students.Add(student); return Task.CompletedTask; }
        public Task UpdateAsync(Student student, CancellationToken ct = default) => Task.CompletedTask;

        public Task DeleteAsync(int id, CancellationToken ct = default)
        {
            if (ThrowOnDelete is not null) throw ThrowOnDelete;
            DeletedIds.Add(id);
            Students.RemoveAll(s => s.Id == id);
            return Task.CompletedTask;
        }
    }

    private static async Task<StudentsViewModel> CreateLoadedVmAsync(RecordingStudentRepo repo)
    {
        repo.Students.Add(new Student { Id = 1, Name = "张三", Grade = "初二" });
        repo.Students.Add(new Student { Id = 2, Name = "李四", Grade = "初二" });
        var vm = new StudentsViewModel(repo);
        for (var i = 0; i < 50 && vm.Students.Count == 0; i++) await Task.Delay(20);
        return vm;
    }

    [Fact]
    public async Task First_delete_click_only_arms_and_shows_warning_label()
    {
        var repo = new RecordingStudentRepo();
        var vm = await CreateLoadedVmAsync(repo);
        vm.Selected = vm.Students.First(s => s.Id == 1);

        await vm.DeleteCommand.ExecuteAsync(null);

        repo.DeletedIds.Should().BeEmpty("the first click must not delete");
        vm.DeleteButtonText.Should().Contain("再次点击确认删除").And.Contain("张三");
    }

    [Fact]
    public async Task Second_delete_click_within_timeout_deletes()
    {
        var repo = new RecordingStudentRepo();
        var vm = await CreateLoadedVmAsync(repo);
        vm.Selected = vm.Students.First(s => s.Id == 1);

        await vm.DeleteCommand.ExecuteAsync(null);
        await vm.DeleteCommand.ExecuteAsync(null);

        repo.DeletedIds.Should().Equal(1);
        vm.Selected.Should().BeNull();
        vm.Status.Should().Contain("已删除");
        vm.DeleteButtonText.Should().Be("删除");
    }

    [Fact]
    public async Task Switching_selection_cancels_pending_confirmation()
    {
        var repo = new RecordingStudentRepo();
        var vm = await CreateLoadedVmAsync(repo);
        vm.Selected = vm.Students.First(s => s.Id == 1);

        await vm.DeleteCommand.ExecuteAsync(null);
        vm.Selected = vm.Students.First(s => s.Id == 2); // cancels the armed state
        await vm.DeleteCommand.ExecuteAsync(null);

        repo.DeletedIds.Should().BeEmpty("clicking delete after a selection switch must only arm again");
        vm.DeleteButtonText.Should().Contain("再次点击确认删除").And.Contain("李四");
    }

    [Fact]
    public async Task Delete_failure_surfaces_status_message_instead_of_throwing()
    {
        var repo = new RecordingStudentRepo { ThrowOnDelete = new InvalidOperationException("FK constraint failed") };
        var vm = await CreateLoadedVmAsync(repo);
        vm.Selected = vm.Students.First(s => s.Id == 1);

        await vm.DeleteCommand.ExecuteAsync(null);
        var act = () => vm.DeleteCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync("an FK-restricted delete must not crash the app");
        repo.DeletedIds.Should().BeEmpty();
        vm.Status.Should().Contain("删除失败").And.Contain("历史卷子");
    }
}
