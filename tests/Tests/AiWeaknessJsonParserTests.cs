using FluentAssertions;
using KnowledgeWeakness.Infrastructure.AI;

namespace KnowledgeWeakness.Tests;

public class AiWeaknessJsonParserTests
{
    [Fact]
    public void Parses_review_advice_from_model_json()
    {
        const string raw = """
        ```json
        {
          "summary": "气候成因掌握不稳定",
          "points": [
            {
              "knowledge_point": "气候与降水",
              "severity": "High",
              "weak_reason": "不能从海陆位置解释水汽来源。",
              "question_numbers": ["1", "2"],
              "review_advice": "先复习影响降水的因素，再用新疆例题复述原因。",
              "practice_direction": "练习区域气候成因分析题。"
            }
          ]
        }
        ```
        """;

        var result = AiWeaknessJsonParser.Parse(raw);

        result.Summary.Should().Be("气候成因掌握不稳定");
        result.Points.Should().ContainSingle();
        result.Points[0].KnowledgePoint.Should().Be("气候与降水");
        result.Points[0].Severity.Should().Be("High");
        result.Points[0].ReviewAdvice.Should().Contain("新疆");
    }
}
