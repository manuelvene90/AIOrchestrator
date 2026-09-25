using AIOrchestratorCoreLib.Git;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Git;

/// <summary>
/// THE BRANCH NAME, READ FROM FILES (plan 2026-09-23 Task 10). The endeavour digest is rebuilt on the
/// 2-second tick for every linked orchestration, so its branch comes from <c>.git/HEAD</c> and never from
/// a <c>git</c> process — every case here is a hand-made tree, and none of them has git behind it.
/// </summary>
public sealed class GitHeadReaderTests : IDisposable
{
    readonly string _tree = Path.Combine(Path.GetTempPath(), $"aiorch-githead-{Guid.NewGuid():N}");

    public GitHeadReaderTests()
    {
        Directory.CreateDirectory(_tree);
    }

    public void Dispose()
    {
        TempTree.Delete_BestEffort(_tree);
    }

    [Fact]
    public void Read_GitFolderWithASymbolicRef_ReturnsTheBranch()
    {
        Directory.CreateDirectory(Path.Combine(_tree, ".git"));
        File.WriteAllText(Path.Combine(_tree, ".git", "HEAD"), "ref: refs/heads/feat/x\n");

        Assert.Equal("feat/x", GitHead_Reader.Read_Branch_OrNull(_tree));
    }

    /// <summary>A LINKED WORKTREE — the shape a sibling works in: <c>.git</c> is a file naming the real git dir.</summary>
    [Fact]
    public void Read_GitFilePointingAtADetachedHead_ReturnsTheShortSha()
    {
        var gitDir = Path.Combine(_tree, "main", ".git", "worktrees", "limits");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), "0123456789abcdef0123456789abcdef01234567\n");

        var worktree = Path.Combine(_tree, "limits");
        Directory.CreateDirectory(worktree);
        File.WriteAllText(Path.Combine(worktree, ".git"), $"gitdir: {gitDir}\n");

        Assert.Equal("0123456", GitHead_Reader.Read_Branch_OrNull(worktree));
    }

    [Fact]
    public void Read_GitFileWithARelativeGitDir_ResolvesItAgainstTheTree()
    {
        var gitDir = Path.Combine(_tree, "main", ".git", "worktrees", "limits");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/sib/limits\n");

        var worktree = Path.Combine(_tree, "limits");
        Directory.CreateDirectory(worktree);
        File.WriteAllText(Path.Combine(worktree, ".git"), "gitdir: ../main/.git/worktrees/limits\n");

        Assert.Equal("sib/limits", GitHead_Reader.Read_Branch_OrNull(worktree));
    }

    [Fact]
    public void Read_NoGit_ReturnsNull()
    {
        Assert.Null(GitHead_Reader.Read_Branch_OrNull(_tree));
    }

    [Fact]
    public void Read_MissingTree_ReturnsNull()
    {
        Assert.Null(GitHead_Reader.Read_Branch_OrNull(Path.Combine(_tree, "gone")));
    }

    /// <summary>A HEAD that cannot be read as a file — here a folder in its place, which fails the same on every OS.</summary>
    [Fact]
    public void Read_UnreadableHead_ReturnsNull()
    {
        Directory.CreateDirectory(Path.Combine(_tree, ".git", "HEAD"));

        Assert.Null(GitHead_Reader.Read_Branch_OrNull(_tree));
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage\n")]
    [InlineData("ref: refs/tags/v1\n")]
    [InlineData("0123abc\n")]
    public void Read_AHeadOfNoKnownShape_ReturnsNull(string head)
    {
        Directory.CreateDirectory(Path.Combine(_tree, ".git"));
        File.WriteAllText(Path.Combine(_tree, ".git", "HEAD"), head);

        Assert.Null(GitHead_Reader.Read_Branch_OrNull(_tree));
    }
}
