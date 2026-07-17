using FluentAssertions;
using KnowledgeWeakness.Core.Analysis;

namespace KnowledgeWeakness.Tests;

public class AnswerTextHelperTests
{
    [Theory]
    [InlineData("A. 季风气候显著", "A", "季风气候显著")]
    [InlineData("A、C", "AC", "")]
    [InlineData("水汽难以到达", "", "水汽难以到达")]
    public void Split_separates_choice_option_from_answer_text(string input, string option, string text)
    {
        AnswerTextHelper.Split(input).Should().Be((option, text));
    }

    [Fact]
    public void Judge_uses_option_when_both_sides_have_options()
    {
        AnswerTextHelper.Judge("A", "季风气候显著", "a", "")
            .Should().BeTrue();

        AnswerTextHelper.Judge("B", "季风气候显著", "A", "季风气候显著")
            .Should().BeFalse();
    }

    [Fact]
    public void Judge_uses_text_when_no_options_exist()
    {
        AnswerTextHelper.Judge(null, "水汽 难以到达。", null, "水汽难以到达")
            .Should().BeTrue();
    }

    [Fact]
    public void ExtractOptionText_gets_selected_text_from_stem()
    {
        const string stem = "下列气候特征正确的是 A. 全年高温多雨 B. 夏季高温多雨 C. 冬季寒冷干燥 D. 全年少雨";

        AnswerTextHelper.ExtractOptionText(stem, "B")
            .Should().Be("夏季高温多雨");
    }

    [Fact]
    public void ExtractOptionText_combines_multiple_selected_option_texts()
    {
        const string stem = "请选择正确项：（A）地形平坦 （B）交通便利 （C）全年严寒";

        AnswerTextHelper.ExtractOptionText(stem, "AB")
            .Should().Be("地形平坦；交通便利");
    }
}
