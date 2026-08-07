namespace AIOrchestratorCoreLib.Git.GitSnapshot;

/// <summary>
/// What a working tree ACTUALLY contains — the ground truth behind an agent's claims. Every
/// field degrades gracefully: a non-repo path or a failing git call yields IsRepository = false
/// rather than an exception.
/// </summary>
public interface IGitSnapshot
{
    string WorkingTreePath { get; }

    /// <summary>Last two folders of the path — the owner reads locations that way.</summary>
    string ShortPath { get; }

    bool IsRepository { get; }
    string Branch { get; }

    /// <summary>Commits ahead of / behind the tracked upstream (0 when there is none).</summary>
    int AheadOfUpstream { get; }
    int BehindUpstream { get; }

    /// <summary>Uncommitted paths (staged + unstaged + untracked).</summary>
    int DirtyFileCount { get; }

    /// <summary>Most recent commits, newest first, as "abc1234 subject".</summary>
    IReadOnlyList<string> RecentCommits { get; }
}
