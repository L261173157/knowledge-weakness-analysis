using System;
using System.Collections.Generic;
using KnowledgeWeakness.App.ViewModels;

namespace KnowledgeWeakness.App.Services;

/// <summary>
/// Original paper images referenced by a report. <see cref="ImageFiles"/> holds
/// absolute on-disk paths when collected by the view model; exporters that copy
/// files next to the report (Markdown/JSON) replace them with staged relative
/// file names.
/// </summary>
public record ReportPaperImages(int PaperId, string PaperTitle, IReadOnlyList<string> ImageFiles);

public record AnalysisReportData(
    string StudentName,
    string SubjectName,
    string Summary,
    DateTime GeneratedAt,
    IReadOnlyList<WeaknessPointRow> Points,
    bool IncludeImages = false,
    IReadOnlyList<ReportPaperImages>? PaperImages = null)
{
    /// <summary>Never-null view over the optional <see cref="PaperImages"/>.</summary>
    public IReadOnlyList<ReportPaperImages> PapersWithImages => PaperImages ?? Array.Empty<ReportPaperImages>();
}
