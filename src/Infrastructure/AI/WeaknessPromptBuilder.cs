using System.Text;
using System.Text.Json;
using KnowledgeWeakness.Core.Analysis;

namespace KnowledgeWeakness.Infrastructure.AI;

internal static class WeaknessPromptBuilder
{
    public const string SystemPrompt = """
你是一名中小学学情分析老师。你会收到已经人工确认过的错题/扣分题、教师批注和可选学科知识库。
请根据知识库优先归纳学生薄弱知识点，并生成可执行的复习建议。
严格只输出 JSON，不要 markdown 代码块，不要解释。
JSON Schema:
{
  "summary": "整体薄弱情况总结",
  "points": [
    {
      "knowledge_point": "知识点名称，优先使用知识库中的名称",
      "severity": "High|Medium|Low",
      "weak_reason": "薄弱原因，结合学生答案和教师批注",
      "question_numbers": ["题号"],
      "review_advice": "给学生的复习建议",
      "practice_direction": "建议练习方向"
    }
  ]
}
规则：
- 未出现在候选题中的题号不要编造。
- 如果多个错题属于同一知识点，请合并成一个 points 项。
- review_advice 要具体到学生该复习什么、如何检查是否掌握。
- practice_direction 要指出适合补练的题型或情境。
""";

    public static string BuildUserText(AiWeaknessAnalysisRequest request)
    {
        var payload = new
        {
            student = new
            {
                name = request.StudentName,
                grade = request.StudentGrade,
                subject = request.SubjectName
            },
            knowledge_base = string.IsNullOrWhiteSpace(request.KnowledgeBase)
                ? "未提供知识库，请基于题干、答案和批注分析。"
                : request.KnowledgeBase,
            weak_questions = request.Candidates.Select(x => new
            {
                paper = x.PaperTitle,
                date = x.PaperDate.ToString("yyyy-MM-dd"),
                number = x.QuestionNumber,
                type = x.QuestionType,
                stem = x.Stem,
                standard_answer = x.StandardAnswer,
                student_answer = x.StudentAnswer,
                is_correct = x.IsCorrect,
                partial_score = x.PartialScore,
                teacher_comment = x.TeacherComment
            })
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        var sb = new StringBuilder();
        sb.AppendLine("请分析以下人工确认后的薄弱题：");
        sb.AppendLine(json);
        return sb.ToString();
    }
}
