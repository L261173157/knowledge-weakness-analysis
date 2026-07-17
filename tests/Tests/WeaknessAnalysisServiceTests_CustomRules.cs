using FluentAssertions;
using KnowledgeWeakness.Core.Analysis;
using KnowledgeWeakness.Core.Domain;

namespace KnowledgeWeakness.Tests;

public class WeaknessAnalysisServiceTests_CustomRules
{
    [Fact]
    public void Analyze_with_db_rules_uses_custom_classification_over_default()
    {
        var papers = new[]
        {
            new Paper
            {
                Id = 1,
                Title = "测试卷",
                Date = DateOnly.FromDateTime(DateTime.Today),
                Questions = new()
                {
                    new Question
                    {
                        Number = "1",
                        Stem = "下列哪种动物是哺乳动物？",
                        StudentAnswer = new StudentAnswer { AnswerText = "鱼", IsCorrect = false }
                    }
                }
            }
        };

        var customRules = new[]
        {
            new KnowledgePoint { Id = 1, SubjectId = 1, Name = "生物分类", Keywords = "哺乳 鱼 动物" }
        };

        var result = WeaknessAnalysisService.Analyze(papers, customRules);

        result.Points.Should().ContainSingle();
        result.Points[0].KnowledgePoint.Should().Be("生物分类");
        result.Points[0].WrongCount.Should().Be(1);
    }

    [Fact]
    public void Analyze_falls_back_to_default_rules_when_db_rules_empty()
    {
        var papers = new[]
        {
            new Paper
            {
                Id = 1,
                Title = "地理卷",
                Date = DateOnly.FromDateTime(DateTime.Today),
                Questions = new()
                {
                    new Question
                    {
                        Number = "1",
                        Stem = "我国季风气候的特点",
                        StudentAnswer = new StudentAnswer { AnswerText = "", IsCorrect = false }
                    }
                }
            }
        };

        var result = WeaknessAnalysisService.Analyze(papers, Array.Empty<KnowledgePoint>());

        result.Points.Should().ContainSingle();
        result.Points[0].KnowledgePoint.Should().Be("气候与降水");
    }

    [Theory]
    [InlineData("一 二 三")]
    [InlineData("一,二,三")]
    [InlineData("一|二|三")]
    [InlineData("一、二、三")]
    public void Custom_keyword_separators_all_parsed(string keywords)
    {
        var papers = new[]
        {
            new Paper
            {
                Id = 1,
                Date = DateOnly.FromDateTime(DateTime.Today),
                Questions = new()
                {
                    new Question
                    {
                        Number = "1",
                        Stem = "题干包含 二 这个字",
                        StudentAnswer = new StudentAnswer { AnswerText = "", IsCorrect = false }
                    }
                }
            }
        };

        var rules = new[]
        {
            new KnowledgePoint { Id = 1, SubjectId = 1, Name = "数字", Keywords = keywords }
        };

        var result = WeaknessAnalysisService.Analyze(papers, rules);
        result.Points.Should().ContainSingle().Which.KnowledgePoint.Should().Be("数字");
    }
}
