using KnowledgeWeakness.Core.Abstractions;
using KnowledgeWeakness.Core.Domain;
using KnowledgeWeakness.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeWeakness.Infrastructure.Repositories;

public class KnowledgePointRepository(IDbContextFactory<AppDbContext> dbFactory) : IKnowledgePointRepository
{
    public async Task<IReadOnlyList<KnowledgePoint>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.KnowledgePoints.AsNoTracking()
            .Include(p => p.Subject)
            .OrderBy(p => p.SubjectId)
            .ThenBy(p => p.Id)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<KnowledgePoint>> ListBySubjectAsync(int subjectId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.KnowledgePoints.AsNoTracking()
            .Where(p => p.SubjectId == subjectId)
            .OrderBy(p => p.Id)
            .ToListAsync(ct);
    }

    public async Task<int> AddAsync(KnowledgePoint point, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.KnowledgePoints.Add(point);
        await db.SaveChangesAsync(ct);
        return point.Id;
    }

    public async Task UpdateAsync(KnowledgePoint point, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.KnowledgePoints.Update(point);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await db.KnowledgePoints.FindAsync(new object?[] { id }, ct);
        if (entity is null) return;
        db.KnowledgePoints.Remove(entity);
        await db.SaveChangesAsync(ct);
    }
}
