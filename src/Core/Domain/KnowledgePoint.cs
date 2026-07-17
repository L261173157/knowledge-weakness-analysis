using System;

namespace KnowledgeWeakness.Core.Domain;

public class KnowledgePoint
{
    public int Id { get; set; }
    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Keywords { get; set; } = string.Empty;
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
