using AIOrchestratorCoreLib.Git;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Git;

/// <summary>
/// The git reader runs on every detail-window refresh and every /diff — it must degrade, never
/// throw, on paths that are not repositories (or do not exist at all).
/// </summary>
public class GitSnapshotReaderTests : IDisposable
{
    readonly string _tempFolder;

    public GitSnapshotReaderTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"aiorch-git-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        Directory.Delete(_tempFolder, recursive: true);
    }

    [Fact]
    public void Read_PlainFolder_ReportsNotARepository_WithoutThrowing()
    {
        var snapshot = GitSnapshot_Reader.Read(_tempFolder);

        Assert.False(snapshot.IsRepository);
        Assert.Empty(snapshot.RecentCommits);
        Assert.Equal(0, snapshot.DirtyFileCount);
    }

    [Fact]
    public void Read_MissingPath_ReportsNotARepository_AndStillCarriesAShortPath()
    {
        var missing = Path.Combine(_tempFolder, "nope", "gone");

        var snapshot = GitSnapshot_Reader.Read(missing);

        Assert.False(snapshot.IsRepository);
        Assert.Contains("gone", snapshot.ShortPath);
    }

    [Fact]
    public void Read_RepoAndWorktrees_OnANonRepo_ReturnsTheSingleNonRepoEntry()
    {
        var snapshots = GitSnapshot_Reader.Read_RepoAndWorktrees(_tempFolder);

        var single = Assert.Single(snapshots);
        Assert.False(single.IsRepository);
    }
}
