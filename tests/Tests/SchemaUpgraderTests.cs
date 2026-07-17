using System;
using System.IO;
using FluentAssertions;
using KnowledgeWeakness.Core.Domain;
using KnowledgeWeakness.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeWeakness.Tests;

public class SchemaUpgraderTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    public SchemaUpgraderTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"kw_upgrade_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath}";
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private DbContextOptions<AppDbContext> Options()
        => new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connectionString).Options;

    /// <summary>
    /// Build a "pre-KnowledgePoints" database by creating every table the original
    /// model had EXCEPT KnowledgePoints (and its index). Simulates an upgrade
    /// from the first release.
    /// </summary>
    private void SeedPreUpgradeDatabase()
    {
        using var db = new AppDbContext(Options());
        db.Database.EnsureCreated();
        // EnsureCreated on the current model already includes KnowledgePoints,
        // so drop it to fake the pre-upgrade shape.
        db.Database.ExecuteSqlRaw("DROP INDEX IF EXISTS \"IX_KnowledgePoints_SubjectId_Name\";");
        db.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS \"KnowledgePoints\";");
    }

    [Fact]
    public void Apply_creates_KnowledgePoints_table_when_missing()
    {
        SeedPreUpgradeDatabase();

        using (var db = new AppDbContext(Options()))
        {
            SchemaUpgrader.Apply(db);
        }

        using var verify = new AppDbContext(Options());
        // Should not throw — table must exist now.
        var subject = new Subject { Code = "ge", Name = "地理" };
        verify.Subjects.Add(subject);
        verify.SaveChanges();
        verify.KnowledgePoints.Add(new KnowledgePoint
        {
            SubjectId = subject.Id,
            Name = "气候",
            Keywords = "气候 降水"
        });
        verify.SaveChanges();
        verify.KnowledgePoints.Should().ContainSingle();
    }

    [Fact]
    public void Apply_is_idempotent_when_table_already_exists()
    {
        using (var db = new AppDbContext(Options()))
        {
            db.Database.EnsureCreated(); // table already there
            SchemaUpgrader.Apply(db);    // should be no-op
            SchemaUpgrader.Apply(db);    // run twice — still no-op
        }

        using var verify = new AppDbContext(Options());
        verify.KnowledgePoints.Should().BeEmpty();
    }

    [Fact]
    public void Apply_enforces_unique_subjectId_name_index()
    {
        SeedPreUpgradeDatabase();
        using (var db = new AppDbContext(Options()))
        {
            SchemaUpgrader.Apply(db);
        }

        using var seed = new AppDbContext(Options());
        var subject = new Subject { Code = "ge", Name = "地理" };
        seed.Subjects.Add(subject);
        seed.SaveChanges();
        seed.KnowledgePoints.Add(new KnowledgePoint
        {
            SubjectId = subject.Id,
            Name = "气候",
            Keywords = "气候"
        });
        seed.SaveChanges();

        seed.KnowledgePoints.Add(new KnowledgePoint
        {
            SubjectId = subject.Id,
            Name = "气候", // duplicate
            Keywords = "气候 降水"
        });
        var act = () => seed.SaveChanges();
        act.Should().Throw<DbUpdateException>();
    }
}
