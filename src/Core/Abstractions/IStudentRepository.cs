using KnowledgeWeakness.Core.Domain;

namespace KnowledgeWeakness.Core.Abstractions;

public interface IStudentRepository
{
    Task<IReadOnlyList<Student>> ListAsync(CancellationToken ct = default);
    Task<Student?> GetAsync(int id, CancellationToken ct = default);
    Task AddAsync(Student student, CancellationToken ct = default);
    Task UpdateAsync(Student student, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
