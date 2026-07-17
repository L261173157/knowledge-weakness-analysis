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
      "options": {"A": "选项A原文", "B": "选项B原文"} | null,
      "standard_answer_option": "A" | "AB" | null,
      "standard_answer_text": string|null,
      "student_answer_option": "A" | "AB" | null,
      "student_answer_text": "学生作答文字原文（手写识别）",
      "teacher_is_correct": true|false|null,
      "ai_is_correct": true|false|null,
      "is_correct": true|false,
      "partial_score": number|null,
      "teacher_comment": string|null
    }
  ]
}
规则：
- 读取题目、标准答案、学生手写答案，以及老师红笔批改痕迹（对勾、叉、半对、扣分、圈画、批注等）。
- teacher_is_correct 仅根据老师红笔批改痕迹判断；无明显批改时填 null，并在 teacher_comment 写"未批改"。
- ai_is_correct 由你根据题目、标准答案和学生答案进行独立判卷；无法判断时填 null。
- is_correct 优先填写 teacher_is_correct；如果老师未批改但 ai_is_correct 可判断，则填写 ai_is_correct；都无法判断时填 false。
- 选择题或带选项的题目，必须把所有可见选项原文写入 options；选项字母写入 *_answer_option，选项对应文字写入 *_answer_text；不要只给 A/B/C/D。
- 如果题干字段 stem 不包含选项，options 仍然必须包含 A/B/C/D 等选项文字。
- 填空题、简答题等没有选项时，*_answer_option 写 null，答案内容写入 *_answer_text。
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
