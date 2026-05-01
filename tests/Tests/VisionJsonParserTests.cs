using FluentAssertions;
using KnowledgeWeakness.Core.Domain;
using KnowledgeWeakness.Infrastructure.AI;

namespace KnowledgeWeakness.Tests;

public class VisionJsonParserTests
{
    [Fact]
    public void Parses_plain_json()
    {
        const string raw = """
        {
            "title": "初二地理单元测验",
            "date": "2026-04-10",
            "questions": [
                {
                    "number": "1",
                    "type": "Choice",
                    "stem": "我国地势总体特征是",
                    "standard_answer": "西高东低",
                    "student_answer": "西高东低",
                    "is_correct": true,
                    "partial_score": 2,
                    "teacher_comment": null
                }
            ]
        }
        """;

        var p = VisionJsonParser.Parse(raw);
        p.Title.Should().Be("初二地理单元测验");
        p.Questions.Should().HaveCount(1);
        p.Questions[0].Type.Should().Be(QuestionType.Choice);
        p.Questions[0].IsCorrect.Should().BeTrue();
        p.Questions[0].PartialScore.Should().Be(2);
    }

    [Fact]
    public void Strips_markdown_fence()
    {
        const string raw = """
        ```json
        {"title":null,"date":null,"questions":[]}
        ```
        """;

        var p = VisionJsonParser.Parse(raw);
        p.Title.Should().BeNull();
        p.Questions.Should().BeEmpty();
    }

    [Fact]
    public void Unknown_type_falls_back_to_Unknown()
    {
        const string raw = """
        {"title":null,"date":null,"questions":[
          {"number":"2","type":"somethingWeird","stem":"x","standard_answer":null,
           "student_answer":"y","is_correct":false,"partial_score":null,"teacher_comment":null}
        ]}
        """;
        var p = VisionJsonParser.Parse(raw);
        p.Questions[0].Type.Should().Be(QuestionType.Unknown);
    }
}
