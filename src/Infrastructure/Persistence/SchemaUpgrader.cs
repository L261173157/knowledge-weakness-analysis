using Microsoft.EntityFrameworkCore;

namespace KnowledgeWeakness.Infrastructure.Persistence;

/// <summary>
/// Idempotent SQLite schema patches applied at startup after EnsureCreated.
///
/// Why not EF migrations: this app shipped its first release using
/// <see cref="DatabaseFacade.EnsureCreated"/>, which does not track migrations
/// and silently skips any model changes for existing databases. Switching to
/// migrations mid-life would require generating a baseline + every later
/// migration and treating already-deployed databases as "post-baseline" —
/// expensive for a single-user desktop app. Instead, each new entity adds an
/// <c>IF NOT EXISTS</c> patch here. The patches are safe to run on:
///   - brand new DBs (EnsureCreated already produced the table — patches are
///     no-ops because of IF NOT EXISTS)
///   - pre-change DBs (table is missing — patches create it)
///   - already-patched DBs (no-op again)
/// </summary>
public static class SchemaUpgrader
{
    public static void Apply(AppDbContext db)
    {
        // KnowledgePoints — added in the multi-subject knowledge-point release.
        db.Database.ExecuteSqlRaw(@"
CREATE TABLE IF NOT EXISTS ""KnowledgePoints"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_KnowledgePoints"" PRIMARY KEY AUTOINCREMENT,
    ""SubjectId"" INTEGER NOT NULL,
    ""Name"" TEXT NOT NULL,
    ""Keywords"" TEXT NOT NULL,
    ""Description"" TEXT NULL,
    ""CreatedAt"" TEXT NOT NULL,
    CONSTRAINT ""FK_KnowledgePoints_Subjects_SubjectId"" FOREIGN KEY (""SubjectId"") REFERENCES ""Subjects"" (""Id"") ON DELETE CASCADE
);");
        db.Database.ExecuteSqlRaw(@"
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_KnowledgePoints_SubjectId_Name""
    ON ""KnowledgePoints"" (""SubjectId"", ""Name"");");
    }
}
