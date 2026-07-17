using System.IO;
using FluentAssertions;
using KnowledgeWeakness.Core.Backup;

namespace KnowledgeWeakness.Tests;

public class BackupEntryResolverTests
{
    private static readonly string Data = Path.Combine(Path.GetTempPath(), "kw_data");
    private static readonly string Papers = Path.Combine(Data, "papers");

    [Theory]
    [InlineData("app.db")]
    [InlineData("app.db-wal")]
    [InlineData("app.db-shm")]
    public void Resolves_database_entries_into_data_directory(string entry)
    {
        var result = BackupEntryResolver.Resolve(entry, Data, Papers);
        result.Should().NotBeNull();
        result!.Should().Be(Path.Combine(Data, entry));
    }

    [Theory]
    [InlineData("papers/20260101_a.jpg")]
    [InlineData("papers/sub/dir/b.png")]
    public void Resolves_paper_entries_into_paper_directory(string entry)
    {
        var result = BackupEntryResolver.Resolve(entry, Data, Papers);
        result.Should().NotBeNull();
        result!.Should().StartWith(Path.GetFullPath(Papers));
    }

    [Theory]
    [InlineData("papers/../app.db")]
    [InlineData("papers/../../Windows/system32/cmd.exe")]
    [InlineData("papers/sub/../../escape.txt")]
    [InlineData("../escape.txt")]
    [InlineData("..\\escape.txt")]
    public void Rejects_path_traversal(string entry)
    {
        BackupEntryResolver.Resolve(entry, Data, Papers).Should().BeNull();
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("\\Windows\\foo")]
    [InlineData("C:\\Windows\\foo")]
    [InlineData("c:/x")]
    public void Rejects_rooted_or_drive_letter_paths(string entry)
    {
        BackupEntryResolver.Resolve(entry, Data, Papers).Should().BeNull();
    }

    [Theory]
    [InlineData("startup/run.bat")]
    [InlineData("random.txt")]
    [InlineData("app.db.txt")]
    [InlineData("papers")]   // exactly the prefix with no relative part
    [InlineData("papers/")]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_unknown_or_empty_entries(string entry)
    {
        BackupEntryResolver.Resolve(entry, Data, Papers).Should().BeNull();
    }

    [Fact]
    public void Backslash_segments_are_normalized_and_still_constrained()
    {
        var ok = BackupEntryResolver.Resolve("papers\\sub\\img.jpg", Data, Papers);
        ok.Should().NotBeNull();
        ok!.Should().StartWith(Path.GetFullPath(Papers));

        var bad = BackupEntryResolver.Resolve("papers\\..\\app.db", Data, Papers);
        bad.Should().BeNull();
    }
}
