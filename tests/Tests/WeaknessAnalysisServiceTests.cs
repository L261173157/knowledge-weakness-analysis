using FluentAssertions;
using KnowledgeWeakness.Core.Analysis;
using KnowledgeWeakness.Core.Domain;

namespace KnowledgeWeakness.Tests;

public class WeaknessAnalysisServiceTests
{
    [Fact]
    public void Groups_wrong_questions_by_matched_knowledge_point()
    {
        var papers = new[]
        {
            new Paper
            {
                Id = 1,
                Title = "初二地理小考",
                Questions =
                [
                    Question("1", "我国地势总体特征是什么？", true),
                    Question("2", "新疆气候干旱的主要原因。", false, "水汽难以到达")
                ]
            },
            new Paper
            {
                Id = 2,
                Title = "初二地理周练",
                Questions =
                [
                    Question("3", "三江源地区被称为中华水塔的原因。", false, "河流发源地")
                ]
            }
        };

        var result = WeaknessAnalysisService.Analyze(papers);

        result.Points.Should().HaveCount(2);
        result.Points[0].KnowledgePoint.Should().Be("气候与降水");
        result.Points[0].WrongCount.Should().Be(1);
        result.Points[0].TotalCount.Should().Be(1);
        result.Points[0].Examples[0].PaperTitle.Should().Be("初二地理小考");
        result.Points[1].KnowledgePoint.Should().Be("河流与水文");
    }

    [Fact]
    public void Treats_partial_score_as_weak_even_when_correct_flag_is_true()
    {
        var papers = new[]
        {
            new Paper
            {
                Id = 1,
                Title = "测试卷",
                Questions =
                [
                    Question("1", "长江三角洲工业基地发展的有利条件。", true, "少答交通", 1)
                ]
            }
        };

        var result = WeaknessAnalysisService.Analyze(papers);

        result.TotalWeakQuestions.Should().Be(1);
        result.Points.Single().KnowledgePoint.Should().Be("区域发展与工业");
        result.Points.Single().Examples.Single().TeacherComment.Should().Be("少答交通");
    }

    private static Question Question(
        string number,
        string stem,
        bool isCorrect,
        string? teacherComment = null,
        double? partialScore = null)
    {
        return new Question
        {
            Number = number,
            Stem = stem,
            StudentAnswer = new StudentAnswer
            {
                AnswerText = "",
                IsCorrect = isCorrect,
                PartialScore = partialScore,
                TeacherComment = teacherComment
            }
        };
    }
}
