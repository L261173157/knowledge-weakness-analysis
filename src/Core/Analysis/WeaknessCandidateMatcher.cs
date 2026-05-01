using System.Text.RegularExpressions;

namespace KnowledgeWeakness.Core.Analysis;

public static partial class WeaknessCandidateMatcher
{
    public static IReadOnlyList<WeaknessCandidate> MatchByQuestionNumbers(
        IEnumerable<WeaknessCandidate> candidates,
        IEnumerable<string> questionNumbers)
    {
        var candidateList = candidates.ToList();
        var requested = questionNumbers
            .SelectMany(SplitQuestionNumbers)
            .Select(NormalizeQuestionNumber)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (requested.Count == 0)
            return candidateList;

        var matched = candidateList
            .Where(x => requested.Contains(NormalizeQuestionNumber(x.QuestionNumber)))
            .ToList();

        return matched.Count == 0 ? candidateList : matched;
    }

    private static IEnumerable<string> SplitQuestionNumbers(string value)
    {
        return value.Split(['，', ',', '、', ';', '；', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    }

    private static string NormalizeQuestionNumber(string value)
    {
        var text = value.Trim()
            .Replace("第", "", StringComparison.Ordinal)
            .Replace("题", "", StringComparison.Ordinal)
            .Replace("号", "", StringComparison.Ordinal)
            .Replace("#", "", StringComparison.Ordinal)
            .Replace("Q", "", StringComparison.OrdinalIgnoreCase);

        var match = QuestionNumberPattern().Match(text);
        return match.Success ? match.Value : text;
    }

    [GeneratedRegex(@"\d+(?:\.\d+)?")]
    private static partial Regex QuestionNumberPattern();
}
