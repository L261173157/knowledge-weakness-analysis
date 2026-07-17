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

    public ObservableCollection<Subject> Subjects { get; } = new();

    [ObservableProperty]
    private Subject? _selected;

    [ObservableProperty]
    private string _newCode = "";

    [ObservableProperty]
    private string _newName = "";

    [ObservableProperty]
    private string _status = "";

    public SubjectsViewModel(ISubjectRepository repo)
    {
        _repo = repo;
        _ = LoadAsync();
    }

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
        await _repo.DeleteAsync(Selected.Id);
        Selected = null;
        await LoadAsync();
    }
}
