namespace KnowledgeWeakness.Core.Domain;

public class Subject
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? KnowledgeBasePath { get; set; }
}
