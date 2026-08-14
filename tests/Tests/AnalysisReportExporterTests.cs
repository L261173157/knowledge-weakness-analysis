using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using KnowledgeWeakness.App.Services;
using KnowledgeWeakness.App.ViewModels;
using SkiaSharp;
using Xunit;

namespace KnowledgeWeakness.Tests;

/// <summary>
/// Regression contract for the “包含原图” export option: with the option on,
/// Markdown/JSON stage copies of the original paper images in a *_files
/// folder next to the report and reference them relatively, PDF embeds them
/// inline, and CSV stays a pure table; with the option off nothing is staged.
/// </summary>
public class AnalysisReportExporterTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "kw-export-tests-" + Guid.NewGuid().ToString("N"));

    public AnalysisReportExporterTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Writes a tiny valid JPEG so the PDF path can actually decode it.</summary>
    private string WriteTestImage(string name)
    {
        var path = Path.Combine(_root, name);
        using var bitmap = new SKBitmap(8, 8);
        bitmap.Erase(SKColors.SteelBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        using var stream = File.OpenWrite(path);
        data.SaveTo(stream);
        return path;
    }

    private static AnalysisReportData BuildReport(
        bool includeImages,
        DateTime generatedAt,
        params ReportPaperImages[] papers)
    {
        var point = new WeaknessPointRow
        {
            KnowledgePoint = "气候与降水",
            Severity = "高",
            WeakReason = "原因",
            WrongCount = 2,
            TotalCount = 5,
            WrongRate = 0.4
        };
        point.Examples.Add(new WeakQuestionExampleRow
        {
            PaperId = 1,
            PaperTitle = "卷子A",
            QuestionNumber = "3",
            Stem = "题干"
        });
        return new AnalysisReportData(
            "张三", "地理", "总结", generatedAt, [point],
            includeImages, papers.Length == 0 ? null : papers);
    }

    [Fact]
    public async Task Markdown_with_include_images_stages_files_and_links_them()
    {
        var image = WriteTestImage("img-1.jpg");
        var report = BuildReport(true, new DateTime(2026, 8, 14, 10, 0, 0),
            new ReportPaperImages(1, "卷子A", [image]));

        var path = await AnalysisReportExporter.ExportAsync(report, _root, "Markdown");

        var markdown = await File.ReadAllTextAsync(path);
        markdown.Should().Contain("## 附录：原卷图片")
            .And.Contain("### 卷子A")
            .And.Contain("_files/p1_1_img-1.jpg");
        var filesDir = Path.Combine(_root, Path.GetFileNameWithoutExtension(path) + "_files");
        Directory.Exists(filesDir).Should().BeTrue();
        Directory.GetFiles(filesDir).Select(Path.GetFileName)
            .Should().Contain(name => name!.StartsWith("p1_1_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Markdown_without_include_images_stages_nothing()
    {
        var report = BuildReport(false, new DateTime(2026, 8, 14, 10, 0, 0));

        var path = await AnalysisReportExporter.ExportAsync(report, _root, "Markdown");

        (await File.ReadAllTextAsync(path)).Should().NotContain("附录：原卷图片");
        Directory.GetDirectories(_root).Should().BeEmpty("no *_files folder may be created");
    }

    [Fact]
    public async Task Json_with_include_images_lists_staged_relative_paths()
    {
        var image = WriteTestImage("img-1.jpg");
        var report = BuildReport(true, new DateTime(2026, 8, 14, 10, 0, 0),
            new ReportPaperImages(1, "卷子A", [image]));

        var path = await AnalysisReportExporter.ExportAsync(report, _root, "JSON");

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var paperImages = doc.RootElement.GetProperty("PaperImages");
        paperImages.GetArrayLength().Should().Be(1);
        paperImages[0].GetProperty("PaperTitle").GetString().Should().Be("卷子A");
        paperImages[0].GetProperty("Images").EnumerateArray().Single().GetString()
            .Should().EndWith("_files/p1_1_img-1.jpg");
    }

    [Fact]
    public async Task Pdf_with_include_images_grows_the_document()
    {
        var image = WriteTestImage("img-1.jpg");
        var withImages = await AnalysisReportExporter.ExportAsync(
            BuildReport(true, new DateTime(2026, 8, 14, 10, 0, 0),
                new ReportPaperImages(1, "卷子A", [image])),
            _root, "PDF");
        var withoutImages = await AnalysisReportExporter.ExportAsync(
            BuildReport(false, new DateTime(2026, 8, 14, 11, 0, 0)),
            _root, "PDF");

        var bytes = await File.ReadAllBytesAsync(withImages);
        bytes[0..4].Should().Equal((byte)'%', (byte)'P', (byte)'D', (byte)'F');
        bytes.Length.Should().BeGreaterThan(
            (int)new FileInfo(withoutImages).Length,
            "the embedded original images must grow the PDF appendix");
    }

    [Fact]
    public async Task Csv_never_stages_images_even_when_option_is_on()
    {
        var image = WriteTestImage("img-1.jpg");
        var report = BuildReport(true, new DateTime(2026, 8, 14, 10, 0, 0),
            new ReportPaperImages(1, "卷子A", [image]));

        var path = await AnalysisReportExporter.ExportAsync(report, _root, "CSV");

        Path.GetExtension(path).Should().Be(".csv");
        Directory.GetDirectories(_root).Should().BeEmpty("CSV stays a pure table format");
    }
}
