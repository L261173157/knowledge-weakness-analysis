using System;
using System.Collections.Generic;

namespace KnowledgeWeakness.Core.Domain;

public enum ImportStatus
{
    Raw = 0,
    Extracted = 1,
    Reviewed = 2,
    Analyzed = 3
}

public class Paper
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public Student? Student { get; set; }

    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }

    public DateOnly Date { get; set; }
    public string Title { get; set; } = string.Empty;

    public string OriginalImagePaths { get; set; } = string.Empty;

    public ImportStatus Status { get; set; } = ImportStatus.Raw;

    public string? Provider { get; set; }
    public string? RawExtractionJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }

    public List<Question> Questions { get; set; } = new();
}
