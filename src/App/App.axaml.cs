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
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KnowledgeWeakness");
        Directory.CreateDirectory(dataDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(dataDir, "logs", "app-.log"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14,
                shared: true, flushToDiskInterval: TimeSpan.FromSeconds(1))
            .CreateLogger();

        var services = new ServiceCollection();
        ConfigureServices(services, dataDir);
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

    private static void ConfigureServices(IServiceCollection services, string dataDir)
    {
        var dbPath = Path.Combine(dataDir, "app.db");
        services.AddInfrastructure($"Data Source={dbPath}");

        services.AddLogging(b =>
        {
            b.ClearProviders();
            b.AddSerilog(dispose: true);
        });

        services.AddSingleton<IFilePickerService, FilePickerService>();

        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<StudentsViewModel>();
        services.AddTransient<SubjectsViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<PaperImportViewModel>();
        services.AddTransient<AnalysisViewModel>();
    }
}
