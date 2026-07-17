using KnowledgeWeakness.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeWeakness.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Paper> Papers => Set<Paper>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<StudentAnswer> StudentAnswers => Set<StudentAnswer>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<KnowledgePoint> KnowledgePoints => Set<KnowledgePoint>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Student>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Grade).HasMaxLength(20);
        });

        mb.Entity<Subject>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(50).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.KnowledgeBasePath).HasMaxLength(500);
        });

        mb.Entity<Paper>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.OriginalImagePaths).IsRequired();
            e.Property(x => x.Provider).HasMaxLength(50);
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Questions).WithOne(q => q.Paper!).HasForeignKey(q => q.PaperId).OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<Question>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Number).HasMaxLength(20).IsRequired();
            e.Property(x => x.Stem).IsRequired();
            e.HasOne(x => x.StudentAnswer).WithOne(a => a.Question!).HasForeignKey<StudentAnswer>(a => a.QuestionId).OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<StudentAnswer>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.AnswerText).IsRequired();
        });

        mb.Entity<AppSetting>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(100);
            e.Property(x => x.Value).IsRequired();
        });

        mb.Entity<KnowledgePoint>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Keywords).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.SubjectId, x.Name }).IsUnique();
        });
    }
}
