using KnowledgeWeakness.Core.Analysis;

namespace KnowledgeWeakness.Core.AI;

public interface IWeaknessAnalyzer
{
    Task<AiWeaknessAnalysisResult> AnalyzeAsync(AiWeaknessAnalysisRequest request, CancellationToken ct = default);
}
