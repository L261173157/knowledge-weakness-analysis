using KnowledgeWeakness.Core.Domain;

namespace KnowledgeWeakness.Core.Analysis;

public record WeakQuestionExample(
    int PaperId,
    string PaperTitle,
    string QuestionNumber,
    string Stem,
    string? StudentAnswer,
    string? StandardAnswer,
    string? TeacherComment);

public record WeaknessPointSummary(
    string KnowledgePoint,
    int WrongCount,
    int TotalCount,
    double WrongRate,
    IReadOnlyList<WeakQuestionExample> Examples);

public record WeaknessAnalysisResult(
    int TotalPapers,
    int TotalQuestions,
    int TotalWeakQuestions,
    IReadOnlyList<WeaknessPointSummary> Points);

public static class WeaknessAnalysisService
{
    private static readonly KnowledgeRule[] Rules =
    [
        new("气候与降水", ["气候", "降水", "干旱", "湿润", "水汽", "季风", "气温", "温带", "热带", "寒带"]),
        new("河流与水文", ["河流", "长江", "黄河", "水文", "水源", "水塔", "三江源", "湖泊", "径流", "汛期"]),
        new("区域发展与工业", ["工业", "农业", "交通", "城市", "经济", "产业", "基地", "长江三角洲", "珠江三角洲"]),
        new("地形与地势", ["地形", "地势", "山脉", "高原", "盆地", "平原", "丘陵", "海拔", "等高线"]),
        new("地图与位置", ["地图", "经度", "纬度", "经纬", "方向", "位置", "范围", "省区", "行政区"]),
        new("人口与民族", ["人口", "民族", "分布", "密度", "聚落"])
    ];

    public static WeaknessAnalysisResult Analyze(IEnumerable<Paper> papers)
    {
        var paperList = papers.ToList();
        var questionRows = paperList
            .SelectMany(p => p.Questions.Select(q => new QuestionRow(p, q, Classify(q))))
            .ToList();

        var weakRows = questionRows.Where(x => IsWeak(x.Question)).ToList();
        var points = weakRows
            .GroupBy(x => x.KnowledgePoint)
            .Select(g =>
            {
                var total = questionRows.Count(x => x.KnowledgePoint == g.Key);
                var examples = g
                    .OrderByDescending(x => x.Paper.Date)
                    .ThenBy(x => x.Question.Number)
                    .Take(5)
                    .Select(x => new WeakQuestionExample(
                        x.Paper.Id,
                        x.Paper.Title,
                        x.Question.Number,
                        x.Question.Stem,
                        x.Question.StudentAnswer?.AnswerText,
                        x.Question.StandardAnswer,
                        x.Question.StudentAnswer?.TeacherComment))
                    .ToList();

                return new WeaknessPointSummary(
                    g.Key,
                    g.Count(),
                    total,
                    total == 0 ? 0 : (double)g.Count() / total,
                    examples);
            })
            .OrderByDescending(x => x.WrongCount)
            .ThenByDescending(x => x.WrongRate)
            .ThenBy(x => x.KnowledgePoint)
            .ToList();

        return new WeaknessAnalysisResult(
            paperList.Count,
            questionRows.Count,
            weakRows.Count,
            points);
    }

    private static bool IsWeak(Question question)
    {
        var answer = question.StudentAnswer;
        if (answer is null) return false;
        return !answer.IsCorrect || answer.PartialScore is not null;
    }

    private static string Classify(Question question)
    {
        var text = string.Join(" ", question.Stem, question.StandardAnswer, question.StudentAnswer?.TeacherComment);
        return Rules
            .Select(rule => new
            {
                rule.Name,
                Score = rule.Keywords.Count(k => text.Contains(k, StringComparison.OrdinalIgnoreCase))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Name)
            .FirstOrDefault() ?? "未归类";
    }

    private sealed record KnowledgeRule(string Name, string[] Keywords);
    private sealed record QuestionRow(Paper Paper, Question Question, string KnowledgePoint);
}
