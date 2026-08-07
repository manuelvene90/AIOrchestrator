namespace AIOrchestratorCoreLib.Git.GitSnapshot;

public static class GitSnapshot_Factory
{
    public static IGitSnapshot Create(
        string workingTreePath,
        string shortPath,
        bool isRepository,
        string branch,
        int aheadOfUpstream,
        int behindUpstream,
        int dirtyFileCount,
        IReadOnlyList<string> recentCommits)
    {
        return new GitSnapshotModel(workingTreePath, shortPath, isRepository, branch, aheadOfUpstream, behindUpstream, dirtyFileCount, recentCommits);
    }

    public static IGitSnapshot Create_NotARepository(string workingTreePath, string shortPath)
    {
        return Create(workingTreePath, shortPath, false, "", 0, 0, 0, []);
    }
}
