using FluentAssertions;
using KnowledgeWeakness.Core.Analysis;
using KnowledgeWeakness.Core.Domain;

namespace KnowledgeWeakness.Tests;

public class WeaknessCandidateSelectorTests
{
    [Fact]
    public void Excludes_papers_that_were_not_manually_confirmed()
    {
        var paper = Paper(
            reviewedAt: null,
            Question("1", "气候题", isCorrect: false, teacherComment: "错误"));

        var candidates = WeaknessCandidateSelector.Select([paper]);

        candidates.Should().BeEmpty();
    }

    [Fact]
    public void Excludes_unreviewed_questions()
    {
        var paper = Paper(
            reviewedAt: DateTime.UtcNow,
            Question("1", "气候题", isCorrect: false, teacherComment: "未批改"));

        var candidates = WeaknessCandidateSelector.Select([paper]);

        candidates.Should().BeEmpty();
    }

    [Fact]
    public void Includes_incorrect_questions_even_when_teacher_comment_is_empty()
    {
        var paper = Paper(
            reviewedAt: DateTime.UtcNow,
            Question("1", "青藏地区河谷农业发展的原因。", isCorrect: false, teacherComment: null));

        var candidates = WeaknessCandidateSelector.Select([paper]);

        candidates.Should().ContainSingle();
        candidates.Single().QuestionNumber.Should().Be("1");
    }

    [Fact]
    public void Includes_partial_score_questions_as_weakness_candidates()
    {
        var paper = Paper(
            reviewedAt: DateTime.UtcNow,
            Question("1", "长江三角洲工业基地发展的有利条件。", isCorrect: true, teacherComment: "少答交通", partialScore: 1));

        var candidates = WeaknessCandidateSelector.Select([paper]);

        candidates.Should().ContainSingle();
        candidates.Single().QuestionNumber.Should().Be("1");
        candidates.Single().TeacherComment.Should().Be("少答交通");
    }

    [Fact]
    public void Matcher_handles_model_question_number_variants()
    {
        var paper = Paper(
            reviewedAt: DateTime.UtcNow,
            Question("2", "青藏地区河谷农业发展的原因。", isCorrect: false, teacherComment: null),
            Question("4", "在青藏地区可以品尝到的当地特产。", isCorrect: false, teacherComment: null));
        var candidates = WeaknessCandidateSelector.Select([paper]);

        var matched = WeaknessCandidateMatcher.MatchByQuestionNumbers(candidates, ["第2题、Q4"]);

        matched.Select(x => x.QuestionNumber).Should().Equal("2", "4");
    }

    private static Paper Paper(DateTime? reviewedAt, params Question[] questions)
    {
        return new Paper
        {
            Id = 7,
            Title = "地理小测",
            Date = new DateOnly(2026, 4, 10),
            Status = reviewedAt is null ? ImportStatus.Extracted : ImportStatus.Reviewed,
            ReviewedAt = reviewedAt,
            Student = new Student { Id = 1, Name = "李明", Grade = "初二" },
            Subject = new Subject { Id = 2, Code = "geo-g8", Name = "地理" },
            Questions = questions.ToList()
        };
    }

    private static Question Question(
        string number,
        string stem,
        bool isCorrect,
        string? teacherComment,
        double? partialScore = null)
    {
        return new Question
        {
            Number = number,
            Stem = stem,
            StandardAnswer = "标准答案",
            StudentAnswer = new StudentAnswer
            {
                AnswerText = "学生答案",
                IsCorrect = isCorrect,
                PartialScore = partialScore,
                TeacherComment = teacherComment
            }
        };
    }
}
