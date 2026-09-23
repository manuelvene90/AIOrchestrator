namespace AIOrchestratorCoreLib.Sessions;

/// <summary>
/// IS THIS THE SAME TREE — asked of a sibling's requested worktree against git's list (is it OF this
/// repo?), against every open sibling's working path (is it TAKEN?), and of parked request files.
///
/// <para>
/// THE SPELLINGS DIFFER BY SOURCE, which is why a string compare is wrong in both directions. `git
/// worktree list --porcelain` prints <c>C:/Users/...</c> with forward slashes (measured on this machine
/// 2026-09-23), a session writes whatever its shell showed it, and a hand-typed path may end in a
/// separator. Compared as strings, a real worktree would be refused as not-of-repo, and — the worse
/// half — two siblings could be handed one tree because their paths were spelled differently.
/// </para>
/// <para>
/// ORDINALIGNORECASE, because <c>GitSnapshot_Reader.Read_RepoAndWorktrees</c> already compares worktree
/// paths that way; two rules for one comparison would disagree about the same pair of trees. Both
/// separators are folded to this OS's one, so git's forward slashes and a Windows-typed backslash meet
/// on every OS the daemon runs on.
/// </para>
/// <para>
/// WHAT IT DOES NOT DO: it never touches the disk, so it does not resolve symlinks or Windows 8.3 short
/// names (<c>GIANPI~1</c>). A short-named path is therefore NOT the same as git's long spelling of it.
/// The refusal that results names git's own list, so the session can re-drop with that spelling.
/// </para>
/// </summary>
public static class WorkingPath_Comparer
{
    /// <summary>
    /// False — never an exception — for a path the OS cannot parse, including against itself: callers
    /// ask this to decide whether a tree is LISTED or TAKEN, and an unreadable path is neither.
    /// </summary>
    public static bool Are_Same(string a, string b)
    {
        try
        {
            return string.Equals(Normalise(a), Normalise(b), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    static string Normalise(string path)
    {
        var folded = path.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar);

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(folded));
    }
}
