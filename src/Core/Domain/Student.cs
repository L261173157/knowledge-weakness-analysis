namespace KnowledgeWeakness.Core.Domain;

public class Student
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Grade { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
