using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KnowledgeWeakness.Core.Abstractions;
using KnowledgeWeakness.Core.Domain;

namespace KnowledgeWeakness.App.ViewModels;

public partial class KnowledgePointsViewModel : ViewModelBase
{
    private readonly IKnowledgePointRepository _repo;
    private readonly ISubjectRepository _subjectRepo;

    public ObservableCollection<Subject> Subjects { get; } = new();
    public ObservableCollection<KnowledgePoint> Points { get; } = new();

    [ObservableProperty] private Subject? _selectedSubject;
    [ObservableProperty] private KnowledgePoint? _selected;
    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private string _newKeywords = "";
    [ObservableProperty] private string _newDescription = "";
    [ObservableProperty] private string _status = "";

    public KnowledgePointsViewModel(IKnowledgePointRepository repo, ISubjectRepository subjectRepo)
    {
        _repo = repo;
        _subjectRepo = subjectRepo;
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        Subjects.Clear();
        foreach (var s in await _subjectRepo.ListAsync()) Subjects.Add(s);
        SelectedSubject = Subjects.FirstOrDefault();
        await LoadAsync();
    }

    partial void OnSelectedSubjectChanged(Subject? value) => _ = LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        Points.Clear();
        if (SelectedSubject is null)
        {
            Status = "请先在学科管理新建学科";
            return;
        }
        var list = await _repo.ListBySubjectAsync(SelectedSubject.Id);
        foreach (var p in list) Points.Add(p);
        Status = $"学科【{SelectedSubject.Name}】共 {Points.Count} 个知识点";
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (SelectedSubject is null) { Status = "请先选择学科"; return; }
        if (string.IsNullOrWhiteSpace(NewName)) { Status = "知识点名称不能为空"; return; }
        if (string.IsNullOrWhiteSpace(NewKeywords)) { Status = "请填写关键词（用空格或逗号分隔）"; return; }

        var point = new KnowledgePoint
        {
            SubjectId = SelectedSubject.Id,
            Name = NewName.Trim(),
            Keywords = NewKeywords.Trim(),
            Description = string.IsNullOrWhiteSpace(NewDescription) ? null : NewDescription.Trim()
        };
        await _repo.AddAsync(point);
        NewName = "";
        NewKeywords = "";
        NewDescription = "";
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
