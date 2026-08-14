using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KnowledgeWeakness.Core.Abstractions;
using KnowledgeWeakness.Core.Domain;

namespace KnowledgeWeakness.App.ViewModels;

public partial class SubjectsViewModel : ViewModelBase
{
    private readonly ISubjectRepository _repo;
    private readonly TwoStepDeleteGuard _deleteGuard;

    public ObservableCollection<Subject> Subjects { get; } = new();

    [ObservableProperty]
    private Subject? _selected;

    [ObservableProperty]
    private string _newCode = "";

    [ObservableProperty]
    private string _newName = "";

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private string _deleteButtonText = "删除";

    public SubjectsViewModel(ISubjectRepository repo)
    {
        _repo = repo;
        _deleteGuard = new TwoStepDeleteGuard(
            armed => DeleteButtonText = armed
                ? $"再次点击确认删除「{Selected?.Name}」"
                : "删除",
            () => Status = "已取消删除");
        _ = LoadAsync();
    }

    partial void OnSelectedChanged(Subject? value) => _deleteGuard.Cancel();

    [RelayCommand]
    private async Task LoadAsync()
    {
        Subjects.Clear();
        foreach (var s in await _repo.ListAsync())
            Subjects.Add(s);
        Status = $"共 {Subjects.Count} 条";
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCode) || string.IsNullOrWhiteSpace(NewName))
        {
            Status = "编码和名称均不能为空";
            return;
        }

        if (await _repo.GetByCodeAsync(NewCode.Trim()) is not null)
        {
            Status = $"编码 {NewCode} 已存在";
            return;
        }

        await _repo.AddAsync(new Subject { Code = NewCode.Trim(), Name = NewName.Trim() });
        NewCode = "";
        NewName = "";
        await LoadAsync();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Selected is null) return;
        await _repo.UpdateAsync(Selected);
        Status = "已保存";
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Selected is null) return;

        if (!_deleteGuard.RequestConfirmed())
        {
            Status = "5 秒内再次点击“删除”按钮确认删除；切换选中学科会自动取消。";
            return;
        }

        try
        {
            await _repo.DeleteAsync(Selected.Id);
            Selected = null;
            await LoadAsync();
            // Set after the reload so the row-count status doesn't overwrite it.
            Status = "已删除";
        }
        catch (Exception ex)
        {
            // Paper→Subject is Restrict, so deleting a subject that still has
            // papers fails at the FK; surface that instead of crashing.
            Status = $"删除失败：{ex.Message}（学科仍被卷子引用时无法删除，请先在历史卷子中删除相关卷子）";
        }
    }
}
