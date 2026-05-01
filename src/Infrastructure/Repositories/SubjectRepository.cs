using KnowledgeWeakness.Core.Abstractions;
using KnowledgeWeakness.Core.Domain;
using KnowledgeWeakness.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeWeakness.Infrastructure.Repositories;

public class SubjectRepository(IDbContextFactory<AppDbContext> dbFactory) : ISubjectRepository
{
    public async Task<IReadOnlyList<Subject>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Subjects.AsNoTracking().OrderBy(s => s.Code).ToListAsync(ct);
    }

    public async Task<Subject?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Subjects.FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<Subject?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Subjects.FirstOrDefaultAsync(s => s.Code == code, ct);
    }

    public async Task AddAsync(Subject subject, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Subjects.Add(subject);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Subject subject, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Subjects.Update(subject);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await db.Subjects.FindAsync([id], ct);
        if (entity is null) return;
        db.Subjects.Remove(entity);
        await db.SaveChangesAsync(ct);
    }
}
