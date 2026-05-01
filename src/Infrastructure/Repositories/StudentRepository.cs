using KnowledgeWeakness.Core.Abstractions;
using KnowledgeWeakness.Core.Domain;
using KnowledgeWeakness.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeWeakness.Infrastructure.Repositories;

public class StudentRepository(IDbContextFactory<AppDbContext> dbFactory) : IStudentRepository
{
    public async Task<IReadOnlyList<Student>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Students.AsNoTracking().OrderBy(s => s.Id).ToListAsync(ct);
    }

    public async Task<Student?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Students.FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task AddAsync(Student student, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Students.Add(student);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Student student, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Students.Update(student);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await db.Students.FindAsync([id], ct);
        if (entity is null) return;
        db.Students.Remove(entity);
        await db.SaveChangesAsync(ct);
    }
}
