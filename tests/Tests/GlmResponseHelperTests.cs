using System;
using FluentAssertions;
using KnowledgeWeakness.Infrastructure.AI;

namespace KnowledgeWeakness.Tests;

/// <summary>
/// ExtractAssistantContent used to throw KeyNotFoundException when the GLM
/// response had no choices / empty choices / missing message, which escaped
/// the surrounding catch(JsonException) and surfaced as a raw stack trace.
/// These tests pin the defensive contract.
/// </summary>
public class GlmResponseHelperTests
{
    [Fact]
    public void Extracts_content_from_well_formed_response()
    {
        const string json = """
        {
            "choices": [
                { "message": { "content": "hello world" } }
            ]
        }
        """;

        GlmResponseHelper.ExtractAssistantContent(json).Should().Be("hello world");
    }

    [Fact]
    public void Returns_empty_when_content_is_null()
    {
        const string json = """
        {
            "choices": [
                { "message": { "content": null } }
            ]
        }
        """;

        GlmResponseHelper.ExtractAssistantContent(json).Should().Be("");
    }

    [Fact]
    public void Returns_empty_when_response_has_no_choices_property()
    {
        // Previously: GetProperty("choices") threw KeyNotFoundException,
        // escaping the catch(JsonException) in the caller.
        const string json = "{}";

        var act = () => GlmResponseHelper.ExtractAssistantContent(json);

        act.Should().NotThrow("the model can return an empty object on overload");
        act.Invoke().Should().Be("");
    }

    [Fact]
    public void Returns_empty_when_choices_array_is_empty()
    {
        // Previously: choices[0] threw ArgumentOutOfRangeException.
        const string json = """
        {
            "choices": []
        }
        """;

        var act = () => GlmResponseHelper.ExtractAssistantContent(json);

        act.Should().NotThrow("content moderation / overload returns empty choices");
        act.Invoke().Should().Be("");
    }

    [Fact]
    public void Returns_empty_when_message_property_is_missing()
    {
        // Previously: GetProperty("message") threw KeyNotFoundException.
        const string json = """
        {
            "choices": [ { "finish_reason": "length" } ]
        }
        """;

        var act = () => GlmResponseHelper.ExtractAssistantContent(json);

        act.Should().NotThrow();
        act.Invoke().Should().Be("");
    }

    [Fact]
    public void Returns_empty_when_content_property_is_missing()
    {
        // Previously: GetProperty("content") threw KeyNotFoundException.
        const string json = """
        {
            "choices": [ { "message": { "role": "assistant" } } ]
        }
        """;

        var act = () => GlmResponseHelper.ExtractAssistantContent(json);

        act.Should().NotThrow();
        act.Invoke().Should().Be("");
    }
}
