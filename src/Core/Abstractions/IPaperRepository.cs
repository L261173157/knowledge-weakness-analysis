using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KnowledgeWeakness.Core.Domain;

namespace KnowledgeWeakness.Core.Abstractions;

public interface IPaperRepository
{
    Task<IReadOnlyList<Paper>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Paper>> ListWithQuestionsAsync(CancellationToken ct = default);
    Task<Paper?> GetAsync(int id, CancellationToken ct = default);
    Task<Paper?> GetWithQuestionsAsync(int id, CancellationToken ct = default);
    Task<int> AddAsync(Paper paper, CancellationToken ct = default);
    Task UpdateAsync(Paper paper, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task ReplaceQuestionsAsync(int paperId, IEnumerable<Question> questions, CancellationToken ct = default);
}
