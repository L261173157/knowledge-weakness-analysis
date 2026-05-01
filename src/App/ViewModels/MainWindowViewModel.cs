using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeWeakness.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViewModelBase _currentPage;

    [ObservableProperty]
    private string _currentPageTitle = "学生";

    public MainWindowViewModel()
    {
        _currentPage = App.Services!.GetRequiredService<StudentsViewModel>();
    }

    [RelayCommand]
    private void GoToStudents()
    {
        CurrentPage = App.Services!.GetRequiredService<StudentsViewModel>();
        CurrentPageTitle = "学生";
    }

    [RelayCommand]
    private void GoToSubjects()
    {
        CurrentPage = App.Services!.GetRequiredService<SubjectsViewModel>();
        CurrentPageTitle = "学科";
    }

    [RelayCommand]
    private void GoToImport()
    {
        CurrentPage = App.Services!.GetRequiredService<PaperImportViewModel>();
        CurrentPageTitle = "卷子导入";
    }

    [RelayCommand]
    private void GoToAnalysis()
    {
        CurrentPage = App.Services!.GetRequiredService<AnalysisViewModel>();
        CurrentPageTitle = "薄弱分析";
    }

    [RelayCommand]
    private void GoToSettings()
    {
        CurrentPage = App.Services!.GetRequiredService<SettingsViewModel>();
        CurrentPageTitle = "设置";
    }
}
