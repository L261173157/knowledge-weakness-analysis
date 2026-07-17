using FluentAssertions;
using KnowledgeWeakness.Core.Analysis;

namespace KnowledgeWeakness.Tests;

public class GradingDecisionHelperTests
{
    [Fact]
    public void Consensus_returns_value_when_all_known_judgments_match()
    {
        GradingDecisionHelper.Consensus(true, true, true).Should().BeTrue();
        GradingDecisionHelper.Consensus(false, false, false).Should().BeFalse();
    }

    [Fact]
    public void Consensus_requires_all_judgments_to_be_known_and_matching()
    {
        GradingDecisionHelper.Consensus(true, null, true).Should().BeNull();
        GradingDecisionHelper.Consensus(true, false, true).Should().BeNull();
    }

    [Fact]
    public void NeedsReview_is_true_when_no_consensus_exists()
    {
        GradingDecisionHelper.NeedsReview(null, null, null).Should().BeTrue();
        GradingDecisionHelper.NeedsReview(true, false, null).Should().BeTrue();
        GradingDecisionHelper.NeedsReview(false, false, null).Should().BeTrue();
        GradingDecisionHelper.NeedsReview(false, false, false).Should().BeFalse();
    }

    [Fact]
    public void ResolveForImport_treats_answer_text_mismatch_as_incorrect_without_consensus()
    {
        GradingDecisionHelper.ResolveForImport(
                aiJudgment: true,
                teacherJudgment: true,
                answerTextJudgment: false)
            .Should().BeFalse();
    }

    [Fact]
    public void ResolveForImport_uses_teacher_or_ai_judgment_when_answer_text_cannot_be_judged()
    {
        GradingDecisionHelper.ResolveForImport(
                aiJudgment: null,
                teacherJudgment: true,
                answerTextJudgment: null)
            .Should().BeTrue();

        GradingDecisionHelper.ResolveForImport(
                aiJudgment: false,
                teacherJudgment: null,
                answerTextJudgment: null)
            .Should().BeFalse();
    }
}
