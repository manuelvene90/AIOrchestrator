using AIOrchestratorCoreLib.Sessions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Sessions;

/// <summary>
/// ONE TREE, SEVERAL SPELLINGS. `git worktree list --porcelain` prints <c>C:/Users/...</c> with forward
/// slashes, a session writes whatever its shell showed it, and a hand-typed path may carry a trailing
/// separator or another case. The validator asks "is this the same tree" of all of them, so a naive
/// string compare would refuse a real worktree as not-of-repo — or, worse, let two siblings share one
/// because their spellings differed.
/// </summary>
public class WorkingPathComparerTests
{
    [Theory]
    [InlineData(@"C:\Users\x\repo.worktrees\limits", "C:/Users/x/repo.worktrees/limits")]   // git's spelling
    [InlineData(@"C:\Users\x\repo.worktrees\limits\", @"C:\Users\x\repo.worktrees\limits")] // trailing separator
    [InlineData(@"c:\users\x\REPO.worktrees\limits", @"C:\Users\x\repo.worktrees\limits")]  // case — OrdinalIgnoreCase, the GitSnapshot_Reader precedent
    public void TheSameTree_SpelledTwoWays_IsTheSame(string a, string b)
    {
        Assert.True(WorkingPath_Comparer.Are_Same(a, b));
    }

    [Fact]
    public void AParentFolder_IsNotTheSameTree()
    {
        Assert.False(WorkingPath_Comparer.Are_Same(@"C:\Users\x\repo", @"C:\Users\x\repo.worktrees\limits"));
        Assert.False(WorkingPath_Comparer.Are_Same(@"C:\Users\x\repo.worktrees", @"C:\Users\x\repo.worktrees\limits"));
    }

    /// <summary>
    /// AN UNREADABLE PATH MATCHES NOTHING, not even itself. Every caller asks this to decide whether a
    /// tree is TAKEN or LISTED, and a path the OS cannot parse is neither — the request is then refused
    /// on the worktree checks with a reason, rather than an exception taking the executor's tick down.
    /// </summary>
    [Fact]
    public void AnUnparseablePath_IsNotTheSame_AndDoesNotThrow()
    {
        Assert.False(WorkingPath_Comparer.Are_Same("C:\\bad\0path", "C:\\bad\0path"));
        Assert.False(WorkingPath_Comparer.Are_Same("C:\\bad\0path", @"C:\Users\x\repo"));
    }
}
