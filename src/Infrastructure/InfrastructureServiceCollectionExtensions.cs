using KnowledgeWeakness.Core.AI;
using KnowledgeWeakness.Core.Abstractions;
using KnowledgeWeakness.Infrastructure.AI;
using KnowledgeWeakness.Infrastructure.Analysis;
using KnowledgeWeakness.Infrastructure.Imaging;
using KnowledgeWeakness.Infrastructure.Persistence;
using KnowledgeWeakness.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeWeakness.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string sqliteConnectionString)
    {
        services.AddDbContextFactory<AppDbContext>(opt => opt.UseSqlite(sqliteConnectionString));
        services.AddSingleton<IStudentRepository, StudentRepository>();
        services.AddSingleton<ISubjectRepository, SubjectRepository>();
        services.AddSingleton<IPaperRepository, PaperRepository>();
        services.AddSingleton<ISettingsRepository, SettingsRepository>();

        services.AddHttpClient("glm", c => c.Timeout = TimeSpan.FromSeconds(300));

        services.AddSingleton<IVisionModelFactory, VisionModelFactory>();
        services.AddSingleton<IWeaknessAnalyzer>(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsRepository>();
            var opts = new GlmVisionOptions
            {
                ApiKey = settings.GetSecretAsync(SettingsKeys.GlmApiKey).GetAwaiter().GetResult(),
                Model = settings.GetAsync(SettingsKeys.GlmModel).GetAwaiter().GetResult() ?? "glm-4.6v"
            };
            return new GlmWeaknessAnalyzer(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("glm"),
                opts,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GlmWeaknessAnalyzer>>());
        });
        services.AddSingleton(new KnowledgeBaseReader(Path.Combine(AppContext.BaseDirectory, "knowledge-bases")));
        services.AddSingleton<ImagePreprocessor>();

        return services;
    }

    public static void EnsureDatabaseCreated(IServiceProvider sp)
    {
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
    }
}
