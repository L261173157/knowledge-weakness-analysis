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
        p.Questions[0].Options.Should().BeEmpty();
        p.Questions[0].StandardAnswerOption.Should().Be("");
        p.Questions[0].StandardAnswerText.Should().Be("西高东低");
        p.Questions[0].StudentAnswerText.Should().Be("西高东低");
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

    [Fact]
    public void Parses_split_option_and_text_answer_fields()
    {
        const string raw = """
        {"title":null,"date":null,"questions":[
          {"number":"1","type":"Choice","stem":"x",
           "standard_answer_option":"B","standard_answer_text":"季风气候显著",
           "student_answer_option":"A","student_answer_text":"地形平坦",
           "is_correct":true,"partial_score":null,"teacher_comment":null}
        ]}
        """;

        var p = VisionJsonParser.Parse(raw);

        p.Questions[0].StandardAnswerOption.Should().Be("B");
        p.Questions[0].StandardAnswerText.Should().Be("季风气候显著");
        p.Questions[0].StudentAnswerOption.Should().Be("A");
        p.Questions[0].StudentAnswerText.Should().Be("地形平坦");
    }

    [Fact]
    public void Fills_missing_choice_answer_text_from_stem_options()
    {
        const string raw = """
        {"title":null,"date":null,"questions":[
          {"number":"1","type":"Choice","stem":"下列正确的是 A. 全年高温多雨 B. 夏季高温多雨 C. 冬季寒冷干燥",
           "standard_answer_option":"B","standard_answer_text":null,
           "student_answer_option":"A","student_answer_text":"",
           "is_correct":false,"partial_score":null,"teacher_comment":null}
        ]}
        """;

        var p = VisionJsonParser.Parse(raw);

        p.Questions[0].StandardAnswerText.Should().Be("夏季高温多雨");
        p.Questions[0].StudentAnswerText.Should().Be("全年高温多雨");
    }

    [Fact]
    public void Fills_missing_student_answer_text_even_when_type_is_unknown()
    {
        const string raw = """
        {"title":null,"date":null,"questions":[
          {"number":"1","type":"Unknown","stem":"下列正确的是 A. 全年高温多雨 B. 夏季高温多雨 C. 冬季寒冷干燥",
           "standard_answer_option":"B","standard_answer_text":null,
           "student_answer_option":"A","student_answer_text":"",
           "is_correct":false,"partial_score":null,"teacher_comment":null}
        ]}
        """;

        var p = VisionJsonParser.Parse(raw);

        p.Questions[0].StandardAnswerText.Should().Be("夏季高温多雨");
        p.Questions[0].StudentAnswerText.Should().Be("全年高温多雨");
    }

    [Fact]
    public void Fills_answer_text_from_structured_options_when_stem_has_no_option_text()
    {
        const string raw = """
        {"title":null,"date":null,"questions":[
          {"number":"1","type":"Choice","stem":"青藏地区河谷农业发展的主要原因是（ ）",
           "options":{"A":"海拔较低，热量较充足","B":"降水丰富，水田广布","C":"土壤肥沃，黑土广布","D":"纬度较低，全年高温"},
           "standard_answer_option":"A","standard_answer_text":null,
           "student_answer_option":"B","student_answer_text":"",
           "is_correct":false,"partial_score":null,"teacher_comment":null}
        ]}
        """;

        var p = VisionJsonParser.Parse(raw);

        p.Questions[0].Options["A"].Should().Be("海拔较低，热量较充足");
        p.Questions[0].StandardAnswerText.Should().Be("海拔较低，热量较充足");
        p.Questions[0].StudentAnswerText.Should().Be("降水丰富，水田广布");
    }

    [Fact]
    public void Keeps_legacy_student_answer_when_option_text_cannot_be_found()
    {
        const string raw = """
        {"title":null,"date":null,"questions":[
          {"number":"1","type":"Choice","stem":"题干没有完整选项",
           "standard_answer_option":"B","standard_answer_text":null,
           "student_answer":"A",
           "is_correct":false,"partial_score":null,"teacher_comment":null}
        ]}
        """;

        var p = VisionJsonParser.Parse(raw);

        p.Questions[0].StudentAnswerText.Should().Be("A");
    }
    [Fact]
    public void Parses_teacher_and_ai_judgments()
    {
        const string raw = """
        {"title":null,"date":null,"questions":[
          {"number":"1","type":"ShortAnswer","stem":"x",
           "standard_answer_text":"answer",
           "student_answer_text":"student",
           "teacher_is_correct":false,
           "ai_is_correct":true,
           "is_correct":false,
           "partial_score":null,
           "teacher_comment":"red mark"}
        ]}
        """;

        var p = VisionJsonParser.Parse(raw);

        p.Questions[0].TeacherIsCorrect.Should().BeFalse();
        p.Questions[0].AiIsCorrect.Should().BeTrue();
        p.Questions[0].IsCorrect.Should().BeFalse();
    }
}
