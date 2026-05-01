using KnowledgeWeakness.Core.Abstractions;
using KnowledgeWeakness.Core.Domain;
using KnowledgeWeakness.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeWeakness.Infrastructure.Repositories;

public class PaperRepository(IDbContextFactory<AppDbContext> dbFactory) : IPaperRepository
{
    public async Task<IReadOnlyList<Paper>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Papers.AsNoTracking()
            .Include(p => p.Student)
            .Include(p => p.Subject)
            .OrderByDescending(p => p.Id)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Paper>> ListWithQuestionsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Papers.AsNoTracking()
            .Include(p => p.Student)
            .Include(p => p.Subject)
            .Include(p => p.Questions)
                .ThenInclude(q => q.StudentAnswer)
            .OrderByDescending(p => p.Date)
            .ThenByDescending(p => p.Id)
            .ToListAsync(ct);
    }

    public async Task<Paper?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Papers.AsNoTracking()
            .Include(p => p.Student)
            .Include(p => p.Subject)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<Paper?> GetWithQuestionsAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Papers.AsNoTracking()
            .Include(p => p.Student)
            .Include(p => p.Subject)
            .Include(p => p.Questions).ThenInclude(q => q.StudentAnswer)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<int> AddAsync(Paper paper, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Papers.Add(paper);
        await db.SaveChangesAsync(ct);
        return paper.Id;
    }

    public async Task UpdateAsync(Paper paper, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Papers.Update(paper);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await db.Papers.FindAsync(new object?[] { id }, ct);
        if (entity is null) return;
        db.Papers.Remove(entity);
        await db.SaveChangesAsync(ct);
    }

    public async Task ReplaceQuestionsAsync(int paperId, IEnumerable<Question> questions, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.Questions.Where(q => q.PaperId == paperId).ToListAsync(ct);
        db.Questions.RemoveRange(existing);

        foreach (var q in questions)
        {
            q.Id = 0;
            q.PaperId = paperId;
            if (q.StudentAnswer is not null)
            {
                q.StudentAnswer.Id = 0;
                q.StudentAnswer.QuestionId = 0;
            }
            db.Questions.Add(q);
        }

        await db.SaveChangesAsync(ct);
    }
}
