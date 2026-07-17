using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KnowledgeWeakness.Core.Domain;

namespace KnowledgeWeakness.Core.Abstractions;

public interface IKnowledgePointRepository
{
    Task<IReadOnlyList<KnowledgePoint>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgePoint>> ListBySubjectAsync(int subjectId, CancellationToken ct = default);
    Task<int> AddAsync(KnowledgePoint point, CancellationToken ct = default);
    Task UpdateAsync(KnowledgePoint point, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
