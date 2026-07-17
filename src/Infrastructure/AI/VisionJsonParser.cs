using System.Text.Json;
using KnowledgeWeakness.Core.AI;
using KnowledgeWeakness.Core.Analysis;
using KnowledgeWeakness.Core.Domain;

namespace KnowledgeWeakness.Infrastructure.AI;

public static class VisionJsonParser
{
    public static PaperExtraction Parse(string rawContent)
    {
        var json = ExtractJsonBlock(rawContent);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string? title = TryGetString(root, "title");
        string? date = TryGetString(root, "date");

        var questions = new List<ExtractedQuestion>();
        if (root.TryGetProperty("questions", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var q in arr.EnumerateArray())
            {
                var number = TryGetString(q, "number") ?? "";
                var typeStr = TryGetString(q, "type") ?? "Unknown";
                var type = Enum.TryParse<QuestionType>(typeStr, ignoreCase: true, out var t) ? t : QuestionType.Unknown;
                var stem = TryGetString(q, "stem") ?? "";
                var options = TryGetOptions(q);
                var legacyStandardAnswer = TryGetString(q, "standard_answer");
                var legacyStudentAnswer = TryGetString(q, "student_answer") ?? "";
                var (legacyStandardOption, legacyStandardText) = AnswerTextHelper.Split(legacyStandardAnswer);
                var (legacyStudentOption, legacyStudentText) = AnswerTextHelper.Split(legacyStudentAnswer);
                var standardAnswerOption =
                    TryGetString(q, "standard_answer_option")
                    ?? TryGetString(q, "standard_option")
                    ?? TryGetString(q, "correct_option")
                    ?? legacyStandardOption;
                var standardAnswerText =
                    TryGetString(q, "standard_answer_text")
                    ?? TryGetString(q, "standard_text")
                    ?? legacyStandardText;
                var studentAnswerOption =
                    TryGetString(q, "student_answer_option")
                    ?? TryGetString(q, "student_option")
                    ?? legacyStudentOption;
                var studentAnswerText =
                    TryGetString(q, "student_answer_text")
                    ?? TryGetString(q, "student_text")
                    ?? legacyStudentText;

                if (string.IsNullOrWhiteSpace(standardAnswerText))
                {
                    standardAnswerText =
                        TryGetOptionText(options, standardAnswerOption)
                        ?? AnswerTextHelper.ExtractOptionText(stem, standardAnswerOption);
                }

                if (string.IsNullOrWhiteSpace(studentAnswerText))
                {
                    studentAnswerText =
                        TryGetOptionText(options, studentAnswerOption)
                        ?? AnswerTextHelper.ExtractOptionText(stem, studentAnswerOption)
                        ?? legacyStudentAnswer;
                }

                var teacherIsCorrect =
                    TryGetBool(q, "teacher_is_correct")
                    ?? TryGetBool(q, "teacher_correct")
                    ?? TryGetBool(q, "red_pen_is_correct")
                    ?? TryGetBool(q, "is_correct");
                var aiIsCorrect =
                    TryGetBool(q, "ai_is_correct")
                    ?? TryGetBool(q, "ai_correct")
                    ?? TryGetBool(q, "model_is_correct");
                var isCorrect = TryGetBool(q, "is_correct") ?? teacherIsCorrect ?? aiIsCorrect ?? false;
                var partial = TryGetDouble(q, "partial_score");
                var teacherComment = TryGetString(q, "teacher_comment");

                questions.Add(new ExtractedQuestion(
                    number, type, stem,
                    options,
                    standardAnswerOption, standardAnswerText,
                    studentAnswerOption, studentAnswerText,
                    isCorrect, partial, teacherComment,
                    teacherIsCorrect, aiIsCorrect));
            }
        }

        return new PaperExtraction(title, date, questions, json);
    }

    private static string ExtractJsonBlock(string raw)
    {
        var text = raw.Trim();
        if (text.StartsWith("```"))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline > 0) text = text[(firstNewline + 1)..];
            var fenceEnd = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fenceEnd >= 0) text = text[..fenceEnd];
        }
        var braceStart = text.IndexOf('{');
        var braceEnd = text.LastIndexOf('}');
        if (braceStart >= 0 && braceEnd > braceStart)
        {
            text = text.Substring(braceStart, braceEnd - braceStart + 1);
        }
        return text.Trim();
    }

    private static string? TryGetString(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Null => null,
            _ => v.ToString()
        };
    }

    private static bool? TryGetBool(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static double? TryGetDouble(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : null;
    }

    private static IReadOnlyDictionary<string, string> TryGetOptions(JsonElement question)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!TryGetOptionsElement(question, out var options)) return result;

        if (options.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in options.EnumerateObject())
            {
                var key = NormalizeOptionKey(property.Name);
                var value = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : property.Value.ToString();
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                {
                    result[key] = value.Trim();
                }
            }
        }
        else if (options.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in options.EnumerateArray())
            {
                var key = TryGetString(item, "key")
                          ?? TryGetString(item, "option")
                          ?? TryGetString(item, "label")
                          ?? TryGetString(item, "letter");
                var value = TryGetString(item, "text")
                            ?? TryGetString(item, "content")
                            ?? TryGetString(item, "value");
                key = NormalizeOptionKey(key);
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                {
                    result[key] = value.Trim();
                }
            }
        }

        return result;
    }

    private static bool TryGetOptionsElement(JsonElement question, out JsonElement options)
    {
        if (question.TryGetProperty("options", out options)) return true;
        if (question.TryGetProperty("choices", out options)) return true;
        if (question.TryGetProperty("choice_options", out options)) return true;
        return false;
    }

    private static string? TryGetOptionText(IReadOnlyDictionary<string, string> options, string? option)
    {
        if (options.Count == 0 || string.IsNullOrWhiteSpace(option)) return null;

        var values = new List<string>();
        foreach (var ch in option)
        {
            var key = NormalizeOptionKey(ch.ToString());
            if (!string.IsNullOrWhiteSpace(key) && options.TryGetValue(key, out var value))
            {
                values.Add(value);
            }
        }

        return values.Count == 0 ? null : string.Join("；", values);
    }

    private static string? NormalizeOptionKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        foreach (var ch in value)
        {
            var upper = char.ToUpperInvariant(ch);
            if (upper is >= 'A' and <= 'H') return upper.ToString();
        }

        return null;
    }
}
