using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using KnowledgeWeakness.App.Services;
using KnowledgeWeakness.App.ViewModels;
using KnowledgeWeakness.App.Views;
using KnowledgeWeakness.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace KnowledgeWeakness.App;

public partial class App : Application
{
    public static IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppPaths.EnsureDirectories();
        AppPaths.MigrateLegacyProgramDataIfNeeded();

        // Apply any pending restore staged in a previous session BEFORE we
        // construct the DbContext factory — otherwise live SQLite handles
        // would race with the file replacement.
        if (BackupService.TryApplyPendingRestore(out var restoreError) && restoreError is null)
        {
            BackupService.RecordRestoreStatus("Restore completed successfully.");
            // applied — nothing more to do; fall through to normal startup
        }
        else if (!string.IsNullOrEmpty(restoreError))
        {
            BackupService.RecordRestoreStatus("Restore failed: " + restoreError);
            // Will be surfaced via logs once Serilog is up; for now write to debug.
            System.Diagnostics.Debug.WriteLine($"[Restore] Failed to apply pending restore: {restoreError}");
        }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(AppPaths.LogDirectory, "app-.log"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14,
                shared: true, flushToDiskInterval: TimeSpan.FromSeconds(1))
            .CreateLogger();

        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        InfrastructureServiceCollectionExtensions.EnsureDatabaseCreated(Services);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>()
            };
            desktop.MainWindow = window;

            if (Services.GetService<IFilePickerService>() is FilePickerService picker)
            {
                picker.TopLevel = window;
            }

            desktop.Exit += (_, _) => Log.CloseAndFlush();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddInfrastructure($"Data Source={AppPaths.DatabasePath}");

        services.AddLogging(b =>
        {
            b.ClearProviders();
            b.AddSerilog(dispose: true);
        });

        services.AddSingleton<IFilePickerService, FilePickerService>();

        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<StudentsViewModel>();
        services.AddTransient<SubjectsViewModel>();
        services.AddTransient<KnowledgePointsViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<PaperImportViewModel>();
        services.AddTransient<PapersViewModel>();
        services.AddTransient<MistakesViewModel>();
        services.AddTransient<AnalysisViewModel>();
        services.AddTransient<TrendsViewModel>();
    }
}
