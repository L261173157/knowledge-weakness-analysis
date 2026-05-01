using KnowledgeWeakness.Core.AI;

namespace KnowledgeWeakness.Infrastructure.AI;

internal static class VisionPromptBuilder
{
    public const string SystemPrompt = """
你是一名负责中国中小学试卷批改数据提取的助手。你会看到一份或多页由手机拍摄的试卷照片，
卷面包含：印刷题干、学生手写答案、教师红笔批改痕迹（对勾/叉/扣分/批注等）。
请精确识别每一道题，严格只输出一段 JSON（不要解释、不要 markdown 代码块）。
JSON Schema：
{
  "title": string|null,
  "date": string|null,
  "questions": [
    {
      "number": "1" | "2.3" | "一.1" 等原始题号,
      "type": "Choice" | "FillBlank" | "ShortAnswer" | "Essay" | "Unknown",
      "stem": "题干全文",
      "standard_answer": string|null,
      "student_answer": "学生作答原文（手写识别）",
      "is_correct": true|false,
      "partial_score": number|null,
      "teacher_comment": string|null
    }
  ]
}
规则：
- 仅根据教师批改痕迹判断 is_correct；无明显批改时置 false 并在 teacher_comment 写"未批改"。
- 手写部分按原字照录，错别字保留；公式/化学式用纯文本表示。
- 数字、单位照录。
- 若整卷图像无法识别，返回 {"title":null,"date":null,"questions":[]}。
""";

    public static string BuildUserText(SubjectContext subject)
    {
        var hint = string.IsNullOrWhiteSpace(subject.ExtractionHints) ? "" : $"\n学科补充提示：{subject.ExtractionHints}";
        return $"""
以下是一份{subject.Grade}{subject.Name}试卷，请按系统提示的 JSON schema 输出全部题目。{hint}
""";
    }
}
