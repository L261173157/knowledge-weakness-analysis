using KnowledgeWeakness.Core.AI;
using KnowledgeWeakness.Core.Abstractions;
using KnowledgeWeakness.Infrastructure.AI;
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
