using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace KnowledgeWeakness.App.Services;

public static class AnalysisReportExporter
{
    static AnalysisReportExporter()
    {
        // QuestPDF requires a license type to be set before generating any
        // document. Community is free for qualifying projects; set it once at
        // type init so the per-call builder doesn't repeat the assignment.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static async Task<string> ExportAsync(AnalysisReportData report, string directory, string format)
    {
        Directory.CreateDirectory(directory);
        var safeStudent = SanitizeFileName(string.IsNullOrWhiteSpace(report.StudentName) ? "全部" : report.StudentName);
        var safeSubject = SanitizeFileName(string.IsNullOrWhiteSpace(report.SubjectName) ? "全部" : report.SubjectName);
        var stamp = report.GeneratedAt.ToString("yyyyMMdd_HHmmss");
        var fmt = (format ?? "Markdown").Trim();
        var ext = fmt.ToUpperInvariant() switch
        {
            "CSV" => "csv",
            "JSON" => "json",
            "PDF" => "pdf",
            _ => "md"
        };
        var path = Path.Combine(directory, $"薄弱分析_{safeStudent}_{safeSubject}_{stamp}.{ext}");

        switch (ext)
        {
            case "csv":
                await File.WriteAllTextAsync(path, BuildCsv(report), new UTF8Encoding(true));
                break;
            case "json":
                // Markdown and JSON reference images by relative path, so stage
                // copies next to the report before writing.
                var jsonImages = await StageImageFilesAsync(report, path);
                await File.WriteAllTextAsync(path, BuildJson(report, jsonImages), new UTF8Encoding(false));
                break;
            case "pdf":
                // QuestPDF's GeneratePdf is synchronous and CPU-bound (font/layout
                // work). Running it on the UI thread freezes the window, so push
                // it to the thread pool. The license is configured once at first
                // use below.
                await Task.Run(() => BuildPdf(report).GeneratePdf(path));
                break;
            default:
                var markdownImages = await StageImageFilesAsync(report, path);
                await File.WriteAllTextAsync(path, BuildMarkdown(report, markdownImages), new UTF8Encoding(false));
                break;
        }
        return path;
    }

    /// <summary>
    /// Copies the report's original paper images into a
    /// <c>&lt;report-name&gt;_files</c> folder next to the exported file and
    /// returns records whose <see cref="ReportPaperImages.ImageFiles"/> are
    /// paths relative to the report (folder/name). Copies run on the thread
    /// pool so large batches never block the UI caller. CSV stays a pure
    /// table and PDF embeds images inline, so neither calls this.
    /// </summary>
    private static async Task<List<ReportPaperImages>> StageImageFilesAsync(AnalysisReportData report, string reportPath)
    {
        if (!report.IncludeImages || report.PapersWithImages.Count == 0) return [];

        var folder = Path.Combine(
            Path.GetDirectoryName(reportPath)!,
            Path.GetFileNameWithoutExtension(reportPath) + "_files");
        var folderName = Path.GetFileName(folder);
        Directory.CreateDirectory(folder);

        var staged = new List<ReportPaperImages>();
        foreach (var paper in report.PapersWithImages)
        {
            var relativeNames = new List<string>();
            for (var i = 0; i < paper.ImageFiles.Count; i++)
            {
                var source = paper.ImageFiles[i];
                // Prefix with paper id and page index so legacy imports whose
                // files only differ by directory cannot collide.
                var fileName = $"p{paper.PaperId}_{i + 1}_{Path.GetFileName(source)}";
                await Task.Run(() => File.Copy(source, Path.Combine(folder, fileName), overwrite: true));
                relativeNames.Add($"{folderName}/{fileName}");
            }
            staged.Add(paper with { ImageFiles = relativeNames });
        }
        return staged;
    }

    private static string BuildMarkdown(AnalysisReportData r, IReadOnlyList<ReportPaperImages> stagedImages)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# 薄弱分析报告");
        sb.AppendLine();
        sb.AppendLine($"- 学生：{r.StudentName}");
        sb.AppendLine($"- 学科：{r.SubjectName}");
        sb.AppendLine($"- 生成时间：{r.GeneratedAt:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine($"> {r.Summary}");
        sb.AppendLine();
        foreach (var p in r.Points)
        {
            sb.AppendLine($"## {p.KnowledgePoint}（{p.Severity}，错题 {p.WrongCount}，占比 {p.WrongRateText}）");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(p.WeakReason)) sb.AppendLine($"**薄弱原因**：{p.WeakReason}");
            if (!string.IsNullOrWhiteSpace(p.ReviewAdvice)) sb.AppendLine($"**复习建议**：{p.ReviewAdvice}");
            if (!string.IsNullOrWhiteSpace(p.PracticeDirection)) sb.AppendLine($"**练习方向**：{p.PracticeDirection}");
            if (p.Examples.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("**关联错题**：");
                foreach (var e in p.Examples)
                {
                    sb.AppendLine($"- 题号 {e.QuestionNumber}（{e.PaperTitle}）：{Truncate(e.Stem, 120)}");
                    if (!string.IsNullOrWhiteSpace(e.StudentAnswer)) sb.AppendLine($"  - 学生答案：{e.StudentAnswer}");
                    if (!string.IsNullOrWhiteSpace(e.StandardAnswer)) sb.AppendLine($"  - 标准答案：{e.StandardAnswer}");
                    if (!string.IsNullOrWhiteSpace(e.TeacherComment)) sb.AppendLine($"  - 批注：{e.TeacherComment}");
                }
            }
            sb.AppendLine();
        }

        if (stagedImages.Count > 0)
        {
            sb.AppendLine("## 附录：原卷图片");
            sb.AppendLine();
            foreach (var paper in stagedImages)
            {
                sb.AppendLine($"### {paper.PaperTitle}");
                sb.AppendLine();
                foreach (var image in paper.ImageFiles)
                {
                    sb.AppendLine($"![{paper.PaperTitle} 原卷]({EscapeMarkdownLink(image)})");
                }
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }

    // Escape each path segment separately so slashes survive (Uri.EscapeDataString
    // would turn them into %2F) while spaces/Chinese punctuation stay link-safe.
    private static string EscapeMarkdownLink(string relativePath) =>
        string.Join("/", relativePath.Split('/').Select(Uri.EscapeDataString));

    private static string BuildCsv(AnalysisReportData r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("知识点,严重度,错题数,占比,薄弱原因,复习建议,练习方向,关联题号");
        foreach (var p in r.Points)
        {
            sb.Append(Csv(p.KnowledgePoint)).Append(',')
              .Append(Csv(p.Severity)).Append(',')
              .Append(p.WrongCount).Append(',')
              .Append(Csv(p.WrongRateText)).Append(',')
              .Append(Csv(p.WeakReason)).Append(',')
              .Append(Csv(p.ReviewAdvice)).Append(',')
              .Append(Csv(p.PracticeDirection)).Append(',')
              .Append(Csv(p.QuestionNumbersText));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string BuildJson(AnalysisReportData r, IReadOnlyList<ReportPaperImages> stagedImages)
    {
        var payload = new
        {
            r.StudentName,
            r.SubjectName,
            r.Summary,
            GeneratedAt = r.GeneratedAt.ToString("o"),
            Points = r.Points.Select(p => new
            {
                p.KnowledgePoint,
                p.Severity,
                p.WrongCount,
                p.TotalCount,
                p.WrongRate,
                p.WeakReason,
                p.ReviewAdvice,
                p.PracticeDirection,
                QuestionNumbers = p.QuestionNumbersText,
                Examples = p.Examples.Select(e => new
                {
                    e.PaperId,
                    e.PaperTitle,
                    e.QuestionNumber,
                    e.Stem,
                    e.StudentAnswer,
                    e.StandardAnswer,
                    e.TeacherComment
                })
            }),
            // Empty when the "包含原图" export option is off; otherwise paths
            // relative to this report file (staged copies in *_files/).
            PaperImages = stagedImages.Select(p => new
            {
                p.PaperId,
                p.PaperTitle,
                Images = p.ImageFiles
            })
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    private static IDocument BuildPdf(AnalysisReportData r)
    {
        return Document.Create(c =>
        {
            c.Page(p =>
            {
                p.Margin(36);
                p.Size(PageSizes.A4);
                p.DefaultTextStyle(t => t.FontFamily("Microsoft YaHei").FontSize(11));

                p.Header().Column(col =>
                {
                    col.Item().Text("薄弱分析报告").FontSize(20).SemiBold();
                    col.Item().PaddingTop(4).Text($"学生：{r.StudentName}    学科：{r.SubjectName}    生成时间：{r.GeneratedAt:yyyy-MM-dd HH:mm}")
                        .FontSize(10).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(6).Text(r.Summary).Italic();
                });

                p.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(12);
                    foreach (var pt in r.Points)
                    {
                        col.Item().Background(Colors.Grey.Lighten4).Padding(10).Column(inner =>
                        {
                            inner.Item().Text($"{pt.KnowledgePoint}（{pt.Severity}，错题 {pt.WrongCount}，占比 {pt.WrongRateText}）")
                                .SemiBold().FontSize(13);
                            if (!string.IsNullOrWhiteSpace(pt.WeakReason))
                                inner.Item().PaddingTop(4).Text($"薄弱原因：{pt.WeakReason}");
                            if (!string.IsNullOrWhiteSpace(pt.ReviewAdvice))
                                inner.Item().Text($"复习建议：{pt.ReviewAdvice}");
                            if (!string.IsNullOrWhiteSpace(pt.PracticeDirection))
                                inner.Item().Text($"练习方向：{pt.PracticeDirection}");
                            if (pt.Examples.Count > 0)
                            {
                                inner.Item().PaddingTop(6).Text("关联错题：").SemiBold();
                                foreach (var e in pt.Examples)
                                {
                                    inner.Item().PaddingLeft(8).Text(t =>
                                    {
                                        t.Span($"· 题号 {e.QuestionNumber}（{e.PaperTitle}）：").SemiBold();
                                        t.Span(Truncate(e.Stem, 140));
                                    });
                                    if (!string.IsNullOrWhiteSpace(e.StudentAnswer))
                                        inner.Item().PaddingLeft(16).Text($"学生：{e.StudentAnswer}").FontColor(Colors.Red.Darken1);
                                    if (!string.IsNullOrWhiteSpace(e.StandardAnswer))
                                        inner.Item().PaddingLeft(16).Text($"标准：{e.StandardAnswer}").FontColor(Colors.Green.Darken1);
                                    if (!string.IsNullOrWhiteSpace(e.TeacherComment))
                                        inner.Item().PaddingLeft(16).Text($"批注：{e.TeacherComment}").FontColor(Colors.Grey.Darken2);
                                }
                            }
                        });
                    }

                    if (r.IncludeImages && r.PapersWithImages.Count > 0)
                    {
                        col.Item().PaddingTop(6).Text("附录：原卷图片").FontSize(15).SemiBold();
                        foreach (var paper in r.PapersWithImages)
                        {
                            col.Item().PaddingTop(8)
                                .Text($"{paper.PaperTitle}（{paper.ImageFiles.Count} 张）")
                                .SemiBold().FontSize(12);
                            foreach (var file in paper.ImageFiles)
                            {
                                col.Item().PaddingTop(4).Element(container =>
                                {
                                    try
                                    {
                                        container.Image(file).FitWidth();
                                    }
                                    catch (Exception)
                                    {
                                        // One unreadable or unsupported image must
                                        // not abort the whole report.
                                        container.Text($"图片加载失败：{Path.GetFileName(file)}")
                                            .FontColor(Colors.Grey.Darken1).FontSize(9);
                                    }
                                });
                            }
                        }
                    }
                });

                p.Footer().AlignCenter().Text(t =>
                {
                    t.Span("知识薄弱分析  ·  第 ").FontSize(9).FontColor(Colors.Grey.Darken1);
                    t.CurrentPageNumber().FontSize(9).FontColor(Colors.Grey.Darken1);
                    t.Span(" / ").FontSize(9).FontColor(Colors.Grey.Darken1);
                    t.TotalPages().FontSize(9).FontColor(Colors.Grey.Darken1);
                    t.Span(" 页").FontSize(9).FontColor(Colors.Grey.Darken1);
                });
            });
        });
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var needsQuote = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        var escaped = value.Replace("\"", "\"\"");
        return needsQuote ? $"\"{escaped}\"" : escaped;
    }

    private static string Truncate(string text, int max)
        => string.IsNullOrEmpty(text) ? "" : (text.Length <= max ? text : text.Substring(0, max) + "…");

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(invalid.Contains(c) ? '_' : c);
        return sb.ToString();
    }
}
