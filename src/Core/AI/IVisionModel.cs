using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KnowledgeWeakness.Core.AI;

public interface IVisionModel
{
    string ProviderCode { get; }
    string DisplayName { get; }

    Task<PaperExtraction> ExtractPaperAsync(
        IReadOnlyList<byte[]> pageImages,
        SubjectContext subject,
        CancellationToken ct = default);
}

public interface IVisionModelFactory
{
    Task<IVisionModel> CreateAsync(string providerCode, CancellationToken ct = default);
    IReadOnlyList<string> AvailableProviders { get; }
}
