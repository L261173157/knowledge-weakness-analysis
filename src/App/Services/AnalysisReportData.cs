using System;
using System.Collections.Generic;
using KnowledgeWeakness.App.ViewModels;

namespace KnowledgeWeakness.App.Services;

public record AnalysisReportData(
    string StudentName,
    string SubjectName,
    string Summary,
    DateTime GeneratedAt,
    IReadOnlyList<WeaknessPointRow> Points);
