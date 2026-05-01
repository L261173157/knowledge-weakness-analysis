using FluentAssertions;
using KnowledgeWeakness.Core.Domain;
using KnowledgeWeakness.Infrastructure.Persistence;
using KnowledgeWeakness.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeWeakness.Tests;

public class PaperRepositoryTests
{
    [Fact]
    public async Task ListWithQuestionsAsync_includes_questions_and_student_answers()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var factory = new TestDbContextFactory(options);
        await SeedAsync(factory);

        var repo = new PaperRepository(factory);

        var papers = await repo.ListWithQuestionsAsync();

        papers.Should().HaveCount(1);
        papers[0].Questions.Should().ContainSingle();
        papers[0].Questions[0].StudentAnswer.Should().NotBeNull();
        papers[0].Questions[0].StudentAnswer!.TeacherComment.Should().Be("概念不清");
    }

    private static async Task SeedAsync(TestDbContextFactory factory)
    {
        await using var db = factory.CreateDbContext();
        var student = new Student { Name = "李明", Grade = "初二" };
        var subject = new Subject { Code = "geo-g8", Name = "地理" };
        db.Students.Add(student);
        db.Subjects.Add(subject);
        db.Papers.Add(new Paper
        {
            Student = student,
            Subject = subject,
            Title = "地理小测",
            Date = new DateOnly(2026, 4, 10),
            OriginalImagePaths = "",
            Questions =
            [
                new Question
                {
                    Number = "1",
                    Stem = "新疆气候干旱的原因",
                    StudentAnswer = new StudentAnswer
                    {
                        AnswerText = "远离海洋",
                        IsCorrect = false,
                        TeacherComment = "概念不清"
                    }
                }
            ]
        });
        await db.SaveChangesAsync();
    }

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            return new AppDbContext(options);
        }
    }
}
