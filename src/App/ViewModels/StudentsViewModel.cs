using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KnowledgeWeakness.Core.Abstractions;
using KnowledgeWeakness.Core.Domain;

namespace KnowledgeWeakness.App.ViewModels;

public partial class StudentsViewModel : ViewModelBase
{
    private readonly IStudentRepository _repo;

    public ObservableCollection<Student> Students { get; } = new();

    [ObservableProperty]
    private Student? _selected;

    [ObservableProperty]
    private string _newName = "";

    [ObservableProperty]
    private string _newGrade = "";

    [ObservableProperty]
    private string _status = "";

    public StudentsViewModel(IStudentRepository repo)
    {
        _repo = repo;
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        Students.Clear();
        foreach (var s in await _repo.ListAsync())
            Students.Add(s);
        Status = $"共 {Students.Count} 条";
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (string.IsNullOrWhiteSpace(NewName))
        {
            Status = "姓名不能为空";
            return;
        }
        await _repo.AddAsync(new Student { Name = NewName.Trim(), Grade = NewGrade.Trim() });
        NewName = "";
        NewGrade = "";
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
