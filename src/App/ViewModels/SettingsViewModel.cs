using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KnowledgeWeakness.App.Services;
using KnowledgeWeakness.Core.Abstractions;
using KnowledgeWeakness.Infrastructure.AI;

namespace KnowledgeWeakness.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsRepository _settings;
    private readonly IFilePickerService _filePicker;

    [ObservableProperty] private string _visionGlmApiKey = "";
    [ObservableProperty] private string _visionGlmModel = "glm-4.6v";
    [ObservableProperty] private string _visionGlmBaseUrl = "https://open.bigmodel.cn/api/paas/v4";
    [ObservableProperty] private string _languageGlmApiKey = "";
    [ObservableProperty] private string _languageGlmModel = "glm-4.6";
    [ObservableProperty] private string _languageGlmBaseUrl = "https://open.bigmodel.cn/api/paas/v4";
    [ObservableProperty] private string _exportDirectory = "";
    [ObservableProperty] private string _exportFormat = "Markdown";
    [ObservableProperty] private bool _exportIncludeImages;
    [ObservableProperty] private string _status = "";

    public string DataDirectory => AppPaths.DataDirectory;
    public string DatabasePath => AppPaths.DatabasePath;
    public string PaperImageDirectory => AppPaths.PaperImageDirectory;
    public string LogDirectory => AppPaths.LogDirectory;
    public string ProgramDirectory => AppPaths.ProgramDirectory;
    public string[] ExportFormats { get; } = ["Markdown", "CSV", "JSON", "PDF"];

    public SettingsViewModel(ISettingsRepository settings, IFilePickerService filePicker)
    {
        _settings = settings;
        _filePicker = filePicker;
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task PickExportDirectoryAsync()
    {
        var dir = await _filePicker.PickDirectoryAsync("选择默认导出目录");
        if (!string.IsNullOrWhiteSpace(dir)) ExportDirectory = dir;
    }

    [RelayCommand]
    private async Task BackupAsync()
    {
        try
        {
            var path = await _filePicker.PickBackupSaveAsync($"KnowledgeWeakness_backup_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
            if (string.IsNullOrEmpty(path)) { Status = "已取消备份"; return; }

            var directory = System.IO.Path.GetDirectoryName(path)!;
            var saved = await BackupService.ExportAsync(directory);
            if (!string.Equals(saved, path, StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(saved))
            {
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                System.IO.File.Move(saved, path);
            }

            Status = $"备份已保存至：{path}";
        }
        catch (Exception ex)
        {
            Status = "备份失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        try
        {
            var path = await _filePicker.PickBackupZipAsync();
            if (string.IsNullOrEmpty(path)) { Status = "已取消恢复"; return; }

            var staged = await BackupService.StageRestoreAsync(path);
            Status = $"备份已校验并暂存于：{staged.PendingDirectory}。重启应用后生效。";
        }
        catch (Exception ex)
        {
            Status = "恢复失败，当前数据未改动：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var legacyKey = await _settings.GetSecretAsync(SettingsKeys.GlmApiKey) ?? "";
        var legacyModel = await _settings.GetAsync(SettingsKeys.GlmModel);

        VisionGlmApiKey = await _settings.GetSecretAsync(SettingsKeys.VisionGlmApiKey) ?? legacyKey;
        VisionGlmModel = await _settings.GetAsync(SettingsKeys.VisionGlmModel) ?? legacyModel ?? "glm-4.6v";
        VisionGlmBaseUrl = await _settings.GetAsync(SettingsKeys.VisionGlmBaseUrl) ?? "https://open.bigmodel.cn/api/paas/v4";

        LanguageGlmApiKey = await _settings.GetSecretAsync(SettingsKeys.LanguageGlmApiKey) ?? legacyKey;
        LanguageGlmModel = await _settings.GetAsync(SettingsKeys.LanguageGlmModel) ?? "glm-4.6";
        LanguageGlmBaseUrl = await _settings.GetAsync(SettingsKeys.LanguageGlmBaseUrl) ?? "https://open.bigmodel.cn/api/paas/v4";

        var savedExportDirectory = await _settings.GetAsync(SettingsKeys.ExportDirectory);
        ExportDirectory = string.IsNullOrWhiteSpace(savedExportDirectory)
                          || savedExportDirectory == AppPaths.LegacyExportDirectory
            ? AppPaths.ExportDirectory
            : savedExportDirectory;
        ExportFormat = await _settings.GetAsync(SettingsKeys.ExportFormat) ?? "Markdown";
        ExportIncludeImages = bool.TryParse(
            await _settings.GetAsync(SettingsKeys.ExportIncludeImages),
            out var includeImages) && includeImages;

        var restoreStatus = BackupService.ReadRestoreStatus();
        if (!string.IsNullOrWhiteSpace(restoreStatus))
        {
            Status = restoreStatus;
            BackupService.ClearRestoreStatus();
            return;
        }

        Status = "设置已加载";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        await SaveSecretOrClearAsync(SettingsKeys.VisionGlmApiKey, VisionGlmApiKey);
        await SaveSecretOrClearAsync(SettingsKeys.LanguageGlmApiKey, LanguageGlmApiKey);
        await _settings.DeleteAsync(SettingsKeys.GlmApiKey);

        await _settings.SetAsync(SettingsKeys.VisionGlmModel, Normalize(VisionGlmModel, "glm-4.6v"));
        await _settings.SetAsync(SettingsKeys.VisionGlmBaseUrl, Normalize(VisionGlmBaseUrl, "https://open.bigmodel.cn/api/paas/v4"));
        await _settings.SetAsync(SettingsKeys.LanguageGlmModel, Normalize(LanguageGlmModel, "glm-4.6"));
        await _settings.SetAsync(SettingsKeys.LanguageGlmBaseUrl, Normalize(LanguageGlmBaseUrl, "https://open.bigmodel.cn/api/paas/v4"));
        await _settings.SetAsync(SettingsKeys.ExportDirectory, Normalize(ExportDirectory, AppPaths.ExportDirectory));
        await _settings.SetAsync(SettingsKeys.ExportFormat, Normalize(ExportFormat, "Markdown"));
        await _settings.SetAsync(SettingsKeys.ExportIncludeImages, ExportIncludeImages.ToString());

        Status = "设置已保存";
    }

    private async Task SaveSecretOrClearAsync(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            await _settings.DeleteAsync(key);
        else
            await _settings.SetSecretAsync(key, value.Trim());
    }

    private static string Normalize(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
