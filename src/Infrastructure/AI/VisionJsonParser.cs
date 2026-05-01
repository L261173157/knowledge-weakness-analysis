using System.Text.Json;
using KnowledgeWeakness.Core.AI;
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
                var standardAnswer = TryGetString(q, "standard_answer");
                var studentAnswer = TryGetString(q, "student_answer") ?? "";
                var isCorrect = TryGetBool(q, "is_correct") ?? false;
                var partial = TryGetDouble(q, "partial_score");
                var teacherComment = TryGetString(q, "teacher_comment");

                questions.Add(new ExtractedQuestion(
                    number, type, stem, standardAnswer,
                    studentAnswer, isCorrect, partial, teacherComment));
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
}
