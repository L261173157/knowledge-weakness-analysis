using System.Text.Json;

namespace KnowledgeWeakness.Infrastructure.AI;

/// <summary>
/// Defensive extraction of <c>choices[0].message.content</c> from an
/// OpenAI-compatible chat-completions response (as returned by GLM).
///
/// The GLM gateway can return shapes such as <c>{}</c> (rate-limit),
/// <c>{ "choices": [] }</c> (content-moderation refusal), or a choice
/// missing the <c>message</c>/<c>content</c> property. The previous
/// implementation called <see cref="JsonElement.GetProperty"/> on each
/// hop, which throws <see cref="KeyNotFoundException"/> /
/// <see cref="System.ArgumentOutOfRangeException"/> — those exceptions
/// escape the caller's <c>catch (JsonException)</c> and surface to the
/// user as a raw stack trace. This helper returns an empty string
/// instead, so the caller can decide how to present the empty result.
/// </summary>
public static class GlmResponseHelper
{
    public static string ExtractAssistantContent(string respJson)
    {
        using var doc = JsonDocument.Parse(respJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("choices", out var choices)) return "";
        if (choices.GetArrayLength() == 0) return "";

        var first = choices[0];
        if (!first.TryGetProperty("message", out var msg)) return "";
        if (!msg.TryGetProperty("content", out var content)) return "";

        return content.ValueKind == JsonValueKind.Null ? "" : content.GetString() ?? "";
    }
}
