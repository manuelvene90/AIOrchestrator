namespace AIOrchestratorCoreLib.Git.GitSnapshot;

internal sealed class GitSnapshotModel(
    string workingTreePath,
    string shortPath,
    bool isRepository,
    string branch,
    int aheadOfUpstream,
    int behindUpstream,
    int dirtyFileCount,
    IReadOnlyList<string> recentCommits) : IGitSnapshot
{
    public string WorkingTreePath { get; } = workingTreePath;
    public string ShortPath { get; } = shortPath;
    public bool IsRepository { get; } = isRepository;
    public string Branch { get; } = branch;
    public int AheadOfUpstream { get; } = aheadOfUpstream;
    public int BehindUpstream { get; } = behindUpstream;
    public int DirtyFileCount { get; } = dirtyFileCount;
    public IReadOnlyList<string> RecentCommits { get; } = recentCommits;
}
