namespace KnowledgeWeakness.Core.Analysis;

public static class GradingDecisionHelper
{
    public static bool ResolveForImport(bool? aiJudgment, bool? teacherJudgment, bool? answerTextJudgment)
    {
        var consensus = Consensus(aiJudgment, teacherJudgment, answerTextJudgment);
        if (consensus is not null) return consensus.Value;

        if (answerTextJudgment == false) return false;

        return teacherJudgment ?? aiJudgment ?? false;
    }

    public static bool? Consensus(params bool?[] judgments)
    {
        if (judgments.Length == 0 || judgments.Any(x => !x.HasValue)) return null;

        var first = judgments[0]!.Value;
        return judgments.All(x => x!.Value == first) ? first : null;
    }

    public static bool NeedsReview(params bool?[] judgments)
    {
        return Consensus(judgments) is null;
    }
}
