using System.Text;
using System.Text.RegularExpressions;

namespace KnowledgeWeakness.Core.Analysis;

public static partial class AnswerTextHelper
{
    public static string Combine(string? option, string? text)
    {
        var cleanOption = NormalizeOptionForDisplay(option);
        var cleanText = text?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(cleanOption)) return cleanText;
        if (string.IsNullOrWhiteSpace(cleanText)) return cleanOption;
        return $"{cleanOption} {cleanText}";
    }

    public static (string Option, string Text) Split(string? answer)
    {
        var text = answer?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(text)) return ("", "");

        var match = LeadingOptionPattern().Match(text);
        if (!match.Success) return ("", text);

        return (
            NormalizeOptionForDisplay(match.Groups["option"].Value),
            match.Groups["text"].Value.Trim());
    }

    public static bool? Judge(
        string? standardOption,
        string? standardText,
        string? studentOption,
        string? studentText)
    {
        var expectedOption = NormalizeOptionForCompare(standardOption);
        var actualOption = NormalizeOptionForCompare(studentOption);
        if (!string.IsNullOrWhiteSpace(expectedOption) && !string.IsNullOrWhiteSpace(actualOption))
        {
            return expectedOption == actualOption;
        }

        var expectedText = NormalizeTextForCompare(standardText);
        var actualText = NormalizeTextForCompare(studentText);
        if (!string.IsNullOrWhiteSpace(expectedText) && !string.IsNullOrWhiteSpace(actualText))
        {
            return expectedText == actualText;
        }

        return null;
    }

    public static string? ExtractOptionText(string? stem, string? option)
    {
        if (string.IsNullOrWhiteSpace(stem) || string.IsNullOrWhiteSpace(option)) return null;

        var normalizedOption = NormalizeOptionForCompare(option);
        if (string.IsNullOrWhiteSpace(normalizedOption)) return null;

        var matches = OptionMarkerPattern().Matches(stem);
        if (matches.Count == 0) return null;

        var optionTexts = new Dictionary<char, string>();
        for (var i = 0; i < matches.Count; i++)
        {
            var key = char.ToUpperInvariant(matches[i].Groups["option"].Value[0]);
            var start = matches[i].Index + matches[i].Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : stem.Length;
            var text = CleanOptionText(stem[start..end]);
            if (!string.IsNullOrWhiteSpace(text))
            {
                optionTexts[key] = text;
            }
        }

        var selectedTexts = normalizedOption
            .Where(optionTexts.ContainsKey)
            .Select(x => optionTexts[x])
            .ToList();

        return selectedTexts.Count == 0 ? null : string.Join("；", selectedTexts);
    }

    private static string NormalizeOptionForDisplay(string? value)
    {
        var normalized = NormalizeOptionForCompare(value);
        if (string.IsNullOrWhiteSpace(normalized)) return "";
        return string.Join("", normalized.Order());
    }

    private static string NormalizeOptionForCompare(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";

        var letters = new SortedSet<char>();
        foreach (var ch in value)
        {
            var upper = char.ToUpperInvariant(ch);
            if (upper is >= 'A' and <= 'H')
            {
                letters.Add(upper);
            }
        }

        return new string(letters.ToArray());
    }

    private static string NormalizeTextForCompare(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";

        var builder = new StringBuilder();
        foreach (var ch in value.Trim())
        {
            if (!char.IsWhiteSpace(ch) && !char.IsPunctuation(ch))
            {
                builder.Append(char.ToUpperInvariant(ch));
            }
        }

        return builder.ToString();
    }

    private static string CleanOptionText(string value)
    {
        return value
            .Trim()
            .Trim(';', '；', ',', '，', '.', '。', '、');
    }

    [GeneratedRegex(@"^\s*(?<option>[A-Ha-h](?:\s*[,/、，]\s*[A-Ha-h])+|[A-Ha-h]{1,8})\s*(?:(?:[\.．。:：\)\）\-、]\s*)(?<text>.*)|$)")]
    private static partial Regex LeadingOptionPattern();

    [GeneratedRegex(@"(?<![A-Za-z])[\(（]?(?<option>[A-Ha-h])[\)）\.．、:：]\s*")]
    private static partial Regex OptionMarkerPattern();
}
