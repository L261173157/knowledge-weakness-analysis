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
        services.AddSingleton<IKnowledgePointRepository, KnowledgePointRepository>();

        // Outer HttpClient timeout disabled — each call wraps itself in a
        // CancellationTokenSource (see GlmVisionProvider / GlmWeaknessAnalyzer),
        // and GLM-4.6V multi-page vision can legitimately run > 5 min.
        services.AddHttpClient("glm", c => c.Timeout = Timeout.InfiniteTimeSpan);

        services.AddSingleton<IVisionModelFactory, VisionModelFactory>();
        services.AddSingleton<IWeaknessAnalyzer>(sp => new GlmWeaknessAnalyzer(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("glm"),
            sp.GetRequiredService<ISettingsRepository>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GlmWeaknessAnalyzer>>()));
        services.AddSingleton(new KnowledgeBaseReader(Path.Combine(AppContext.BaseDirectory, "knowledge-bases")));
        services.AddSingleton<ImagePreprocessor>();

        return services;
    }

    public static void EnsureDatabaseCreated(IServiceProvider sp)
    {
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        // EnsureCreated is a no-op for pre-existing databases, so any new entity
        // added after the first release would otherwise be missing on upgrade.
        // SchemaUpgrader runs idempotent CREATE-IF-NOT-EXISTS patches to close that gap.
        SchemaUpgrader.Apply(db);
    }
}
