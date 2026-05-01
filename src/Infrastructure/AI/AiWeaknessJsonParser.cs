using System.Text.Json;
using KnowledgeWeakness.Core.Analysis;

namespace KnowledgeWeakness.Infrastructure.AI;

public static class AiWeaknessJsonParser
{
    public static AiWeaknessAnalysisResult Parse(string rawContent)
    {
        var json = ExtractJsonBlock(rawContent);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var summary = TryGetString(root, "summary") ?? "";
        var points = new List<AiWeaknessPoint>();
        if (root.TryGetProperty("points", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var point in arr.EnumerateArray())
            {
                points.Add(new AiWeaknessPoint(
                    TryGetString(point, "knowledge_point") ?? "",
                    TryGetString(point, "severity") ?? "Medium",
                    TryGetString(point, "weak_reason") ?? "",
                    TryGetStringArray(point, "question_numbers"),
                    TryGetString(point, "review_advice") ?? "",
                    TryGetString(point, "practice_direction") ?? ""));
            }
        }

        return new AiWeaknessAnalysisResult(summary, points, json);
    }

    private static string ExtractJsonBlock(string raw)
    {
        var text = raw.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline > 0) text = text[(firstNewline + 1)..];
            var fenceEnd = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fenceEnd >= 0) text = text[..fenceEnd];
        }

        var braceStart = text.IndexOf('{');
        var braceEnd = text.LastIndexOf('}');
        if (braceStart >= 0 && braceEnd > braceStart)
            text = text.Substring(braceStart, braceEnd - braceStart + 1);

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

    private static IReadOnlyList<string> TryGetStringArray(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array)
            return [];

        return v.EnumerateArray()
            .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .ToList();
    }
}
