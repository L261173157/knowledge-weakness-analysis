using FluentAssertions;
using KnowledgeWeakness.Infrastructure.Analysis;

namespace KnowledgeWeakness.Tests;

public class KnowledgeBaseReaderTests
{
    [Fact]
    public async Task Reads_relative_knowledge_base_path_from_configured_root()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "geo.md"), "# 地理知识库\n气候与降水");

        var reader = new KnowledgeBaseReader(root);

        var content = await reader.ReadAsync("geo.md");

        content.Should().Contain("气候与降水");
    }
}
