using KnowledgeWeakness.Core.Domain;

namespace KnowledgeWeakness.Core.Analysis;

public static class WeaknessCandidateSelector
{
    public static IReadOnlyList<WeaknessCandidate> Select(IEnumerable<Paper> papers)
    {
        return papers
            .Where(IsManuallyConfirmed)
            .SelectMany(paper => paper.Questions
                .Where(IsWeakCandidate)
                .Select(question => ToCandidate(paper, question)))
            .ToList();
    }

    private static bool IsManuallyConfirmed(Paper paper)
    {
        return paper.Status == ImportStatus.Reviewed && paper.ReviewedAt is not null;
    }

    private static bool IsWeakCandidate(Question question)
    {
        var answer = question.StudentAnswer;
        if (answer is null) return false;
        if (IsUnreviewed(answer)) return false;

        return !answer.IsCorrect || answer.PartialScore is not null;
    }

    private static bool IsUnreviewed(StudentAnswer answer)
    {
        var comment = answer.TeacherComment?.Trim();
        return !string.IsNullOrWhiteSpace(comment)
               && (comment.Contains("未批改", StringComparison.Ordinal)
                   || comment.Contains("未判", StringComparison.Ordinal)
                   || comment.Contains("未评价", StringComparison.Ordinal));
    }

    private static WeaknessCandidate ToCandidate(Paper paper, Question question)
    {
        var answer = question.StudentAnswer!;
        return new WeaknessCandidate(
            paper.Id,
            paper.Title,
            paper.Student?.Name ?? "",
            paper.Student?.Grade ?? "",
            paper.Subject?.Name ?? "",
            paper.Date,
            question.Number,
            question.Type.ToString(),
            question.Stem,
            question.StandardAnswer,
            answer.AnswerText,
            answer.IsCorrect,
            answer.PartialScore,
            answer.TeacherComment);
    }
}
