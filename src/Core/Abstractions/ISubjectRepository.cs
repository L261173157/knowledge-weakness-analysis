using KnowledgeWeakness.Core.Domain;

namespace KnowledgeWeakness.Core.Abstractions;

public interface ISubjectRepository
{
    Task<IReadOnlyList<Subject>> ListAsync(CancellationToken ct = default);
    Task<Subject?> GetAsync(int id, CancellationToken ct = default);
    Task<Subject?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task AddAsync(Subject subject, CancellationToken ct = default);
    Task UpdateAsync(Subject subject, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
