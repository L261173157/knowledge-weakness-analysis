using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeWeakness.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViewModelBase _currentPage;

    [ObservableProperty]
    private string _currentPageTitle = "学生管理";

    [ObservableProperty]
    private string _currentPageKey = "students";

    [ObservableProperty]
    private string _currentPageDescription = "维护参与练习的学生档案";

    public MainWindowViewModel()
    {
        _currentPage = App.Services!.GetRequiredService<StudentsViewModel>();
    }

    private void SetPage(string key, string title, string description)
    {
        CurrentPageKey = key;
        CurrentPageTitle = title;
        CurrentPageDescription = description;
    }

    [RelayCommand]
    private void GoToStudents()
    {
        CurrentPage = App.Services!.GetRequiredService<StudentsViewModel>();
        SetPage("students", "学生管理", "维护参与练习的学生档案");
    }

    [RelayCommand]
    private void GoToSubjects()
    {
        CurrentPage = App.Services!.GetRequiredService<SubjectsViewModel>();
        SetPage("subjects", "学科管理", "配置学科以及对应的知识库路径");
    }

    [RelayCommand]
    private void GoToKnowledgePoints()
    {
        CurrentPage = App.Services!.GetRequiredService<KnowledgePointsViewModel>();
        SetPage("knowledge-points", "知识点管理", "为每个学科维护知识点和归纳关键词");
    }

    [RelayCommand]
    private void GoToImport()
    {
        CurrentPage = App.Services!.GetRequiredService<PaperImportViewModel>();
        SetPage("import", "卷子导入", "上传试卷图片，AI 识别后逐题校对入库");
    }

    [RelayCommand]
    private void GoToPapers()
    {
        CurrentPage = App.Services!.GetRequiredService<PapersViewModel>();
        SetPage("papers", "历史卷子", "查看已入库的试卷、题目和原图");
    }

    [RelayCommand]
    private void GoToMistakes()
    {
        CurrentPage = App.Services!.GetRequiredService<MistakesViewModel>();
        SetPage("mistakes", "错题本", "按学生 / 学科 / 知识点筛选错题");
    }

    [RelayCommand]
    private void GoToAnalysis()
    {
        CurrentPage = App.Services!.GetRequiredService<AnalysisViewModel>();
        SetPage("analysis", "薄弱分析", "汇总薄弱知识点并由 AI 生成复习建议");
    }

    [RelayCommand]
    private void GoToTrends()
    {
        CurrentPage = App.Services!.GetRequiredService<TrendsViewModel>();
        SetPage("trends", "成绩趋势", "查看历次试卷的正确率走势");
    }

    [RelayCommand]
    private void GoToSettings()
    {
        CurrentPage = App.Services!.GetRequiredService<SettingsViewModel>();
        SetPage("settings", "系统设置", "配置大模型密钥、数据目录、导出与备份");
    }
}
