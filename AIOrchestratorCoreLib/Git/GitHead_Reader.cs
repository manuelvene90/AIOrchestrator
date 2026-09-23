using System.Text.RegularExpressions;

namespace AIOrchestratorCoreLib.Git;

/// <summary>
/// THE BRANCH A WORKING TREE IS ON, READ FROM ITS FILES — NO PROCESS (plan 2026-09-23 Task 10).
///
/// <para>
/// WHY NOT <see cref="GitSnapshot_Reader"/>: that one forks <c>git</c> several times, which is right for a
/// detail window and wrong for the endeavour digest, which is rebuilt on the 2-second mirror tick for every
/// linked orchestration — N siblings × every tick × several forks is a load nobody asked for (the plan's
/// "never spawn git on the tick"). A branch name is one line of one file, so it is read as one.
/// </para>
/// <para>
/// BOTH SHAPES OF <c>.git</c>: a folder in a main checkout, and a FILE (<c>gitdir: &lt;path&gt;</c>) in a
/// linked worktree — the shape every sibling works in. A relative gitdir (git's
/// <c>worktree.useRelativePaths</c>) is resolved against the tree.
/// </para>
/// <para>
/// NULL, NEVER A THROW, for anything unrecognised: a missing tree, no <c>.git</c>, an unreadable HEAD, a ref
/// outside <c>refs/heads</c>. The digest prints <c>branch ?</c> for it — an honest unknown in a file an agent
/// reads, where a guess would be a wrong fact.
/// </para>
/// </summary>
public static partial class GitHead_Reader
{
    const string GIT_ENTRY = ".git";
    const string HEAD_FILE = "HEAD";
    const string GITDIR_PREFIX = "gitdir:";
    const string BRANCH_REF_PREFIX = "ref: refs/heads/";
    const int SHORT_SHA_LENGTH = 7;

    [GeneratedRegex("^[0-9a-f]{40}([0-9a-f]{24})?$")]
    private static partial Regex FullSha_Regex();

    public static string? Read_Branch_OrNull(string workingTreePath)
    {
        try
        {
            var gitDirectory = Find_GitDirectory_OrNull(workingTreePath);

            if (gitDirectory == null)
                return null;

            var head = File.ReadAllText(Path.Combine(gitDirectory, HEAD_FILE)).Trim();

            if (head.StartsWith(BRANCH_REF_PREFIX, StringComparison.Ordinal))
            {
                var branch = head[BRANCH_REF_PREFIX.Length..].Trim();
                return branch.Length == 0 ? null : branch;
            }

            // A detached HEAD holds the commit itself — SHA-1 (40) or SHA-256 (64) — shown short, as git does.
            return FullSha_Regex().IsMatch(head) ? head[..SHORT_SHA_LENGTH] : null;
        }
        catch
        {
            // Unreadable (locked, denied, a folder where the file should be) means unknown, and unknown is
            // what null already says. Nothing here is worth failing the tick for.
            return null;
        }
    }

    static string? Find_GitDirectory_OrNull(string workingTreePath)
    {
        var gitEntry = Path.Combine(workingTreePath, GIT_ENTRY);

        if (Directory.Exists(gitEntry))
            return gitEntry;

        if (!File.Exists(gitEntry))
            return null;

        var pointer = File.ReadAllText(gitEntry).Trim();

        if (!pointer.StartsWith(GITDIR_PREFIX, StringComparison.Ordinal))
            return null;

        var gitDirectory = pointer[GITDIR_PREFIX.Length..].Trim();

        if (gitDirectory.Length == 0)
            return null;

        return Path.IsPathRooted(gitDirectory)
            ? gitDirectory
            : Path.GetFullPath(Path.Combine(workingTreePath, gitDirectory));
    }
}
