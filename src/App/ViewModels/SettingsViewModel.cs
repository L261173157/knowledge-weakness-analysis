using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KnowledgeWeakness.Core.Abstractions;
using KnowledgeWeakness.Infrastructure.AI;

namespace KnowledgeWeakness.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsRepository _settings;

    [ObservableProperty]
    private string _glmApiKey = "";

    [ObservableProperty]
    private string _glmModel = "glm-4.6v";

    [ObservableProperty]
    private string _status = "";

    public SettingsViewModel(ISettingsRepository settings)
    {
        _settings = settings;
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        GlmApiKey = await _settings.GetSecretAsync(SettingsKeys.GlmApiKey) ?? "";
        GlmModel = await _settings.GetAsync(SettingsKeys.GlmModel) ?? "glm-4.6v";
        Status = "已加载";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!string.IsNullOrWhiteSpace(GlmApiKey))
            await _settings.SetSecretAsync(SettingsKeys.GlmApiKey, GlmApiKey.Trim());
        await _settings.SetAsync(SettingsKeys.GlmModel, string.IsNullOrWhiteSpace(GlmModel) ? "glm-4.6v" : GlmModel.Trim());
        Status = "已保存";
    }
}
